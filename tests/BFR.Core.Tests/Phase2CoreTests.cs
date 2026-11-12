namespace BFR.Core.Tests;

using BFR.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Xunit;

public class ChecksumTests
{
    [Fact]
    public void Compute_MatchesKnownVectors()
    {
        var fs = new FakeFileSystem();
        fs.WriteAllBytes("/a.txt", Encoding.ASCII.GetBytes("abc"));

        Assert.Equal("900150983cd24fb0d6963f7d28e17f72", Checksum.Compute(fs, "/a.txt", ChecksumAlgorithm.Md5));
        Assert.Equal("a9993e364706816aba3e25717850c26c9cd0d89d", Checksum.Compute(fs, "/a.txt", ChecksumAlgorithm.Sha1));
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", Checksum.Compute(fs, "/a.txt", ChecksumAlgorithm.Sha256));
    }

    [Fact]
    public void FormatThenParse_RoundTrips_AndAcceptsBinaryMarker()
    {
        var hash = new string('a', 64);
        var text = Checksum.Format(new[] { new ChecksumEntry(hash, @"sub\x.bin") });
        Assert.Equal(hash + "  sub/x.bin\n", text);

        var parsed = Checksum.Parse("# note\n" + hash + " *y.bin\r\nnot a line\n" + text, ChecksumAlgorithm.Sha256);
        Assert.Equal(new[] { "y.bin", "sub/x.bin" }, parsed.Select(p => p.RelativePath));
    }

    [Fact]
    public void Verify_ReportsMatchMismatchMissingAndRejectsUnsafePaths()
    {
        var fs = new FakeFileSystem();
        fs.WriteAllBytes("/d/ok.txt", Encoding.ASCII.GetBytes("abc"));
        fs.WriteAllBytes("/d/bad.txt", Encoding.ASCII.GetBytes("zzz"));
        var good = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

        var results = Checksum.Verify(fs, "/d", new[]
        {
            new ChecksumEntry(good, "ok.txt"),
            new ChecksumEntry(good, "bad.txt"),
            new ChecksumEntry(good, "gone.txt"),
            new ChecksumEntry(good, "../ok.txt"),
            new ChecksumEntry(good, "C:/Windows/x")
        }, ChecksumAlgorithm.Sha256);

        Assert.Equal(new[] { ChecksumStatus.Match, ChecksumStatus.Mismatch, ChecksumStatus.Missing, ChecksumStatus.Invalid, ChecksumStatus.Invalid },
            results.Select(r => r.Status));
    }
}

public class DuplicateTests
{
    private static FileItem Add(FakeFileSystem fs, string path, byte[] content, DateTime? modified = null)
    {
        fs.WriteAllBytes(path, content);
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        var ext = System.IO.Path.GetExtension(path).TrimStart('.');
        var dir = path[..path.LastIndexOf('/')];
        var m = modified ?? new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return new FileItem(path, dir, name, ext, false, content.Length, m, m, 0);
    }

    [Fact]
    public void Finder_GroupsIdenticalContent_NoFalsePositiveForSameSizeDifferentContent()
    {
        var fs = new FakeFileSystem();
        var a = Add(fs, "/x/a.bin", new byte[] { 1, 2, 3, 4 });
        var b = Add(fs, "/y/b.bin", new byte[] { 1, 2, 3, 4 });
        var c = Add(fs, "/y/c.bin", new byte[] { 1, 2, 3, 5 });   // cùng kích thước, khác nội dung
        var d = Add(fs, "/y/d.bin", new byte[] { 9 });             // kích thước duy nhất

        var groups = new DuplicateFinder(fs).Find(new[] { a, b, c, d }, new DuplicateOptions());

        var group = Assert.Single(groups);
        Assert.Equal(new[] { "/x/a.bin", "/y/b.bin" }, group.Files.Select(f => f.FullPath).OrderBy(x => x));
    }

    [Fact]
    public void Finder_DistinguishesFilesThatDifferOnlyInTheMiddle()
    {
        var fs = new FakeFileSystem();
        var big1 = new byte[300_000];
        var big2 = new byte[300_000];
        big2[150_000] = 7; // đầu và cuối giống nhau, giữa khác nhau: tầng 2 không tách được, tầng 3 phải tách
        var a = Add(fs, "/a.bin", big1);
        var b = Add(fs, "/b.bin", big2);
        var c = Add(fs, "/c.bin", (byte[])big1.Clone());

        var groups = new DuplicateFinder(fs).Find(new[] { a, b, c }, new DuplicateOptions());

        var group = Assert.Single(groups);
        Assert.Equal(new[] { "/a.bin", "/c.bin" }, group.Files.Select(f => f.FullPath).OrderBy(x => x));
    }

