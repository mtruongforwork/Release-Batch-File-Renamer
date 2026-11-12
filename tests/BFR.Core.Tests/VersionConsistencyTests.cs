namespace BFR.Core.Tests;

using BFR.Core;

using System;
using System.IO;
using System.Text.RegularExpressions;

using Xunit;

/// <summary>Canh để số phiên bản không lệch giữa mã nguồn, manifest MSIX, script đóng gói và release notes.</summary>
public class VersionConsistencyTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BatchFileRenamer.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Không tìm thấy thư mục gốc repo.");
    }

    [Fact]
    public void Assembly_MatchesAppVersion()
    {
        var v = typeof(AppVersion).Assembly.GetName().Version!;

        Assert.Equal(AppVersion.Current, $"{v.Major}.{v.Minor}.{v.Build}");
    }

    [Fact]
    public void Manifest_MatchesAppVersion()
    {
        var xml = File.ReadAllText(Path.Combine(RepoRoot(), "packaging", "msix", "AppxManifest.xml"));
        var m = Regex.Match(xml, "<Identity[^>]*?Version=\"([^\"]+)\"", RegexOptions.Singleline);

        Assert.True(m.Success);
        Assert.Equal(AppVersion.Current + ".0", m.Groups[1].Value);
    }

    [Fact]
    public void BuildScript_MatchesAppVersion()
    {
        var ps = File.ReadAllText(Path.Combine(RepoRoot(), "packaging", "msix", "build-msix.ps1"));
        var m = Regex.Match(ps, @"\$version\s*=\s*""([^""]+)""");

        Assert.True(m.Success);
        Assert.Equal(AppVersion.Current, m.Groups[1].Value);
    }

    [Fact]
    public void ReleaseNotes_ExistForCurrentVersion()
    {
        var path = Path.Combine(RepoRoot(), "docs", $"RELEASE_NOTES_v{AppVersion.Current}.md");

        Assert.True(File.Exists(path), $"Thiếu {path}");
        Assert.Contains($"`{AppVersion.Current}.0`", File.ReadAllText(path));
    }
}
