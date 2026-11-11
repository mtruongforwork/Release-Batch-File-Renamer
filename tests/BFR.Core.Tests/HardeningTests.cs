namespace BFR.Core.Tests;

using BFR.Core;
using BFR.Core.Updates;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Xunit;

public class GlobMatcherTests
{
    [Theory]
    [InlineData("photo.jpg", "*.jpg", true)]
    [InlineData("PHOTO.JPG", "*.jpg", true)]
    [InlineData("photo.png", "*.jpg", false)]
    [InlineData("a1b", "a?b", true)]
    [InlineData("ab", "a?b", false)]
    [InlineData("anything", "*", true)]
    [InlineData("", "*", true)]
    [InlineData("", "", true)]
    [InlineData("abc", "", false)]
    [InlineData("IMG_001.jpg", "img_*.j*g", true)]
    [InlineData("tmp~file", "tmp~*", true)]
    [InlineData("a.b", "a.b", true)]
    [InlineData("axb", "a.b", false)]   // dấu chấm là ký tự thường, không phải wildcard Regex
    [InlineData("abc", "a(b)c", false)]
    public void IsMatch_Cases(string name, string glob, bool expected)
    {
        Assert.Equal(expected, GlobMatcher.IsMatch(name, glob));
    }

    [Fact]
    public void PathologicalPattern_DoesNotBacktrack()
    {
        var name = new string('a', 5_000);
        var sw = Stopwatch.StartNew();
        Assert.False(GlobMatcher.IsMatch(name, "*a*a*a*a*a*a*a*a*a*a*b"));
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1), $"mất {sw.Elapsed.TotalMilliseconds:F0} ms");
    }
}

public class RegexTimeoutTests
{
    private static FileItem File1(string name) =>
        new($@"C:\d\{name}.txt", @"C:\d", name, "txt", false, 1, DateTime.UtcNow, DateTime.UtcNow, 0);

    [Fact]
    public void CatastrophicUserRegex_IsCutOff_AndKeepsOriginalName()
    {
        var pipeline = new RulePipeline();
        pipeline.Add(new FindReplaceRule { Pattern = "(a+)+$", Replacement = "X", IsRegex = true });

        var name = new string('a', 40) + "!";
        var sw = Stopwatch.StartNew();
        var result = pipeline.Run(new List<FileItem> { File1(name) }, new RuleContext(DateTime.Now, "t"));
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"mất {sw.Elapsed.TotalSeconds:F1}s");
        Assert.Equal(name + ".txt", result.Items[0].NewName);
    }

    [Fact]
    public void ReplaceDict_StillWorks_WithTimeoutApplied()
    {
        var rule = new ReplaceDictRule { Pairs = "cat\tdog" };
        var pipeline = new RulePipeline();
        pipeline.Add(rule);
        var result = pipeline.Run(new List<FileItem> { File1("my cat") }, new RuleContext(DateTime.Now, "t"));
        Assert.Equal("my dog.txt", result.Items[0].NewName);
    }

    [Fact]
    public void Limits_AreUniform()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(RegexLimits.TimeoutMilliseconds), RegexLimits.Timeout);
    }
}

public class PresetValidatorTests
{
    private static string Json(string name = "Hợp lệ", int rules = 1, string? extra = null)
    {
        var sb = new StringBuilder();
        sb.Append("{\"schemaVersion\":\"1.0.0\",\"name\":").Append(JsonSerializer.Serialize(name)).Append(",\"rules\":[");
        for (var i = 0; i < rules; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("{\"type\":\"Case\",\"params\":{\"mode\":\"Lower\"}}");
        }
        sb.Append(']');
        if (extra is not null) sb.Append(',').Append(extra);
        sb.Append('}');
        return sb.ToString();
    }

