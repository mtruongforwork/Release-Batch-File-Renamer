namespace BFR.Core.PropertyTests;

using BFR.Core;
using Xunit;

/// <summary>
/// Property-based tests cho v1.4: bất biến của ConflictResolver (B3) và độ bền của ListParser /
/// các quy tắc danh sách (B2) trên dữ liệu ngẫu nhiên (hạt giống cố định để lặp lại được).
/// </summary>
public class V14PropertyTests
{
    private static PreviewItem Item(string oldName, string newName, string dir)
    {
        var name = Path.GetFileNameWithoutExtension(oldName);
        var ext = Path.GetExtension(oldName).TrimStart('.');
        var file = new FileItem(Path.Combine(dir, oldName), dir, name, ext, false, 1, DateTime.UtcNow, DateTime.UtcNow, 0);
        return new PreviewItem(file, oldName, newName, PreviewStatus.Ok, new List<string>());
    }

    [Fact]
    public void ConflictResolver_AlwaysProducesUniqueNames_PerDirectory_AndAvoidsOutsideNames()
    {
        var rnd = new Random(1234);
        string[] stems = { "a", "b", "A", "photo", "photo (2)", "x" };
        string[] exts = { ".txt", ".TXT", ".jpg", "" };
        string[] dirs = { @"C:\one", @"C:\two" };

        for (var round = 0; round < 300; round++)
        {
            var count = rnd.Next(1, 25);
            var items = new List<PreviewItem>();
            for (var i = 0; i < count; i++)
            {
                var dir = dirs[rnd.Next(dirs.Length)];
                var oldName = $"old{i}{exts[rnd.Next(exts.Length)]}";
                var newName = rnd.Next(5) == 0 ? oldName : stems[rnd.Next(stems.Length)] + exts[rnd.Next(exts.Length)];
                items.Add(Item(oldName, newName, dir));
            }

            var outside = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var dir in dirs)
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (var k = 0; k < rnd.Next(0, 4); k++) set.Add(stems[rnd.Next(stems.Length)] + exts[rnd.Next(exts.Length)]);
                outside[ConflictDetector.NormalizeDirectory(dir)] = set;
            }

            IReadOnlySet<string>? OutsideFor(PreviewItem it) => outside[ConflictDetector.NormalizeDirectory(it.File.Directory)];

            var result = ConflictResolver.Resolve(items, OutsideFor);

            Assert.Equal(items.Count, result.Items.Count);

            foreach (var group in result.Items.GroupBy(i => ConflictDetector.NormalizeDirectory(i.File.Directory)))
            {
                var names = group.Select(i => i.NewName).ToList();
                Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            }

            // Tệp đổi tên không được rơi vào tên đang có ngoài lô (tệp giữ nguyên tên thì không xét).
            foreach (var (before, after) in items.Zip(result.Items))
            {
                if (before.NewName == before.OldName) { Assert.Equal(before.NewName, after.NewName); continue; }
                if (after.NewName != before.NewName)
                    Assert.DoesNotContain(after.NewName, OutsideFor(after)!);
            }

            // Chạy lần hai không đổi gì nữa (idempotent).
            var again = ConflictResolver.Resolve(result.Items, OutsideFor);
            Assert.Equal(0, again.ResolvedCount);
        }
    }

    [Fact]
    public void ConflictResolver_NeverTouchesAlreadyUniqueBatches()
    {
        var rnd = new Random(99);
        for (var round = 0; round < 200; round++)
        {
            var count = rnd.Next(1, 30);
            var items = Enumerable.Range(0, count)
                .Select(i => Item($"old{i}.txt", $"new{i}.txt", @"C:\a"))
                .ToList();

            var result = ConflictResolver.Resolve(items, _ => null);

            Assert.Equal(0, result.ResolvedCount);
            Assert.Equal(items.Select(i => i.NewName), result.Items.Select(i => i.NewName));
        }
    }

    [Fact]
    public void ListParser_NeverThrows_AndRespectsMaxRows_OnRandomText()
    {
        var rnd = new Random(7);
        const string alphabet = "ab \t;,\"\r\n\uFEFFé";

        for (var round = 0; round < 2000; round++)
        {
            var len = rnd.Next(0, 80);
            var chars = new char[len];
            for (var i = 0; i < len; i++) chars[i] = alphabet[rnd.Next(alphabet.Length)];
            var text = new string(chars);
            var max = rnd.Next(0, 8);

            var rows = ListParser.Parse(text, max);

            Assert.True(rows.Count <= max);
            Assert.All(rows, r => Assert.NotNull(r.First));
            Assert.True(ListParser.Parse(text).Count >= rows.Count);
        }
    }

    [Fact]
    public void ListRules_NeverThrow_AndAreDeterministic_OnRandomInput()
    {
        var rnd = new Random(2026);
        const string alphabet = "abc _-().[]\t;,\"é";
        var ctx = new RuleContext(DateTime.UtcNow, "seed");

        string Random(int max)
        {
            var len = rnd.Next(0, max);
            var chars = new char[len];
            for (var i = 0; i < len; i++) chars[i] = alphabet[rnd.Next(alphabet.Length)];
            return new string(chars);
        }

        for (var round = 0; round < 500; round++)
        {
            var name = Random(20);
            var file = new FileItem(@"C:\a\" + name + ".txt", @"C:\a", name, "txt", false, 1, DateTime.UtcNow, DateTime.UtcNow, 0);
            var state = new RenameState(name, "txt", rnd.Next(0, 5), rnd.Next(0, 5), new List<string>());

            IRule[] rules =
            {
                new ListMapRule { Mapping = Random(60), IgnoreCase = rnd.Next(2) == 0 },
                new ListMapRule { Mapping = Random(60), Mode = ListMapRule.ListMode.ByOrder },
                new ListMapRule { Mapping = Random(60), MatchBy = ListMapRule.MatchTarget.FileName },
                new ReplaceDictRule { Pairs = Random(60) }
            };

            foreach (var rule in rules)
            {
                var a = rule.Apply(file, state, ctx);
                var b = rule.Apply(file, state, ctx);
                Assert.Equal(a.Name, b.Name);
                Assert.Equal(a.Extension, b.Extension);
            }
        }
    }
}
