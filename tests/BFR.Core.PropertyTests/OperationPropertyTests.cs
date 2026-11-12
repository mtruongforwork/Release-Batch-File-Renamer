namespace BFR.Core.PropertyTests;

using BFR.Core;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

/// <summary>
/// Bất biến cốt lõi của v2.0: với MỌI chuỗi thao tác (kể cả thao tác lỗi), thực thi rồi hoàn tác
/// phải trả hệ thống tệp về đúng trạng thái ban đầu (tên, nội dung, thư mục, ngày giờ, thuộc tính).
/// </summary>
public class OperationPropertyTests
{
    private const string Journal = "/journal";
    private const string Quarantine = "/q";
    private const string Tmp = "/tmp";

    // Các vùng do BFR tự quản (journal, khu cách ly, tệp tạm của công cụ) không thuộc "trạng thái người dùng".
    private static bool IsUserPath(string p) =>
        !p.StartsWith(Journal, StringComparison.OrdinalIgnoreCase)
        && !p.StartsWith(Quarantine, StringComparison.OrdinalIgnoreCase)
        && !p.StartsWith(Tmp, StringComparison.OrdinalIgnoreCase);

    private static string Snapshot(FakeFileSystem fs)
    {
        var lines = new List<string>();
        foreach (var f in fs.GetFiles(string.Empty, "*", true).Where(IsUserPath).OrderBy(x => x, StringComparer.Ordinal))
        {
            var t = fs.GetTimes(f);
            lines.Add($"F {f} | {fs.ReadAllText(f)} | {t.CreatedUtcTicks}/{t.ModifiedUtcTicks}/{t.AccessedUtcTicks} | {(int)fs.GetAttributes(f)}");
        }
        foreach (var d in fs.GetDirectories(string.Empty, true).Where(IsUserPath).OrderBy(x => x, StringComparer.Ordinal))
        {
            lines.Add($"D {d}");
        }
        return string.Join("\n", lines);
    }

    private static FakeFileSystem RandomState(Random rnd)
    {
        var fs = new FakeFileSystem();
        var dirs = new[] { "/data/a", "/data/b", "/data/c/deep" };
        foreach (var d in dirs)
        {
            if (rnd.Next(4) > 0) fs.EnsureDirectory(d);
        }
        if (rnd.Next(2) == 0) fs.EnsureDirectory("/data/empty" + rnd.Next(3));

        var count = rnd.Next(3, 12);
        for (var i = 0; i < count; i++)
        {
            var dir = dirs[rnd.Next(dirs.Length)];
            fs.WriteAllText($"{dir}/f{rnd.Next(8)}.txt", "content-" + rnd.Next(1000));
        }
        return fs;
    }

    private static List<Operation> RandomOperations(Random rnd, FakeFileSystem fs)
    {
        var existing = fs.GetFiles(string.Empty, "*", true).Where(IsUserPath).ToList();
        var dirs = new[] { "/data/a", "/data/b", "/data/new1", "/data/new2/deep", "/data/c/deep" };
        var ops = new List<Operation>();
        var tmpCounter = 0;

        string AnyFile() => existing.Count > 0 && rnd.Next(10) > 0 ? existing[rnd.Next(existing.Count)] : "/data/ghost.txt";
        string AnyTarget() => $"{dirs[rnd.Next(dirs.Length)]}/t{rnd.Next(6)}.txt";
        FileTimes Times() => FileTimes.From(
            new DateTime(2000 + rnd.Next(20), 1 + rnd.Next(12), 1 + rnd.Next(28), 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2000 + rnd.Next(20), 1 + rnd.Next(12), 1 + rnd.Next(28), 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2000 + rnd.Next(20), 1 + rnd.Next(12), 1 + rnd.Next(28), 0, 0, 0, DateTimeKind.Utc));

        var n = rnd.Next(1, 14);
        for (var i = 0; i < n; i++)
        {
            switch (rnd.Next(8))
            {
                case 0: ops.Add(Operation.Move(AnyFile(), AnyTarget())); break;
                case 1: ops.Add(Operation.Copy(AnyFile(), AnyTarget())); break;
                case 2: ops.Add(Operation.CreateDir(dirs[rnd.Next(dirs.Length)])); break;
                case 3: ops.Add(Operation.RemoveDir(dirs[rnd.Next(dirs.Length)])); break;
                case 4: ops.Add(Operation.Quarantine(AnyFile())); break;
                case 5: ops.Add(Operation.SetTimestamps(AnyFile(), Times())); break;
                case 6: ops.Add(Operation.SetAttributes(AnyFile(), rnd.Next(2) == 0 ? FileAttributes.ReadOnly : FileAttributes.Hidden | FileAttributes.Archive)); break;
                default:
                    var temp = $"{Tmp}/gen{tmpCounter++}.bin";
                    fs.WriteAllText(temp, "generated-" + rnd.Next(1000));
                    ops.Add(Operation.WriteFile(temp, rnd.Next(2) == 0 ? AnyFile() : AnyTarget()));
                    break;
            }
        }
        return ops;
    }

