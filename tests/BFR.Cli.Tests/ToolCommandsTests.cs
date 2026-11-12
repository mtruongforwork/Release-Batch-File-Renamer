namespace BFR.Cli.Tests;

using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BFR.Cli;
using BFR.Core;
using BFR.Core.Licensing;
using BFR.Media;
using Xunit;

public sealed class ToolCommandsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bfr-cli-" + Guid.NewGuid().ToString("N"));
    private readonly RealFileSystem _fs = new();
    private readonly StringWriter _out = new();
    private readonly StringWriter _err = new();

    public ToolCommandsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(_root, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string P(params string[] parts) => Path.Combine(new[] { _root }.Concat(parts).ToArray());

    private string Write(string relative, string content = "x")
    {
        var path = P(relative.Split('/'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private ToolCommands Tools(LicenseTier tier = LicenseTier.Pro, string answer = "", bool withImages = false)
    {
        var license = new LicenseService(new LicenseStore(_fs, P("license.json")), tierOverride: () => tier, trialEnabled: false);
        return new ToolCommands(_fs, license, _out, _err, new StringReader(answer), P("_journal"), P("_tmp"),
            new HistoryStore(_fs, P("history.json")), "test",
            new CompositeMetadataProvider(new ExifMetadataReader(), new MediaMetadataReader(), new AudioTagReader()),
            withImages ? new WicImageProcessor() : null, P("_quarantine"));
    }

    private int Run(params string[] args) => Tools().Run(args);

    [Fact]
    public void FreeTier_CannotUseAnyTool()
    {
        var code = Tools(LicenseTier.Free).Run(new[] { "organize", _root, "--template", "{ext}" });

        Assert.Equal(1, code);
        Assert.Contains("Pro", _err.ToString());
    }

    [Fact]
    public void Organize_PreviewsThenAppliesAndUndoRestores()
    {
        Write("in/a.txt");
        Write("in/b.png");

        Assert.Equal(0, Run("organize", P("in", "a.txt"), P("in", "b.png"), "--template", "{type}", "--dry-run"));
        Assert.True(File.Exists(P("in", "a.txt")));

        Assert.Equal(0, Run("organize", P("in", "a.txt"), P("in", "b.png"), "--template", "{ext.upper}", "--yes"));
        Assert.True(File.Exists(P("in", "TXT", "a.txt")));
        Assert.True(File.Exists(P("in", "PNG", "b.png")));

        // Lệnh ghi đã vào lịch sử: undo (dùng chung với rename) hoàn tác được.
        var entry = new HistoryStore(_fs, P("history.json")).LoadAll().Last();
        Assert.Equal("organize", entry.Preset);
        new UndoService(_fs).UndoJournal(entry.JournalPath);
        Assert.True(File.Exists(P("in", "a.txt")));
        Assert.False(Directory.Exists(P("in", "TXT")));
    }

    [Fact]
    public void Organize_RejectsBadTemplateAndUnknownOptions_WithoutChangingAnything()
    {
        var file = Write("in/a.txt");

        Assert.Equal(2, Run("organize", file, "--template", "{nope}", "--yes"));
        Assert.Equal(1, Run("organize", file, "--template", "{ext}", "--bogus"));
        Assert.Equal(1, Run("organize", file));
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Confirmation_DefaultsToNo()
    {
        var file = Write("in/a.txt");

        var code = Tools(answer: "n").Run(new[] { "organize", file, "--template", "{ext}" });

        Assert.Equal(0, code);
        Assert.True(File.Exists(file));
        Assert.Contains("Đã hủy", _out.ToString());
    }

    [Fact]
    public void FlattenAndEmptyDirs_Work()
    {
        Write("tree/a/x.txt");
        Write("tree/a/b/y.txt");
        Directory.CreateDirectory(P("tree", "empty", "inner"));
        Write("tree/junk/Thumbs.db");

        Assert.Equal(0, Run("empty-dirs", P("tree"), "--yes"));
        Assert.False(Directory.Exists(P("tree", "empty")));
        Assert.False(Directory.Exists(P("tree", "junk")));
        Assert.True(Directory.Exists(P("tree", "a")));

        Assert.Equal(0, Run("flatten", P("tree"), "--yes"));
        Assert.True(File.Exists(P("tree", "x.txt")));
        Assert.True(File.Exists(P("tree", "y.txt")));
        Assert.False(Directory.Exists(P("tree", "a")));
    }

    [Fact]
    public void Touch_ShiftAndFixed_AndAttrib()
    {
        var file = Write("t/a.txt");
        File.SetLastWriteTimeUtc(file, new DateTime(2020, 1, 1, 10, 0, 0, DateTimeKind.Utc));

        Assert.Equal(0, Run("touch", file, "--source", "shift", "--offset", "+2h", "--yes"));
        Assert.Equal(new DateTime(2020, 1, 1, 12, 0, 0, DateTimeKind.Utc), File.GetLastWriteTimeUtc(file));

        Assert.Equal(0, Run("touch", file, "--source", "fixed", "--value", "2019-05-06T07:08:09", "--set", "modified,created", "--yes"));
        Assert.Equal(new DateTime(2019, 5, 6, 7, 8, 9, DateTimeKind.Local).ToUniversalTime(), File.GetLastWriteTimeUtc(file));

        Assert.Equal(1, Run("touch", file, "--source", "fixed", "--yes")); // thiếu --value
        Assert.Equal(1, Run("touch", file, "--source", "shift", "--offset", "soon"));

        Assert.Equal(0, Run("attrib", file, "--set", "readonly,hidden", "--yes"));
        Assert.Equal(FileAttributes.ReadOnly | FileAttributes.Hidden, File.GetAttributes(file) & (FileAttributes.ReadOnly | FileAttributes.Hidden));
        Assert.Equal(1, Run("attrib", file, "--set", "system", "--yes"));
    }

    [Fact]
    public void Dupes_ListsThenQuarantinesOnApply_KeepingOneCopy()
    {
        Write("d/one.txt", "same content");
        Write("d/two.txt", "same content");
        Write("d/three.txt", "same content");
        Write("d/other.txt", "different");

        Assert.Equal(0, Run("dupes", P("d"), "--recursive"));
        Assert.Equal(4, Directory.GetFiles(P("d")).Length); // chỉ xem: không đổi gì

        Assert.Equal(0, Run("dupes", P("d"), "--keep", "shortest", "--apply", "--yes"));
        Assert.Equal(2, Directory.GetFiles(P("d")).Length);
        Assert.True(File.Exists(P("d", "one.txt")) || File.Exists(P("d", "two.txt")));
        Assert.True(File.Exists(P("d", "other.txt")));
        Assert.NotEmpty(Directory.GetFiles(P("_quarantine"), "*", SearchOption.AllDirectories)); // không xoá vĩnh viễn
    }

    [Fact]
    public void HashThenVerify_DetectsTampering()
    {
        Write("h/a.txt", "alpha");
        Write("h/sub/b.txt", "bravo");
        var sums = P("h", "sums.sha256");

        Assert.Equal(0, Run("hash", P("h", "a.txt"), P("h", "sub", "b.txt"), "--out", sums));
        Assert.Equal(0, Run("verify", sums));

        File.WriteAllText(P("h", "a.txt"), "tampered");
        Assert.Equal(2, Run("verify", sums));
        Assert.Contains("Mismatch", _out.ToString());
    }

    [Fact]
    public void Analyze_JsonIsValidAndComplete()
    {
        Write("an/a.txt", new string('a', 100));
        Write("an/sub/b.bin", new string('b', 500));

        Assert.Equal(0, Run("analyze", P("an"), "--format", "json"));

        using var doc = JsonDocument.Parse(_out.ToString());
        Assert.Equal(2, doc.RootElement.GetProperty("files").GetInt32());
        Assert.Equal(600, doc.RootElement.GetProperty("totalBytes").GetInt64());
    }

    [Fact]
    public void ZipThenUnzip_RoundTrips_AndMaliciousZipIsRejected()
    {
        Write("z/a.txt", "alpha");
        Write("z/sub/b.txt", "bravo");

        Assert.Equal(0, Run("zip", P("z", "a.txt"), P("z", "sub", "b.txt"), "--name", "bundle", "--yes"));
        Assert.True(File.Exists(P("z", "bundle.zip")));

        Directory.CreateDirectory(P("out"));
        Assert.Equal(0, Run("unzip", P("z", "bundle.zip"), "--to", P("out"), "--yes"));
        Assert.Equal("alpha", File.ReadAllText(P("out", "bundle", "a.txt")));

        var evil = P("evil.zip");
        using (var zip = ZipFile.Open(evil, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("../escaped.txt");
            using var w = new StreamWriter(entry.Open());
            w.Write("evil");
        }
        Assert.Equal(2, Run("unzip", evil, "--to", P("out"), "--yes"));
        Assert.False(File.Exists(P("escaped.txt")));
        Assert.Contains("nguy hiểm", _err.ToString());
    }

    [Fact]
    public void Workflow_RunsFromJsonFile()
    {
        Write("w/Photo.JPG");
        var definition = new WorkflowDefinition("test", new List<WorkflowStep>
        {
            new RenameStep(BuiltInPresets.All.First(p => p.Name == "Chữ thường toàn bộ")),
            new OrganizeStep("{ext.upper}")
        });
        File.WriteAllText(P("wf.json"), WorkflowSerializer.Serialize(definition));

        Assert.Equal(0, Run("workflow", "run", P("wf.json"), P("w", "Photo.JPG"), "--yes"));
        Assert.True(File.Exists(P("w", "JPG", "photo.jpg")));

        Assert.Equal(1, Run("workflow", "run", P("wf.json")));
    }

    [Fact]
    public void StripGps_LeavesCleanFilesAlone()
    {
        var jpg = Write("g/a.jpg", "not really a jpeg");

        Assert.Equal(0, Run("strip-gps", jpg, "--yes"));
        Assert.Equal("not really a jpeg", File.ReadAllText(jpg));
        Assert.Contains("không hỗ trợ", _out.ToString());
    }

    [Fact]
    public void Image_ResizesWithoutTouchingTheOriginal()
    {
        var src = P("img", "a.png");
        Directory.CreateDirectory(P("img"));
        var pixels = new byte[40 * 20 * 3];
        var bmp = System.Windows.Media.Imaging.BitmapSource.Create(40, 20, 96, 96, System.Windows.Media.PixelFormats.Bgr24, null, pixels, 40 * 3);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using (var fs = File.Create(src)) enc.Save(fs);
        var originalLength = new FileInfo(src).Length;

        Assert.Equal(0, Tools(withImages: true).Run(new[] { "image", src, "--long-edge", "20", "--yes" }));

        Assert.Equal(originalLength, new FileInfo(src).Length);
        var output = P("img", "a_edit.png");
        Assert.True(File.Exists(output));
        using var stream = File.OpenRead(output);
        var frame = System.Windows.Media.Imaging.BitmapDecoder.Create(stream, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0];
        Assert.Equal((20, 10), (frame.PixelWidth, frame.PixelHeight));
    }

    [Fact]
    public void Quarantine_StatusAndPurgeNeverGoBelowRetention()
    {
        Assert.Equal(0, Run("quarantine", "status"));
        Assert.Contains("Khu cách ly", _out.ToString());

        Assert.Equal(0, Run("quarantine", "purge", "--days", "0"));
        Assert.Contains($"{QuarantineStore.DefaultRetentionDays} ngày", _out.ToString());
        Assert.Equal(1, Run("quarantine", "wat"));
    }

    [Fact]
    public void ArgParser_HandlesInlineValuesDoubleDashAndErrors()
    {
        var a = CliArgs.Parse(new[] { "x", "--to=dir", "--yes", "--", "--literal" }, new[] { "--to" }, Array.Empty<string>());
        Assert.Null(a.Error);
        Assert.Equal("dir", a.Get("--to"));
        Assert.True(a.Has("--yes"));
        Assert.Equal(new[] { "x", "--literal" }, a.Positional);

        Assert.NotNull(CliArgs.Parse(new[] { "--to" }, new[] { "--to" }, Array.Empty<string>()).Error);
        Assert.NotNull(CliArgs.Parse(new[] { "--nope" }, Array.Empty<string>(), Array.Empty<string>()).Error);
    }
}
