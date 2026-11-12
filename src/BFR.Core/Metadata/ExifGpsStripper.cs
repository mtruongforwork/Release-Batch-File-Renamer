namespace BFR.Core;

using System;
using System.Text;

/// <param name="Data">Dữ liệu JPEG sau khi xử lý (cùng độ dài với bản gốc).</param>
/// <param name="ExifGpsFound">Có thẻ GPS trong EXIF.</param>
/// <param name="XmpGpsRemains">XMP nhúng vẫn chứa toạ độ GPS (không thể gỡ không mã hoá lại; cần cảnh báo người dùng).</param>
public sealed record GpsStripResult(byte[] Data, bool ExifGpsFound, bool XmpGpsRemains);

/// <summary>
/// Xoá GPS khỏi EXIF của JPEG KHÔNG mã hoá lại ảnh (M4): sửa trực tiếp các byte của khối EXIF, giữ nguyên độ dài tệp
/// nên điểm ảnh, hướng xoay, ngày chụp và mọi thẻ khác không đổi. Vùng dữ liệu GPS bị ghi đè bằng 0, không chỉ bị bỏ con trỏ.
/// Dữ liệu hỏng/không phải JPEG: trả về nguyên vẹn, không ném lỗi.
/// </summary>
public static class ExifGpsStripper
{
    private const ushort GpsPointerTag = 0x8825;
    private static readonly byte[] ExifHeader = { (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0 };
    private static readonly byte[] XmpHeader = Encoding.ASCII.GetBytes("http://ns.adobe.com/xap/1.0/\0");

    public static bool IsJpeg(ReadOnlySpan<byte> data) => data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF;

    /// <summary>Chỉ kiểm tra, không sửa.</summary>
    public static bool HasGps(byte[] data) => Process(data, modify: false).ExifGpsFound;

    public static GpsStripResult Strip(byte[] data) => Process(data, modify: true);

    private static GpsStripResult Process(byte[] source, bool modify)
    {
        var data = modify ? (byte[])source.Clone() : source;
        var found = false;
        var xmpGps = false;

        try
        {
            if (!IsJpeg(data)) return new GpsStripResult(data, false, false);

            var pos = 2;
            while (pos + 4 <= data.Length)
            {
                if (data[pos] != 0xFF) break;
                var marker = data[pos + 1];
                if (marker == 0xFF) { pos++; continue; }
                if (marker == 0xDA || marker == 0xD9) break; // bắt đầu dữ liệu ảnh
                if (marker is 0x01 or (>= 0xD0 and <= 0xD8)) { pos += 2; continue; }

                var length = (data[pos + 2] << 8) | data[pos + 3];
                var start = pos + 4;
                var end = pos + 2 + length;
                if (length < 2 || end > data.Length) break;

                if (marker == 0xE1)
                {
                    if (StartsWith(data, start, end, ExifHeader))
                    {
                        if (ProcessTiff(data, start + ExifHeader.Length, end, modify)) found = true;
                    }
                    else if (StartsWith(data, start, end, XmpHeader))
                    {
                        var xmp = Encoding.UTF8.GetString(data, start + XmpHeader.Length, end - start - XmpHeader.Length);
                        if (xmp.Contains("GPSLatitude", StringComparison.Ordinal) || xmp.Contains("GPSLongitude", StringComparison.Ordinal)) xmpGps = true;
                    }
                }

                pos = end;
            }
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException or OverflowException)
        {
            // Cấu trúc hỏng: trả về những gì đã làm được.
        }

        return new GpsStripResult(data, found, xmpGps);
    }

    private static bool StartsWith(byte[] data, int start, int end, byte[] prefix)
    {
        if (end - start < prefix.Length) return false;
        for (var i = 0; i < prefix.Length; i++)
        {
            if (data[start + i] != prefix[i]) return false;
        }
        return true;
    }

    private readonly struct Tiff
    {
        private readonly byte[] _d;
        public readonly int Base;
        public readonly int End;
        public readonly bool Little;

        public Tiff(byte[] d, int @base, int end, bool little)
        {
            _d = d;
            Base = @base;
            End = end;
            Little = little;
        }

        public bool InRange(long offset, long length) => offset >= 0 && length >= 0 && Base + offset + length <= End;

        public int U16(int offset) => Little ? _d[Base + offset] | (_d[Base + offset + 1] << 8) : (_d[Base + offset] << 8) | _d[Base + offset + 1];

        public long U32(int offset) => Little
            ? (uint)(_d[Base + offset] | (_d[Base + offset + 1] << 8) | (_d[Base + offset + 2] << 16) | (_d[Base + offset + 3] << 24))
            : (uint)((_d[Base + offset] << 24) | (_d[Base + offset + 1] << 16) | (_d[Base + offset + 2] << 8) | _d[Base + offset + 3]);

        public void Zero(long offset, long length)
        {
            if (InRange(offset, length)) Array.Clear(_d, (int)(Base + offset), (int)length);
        }

        public void Move(int from, int to, int length) => Buffer.BlockCopy(_d, Base + from, _d, Base + to, length);
    }

    private static bool ProcessTiff(byte[] data, int tiffStart, int end, bool modify)
    {
        if (end - tiffStart < 8) return false;
        var little = data[tiffStart] == 'I' && data[tiffStart + 1] == 'I';
        var big = data[tiffStart] == 'M' && data[tiffStart + 1] == 'M';
        if (!little && !big) return false;

        var t = new Tiff(data, tiffStart, end, little);
        if (t.U16(2) != 42) return false;

        var ifd0 = t.U32(4);
        if (!t.InRange(ifd0, 2)) return false;

        var count = t.U16((int)ifd0);
        if (!t.InRange(ifd0, 2 + count * 12L + 4)) return false;

        for (var i = 0; i < count; i++)
        {
            var entry = (int)ifd0 + 2 + i * 12;
            if (t.U16(entry) != GpsPointerTag) continue;

            if (!modify) return true;

            EraseGpsIfd(t, t.U32(entry + 8));

            // Gỡ mục khỏi IFD0: dồn các mục sau và con trỏ IFD kế tiếp lên 12 byte, xoá phần thừa, giảm số mục.
            var tail = (count - i - 1) * 12 + 4;
            t.Move(entry + 12, entry, tail);
            t.Zero(ifd0 + 2 + (count - 1) * 12L + 4, 12);
            var newCount = count - 1;
            if (little)
            {
                data[tiffStart + (int)ifd0] = (byte)(newCount & 0xFF);
                data[tiffStart + (int)ifd0 + 1] = (byte)(newCount >> 8);
            }
            else
            {
                data[tiffStart + (int)ifd0] = (byte)(newCount >> 8);
                data[tiffStart + (int)ifd0 + 1] = (byte)(newCount & 0xFF);
            }
            return true;
        }

        return false;
    }

    private static void EraseGpsIfd(Tiff t, long offset)
    {
        if (!t.InRange(offset, 2)) return;
        var count = t.U16((int)offset);
        if (!t.InRange(offset, 2 + count * 12L + 4)) return;

        for (var i = 0; i < count; i++)
        {
            var entry = (int)offset + 2 + i * 12;
            var type = t.U16(entry + 2);
            var n = t.U32(entry + 4);
            var size = TypeSize(type) * n;
            if (size > 4) t.Zero(t.U32(entry + 8), size); // giá trị nằm ngoài mục (toạ độ, ngày GPS...)
        }

        t.Zero(offset, 2 + count * 12L + 4);
    }

    private static long TypeSize(int type) => type switch
    {
        1 or 2 or 6 or 7 => 1,
        3 or 8 => 2,
        4 or 9 or 11 => 4,
        5 or 10 or 12 => 8,
        _ => 1
    };
}
