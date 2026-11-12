namespace BFR.Core.PropertyTests;

using BFR.Core;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

/// <summary>
/// Lớp phủ phải cho CÙNG kết quả với hệ thống tệp thật khi chạy cùng chuỗi thao tác (kể cả thao tác lỗi),
/// và không bao giờ làm thay đổi hệ thống tệp nền.
/// </summary>
public class OverlayPropertyTests
{
    private static bool IsUserPath(string p) =>
        !p.StartsWith("/journal", StringComparison.OrdinalIgnoreCase)
        && !p.StartsWith("/q", StringComparison.OrdinalIgnoreCase)
        && !p.StartsWith("/tmp", StringComparison.OrdinalIgnoreCase);

    private static string Snapshot(IFileSystem fs)
    {
        var lines = new List<string>();
        foreach (var f in fs.GetFiles(string.Empty, "*", true).Where(IsUserPath).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var t = fs.GetTimes(f);
            // Tệp mới sinh ra mang giờ hiện tại của từng hệ thống tệp: coi mọi giá trị "gần đây" là như nhau.
            static string Tick(long ticks) => ticks > new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks ? "now" : ticks.ToString();
            lines.Add($"F {f.Replace('\\', '/').ToLowerInvariant()} | {fs.ReadAllText(f)} | {Tick(t.CreatedUtcTicks)}/{Tick(t.ModifiedUtcTicks)}/{Tick(t.AccessedUtcTicks)} | {(int)fs.GetAttributes(f)} | {fs.GetLength(f)}");
        }
        foreach (var d in fs.GetDirectories(string.Empty, true).Where(IsUserPath).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add("D " + d.Replace('\\', '/').ToLowerInvariant());
        }
        return string.Join("\n", lines.OrderBy(l => l, StringComparer.Ordinal));
    }

    private static void Seed(FakeFileSystem fs, int seed)
    {
        var rnd = new Random(seed);
        var dirs = new[] { "/data/a", "/data/b", "/data/c/deep" };
        foreach (var d in dirs) if (rnd.Next(4) > 0) fs.EnsureDirectory(d);
        if (rnd.Next(2) == 0) fs.EnsureDirectory("/data/empty" + rnd.Next(3));
        var count = rnd.Next(3, 12);
        for (var i = 0; i < count; i++) fs.WriteAllText($"{dirs[rnd.Next(dirs.Length)]}/f{rnd.Next(8)}.txt", "content-" + rnd.Next(1000));

        // Cố định ngày giờ: hai hệ thống tệp được dựng ở hai thời điểm khác nhau không được lệch nhau vì đồng hồ.
        var fixedTime = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (var f in fs.GetFiles(string.Empty, "*", true)) fs.SetTimes(f, FileTimes.From(fixedTime, fixedTime, fixedTime));
    }

    private static List<Operation> Operations(Random rnd, FakeFileSystem sample, params IFileSystem[] temps)
    {
        var existing = sample.GetFiles(string.Empty, "*", true).Where(IsUserPath).ToList();
        var dirs = new[] { "/data/a", "/data/b", "/data/new1", "/data/new2/deep", "/data/c/deep" };
        var ops = new List<Operation>();
        var tmp = 0;

        string AnyFile() => existing.Count > 0 && rnd.Next(10) > 0 ? existing[rnd.Next(existing.Count)] : "/data/ghost.txt";
        string AnyTarget() => $"{dirs[rnd.Next(dirs.Length)]}/t{rnd.Next(6)}.txt";
        FileTimes Times() => FileTimes.From(
            new DateTime(2000 + rnd.Next(20), 1 + rnd.Next(12), 1 + rnd.Next(28), 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2000 + rnd.Next(20), 1 + rnd.Next(12), 1 + rnd.Next(28), 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2000 + rnd.Next(20), 1 + rnd.Next(12), 1 + rnd.Next(28), 0, 0, 0, DateTimeKind.Utc));

        for (var i = 0; i < rnd.Next(1, 14); i++)
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
                    var temp = $"/tmp/gen{tmp++}.bin";
                    var content = "generated-" + rnd.Next(1000);
                    foreach (var t in temps) t.WriteAllText(temp, content);
                    ops.Add(Operation.WriteFile(temp, rnd.Next(2) == 0 ? AnyFile() : AnyTarget()));
                    break;
            }
        }
        return ops;
    }

    [Fact]
    public void Overlay_MatchesRealFileSystem_AndLeavesBaseUntouched()
    {
        for (var seed = 0; seed < 300; seed++)
        {
            var reference = new FakeFileSystem();
            var baseFs = new FakeFileSystem();
            Seed(reference, seed);
            Seed(baseFs, seed);
            var baseBefore = Snapshot(baseFs);
            var overlay = new OverlayFileSystem(baseFs);

            // Thao tác chạy trên bản tham chiếu và lớp phủ; tệp tạm WriteFile có mặt ở cả hai (lớp phủ ghi vào bộ nhớ).
            var rnd = new Random(seed + 10_000);
            var ops = Operations(rnd, reference, reference, overlay);

            var a = new OperationExecutor(reference, "/journal", new QuarantineStore(reference, "/q")).Execute(ops);
            var b = new OperationExecutor(overlay, "/journal", new QuarantineStore(overlay, "/q")).Execute(ops);

            Assert.True(a.Succeeded == b.Succeeded && a.Failed == b.Failed, $"seed {seed}: kết quả khác ({a.Succeeded}/{a.Failed} vs {b.Succeeded}/{b.Failed})");
            var expected = Snapshot(reference);
            var actual = Snapshot(overlay);
            Assert.True(expected == actual, $"seed {seed}: lớp phủ khác bản thật\n--- thật ---\n{expected}\n--- lớp phủ ---\n{actual}\nOps: {string.Join("; ", ops.Select(o => $"{o.Kind}({o.From}->{o.To})"))}");
            Assert.Equal(baseBefore, Snapshot(baseFs));
        }
    }
}
