namespace BFR.Core.Tests;

using BFR.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

public class GpsStripTests
{
    private static void U16(List<byte> b, int v, bool le) { if (le) { b.Add((byte)v); b.Add((byte)(v >> 8)); } else { b.Add((byte)(v >> 8)); b.Add((byte)v); } }
    private static void U32(List<byte> b, long v, bool le)
    {
        var x = (uint)v;
        var bytes = new[] { (byte)(x >> 24), (byte)(x >> 16), (byte)(x >> 8), (byte)x };
        if (le) Array.Reverse(bytes);
        b.AddRange(bytes);
    }

    private static void Entry(List<byte> b, int tag, int type, long count, byte[] value4, bool le)
    {
        U16(b, tag, le); U16(b, type, le); U32(b, count, le);
        b.AddRange(value4);
    }

    /// <summary>JPEG tối giản: SOI, APP1(EXIF: Make, Orientation, GPS), SOS và dữ liệu ảnh giả.</summary>
    private static byte[] BuildJpeg(bool le, bool withGps = true, string? xmp = null)
    {
        var t = new List<byte>();
        t.AddRange(Encoding.ASCII.GetBytes(le ? "II" : "MM"));
        U16(t, 42, le);
        U32(t, 8, le);

        var entries = withGps ? 3 : 2;
        U16(t, entries, le);
        Entry(t, 0x010F, 2, 4, Encoding.ASCII.GetBytes("Can\0"), le);
        var orient = new List<byte>(); U16(orient, 6, le); orient.AddRange(new byte[] { 0, 0 });
        Entry(t, 0x0112, 3, 1, orient.ToArray(), le);
        if (withGps)
        {
            var ptr = new List<byte>(); U32(ptr, 8 + 2 + 36 + 4, le);
            Entry(t, 0x8825, 4, 1, ptr.ToArray(), le);
        }
        U32(t, 0, le); // không có IFD kế tiếp

        if (withGps)
        {
            var gpsStart = t.Count;
            U16(t, 2, le);
            Entry(t, 1, 2, 2, new byte[] { (byte)'N', 0, 0, 0 }, le);
            var off = new List<byte>(); U32(off, gpsStart + 2 + 24 + 4, le);
            Entry(t, 2, 5, 3, off.ToArray(), le);
            U32(t, 0, le);
            for (var i = 0; i < 24; i++) t.Add((byte)(0x41 + i)); // "toạ độ"
        }

        var app1 = new List<byte> { 0xFF, 0xD8 };
        var exifBody = Encoding.ASCII.GetBytes("Exif\0\0").Concat(t).ToArray();
        app1.AddRange(new byte[] { 0xFF, 0xE1, (byte)((exifBody.Length + 2) >> 8), (byte)(exifBody.Length + 2) });
        app1.AddRange(exifBody);

        if (xmp is not null)
        {
            var body = Encoding.ASCII.GetBytes("http://ns.adobe.com/xap/1.0/\0").Concat(Encoding.UTF8.GetBytes(xmp)).ToArray();
            app1.AddRange(new byte[] { 0xFF, 0xE1, (byte)((body.Length + 2) >> 8), (byte)(body.Length + 2) });
            app1.AddRange(body);
        }

        app1.AddRange(new byte[] { 0xFF, 0xDA, 0x00, 0x04, 0x00, 0x00, 0x11, 0x22, 0x33, 0xFF, 0xD9 });
        return app1.ToArray();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Strip_RemovesGps_KeepsEverythingElse_AndSameLength(bool littleEndian)
    {
        var jpeg = BuildJpeg(littleEndian);
        Assert.True(ExifGpsStripper.HasGps(jpeg));

        var result = ExifGpsStripper.Strip(jpeg);

        Assert.True(result.ExifGpsFound);
        Assert.Equal(jpeg.Length, result.Data.Length);
        Assert.False(ExifGpsStripper.HasGps(result.Data));
        Assert.False(ExifGpsStripper.Strip(result.Data).ExifGpsFound);
        // Dữ liệu GPS ("ABCD...") không còn sót trong tệp.
        Assert.DoesNotContain("ABCDEFGH", Encoding.ASCII.GetString(result.Data));
        // Make và Orientation vẫn còn, ảnh (SOS...) nguyên vẹn.
        Assert.Contains("Can", Encoding.ASCII.GetString(result.Data));
        Assert.True(result.Data.TakeLast(11).SequenceEqual(jpeg.TakeLast(11)));
        // Đầu vào không bị sửa.
        Assert.True(ExifGpsStripper.HasGps(jpeg));
    }

    [Fact]
    public void Strip_WithoutGps_ReturnsUnchanged_AndXmpGpsIsFlagged()
    {
        var clean = BuildJpeg(true, withGps: false);
        var r = ExifGpsStripper.Strip(clean);
        Assert.False(r.ExifGpsFound);
        Assert.True(clean.SequenceEqual(r.Data));

        var withXmp = BuildJpeg(true, xmp: "<x exif:GPSLatitude=\"1\"/>");
        Assert.True(ExifGpsStripper.Strip(withXmp).XmpGpsRemains);
    }

    [Fact]
    public void CorruptAndNonJpegData_NeverThrows()
    {
        var rnd = new Random(3);
        var good = BuildJpeg(true);
        for (var i = 0; i < 2000; i++)
        {
            var data = (byte[])good.Clone();
            for (var k = 0; k < rnd.Next(1, 6); k++) data[rnd.Next(data.Length)] = (byte)rnd.Next(256);
            if (i % 3 == 0) data = data[..rnd.Next(data.Length)];
            _ = ExifGpsStripper.Strip(data);
        }
        Assert.False(ExifGpsStripper.Strip(Encoding.ASCII.GetBytes("not a jpeg")).ExifGpsFound);
        Assert.False(ExifGpsStripper.Strip(Array.Empty<byte>()).ExifGpsFound);
    }

    [Fact]
    public void Planner_ReplacesFile_KeepsTimes_AndUndoRestoresOriginalBytesAndTimes()
    {
        var fs = new FakeFileSystem();
        var jpeg = BuildJpeg(true);
        fs.WriteAllBytes("/p/a.jpg", jpeg);
        fs.WriteAllBytes("/p/b.jpg", BuildJpeg(true, withGps: false));
        fs.WriteAllText("/p/c.png", "x");
        var t = new DateTime(2018, 8, 8, 8, 8, 8, DateTimeKind.Utc);
        fs.SetTimes("/p/a.jpg", FileTimes.From(t, t, t));

        FileItem Item(string path, long size) => new(path, "/p", System.IO.Path.GetFileNameWithoutExtension(path),
            System.IO.Path.GetExtension(path).TrimStart('.'), false, size, t, t, 0);
        var files = new[] { Item("/p/a.jpg", jpeg.Length), Item("/p/b.jpg", 10), Item("/p/c.png", 1) };

        var plan = GpsStripPlanner.Plan(files, fs, "/tmp");

        Assert.Equal(new[] { GpsStripStatus.Ready, GpsStripStatus.NoGps, GpsStripStatus.Unsupported }, plan.Items.Select(i => i.Status));

        var result = new OperationExecutor(fs, "/journal", new QuarantineStore(fs, "/q")).Execute(plan.Operations);
        Assert.Equal(0, result.Failed);
        Assert.False(ExifGpsStripper.HasGps(ReadAll(fs, "/p/a.jpg")));
        Assert.Equal(FileTimes.From(t, t, t), fs.GetTimes("/p/a.jpg"));

        new UndoService(fs).UndoJournal(result.JournalPath);
        Assert.True(jpeg.SequenceEqual(ReadAll(fs, "/p/a.jpg")));
        Assert.Equal(FileTimes.From(t, t, t), fs.GetTimes("/p/a.jpg"));
    }

    private static byte[] ReadAll(IFileSystem fs, string path)
    {
        using var s = fs.OpenRead(path);
        using var ms = new System.IO.MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
