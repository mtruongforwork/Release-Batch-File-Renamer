namespace BFR.Core;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// Đọc tag nhạc (artist, album, title, track, year) từ MP3 (ID3v2.2/2.3/2.4 và ID3v1) và FLAC (Vorbis comment).
/// Tự đọc nhỏ gọn, KHÔNG thực thi gì từ tệp: mọi độ dài đều bị chặn, tệp hỏng cho kết quả rỗng.
/// Không đọc tệp "chỉ trực tuyến" (OneDrive) để khỏi tải về ngoài ý muốn.
/// </summary>
public sealed class AudioTagReader : IMetadataProvider
{
    private const int MaxTagBytes = 1024 * 1024; // bỏ qua phần ảnh bìa quá lớn

    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

    private readonly BoundedCache<string, IReadOnlyDictionary<string, string>> _cache = new(capacity: 8192);

    public IReadOnlyDictionary<string, string> Read(FileItem file)
    {
        if (file.IsDirectory || file.Size == 0 || file.IsOffline) return Empty;
        var ext = file.Extension.ToLowerInvariant();
        if (ext is not ("mp3" or "flac")) return Empty;

        var key = $"{file.FullPath}|{file.ModifiedUtc.Ticks}|{file.Size}";
        if (_cache.TryGet(key, out var cached)) return cached;

        IReadOnlyDictionary<string, string> map;
        try
        {
            using var stream = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            map = Parse(stream, ext);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            map = Empty;
        }

        _cache.Set(key, map);
        return map;
    }

