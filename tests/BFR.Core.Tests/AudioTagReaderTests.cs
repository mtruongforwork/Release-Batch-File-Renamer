namespace BFR.Core.Tests;

using BFR.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

public class AudioTagReaderTests
{
    private static byte[] SyncSafe(int n) => new[] { (byte)((n >> 21) & 0x7F), (byte)((n >> 14) & 0x7F), (byte)((n >> 7) & 0x7F), (byte)(n & 0x7F) };

    private static byte[] Frame23(string id, byte encoding, byte[] text)
    {
        var size = text.Length + 1;
        return Encoding.ASCII.GetBytes(id)
            .Concat(new[] { (byte)(size >> 24), (byte)(size >> 16), (byte)(size >> 8), (byte)size, (byte)0, (byte)0, encoding })
            .Concat(text).ToArray();
    }

    private static byte[] Id3v23(params byte[][] frames)
    {
        var body = frames.SelectMany(f => f).ToArray();
        return Encoding.ASCII.GetBytes("ID3").Concat(new byte[] { 3, 0, 0 }).Concat(SyncSafe(body.Length)).Concat(body).ToArray();
    }

    private static IReadOnlyDictionary<string, string> Parse(byte[] data, string ext = "mp3") =>
        AudioTagReader.Parse(new MemoryStream(data), ext);

    [Fact]
    public void Id3v23_ReadsLatin1AndUtf16Frames()
    {
        var data = Id3v23(
            Frame23("TPE1", 0, Encoding.Latin1.GetBytes("Daft Punk")),
            Frame23("TALB", 1, new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes("Discovery")).ToArray()),
            Frame23("TRCK", 0, Encoding.Latin1.GetBytes("3/14")),
            Frame23("TIT2", 3, Encoding.UTF8.GetBytes("Aerodynamic")));

        var map = Parse(data);

        Assert.Equal("Daft Punk", map["artist"]);
        Assert.Equal("Discovery", map["album"]);
        Assert.Equal("3", map["track"]);
        Assert.Equal("Aerodynamic", map["title"]);
    }

    [Fact]
    public void Id3v1_IsUsedWhenNoV2()
    {
        var tag = new byte[128];
        Encoding.ASCII.GetBytes("TAG").CopyTo(tag, 0);
        Encoding.ASCII.GetBytes("Song").CopyTo(tag, 3);
        Encoding.ASCII.GetBytes("Band").CopyTo(tag, 33);
        Encoding.ASCII.GetBytes("Record").CopyTo(tag, 63);
        Encoding.ASCII.GetBytes("1999").CopyTo(tag, 93);
        tag[126] = 7;
        var data = new byte[500].Concat(tag).ToArray();

        var map = Parse(data);

        Assert.Equal("Band", map["artist"]);
        Assert.Equal("Record", map["album"]);
        Assert.Equal("1999", map["year"]);
        Assert.Equal("7", map["track"]);
    }

    [Fact]
    public void Flac_ReadsVorbisComments()
    {
        static byte[] Le(int n) => BitConverter.GetBytes(n);
        var comments = new[] { "ARTIST=Bjork", "album=Homogenic", "TRACKNUMBER=02", "DATE=1997-09-22" };
        var block = Le(3).Concat(Encoding.ASCII.GetBytes("abc")).Concat(Le(comments.Length))
            .Concat(comments.SelectMany(c => Le(Encoding.UTF8.GetByteCount(c)).Concat(Encoding.UTF8.GetBytes(c)))).ToArray();
        var data = Encoding.ASCII.GetBytes("fLaC")
            .Concat(new byte[] { 0x84, (byte)(block.Length >> 16), (byte)(block.Length >> 8), (byte)block.Length })
            .Concat(block).ToArray();

        var map = Parse(data, "flac");

        Assert.Equal("Bjork", map["artist"]);
        Assert.Equal("Homogenic", map["album"]);
        Assert.Equal("02", map["track"]);
        Assert.Equal("1997", map["year"]);
    }

    [Fact]
    public void CorruptOrRandomData_NeverThrows()
    {
        var rnd = new Random(7);
        for (var i = 0; i < 500; i++)
        {
            var data = new byte[rnd.Next(0, 400)];
            rnd.NextBytes(data);
            if (i % 2 == 0 && data.Length > 10) { data[0] = (byte)'I'; data[1] = (byte)'D'; data[2] = (byte)'3'; data[3] = (byte)(2 + i % 3); }
            if (i % 5 == 0 && data.Length > 4) { Encoding.ASCII.GetBytes("fLaC").CopyTo(data, 0); }
            _ = Parse(data);
            _ = Parse(data, "flac");
        }
    }

    [Fact]
    public void Tokens_FillFromTags_AndReportMissing()
    {
        var file = new FileItem("/m/a.mp3", "/m", "a", "mp3", false, 1, DateTime.UtcNow, DateTime.UtcNow, 0);
        var meta = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            [file.FullPath] = new Dictionary<string, string> { ["artist"] = "AC/DC", ["track"] = "3" }
        };
        var ctx = new RuleContext(DateTime.UtcNow, "s", null, meta);
        var errors = new List<string>();
        var engine = new TokenEngine();
        var state = new RenameState("a", "mp3", 0, 0, new List<string>());

        var text = engine.Expand("{track:2} - {artist} - {album}", file, state, ctx, errors);

        Assert.StartsWith("03 - AC_DC - ", text);
        Assert.Single(errors);
        Assert.True(MetadataRequirement.HasMetadataToken("{artist}"));
    }
}
