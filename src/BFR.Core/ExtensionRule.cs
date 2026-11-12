namespace BFR.Core;

using System.Globalization;

/// <summary>
/// Quy tắc Phần mở rộng (FR-B9).
/// Mặc định bảo toàn đuôi; đổi đuôi cần bật cờ cho phép.
/// </summary>
public sealed class ExtensionRule : IRule
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public bool Enabled { get; set; } = true;

    public ExtensionMode Mode { get; set; } = ExtensionMode.Keep;
    public string NewExtension { get; set; } = string.Empty;
    public bool AllowExtensionChange { get; set; } = false;

    public RenameState Apply(FileItem file, RenameState state, RuleContext ctx)
    {
        var trace = new List<string>(state.Trace) { $"{Id}: Extension" };

        if (!AllowExtensionChange && Mode != ExtensionMode.Keep)
        {
            trace.Add($"{Id}: " + CoreText.Get("CoreExtDisabled", "Đổi đuôi bị tắt (AllowExtensionChange=false)"));
            return state with { Trace = trace };
        }

        var ext = Mode switch
        {
            ExtensionMode.Lower => state.Extension.ToLowerInvariant(),
            ExtensionMode.Upper => state.Extension.ToUpperInvariant(),
            ExtensionMode.Set => NewExtension.TrimStart('.'),
            ExtensionMode.Remove => string.Empty,
            _ => state.Extension
        };

        return state with { Extension = ext, Trace = trace };
    }

    public enum ExtensionMode { Keep, Lower, Upper, Set, Remove }
}
