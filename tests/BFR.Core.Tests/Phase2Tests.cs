namespace BFR.Core.Tests;

using BFR.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Xunit;

public class FileListReaderTests
{
    [Fact]
    public void MissingOrEmptyFile_ReturnsEmpty()
    {
        var fake = new FakeFileSystem();
        Assert.Empty(FileListReader.Read("", fake));
        Assert.Empty(FileListReader.Read("missing.txt", fake));
    }

    [Fact]
    public void ParsesQuotesAndComments_Correctly()
    {
        var fake = new FakeFileSystem();
        var content = """
            # Ghi chú đầu tệp
            "C:\My Documents\file1.txt"
            C:\Photos\image.jpg
            
               # Một ghi chú khác
               "D:\Music\song with spaces.mp3"   
            """;
        fake.WriteAllText(@"C:\temp\files.txt", content);

        var list = FileListReader.Read(@"C:\temp\files.txt", fake);
        Assert.Equal(3, list.Count);
        Assert.Equal(@"C:\My Documents\file1.txt", list[0]);
        Assert.Equal(@"C:\Photos\image.jpg", list[1]);
        Assert.Equal(@"D:\Music\song with spaces.mp3", list[2]);
    }

    [Fact]
    public void LargeBatch_5000Files_QA12_PassesWithoutTruncation()
    {
        var fake = new FakeFileSystem();
        var generated = Enumerable.Range(1, 5000)
            .Select(i => $@"C:\Data\BatchFolder\Document_{i:D5}.dat")
            .ToList();

        var listFile = @"C:\Data\filelist.txt";
        FileListReader.Write(listFile, generated, fake);

        var readBack = FileListReader.Read(listFile, fake);
        Assert.Equal(5000, readBack.Count);
        Assert.Equal(generated[0], readBack[0]);
        Assert.Equal(generated[2499], readBack[2499]);
        Assert.Equal(generated[4999], readBack[4999]);
    }

    [Fact]
    public void ReadAndDelete_RemovesRealTemporaryFile()
    {
        Directory.CreateDirectory(Path.Combine(PathGuard.BfrTempRoot, "shell"));
        var listFile = Path.Combine(PathGuard.BfrTempRoot, "shell", $"shell-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(listFile, new[] { @"C:\Data\one.txt", @"C:\Data\two.txt" });

        var readBack = FileListReader.ReadAndDelete(listFile);

        Assert.Equal(2, readBack.Count);
        Assert.False(File.Exists(listFile));
    }
}

public class SingleInstanceLockTests
{
    [Fact]
    public void PrimaryLock_IsAcquired_AndSecondaryIsRejected()
    {
        var mutexName = @"Local\BFR_Test_Mutex_" + Guid.NewGuid().ToString("N");
        using var primary = new SingleInstanceLock(mutexName);
        Assert.True(primary.IsPrimary);

        // Mutex là reentrant trong cùng thread, nên kiểm tra "instance thứ hai"
        // trên một thread khác để mô phỏng tiến trình thứ hai.
        bool? secondaryIsPrimary = null;
        var thread = new Thread(() =>
        {
            using var secondary = new SingleInstanceLock(mutexName);
            secondaryIsPrimary = secondary.IsPrimary;
        });
        thread.Start();
        thread.Join();

        Assert.False(secondaryIsPrimary);
    }

    [Fact]
    public void AbandonedMutex_IsAcquiredByNextInstance()
    {
        var mutexName = @"Local\BFR_Abandoned_Test_" + Guid.NewGuid().ToString("N");

        // Giữ thêm một handle để kernel object không bị hủy khi owner đóng handle.
        using var owner = new Mutex(true, mutexName);
        using var keeper = new Mutex(false, mutexName);

        owner.Dispose(); // owner "crash" mà không ReleaseMutex

        using var next = new SingleInstanceLock(mutexName);
        Assert.True(next.IsPrimary);
    }
}

public class SingleInstanceIpcTests
{
    [Fact]
    public async Task Ipc_SendsAndReceivesPayload_OverNamedPipe()
    {
        var pipeName = "BFR_Test_Pipe_" + Guid.NewGuid().ToString("N");
        IpcPayload? received = null;
        var tcs = new TaskCompletionSource<bool>();

        await using var server = new SingleInstanceServer(payload =>
        {
            received = payload;
            tcs.TrySetResult(true);
        }, pipeName);

        server.Start();

        var toSend = new IpcPayload
        {
            Paths = new List<string> { @"C:\Folder\file1.txt", @"C:\Folder\file2.txt" },
            PresetName = "Ảnh theo ngày chụp"
        };

        var success = await SingleInstanceClient.TrySendAsync(toSend, pipeName, timeoutMs: 3000);
        Assert.True(success);

        var receivedInTime = await Task.WhenAny(tcs.Task, Task.Delay(3000)) == tcs.Task;
        Assert.True(receivedInTime);
        Assert.NotNull(received);
        Assert.Equal(2, received!.Paths.Count);
        Assert.Equal(@"C:\Folder\file1.txt", received.Paths[0]);
        Assert.Equal("Ảnh theo ngày chụp", received.PresetName);
    }

