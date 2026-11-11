namespace BFR.Core;

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Phân tích và thay thế token BFR trong chuỗi mẫu/thay thế.
/// Token không hợp lệ hoặc thiếu dữ liệu không âm thầm biến mất:
/// giữ nguyên token và ghi lỗi vào danh sách Errors.
/// </summary>
public sealed class TokenEngine
{
    private static readonly Regex TokenRegex = new(@"\{([^{}]+)\}", RegexOptions.Compiled, RegexLimits.Timeout);

    public string Expand(string template, FileItem file, RenameState state, RuleContext ctx, ICollection<string>? errors = null)
    {
        if (string.IsNullOrEmpty(template)) return template;

        return TokenRegex.Replace(template, match =>
        {
            var token = match.Groups[1].Value;
            var value = Resolve(token, file, state, ctx, out var missing);
            if (value is null)
            {
                errors?.Add(missing ?? CoreText.Get("CoreInvalidToken", "Token không hợp lệ: {0}", "{" + token + "}"));
                return match.Value; // giữ nguyên token
            }
            return value;
        });
    }

    private static string? Resolve(string token, FileItem file, RenameState state, RuleContext ctx, out string? missing)
    {
        missing = null;
        var parts = token.Split(':', 2);
        var name = parts[0];
        var arg = parts.Length > 1 ? parts[1] : null;

        switch (name)
        {
            case "name": return state.Name;
            case "ext": return state.Extension;
            case "orig": return file.Name;
            case "parent":
                return System.IO.Path.GetFileName(file.Directory.TrimEnd('\\', '/'));
            case "n":
            {
                var value = state.GroupIndex + 1;
                var width = ParseInt(arg, 0);
                return width > 0 ? value.ToString().PadLeft(width, '0') : value.ToString();
            }
            case "created":
                return FormatDate(file.CreatedUtc, arg);
            case "modified":
                return FormatDate(file.ModifiedUtc, arg);
            case "now":
                return FormatDate(ctx.Now, arg);
            case "taken":
            {
                var metadata = ctx.MetadataFor(file);
                if (metadata is not null && metadata.TryGetValue("taken", out var taken) && !string.IsNullOrWhiteSpace(taken))
                {
                    return FormatDate(DateTime.Parse(taken, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), arg);
                }
                missing = CoreText.Get("CoreMissingData", "Thiếu dữ liệu: {0}", "{taken}");
                return null;
            }
            case "media":
            {
                var metadata = ctx.MetadataFor(file);
                if (metadata is not null && metadata.TryGetValue("media", out var media) && !string.IsNullOrWhiteSpace(media))
                {
                    return FormatDate(DateTime.Parse(media, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), arg);
                }
                missing = CoreText.Get("CoreMissingData", "Thiếu dữ liệu: {0}", "{media}");
                return null;
            }
            case "size":
                return FormatSize(file.Size, arg);
            case "rand":
            {
                var len = ParseInt(arg, 6);
                return RandomString(ctx.RandomSeed, state.Index, len);
            }
            default:
                missing = CoreText.Get("CoreInvalidToken", "Token không hợp lệ: {0}", "{" + token + "}");
                return null;
        }
    }

    private static string FormatDate(DateTime value, string? format)
    {
        var fmt = string.IsNullOrWhiteSpace(format) ? "yyyy-MM-dd" : format;
        return value.ToLocalTime().ToString(fmt, CultureInfo.InvariantCulture);
    }

    private static string FormatSize(long size, string? unit) => (unit?.ToUpperInvariant()) switch
    {
        "KB" => (size / 1024.0).ToString("0.##", CultureInfo.InvariantCulture),
        "MB" => (size / 1024.0 / 1024.0).ToString("0.##", CultureInfo.InvariantCulture),
        "GB" => (size / 1024.0 / 1024.0 / 1024.0).ToString("0.##", CultureInfo.InvariantCulture),
        _ => size.ToString(CultureInfo.InvariantCulture)
    };

    private static int ParseInt(string? s, int fallback) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static string RandomString(string seed, int index, int length)
    {
        // Sinh chuỗi xác định từ seed + index để preview và áp dụng khớp nhau.
        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}:{index}"));
        const string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
        var sb = new StringBuilder(length);
        for (int i = 0; i < length; i++)
        {
            sb.Append(alphabet[hash[i % hash.Length] % alphabet.Length]);
        }
        return sb.ToString();
    }
}