    [Fact]
    public void Finder_SkipsEmptyOfflineAndHonoursCancellation()
    {
        var fs = new FakeFileSystem();
        var e1 = Add(fs, "/e1", Array.Empty<byte>());
        var e2 = Add(fs, "/e2", Array.Empty<byte>());
        var o1 = Add(fs, "/o1", new byte[] { 1 }) with { IsOffline = true };
        var o2 = Add(fs, "/o2", new byte[] { 1 }) with { IsOffline = true };

        Assert.Empty(new DuplicateFinder(fs).Find(new[] { e1, e2, o1, o2 }, new DuplicateOptions()));

        var a = Add(fs, "/a", new byte[] { 1, 1 });
        var b = Add(fs, "/b", new byte[] { 1, 1 });
        using var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => new DuplicateFinder(fs).Find(new[] { a, b }, new DuplicateOptions(), cts.Token));
    }

    [Fact]
    public void KeepRules_ChooseExpectedFile()
    {
        var fs = new FakeFileSystem();
        var old = Add(fs, "/a/photo.jpg", new byte[] { 1 }, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var copy = Add(fs, "/b/photo (1).jpg", new byte[] { 1 }, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var group = new DuplicateGroup(1, "h", new[] { copy, old });

        Assert.Equal(old, DuplicatePlanner.Choose(group, KeepRule.Oldest));
        Assert.Equal(copy, DuplicatePlanner.Choose(group, KeepRule.Newest));
        Assert.Equal(old, DuplicatePlanner.Choose(group, KeepRule.CleanName));
        Assert.Equal(copy, DuplicatePlanner.Choose(group, KeepRule.PreferFolder, new[] { "/b" }));
    }

    [Fact]
    public void Plan_AlwaysKeepsOneFile_AndQuarantineRoundTrips()
    {
        var fs = new FakeFileSystem();
        var a = Add(fs, "/a/f.bin", new byte[] { 5, 5 });
        var b = Add(fs, "/b/f.bin", new byte[] { 5, 5 });
        var c = Add(fs, "/c/f.bin", new byte[] { 5, 5 });
        var groups = new DuplicateFinder(fs).Find(new[] { a, b, c }, new DuplicateOptions());

        var plan = DuplicatePlanner.Plan(groups, new[] { DuplicatePlanner.Choose(groups[0], KeepRule.ShortestPath) }, DuplicateAction.Quarantine, fs);

        Assert.Equal(2, plan.Operations.Count);
        Assert.Equal(4, plan.BytesFreed);

        var result = new OperationExecutor(fs, "/journal", new QuarantineStore(fs, "/q")).Execute(plan.Operations);
        Assert.Equal(2, result.Succeeded);
        Assert.Equal(1, new[] { a, b, c }.Count(f => fs.FileExists(f.FullPath)));

        new UndoService(fs).UndoJournal(result.JournalPath);
        Assert.True(new[] { a, b, c }.All(f => fs.FileExists(f.FullPath)));
    }

    [Fact]
    public void Plan_IgnoresInvalidKeeper_AndMoveToFolderAvoidsCollisions()
    {
        var fs = new FakeFileSystem();
        var a = Add(fs, "/a/f.bin", new byte[] { 5, 5 });
        var b = Add(fs, "/b/f.bin", new byte[] { 5, 5 });
        var outsider = Add(fs, "/z/other.bin", new byte[] { 0 });
        var group = new DuplicateGroup(2, "h", new[] { a, b });

        var none = DuplicatePlanner.Plan(new[] { group }, new[] { outsider }, DuplicateAction.Quarantine, fs);
        Assert.Empty(none.Operations);
        Assert.Single(none.Skipped);

        fs.WriteAllText("/dup/f.bin", "existing");
        var move = DuplicatePlanner.Plan(new[] { group }, new[] { a }, DuplicateAction.MoveToFolder, fs, "/dup");
        Assert.EndsWith("f (2).bin", move.Operations.Single().To);
    }
}

public class AnalyzeTests
{
    private static readonly byte[] Png = { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };

    [Fact]
    public void MagicBytes_DetectsAndJudgesExtensions()
    {
        Assert.Equal("png", MagicBytes.Detect(Png));
        Assert.Equal("zip", MagicBytes.Detect(new byte[] { (byte)'P', (byte)'K', 3, 4 }));
        Assert.Null(MagicBytes.Detect(Encoding.ASCII.GetBytes("hello world, plain")));

        Assert.True(MagicBytes.IsConsistent("docx", "zip"));
        Assert.True(MagicBytes.IsConsistent("JPEG", "jpg"));
        Assert.False(MagicBytes.IsConsistent("jpg", "png"));
        Assert.True(MagicBytes.IsConsistent("dat", "png"));   // đuôi lạ: không báo nhầm
        Assert.False(MagicBytes.IsConsistent("", "png"));
    }

    [Fact]
    public void Analyze_ReportsTotalsTypesLargestOldAndMismatches_SkippingLinks()
    {
        var fs = new FakeFileSystem();
        fs.WriteAllBytes("/r/pic.png", Png);
        fs.WriteAllBytes("/r/fake.jpg", Png);                       // PNG đội lốt .jpg
        fs.WriteAllBytes("/r/docs/big.pdf", new byte[5000]);
        fs.WriteAllBytes("/r/docs/note.txt", new byte[10]);
        fs.WriteAllBytes("/r/linked/secret.bin", new byte[999999]);
        fs.MarkLink("/r/linked");
        fs.EnsureDirectory("/r/linked");

        var report = FolderAnalyzer.Analyze(fs, "/r", new AnalyzeOptions(TopLargest: 2, OlderThanYears: 1), DateTime.UtcNow.AddYears(10));

        Assert.Equal(4, report.TotalFiles);
        Assert.Equal(Png.Length * 2 + 5010, report.TotalBytes);
        Assert.Equal("/r/docs/big.pdf", report.Largest[0].Path);
        Assert.Equal(2, report.Largest.Count);
        Assert.Equal(4, report.Old.Count);
        Assert.Single(report.Mismatched);
        Assert.Equal("/r/fake.jpg", report.Mismatched[0].Path);
        Assert.Contains("/r/linked", report.Skipped);
        Assert.Equal("/r/docs", report.TopFolders[0].Path);
    }
}
