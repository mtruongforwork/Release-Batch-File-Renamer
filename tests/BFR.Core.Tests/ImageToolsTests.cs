namespace BFR.Core.Tests;

using BFR.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Xunit;

public class ImageToolsTests
{
    private sealed class FakeProcessor : IImageProcessor
    {
        private readonly FakeFileSystem _fs;
        private readonly object _lock = new();
        public HashSet<string> Fail { get; } = new();
        public long BigPixels { get; set; }
        public int MaxConcurrent;
        private int _running;

        public FakeProcessor(FakeFileSystem fs) { _fs = fs; }

        public byte[]? GrayThumbnail(string path, int width, int height) => null;

        public ImageInfo? Probe(string path) => path.Contains("big") ? new ImageInfo(20000, 10000) : new ImageInfo(100, 50);

        public ImageResult Process(string source, string destination, ImageOptions options, ImageFormat outputFormat)
        {
            var now = Interlocked.Increment(ref _running);
            MaxConcurrent = Math.Max(MaxConcurrent, now);
            Thread.Sleep(5);
            try
            {
                if (Fail.Contains(source)) return new ImageResult(false, Error: "bad image");
                lock (_lock) _fs.WriteAllBytes(destination, new byte[] { 1, 2, 3 });
                return new ImageResult(true, 10, 10);
            }
            finally { Interlocked.Decrement(ref _running); }
        }
    }

    private static FileItem Img(FakeFileSystem fs, string path, long size = 10)
    {
        fs.WriteAllText(path, "img");
        var t = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        fs.SetTimes(path, FileTimes.From(t, t, t));
        return new FileItem(path, path[..path.LastIndexOf('/')], System.IO.Path.GetFileNameWithoutExtension(path),
            System.IO.Path.GetExtension(path).TrimStart('.'), false, size, t, t, 0);
    }

    [Theory]
    [InlineData(4000, 3000, ImageResizeMode.LongEdge, 2048, 0, 0, false, 2048, 1536)]
    [InlineData(3000, 4000, ImageResizeMode.LongEdge, 2048, 0, 0, false, 1536, 2048)]
    [InlineData(1000, 500, ImageResizeMode.LongEdge, 2048, 0, 0, false, 1000, 500)]   // không phóng to
    [InlineData(1000, 500, ImageResizeMode.LongEdge, 2000, 0, 0, true, 2000, 1000)]
    [InlineData(1000, 500, ImageResizeMode.Percent, 50, 0, 0, false, 500, 250)]
    [InlineData(1000, 500, ImageResizeMode.Fit, 0, 400, 400, false, 400, 200)]
    [InlineData(10, 1, ImageResizeMode.Percent, 1, 0, 0, false, 1, 1)]                  // tối thiểu 1 điểm ảnh
    public void Resize_ComputesAspectPreservingSizes(int w, int h, ImageResizeMode mode, int value, int fw, int fh, bool up, int ew, int eh) =>
        Assert.Equal((ew, eh), ImageResize.Compute(w, h, new ImageOptions(Resize: mode, ResizeValue: value, FitWidth: fw, FitHeight: fh, AllowUpscale: up)));

    [Fact]
    public void Planner_NeverOverwritesOriginalByDefault_AndAvoidsCollisions()
    {
        var fs = new FakeFileSystem();
        var a = Img(fs, "/p/a.png");
        var b = Img(fs, "/q/a.png");
        fs.WriteAllText("/p/a_edit.jpg", "existing");

        var plan = ImagePlanner.Plan(new[] { a, b }, new ImageOptions(ImageFormat.Jpeg), fs);

        Assert.Equal("/p/a_edit (2).jpg", plan.Items[0].OutputPath);
        Assert.Equal("/q/a_edit.jpg", plan.Items[1].OutputPath);

        var toFolder = ImagePlanner.Plan(new[] { a, b }, new ImageOptions(ImageFormat.Jpeg, OutputFolder: "/out", Suffix: ""), fs);
        Assert.Equal("/out/a.jpg", toFolder.Items[0].OutputPath);
        Assert.Equal("/out/a (2).jpg", toFolder.Items[1].OutputPath);

        var same = ImagePlanner.Plan(new[] { a }, new ImageOptions(ImageFormat.Keep, AutoOrient: true, OutputFolder: "/p", Suffix: ""), fs);
        Assert.NotEqual(a.FullPath, same.Items[0].OutputPath);
    }

