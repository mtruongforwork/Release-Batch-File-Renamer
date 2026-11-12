namespace BFR.Core.PropertyTests;

using BFR.Core;

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

/// <summary>Organize: không ghi ra ngoài thư mục gốc, không mất tệp, và thực thi rồi hoàn tác trả đúng bố cục cũ.</summary>
public class OrganizePropertyTests
{
    private static readonly string[] Templates =
    {
        "{type}", @"{year}\{month}", "{ext.upper}", @"{type}\{name.first:1}", @"..\{ext}", "{size.bucket}", @"{parent}\{ext.lower}"
    };

    private static readonly string[] Names = { "a", "B", "img 1", "...x", "con", "Éclair", "x.y" };
    private static readonly string[] Exts = { "jpg", "txt", "MP3", "zip", "", "pdf" };

    [Fact]
    public void Plan_StaysInsideRoot_AndExecuteUndoRestoresLayout()
    {
        for (var seed = 0; seed < 300; seed++)
        {
            var rnd = new Random(seed);
            var fs = new FakeFileSystem();
            var files = new List<FileItem>();
            var n = rnd.Next(1, 15);
            for (var i = 0; i < n; i++)
            {
                var dir = rnd.Next(2) == 0 ? "/root" : "/root/sub" + rnd.Next(3);
                var name = Names[rnd.Next(Names.Length)] + rnd.Next(4);
                var ext = Exts[rnd.Next(Exts.Length)];
                var path = ext.Length == 0 ? $"{dir}/{name}" : $"{dir}/{name}.{ext}";
                if (fs.FileExists(path)) continue;
                fs.WriteAllText(path, "c" + i);
                var date = new DateTime(2000 + rnd.Next(25), 1 + rnd.Next(12), 1 + rnd.Next(28), 0, 0, 0, DateTimeKind.Utc);
                files.Add(new FileItem(path, dir, name, ext, false, rnd.Next(0, 300_000_000), date, date, i));
            }

            var before = Layout(fs);
            var options = new OrganizeOptions("/root", Templates[rnd.Next(Templates.Length)],
                rnd.Next(2) == 0 ? OrganizeMode.Move : OrganizeMode.Copy,
                rnd.Next(2) == 0 ? OrganizeConflictPolicy.Rename : OrganizeConflictPolicy.Skip);
            var plan = OrganizePlanner.Plan(files, options, new RuleContext(DateTime.UtcNow, "s"), fs);

            foreach (var op in plan.Operations)
            {
                var to = op.To.Replace('\\', '/');
                Assert.True(to.StartsWith("/root/", StringComparison.OrdinalIgnoreCase) && !to.Contains("/../") && !to.EndsWith("/.."),
                    $"seed {seed}: đích ngoài gốc: {to}");
            }
            Assert.Equal(plan.Operations.Count, plan.Operations.Select(o => o.To.ToLowerInvariant()).Distinct().Count());

            var result = new OperationExecutor(fs, "/journal", new QuarantineStore(fs, "/q")).Execute(plan.Operations);
            Assert.Equal(0, result.Failed);
            var undo = new UndoService(fs).UndoJournal(result.JournalPath);

            Assert.Equal(0, undo.Failed);
            Assert.Equal(before, Layout(fs));
        }
    }

    private static string Layout(FakeFileSystem fs) =>
        string.Join("\n",
            fs.GetFiles("/root", "*", true).OrderBy(x => x, StringComparer.Ordinal).Select(f => f + "=" + fs.ReadAllText(f))
                .Concat(fs.GetDirectories("/root", true).OrderBy(x => x, StringComparer.Ordinal).Select(d => "D " + d)));
}
