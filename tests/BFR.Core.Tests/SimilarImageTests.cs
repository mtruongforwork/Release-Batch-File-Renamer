namespace BFR.Core.Tests;

using BFR.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

public class SimilarImageTests
{
    private sealed class HashProcessor : IImageProcessor
    {
        public Dictionary<string, byte[]> Gray { get; } = new(StringComparer.OrdinalIgnoreCase);
        public ImageInfo? Probe(string path) => new(10, 10);
        public byte[]? GrayThumbnail(string path, int width, int height) => Gray.TryGetValue(path, out var g) ? g : null;
        public ImageResult Process(string source, string destination, ImageOptions options, ImageFormat outputFormat) => new(false);
    }

    private static byte[] Pattern(int seed)
    {
        var rnd = new Random(seed);
        var g = new byte[72];
        rnd.NextBytes(g);
        return g;
    }

    private static FileItem Img(string path) =>
        new(path, path[..path.LastIndexOf('/')], System.IO.Path.GetFileNameWithoutExtension(path), "jpg", false, 100, DateTime.UtcNow, DateTime.UtcNow, 0);

    [Fact]
    public void DHash_IsStableUnderBrightnessAndScaling_AndDistanceCountsBits()
    {
        var g = Pattern(1);
        var brighter = g.Select(b => (byte)Math.Min(255, b + 10)).ToArray(); // đổi độ sáng đều không đổi thứ tự điểm kề nhau (trừ chỗ bão hoà)
        var noSaturation = g.Select(b => (byte)(b / 2)).ToArray();
        var darker = noSaturation.Select(b => (byte)(b + 20)).ToArray();

        Assert.Equal(PerceptualHash.FromGray9x8(noSaturation), PerceptualHash.FromGray9x8(darker));
        Assert.Equal(0, PerceptualHash.Distance(5, 5));
        Assert.Equal(64, PerceptualHash.Distance(0, ulong.MaxValue));
        Assert.Equal(3, PerceptualHash.Distance(0b1011, 0));
        Assert.Throws<ArgumentException>(() => PerceptualHash.FromGray9x8(new byte[10]));
        _ = brighter;
    }

    [Fact]
    public void Finder_GroupsNearHashes_AndLeavesDifferentImagesAlone()
    {
        var p = new HashProcessor();
        var baseGray = Pattern(7);
        var near = (byte[])baseGray.Clone();
        near[3] = (byte)(255 - near[3]);          // thay đổi cục bộ: chỉ lệch vài bit
        p.Gray["/a/one.jpg"] = baseGray;
        p.Gray["/b/two.jpg"] = near;
        p.Gray["/c/other.jpg"] = Pattern(99);

        var files = new[] { Img("/a/one.jpg"), Img("/b/two.jpg"), Img("/c/other.jpg"), Img("/d/broken.jpg") };
        var finder = new SimilarImageFinder(p);

        var groups = finder.Find(files, new SimilarImageOptions(8), maxParallel: 2);

        var group = Assert.Single(groups);
        Assert.Equal(new[] { "/a/one.jpg", "/b/two.jpg" }, group.Files.Select(f => f.FullPath));
        Assert.Equal(new[] { "/d/broken.jpg" }, finder.Unreadable);
    }

    [Fact]
    public void Finder_MatchesBruteForceOnRandomHashes()
    {
        for (var seed = 0; seed < 20; seed++)
        {
            var rnd = new Random(seed);
            var p = new HashProcessor();
            var files = new List<FileItem>();
            var hashes = new List<ulong>();
            for (var i = 0; i < 60; i++)
            {
                // Các ảnh gốc cộng vài biến thể lệch vài bit.
                var baseHash = i % 3 == 0 || hashes.Count == 0 ? (ulong)rnd.NextInt64() : hashes[rnd.Next(hashes.Count)] ^ (1UL << rnd.Next(64)) ^ (1UL << rnd.Next(64));
                hashes.Add(baseHash);
                files.Add(Img($"/d/f{i}.jpg"));
                p.Gray[$"/d/f{i}.jpg"] = FromHash(baseHash);
            }

            var max = rnd.Next(0, 9);
            var found = new SimilarImageFinder(p).Find(files, new SimilarImageOptions(max), maxParallel: 1);

            // Chuẩn: thành phần liên thông của đồ thị "khoảng cách ≤ max", tính bằng vét cạn.
            var actualHashes = files.Select(f => PerceptualHash.FromGray9x8(p.Gray[f.FullPath])).ToList();
            var comp = Enumerable.Range(0, files.Count).ToArray();
            int Root(int x) { while (comp[x] != x) x = comp[x]; return x; }
            for (var i = 0; i < files.Count; i++)
                for (var j = i + 1; j < files.Count; j++)
                    if (PerceptualHash.Distance(actualHashes[i], actualHashes[j]) <= max) comp[Root(i)] = Root(j);

            var expected = Enumerable.Range(0, files.Count).GroupBy(Root).Where(g => g.Count() > 1)
                .Select(g => string.Join(",", g.Select(i => files[i].FullPath).OrderBy(x => x))).OrderBy(x => x).ToList();
            var actual = found.Select(g => string.Join(",", g.Files.Select(f => f.FullPath).OrderBy(x => x))).OrderBy(x => x).ToList();

            Assert.Equal(expected, actual);
        }
    }

    /// <summary>Dựng ảnh xám 9×8 cho ra đúng băm mong muốn.</summary>
    private static byte[] FromHash(ulong hash)
    {
        var g = new byte[72];
        var bit = 0;
        for (var y = 0; y < 8; y++)
        {
            g[y * 9] = 100;
            for (var x = 0; x < 8; x++)
            {
                var left = g[y * 9 + x];
                g[y * 9 + x + 1] = (hash >> bit & 1) == 1 ? (byte)(left + 1) : (byte)(left > 0 ? left - 1 : 0);
                bit++;
            }
        }
        return g;
    }
}
