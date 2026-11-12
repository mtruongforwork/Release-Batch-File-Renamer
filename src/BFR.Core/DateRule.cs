namespace BFR.Core;

using System.Globalization;

/// <summary>
/// Quy tắc Ngày tháng (FR-B4).
/// Nguồn: Created, Modified, Taken (EXIF), Media (video/audio - Pro), Now.
/// Chuỗi dự phòng do người dùng chọn.
/// </summary>
public sealed class DateRule : IRule
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public bool Enabled { get; set; } = true;

    public DateSource Source { get; set; } = DateSource.Created;
    public string Format { get; set; } = "yyyy-MM-dd";
    public DatePosition Position { get; set; } = DatePosition.Start;
    public string Separator { get; set; } = "_";
    public List<DateSource> Fallback { get; set; } = new() { DateSource.Taken, DateSource.Media, DateSource.Created, DateSource.Modified };
    public bool UseUtc { get; set; } = false;

    public RenameState Apply(FileItem file, RenameState state, RuleContext ctx)
    {
        var trace = new List<string>(state.Trace) { $"{Id}: Date" };

        var (value, usedSource) = ResolveDate(file, ctx);
        var formatted = value.ToString(Format, CultureInfo.InvariantCulture);
        if (usedSource != Source) trace.Add($"{Id}: " + CoreText.Get("CoreDateFallback", "dự phòng {0}", usedSource));

        var result = Position switch
        {
            DatePosition.Start => $"{formatted}{Separator}{state.Name}",
            DatePosition.End => $"{state.Name}{Separator}{formatted}",
            DatePosition.ReplaceAll => formatted,
            _ => state.Name
        };

        return state with { Name = result, Trace = trace };
    }

    private (DateTime Value, DateSource Used) ResolveDate(FileItem file, RuleContext ctx)
    {
        var order = new List<DateSource> { Source };
        foreach (var f in Fallback)
        {
            if (!order.Contains(f)) order.Add(f);
        }

        foreach (var source in order)
        {
            if (TryGet(source, file, ctx, out var value)) return (value, source);
        }

        return (ctx.Now, DateSource.Now);
    }

    private bool TryGet(DateSource source, FileItem file, RuleContext ctx, out DateTime value)
    {
        value = default;
        switch (source)
        {
            case DateSource.Created:
                value = UseUtc ? file.CreatedUtc : file.CreatedUtc.ToLocalTime();
                return true;
            case DateSource.Modified:
                value = UseUtc ? file.ModifiedUtc : file.ModifiedUtc.ToLocalTime();
                return true;
            case DateSource.Now:
                value = UseUtc ? ctx.Now.ToUniversalTime() : ctx.Now;
                return true;
            case DateSource.Taken:
            {
                var metadata = ctx.MetadataFor(file);
                if (metadata is not null && metadata.TryGetValue("taken", out var taken)
                    && DateTime.TryParse(taken, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                {
                    value = UseUtc ? parsed.ToUniversalTime() : parsed;
                    return true;
                }
                return false;
            }
            case DateSource.Media:
            {
                var metadata = ctx.MetadataFor(file);
                if (metadata is not null && metadata.TryGetValue("media", out var media)
                    && DateTime.TryParse(media, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedMedia))
                {
                    value = UseUtc ? parsedMedia.ToUniversalTime() : parsedMedia;
                    return true;
                }
                return false;
            }
            default:
                return false;
        }
    }

    public enum DateSource { Created, Modified, Taken, Media, Now }
    public enum DatePosition { Start, End, ReplaceAll }
}
