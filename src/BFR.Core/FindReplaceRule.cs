namespace BFR.Core;

using System.Text.RegularExpressions;

/// <summary>
/// Quy tắc Tìm và thay / Regex (FR-B1, FR-B2).
/// Thay thế token BFR được xử lý sau khi áp Regex.
/// </summary>
public sealed class FindReplaceRule : IRule
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public bool Enabled { get; set; } = true;

    public string Pattern { get; set; } = string.Empty;
    public string Replacement { get; set; } = string.Empty;
    public bool IsRegex { get; set; } = false;
    public bool IgnoreCase { get; set; } = false;
    public bool ReplaceAll { get; set; } = true;

    private sealed record CachedRegex(string Pattern, bool IgnoreCase, Regex Regex);

    // Ghi/đọc một tham chiếu là nguyên tử; hai luồng cùng dựng chỉ tốn thêm một lần dựng.
    private volatile CachedRegex? _regexCache;
    public ApplyScope Scope { get; set; } = ApplyScope.Name; // Tên / Đuôi / Cả hai

    public RenameState Apply(FileItem file, RenameState state, RuleContext ctx)
    {
        var trace = new List<string>(state.Trace) { $"{Id}: FindReplace" };

        // Token BFR trong chuỗi thay thế được mở rộng theo đúng tệp/ngữ cảnh.
        var replacement = new TokenEngine().Expand(Replacement, file, state, ctx, trace);

        var targetName = state.Name;
        var targetExt = state.Extension;

        var applyName = Scope is ApplyScope.Name or ApplyScope.Both;
        var applyExt = Scope is ApplyScope.Extension or ApplyScope.Both;

        if (applyName)
        {
            targetName = ApplyOne(targetName, file);
        }
        if (applyExt && !string.IsNullOrEmpty(targetExt))
        {
            targetExt = ApplyOne(targetExt, file);
        }

        return state with { Name = targetName, Extension = targetExt, Trace = trace };

        // Dựng Regex một lần cho mỗi (mẫu, hoa/thường): trước đây dựng lại (RegexOptions.Compiled) cho TỪNG tệp,
        // mỗi lần vài ms nên 10.000 tệp mất cả chục giây.
        Regex GetRegex()
        {
            var cached = _regexCache;
            if (cached is not null && cached.Pattern == Pattern && cached.IgnoreCase == IgnoreCase) return cached.Regex;

            var options = IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
            var created = new Regex(Pattern, options, RegexLimits.Timeout);
            _regexCache = new CachedRegex(Pattern, IgnoreCase, created);
            return created;
        }

        string ApplyOne(string input, FileItem f)
        {
            if (string.IsNullOrEmpty(input)) return input;
            try
            {
                if (IsRegex)
                {
                    var regex = GetRegex();
                    var result = ReplaceAll ? regex.Replace(input, replacement) : regex.Replace(input, replacement, 1);
                    return result;
                }

                // Văn bản thuần: không diễn giải $1, {name}... như Regex.
                var comparison = IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (ReplaceAll) return input.Replace(Pattern, replacement, comparison);

                var index = input.IndexOf(Pattern, comparison);
                if (index < 0) return input;
                return input[..index] + replacement + input[(index + Pattern.Length)..];
            }
            catch (RegexMatchTimeoutException)
            {
                // Không treo ứng dụng; giữ nguyên và đánh dấu trong trace
                trace.Add($"{Id}: " + CoreText.Get("CoreRegexTimeout", "Regex quá chậm (timeout)"));
                return input;
            }
            catch (ArgumentException)
            {
                // Mẫu không hợp lệ; giữ nguyên
                trace.Add($"{Id}: " + CoreText.Get("CoreRegexInvalid", "Mẫu Regex không hợp lệ"));
                return input;
            }
        }
    }

    public enum ApplyScope { Name, Extension, Both }
}
