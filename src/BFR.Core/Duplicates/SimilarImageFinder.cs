namespace BFR.Core;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

/// <summary>Băm cảm nhận dHash 64 bit: so sánh độ sáng điểm ảnh kề nhau trên ảnh xám 9×8.</summary>
public static class PerceptualHash
{
    public const int Width = 9;
    public const int Height = 8;

    /// <param name="gray">72 byte, từng dòng của ảnh 9×8.</param>
    public static ulong FromGray9x8(ReadOnlySpan<byte> gray)
    {
        if (gray.Length != Width * Height) throw new ArgumentException("Cần đúng 72 byte (ảnh xám 9×8).", nameof(gray));

        ulong hash = 0;
        var bit = 0;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width - 1; x++)
            {
                if (gray[y * Width + x] < gray[y * Width + x + 1]) hash |= 1UL << bit;
                bit++;
            }
        }
        return hash;
    }

    public static int Distance(ulong a, ulong b) => BitOperations.PopCount(a ^ b);
}

public sealed record SimilarImageOptions(int MaxDistance = 6);

/// <summary>
/// Tìm ảnh gần giống (Pro): cùng nội dung nhưng khác kích thước, nén hoặc chỉnh nhẹ. Chỉ để NGƯỜI DÙNG xác nhận,
/// không bao giờ tự chọn xoá. Dùng chỉ mục nhiều đoạn (pigeonhole) để không phải so O(n²) mọi cặp.
/// </summary>
public sealed class SimilarImageFinder
{
    private readonly IImageProcessor _processor;

    public SimilarImageFinder(IImageProcessor processor)
    {
        _processor = processor;
    }

    /// <summary>Các tệp không tạo được băm (hỏng/không hỗ trợ).</summary>
    public IReadOnlyList<string> Unreadable { get; private set; } = Array.Empty<string>();

    public IReadOnlyList<DuplicateGroup> Find(
        IEnumerable<FileItem> files, SimilarImageOptions options, int? maxParallel = null, CancellationToken ct = default, IProgress<int>? progress = null)
    {
        var candidates = files.Where(f => !f.IsDirectory && !f.IsOffline && ImagePlanner.IsSupportedSource(f.Extension)).ToList();
        var hashes = new ulong?[candidates.Count];
        var done = 0;

        Parallel.For(0, candidates.Count,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, maxParallel ?? Environment.ProcessorCount - 1), CancellationToken = ct },
            i =>
            {
                var gray = _processor.GrayThumbnail(candidates[i].FullPath, PerceptualHash.Width, PerceptualHash.Height);
                if (gray is { Length: PerceptualHash.Width * PerceptualHash.Height }) hashes[i] = PerceptualHash.FromGray9x8(gray);
                progress?.Report(Interlocked.Increment(ref done));
            });

        Unreadable = candidates.Where((_, i) => hashes[i] is null).Select(f => f.FullPath).ToList();

        var distance = Math.Clamp(options.MaxDistance, 0, 16);
        var parent = Enumerable.Range(0, candidates.Count).ToArray();
        int Find(int x)
        {
            while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
            return x;
        }

        // Chia 64 bit thành distance+1 đoạn: hai ảnh cách nhau ≤ distance bit phải trùng ít nhất một đoạn.
        var segments = distance + 1;
        var buckets = new Dictionary<(int seg, ulong value), List<int>>();
        var bounds = Enumerable.Range(0, segments + 1).Select(s => s * 64 / segments).ToArray();

        for (var i = 0; i < candidates.Count; i++)
        {
            if (hashes[i] is not { } h) continue;
            ct.ThrowIfCancellationRequested();

            for (var s = 0; s < segments; s++)
            {
                var width = bounds[s + 1] - bounds[s];
                var value = (h >> bounds[s]) & (width >= 64 ? ulong.MaxValue : (1UL << width) - 1);
                if (!buckets.TryGetValue((s, value), out var list)) buckets[(s, value)] = list = new List<int>();

                foreach (var j in list)
                {
                    if (PerceptualHash.Distance(h, hashes[j]!.Value) <= distance) parent[Find(i)] = Find(j);
                }
                list.Add(i);
            }
        }

        return Enumerable.Range(0, candidates.Count)
            .Where(i => hashes[i] is not null)
            .GroupBy(Find)
            .Where(g => g.Count() > 1)
            .Select(g =>
            {
                var members = g.Select(i => candidates[i]).OrderBy(f => f.FullPath, StringComparer.OrdinalIgnoreCase).ToList();
                return new DuplicateGroup(members.Max(m => m.Size), "similar:" + hashes[g.First()]!.Value.ToString("x16"), members);
            })
            .OrderByDescending(g => g.Files.Count)
            .ThenBy(g => g.Files[0].FullPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
