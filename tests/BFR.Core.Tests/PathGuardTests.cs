using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BFR.Core;
using Xunit;

namespace BFR.Core.Tests;

public class PathGuardTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative\\file.txt")]
    [InlineData("C:\\a\\b\0c.txt")]
    [InlineData("C:\\a\\b\nc.txt")]
    public void TryNormalize_RejectsBadPaths(string? path)
    {
        Assert.False(PathGuard.TryNormalize(path, out _));
    }

    [Fact]
    public void TryNormalize_RejectsOverlongPath()
    {
        Assert.False(PathGuard.TryNormalize(@"C:\" + new string('a', PathGuard.MaxPathLength), out _));
    }

    [Fact]
    public void TryNormalize_CollapsesDotDot()
    {
        Assert.True(PathGuard.TryNormalize(@"C:\a\b\..\c.txt", out var n));
        Assert.Equal(@"C:\a\c.txt", n);
    }

    [Fact]
    public void IsInside_BlocksSiblingWithSamePrefix()
    {
        Assert.True(PathGuard.IsInside(@"C:\Temp\BatchFileRenamer\x.txt", @"C:\Temp\BatchFileRenamer"));
        Assert.False(PathGuard.IsInside(@"C:\Temp\BatchFileRenamerEvil\x.txt", @"C:\Temp\BatchFileRenamer"));
        Assert.False(PathGuard.IsInside(@"C:\Temp\BatchFileRenamer\..\secret.txt", @"C:\Temp\BatchFileRenamer"));
    }

    [Fact]
    public void Sanitize_DropsBadPaths_DedupesAndCaps()
    {
        var payload = new IpcPayload();
        payload.Paths.Add(@"C:\ok\a.txt");
        payload.Paths.Add(@"C:\OK\A.TXT");           // trùng, khác hoa/thường
        payload.Paths.Add("relative.txt");           // không tuyệt đối
        payload.Paths.Add("C:\\x\0y");               // ký tự điều khiển
        var clean = PathGuard.SanitizeIpcPayload(payload);
        Assert.Single(clean.Paths);

        var many = new IpcPayload();
        for (var i = 0; i < PathGuard.MaxPaths + 500; i++) many.Paths.Add($@"C:\d\f{i}.txt");
        Assert.Equal(PathGuard.MaxPaths, PathGuard.SanitizeIpcPayload(many).Paths.Count);
    }

    [Fact]
    public void Sanitize_KeepsFileListOnlyInsideBfrTemp()
    {
        var inside = Path.Combine(PathGuard.BfrTempRoot, "shell", "shell-1.txt");
        Assert.Equal(inside, PathGuard.SanitizeIpcPayload(new IpcPayload { FileListPath = inside }).FileListPath);

        Assert.Null(PathGuard.SanitizeIpcPayload(new IpcPayload { FileListPath = @"C:\Users\me\Documents\important.docx" }).FileListPath);
    }

    [Fact]
    public void Sanitize_RejectsOversizeOrControlStrings()
    {
        var clean = PathGuard.SanitizeIpcPayload(new IpcPayload
        {
            PresetName = new string('p', PathGuard.MaxPresetNameLength + 1),
            QuickPreset = "num\u0007ber"
        });
        Assert.Null(clean.PresetName);
        Assert.Null(clean.QuickPreset);
        Assert.NotNull(PathGuard.SanitizeIpcPayload(null));
    }

    [Fact]
    public void ReadAndDelete_DoesNotDeleteFilesOutsideBfrTemp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "BFR_PathGuard_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var victim = Path.Combine(dir, "important.txt");
            File.WriteAllText(victim, @"C:\a.txt");
            FileListReader.ReadAndDelete(victim);
            Assert.True(File.Exists(victim));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ReadAndDelete_DeletesOwnTempListAfterReading()
    {
        var dir = Path.Combine(PathGuard.BfrTempRoot, "test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var list = Path.Combine(dir, "list.txt");
            File.WriteAllText(list, @"C:\a.txt");
            Assert.Single(FileListReader.ReadAndDelete(list));
            Assert.False(File.Exists(list));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}

public class SingleInstanceHardeningTests
{
    [Fact]
    public async Task Server_SanitizesIncomingPayload()
    {
        var pipe = "BFR_Test_Pipe_" + Guid.NewGuid().ToString("N");
        IpcPayload? received = null;
        var tcs = new TaskCompletionSource<bool>();
        await using var server = new SingleInstanceServer(p => { received = p; tcs.TrySetResult(true); }, pipe);
        server.Start();

        var sent = new IpcPayload { FileListPath = @"C:\Users\me\victim.docx" };
        sent.Paths.Add("relative.txt");
        sent.Paths.Add(@"C:\ok\a.txt");
        Assert.True(await SingleInstanceClient.TrySendAsync(sent, pipe, timeoutMs: 3000));
        Assert.True(await Task.WhenAny(tcs.Task, Task.Delay(3000)) == tcs.Task);

        Assert.Equal(new[] { @"C:\ok\a.txt" }, received!.Paths.ToArray());
        Assert.Null(received.FileListPath);
    }

    [Fact]
    public async Task ReadLineBounded_ReturnsNullWhenTooLong()
    {
        using var tooLong = new StringReader(new string('x', 100) + "\n");
        Assert.Null(await SingleInstanceServer.ReadLineBoundedAsync(tooLong, 50, CancellationToken.None));

        using var ok = new StringReader("hello\r\nrest");
        Assert.Equal("hello", await SingleInstanceServer.ReadLineBoundedAsync(ok, 50, CancellationToken.None));
    }

    [Fact]
    public async Task Server_StaysAliveAfterSilentClient()
    {
        var pipe = "BFR_Test_Pipe_" + Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<bool>();
        await using var server = new SingleInstanceServer(_ => tcs.TrySetResult(true), pipe);
        server.Start();

        // Client kết nối nhưng không gửi gì: server phải bỏ kết nối sau thời gian chờ rồi phục vụ client kế tiếp.
        using var silent = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await silent.ConnectAsync(3000);

        var payload = new IpcPayload();
        payload.Paths.Add(@"C:\ok\a.txt");
        var ok = await SingleInstanceClient.TrySendAsync(payload, pipe, timeoutMs: SingleInstanceServer.ReadTimeoutMs + 4000);
        Assert.True(ok);
        Assert.True(await Task.WhenAny(tcs.Task, Task.Delay(3000)) == tcs.Task);
    }
}

public class ToolPayloadTests
{
    [Theory]
    [InlineData("organize")]
    [InlineData("duplicates")]
    [InlineData("checksum")]
    public void KnownToolKeys_SurviveSanitizing(string key)
    {
        var args = StartupArgs.Parse(new[] { "--tool", key, @"C:\data\a.txt" });

        var clean = PathGuard.SanitizeIpcPayload(args.Payload);

        Assert.Equal(key, clean.Tool);
        Assert.Single(clean.Paths);
    }

    [Theory]
    [InlineData("rm -rf")]
    [InlineData("ORGANIZE")]
    [InlineData("..\\evil")]
    [InlineData("")]
    public void UnknownToolKeys_AreDropped(string key)
    {
        var payload = new IpcPayload { Tool = key };

        Assert.Null(PathGuard.SanitizeIpcPayload(payload).Tool);
    }

    [Fact]
    public void ToolKeysMatchTheCatalogue() =>
        Assert.Equal(KnownTools.Keys.OrderBy(k => k), new[] { "analyze", "archive", "checksum", "duplicates", "folders", "images", "metadata", "organize", "workflow" });
}
