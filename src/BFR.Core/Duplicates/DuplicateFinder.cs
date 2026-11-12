namespace BFR.Core;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

/// <param name="Size">Kích thước chung (byte).</param>
/// <param name="Hash">SHA-256 toàn bộ nội dung (hex thường).</param>
/// <param name="Files">Tối thiểu 2 tệp có nội dung giống hệt nhau.</param>
public sealed record DuplicateGroup(long Size, string Hash, IReadOnlyList<FileItem> Files);

public sealed record DuplicateOptions(long MinSize = 1, int EdgeBytes = 64 * 1024);

/// <summary>
/// Tìm tệp trùng nội dung (M3) theo 3 tầng: kích thước → băm đầu/cuối → SHA-256 toàn bộ.
/// Chỉ coi là trùng khi SHA-256 toàn bộ khớp, nên không có dương tính giả ở tầng cuối.
/// Bỏ qua thư mục, tệp rỗng (dưới MinSize) và tệp "chỉ trực tuyến" để không kích hoạt tải xuống.
/// Tệp không đọc được bị bỏ qua và liệt kê trong <see cref="Unreadable"/>.
/// </summary>
public sealed class DuplicateFinder
{
    private readonly IFileSystem _fs;

    public DuplicateFinder(IFileSystem fs)
    {
        _fs = fs;
    }

    /// <summary>Các tệp không đọc được trong lần tìm gần nhất.</summary>
    public IReadOnlyList<string> Unreadable { get; private set; } = Array.Empty<string>();

    public IReadOnlyList<DuplicateGroup> Find(
        IEnumerable<FileItem> files, DuplicateOptions options, CancellationToken ct = default, IProgress<int>? progress = null)
    {
        var unreadable = new List<string>();
        var groups = new List<DuplicateGroup>();
        var done = 0;

        // Tầng 1: nhóm theo kích thước (kích thước duy nhất bị loại).
        var candidates = files
            .Where(f => !f.IsDirectory && !f.IsOffline && f.Size >= Math.Max(1, options.MinSize))
            .GroupBy(f => f.Size)
            .Where(g => g.Count() > 1);

        foreach (var sizeGroup in candidates)
        {
            ct.ThrowIfCancellationRequested();

            // Tầng 2: băm 64 KB đầu + 64 KB cuối.
            var byEdge = new Dictionary<string, List<FileItem>>();
            foreach (var file in sizeGroup)
            {
                ct.ThrowIfCancellationRequested();
                var edge = TryHash(file, partial: true, options.EdgeBytes, ct, unreadable);
                if (edge is null) continue;
                if (!byEdge.TryGetValue(edge, out var list)) byEdge[edge] = list = new List<FileItem>();
                list.Add(file);
            }

            foreach (var edgeGroup in byEdge.Values.Where(l => l.Count > 1))
            {
                // Tầng 3: SHA-256 toàn bộ nội dung.
                var byFull = new Dictionary<string, List<FileItem>>();
                foreach (var file in edgeGroup)
                {
                    ct.ThrowIfCancellationRequested();
                    var full = TryHash(file, partial: false, options.EdgeBytes, ct, unreadable);
                    if (full is null) continue;
                    if (!byFull.TryGetValue(full, out var list)) byFull[full] = list = new List<FileItem>();
                    list.Add(file);
                    progress?.Report(++done);
                }

                foreach (var pair in byFull.Where(p => p.Value.Count > 1))
                {
                    groups.Add(new DuplicateGroup(sizeGroup.Key, pair.Key, pair.Value));
                }
            }
        }

        Unreadable = unreadable;
        return groups
            .OrderByDescending(g => g.Size * (g.Files.Count - 1))
            .ThenBy(g => g.Hash, StringComparer.Ordinal)
            .ToList();
    }

    private string? TryHash(FileItem file, bool partial, int edge, CancellationToken ct, List<string> unreadable)
    {
        try
        {
            using var stream = _fs.OpenRead(file.FullPath);
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[Math.Max(edge, 81920)];

            if (partial && file.Size > (long)edge * 2)
            {
                ReadInto(stream, hasher, buffer, edge, ct);
                stream.Seek(-edge, SeekOrigin.End);
                ReadInto(stream, hasher, buffer, edge, ct);
            }
            else
            {
                ReadInto(stream, hasher, buffer, long.MaxValue, ct);
            }
            return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            unreadable.Add(file.FullPath);
            return null;
        }
    }

    private static void ReadInto(Stream stream, IncrementalHash hasher, byte[] buffer, long limit, CancellationToken ct)
    {
        long remaining = limit;
        while (remaining > 0)
        {
            ct.ThrowIfCancellationRequested();
            var want = (int)Math.Min(buffer.Length, remaining);
            var read = stream.Read(buffer, 0, want);
            if (read <= 0) break;
            hasher.AppendData(buffer, 0, read);
            remaining -= read;
        }
    }
}
