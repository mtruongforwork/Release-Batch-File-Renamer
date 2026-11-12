namespace BFR.Core.Tests;

using BFR.Core;
using BFR.Core.Licensing;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Xunit;

/// <summary>Test cho B2: đổi tên theo danh sách (ListParser, ListMapRule, ReplaceDictRule) và hạn mức Free/Pro.</summary>
public class ListRuleTests
{
    private static FileItem File(string fileName, int order = 0, string dir = @"C:\a")
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName).TrimStart('.');
        return new FileItem(Path.Combine(dir, fileName), dir, name, ext, false, 1, DateTime.UtcNow, DateTime.UtcNow, order);
    }

    private static RenameState Run(IRule rule, FileItem f, int? index = null)
    {
        var state = new RenameState(f.Name, f.Extension, index ?? f.LoadOrder, index ?? f.LoadOrder, new List<string>());
        return rule.Apply(f, state, new RuleContext(DateTime.UtcNow, "seed"));
    }

    // ------------------------------------------------------------------ ListParser

    [Fact]
    public void Parser_TabSeparated_FromExcelPaste()
    {
        var rows = ListParser.Parse("a\tA1\r\nb\tB2\r\n");

        Assert.Equal(2, rows.Count);
        Assert.Equal(new ListRow("a", "A1"), rows[0]);
        Assert.Equal(new ListRow("b", "B2"), rows[1]);
    }

    [Theory]
    [InlineData("a;A1", ';')]
    [InlineData("a,A1", ',')]
    [InlineData("a\tA1", '\t')]
    [InlineData("just one column", '\0')]
    public void Parser_DetectsDelimiter(string text, char expected) =>
        Assert.Equal(expected, ListParser.DetectDelimiter(text));

    [Fact]
    public void Parser_TabWinsOverSemicolonAndComma() =>
        Assert.Equal('\t', ListParser.DetectDelimiter("a;b,c\td"));

    [Fact]
    public void Parser_QuotedFields_KeepDelimiterAndEscapedQuotes()
    {
        var rows = ListParser.Parse("\"Smith, John\",\"He said \"\"hi\"\"\"");

        var row = Assert.Single(rows);
        Assert.Equal("Smith, John", row.First);
        Assert.Equal("He said \"hi\"", row.Second);
    }

    [Fact]
    public void Parser_SkipsBlankLines_Bom_AndMixedLineEndings()
    {
        var rows = ListParser.Parse("\uFEFFa;1\n\n  \r\nb;2\rc;3");

        Assert.Equal(new[] { "a", "b", "c" }, rows.Select(r => r.First));
    }

    [Fact]
    public void Parser_UnquotedFieldsAreTrimmed_QuotedFieldsKeepSpaces()
    {
        var rows = ListParser.Parse("  a  ;  b  \n\"c \";\" d\"");

        Assert.Equal(new ListRow("a", "b"), rows[0]);
        Assert.Equal(new ListRow("c ", " d"), rows[1]);
    }

    [Fact]
    public void Parser_SingleColumn_HasNoSecondField()
    {
        var rows = ListParser.Parse("one\ntwo");

        Assert.All(rows, r => Assert.Null(r.Second));
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void Parser_MaxRows_Truncates()
    {
        var text = string.Join("\n", Enumerable.Range(1, 100).Select(i => $"f{i}\tn{i}"));

        Assert.Equal(50, ListParser.Parse(text, 50).Count);
        Assert.Equal(100, ListParser.CountRows(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\n")]
    public void Parser_EmptyInput_GivesNoRows(string? text) => Assert.Empty(ListParser.Parse(text));

    // ------------------------------------------------------------------ ListMapRule

    [Fact]
    public void Map_RenamesMatchingNames_CaseInsensitive()
    {
        var rule = new ListMapRule { Mapping = "IMG_001\tHoliday 1\nimg_002\tHoliday 2" };

        Assert.Equal("Holiday 1", Run(rule, File("IMG_001.jpg")).Name);
        Assert.Equal("Holiday 2", Run(rule, File("IMG_002.JPG")).Name);
    }

    [Fact]
    public void Map_CaseSensitive_WhenRequested()
    {
        var rule = new ListMapRule { Mapping = "A\tX", IgnoreCase = false };

        Assert.Equal("a", Run(rule, File("a.txt")).Name);
        Assert.Equal("X", Run(rule, File("A.txt")).Name);
    }

    [Fact]
    public void Map_NoMatch_LeavesNameAndExtension()
    {
        var rule = new ListMapRule { Mapping = "zzz\tyyy" };
        var s = Run(rule, File("keep.me"));

        Assert.Equal("keep", s.Name);
        Assert.Equal("me", s.Extension);
    }

    [Fact]
    public void Map_NameMode_KeepsExtension()
    {
        var s = Run(new ListMapRule { Mapping = "a\tb" }, File("a.png"));

        Assert.Equal("b", s.Name);
        Assert.Equal("png", s.Extension);
    }

    [Fact]
    public void Map_FileNameMode_MatchesWithExtension_AndCanSetExtension()
    {
        var rule = new ListMapRule { MatchBy = ListMapRule.MatchTarget.FileName, Mapping = "a.txt\tb.md" };

        var hit = Run(rule, File("a.txt"));
        var miss = Run(rule, File("a.log"));

        Assert.Equal(("b", "md"), (hit.Name, hit.Extension));
        Assert.Equal(("a", "log"), (miss.Name, miss.Extension));
    }

    [Fact]
    public void Map_DuplicateKeys_FirstRowWins()
    {
        var s = Run(new ListMapRule { Mapping = "a\tfirst\na\tsecond" }, File("a.txt"));

        Assert.Equal("first", s.Name);
    }

    [Fact]
    public void Map_ChangingMapping_IsPickedUp()
    {
        var rule = new ListMapRule { Mapping = "a\tone" };
        Assert.Equal("one", Run(rule, File("a.txt")).Name);

        rule.Mapping = "a\ttwo";

        Assert.Equal("two", Run(rule, File("a.txt")).Name);
    }

    [Fact]
    public void Map_MaxRows_IgnoresRowsBeyondTheLimit()
    {
        var rule = new ListMapRule { Mapping = "a\t1\nb\t2\nc\t3", MaxRows = 2 };

        Assert.Equal("2", Run(rule, File("b.txt")).Name);
        Assert.Equal("c", Run(rule, File("c.txt")).Name);   // dòng thứ 3 bị bỏ
    }

    [Fact]
    public void ByOrder_AssignsRowsByFileIndex_AndLeavesTheRest()
    {
        var rule = new ListMapRule { Mode = ListMapRule.ListMode.ByOrder, Mapping = "first\nsecond" };

        Assert.Equal("first", Run(rule, File("x.txt"), 0).Name);
        Assert.Equal("second", Run(rule, File("y.txt"), 1).Name);
        Assert.Equal("z", Run(rule, File("z.txt"), 2).Name);
    }

    [Fact]
    public void ByOrder_WorksAcrossAWholePipelineRun()
    {
        var pipeline = new RulePipeline();
        pipeline.Add(new ListMapRule { Mode = ListMapRule.ListMode.ByOrder, Mapping = "A\nB\nC" });
        var files = new List<FileItem> { File("x.txt", 0), File("y.txt", 1), File("z.txt", 2) };

        var result = pipeline.Run(files, new RuleContext(DateTime.UtcNow, "seed"));

        Assert.Equal(new[] { "A.txt", "B.txt", "C.txt" }, result.Items.Select(i => i.NewName));
    }

    // ------------------------------------------------------------------ ReplaceDictRule

    [Fact]
    public void Dict_ReplacesAllPairsInOnePass()
    {
        var rule = new ReplaceDictRule { Pairs = "foo\tbar\nbaz\tqux" };

        Assert.Equal("bar_qux", Run(rule, File("foo_baz.txt")).Name);
    }

    [Fact]
    public void Dict_PairsAreSimultaneous_NotChained()
    {
        var rule = new ReplaceDictRule { Pairs = "a\tb\nb\tc" };

        Assert.Equal("bc", Run(rule, File("ab.txt")).Name);   // không thành "cc"
    }

    [Fact]
    public void Dict_LongerKeyMatchesFirst()
    {
        var rule = new ReplaceDictRule { Pairs = "a\tY\nab\tX" };

        Assert.Equal("XX", Run(rule, File("abab.txt")).Name);
    }

    [Fact]
    public void Dict_EmptyReplacement_RemovesText()
    {
        var rule = new ReplaceDictRule { Pairs = "\"copy of \";" };   // trường trong ngoặc kép giữ dấu cách; vế thay rỗng

        Assert.Equal("report", Run(rule, File("copy of report.txt")).Name);
    }

    [Fact]
    public void Dict_CaseSensitivity_IsRespected()
    {
        var insensitive = new ReplaceDictRule { Pairs = "x\ty" };
        var sensitive = new ReplaceDictRule { Pairs = "x\ty", IgnoreCase = false };

        Assert.Equal("yy", Run(insensitive, File("xX.txt")).Name);
        Assert.Equal("yX", Run(sensitive, File("xX.txt")).Name);
    }

    [Fact]
    public void Dict_RegexCharactersInKeys_AreTreatedLiterally()
    {
        var rule = new ReplaceDictRule { Pairs = "(1)\t_one\n[x]\t_x" };

        Assert.Equal("a_one_x", Run(rule, File("a(1)[x].txt")).Name);
    }

    [Fact]
    public void Dict_EmptyPairs_IsNoOp() =>
        Assert.Equal("same", Run(new ReplaceDictRule(), File("same.txt")).Name);

    [Fact]
    public void Dict_MaxRows_LimitsPairs()
    {
        var rule = new ReplaceDictRule { Pairs = "a\t1\nb\t2", MaxRows = 1 };

        Assert.Equal("1b", Run(rule, File("ab.txt")).Name);
    }

    // ------------------------------------------------------------------ RuleFactory / Preset / Free-Pro

    private static RuleDefinition Def(string type, params (string key, object value)[] p)
    {
        var d = new RuleDefinition { Type = type };
        foreach (var (k, v) in p) d.Params[k] = JsonSerializer.SerializeToElement(v);
        return d;
    }

    [Fact]
    public void Factory_BuildsListMapAndReplaceDict_FromPreset()
    {
        var preset = new Preset
        {
            Rules =
            {
                Def("ListMap", ("mapping", "a\tb"), ("mode", "Map"), ("matchBy", "Name")),
                Def("ReplaceDict", ("pairs", "b\tc"))
            }
        };

        var pipeline = new PresetCompiler().Compile(preset);
        var r = pipeline.Run(new[] { File("a.txt") }, new RuleContext(DateTime.UtcNow, "seed"));

        Assert.Equal("c.txt", r.Items[0].NewName);   // a → b (ListMap) rồi b → c (ReplaceDict)
    }

    [Fact]
    public void Compile_AppliesTheListRowLimit()
    {
        var mapping = string.Join("\n", Enumerable.Range(1, 60).Select(i => $"f{i}\tn{i}"));
        var preset = new Preset { Rules = { Def("ListMap", ("mapping", mapping)) } };
        var ctx = new RuleContext(DateTime.UtcNow, "seed");

        var free = new PresetCompiler().Compile(preset, maxListRows: 50).Run(new[] { File("f50.txt"), File("f51.txt") }, ctx);
        var pro = new PresetCompiler().Compile(preset).Run(new[] { File("f50.txt"), File("f51.txt") }, ctx);

        Assert.Equal(new[] { "n50.txt", "f51.txt" }, free.Items.Select(i => i.NewName));   // dòng 51 bị bỏ
        Assert.Equal(new[] { "n50.txt", "n51.txt" }, pro.Items.Select(i => i.NewName));
    }

    [Fact]
    public void Limits_FreeHas50ListRowsAndNoCsvImport_ProIsUnlimited()
    {
        Assert.Equal(50, LicenseLimits.Free.MaxListRows);
        Assert.False(LicenseLimits.Free.AllowListCsvImport);
        Assert.Equal(int.MaxValue, LicenseLimits.Pro.MaxListRows);
        Assert.True(LicenseLimits.Pro.AllowListCsvImport);
    }

    [Theory]
    [InlineData(LicenseTier.Free, false)]
    [InlineData(LicenseTier.Pro, true)]
    public void LicenseGate_ListFeatures(LicenseTier tier, bool allowed)
    {
        var service = new LicenseService(new LicenseStore(new FakeFileSystem(), "license.json"),
            tierOverride: () => tier, trialEnabled: false);

        Assert.Equal(allowed, service.Allows(ProFeature.UnlimitedListRows));
        Assert.Equal(allowed, service.Allows(ProFeature.ListCsvImport));
        Assert.Equal(tier == LicenseTier.Pro ? int.MaxValue : 50, service.MaxListRows);
    }
}
