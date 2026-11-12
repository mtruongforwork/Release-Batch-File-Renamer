namespace BFR.Core;

using System.Collections.Generic;

/// <summary>
/// Đọc metadata (EXIF/media) theo yêu cầu cho một tệp.
/// </summary>
public interface IMetadataProvider
{
    IReadOnlyDictionary<string, string> Read(FileItem file);
}

/// <summary>
/// Xác định chuỗi quy tắc hiện tại có cần đọc metadata hay không (tránh đọc EXIF vô ích).
/// </summary>
public static class MetadataRequirement
{
    public static bool NeedsMetadata(RulePipeline pipeline)
    {
        foreach (var rule in pipeline.Rules)
        {
            switch (rule)
            {
                case DateRule d when d.Source is DateRule.DateSource.Taken or DateRule.DateSource.Media:
                    return true;
                case TemplateRule t when HasMetadataToken(t.Template):
                    return true;
                case InsertRule i when HasMetadataToken(i.Text):
                    return true;
                case FindReplaceRule f when HasMetadataToken(f.Replacement) || HasMetadataToken(f.Pattern):
                    return true;
                case ExtensionRule e when HasMetadataToken(e.NewExtension):
                    return true;
            }
        }
        return false;
    }

    /// <summary>True nếu văn bản chứa token cần đọc metadata ({taken} hoặc {media}).</summary>
    public static bool HasMetadataToken(string? text) =>
        HasTakenToken(text) || HasMediaToken(text) || HasAudioTagToken(text);

    /// <summary>True nếu chứa token tag nhạc ({artist}, {album}, {title}, {track}).</summary>
    public static bool HasAudioTagToken(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        return text.Contains("{artist", System.StringComparison.OrdinalIgnoreCase)
            || text.Contains("{album", System.StringComparison.OrdinalIgnoreCase)
            || text.Contains("{title", System.StringComparison.OrdinalIgnoreCase)
            || text.Contains("{track", System.StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasTakenToken(string? text) =>
        !string.IsNullOrEmpty(text) && text.Contains("{taken", System.StringComparison.OrdinalIgnoreCase);

    public static bool HasMediaToken(string? text) =>
        !string.IsNullOrEmpty(text) && text.Contains("{media", System.StringComparison.OrdinalIgnoreCase);
}
