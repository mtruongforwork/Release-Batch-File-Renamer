namespace BFR.Core.WatchFolder;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

/// <summary>
/// Một dòng nhật ký của Watch Folder.
/// </summary>
public sealed record WatchFolderLogEntry(
    DateTime TimestampUtc,
    string Level,
    string Message,
    string? Path = null)
{
    public string Format() =>
        $"[{TimestampUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}] {Level.ToUpperInvariant(),-7} {Message}"
             + (string.IsNullOrEmpty(Path) ? "" : $" — {Path}");
}

/// <summary>
/// Theo dõi một thư mục và tự áp dụng preset cho tệp mới (FR-D4, tính năng Pro v1.2).
///
/// Cơ chế:
/// - <see cref="FileSystemWatcher"/> chỉ dùng để phát sự kiện; quyết định "tệp đã ổn định"
///   do <see cref="WatchFolderPlanner"/> đảm nhiệm (chờ settle delay để không đổi tên tệp
///   đang được sao chép).
/// - Chống thao tác lặp: bỏ qua tệp đã biết từ trước, tệp đã xử lý, và tệp do chính
///   Watch Folder tạo ra (nếu không, mỗi lần đổi tên sẽ sinh sự kiện mới và vòng lặp).
/// </summary>
public sealed class WatchFolderService : IDisposable
{
    private readonly WatchFolderOptions _options;
    private readonly string _label;
    private readonly Func<IReadOnlyList<string>, WatchFolderResult> _process;
    private readonly IFileSystem _fs;
    private readonly Action<WatchFolderLogEntry>? _logger;
    private readonly WatchFolderPlanner _planner = new();
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _produced = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    private FileSystemWatcher? _watcher;
    private Timer? _timer;
    private bool _disposed;

    public WatchFolderService(
        WatchFolderOptions options,
        Preset preset,
        WatchFolderProcessor processor,
        IFileSystem fs,
        Action<WatchFolderLogEntry>? logger = null)
    {
        _options = options;
        _label = preset.Name;
        _process = batch => processor.Process(batch, preset, options);
        _fs = fs;
        _logger = logger;
    }

    /// <summary>
    /// Theo dõi với một bộ xử lý lô tuỳ ý (ví dụ chạy cả workflow cho mỗi lô tệp mới).
    /// <paramref name="label"/> chỉ dùng để ghi nhật ký.
    /// </summary>
    public WatchFolderService(
        WatchFolderOptions options,
        string label,
        Func<IReadOnlyList<string>, WatchFolderResult> process,
        IFileSystem fs,
        Action<WatchFolderLogEntry>? logger = null)
    {
        _options = options;
        _label = label;
        _process = process;
        _fs = fs;
        _logger = logger;
    }

    /// <summary>True khi đang theo dõi.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Số tệp đang chờ ổn định.</summary>
    public int PendingCount => _planner.PendingCount;

    /// <summary>Số lô đã xử lý.</summary>
    public int ProcessedBatches { get; private set; }

    /// <summary>Số tệp đã đổi tên.</summary>
    public int RenamedFiles { get; private set; }

    public event EventHandler<WatchFolderLogEntry>? Logged;

    public event EventHandler<WatchFolderResult>? BatchProcessed;

    /// <summary>Bắt đầu theo dõi thư mục.</summary>
    public void Start()
    {
        if (IsRunning) return;
        if (string.IsNullOrWhiteSpace(_options.Folder) || !Directory.Exists(_options.Folder))
            throw new DirectoryNotFoundException($"Thư mục không tồn tại: {_options.Folder}");

        // Tệp đã có sẵn trước khi bắt đầu không phải "tệp mới" -> bỏ qua.
        foreach (var file in Directory.EnumerateFiles(_options.Folder, "*", SearchOption.AllDirectories))
        {
            _seen.Add(file);
        }

        _watcher = new FileSystemWatcher(_options.Folder, "*.*")
        {
            IncludeSubdirectories = _options.Recursive,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            InternalBufferSize = 64 * 1024
        };

        _watcher.Created += OnFileEvent;
        _watcher.Changed += OnFileEvent;
        _watcher.Renamed += OnRenamed;
        _watcher.Deleted += OnDeleted;
        _watcher.Error += OnError;

        _timer = new Timer(_ => OnTick(), null, _options.Debounce, _options.Debounce);

        _watcher.EnableRaisingEvents = true;
        IsRunning = true;
        Log("info", CoreText.Get("CoreWatchStart", "Đang theo dõi: {0} (preset: {1})", _options.Folder, _label));
    }

