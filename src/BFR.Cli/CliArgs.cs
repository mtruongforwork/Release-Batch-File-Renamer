namespace BFR.Cli;

/// <summary>
/// Phân tích tham số dòng lệnh: vị trí, tuỳ chọn có giá trị (lặp được) và cờ. Từ chối tuỳ chọn lạ để gõ nhầm không bị bỏ qua âm thầm.
/// </summary>
internal sealed class CliArgs
{
    private readonly Dictionary<string, List<string>> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Positional { get; } = new();
    public string? Error { get; private set; }

    /// <param name="valued">Tuỳ chọn nhận một giá trị (<c>--to dir</c>).</param>
    /// <param name="flags">Tuỳ chọn là cờ (<c>--copy</c>).</param>
    public static CliArgs Parse(string[] args, string[] valued, string[] flags)
    {
        var result = new CliArgs();
        var valuedSet = new HashSet<string>(valued.Concat(CommonValued), StringComparer.OrdinalIgnoreCase);
        var flagSet = new HashSet<string>(flags.Concat(CommonFlags), StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a == "--")
            {
                result.Positional.AddRange(args[(i + 1)..]);
                break;
            }

            if (!a.StartsWith("--", StringComparison.Ordinal))
            {
                result.Positional.Add(a);
                continue;
            }

            string? inline = null;
            var eq = a.IndexOf('=');
            var name = eq > 0 ? a[..eq] : a;
            if (eq > 0) inline = a[(eq + 1)..];

            if (valuedSet.Contains(name))
            {
                var value = inline ?? (i + 1 < args.Length ? args[++i] : null);
                if (value is null) { result.Error = $"Thiếu giá trị cho {name}"; return result; }
                if (!result._values.TryGetValue(name, out var list)) result._values[name] = list = new List<string>();
                list.Add(value);
            }
            else if (flagSet.Contains(name))
            {
                result._flags.Add(name);
            }
            else
            {
                result.Error = $"Tham số không hợp lệ: {a}";
                return result;
            }
        }

        return result;
    }

    public static readonly string[] CommonValued = { "--format", "--file-list", "--include", "--exclude", "--depth" };
    public static readonly string[] CommonFlags = { "--dry-run", "--yes", "--recursive" };

    public bool Has(string flag) => _flags.Contains(flag);

    public string? Get(string name) => _values.TryGetValue(name, out var l) ? l[^1] : null;

    public IReadOnlyList<string> GetAll(string name) => _values.TryGetValue(name, out var l) ? l : Array.Empty<string>();

    public int GetInt(string name, int fallback) => int.TryParse(Get(name), out var v) ? v : fallback;
}
