namespace BFR.Core.PropertyTests;

using BFR.Core;

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

/// <summary>Duplicates: mỗi nhóm luôn còn ≥ 1 tệp; nhóm tìm được chính xác theo nội dung; hoàn tác trả đủ tệp.</summary>
public class DuplicatePropertyTests
{
    [Fact]
    public void EveryGroupKeepsAtLeastOneFile_AndUndoRestoresAll()
    {
        for (var seed = 0; seed < 300; seed++)
        {
            var rnd = new Random(seed);
            var fs = new FakeFileSystem();
            var files = new List<FileItem>();
            var n = rnd.Next(2, 16);
            for (var i = 0; i < n; i++)
            {
                var content = new byte[rnd.Next(1, 4)];
                for (var k = 0; k < content.Length; k++) content[k] = (byte)rnd.Next(3); // nhiều trùng lặp
                var path = $"/d{rnd.Next(3)}/f{i}.bin";
                fs.WriteAllBytes(path, content);
                var date = new DateTime(2000 + rnd.Next(24), 1, 1, 0, 0, 0, DateTimeKind.Utc);
                files.Add(new FileItem(path, path[..path.LastIndexOf('/')], $"f{i}", "bin", false, content.Length, date, date, i));
            }

            var groups = new DuplicateFinder(fs).Find(files, new DuplicateOptions());

            // Đúng theo định nghĩa: cùng nội dung ⇔ cùng nhóm.
            var expected = files.GroupBy(f => Convert.ToBase64String(fs.OpenRead(f.FullPath).ReadAllBytesAndClose()))
                .Where(g => g.Count() > 1).Select(g => string.Join(",", g.Select(f => f.FullPath).OrderBy(x => x))).OrderBy(x => x).ToList();
            var actual = groups.Select(g => string.Join(",", g.Files.Select(f => f.FullPath).OrderBy(x => x))).OrderBy(x => x).ToList();
            Assert.Equal(expected, actual);

            var rule = (KeepRule)rnd.Next(5);
            var keepers = groups.Select(g => DuplicatePlanner.Choose(g, rule, new[] { "/d1" })).ToList();
            var plan = DuplicatePlanner.Plan(groups, keepers, rnd.Next(2) == 0 ? DuplicateAction.Quarantine : DuplicateAction.MoveToFolder, fs, "/moved");
            var result = new OperationExecutor(fs, "/journal", new QuarantineStore(fs, "/q")).Execute(plan.Operations);
            Assert.Equal(0, result.Failed);

            foreach (var g in groups)
            {
                Assert.True(g.Files.Count(f => fs.FileExists(f.FullPath)) >= 1, $"seed {seed}: nhóm mất hết bản");
            }

            new UndoService(fs).UndoJournal(result.JournalPath);
            Assert.All(files, f => Assert.True(fs.FileExists(f.FullPath), $"seed {seed}: thiếu {f.FullPath}"));
        }
    }
}

internal static class StreamExtensions
{
    public static byte[] ReadAllBytesAndClose(this System.IO.Stream s)
    {
        using (s)
        {
            using var ms = new System.IO.MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }
    }
}
