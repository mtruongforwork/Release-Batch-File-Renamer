namespace BFR.Core.Tests;

using BFR.Core;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using Xunit;

/// <summary>Test cho B1: đổi tên nhanh không giao diện (QuickRenamer, QuickPresets).</summary>
public class QuickRenamerTests
{
    private const string Dir = "/work";
    private const string Journals = "/journals";

    private static (FakeFileSystem fs, QuickRenamer quick, HistoryStore history) Setup(params string[] fileNames)
    {
        var fs = new FakeFileSystem();
        fs.EnsureDirectory(Dir);
        foreach (var n in fileNames) fs.WriteAllText($"{Dir}/{n}", "x");
        var history = new HistoryStore(fs, "/history.json");
        return (fs, new QuickRenamer(fs, Journals, history, () => new DateTime(2026, 11, 5, 10, 0, 0)), history);
    }

    private static IReadOnlyList<FileItem> Files(FakeFileSystem fs, params string[] fileNames) =>
        fileNames.Select((n, i) => new FileItem(
            $"{Dir}/{n}", Dir,
            System.IO.Path.GetFileNameWithoutExtension(n), System.IO.Path.GetExtension(n).TrimStart('.'),
            false, 1, new DateTime(2026, 1, 2), new DateTime(2026, 1, 3), i)).ToList();

    private static Preset Preset(string key)
    {
        Assert.True(QuickPresets.TryGet(key, out _, out var preset), key);
        return preset;
    }

    private static string[] Names(FakeFileSystem fs) =>
        fs.GetFiles(Dir, "*", false).Select(p => p.Replace('\\', '/').Split('/').Last()).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    [Fact]
    public void Number_RenamesInOrder_WritesJournalAndHistory()
    {
        var (fs, quick, history) = Setup("c.txt", "a.txt", "b.txt");

        var r = quick.Run(Files(fs, "a.txt", "b.txt", "c.txt"), Preset("number"));

        Assert.Equal(QuickRenameStatus.Done, r.Status);
        Assert.Equal(3, r.Renamed);
        Assert.Equal(new[] { "001.txt", "002.txt", "003.txt" }, Names(fs));
        Assert.True(fs.FileExists(r.JournalPath!));
        var entry = Assert.Single(history.LoadAll());
        Assert.Equal(r.JournalPath, entry.JournalPath);
        Assert.Equal("completed", entry.Status);
    }

    [Fact]
    public void Undo_RestoresTheOriginalNames()
    {
        var (fs, quick, _) = Setup("alpha.txt", "beta.txt");
        var r = quick.Run(Files(fs, "alpha.txt", "beta.txt"), Preset("number"));

        var undone = quick.Undo(r.JournalPath!);

        Assert.Equal(2, undone);
        Assert.Equal(new[] { "alpha.txt", "beta.txt" }, Names(fs));
    }

    [Fact]
    public void Ascii_RemovesVietnameseDiacritics()
    {
        var (fs, quick, _) = Setup("Bài Tập Số 1.txt");

        var r = quick.Run(Files(fs, "Bài Tập Số 1.txt"), Preset("ascii"));

        Assert.Equal(QuickRenameStatus.Done, r.Status);
        Assert.Equal(new[] { "bai-tap-so-1.txt" }, Names(fs));
    }

    [Fact]
    public void CollisionWithAFileOutsideTheBatch_IsAutoResolved_NotOverwritten()
    {
        var (fs, quick, _) = Setup("a.txt", "001.txt");   // 001.txt đã có sẵn và KHÔNG thuộc lô

        var r = quick.Run(Files(fs, "a.txt"), Preset("number"));

        Assert.Equal(QuickRenameStatus.Done, r.Status);
        Assert.Equal(1, r.AutoResolved);
        Assert.Equal(new[] { "001 (2).txt", "001.txt" }, Names(fs));
    }

    [Fact]
    public void AlreadyInTheTargetState_ReportsNothingToChange_AndWritesNothing()
    {
        var (fs, quick, history) = Setup("001.txt", "002.txt");

        var r = quick.Run(Files(fs, "001.txt", "002.txt"), Preset("number"));

        Assert.Equal(QuickRenameStatus.NothingToChange, r.Status);
        Assert.Null(r.JournalPath);
        Assert.Empty(history.LoadAll());
    }

    [Fact]
    public void InvalidResultingName_BlocksEverything_AndChangesNoFile()
    {
        var (fs, quick, history) = Setup("a.txt", "b.txt");
        var preset = new Preset
        {
            Name = "bad",
            Rules =
            {
                new RuleDefinition
                {
                    Type = "ReplaceDict",
                    Params = { ["pairs"] = JsonSerializer.SerializeToElement("b\tbad:name") }
                }
            }
        };

        var r = quick.Run(Files(fs, "a.txt", "b.txt"), preset);

        Assert.Equal(QuickRenameStatus.Blocked, r.Status);
        Assert.Contains("b.txt", r.Message);
        Assert.Equal(new[] { "a.txt", "b.txt" }, Names(fs));   // a.txt cũng không bị đổi
        Assert.Empty(history.LoadAll());
    }

    [Fact]
    public void EmptySelection_IsNothingToChange()
    {
        var (_, quick, _) = Setup();

        Assert.Equal(QuickRenameStatus.NothingToChange, quick.Run(Array.Empty<FileItem>(), Preset("number")).Status);
    }

    [Fact]
    public void ExecutionFailure_IsReported_AndJournalStillAllowsUndo()
    {
        var (fs, quick, _) = Setup("a.txt", "b.txt");
        fs.FailOnMovePaths.Add($"{Dir}/b.txt");

        var r = quick.Run(Files(fs, "a.txt", "b.txt"), Preset("number"));

        Assert.Equal(QuickRenameStatus.Failed, r.Status);
        Assert.Equal(1, r.Renamed);
        Assert.NotNull(r.Message);

        quick.Undo(r.JournalPath!);
        Assert.Equal(new[] { "a.txt", "b.txt" }, Names(fs));
    }

    [Fact]
    public void QuickPresets_AllResolveToBuiltInPresets_WithUniqueKeys()
    {
        Assert.Equal(QuickPresets.All.Count, QuickPresets.All.Select(q => q.Key).Distinct().Count());
        foreach (var q in QuickPresets.All)
        {
            Assert.True(QuickPresets.TryGet(q.Key, out var found, out var preset), q.Key);
            Assert.Equal(q.PresetName, preset.Name);
            Assert.Same(q, found);
        }
    }

    [Fact]
    public void QuickPresets_HaveTitlesInBothLanguages()
    {
        foreach (var q in QuickPresets.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(q.Title(vietnamese: true)), q.Key);
            Assert.False(string.IsNullOrWhiteSpace(q.Title(vietnamese: false)), q.Key);
        }
        Assert.Equal(QuickPresets.All.Count, QuickPresets.All.Select(q => q.Title(false)).Distinct().Count());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nope")]
    public void UnknownQuickKey_IsRejected(string? key) =>
        Assert.False(QuickPresets.TryGet(key, out _, out _));

    [Fact]
    public void QuickKey_IsCaseInsensitive() =>
        Assert.True(QuickPresets.TryGet(" NUMBER ", out var q, out _) && q.Key == "number");
}