    [Fact]
    public void Planner_RejectsEmptyOrBadSettings_AndMarksUnsupportedFiles()
    {
        var fs = new FakeFileSystem();
        var a = Img(fs, "/p/a.png");
        var doc = Img(fs, "/p/a.pdf");

        Assert.NotEmpty(ImagePlanner.Plan(new[] { a }, new ImageOptions(AutoOrient: false), fs).Errors);
        Assert.NotEmpty(ImagePlanner.Plan(new[] { a }, new ImageOptions(Resize: ImageResizeMode.LongEdge, ResizeValue: 0), fs).Errors);
        Assert.NotEmpty(ImagePlanner.Plan(new[] { a }, new ImageOptions(Format: ImageFormat.Png, Suffix: ""), fs).Errors);

        var plan = ImagePlanner.Plan(new[] { a, doc }, new ImageOptions(ImageFormat.Png), fs);
        Assert.Equal(new[] { ImageStatus.Ready, ImageStatus.Unsupported }, plan.Items.Select(i => i.Status));
        Assert.Equal(ImageFormat.Jpeg, ImagePlanner.ResolveFormat("heic", ImageFormat.Keep));
        Assert.Equal(ImageFormat.Png, ImagePlanner.ResolveFormat("PNG", ImageFormat.Keep));
    }

    [Fact]
    public void Batch_WritesTempFiles_BuildsOperations_AndUndoRemovesGeneratedFiles()
    {
        var fs = new FakeFileSystem();
        var files = Enumerable.Range(0, 6).Select(i => Img(fs, $"/p/i{i}.png")).ToList();
        var options = new ImageOptions(ImageFormat.Jpeg);
        var plan = ImagePlanner.Plan(files, options, fs);
        var processor = new FakeProcessor(fs);
        processor.Fail.Add("/p/i2.png");

        var batch = ImageBatch.Run(plan, options, processor, fs, "/tmp", maxParallel: 3);

        Assert.Single(batch.Failures);
        Assert.Equal("/p/i2.png", batch.Failures[0].Path);
        Assert.Equal(5, batch.Operations.Count(o => o.Kind == OperationKind.WriteFile));
        Assert.Equal(5, batch.Operations.Count(o => o.Kind == OperationKind.SetTimestamps));
        Assert.False(fs.FileExists("/tmp/00003.jpg")); // tệp tạm của ảnh lỗi bị dọn

        var result = new OperationExecutor(fs, "/journal", new QuarantineStore(fs, "/q")).Execute(batch.Operations);
        Assert.Equal(0, result.Failed);
        Assert.True(fs.FileExists("/p/i0_edit.jpg"));
        Assert.True(files.All(f => fs.FileExists(f.FullPath)));   // bản gốc còn nguyên
        Assert.Empty(fs.GetFiles("/tmp", "*", true));

        new UndoService(fs).UndoJournal(result.JournalPath);
        Assert.False(fs.FileExists("/p/i0_edit.jpg"));
        Assert.True(files.All(f => fs.FileExists(f.FullPath)));
    }

    [Fact]
    public void Batch_OverwriteWithFormatChange_QuarantinesOriginal_AndUndoBringsItBack()
    {
        var fs = new FakeFileSystem();
        var a = Img(fs, "/p/a.png");
        var options = new ImageOptions(ImageFormat.Jpeg, OverwriteOriginal: true);
        var plan = ImagePlanner.Plan(new[] { a }, options, fs);
        Assert.Equal("/p/a.jpg", plan.Items[0].OutputPath);

        var batch = ImageBatch.Run(plan, options, new FakeProcessor(fs), fs, "/tmp", 1);
        var result = new OperationExecutor(fs, "/journal", new QuarantineStore(fs, "/q")).Execute(batch.Operations);

        Assert.Equal(0, result.Failed);
        Assert.False(fs.FileExists("/p/a.png"));
        Assert.True(fs.FileExists("/p/a.jpg"));

        new UndoService(fs).UndoJournal(result.JournalPath);
        Assert.True(fs.FileExists("/p/a.png"));
        Assert.False(fs.FileExists("/p/a.jpg"));
    }

    [Fact]
    public void Batch_LargeImagesRunSequentially_AndCancellationCleansTemp()
    {
        var fs = new FakeFileSystem();
        var files = new[] { Img(fs, "/p/big1.png"), Img(fs, "/p/big2.png"), Img(fs, "/p/big3.png") };
        var options = new ImageOptions(ImageFormat.Jpeg);
        var processor = new FakeProcessor(fs);

        ImageBatch.Run(ImagePlanner.Plan(files, options, fs), options, processor, fs, "/tmp", 8);
        Assert.Equal(1, processor.MaxConcurrent);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            ImageBatch.Run(ImagePlanner.Plan(files, options, fs), options, processor, fs, "/tmp2", 2, cts.Token));
        Assert.Empty(fs.GetFiles("/tmp2", "*", true));
    }
}
