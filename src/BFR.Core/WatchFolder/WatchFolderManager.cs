namespace BFR.Core.WatchFolder;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>
/// Quản lý nhiều quy tắc theo dõi cùng lúc (B6). Mỗi quy tắc có một <see cref="WatchFolderService"/> riêng; nhật ký và
/// sự kiện gom về một chỗ. Chặn các quy tắc chồng lấn (thư mục nằm trong thư mục đệ quy của quy tắc khác) vì tệp sẽ bị
/// xử lý hai lần, và giới hạn <see cref="WatchRule.MaxRules"/> quy tắc.
/// </summary>
public sealed class WatchFolderManager : IDisposable
{
    private readonly IFileSystem _fs;
    private readonly WorkflowWatchProcessor _processor;
    private readonly Dictionary<string, WatchFolderService> _services = new(StringComparer.OrdinalIgnoreCase);

    public WatchFolderManager(IFileSystem fs, WorkflowWatchProcessor processor)
    {
        _fs = fs;
        _processor = processor;
    }

    public event EventHandler<(WatchRule Rule, WatchFolderLogEntry Entry)>? Logged;

    public event EventHandler<(WatchRule Rule, WatchFolderResult Result)>? BatchProcessed;

    public int RunningCount => _services.Values.Count(s => s.IsRunning);

    /// <summary>Lỗi của cả bộ quy tắc (rỗng = hợp lệ): từng quy tắc, trùng thư mục, chồng lấn, quá nhiều.</summary>
    public static IReadOnlyList<string> Validate(IReadOnlyList<WatchRule> rules, ProtectedZones? zones = null)
    {
        var errors = new List<string>();
        if (rules.Count > WatchRule.MaxRules) errors.Add(CoreText.Get("CoreWatchTooMany", "Tối đa {0} quy tắc theo dõi", WatchRule.MaxRules));

        var enabled = rules.Where(r => r.Enabled).ToList();
        foreach (var rule in enabled)
        {
            foreach (var e in rule.Validate(zones)) errors.Add($"{DisplayName(rule)}: {e}");
        }

        for (var i = 0; i < enabled.Count; i++)
        {
            for (var j = i + 1; j < enabled.Count; j++)
            {
                if (Overlaps(enabled[i], enabled[j]))
                {
                    errors.Add(CoreText.Get("CoreWatchOverlap", "{0} và {1} theo dõi chồng lấn nhau", DisplayName(enabled[i]), DisplayName(enabled[j])));
                }
            }
        }

        return errors;
    }

    /// <summary>Bắt đầu mọi quy tắc đang bật. Ném <see cref="InvalidOperationException"/> nếu bộ quy tắc không hợp lệ.</summary>
    public void Start(IReadOnlyList<WatchRule> rules)
    {
        Stop();
        var errors = Validate(rules);
        if (errors.Count > 0) throw new InvalidOperationException(errors[0]);

        foreach (var rule in rules.Where(r => r.Enabled))
        {
            var captured = rule;
            var service = new WatchFolderService(
                rule.ToOptions(),
                DisplayName(rule),
                batch => _processor.Process(batch, captured),
                _fs);

            service.Logged += (_, entry) => Logged?.Invoke(this, (captured, entry));
            service.BatchProcessed += (_, result) => BatchProcessed?.Invoke(this, (captured, result));
            service.Start();
            _services[rule.Id] = service;
        }
    }

    public void Stop()
    {
        foreach (var service in _services.Values) service.Dispose();
        _services.Clear();
    }

    private static string DisplayName(WatchRule rule) => string.IsNullOrWhiteSpace(rule.Name) ? rule.Folder : rule.Name;

    private static bool Overlaps(WatchRule a, WatchRule b)
    {
        var x = Canon(a.Folder);
        var y = Canon(b.Folder);
        if (string.Equals(x, y, StringComparison.OrdinalIgnoreCase)) return true;
        if (a.Recursive && y.StartsWith(x + "/", StringComparison.OrdinalIgnoreCase)) return true;
        if (b.Recursive && x.StartsWith(y + "/", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static string Canon(string path)
    {
        try { return Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/'); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return path.Replace('\\', '/').TrimEnd('/'); }
    }

    public void Dispose() => Stop();
}
