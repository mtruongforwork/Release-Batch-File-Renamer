namespace BFR.Core.Tests;

using BFR.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

public class TimestampTests
{
    private static DateTime Utc(int y, int m, int d, int h = 0, int mi = 0, int s = 0) => new(y, m, d, h, mi, s, DateTimeKind.Utc);

    private static FileItem Make(FakeFileSystem fs, string path, DateTime? modified = null)
    {
        fs.WriteAllText(path, "x");
        var m = modified ?? Utc(2020, 1, 1);
        fs.SetTimes(path, FileTimes.From(m, m, m));
        var name = Path.GetFileNameWithoutExtension(path);
        return new FileItem(path, path[..path.LastIndexOf('/')], name, Path.GetExtension(path).TrimStart('.'), false, 1, m, m, 0);
    }

    private static RuleContext Ctx(params (string path, string key, string value)[] meta) =>
        new(DateTime.UtcNow, "s", null, meta.GroupBy(x => x.path).ToDictionary(
            g => g.Key, g => (IReadOnlyDictionary<string, string>)g.ToDictionary(x => x.key, x => x.value)));

    [Fact]
    public void Fixed_SetsSelectedFieldsOnly_AndUndoRestoresExactTicks()
    {
        var fs = new FakeFileSystem();
        var f = Make(fs, "/a/x.jpg");
        var before = fs.GetTimes(f.FullPath);
        var local = new DateTime(2022, 6, 1, 12, 0, 0, DateTimeKind.Local);

        var plan = TimestampPlanner.Plan(new[] { f }, new TimestampOptions(TimestampSource.Fixed, SetCreated: true, SetModified: true, FixedLocal: local), Ctx(), fs);
        var result = new OperationExecutor(fs, "/journal", new QuarantineStore(fs, "/q")).Execute(plan.Operations);

        var now = fs.GetTimes(f.FullPath);
        Assert.Equal(local.ToUniversalTime().Ticks, now.CreatedUtcTicks);
        Assert.Equal(local.ToUniversalTime().Ticks, now.ModifiedUtcTicks);
        Assert.Equal(before.AccessedUtcTicks, now.AccessedUtcTicks);

        new UndoService(fs).UndoJournal(result.JournalPath);
        Assert.Equal(before, fs.GetTimes(f.FullPath));
    }

    [Fact]
    public void Shift_AddsOffsetToExistingValues()
    {
        var fs = new FakeFileSystem();
        var f = Make(fs, "/a/x.jpg", Utc(2020, 1, 1, 10));

        var plan = TimestampPlanner.Plan(new[] { f },
            new TimestampOptions(TimestampSource.Shift, SetModified: true, Offset: TimeSpan.FromHours(-3)), Ctx(), fs);

        Assert.Equal(Utc(2020, 1, 1, 7), plan.Items[0].After!.Value.ModifiedUtc);
        Assert.Equal(plan.Items[0].Before!.Value.CreatedUtcTicks, plan.Items[0].After!.Value.CreatedUtcTicks);
    }

    [Fact]
    public void Exif_UsesTakenThenMedia_AndReportsNoData()
    {
        var fs = new FakeFileSystem();
        var a = Make(fs, "/a/a.jpg");
        var b = Make(fs, "/a/b.mp4");
        var c = Make(fs, "/a/c.jpg");
        var taken = new DateTime(2019, 3, 4, 5, 6, 7, DateTimeKind.Local);
        var ctx = Ctx((a.FullPath, "taken", taken.ToString("O")), (b.FullPath, "media", taken.AddDays(1).ToString("O")));

        var plan = TimestampPlanner.Plan(new[] { a, b, c }, new TimestampOptions(TimestampSource.Exif, SetCreated: true), ctx, fs);

        Assert.Equal(taken.ToUniversalTime(), plan.Items[0].After!.Value.CreatedUtc);
        Assert.Equal(taken.AddDays(1).ToUniversalTime(), plan.Items[1].After!.Value.CreatedUtc);
        Assert.Equal(TimestampStatus.NoData, plan.Items[2].Status);
        Assert.Equal(2, plan.Operations.Count);
    }

