namespace BFR.Media.Tests;

using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BFR.Core;
using BFR.Media;
using Xunit;

public class WicImageProcessorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bfr-media-" + Guid.NewGuid().ToString("N"));

    public WicImageProcessorTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static BitmapSource Pixels(int w, int h, bool alpha = false)
    {
        var format = alpha ? PixelFormats.Bgra32 : PixelFormats.Bgr24;
        var bpp = alpha ? 4 : 3;
        var data = new byte[w * h * bpp];
        for (var i = 0; i < data.Length; i += bpp)
        {
            data[i] = 200; data[i + 1] = 100; data[i + 2] = 50;
            if (alpha) data[i + 3] = 0; // hoàn toàn trong suốt
        }
        var bmp = BitmapSource.Create(w, h, 96, 96, format, null, data, w * bpp);
        bmp.Freeze();
        return bmp;
    }

    private string WriteJpeg(string name, int w, int h, ushort? orientation, string? dateTaken = null)
    {
        var path = Path.Combine(_dir, name);
        var metadata = new BitmapMetadata("jpg");
        if (orientation is { } o) metadata.SetQuery("/app1/ifd/{ushort=274}", o);
        if (dateTaken is not null) metadata.DateTaken = dateTaken;
        var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
        encoder.Frames.Add(BitmapFrame.Create(Pixels(w, h), null, metadata, null));
        using var fs = File.Create(path);
        encoder.Save(fs);
        return path;
    }

    private static (int w, int h, int orientation, string? date) Read(string path)
    {
        using var fs = File.OpenRead(path);
        var frame = BitmapDecoder.Create(fs, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        var meta = frame.Metadata as BitmapMetadata;
        var o = 1;
        if (meta is not null && meta.ContainsQuery("/app1/ifd/{ushort=274}") && meta.GetQuery("/app1/ifd/{ushort=274}") is ushort v) o = v;
        return (frame.PixelWidth, frame.PixelHeight, o, meta?.DateTaken);
    }

    [Fact]
    public void Resize_LongEdge_KeepsAspectAndWritesJpeg()
    {
        var src = WriteJpeg("a.jpg", 400, 200, null);
        var dst = Path.Combine(_dir, "out.jpg");

        var result = new WicImageProcessor().Process(src, dst, new ImageOptions(Resize: ImageResizeMode.LongEdge, ResizeValue: 100), ImageFormat.Jpeg);

        Assert.True(result.Success, result.Error);
        var (w, h, _, _) = Read(dst);
        Assert.Equal((100, 50), (w, h));
        Assert.Equal(new ImageInfo(400, 200), new WicImageProcessor().Probe(src));
    }

    [Fact]
    public void AutoOrient_RotatesPixels_ResetsOrientation_AndKeepsDateTaken()
    {
        var src = WriteJpeg("rot.jpg", 400, 200, 6, "2021-05-06T07:08:09");
        var dst = Path.Combine(_dir, "rot-out.jpg");

        var result = new WicImageProcessor().Process(src, dst, new ImageOptions(AutoOrient: true, Format: ImageFormat.Jpeg), ImageFormat.Jpeg);

        Assert.True(result.Success, result.Error);
        var (w, h, orientation, date) = Read(dst);
        Assert.Equal((200, 400), (w, h));            // 400x200 xoay 90° thành 200x400
        Assert.Equal(1, orientation);
        Assert.NotNull(date);
        Assert.Contains("2021", date);
    }

    [Fact]
    public void Convert_PngWithAlphaToJpeg_UsesWhiteBackground_AndPngRoundTrip()
    {
        var png = Path.Combine(_dir, "t.png");
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(Pixels(20, 20, alpha: true)));
        using (var fs = File.Create(png)) enc.Save(fs);

        var jpg = Path.Combine(_dir, "t.jpg");
        var result = new WicImageProcessor().Process(png, jpg, new ImageOptions(ImageFormat.Jpeg), ImageFormat.Jpeg);
        Assert.True(result.Success, result.Error);

        using var fs2 = File.OpenRead(jpg);
        var frame = BitmapDecoder.Create(fs2, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        var px = new byte[frame.PixelWidth * frame.PixelHeight * 4];
        new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0).CopyPixels(px, frame.PixelWidth * 4, 0);
        Assert.True(px[0] > 240 && px[1] > 240 && px[2] > 240, "nền trong suốt phải thành trắng");

        var back = Path.Combine(_dir, "back.png");
        Assert.True(new WicImageProcessor().Process(jpg, back, new ImageOptions(ImageFormat.Png), ImageFormat.Png).Success);
        Assert.Equal(20, Read(back).w);
    }

    [Fact]
    public void JpegQuality_LowerQualityGivesSmallerFile()
    {
        var src = WriteJpeg("q.jpg", 600, 400, null);
        var hi = Path.Combine(_dir, "hi.jpg");
        var lo = Path.Combine(_dir, "lo.jpg");
        var processor = new WicImageProcessor();

        Assert.True(processor.Process(src, hi, new ImageOptions(JpegQuality: 95), ImageFormat.Jpeg).Success);
        Assert.True(processor.Process(src, lo, new ImageOptions(JpegQuality: 60), ImageFormat.Jpeg).Success);

        Assert.True(new FileInfo(lo).Length < new FileInfo(hi).Length);
    }

    [Fact]
    public void BrokenFile_FailsCleanly_WithoutLeavingOutput()
    {
        var bad = Path.Combine(_dir, "bad.jpg");
        File.WriteAllText(bad, "this is not an image");
        var dst = Path.Combine(_dir, "bad-out.jpg");

        var result = new WicImageProcessor().Process(bad, dst, new ImageOptions(), ImageFormat.Jpeg);

        Assert.False(result.Success);
        Assert.False(File.Exists(dst));
        Assert.Null(new WicImageProcessor().Probe(bad));
    }

    [Fact]
    public void EndToEnd_ThroughCoreBatchAndExecutor_RoundTripsWithUndo()
    {
        var src = WriteJpeg("e2e.jpg", 800, 600, null);
        var fs = new RealFileSystem();
        var item = FileScanner.FromFile(src);
        var options = new ImageOptions(Resize: ImageResizeMode.LongEdge, ResizeValue: 200);
        var plan = ImagePlanner.Plan(new[] { item }, options, fs);
        var tempRoot = Path.Combine(_dir, "tmp");
        var batch = ImageBatch.Run(plan, options, new WicImageProcessor(), fs, tempRoot);
        var journal = Path.Combine(_dir, "journal");

        var result = new OperationExecutor(fs, journal, new QuarantineStore(fs, Path.Combine(_dir, "q"))).Execute(batch.Operations);

        Assert.Equal(0, result.Failed);
        var output = plan.Items[0].OutputPath;
        Assert.Equal(200, Read(output).w);
        Assert.True(File.Exists(src));
        Assert.Equal(File.GetLastWriteTimeUtc(src), File.GetLastWriteTimeUtc(output));

        new UndoService(fs).UndoJournal(result.JournalPath);
        Assert.False(File.Exists(output));
        Assert.True(File.Exists(src));
    }
}