    /// <summary>Phân tích từ luồng (dùng cho test). Không bao giờ ném lỗi với dữ liệu hỏng.</summary>
    public static IReadOnlyDictionary<string, string> Parse(Stream stream, string extension)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (extension.Equals("flac", StringComparison.OrdinalIgnoreCase)) ReadFlac(stream, map);
            else
            {
                ReadId3v2(stream, map);
                ReadId3v1(stream, map);
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or OverflowException or InvalidDataException)
        {
            // Dữ liệu hỏng: giữ những gì đã đọc được.
        }
        return map;
    }

    // ---------- ID3v2 ----------

    private static void ReadId3v2(Stream s, Dictionary<string, string> map)
    {
        s.Seek(0, SeekOrigin.Begin);
        var header = new byte[10];
        if (!ReadFully(s, header, 10) || header[0] != 'I' || header[1] != 'D' || header[2] != '3') return;

        var version = header[3];
        if (version is < 2 or > 4) return;

        var flags = header[5];
        var size = SyncSafe(header, 6);
        if (size <= 0) return;
        size = Math.Min(size, MaxTagBytes);

        var body = new byte[size];
        var got = ReadAvailable(s, body);
        var pos = 0;

        if ((flags & 0x40) != 0 && version >= 3 && got >= 4) // extended header
        {
            var ext = version == 4 ? SyncSafe(body, 0) : 4 + BigEndian(body, 0, 4);
            if (ext < 0 || ext > got) return;
            pos = ext;
        }

        var idLen = version == 2 ? 3 : 4;
        var frameHeader = version == 2 ? 6 : 10;

        while (pos + frameHeader <= got)
        {
            if (body[pos] == 0) break; // padding

            var id = Encoding.ASCII.GetString(body, pos, idLen);
            var frameSize = version switch
            {
                2 => BigEndian(body, pos + 3, 3),
                4 => SyncSafe(body, pos + 4),
                _ => BigEndian(body, pos + 4, 4)
            };
            pos += frameHeader;

            if (frameSize < 0 || pos + frameSize > got) break;

            if (frameSize > 1)
            {
                var name = MapFrame(id);
                if (name is not null && !map.ContainsKey(name))
                {
                    var text = DecodeText(body, pos, frameSize);
                    if (!string.IsNullOrWhiteSpace(text)) map[name] = Clean(name, text);
                }
            }
            pos += frameSize;
        }
    }

    private static string? MapFrame(string id) => id switch
    {
        "TPE1" or "TP1" => "artist",
        "TALB" or "TAL" => "album",
        "TIT2" or "TT2" => "title",
        "TRCK" or "TRK" => "track",
        "TDRC" or "TYER" or "TYE" => "year",
        _ => null
    };

    private static string DecodeText(byte[] data, int start, int length)
    {
        var encoding = data[start];
        var offset = start + 1;
        var count = length - 1;
        if (count <= 0) return string.Empty;

        string text;
        switch (encoding)
        {
            case 1: // UTF-16 có BOM
                text = Encoding.Unicode.GetString(data, offset, count & ~1);
                if (text.Length > 0 && text[0] == '﻿') text = text[1..];
                else if (count >= 2 && data[offset] == 0xFE && data[offset + 1] == 0xFF)
                    text = Encoding.BigEndianUnicode.GetString(data, offset + 2, (count - 2) & ~1);
                break;
            case 2:
                text = Encoding.BigEndianUnicode.GetString(data, offset, count & ~1);
                break;
            case 3:
                text = Encoding.UTF8.GetString(data, offset, count);
                break;
            default:
                text = Encoding.Latin1.GetString(data, offset, count);
                break;
        }

        var nul = text.IndexOf('\0');
        return nul >= 0 ? text[..nul] : text;
    }

    // ---------- ID3v1 ----------

    private static void ReadId3v1(Stream s, Dictionary<string, string> map)
    {
        if (s.Length < 128) return;
        s.Seek(-128, SeekOrigin.End);
        var tag = new byte[128];
        if (!ReadFully(s, tag, 128) || tag[0] != 'T' || tag[1] != 'A' || tag[2] != 'G') return;

        void Put(string key, int offset, int length)
        {
            if (map.ContainsKey(key)) return;
            var text = Encoding.Latin1.GetString(tag, offset, length).TrimEnd('\0', ' ');
            if (text.Length > 0) map[key] = Clean(key, text);
        }

        Put("title", 3, 30);
        Put("artist", 33, 30);
        Put("album", 63, 30);
        Put("year", 93, 4);
        if (tag[125] == 0 && tag[126] != 0 && !map.ContainsKey("track")) map["track"] = tag[126].ToString();
    }

    // ---------- FLAC ----------

    private static void ReadFlac(Stream s, Dictionary<string, string> map)
    {
        s.Seek(0, SeekOrigin.Begin);
        var magic = new byte[4];
        if (!ReadFully(s, magic, 4) || magic[0] != 'f' || magic[1] != 'L' || magic[2] != 'a' || magic[3] != 'C') return;

        for (var guard = 0; guard < 64; guard++)
        {
            var h = new byte[4];
            if (!ReadFully(s, h, 4)) return;
            var last = (h[0] & 0x80) != 0;
            var type = h[0] & 0x7F;
            var len = (h[1] << 16) | (h[2] << 8) | h[3];

            if (type == 4 && len <= MaxTagBytes)
            {
                var block = new byte[len];
                if (!ReadFully(s, block, len)) return;
                ParseVorbis(block, map);
                return;
            }

            if (s.Position + len > s.Length) return;
            s.Seek(len, SeekOrigin.Current);
            if (last) return;
        }
    }

    private static void ParseVorbis(byte[] b, Dictionary<string, string> map)
    {
        var pos = 0;
        int ReadInt()
        {
            if (pos + 4 > b.Length) throw new InvalidDataException();
            var v = b[pos] | (b[pos + 1] << 8) | (b[pos + 2] << 16) | (b[pos + 3] << 24);
            pos += 4;
            return v;
        }

        var vendor = ReadInt();
        if (vendor < 0 || pos + vendor > b.Length) return;
        pos += vendor;
        var count = ReadInt();
        if (count < 0) return;

        for (var i = 0; i < count && i < 4096; i++)
        {
            var len = ReadInt();
            if (len < 0 || pos + len > b.Length) return;
            var entry = Encoding.UTF8.GetString(b, pos, len);
            pos += len;

            var eq = entry.IndexOf('=');
            if (eq <= 0) continue;
            var name = entry[..eq].ToUpperInvariant() switch
            {
                "ARTIST" => "artist",
                "ALBUM" => "album",
                "TITLE" => "title",
                "TRACKNUMBER" => "track",
                "DATE" or "YEAR" => "year",
                _ => null
            };
            var value = entry[(eq + 1)..];
            if (name is not null && !map.ContainsKey(name) && !string.IsNullOrWhiteSpace(value)) map[name] = Clean(name, value);
        }
    }

    // ---------- tiện ích ----------

    private static string Clean(string key, string value)
    {
        var text = value.Trim();
        if (key == "track")
        {
            var slash = text.IndexOf('/');
            if (slash > 0) text = text[..slash]; // "3/12" -> "3"
        }
        else if (key == "year" && text.Length > 4)
        {
            text = text[..4]; // "2021-05-01" -> "2021"
        }
        return text.Length > 200 ? text[..200] : text;
    }

    private static int SyncSafe(byte[] b, int o) =>
        ((b[o] & 0x7F) << 21) | ((b[o + 1] & 0x7F) << 14) | ((b[o + 2] & 0x7F) << 7) | (b[o + 3] & 0x7F);

    private static int BigEndian(byte[] b, int o, int n)
    {
        var v = 0;
        for (var i = 0; i < n; i++) v = (v << 8) | b[o + i];
        return v;
    }

    private static bool ReadFully(Stream s, byte[] buffer, int count) => ReadAvailable(s, buffer, count) == count;

    private static int ReadAvailable(Stream s, byte[] buffer, int? count = null)
    {
        var want = count ?? buffer.Length;
        var total = 0;
        while (total < want)
        {
            var n = s.Read(buffer, total, want - total);
            if (n <= 0) break;
            total += n;
        }
        return total;
    }
}
