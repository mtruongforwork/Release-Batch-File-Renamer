namespace BFR.Presentation.Tests;

using BFR.Core;
using BFR.Core.Licensing;
using BFR.Presentation;

using System;
using System.IO;
using System.Linq;

using Xunit;

/// <summary>Test cho Shelf: gom tệp, chống trùng và giới hạn Free/Pro.</summary>
public class ShelfModelTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "bfr-shelf-" + Guid.NewGuid().ToString("N"));

    public ShelfModelTests() => Directory.CreateDirectory(_temp);

    public void Dispose()
    {
        try { if (Directory.Exists(_temp)) Directory.Delete(_temp, recursive: true); } catch (IOException) { }
    }

    private LicenseService CreateLicense(LicenseTier tier) =>
        new(new LicenseStore(new RealFileSystem(), Path.Combine(_temp, "license.json")),
            tierOverride: () => tier, trialEnabled: false);

    private static string[] Paths(int count, string prefix = "f") =>
        Enumerable.Range(0, count).Select(i => $@"C:\x\{prefix}{i}.txt").ToArray();

    [Fact]
    public void Add_DeduplicatesCaseInsensitively()
    {
        var shelf = new ShelfModel(CreateLicense(LicenseTier.Free));

        var first = shelf.Add(new[] { @"C:\a\One.txt", @"c:\a\one.TXT" });
        var second = shelf.Add(new[] { @"C:\a\ONE.txt" });

        Assert.Equal(1, first.Added);
        Assert.Equal(1, first.Duplicates);
        Assert.Equal(0, second.Added);
        Assert.Equal(1, second.Duplicates);
        Assert.Single(shelf.Items);
    }

    [Fact]
    public void Free_StopsAtFiftyItems()
    {
        var shelf = new ShelfModel(CreateLicense(LicenseTier.Free));

        var result = shelf.Add(Paths(60));

        Assert.Equal(50, result.Added);
        Assert.Equal(10, result.OverLimit);
        Assert.True(result.HitLimit);
        Assert.Equal(50, shelf.Count);
    }

    [Fact]
    public void Pro_HasNoLimit()
    {
        var shelf = new ShelfModel(CreateLicense(LicenseTier.Pro));

        var result = shelf.Add(Paths(500));

        Assert.Equal(500, result.Added);
        Assert.False(result.HitLimit);
    }

    [Fact]
    public void Remove_FreesRoomUnderLimit()
    {
        var shelf = new ShelfModel(CreateLicense(LicenseTier.Free));
        shelf.Add(Paths(50));

        Assert.True(shelf.Remove(@"C:\X\F0.txt"));
        var result = shelf.Add(new[] { @"C:\x\new.txt" });

        Assert.Equal(1, result.Added);
        Assert.Equal(50, shelf.Count);
    }

    [Fact]
    public void Clear_EmptiesShelfAndAllowsReAdd()
    {
        var shelf = new ShelfModel(CreateLicense(LicenseTier.Free));
        shelf.Add(Paths(3));

        shelf.Clear();
        var result = shelf.Add(Paths(3));

        Assert.True(shelf.Count == 3 && result.Added == 3);
    }

    [Fact]
    public void LicenseGate_ReportsShelfAsProFeature()
    {
        Assert.False(CreateLicense(LicenseTier.Free).Allows(ProFeature.UnlimitedShelf));
        Assert.True(CreateLicense(LicenseTier.Pro).Allows(ProFeature.UnlimitedShelf));
    }
}