    [Fact]
    public void Parse_AcceptsNormalPreset()
    {
        var p = PresetValidator.Parse(Json());
        Assert.Equal("Hợp lệ", p.Name);
        Assert.Single(p.Rules);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    public void Parse_RejectsGarbage(string? json)
    {
        Assert.Throws<InvalidDataException>(() => PresetValidator.Parse(json));
    }

    [Fact]
    public void Parse_RejectsBadNames()
    {
        Assert.Throws<InvalidDataException>(() => PresetValidator.Parse(Json(name: "")));
        Assert.Throws<InvalidDataException>(() => PresetValidator.Parse(Json(name: new string('n', PresetValidator.MaxNameLength + 1))));
        Assert.Throws<InvalidDataException>(() => PresetValidator.Parse(Json(name: "bad\u0007name")));
    }

    [Fact]
    public void Parse_RejectsTooManyRules()
    {
        Assert.Throws<InvalidDataException>(() => PresetValidator.Parse(Json(rules: PresetValidator.MaxRules + 1)));
        Assert.Equal(PresetValidator.MaxRules, PresetValidator.Parse(Json(rules: PresetValidator.MaxRules)).Rules.Count);
    }

    [Fact]
    public void Parse_RejectsDeeplyNestedJson_WithoutStackOverflow()
    {
        var deep = new string('[', 5000) + new string(']', 5000);
        var json = Json(extra: "\"x\":" + deep);
        Assert.Throws<InvalidDataException>(() => PresetValidator.Parse(json));
    }

    [Fact]
    public void Parse_RejectsHugeParamString()
    {
        var huge = new string('x', PresetValidator.MaxParamStringLength + 1);
        var json = "{\"name\":\"p\",\"rules\":[{\"type\":\"Find\",\"params\":{\"pattern\":\"" + huge + "\"}}]}";
        Assert.Throws<InvalidDataException>(() => PresetValidator.Parse(json));
    }

    [Fact]
    public void ReadFile_RejectsOversizeFile_BeforeReadingIt()
    {
        var fs = new FakeFileSystem();
        fs.WriteAllText(@"C:\p\big.bfrpreset", new string(' ', PresetValidator.MaxFileBytes + 10));
        Assert.Throws<InvalidDataException>(() => PresetValidator.ReadFile(fs, @"C:\p\big.bfrpreset"));
    }

    [Fact]
    public void Store_LoadAll_SkipsInvalidAndKeepsValid()
    {
        var fs = new FakeFileSystem();
        var store = new PresetStore(fs, @"C:\presets");
        fs.EnsureDirectory(@"C:\presets");
        fs.WriteAllText(@"C:\presets\good.bfrpreset", Json("Tốt"));
        fs.WriteAllText(@"C:\presets\bad.bfrpreset", "{ not json");
        fs.WriteAllText(@"C:\presets\noname.bfrpreset", Json(""));

        var all = store.LoadAll();
        Assert.Single(all);
        Assert.Equal("Tốt", all[0].Name);
    }

    [Fact]
    public void Store_FromJson_RoundTripsASavedPreset()
    {
        var store = new PresetStore(new FakeFileSystem(), @"C:\presets");
        var preset = new Preset { Name = "Vòng lặp" };
        preset.Rules.Add(new RuleDefinition { Type = "Case" });
        var back = store.FromJson(store.ToJson(preset));
        Assert.Equal("Vòng lặp", back.Name);
        Assert.Single(back.Rules);
    }
}

public class UpdateFeedPolicyTests
{
    [Theory]
    [InlineData("https://api.github.com/repos/o/r/releases/latest", true)]
    [InlineData("HTTPS://API.GITHUB.COM/repos/o/r/releases/latest", true)]
    [InlineData("http://api.github.com/repos/o/r/releases/latest", false)]
    [InlineData("https://evil.example.com/repos/o/r/releases/latest", false)]
    [InlineData("https://api.github.com.evil.com/x", false)]
    [InlineData("https://user:pw@api.github.com/x", false)]
    [InlineData("https://api.github.com:8443/x", false)]
    [InlineData("file:///C:/x", false)]
    public void IsAllowedFeed_Cases(string url, bool expected)
    {
        Assert.Equal(expected, UpdateFeedPolicy.IsAllowedFeed(new Uri(url)));
    }

    [Theory]
    [InlineData("https://github.com/o/r/releases/tag/v2.0.0", true)]
    [InlineData("http://github.com/o/r", false)]
    [InlineData("https://evil.com/o/r", false)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    [InlineData("ms-settings:startupapps", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void SafeReleaseUrl_Cases(string? url, bool ok)
    {
        Assert.Equal(ok, UpdateFeedPolicy.SafeReleaseUrl(url) is not null);
    }

    [Fact]
    public void Feed_RefusesDisallowedUri()
    {
        Assert.Throws<ArgumentException>(() => new GitHubReleasesUpdateFeed(new Uri("http://api.github.com/x")));
        Assert.Throws<ArgumentException>(() => new GitHubReleasesUpdateFeed(new Uri("https://example.com/x")));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public HttpRequestMessage? Last { get; private set; }
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            var response = _respond(request);
            response.RequestMessage ??= request;
            return Task.FromResult(response);
        }
    }

    private static readonly Uri Api = new("https://api.github.com/repos/o/r/releases/latest");

    private static HttpResponseMessage Ok(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Feed_SendsVersionedUserAgent_AndKeepsSafeUrl()
    {
        var handler = new StubHandler(_ => Ok("{\"tag_name\":\"v9.9.9\",\"html_url\":\"https://github.com/o/r/releases/tag/v9.9.9\",\"body\":\"x\"}"));
        using var feed = new GitHubReleasesUpdateFeed(Api, new HttpClient(handler));

        var result = await feed.FetchAsync();

        Assert.True(result.Success);
        Assert.Equal("https://github.com/o/r/releases/tag/v9.9.9", result.ReleaseUrl);
        Assert.Contains(AppVersion.Current, handler.Last!.Headers.UserAgent.ToString());
        Assert.DoesNotContain("localhost", handler.Last.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task Feed_DropsUnsafeReleaseUrl()
    {
        var handler = new StubHandler(_ => Ok("{\"tag_name\":\"v9.9.9\",\"html_url\":\"file:///C:/Windows/System32/calc.exe\"}"));
        using var feed = new GitHubReleasesUpdateFeed(Api, new HttpClient(handler));

        var result = await feed.FetchAsync();

        Assert.True(result.Success);
        Assert.Null(result.ReleaseUrl);
    }

    [Fact]
    public async Task Feed_RejectsOversizeResponse()
    {
        var big = "{\"tag_name\":\"v1\",\"body\":\"" + new string('x', UpdateFeedPolicy.MaxResponseBytes + 10) + "\"}";
        var handler = new StubHandler(_ => Ok(big));
        using var feed = new GitHubReleasesUpdateFeed(Api, new HttpClient(handler));

        var result = await feed.FetchAsync();

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Feed_RejectsRedirectToDisallowedHost()
    {
        var handler = new StubHandler(req =>
        {
            var response = Ok("{\"tag_name\":\"v9.9.9\"}");
            response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://evil.example.com/latest");
            return response;
        });
        using var feed = new GitHubReleasesUpdateFeed(Api, new HttpClient(handler));

        var result = await feed.FetchAsync();

        Assert.False(result.Success);
    }
}
