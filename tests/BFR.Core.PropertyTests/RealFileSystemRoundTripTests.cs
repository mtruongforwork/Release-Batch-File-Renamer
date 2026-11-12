namespace BFR.Core.PropertyTests;

using BFR.Core;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

/// <summary>
/// Cùng bất biến "thực thi rồi hoàn tác trả lại đúng trạng thái" nhưng trên đĩa THẬT: bản giả có thể che các lỗi
/// phụ thuộc hệ thống tệp (tự tạo thư mục cha, phân biệt hoa/thường, tệp chỉ đọc...).
/// </summary>
public sealed class RealFileSystemRoundTripTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bfr-real-" + Guid.NewGuid().ToString("N"));

    public RealFileSystemRoundTripTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(_root, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string P(string relative) => Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));

    private string Snapshot(string dataRoot)
    {
        var lines = new List<string>();
        foreach (var f in Directory.EnumerateFiles(dataRoot, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add($"F {Path.GetRelativePath(dataRoot, f)} | {File.ReadAllText(f)} | {File.GetLastWriteTimeUtc(f).Ticks} | {(int)File.GetAttributes(f)}");
        }
        foreach (var d in Directory.EnumerateDirectories(dataRoot, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add("D " + Path.GetRelativePath(dataRoot, d));
        }
        return string.Join("\n", lines);
    }

    [Fact]
    public void ExecuteThenUndo_RestoresTheOriginalState_OnTheRealDisk()
    {
        for (var seed = 0; seed < 80; seed++)
        {
            var rnd = new Random(seed);
            var data = P($"s{seed}/data");
            var journal = P($"s{seed}/journal");
            var quarantine = P($"s{seed}/q");
            var tmp = P($"s{seed}/tmp");
            Directory.CreateDirectory(data);
            Directory.CreateDirectory(tmp);

            var dirs = new[] { "a", "b", "c/deep" };
            foreach (var d in dirs) if (rnd.Next(4) > 0) Directory.CreateDirectory(Path.Combine(data, d.Replace('/', Path.DirectorySeparatorChar)));
            if (rnd.Next(2) == 0) Directory.CreateDirectory(Path.Combine(data, "empty" + rnd.Next(3)));
            for (var i = 0; i < rnd.Next(3, 12); i++)
            {
                var dir = Path.Combine(data, dirs[rnd.Next(dirs.Length)].Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, $"f{rnd.Next(8)}.txt");
                File.WriteAllText(file, "content-" + rnd.Next(1000));
                File.SetLastWriteTimeUtc(file, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            }

            var existing = Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories).ToList();
            var targets = new[] { "a", "b", "new1", "new2/deep", "c/deep" };
            string AnyFile() => existing.Count > 0 && rnd.Next(10) > 0 ? existing[rnd.Next(existing.Count)] : Path.Combine(data, "ghost.txt");
            string Target() => Path.Combine(data, targets[rnd.Next(targets.Length)].Replace('/', Path.DirectorySeparatorChar), $"t{rnd.Next(6)}.txt");
            string Dir() => Path.Combine(data, targets[rnd.Next(targets.Length)].Replace('/', Path.DirectorySeparatorChar));

            var ops = new List<Operation>();
            var generated = 0;
            for (var i = 0; i < rnd.Next(1, 14); i++)
            {
                switch (rnd.Next(8))
                {
                    case 0: ops.Add(Operation.Move(AnyFile(), Target())); break;
                    case 1: ops.Add(Operation.Copy(AnyFile(), Target())); break;
                    case 2: ops.Add(Operation.CreateDir(Dir())); break;
                    case 3: ops.Add(Operation.RemoveDir(Dir())); break;
                    case 4: ops.Add(Operation.Quarantine(AnyFile())); break;
                    case 5:
                        var t = new DateTime(2000 + rnd.Next(20), 1 + rnd.Next(12), 1 + rnd.Next(28), 0, 0, 0, DateTimeKind.Utc);
                        ops.Add(Operation.SetTimestamps(AnyFile(), FileTimes.From(t, t, t)));
                        break;
                    case 6: ops.Add(Operation.SetAttributes(AnyFile(), rnd.Next(2) == 0 ? FileAttributes.ReadOnly : FileAttributes.Hidden | FileAttributes.Archive)); break;
                    default:
                        var temp = Path.Combine(tmp, $"gen{generated++}.bin");
                        File.WriteAllText(temp, "generated-" + rnd.Next(1000));
                        ops.Add(Operation.WriteFile(temp, rnd.Next(2) == 0 ? AnyFile() : Target()));
                        break;
                }
            }

            var before = Snapshot(data);
            var fs = new RealFileSystem();
            var result = new OperationExecutor(fs, journal, new QuarantineStore(fs, quarantine)).Execute(ops);
            var undo = new UndoService(fs).UndoJournal(result.JournalPath);
            var after = Snapshot(data);

            Assert.True(before == after,
                $"seed {seed}: khác sau hoàn tác (ok={result.Succeeded}, fail={result.Failed}, undone={undo.Undone}, undoFailed={undo.Failed})\n--- trước ---\n{before}\n--- sau ---\n{after}\nOps: {string.Join("; ", ops.Select(o => $"{o.Kind}({Path.GetRelativePath(_root, o.From)}->{(o.To.Length > 0 ? Path.GetRelativePath(_root, o.To) : "")})"))}");
            Assert.Equal(0, undo.Failed);
        }
    }
}