    /// <summary>Dừng theo dõi.</summary>
    public void Stop()
    {
        if (!IsRunning) return;

        IsRunning = false;

        var watcher = _watcher;
        if (watcher is not null)
        {
            try { watcher.EnableRaisingEvents = false; } catch { /* ignore */ }
            watcher.Dispose();
        }
        _watcher = null;

        _timer?.Dispose();
        _timer = null;

        Log("info", CoreText.Get("CoreWatchStop", "Đã dừng theo dõi."));
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.FullPath)) return;
        _planner.Notify(e.FullPath, DateTime.UtcNow);
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.OldFullPath)) _planner.Notify(e.OldFullPath, DateTime.UtcNow);
        if (!string.IsNullOrWhiteSpace(e.FullPath)) _planner.Notify(e.FullPath, DateTime.UtcNow);
    }

    private void OnDeleted(object sender, FileSystemEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.FullPath)) return;
        _planner.Forget(e.FullPath);
        lock (_gate) { _seen.Remove(e.FullPath); }
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        Log("error", CoreText.Get("CoreWatchError", "Lỗi theo dõi: {0}", e.GetException().Message));
    }

    private void OnTick()
    {
        if (!IsRunning) return;

        try
        {
            var ready = _planner.TakeReady(DateTime.UtcNow, _options.SettleDelay);
            if (ready.Count == 0) return;

            List<string> batch;
            lock (_gate)
            {
                batch = ready
                    // Sự kiện của thư mục (ví dụ thư mục do Organize vừa tạo) và tệp đã biến mất không tạo lô rỗng.
                    .Where(p => !_seen.Contains(p) && !_produced.Contains(p) && _fs.FileExists(p))
                    .ToList();

                foreach (var path in batch) _seen.Add(path);
            }

            if (batch.Count == 0) return;

            if (batch.Count > _options.MaxFilesPerBatch)
            {
                batch = batch.Take(_options.MaxFilesPerBatch).ToList();
            }

            Log("info", CoreText.Get("CoreWatchDetected", "Phát hiện {0} tệp mới, đang áp dụng preset…", batch.Count));

            var result = _process(batch);
            ProcessedBatches++;
            RenamedFiles += result.Renamed;

            foreach (var (path, error) in result.Errors)
            {
                Log("warn", CoreText.Get("CoreWatchSkipped", "Bỏ qua: {0}", error), path);
            }

            // Ghi nhớ các tệp mới sinh ra để không xử lý lại (chống lặp).
            lock (_gate)
            {
                foreach (var path in result.RenamedPaths) _produced.Add(path);
            }

            Log(result.Failed > 0 ? "warn" : "info",
                CoreText.Get("CoreWatchBatchDone", "Hoàn tất lô: đổi {0}, không đổi {1}, bỏ qua {2}, lỗi {3}", result.Renamed, result.Unchanged, result.Skipped, result.Failed));

            BatchProcessed?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            Log("error", CoreText.Get("CoreWatchBatchError", "Lỗi khi xử lý lô: {0}", ex.Message));
        }
    }

    private void Log(string level, string message, string? path = null)
    {
        var entry = new WatchFolderLogEntry(DateTime.UtcNow, level, message, path);
        try { _logger?.Invoke(entry); } catch { /* ignore */ }
        Logged?.Invoke(this, entry);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