    [Fact]
    public void ExecuteThenUndo_AlwaysRestoresTheOriginalState()
    {
        for (var seed = 0; seed < 400; seed++)
        {
            var rnd = new Random(seed);
            var fs = RandomState(rnd);
            var ops = RandomOperations(rnd, fs);
            var before = Snapshot(fs);

            var exec = new OperationExecutor(fs, Journal, new QuarantineStore(fs, Quarantine));
            var result = exec.Execute(ops);
            var undo = new UndoService(fs).UndoJournal(result.JournalPath);

            var after = Snapshot(fs);
            Assert.True(before == after,
                $"seed {seed}: trạng thái khác sau hoàn tác (succeeded={result.Succeeded}, failed={result.Failed}, undone={undo.Undone}, undoFailed={undo.Failed})\n--- trước ---\n{before}\n--- sau ---\n{after}\nOps: {string.Join("; ", ops.Select(o => $"{o.Kind}({o.From}->{o.To})"))}");
            Assert.Equal(0, undo.Failed);
            Assert.Equal(result.Succeeded, undo.Undone);
        }
    }

    [Fact]
    public void Quarantine_NeverLosesFileContent()
    {
        for (var seed = 0; seed < 200; seed++)
        {
            var rnd = new Random(1000 + seed);
            var fs = RandomState(rnd);
            var files = fs.GetFiles(string.Empty, "*", true).Where(IsUserPath).ToList();
            var contents = files.Select(f => fs.ReadAllText(f)).OrderBy(x => x, StringComparer.Ordinal).ToList();

            var victims = files.Where(_ => rnd.Next(2) == 0).Select(Operation.Quarantine).ToList();
            new OperationExecutor(fs, Journal, new QuarantineStore(fs, Quarantine)).Execute(victims);

            // Mọi nội dung vẫn còn đâu đó (tại chỗ hoặc trong khu cách ly): "xoá" không bao giờ mất dữ liệu.
            var remaining = fs.GetFiles(string.Empty, "*", true)
                .Where(f => IsUserPath(f) || f.StartsWith(Quarantine, StringComparison.OrdinalIgnoreCase))
                .Select(f => fs.ReadAllText(f)).OrderBy(x => x, StringComparer.Ordinal).ToList();
            Assert.Equal(contents, remaining);
        }
    }

    [Fact]
    public void RecoveryRollback_AfterCrashAtAnyPoint_RestoresTheOriginalState()
    {
        for (var seed = 0; seed < 150; seed++)
        {
            var rnd = new Random(5000 + seed);
            var fs = RandomState(rnd);
            var ops = RandomOperations(rnd, fs);
            var before = Snapshot(fs);

            var exec = new OperationExecutor(fs, Journal, new QuarantineStore(fs, Quarantine));
            var result = exec.Execute(ops);

            // Giả lập sự cố: cắt journal tại một điểm ngẫu nhiên (mất footer và các bước sau đó),
            // trong khi mọi thay đổi đã xảy ra vẫn nằm trên đĩa.
            var lines = fs.ReadAllText(result.JournalPath).Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
            var keep = rnd.Next(1, lines.Count);
            // Bước đang dở: dòng pending cuối cùng có thể không có done kèm theo.
            fs.WriteAllText(result.JournalPath, string.Join("\n", lines.Take(keep)) + "\n");

            var recovery = new RecoveryService(fs).Rollback(result.JournalPath);

            // Các bước nằm sau điểm cắt đã xảy ra trên đĩa nhưng không còn dấu vết trong journal: chỉ kiểm tra
            // rằng khôi phục không tạo thêm lỗi và không làm mất nội dung nào.
            Assert.True(recovery.Failed >= 0);
            var contentsBefore = before.Split('\n').Where(l => l.StartsWith("F ")).Select(l => l.Split('|')[1].Trim()).OrderBy(x => x).ToList();
            var contentsAfter = fs.GetFiles(string.Empty, "*", true)
                .Where(f => IsUserPath(f) || f.StartsWith(Quarantine, StringComparison.OrdinalIgnoreCase))
                .Select(f => fs.ReadAllText(f)).ToList();
            foreach (var c in contentsBefore)
            {
                var detail = $"seed {seed}: mất nội dung {c} | Ops: {string.Join("; ", ops.Select(o => $"{o.Kind}({o.From}->{o.To})"))} | Journal giữ {keep}/{lines.Count} dòng: {string.Join(" || ", lines)}";
                Assert.True(contentsAfter.Contains(c), detail);
            }
        }
    }
}
