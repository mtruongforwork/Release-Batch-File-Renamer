namespace BFR.Core;

using System;
using System.Globalization;
using System.Threading;

/// <summary>
/// Chuỗi hiển thị do lớp Core sinh ra (chi tiết xung đột, vết quy tắc, nhật ký theo dõi, lỗi hoàn tác...).
/// Core không biết ngôn ngữ giao diện: ứng dụng gắn <see cref="Resolver"/> để tra chuỗi theo ngôn ngữ đang dùng;
/// nếu chưa gắn (CLI, kiểm thử) thì dùng bản tiếng Việt viết sẵn tại chỗ gọi.
/// </summary>
public static class CoreText
{
    /// <summary>Trả về mẫu chuỗi (có thể chứa {0}, {1}...) của khoá, hoặc null nếu không có.</summary>
    public static Func<string, string?>? Resolver { get; set; }

    private static readonly AsyncLocal<Func<string, string?>?> Scoped = new();

    public static string? TryGet(string key) => (Scoped.Value ?? Resolver)?.Invoke(key);

    /// <summary>Bộ tra cứu chỉ có hiệu lực trong luồng/ngữ cảnh gọi hiện tại (dùng cho kiểm thử song song).</summary>
    public static IDisposable UseScoped(Func<string, string?> resolver)
    {
        var previous = Scoped.Value;
        Scoped.Value = resolver;
        return new Restore(previous);
    }

    private sealed class Restore(Func<string, string?>? previous) : IDisposable
    {
        public void Dispose() => Scoped.Value = previous;
    }

    public static string Get(string key, string fallback, params object?[] args)
    {
        var template = TryGet(key) ?? fallback;
        return args.Length == 0 ? template : string.Format(CultureInfo.CurrentCulture, template, args);
    }
}
