namespace BFR.Core;

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Quy tắc Bỏ dấu và chuẩn hóa (FR-B8).
/// </summary>
public sealed class SanitizeRule : IRule
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public bool Enabled { get; set; } = true;

    public bool RemoveDiacritics { get; set; } = false;
    public bool NormalizeNfc { get; set; } = false;
    public bool ReplaceInvalidChars { get; set; } = false;
    public char InvalidCharReplacement { get; set; } = '_';
    public SpaceMode Spaces { get; set; } = SpaceMode.Keep;
    public bool Slugify { get; set; } = false;

    public RenameState Apply(FileItem file, RenameState state, RuleContext ctx)
    {
        var trace = new List<string>(state.Trace) { $"{Id}: Sanitize" };
        var s = state.Name;

        if (RemoveDiacritics) s = RemoveVietnameseDiacritics(s);
        if (NormalizeNfc) s = s.Normalize(NormalizationForm.FormC);
        if (ReplaceInvalidChars) s = ReplaceInvalid(s);
        if (Slugify) s = SlugifyText(s);
        else s = ApplySpaceMode(s, Spaces);

        return state with { Name = s, Trace = trace };
    }

    public static string RemoveVietnameseDiacritics(string s)
    {
        s = s.Replace('đ', 'd').Replace('Đ', 'D');
        var formD = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var ch in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private string ReplaceInvalid(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (ch < 32 || "<>:\"/\\|?*".Contains(ch)) sb.Append(InvalidCharReplacement);
            else sb.Append(ch);
        }
        return sb.ToString();
    }

    private static string ApplySpaceMode(string s, SpaceMode mode) => mode switch
    {
        SpaceMode.Dash => Regex.Replace(s.Trim(), @"\s+", "-", RegexOptions.None, RegexLimits.Timeout),
        SpaceMode.Underscore => Regex.Replace(s.Trim(), @"\s+", "_", RegexOptions.None, RegexLimits.Timeout),
        SpaceMode.Remove => Regex.Replace(s, @"\s+", string.Empty, RegexOptions.None, RegexLimits.Timeout),
        _ => s
    };

    private static string SlugifyText(string s)
    {
        s = RemoveVietnameseDiacritics(s);
        s = s.Normalize(NormalizationForm.FormC);
        s = s.ToLowerInvariant();
        s = Regex.Replace(s, @"[^a-z0-9]+", "-", RegexOptions.None, RegexLimits.Timeout);
        return s.Trim('-');
    }

    public enum SpaceMode { Keep, Dash, Underscore, Remove }
}
