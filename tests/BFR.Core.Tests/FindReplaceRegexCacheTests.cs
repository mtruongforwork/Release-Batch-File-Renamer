namespace BFR.Core.Tests;

using BFR.Core;

using System;
using System.Collections.Generic;
using System.Linq;

using Xunit;

/// <summary>Regex của quy tắc Tìm và thay phải được dựng một lần cho cả lô, không dựng lại cho từng tệp.</summary>
public class FindReplaceRegexCacheTests
{
    private static List<FileItem> Files(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new FileItem($@"C:\d\file{i}.txt", @"C:\d", $"file{i}", "txt", false, 1, DateTime.UtcNow, DateTime.UtcNow, i))
            .ToList();

    [Fact]
    public void RegexOverManyFiles_IsFast_AndCorrect()
    {
        var pipeline = new RulePipeline();
        pipeline.Add(new FindReplaceRule { Pattern = "file([0-9]+)", Replacement = "doc_$1", IsRegex = true });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = pipeline.Run(Files(20_000), new RuleContext(DateTime.Now, "t"));
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"mất {sw.Elapsed.TotalSeconds:F1}s (dựng Regex lại cho từng tệp)");
        Assert.Equal("doc_19999.txt", result.Items[19_999].NewName);
    }

    [Fact]
    public void ChangingThePatternBetweenRuns_UsesTheNewPattern()
    {
        var rule = new FindReplaceRule { Pattern = "a", Replacement = "X", IsRegex = true };
        var pipeline = new RulePipeline();
        pipeline.Add(rule);
        var files = new List<FileItem> { new(@"C:\d\abc.txt", @"C:\d", "abc", "txt", false, 1, DateTime.UtcNow, DateTime.UtcNow, 0) };

        Assert.Equal("Xbc.txt", pipeline.Run(files, new RuleContext(DateTime.Now, "t")).Items[0].NewName);

        rule.Pattern = "b";
        Assert.Equal("aXc.txt", pipeline.Run(files, new RuleContext(DateTime.Now, "t")).Items[0].NewName);

        rule.Pattern = "B";
        rule.IgnoreCase = true;
        Assert.Equal("aXc.txt", pipeline.Run(files, new RuleContext(DateTime.Now, "t")).Items[0].NewName);
    }
}