    [Fact]
    public async Task Ipc_ReturnsFalse_WhenServerNotRunning()
    {
        var nonexistentPipe = "BFR_Nonexistent_Pipe_" + Guid.NewGuid().ToString("N");
        var payload = new IpcPayload { Paths = new List<string> { "test.txt" } };

        var result = await SingleInstanceClient.TrySendAsync(payload, nonexistentPipe, timeoutMs: 300);
        Assert.False(result);
    }
}

public class ShellIntegrationTests
{
    [Fact]
    public void GenerateRegContent_Fallback_ContainsDocumentMultiSelectAndCommand()
    {
        var appPath = @"C:\Program Files\BatchFileRenamer\BatchFileRenamer.exe";
        var missingComServer = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.exe");

        var reg = ShellIntegration.GenerateRegContent(appPath, "Đổi tên hàng loạt với BFR", comServerPath: missingComServer);

        Assert.Contains(@"[HKEY_CURRENT_USER\Software\Classes\*\shell\BatchFileRenamer]", reg);
        Assert.Contains(@"""MultiSelectModel""=""Document""", reg);
        Assert.Contains(@"[HKEY_CURRENT_USER\Software\Classes\Directory\shell\BatchFileRenamer]", reg);
        Assert.Contains(@"[HKEY_CURRENT_USER\Software\Classes\Directory\Background\shell\BatchFileRenamer]", reg);
        Assert.Contains(@"\""C:\\Program Files\\BatchFileRenamer\\BatchFileRenamer.exe\""", reg);
        Assert.Contains(@"\command]", reg);
        Assert.DoesNotContain("ExplorerCommandHandler", reg);
    }

    [Fact]
    public void GenerateRegContent_WithComServer_RegistersLocalServerAndExplorerCommandHandler()
    {
        var appPath = @"C:\Program Files\BatchFileRenamer\BatchFileRenamer.exe";
        var comServer = Path.Combine(Path.GetTempPath(), $"bfr-{Guid.NewGuid():N}.exe");
        File.WriteAllText(comServer, string.Empty);

        try
        {
            var reg = ShellIntegration.GenerateRegContent(appPath, "BFR", comServerPath: comServer);

            Assert.Contains($@"CLSID\{{{ShellIntegration.ExplorerCommandClsid}}}\LocalServer32", reg);
            Assert.Contains($"\"ExplorerCommandHandler\"=\"{{{ShellIntegration.ExplorerCommandClsid}}}\"", reg);
            Assert.Contains(@"""MultiSelectModel""=""Player""", reg);
            Assert.DoesNotContain(@"\InprocServer32]", reg);
            Assert.Contains(ShellIntegration.ComServerArguments, reg);
            Assert.DoesNotContain(@"\command]", reg);
        }
        finally
        {
            File.Delete(comServer);
        }
    }

    [Fact]
    public void ResolveComServerPath_ReturnsFileNextToApplication()
    {
        var appPath = @"C:\Program Files\BatchFileRenamer\BatchFileRenamer.exe";
        var expected = @"C:\Program Files\BatchFileRenamer\" + ShellIntegration.ComServerFileName;

        Assert.Equal(expected, ShellIntegration.ResolveComServerPath(appPath));
    }
}

public class PackagingAndManifestTests
{
    private static readonly XNamespace NsUap3 = "http://schemas.microsoft.com/appx/manifest/uap/windows10/3";
    private static readonly XNamespace NsDesktop = "http://schemas.microsoft.com/appx/manifest/desktop/windows10";
    private static readonly XNamespace NsDesktop4 = "http://schemas.microsoft.com/appx/manifest/desktop/windows10/4";
    private static readonly XNamespace NsDesktop5 = "http://schemas.microsoft.com/appx/manifest/desktop/windows10/5";
    private static readonly XNamespace NsDesktop10 = "http://schemas.microsoft.com/appx/manifest/desktop/windows10/10";
    private static readonly XNamespace NsCom = "http://schemas.microsoft.com/appx/manifest/com/windows10";

    [Fact]
    public void AppManifest_ContainsLongPathAware_AndAsInvoker()
    {
        var manifestPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BFR.App", "app.manifest"));
        Assert.True(File.Exists(manifestPath), $"Không tìm thấy app.manifest tại: {manifestPath}");

        var content = File.ReadAllText(manifestPath);
        Assert.Contains("<longPathAware xmlns=\"http://schemas.microsoft.com/SMI/2016/WindowsSettings\">true</longPathAware>", content);
        Assert.Contains("<requestedExecutionLevel level=\"asInvoker\" uiAccess=\"false\" />", content);
        Assert.Contains("<dpiAwareness xmlns=\"http://schemas.microsoft.com/SMI/2016/WindowsSettings\">PerMonitorV2</dpiAwareness>", content);
    }

    [Fact]
    public void CliManifest_ContainsLongPathAware_AndAsInvoker()
    {
        var manifestPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BFR.Cli", "app.manifest"));
        Assert.True(File.Exists(manifestPath), $"Không tìm thấy app.manifest tại: {manifestPath}");

        var content = File.ReadAllText(manifestPath);
        Assert.Contains("<longPathAware xmlns=\"http://schemas.microsoft.com/SMI/2016/WindowsSettings\">true</longPathAware>", content);
        Assert.Contains("<requestedExecutionLevel level=\"asInvoker\" uiAccess=\"false\" />", content);
    }

    [Fact]
    public void PackageAppxManifest_ContainsComServerAndModernContextMenus()
    {
        var appxPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "packaging", "msix", "AppxManifest.xml"));
        Assert.True(File.Exists(appxPath), $"Không tìm thấy AppxManifest.xml tại: {appxPath}");

        var doc = XDocument.Load(appxPath);
        var content = File.ReadAllText(appxPath);

        Assert.Contains("runFullTrust", content);
        Assert.Contains("windows.fileExplorerContextMenus", content);

        var comServer = doc.Descendants(NsCom + "ExeServer").Single();
        Assert.Equal(ShellIntegration.ComServerFileName, (string?)comServer.Attribute("Executable"));
        Assert.Equal(ShellIntegration.ComServerArguments, (string?)comServer.Attribute("Arguments"));

        var comClass = doc.Descendants(NsCom + "Class").Single();
        Assert.Equal(ShellIntegration.ExplorerCommandClsid, (string?)comClass.Attribute("Id"));
        Assert.Null(comClass.Attribute("Path"));
        Assert.Null(comClass.Attribute("ThreadingModel"));

        var modernTypes = doc.Descendants(NsDesktop5 + "ItemType")
            .Select(e => (string?)e.Attribute("Type"))
            .ToList();
        Assert.Contains("*", modernTypes);
        Assert.Contains("Directory", modernTypes);
        Assert.DoesNotContain(@"Directory\Background", modernTypes);

        var backgroundTypes = doc.Descendants(NsDesktop10 + "ItemType")
            .Select(e => (string?)e.Attribute("Type"))
            .ToList();
        Assert.Contains(@"Directory\Background", backgroundTypes);

        var verbClsids = doc.Descendants()
            .Where(e => e.Name.LocalName == "Verb")
            .Select(e => (string?)e.Attribute("Clsid"))
            .ToList();
        Assert.NotEmpty(verbClsids);
        Assert.All(verbClsids, clsid => Assert.Equal(ShellIntegration.ExplorerCommandClsid, clsid));

        // Element cũ desktop4:Item không đúng schema; phải dùng ItemType.
        Assert.Empty(doc.Descendants(NsDesktop4 + "Item"));
    }

    [Fact]
    public void PackageAppxManifest_ContainsAppExecutionAliasWithDesktopNamespace()
    {
        var appxPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "packaging", "msix", "AppxManifest.xml"));
        var doc = XDocument.Load(appxPath);

        var aliasExtension = doc.Descendants(NsUap3 + "Extension")
            .Single(e => (string?)e.Attribute("Category") == "windows.appExecutionAlias");
        Assert.Equal("bfr.exe", (string?)aliasExtension.Attribute("Executable"));

        var alias = doc.Descendants(NsDesktop + "ExecutionAlias").Single();
        Assert.Equal("bfr.exe", (string?)alias.Attribute("Alias"));
    }
}
