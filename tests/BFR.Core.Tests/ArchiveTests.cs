namespace BFR.Core.Tests;

using BFR.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Xunit;

public class ArchiveTests
{
    private static FileItem Add(FakeFileSystem fs, string path, string content)
    {
        fs.WriteAllBytes(path, Encoding.UTF8.GetBytes(content));
        var t = new DateTime(2022, 3, 4, 5, 6, 8, DateTimeKind.Utc);
        var dir = path[..path.LastIndexOf('/')];
        return new FileItem(path, dir, Path.GetFileNameWithoutExtension(path), Path.GetExtension(path).TrimStart('.'), false, content.Length, t, t, 0);
    }

    private static byte[] MakeZip(Action<ZipArchive> fill)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true)) fill(zip);
        return ms.ToArray();
    }

    private static void Put(ZipArchive z, string name, string text)
    {
        using var w = z.CreateEntry(name).Open();
        w.Write(Encoding.UTF8.GetBytes(text));
    }

    private static OperationExecutor Exec(FakeFileSystem fs) => new(fs, "/journal", new QuarantineStore(fs, "/q"));

    private static string Sha(FakeFileSystem fs, string path) => Checksum.Compute(fs, path, ChecksumAlgorithm.Sha256);

    [Fact]
    public void Compress_AllInOne_ThenExtract_GivesIdenticalFiles()
    {
        var fs = new FakeFileSystem();
        var files = new[] { Add(fs, "/src/a.txt", "alpha"), Add(fs, "/src/sub/b.txt", "bravo"), Add(fs, "/src/sub/deep/c.bin", new string('z', 5000)) };
        var options = new CompressOptions();

        var jobs = ArchiveCompressor.Plan(files, options, fs);
        var job = Assert.Single(jobs);
        Assert.Equal("/src/Archive.zip", job.ZipPath);
        Assert.Equal(new[] { "a.txt", "sub/b.txt", "sub/deep/c.bin" }, job.Entries.Select(e => e.EntryName));

        var run = ArchiveCompressor.Run(jobs, options, fs, "/tmp");
        var result = Exec(fs).Execute(run.Operations);
        Assert.Equal(0, result.Failed);
        Assert.True(fs.FileExists("/src/Archive.zip"));
        Assert.True(run.BytesAfter < run.BytesBefore);

        var extract = ArchiveExtractor.Extract(new[] { "/src/Archive.zip" }, new ExtractOptions(), fs, "/tmp2");
        Assert.Empty(extract.Rejected);
        Assert.Equal(0, Exec(fs).Execute(extract.Operations).Failed);

        foreach (var f in files)
        {
            var copy = f.FullPath.Replace("/src/", "/src/Archive/");
            Assert.Equal(Sha(fs, f.FullPath), Sha(fs, copy));
        }
    }

    [Fact]
    public void Compress_PerFolder_AndPerGroup_AndNeverOverwritesExistingZip()
    {
        var fs = new FakeFileSystem();
        var files = new[] { Add(fs, "/s/x/1.txt", "1"), Add(fs, "/s/x/2.txt", "2"), Add(fs, "/s/y/3.txt", "3"), Add(fs, "/s/y/4.txt", "4"), Add(fs, "/s/y/5.txt", "5") };
        fs.WriteAllText("/s/x/x.zip", "already");

        var perFolder = ArchiveCompressor.Plan(files, new CompressOptions(ArchiveGrouping.PerFolder), fs);
        Assert.Equal(new[] { "/s/x/x (2).zip", "/s/y/y.zip" }, perFolder.Select(j => j.ZipPath));

        var perGroup = ArchiveCompressor.Plan(files, new CompressOptions(ArchiveGrouping.PerGroup, MaxFiles: 2), fs);
        Assert.Equal(new[] { "/s/Archive_01.zip", "/s/Archive_02.zip", "/s/Archive_03.zip" }, perGroup.Select(j => j.ZipPath));
        Assert.Equal(new[] { 2, 2, 1 }, perGroup.Select(j => j.Entries.Count));
        Assert.Equal("x/1.txt", perGroup[0].Entries[0].EntryName);
    }

    [Fact]
    public void Compress_UndoRemovesTheZip()
    {
        var fs = new FakeFileSystem();
        var a = Add(fs, "/d/a.txt", "a");
        var jobs = ArchiveCompressor.Plan(new[] { a }, new CompressOptions(NameTemplate: "bundle"), fs);
        var run = ArchiveCompressor.Run(jobs, new CompressOptions(), fs, "/tmp");
        var result = Exec(fs).Execute(run.Operations);
        Assert.True(fs.FileExists("/d/bundle.zip"));

        new UndoService(fs).UndoJournal(result.JournalPath);
        Assert.False(fs.FileExists("/d/bundle.zip"));
        Assert.True(fs.FileExists("/d/a.txt"));
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("a/../../evil.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("\\windows\\system32\\x.dll")]
    [InlineData("C:/Windows/x.dll")]
    [InlineData("..\\evil.txt")]
    public void ZipSlip_IsRejected_AndNothingIsWritten(string badName)
    {
        var fs = new FakeFileSystem();
        fs.WriteAllBytes("/in/bad.zip", MakeZip(z => { Put(z, "ok.txt", "fine"); Put(z, badName, "evil"); }));

        var result = ArchiveExtractor.Extract(new[] { "/in/bad.zip" }, new ExtractOptions(), fs, "/tmp");

        var rejected = Assert.Single(result.Rejected);
        Assert.Contains(badName, rejected.Reason);
        Assert.Empty(result.Operations);
        Assert.Empty(fs.GetFiles("/tmp", "*", true));
    }

    [Fact]
    public void ZipBomb_IsRejectedAndTempCleaned()
    {
        var fs = new FakeFileSystem();
        fs.WriteAllBytes("/in/bomb.zip", MakeZip(z =>
        {
            using var w = z.CreateEntry("zeros.bin", CompressionLevel.SmallestSize).Open();
            var chunk = new byte[1024 * 1024];
            for (var i = 0; i < 40; i++) w.Write(chunk);
        }));

        var result = ArchiveExtractor.Extract(new[] { "/in/bomb.zip" }, new ExtractOptions(), fs, "/tmp");

        Assert.Single(result.Rejected);
        Assert.Empty(result.Operations);
        Assert.Empty(fs.GetFiles("/tmp", "*", true));

        var big = ArchiveExtractor.Extract(new[] { "/in/bomb.zip" }, new ExtractOptions(MaxRatio: 100000, MaxTotalBytes: 1024), fs, "/tmp");
        Assert.Single(big.Rejected);
    }

    [Fact]
    public void Symlinks_InvalidZips_AndDirectoryEntries_AreHandled()
    {
        var fs = new FakeFileSystem();
        fs.WriteAllBytes("/in/s.zip", MakeZip(z =>
        {
            Put(z, "real.txt", "data");
            var link = z.CreateEntry("link");
            link.ExternalAttributes = (0xA000 | 0x1FF) << 16;
            using (var w = link.Open()) w.Write(Encoding.UTF8.GetBytes("/etc/passwd"));
            z.CreateEntry("emptydir/");
        }));
        fs.WriteAllText("/in/notzip.zip", "hello");

        var result = ArchiveExtractor.Extract(new[] { "/in/s.zip", "/in/notzip.zip" }, new ExtractOptions(), fs, "/tmp");

        Assert.Equal("/in/notzip.zip", Assert.Single(result.Rejected).ZipPath);
        Assert.Single(result.Skipped);
        Assert.Equal(0, Exec(fs).Execute(result.Operations).Failed);
        Assert.True(fs.FileExists("/in/s/real.txt"));
        Assert.False(fs.FileExists("/in/s/link"));
        Assert.True(fs.DirectoryExists("/in/s/emptydir"));
    }

    [Fact]
    public void OwnFolder_UsesUniqueFolder_SingleFolder_RenamesCollisions_AndUndoCleansUp()
    {
        var fs = new FakeFileSystem();
        fs.WriteAllBytes("/in/p.zip", MakeZip(z => Put(z, "same.txt", "one")));
        fs.WriteAllBytes("/in/q.zip", MakeZip(z => Put(z, "same.txt", "two")));
        fs.WriteAllText("/in/p/existing.txt", "x");

        var own = ArchiveExtractor.Extract(new[] { "/in/p.zip" }, new ExtractOptions(), fs, "/t1");
        Assert.Equal(0, Exec(fs).Execute(own.Operations).Failed);
        Assert.Equal("one", fs.ReadAllText("/in/p (2)/same.txt"));

        var single = ArchiveExtractor.Extract(new[] { "/in/p.zip", "/in/q.zip" }, new ExtractOptions(ExtractLayout.SingleFolder, "/out"), fs, "/t2");
        var run = Exec(fs).Execute(single.Operations);
        Assert.Equal(0, run.Failed);
        Assert.Equal("one", fs.ReadAllText("/out/same.txt"));
        Assert.Equal("two", fs.ReadAllText("/out/same (2).txt"));

        new UndoService(fs).UndoJournal(run.JournalPath);
        Assert.False(fs.DirectoryExists("/out"));
    }

    [Theory]
    [InlineData("a/b.txt", "a|b.txt")]
    [InlineData("a\\b.txt", "a|b.txt")]
    [InlineData("./a//b/", "a|b")]
    [InlineData("we:ird?.txt", "we_ird_.txt")]
    [InlineData("CON/x.txt", "_CON|x.txt")]
    public void SafeParts_NormalisesNames(string name, string expected)
    {
        Assert.True(ArchiveExtractor.TrySafeParts(name, out var parts, out _));
        Assert.Equal(expected, string.Join("|", parts));
    }
}
