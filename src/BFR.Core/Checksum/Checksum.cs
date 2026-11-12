namespace BFR.Core;

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

public enum ChecksumAlgorithm
{
    Md5,
    Sha1,
    Sha256
}

public enum ChecksumStatus
{
    Match,
    Mismatch,
    Missing,
    /// <summary>Đường dẫn trong tệp kiểm tra không an toàn (tuyệt đối hoặc thoát khỏi thư mục).</summary>
    Invalid
}

public sealed record ChecksumEntry(string Hash, string RelativePath);

public sealed record ChecksumResult(string RelativePath, ChecksumStatus Status, string? Expected, string? Actual);

/// <summary>Tính và kiểm tra checksum (M9), tương thích định dạng của <c>sha256sum</c>/<c>md5sum</c>.</summary>
public static class Checksum
{
    public static string Compute(IFileSystem fs, string path, ChecksumAlgorithm algorithm, CancellationToken ct = default)
    {
        using var hasher = Create(algorithm);
        using var stream = fs.OpenRead(path);
        var buffer = new byte[1024 * 1024];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            hasher.AppendData(buffer, 0, read);
        }
        return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
    }

    public static ChecksumAlgorithm? FromExtension(string? path) =>
        Path.GetExtension(path ?? string.Empty).ToLowerInvariant() switch
        {
            ".md5" => ChecksumAlgorithm.Md5,
            ".sha1" => ChecksumAlgorithm.Sha1,
            ".sha256" => ChecksumAlgorithm.Sha256,
            _ => null
        };

    public static string Extension(ChecksumAlgorithm algorithm) => algorithm switch
    {
        ChecksumAlgorithm.Md5 => ".md5",
        ChecksumAlgorithm.Sha1 => ".sha1",
        _ => ".sha256"
    };

    private static int HexLength(ChecksumAlgorithm a) => a switch
    {
        ChecksumAlgorithm.Md5 => 32,
        ChecksumAlgorithm.Sha1 => 40,
        _ => 64
    };

    /// <summary>Định dạng <c>&lt;hash&gt;  &lt;đường dẫn&gt;</c> một dòng mỗi tệp (hai dấu cách, như sha256sum).</summary>
    public static string Format(IEnumerable<ChecksumEntry> entries)
    {
        var sb = new StringBuilder();
        foreach (var e in entries) sb.Append(e.Hash).Append("  ").Append(e.RelativePath.Replace('\\', '/')).Append('\n');
        return sb.ToString();
    }

    /// <summary>Đọc tệp .md5/.sha1/.sha256; bỏ qua dòng trống, dòng chú thích (#) và dòng sai định dạng.</summary>
    public static List<ChecksumEntry> Parse(string text, ChecksumAlgorithm algorithm)
    {
        var length = HexLength(algorithm);
        var result = new List<ChecksumEntry>();

        foreach (var raw in (text ?? string.Empty).Replace("\r", string.Empty).Split('\n'))
        {
            var line = raw.TrimStart('﻿');
            if (line.Length < length + 2 || line[0] == '#') continue;

            var hash = line[..length];
            if (!IsHex(hash)) continue;

            var rest = line[length..];
            // "  name" (text) hoặc " *name" (binary)
            if (rest.Length < 2 || rest[0] != ' ' || (rest[1] != ' ' && rest[1] != '*')) continue;
            var name = rest[2..];
            if (name.Length > 0) result.Add(new ChecksumEntry(hash.ToLowerInvariant(), name));
        }
        return result;
    }

    /// <summary>Kiểm tra từng dòng với tệp trong <paramref name="baseDirectory"/>. Đường dẫn không an toàn bị từ chối.</summary>
    public static List<ChecksumResult> Verify(
        IFileSystem fs, string baseDirectory, IEnumerable<ChecksumEntry> entries, ChecksumAlgorithm algorithm, CancellationToken ct = default)
    {
        var results = new List<ChecksumResult>();
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            if (!IsSafeRelative(entry.RelativePath))
            {
                results.Add(new ChecksumResult(entry.RelativePath, ChecksumStatus.Invalid, entry.Hash, null));
                continue;
            }

            var sep = baseDirectory.Contains('\\') || baseDirectory.Contains(':') ? '\\' : '/';
            var full = baseDirectory.TrimEnd('\\', '/') + sep + entry.RelativePath.Replace('/', sep);
            if (!fs.FileExists(full))
            {
                results.Add(new ChecksumResult(entry.RelativePath, ChecksumStatus.Missing, entry.Hash, null));
                continue;
            }

            try
            {
                var actual = Compute(fs, full, algorithm, ct);
                results.Add(new ChecksumResult(entry.RelativePath,
                    string.Equals(actual, entry.Hash, StringComparison.OrdinalIgnoreCase) ? ChecksumStatus.Match : ChecksumStatus.Mismatch,
                    entry.Hash, actual));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                results.Add(new ChecksumResult(entry.RelativePath, ChecksumStatus.Missing, entry.Hash, null));
            }
        }
        return results;
    }

    private static bool IsSafeRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 1024) return false;
        if (path.StartsWith('/') || path.StartsWith('\\') || path.Contains(':')) return false;
        foreach (var c in path) if (c < 32) return false;
        foreach (var part in path.Split('/', '\\'))
        {
            if (part == "..") return false;
        }
        return true;
    }

    private static bool IsHex(string s)
    {
        foreach (var c in s)
        {
            if (!Uri.IsHexDigit(c)) return false;
        }
        return true;
    }

    private static IncrementalHash Create(ChecksumAlgorithm a) => a switch
    {
        ChecksumAlgorithm.Md5 => IncrementalHash.CreateHash(HashAlgorithmName.MD5),
        ChecksumAlgorithm.Sha1 => IncrementalHash.CreateHash(HashAlgorithmName.SHA1),
        _ => IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
    };
}
