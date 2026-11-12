namespace BFR.Presentation;

using BFR.Core;

using BFR.Core.Licensing;
using BFR.Core.Reports;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;


/// <summary>Bộ lọc nhanh trên bảng xem trước (chip trạng thái).</summary>
public enum PreviewFilter
{
    All,
    WillChange,
    Unchanged,
    Problems
}

/// <summary>
/// ViewModel chính của cửa sổ: nguồn tệp, chuỗi quy tắc, xem trước, áp dụng, hoàn tác.
///
/// Toàn bộ logic đổi tên vẫn nằm trong <c>BFR.Core</c>; lớp này chỉ điều phối
/// và chuẩn bị dữ liệu cho view (không sửa kết quả của preview/apply).
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly RealFileSystem _fs = new();
    private readonly LicenseCoordinator _license;
    private readonly PresetStore _presetStore;
    private readonly HistoryStore _history;
    private readonly string _journalDir;
    private readonly IMetadataProvider? _metadataProvider;
    private readonly IStringProvider _strings;
    private readonly IUiDispatcher _dispatcher;
    private readonly LicenseTier _metadataTier;

    private readonly List<string> _sources = new();
    private readonly HashSet<string> _excludedPaths = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _cts;
    private Preset _preset = new();
    private PreviewResult? _preview;
    private bool _isBusy;

    public MainViewModel(
        LicenseCoordinator license,
        IpcPayload? initialPayload = null,
        string? appDataDirectory = null,
        IStringProvider? strings = null,
        IUiDispatcher? dispatcher = null)
    {
        _license = license;
        _strings = strings ?? new KeyOnlyStringProvider();
        _dispatcher = dispatcher ?? new ImmediateUiDispatcher();
        var appData = appDataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BatchFileRenamer");
        _journalDir = Path.Combine(appData, "journal");
        _history = new HistoryStore(_fs, Path.Combine(appData, "history.json"));
        _presetStore = new PresetStore(_fs, Path.Combine(appData, "presets"));

        _metadataTier = license.Status.Tier;
        _metadataProvider = license.Status.Tier == LicenseTier.Pro
            ? new CompositeMetadataProvider(new ExifMetadataReader(), new MediaMetadataReader(), new AudioTagReader())
            : null;

        Presets = new ObservableCollection<Preset>(BuiltInPresets.All);
        Rules = new ObservableCollection<RuleCardViewModel>();
        Rows = new ObservableCollection<PreviewRowViewModel>();

        ApplyCommand = new RelayCommand(async () => await ApplyAsync(), () => CanApply);
        UndoCommand = new RelayCommand(async () => await UndoLastAsync(), () => CanUndo);
        ClearCommand = new RelayCommand(ClearList);
        ResolveConflictsCommand = new RelayCommand(() => AutoResolveConflicts = true);
        StopAutoResolveCommand = new RelayCommand(() => AutoResolveConflicts = false);
        SelectAllCommand = new RelayCommand(() => SetAllSelected(true));
        ClearSelectionCommand = new RelayCommand(() => SetAllSelected(false));

        if (initialPayload is not null)
        {
            var paths = new List<string>(initialPayload.Paths);
            if (!string.IsNullOrWhiteSpace(initialPayload.FileListPath))
            {
                paths.AddRange(FileListReader.ReadAndDelete(initialPayload.FileListPath, _fs));
            }

            if (!string.IsNullOrWhiteSpace(initialPayload.PresetName))
            {
                var preset = Presets.FirstOrDefault(p => p.Name.Equals(initialPayload.PresetName, StringComparison.OrdinalIgnoreCase))
                             ?? _presetStore.Load(initialPayload.PresetName);
                if (preset is not null) SelectedPreset = preset;
            }

            foreach (var path in paths)
            {
                if (!string.IsNullOrWhiteSpace(path) && !_sources.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    _sources.Add(path);
                }
            }
        }
    }

    // ----------------------------------------------------------------- State

    public ObservableCollection<Preset> Presets { get; }

    /// <summary>Chuỗi quy tắc đang chỉnh sửa (dùng để lưu preset, kiểm tra thứ tự).</summary>
    public Preset CurrentPreset => _preset;

    public ObservableCollection<RuleCardViewModel> Rules { get; }

    public ObservableCollection<PreviewRowViewModel> Rows { get; }

    /// <summary>Các dòng sau khi áp bộ lọc (chip trạng thái + tìm kiếm) — view bind vào đây.</summary>
    public ObservableCollection<PreviewRowViewModel> FilteredRows { get; } = new();

    public RelayCommand ApplyCommand { get; }

    public RelayCommand UndoCommand { get; }

    public RelayCommand ClearCommand { get; }

    /// <summary>Bật tự xử lý trùng tên (thêm " (2)", " (3)"…) rồi tính lại xem trước.</summary>
    public RelayCommand ResolveConflictsCommand { get; }

    public RelayCommand StopAutoResolveCommand { get; }

    public RelayCommand SelectAllCommand { get; }

    public RelayCommand ClearSelectionCommand { get; }

    public Preset? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (!Set(ref _selectedPreset, value)) return;
            if (value is not null) LoadPreset(value);
        }
    }

    private Preset? _selectedPreset;

    public bool IsRecursive { get; set; } = false;

    public bool IncludeFolders { get; set; } = false;

    public SortBy SortBy { get; set; } = SortBy.NameNatural;

    public PreviewFilter Filter
    {
        get => _filter;
        set
        {
            if (!Set(ref _filter, value)) return;
            ApplyFilter();
        }
    }

    private PreviewFilter _filter = PreviewFilter.All;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!Set(ref _searchText, value)) return;
            ApplyFilter();
        }
    }

    private string _searchText = string.Empty;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value)) return;

            // Nút Áp dụng/Hoàn tác phải cập nhật trạng thái ngay khi lượt tính xong.
            Raise(nameof(CanApply));
            Raise(nameof(CanUndo));
            ApplyCommand.RaiseCanExecuteChanged();
            UndoCommand.RaiseCanExecuteChanged();
        }
    }

    public string BusyText
    {
        get => _busyText;
        private set => Set(ref _busyText, value);
    }

    private string _busyText = string.Empty;

    /// <summary>Thông báo thành công sau khi áp dụng (InfoBar), có thể ẩn.</summary>
    public string? SuccessMessage
    {
        get => _successMessage;
        set => Set(ref _successMessage, value);
    }

    private string? _successMessage;

    public bool HasSuccessMessage => !string.IsNullOrEmpty(SuccessMessage);

    public int TotalCount => Rows.Count;

    public int WillChangeCount => Rows.Count(r => r.Status == PreviewStatus.Ok && r.IsSelected && !string.Equals(r.NewName, r.OldName, StringComparison.Ordinal));

    public int UnchangedCount => Rows.Count(r => r.Status == PreviewStatus.Unchanged);

    public int ErrorCount => Rows.Count(r => r.Status is PreviewStatus.Conflict or PreviewStatus.InvalidName or PreviewStatus.PathTooLong or PreviewStatus.Protected);

    public int SelectedCount => Rows.Count(r => r.IsSelected);

    /// <summary>Số tệp đang bị trùng tên (xung đột) trong xem trước hiện tại.</summary>
    public int ConflictCount => Rows.Count(r => r.Status == PreviewStatus.Conflict);

    public bool HasConflicts => ConflictCount > 0;

    /// <summary>Dòng chữ của khung cảnh báo trùng tên.</summary>
    public string ConflictBannerText => _strings.F("ConflictBannerText", ConflictCount);

    /// <summary>Dòng chữ của khung thông tin sau khi bật tự xử lý trùng tên.</summary>
    public string AutoResolvedText => _strings.F("ConflictAutoResolvedText", AutoResolvedCount);

    /// <summary>Số tệp đã được tự thêm hậu tố ở lượt xem trước gần nhất (khi bật tự xử lý).</summary>
    public int AutoResolvedCount
    {
        get => _autoResolvedCount;
        private set { if (Set(ref _autoResolvedCount, value)) { Raise(nameof(HasAutoResolved)); } }
    }

    private int _autoResolvedCount;

    public bool HasAutoResolved => AutoResolveConflicts && AutoResolvedCount > 0;

    /// <summary>Tự thêm hậu tố " (2)"… khi tên mới trùng (B3).</summary>
    public bool AutoResolveConflicts
    {
        get => _autoResolve;
        set
        {
            if (!Set(ref _autoResolve, value)) return;
            Raise(nameof(HasAutoResolved));
            _ = RebuildAsync();
        }
    }

    private bool _autoResolve;

    public bool CanApply => WillChangeCount > 0 && ErrorCount == 0 && !IsBusy;

    public bool CanUndo => _history.LoadAll().Count > 0 && !IsBusy;

    public bool IsEmptyStateVisible => Rows.Count == 0;

    public string StatusSummary => _strings.F("StatusSummary", TotalCount, WillChangeCount, UnchangedCount, ErrorCount);

    /// <summary>Nhãn nút áp dụng: nêu rõ số tệp thực sự sẽ đổi.</summary>
    public string ApplyButtonText => _strings.F("ApplyWithCount", WillChangeCount);

    // ----------------------------------------------------------------- Sources

    /// <summary>Thêm tệp/thư mục vào lô (dùng cho cả nút bấm và kéo-thả).</summary>
    public void AddPaths(IEnumerable<string> paths)
    {
        var changed = false;
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            if (_sources.Contains(path, StringComparer.OrdinalIgnoreCase)) continue;
            _sources.Add(path);
            changed = true;
        }

        if (changed) _ = RebuildAsync();
    }

    public void ClearList()
    {
        _sources.Clear();
        _excludedPaths.Clear();
        foreach (var r in Rows) r.PropertyChanged -= OnRowPropertyChanged;
        Rows.Clear();
        _preview = null;
        OnCountsChanged();
        ApplyFilter();
    }

    // ----------------------------------------------------------------- Preset & rules

    public void LoadPreset(Preset preset)
    {
        _preset = preset;
        Rules.Clear();
        foreach (var rule in preset.Rules)
        {
            var card = new RuleCardViewModel(rule, _strings, MoveRule);
            card.Changed += (_, _) => ScheduleRebuild();
            Rules.Add(card);
        }

        RefreshRuleOrder();
    }

    public void RefreshPresets()
    {
        var selected = _selectedPreset;
        Presets.Clear();
        foreach (var preset in BuiltInPresets.All) Presets.Add(preset);
        foreach (var preset in _presetStore.LoadAll()) Presets.Add(preset);

        if (selected is not null)
        {
            var match = Presets.FirstOrDefault(p => p.Name.Equals(selected.Name, StringComparison.OrdinalIgnoreCase));
            if (match is not null) _selectedPreset = match;
        }
    }

    /// <summary>Thêm quy tắc mới vào chuỗi (kiểm tra giới hạn Free/Pro).</summary>
    public bool AddRule(string type)
    {
        if (Rules.Count >= _license.License.MaxRules)
        {
            RaiseProLimit(_license.License.Check(ProFeature.UnlimitedRules), ProFeature.UnlimitedRules);
            return false;
        }

        var definition = new RuleDefinition { Type = type, Enabled = true };
        ApplyDefaultParams(definition);
        _preset.Rules.Add(definition);
        var card = new RuleCardViewModel(definition, _strings, MoveRule);
        // Quy tắc vừa thêm mở sẵn để nhập tham số ngay (đặc biệt ListMap/ReplaceDict cần dán dữ liệu).
        card.IsExpanded = true;
        // Như LoadPreset: sửa tham số của thẻ vừa thêm phải làm mới xem trước.
        card.Changed += (_, _) => ScheduleRebuild();
        Rules.Add(card);
        RefreshRuleOrder();
        _ = RebuildAsync();
        return true;
    }

    public void RemoveRule(RuleCardViewModel card)
    {
        _preset.Rules.Remove(card.Definition);
        Rules.Remove(card);
        RefreshRuleOrder();
        _ = RebuildAsync();
    }

    public void MoveRule(RuleCardViewModel card, int delta)
    {
        var index = Rules.IndexOf(card);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= Rules.Count) return;

        Rules.Move(index, target);
        _preset.Rules.RemoveAt(index);
        _preset.Rules.Insert(target, card.Definition);
        RefreshRuleOrder();
        _ = RebuildAsync();
    }

    /// <summary>Di chuyển quy tắc tới vị trí bất kỳ (dùng cho kéo-thả).</summary>
    public bool MoveRuleTo(RuleCardViewModel card, int targetIndex)
    {
        var index = Rules.IndexOf(card);
        if (index < 0 || targetIndex < 0 || targetIndex >= Rules.Count || index == targetIndex) return false;

        Rules.Move(index, targetIndex);
        _preset.Rules.RemoveAt(index);
        _preset.Rules.Insert(targetIndex, card.Definition);
        RefreshRuleOrder();
        _ = RebuildAsync();
        return true;
    }

    /// <summary>Cập nhật trạng thái nút lên/xuống cho tất cả thẻ quy tắc.</summary>
    public void RefreshRuleOrder()
    {
        for (int i = 0; i < Rules.Count; i++)
        {
            Rules[i].UpdateOrder(i, Rules.Count);
        }
    }

    public void ToggleRule(RuleCardViewModel card)
    {
        card.IsEnabled = !card.IsEnabled;
        _ = RebuildAsync();
    }

    /// <summary>Lưu chuỗi quy tắc hiện tại vào thư viện preset (Free: tối đa 3).</summary>
    public bool SavePresetToLibrary(string name)
    {
        var existing = _presetStore.LoadAll();
        var alreadyExists = existing.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (!alreadyExists)
        {
            var gate = _license.License.CheckPresetCount(existing.Count + 1);
            if (!gate.Allowed)
            {
                RaiseProLimit(gate, ProFeature.UnlimitedPresets);
                return false;
            }
        }

        var copy = new Preset
        {
            Name = name,
            Description = _preset.Description,
            Options = _preset.Options,
            Rules = _preset.Rules.Select(r => new RuleDefinition
            {
                Type = r.Type,
                Name = r.Name,
                Enabled = r.Enabled,
                Params = new Dictionary<string, System.Text.Json.JsonElement>(r.Params)
            }).ToList()
        };

        _presetStore.Save(copy);
        RefreshPresets();
        return true;
    }

    public string ExportPresetJson(Preset preset) => _presetStore.ToJson(preset);

    public Preset ImportPresetJson(string json) => _presetStore.FromJson(json);

    // ----------------------------------------------------------------- Preview

    /// <summary>Trì hoãn 150 ms rồi tính lại preview (nhập liên tục không giật).</summary>
    public void ScheduleRebuild()
    {
        _debounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _debounceCts = cts;

        // Luôn hồi lại luồng UI: ObservableCollection không được sửa ngoài luồng giao diện.
        _dispatcher.Post(() => _ = DebouncedRebuildAsync(cts));
    }

    private async Task DebouncedRebuildAsync(CancellationTokenSource cts)
    {
        try
        {
            // Không ConfigureAwait(false): RebuildAsync sửa ObservableCollection đang gắn với DataGrid,
            // nên phải tiếp tục trên luồng giao diện (ném NotSupportedException ở luồng nền sẽ bị nuốt
            // ở dưới và bảng xem trước đứng im dù số đếm đã đổi).
            await Task.Delay(150, cts.Token);
            if (!cts.IsCancellationRequested) await RebuildAsync();
            CheckProDateSource();
        }
        catch (OperationCanceledException)
        {
            // Bị thay thế bởi lượt gõ tiếp theo.
        }
        catch (Exception)
        {
            // Lỗi preview không được làm sập giao diện.
        }
    }

    private CancellationTokenSource? _debounceCts;

    /// <summary>Dòng đang chọn (dùng cho Rule Trace).</summary>
    public PreviewRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (!Set(ref _selectedRow, value)) return;
            BuildTrace();
        }
    }

    private PreviewRowViewModel? _selectedRow;

    /// <summary>Các bước của Rule Trace (timeline) cho dòng đang chọn.</summary>
    public ObservableCollection<TraceStepViewModel> TraceSteps { get; } = new();

    private void BuildTrace()
    {
        TraceSteps.Clear();

        if (SelectedRow is null) return;

        TraceSteps.Add(new TraceStepViewModel(
            _strings.T("ColumnOriginalName"),
            SelectedRow.OldName,
            false));

        var trace = _preview?.Items.FirstOrDefault(i =>
            string.Equals(i.File.FullPath, SelectedRow!.File.FullPath, StringComparison.OrdinalIgnoreCase))?.Trace;

        if (trace is null) return;

        // Id của quy tắc trong vết là Id của IRule đã biên dịch, nên ánh xạ theo thứ tự
        // của chuỗi quy tắc (preset giữ nguyên thứ tự).
        var compiled = new PresetCompiler().Compile(_preset, _license.License.MaxListRows).Rules.ToList();
        var titlesById = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < compiled.Count && i < Rules.Count; i++)
        {
            titlesById[compiled[i].Id] = Rules[i].Title;
        }

        foreach (var entry in trace)
        {
            var separator = entry.IndexOf(':');
            var id = separator > 0 ? entry[..separator].Trim() : string.Empty;
            var note = separator > 0 ? entry[(separator + 1)..].Trim() : entry;

            var title = titlesById.TryGetValue(id, out var ruleTitle)
                ? ruleTitle
                : _strings.T("ColumnRule");

            TraceSteps.Add(new TraceStepViewModel(title, note, true));
        }

        TraceSteps.Add(new TraceStepViewModel(
            _strings.T("ColumnResult"),
            SelectedRow.NewName,
            false));
    }

    /// <summary>Quét + tính preview trên luồng nền (hủy lượt cũ khi tham số đổi).</summary>
    public async Task RebuildAsync()
    {
        var sources = _sources.ToList();
        if (sources.Count == 0)
        {
            Rows.Clear();
            _preview = null;
            OnCountsChanged();
            ApplyFilter();
            return;
        }

        _cts?.Cancel();
        var cts = new CancellationTokenSource();
        _cts = cts;
        var autoResolve = _autoResolve;
        var maxListRows = _license.License.MaxListRows;
        NotifyListLimit(maxListRows);

        IsBusy = true;
        BusyText = _strings.T("StatusCalculating");

        try
        {
            var (scanned, preview, autoResolved) = await Task.Run(() =>
            {
                var scanner = new FileScanner();
                var items = scanner.Scan(sources, new ScanOptions
                {
                    Recursive = IsRecursive,
                    IncludeFolders = IncludeFolders,
                    MaxDepth = IsRecursive ? _license.License.MaxRecursionDepth : 0
                }, cts.Token);

                if (_excludedPaths.Count > 0)
                {
                    items = items.Where(f => !_excludedPaths.Contains(f.FullPath)).ToList();
                }

                items = SortItems(items, SortBy);

                var context = new RuleContext(DateTime.Now, Guid.NewGuid().ToString("N"));
                var pipeline = new PresetCompiler().Compile(_preset, maxListRows);
                var outside = BuildOutsideIndex(items);
                var engine = new PreviewEngine(pipeline, metadataProvider: _metadataProvider) { AutoResolveConflicts = autoResolve };
                var result = engine.Run(items, context, null, cts.Token, outside);

                return (items, result, engine.AutoResolvedCount);
            }, cts.Token).ConfigureAwait(true);

            if (cts.IsCancellationRequested) return;

            _preview = preview;
            AutoResolvedCount = autoResolved;

            // Giữ trạng thái chọn/bỏ chọn của người dùng giữa các lượt tính lại;
            // tệp mới xuất hiện mặc định được chọn.
            var previousSelection = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in Rows) previousSelection[row.File.FullPath] = row.IsSelected;

            foreach (var r in Rows) r.PropertyChanged -= OnRowPropertyChanged;
            Rows.Clear();
            foreach (var item in preview.Items)
            {
                var isSelected = previousSelection.TryGetValue(item.File.FullPath, out var kept) ? kept : true;
                var row = new PreviewRowViewModel(
                    item.File,
                    item.OldName,
                    item.NewName,
                    item.Status,
                    item.Detail,
                    isSelected,
                    _strings);
                row.PropertyChanged += OnRowPropertyChanged;
                Rows.Add(row);
            }

            OnCountsChanged();
            ApplyFilter();
        }
        catch (OperationCanceledException)
        {
            // Lượt tính mới đã thay thế.
        }
        finally
        {
            if (ReferenceEquals(_cts, cts))
            {
                IsBusy = false;
                BusyText = string.Empty;
            }
        }
    }

    /// <summary>Sắp xếp danh sách tệp theo lựa chọn của người dùng (FR-A5).</summary>
    public static IReadOnlyList<FileItem> SortItems(IReadOnlyList<FileItem> items, SortBy sortBy)
    {
        var list = items.ToList();

        switch (sortBy)
        {
            case SortBy.NameOrdinal:
                list.Sort((a, b) => string.Compare(a.FullPath, b.FullPath, StringComparison.OrdinalIgnoreCase));
                break;
            case SortBy.Created:
                list.Sort((a, b) => a.CreatedUtc.CompareTo(b.CreatedUtc));
                break;
            case SortBy.Modified:
                list.Sort((a, b) => a.ModifiedUtc.CompareTo(b.ModifiedUtc));
                break;
            case SortBy.Size:
                list.Sort((a, b) => b.Size.CompareTo(a.Size));
                break;
            case SortBy.LoadOrder:
                list.Sort((a, b) => a.LoadOrder.CompareTo(b.LoadOrder));
                break;
            default:
                var cmp = NaturalSortComparer.Instance;
                list.Sort((a, b) => cmp.Compare(a.FullPath, b.FullPath));
                break;
        }

        return list;
    }

    /// <summary>
    /// Chỉ mục tên tệp "ngoài lô" để phát hiện xung đột: gồm tệp trên đĩa không thuộc lô
    /// và các tệp trong lô đã bị người dùng bỏ chọn (FR-A6).
    /// </summary>
    private IReadOnlyDictionary<string, IReadOnlySet<string>> BuildOutsideIndex(IReadOnlyList<FileItem> all)
    {
        var batchPaths = new HashSet<string>(all.Select(f => f.FullPath), StringComparer.OrdinalIgnoreCase);
        var unselected = new HashSet<string>(
            Rows.Where(r => !r.IsSelected).Select(r => r.File.FullPath),
            StringComparer.OrdinalIgnoreCase);

        // Khoá PHẢI là tên thư mục đã chuẩn hoá: ConflictDetector tra cứu bằng NormalizeDirectory
        // (chữ thường, dấu '/'). Dùng khoá thô thì không bao giờ khớp và mọi va chạm với tệp
        // ngoài lô đều không được phát hiện.
        var result = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);

        foreach (var directory in all.Select(f => f.Directory).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var path in _fs.GetFiles(directory, "*", false))
                {
                    if (!batchPaths.Contains(path)) names.Add(Path.GetFileName(path));
                }
                foreach (var path in _fs.GetDirectories(directory, false))
                {
                    if (!batchPaths.Contains(path)) names.Add(Path.GetFileName(path));
                }
            }
            catch
            {
                // Thư mục không truy cập được: bỏ qua.
            }

            // Tệp trong lô nhưng bị bỏ chọn vẫn giữ tên cũ nên cũng chiếm chỗ (FR-A6).
            foreach (var path in unselected)
            {
                if (string.Equals(Path.GetDirectoryName(path), directory, StringComparison.OrdinalIgnoreCase))
                    names.Add(Path.GetFileName(path));
            }

            result[ConflictDetector.NormalizeDirectory(directory)] = names;
        }

        return result;
    }

    /// <summary>
    /// Áp bộ lọc (chip trạng thái + ô tìm kiếm) vào danh sách hiển thị.
    /// Tính trong ViewModel để không phụ thuộc WPF (CollectionView) và test được.
    /// </summary>
    private void ApplyFilter()
    {
        FilteredRows.Clear();
        foreach (var row in Rows)
        {
            if (MatchesFilter(row)) FilteredRows.Add(row);
        }
    }

    private bool MatchesFilter(PreviewRowViewModel row)
    {
        var byStatus = Filter switch
        {
            PreviewFilter.WillChange => row.Status == PreviewStatus.Ok && !string.Equals(row.NewName, row.OldName, StringComparison.Ordinal),
            PreviewFilter.Unchanged => row.Status == PreviewStatus.Unchanged,
            PreviewFilter.Problems => row.Status is PreviewStatus.Conflict or PreviewStatus.InvalidName or PreviewStatus.PathTooLong or PreviewStatus.Protected,
            _ => true
        };

        if (!byStatus) return false;

        if (string.IsNullOrWhiteSpace(SearchText)) return true;

        return row.OldName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || row.NewName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || row.FolderText.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    private void OnRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PreviewRowViewModel.IsSelected))
        {
            OnCountsChanged();
        }
    }

    /// <summary>Trạng thái chọn tất cả (true: tất cả, false: không dòng nào, null: một phần).</summary>
    public bool? AreAllRowsSelected
    {
        get
        {
            if (Rows.Count == 0) return false;
            var selectedCount = Rows.Count(r => r.IsSelected);
            if (selectedCount == 0) return false;
            if (selectedCount == Rows.Count) return true;
            return null;
        }
        set
        {
            if (value.HasValue)
            {
                SetAllSelected(value.Value);
            }
        }
    }

    public void SetAllSelected(bool selected)
    {
        foreach (var row in Rows) row.IsSelected = selected;
        OnCountsChanged();
    }

    public void RemoveRow(PreviewRowViewModel row)
    {
        var path = row.File.FullPath;
        _sources.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        _excludedPaths.Add(path);
        row.PropertyChanged -= OnRowPropertyChanged;
        Rows.Remove(row);
        FilteredRows.Remove(row);
        OnCountsChanged();
    }

    // ----------------------------------------------------------------- Apply / Undo

    /// <summary>Tóm tắt lô trước khi áp dụng (hiển thị trong hộp xác nhận).</summary>
    public ApplySummary BuildApplySummary()
    {
        var changing = Rows
            .Where(r => r.Status == PreviewStatus.Ok && r.IsSelected && !string.Equals(r.NewName, r.OldName, StringComparison.Ordinal))
            .ToList();

        var folders = changing.Select(r => r.FolderText).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var extensionChanged = changing.Count(r =>
            !string.Equals(Path.GetExtension(r.OldName), Path.GetExtension(r.NewName), StringComparison.OrdinalIgnoreCase));

        return new ApplySummary(changing.Count, folders, extensionChanged,
            changing.Take(3).Select(r => (r.OldName, r.NewName)).ToList());
    }

    public async Task ApplyAsync()
    {
        var pairs = Rows
            .Where(r => r.Status == PreviewStatus.Ok && r.IsSelected && !string.Equals(r.NewName, r.OldName, StringComparison.Ordinal))
            .Select(r => (From: r.File.FullPath, To: Path.Combine(r.File.Directory, r.NewName)))
            .ToList();

        if (pairs.Count == 0) return;

        IsBusy = true;
        BusyText = _strings.T("StatusApplying");

        try
        {
            var steps = new RenamePlanner().Plan(pairs);
            var result = await Task.Run(() => new RenameExecutor(_fs, _journalDir).Execute(steps, AppVersion.Current))
                .ConfigureAwait(true);

            _history.Add(new HistoryEntry
            {
                Id = result.JournalId,
                StartedUtc = DateTime.UtcNow,
                Preset = _preset.Name,
                Total = result.TotalSteps,
                Succeeded = result.Succeeded,
                Failed = result.Failed,
                Status = result.Failed > 0 ? "partial" : "completed",
                JournalPath = result.JournalPath
            });

            _history.PruneAndLimit(_license.License.MaxUndoEntries);

            _lastReport = Rows
                .Where(r => r.Status == PreviewStatus.Ok && r.IsSelected)
                .Select(r => new RenameReportEntry
                {
                    OldPath = r.File.FullPath,
                    NewPath = string.Equals(r.NewName, r.OldName, StringComparison.Ordinal) ? null : Path.Combine(r.File.Directory, r.NewName),
                    Status = string.Equals(r.NewName, r.OldName, StringComparison.Ordinal) ? "skipped" : "ok",
                    TimestampUtc = DateTime.UtcNow
                })
                .ToList();

            SuccessMessage = _strings.F("ApplySuccessMessage", result.Succeeded, result.TotalSteps);
            OnCountsChanged();
            await RebuildAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
        }
    }

    private List<RenameReportEntry> _lastReport = new();

    public IReadOnlyList<RenameReportEntry> LastReport => _lastReport;

    public async Task UndoLastAsync()
    {
        IsBusy = true;
        BusyText = _strings.T("StatusUndoing");

        try
        {
            var result = new UndoService(_fs).UndoLast(_journalDir);
            SuccessMessage = _strings.F("UndoMessage", result.Undone, result.Total);
            await RebuildAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
        }
    }

    public void CancelWork() => _cts?.Cancel();

    /// <summary>
    /// Phát hiện việc người dùng chọn nguồn ngày Pro (EXIF / media) khi đang ở cấp Free
    /// để mở hộp thoại nâng cấp với đúng ngữ cảnh (yêu cầu mục 4).
    /// </summary>
    public void CheckProDateSource()
    {
        if (_license.License.Allows(ProFeature.ExifDate)) return;

        foreach (var rule in _preset.Rules)
        {
            if (!string.Equals(rule.Type, "Date", StringComparison.OrdinalIgnoreCase)) continue;

            var source = rule.Params.TryGetValue("source", out var element)
                         && element.ValueKind == System.Text.Json.JsonValueKind.String
                ? element.GetString()
                : null;

            if (source is "Taken" or "Media")
            {
                RaiseProLimit(_license.License.Check(ProFeature.ExifDate), ProFeature.ExifDate);
                return;
            }
        }
    }

    // ----------------------------------------------------------------- Danh sách (B2)

    private bool _listLimitNotified;

    /// <summary>
    /// Báo một lần khi danh sách vượt hạn mức Free (50 dòng): phần dư bị bỏ qua và hiện hộp thoại nâng cấp.
    /// Khi trở lại trong hạn mức thì cho phép báo lại.
    /// </summary>
    private void NotifyListLimit(int maxListRows)
    {
        var over = maxListRows != int.MaxValue && _preset.Rules.Any(r =>
            r.Enabled && r.Type is "ListMap" or "ReplaceDict"
            && ListParser.CountRows(ReadRuleText(r)) > maxListRows);

        if (over && !_listLimitNotified)
        {
            _listLimitNotified = true;
            ProLimitReached?.Invoke(this, ProFeature.UnlimitedListRows);
        }
        else if (!over)
        {
            _listLimitNotified = false;
        }
    }

    private static string ReadRuleText(RuleDefinition rule)
    {
        var key = rule.Type == "ListMap" ? "mapping" : "pairs";
        return rule.Params.TryGetValue(key, out var e) && e.ValueKind == System.Text.Json.JsonValueKind.String
            ? e.GetString() ?? string.Empty
            : string.Empty;
    }

    /// <summary>Tham số mặc định để giao diện và bộ dựng quy tắc khớp nhau (ví dụ "bỏ qua hoa/thường" bật sẵn).</summary>
    private static void ApplyDefaultParams(RuleDefinition rule)
    {
        System.Text.Json.JsonElement E(object v) => System.Text.Json.JsonSerializer.SerializeToElement(v);
        switch (rule.Type)
        {
            case "ListMap":
                rule.Params["mode"] = E("Map");
                rule.Params["matchBy"] = E("Name");
                rule.Params["ignoreCase"] = E(true);
                rule.Params["mapping"] = E(string.Empty);
                break;
            case "ReplaceDict":
                rule.Params["ignoreCase"] = E(true);
                rule.Params["pairs"] = E(string.Empty);
                break;
        }
    }

    /// <summary>Kiểm tra giấy phép trước khi nhập danh sách từ tệp CSV; hiện hộp thoại nâng cấp nếu là Free.</summary>
    public bool CanImportListFile()
    {
        if (_license.License.Allows(ProFeature.ListCsvImport)) return true;
        ProLimitReached?.Invoke(this, ProFeature.ListCsvImport);
        return false;
    }

    // ----------------------------------------------------------------- Misc

    public event EventHandler<ProFeature>? ProLimitReached;

    private void RaiseProLimit(GateResult gate, ProFeature feature)
    {
        if (!string.IsNullOrEmpty(gate.Message))
        {
            ProLimitReached?.Invoke(this, feature);
        }
    }

    private void OnCountsChanged()
    {
        Raise(nameof(TotalCount));
        Raise(nameof(WillChangeCount));
        Raise(nameof(UnchangedCount));
        Raise(nameof(ErrorCount));
        Raise(nameof(SelectedCount));
        Raise(nameof(ConflictCount));
        Raise(nameof(HasConflicts));
        Raise(nameof(ConflictBannerText));
        Raise(nameof(AutoResolvedText));
        Raise(nameof(HasAutoResolved));
        Raise(nameof(AreAllRowsSelected));
        Raise(nameof(CanApply));
        Raise(nameof(CanUndo));
        Raise(nameof(StatusSummary));
        Raise(nameof(ApplyButtonText));
        Raise(nameof(IsEmptyStateVisible));
        Raise(nameof(HasSuccessMessage));
        ApplyCommand.RaiseCanExecuteChanged();
        UndoCommand.RaiseCanExecuteChanged();
    }
}

/// <summary>Một bước trong timeline Rule Trace.</summary>
public sealed record TraceStepViewModel(string Title, string Detail, bool IsIntermediate);

/// <summary>Tóm tắt lô để hiển thị trong hộp thoại xác nhận.</summary>
public sealed record ApplySummary(
    int FileCount,
    int FolderCount,
    int ExtensionChangeCount,
    IReadOnlyList<(string OldName, string NewName)> Samples);