    [Theory]
    [InlineData("IMG_20240512_103000", null, 2024, 5, 12, 10, 30, 0)]
    [InlineData("2023-07-04 08.15.09 photo", null, 2023, 7, 4, 8, 15, 9)]
    [InlineData("scan_20210301", null, 2021, 3, 1, 0, 0, 0)]
    [InlineData("VID-20240512-WA0001", null, 2024, 5, 12, 0, 0, 0)]
    [InlineData("PXL_20240512_103000123", "PXL_{yyyyMMdd_HHmmss}", 2024, 5, 12, 10, 30, 0)]
    public void FileName_ParsesDates(string name, string? pattern, int y, int mo, int d, int h, int mi, int s) =>
        Assert.Equal(new DateTime(y, mo, d, h, mi, s, DateTimeKind.Local), TimestampPlanner.FromFileName(name, pattern));

    [Theory]
    [InlineData("holiday photo", null)]
    [InlineData("20240231_101010", null)]      // ngày không tồn tại
    [InlineData("order 1234567890123", null)]  // dãy số dài không phải ngày
    [InlineData("IMG_x", "IMG_{yyyyMMdd}")]
    [InlineData("other_20240512", "IMG_{yyyyMMdd}")]
    public void FileName_RejectsNonDates(string name, string? pattern) =>
        Assert.Null(TimestampPlanner.FromFileName(name, pattern));

    [Fact]
    public void CopyFromFile_CopiesAllThreeFields_AndMissingSourceIsNoData()
    {
        var fs = new FakeFileSystem();
        var src = Make(fs, "/s/src.txt", Utc(2001, 2, 3));
        fs.SetTimes(src.FullPath, new FileTimes(Utc(2001, 1, 1).Ticks, Utc(2001, 2, 3).Ticks, Utc(2001, 4, 5).Ticks));
        var dst = Make(fs, "/d/dst.txt");

        var plan = TimestampPlanner.Plan(new[] { dst },
            new TimestampOptions(TimestampSource.CopyFromFile, true, true, true, CopyFromPath: src.FullPath), Ctx(), fs);
        Assert.Equal(fs.GetTimes(src.FullPath), plan.Items[0].After);

        var missing = TimestampPlanner.Plan(new[] { dst },
            new TimestampOptions(TimestampSource.CopyFromFile, CopyFromPath: "/none"), Ctx(), fs);
        Assert.Equal(TimestampStatus.NoData, missing.Items[0].Status);
    }

    [Fact]
    public void UnchangedValues_ProduceNoOperation()
    {
        var fs = new FakeFileSystem();
        var f = Make(fs, "/a/x.txt", Utc(2020, 1, 1));

        var plan = TimestampPlanner.Plan(new[] { f }, new TimestampOptions(TimestampSource.Shift, SetModified: true), Ctx(), fs);

        Assert.Equal(TimestampStatus.Unchanged, plan.Items[0].Status);
        Assert.Empty(plan.Operations);
    }

    [Fact]
    public void Attributes_OnlyAllowedBitsChange_AndUndoRestores()
    {
        var fs = new FakeFileSystem();
        var f = Make(fs, "/a/x.txt");
        fs.SetAttributes(f.FullPath, FileAttributes.System | FileAttributes.Archive);

        var plan = AttributePlanner.Plan(new[] { f }, FileAttributes.ReadOnly | FileAttributes.System, FileAttributes.Archive | FileAttributes.System, fs);
        var op = Assert.Single(plan.Operations);
        // System không bao giờ được thêm hay bỏ; Archive bị bỏ; ReadOnly được thêm.
        Assert.Equal(FileAttributes.System | FileAttributes.ReadOnly, op.AttributesAfter);

        var result = new OperationExecutor(fs, "/journal", new QuarantineStore(fs, "/q")).Execute(plan.Operations);
        new UndoService(fs).UndoJournal(result.JournalPath);
        Assert.Equal(FileAttributes.System | FileAttributes.Archive, fs.GetAttributes(f.FullPath));
    }
}
