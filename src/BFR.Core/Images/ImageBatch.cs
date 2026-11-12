namespace BFR.Core;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public sealed record ImageFailure(string Path, string Error);

public sealed record ImageBatchResult(IReadOnlyList<Operation> Operations, IReadOnlyList<ImageFailure> Failures, long BytesBefore, long BytesAfter);

/// <summary>
/// Chạy bộ xử lý ảnh cho các mục <see cref="ImageStatus.Ready"/>: ghi kết quả ra tệp tạm (song song, tối đa
/// <c>ProcessorCount - 1</c>), rồi trả về các <see cref="Operation"/> để <see cref="OperationExecutor"/> đưa vào đúng chỗ.
/// Ảnh quá lớn (trên <see cref="LargeImagePixels"/>) chạy tuần tự để giới hạn bộ nhớ. Tệp tạm của ảnh lỗi bị xoá.
/// </summary>
public static class ImageBatch
{
    public const long LargeImagePixels = 100_000_000;

    public static ImageBatchResult Run(
        ImagePlan plan, ImageOptions options, IImageProcessor processor, IFileSystem fs, string tempRoot,
        int? maxParallel = null, CancellationToken ct = default, IProgress<int>? progress = null)
    {
        var ready = plan.Items.Where(i => i.Status == ImageStatus.Ready).ToList();
        var results = new (ImageResult? result, string temp)[ready.Count];
        var sep = tempRoot.Contains('\\') || tempRoot.Contains(':') ? '\\' : '/';
        var done = 0;

        string TempFor(int index, ImageItem item) =>
            $"{tempRoot.TrimEnd('\\', '/')}{sep}{index + 1:D5}.{ImagePlanner.ExtensionOf(item.OutputFormat)}";

        void RunOne(int index)
        {
            ct.ThrowIfCancellationRequested();
            var item = ready[index];
            var temp = TempFor(index, item);
            ImageResult result;
            try
            {
                result = processor.Process(item.File.FullPath, temp, options, item.OutputFormat);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or ArgumentException)
            {
                result = new ImageResult(false, Error: ex.Message);
            }

            if (!result.Success && fs.FileExists(temp))
            {
                try { fs.Delete(temp, recursive: false); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }

            results[index] = (result, temp);
            progress?.Report(Interlocked.Increment(ref done));
        }

        var large = new List<int>();
        var normal = new List<int>();
        for (var i = 0; i < ready.Count; i++)
        {
            var info = processor.Probe(ready[i].File.FullPath);
            (info is not null && info.Pixels > LargeImagePixels ? large : normal).Add(i);
        }

        var parallel = Math.Max(1, maxParallel ?? Environment.ProcessorCount - 1);
        try
        {
            Parallel.ForEach(normal, new ParallelOptions { MaxDegreeOfParallelism = parallel, CancellationToken = ct }, RunOne);
            foreach (var i in large) RunOne(i);
        }
        catch (OperationCanceledException)
        {
            // Huỷ giữa chừng: dọn mọi tệp tạm đã sinh ra rồi báo lên.
            foreach (var (result, temp) in results)
            {
                if (result is { Success: true } && fs.FileExists(temp))
                {
                    try { fs.Delete(temp, recursive: false); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            }
            throw;
        }

        var ops = new List<Operation>();
        var failures = new List<ImageFailure>();
        long before = 0, after = 0;

        for (var i = 0; i < ready.Count; i++)
        {
            var item = ready[i];
            var (result, temp) = results[i];
            if (result is not { Success: true })
            {
                failures.Add(new ImageFailure(item.File.FullPath, result?.Error ?? CoreText.Get("CoreImageFailed", "Không xử lý được ảnh")));
                continue;
            }

            FileTimes? times = null;
            if (options.PreserveTimes)
            {
                try { times = fs.GetTimes(item.File.FullPath); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }

            ops.Add(Operation.WriteFile(temp, item.OutputPath));
            if (options.OverwriteOriginal && !string.Equals(item.OutputPath.Replace('\\', '/'), item.File.FullPath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
            {
                ops.Add(Operation.Quarantine(item.File.FullPath)); // đổi định dạng + ghi đè: bản gốc vào khu cách ly
            }
            if (times is { } t) ops.Add(Operation.SetTimestamps(item.OutputPath, t));

            before += item.File.Size;
            try { after += fs.GetLength(temp); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        return new ImageBatchResult(ops, failures, before, after);
    }
}
