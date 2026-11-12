using BFR.Core;
using BFR.Core.Licensing;
using BFR.Core.Reports;
using BFR.Cli;
using BFR.Core.WatchFolder;
using BFR.Media;
using BFR.Store;
using System.Text;
using System.Text.Json;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

var appVersion = "1.2.0";
var fs = new RealFileSystem();
var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BatchFileRenamer");
var journalDir = Path.Combine(appData, "journal");
var presetStore = new PresetStore(fs, Path.Combine(appData, "presets"));
var historyStore = new HistoryStore(fs, Path.Combine(appData, "history.json"));

// Giấy phép: bản cài MSIX dùng Microsoft Store IAP; bản portable chỉ có Free.
var devTier = LicenseService.ResolveDevTierOverride(packaged: false);
var licenseService = new LicenseService(new LicenseStore(fs), tierOverride: devTier, trialEnabled: devTier is not null);
var licenseCoordinator = new LicenseCoordinator(licenseService, new MicrosoftStoreLicenseProvider());
try
{
    using var refreshCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    await licenseCoordinator.RefreshAsync(refreshCts.Token);
}
catch
{
    // Không truy vấn được Store: tiếp tục với cấp Free.
}

if (args.Length == 0)
{
    PrintHelp();
    return 0;
}

try
{
    return args[0].ToLowerInvariant() switch
    {
        "rename" => Rename(args[1..]),
        "undo" => Undo(args[1..]),
        "history" => History(args[1..]),
        "preset" => PresetCommand(args[1..]),
        "report" => ReportCommand(args[1..]),
        "watch" => WatchCommand(args[1..]),
        "license" => await LicenseCommand(args[1..]),
        "context-menu" or "shell" => ContextMenuCommand(args[1..]),
        var tool when ToolCommands.Names.Contains(tool) => RunTool(args),
        "help" or "--help" or "-h" => Help(),
        _ => Unknown(args[0])
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Lỗi hệ thống: {ex.Message}");
    return 4;
}

/// <summary>Kiểm tra tính năng Pro; trả về false và in thông báo nếu bị khóa.</summary>
bool RequirePro(ProFeature feature)
{
    if (licenseService.Allows(feature)) return true;

    Console.Error.WriteLine(FeatureMessages.For(feature));
    Console.Error.WriteLine("Nâng cấp Pro trong bản cài từ Microsoft Store để mở khoá tính năng này.");
    return false;
}

int Rename(string[] rest)
{
    // CLI là tính năng Pro (PHẦN 3.6).
    if (!RequirePro(ProFeature.CommandLine)) return 1;

    var pathArgs = new List<string>();
    string? presetName = null;
    string? find = null, replace = null, include = null, exclude = null, number = null;
    bool regex = false, ignoreCase = false, recursive = false, dryRun = false, yes = false, allowExt = false;
    int? depth = null;
    string format = "text";

    for (int i = 0; i < rest.Length; i++)
    {
        var a = rest[i];
        switch (a)
        {
            case "--file-list":
                var fl = Next(rest, ref i);
                if (!string.IsNullOrWhiteSpace(fl)) pathArgs.AddRange(FileListReader.Read(fl, fs));
                break;
            case "--preset": presetName = Next(rest, ref i); break;
            case "--find": find = Next(rest, ref i); break;
            case "--replace": replace = Next(rest, ref i); break;
            case "--number": number = Next(rest, ref i); break;
            case "--include": include = Next(rest, ref i); break;
            case "--exclude": exclude = Next(rest, ref i); break;
            case "--format": format = Next(rest, ref i) ?? "text"; break;
            case "--depth": depth = ParseInt(Next(rest, ref i), 2); break;
            case "--recursive": recursive = true; break;
            case "--regex": regex = true; break;
            case "--ignore-case": ignoreCase = true; break;
            case "--allow-extension-change": allowExt = true; break;
            case "--dry-run": dryRun = true; break;
            case "--yes": yes = true; break;
            default:
                if (a.StartsWith("--")) { Console.Error.WriteLine($"Tham số không hợp lệ: {a}"); return 1; }
                pathArgs.Add(a);
                break;
        }
    }

    if (pathArgs.Count == 0)
    {
        Console.Error.WriteLine("Cần ít nhất một đường dẫn.");
        return 1;
    }

    // Ở chế độ json/csv, stdout chỉ chứa dữ liệu máy đọc; thông báo đi vào stderr.
    var structured = format.Equals("json", StringComparison.OrdinalIgnoreCase)
                     || format.Equals("csv", StringComparison.OrdinalIgnoreCase);

    var pipeline = new RulePipeline();
    Preset? preset = null;

    if (!string.IsNullOrWhiteSpace(presetName))
    {
        preset = BuiltInPresets.All.FirstOrDefault(p => p.Name.Equals(presetName, StringComparison.OrdinalIgnoreCase))
                 ?? presetStore.Load(presetName);
        if (preset is null) { Console.Error.WriteLine($"Không tìm thấy preset: {presetName}"); return 1; }
        pipeline = new PresetCompiler().Compile(preset);
        recursive |= preset.Options.Recursive;
        allowExt |= preset.Options.AllowExtensionChange;
    }
    else
    {
        if (!string.IsNullOrWhiteSpace(find))
        {
            pipeline.Add(new FindReplaceRule
            {
                Pattern = find!,
                Replacement = replace ?? string.Empty,
                IsRegex = regex,
                IgnoreCase = ignoreCase,
                Scope = FindReplaceRule.ApplyScope.Name
            });
        }
        if (!string.IsNullOrWhiteSpace(number))
        {
            var parts = number!.Split(':');
            pipeline.Add(new NumberingRule
            {
                Start = ParseInt(parts.ElementAtOrDefault(0), 1),
                Step = ParseInt(parts.ElementAtOrDefault(1), 1),
                Width = parts.Length > 2 ? ParseInt(parts[2], 3) : 3,
                Position = NumberingRule.NumberPosition.Start,
                Separator = "_"
            });
        }
        if (pipeline.Rules.Count == 0)
        {
            Console.Error.WriteLine("Cần --preset hoặc --find/--replace hoặc --number.");
            return 1;
        }
    }

    var maxDepth = licenseService.MaxRecursionDepth;
    if (depth.HasValue && depth.Value > maxDepth)
    {
        Console.Error.WriteLine(FeatureMessages.For(ProFeature.UnlimitedRecursion));
        return 1;
    }

    // Số quy tắc vượt giới hạn của cấp hiện tại (Free: tối đa 3).
    var ruleGate = licenseService.CheckRuleCount(pipeline.Rules.Count);
    if (!ruleGate.Allowed)
    {
        Console.Error.WriteLine(ruleGate.Message);
        return 1;
    }

    var scanner = new FileScanner();
    var files = scanner.Scan(pathArgs, new ScanOptions
    {
        Recursive = recursive,
        IncludeGlob = include,
        ExcludeGlob = exclude,
        MaxDepth = recursive ? (depth ?? maxDepth) : 0,
        IncludeFolders = preset?.Options.IncludeFolders ?? false
    });

    if (files.Count == 0)
    {
        Console.Error.WriteLine("Không tìm thấy tệp nào.");
        return 0;
    }

    var ctx = new RuleContext(DateTime.Now, Guid.NewGuid().ToString("N"));
    var outside = OutsideBatchIndex.Build(files, fs);
    var engine = new PreviewEngine(pipeline, metadataProvider: new CompositeMetadataProvider(new ExifMetadataReader(), new MediaMetadataReader(), new AudioTagReader()));
    var preview = engine.Run(files, ctx, null, default, outside);

    if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
    {
        var json = JsonSerializer.Serialize(preview.Items.Select(i => new
        {
            old = i.OldName,
            @new = i.NewName,
            status = i.Status.ToString(),
            detail = i.Detail
        }), new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(json);
    }
    else if (format.Equals("csv", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("old,new,status,detail");
        foreach (var i in preview.Items)
            Console.WriteLine($"\"{i.OldName}\",\"{i.NewName}\",{i.Status},\"{i.Detail}\"");
    }
    else
    {
        foreach (var i in preview.Items)
            Console.WriteLine($"{i.OldName} -> {i.NewName}  [{i.Status}]{(string.IsNullOrEmpty(i.Detail) ? "" : " " + i.Detail)}");
    }

    if (preview.HasBlockingError)
    {
        Console.Error.WriteLine("Có xung đột/tên không hợp lệ. Không thay đổi gì.");
        return 2;
    }

    var pairs = preview.Items
        .Where(i => i.Status is PreviewStatus.Ok && !string.Equals(i.NewName, i.OldName, StringComparison.Ordinal))
        .Select(i => (i.File.FullPath, Path.Combine(i.File.Directory, i.NewName)))
        .ToList();

    if (pairs.Count == 0)
    {
        if (structured) Console.Error.WriteLine("Không có tệp nào cần đổi tên.");
        else Console.WriteLine("Không có tệp nào cần đổi tên.");
        return 0;
    }

    if (dryRun)
    {
        if (structured) Console.Error.WriteLine("Dry-run: không ghi gì.");
        else Console.WriteLine("Dry-run: không ghi gì.");
        return 0;
    }

    if (!yes)
    {
        Console.Error.Write($"Áp dụng đổi tên {pairs.Count} tệp? [y/N] ");
        var answer = Console.ReadLine();
        if (!string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Đã hủy.");
            return 0;
        }
    }

    var steps = new RenamePlanner().Plan(pairs);
    var result = new RenameExecutor(fs, journalDir).Execute(steps, appVersion);

    historyStore.Add(new HistoryEntry
    {
        Id = result.JournalId,
        StartedUtc = DateTime.UtcNow,
        Preset = presetName,
        Total = result.TotalSteps,
        Succeeded = result.Succeeded,
        Failed = result.Failed,
        Status = result.Cancelled ? "cancelled" : result.Failed > 0 ? "partial" : "completed",
        JournalPath = result.JournalPath
    });

    if (structured)
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            journalId = result.JournalId,
            total = result.TotalSteps,
            succeeded = result.Succeeded,
            failed = result.Failed,
            cancelled = result.Cancelled
        }));
    }
    else
    {
        Console.WriteLine($"Đã đổi {result.Succeeded}/{result.TotalSteps} tệp. Journal: {result.JournalId}");
    }

    foreach (var (step, error) in result.Errors)
        Console.Error.WriteLine($"  Lỗi {step}: {error}");

    return result.Failed > 0 ? 3 : 0;
}

int Undo(string[] rest)
{
    if (!RequirePro(ProFeature.CommandLine)) return 1;

    string? journal = null, id = null;
    for (int i = 0; i < rest.Length; i++)
    {
        switch (rest[i])
        {
            case "--last": break; // mặc định đã là lần gần nhất
            case "--journal": journal = Next(rest, ref i); break;
            case "--id": id = Next(rest, ref i); break;
        }
    }

    var service = new UndoService(fs);
    UndoResult result;
    if (journal is not null) result = service.UndoJournal(journal);
    else if (id is not null)
    {
        var entry = historyStore.LoadAll().FirstOrDefault(e => e.Id.StartsWith(id, StringComparison.OrdinalIgnoreCase));
        if (entry is null) { Console.Error.WriteLine($"Không tìm thấy lịch sử: {id}"); return 1; }
        result = service.UndoJournal(entry.JournalPath);
    }
    else result = service.UndoLast(journalDir);

    Console.WriteLine($"Đã hoàn tác {result.Undone}/{result.Total} bước.");
    foreach (var (path, error) in result.Errors)
        Console.Error.WriteLine($"  {path}: {error}");
    return result.Failed > 0 ? 3 : 0;
}

int History(string[] rest)
{
    var entries = historyStore.LoadAll();
    if (entries.Count == 0)
    {
        Console.WriteLine("Chưa có lịch sử.");
        return 0;
    }
    Console.WriteLine("ID       | Thời gian            | Tổng | OK | Lỗi | Trạng thái | Preset");
    foreach (var e in entries)
    {
        Console.WriteLine($"{e.Id,-8} | {e.StartedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss} | {e.Total,4} | {e.Succeeded,2} | {e.Failed,3} | {e.Status,-10} | {e.Preset}");
    }
    return 0;
}

int PresetCommand(string[] rest)
{
    if (rest.Length == 0) { Console.Error.WriteLine("preset list|export|import"); return 1; }
    switch (rest[0])
    {
        case "list":
            foreach (var p in BuiltInPresets.All)
                Console.WriteLine($"[built-in] {p.Name} — {p.Description}");
            foreach (var p in presetStore.LoadAll())
                Console.WriteLine($"[user]     {p.Name} — {p.Description}");
            return 0;
        case "export":
        {
            if (!RequirePro(ProFeature.PresetImportExport)) return 1;
            if (rest.Length < 3) { Console.Error.WriteLine("preset export <tên> <đường dẫn>"); return 1; }
            var preset = BuiltInPresets.All.FirstOrDefault(p => p.Name.Equals(rest[1], StringComparison.OrdinalIgnoreCase))
                         ?? presetStore.Load(rest[1]);
            if (preset is null) { Console.Error.WriteLine("Không tìm thấy preset."); return 1; }
            presetStore.Export(preset, rest[2]);
            Console.WriteLine($"Đã xuất: {rest[2]}");
            return 0;
        }
        case "import":
        {
            if (!RequirePro(ProFeature.PresetImportExport)) return 1;
            if (rest.Length < 2) { Console.Error.WriteLine("preset import <đường dẫn>"); return 1; }
            var preset = presetStore.Import(rest[1]);
            presetStore.Save(preset);
            Console.WriteLine($"Đã nhập: {preset.Name}");
            return 0;
        }
        default:
            Console.Error.WriteLine("preset list|export|import");
            return 1;
    }
}

/// <summary>
/// Xuất báo cáo CSV/JSON từ journal (FR-C7, Pro).
/// </summary>
int ReportCommand(string[] rest)
{
    if (!RequirePro(ProFeature.ReportExport)) return 1;

    if (rest.Length == 0) { Console.Error.WriteLine("report export <--last|id> [--format csv|json] [--out <đường dẫn>]"); return 1; }

    var sub = rest[0].ToLowerInvariant();
    if (sub != "export") { Console.Error.WriteLine("report export <--last|id> [--format csv|json] [--out <đường dẫn>]"); return 1; }

    string? id = null, format = "csv", outPath = null;
    for (int i = 1; i < rest.Length; i++)
    {
        switch (rest[i])
        {
            case "--id": id = Next(rest, ref i); break;
            case "--format": format = (Next(rest, ref i) ?? "csv").ToLowerInvariant(); break;
            case "--out": outPath = Next(rest, ref i); break;
            case "--last": break;
        }
    }

    HistoryEntry? entry = null;
    var entries = historyStore.LoadAll();
    if (id is not null)
    {
        entry = entries.FirstOrDefault(e => e.Id.StartsWith(id, StringComparison.OrdinalIgnoreCase));
        if (entry is null) { Console.Error.WriteLine($"Không tìm thấy lịch sử: {id}"); return 1; }
    }
    else
    {
        entry = entries.FirstOrDefault();
        if (entry is null) { Console.Error.WriteLine("Chưa có lịch sử để xuất báo cáo."); return 0; }
    }

    var report = BuildReportFromJournal(entry.JournalPath);
    var targetFormat = format switch
    {
        "json" => ReportFormat.Json,
        "csv" => ReportFormat.Csv,
        _ => ReportFormat.Csv
    };

    if (string.IsNullOrWhiteSpace(outPath))
    {
        Console.WriteLine(targetFormat == ReportFormat.Json ? ReportExporter.ToJson(report) : ReportExporter.ToCsv(report));
        return 0;
    }

    ReportExporter.Export(report, outPath, targetFormat);
    Console.WriteLine($"Đã xuất báo cáo: {outPath}");
    return 0;
}

List<RenameReportEntry> BuildReportFromJournal(string journalPath)
{
    var result = new List<RenameReportEntry>();
    if (!fs.FileExists(journalPath)) return result;

    // Mỗi bước được ghi 2 lần (pending rồi done/failed): chỉ giữ trạng thái
    // cuối cùng cho mỗi tệp để báo cáo có đúng một dòng mỗi tệp.
    var latest = new Dictionary<string, (JournalEntry Entry, int Seq)>(StringComparer.OrdinalIgnoreCase);

    foreach (var step in UndoService.ReadSteps(journalPath, fs))
    {
        if (step.Type != "step" || step.From is null) continue;

        if (!latest.TryGetValue(step.From, out var current) || step.Seq >= current.Seq)
        {
            latest[step.From] = (step, step.Seq);
        }
    }

    foreach (var (from, (step, _)) in latest)
    {
        var status = step.Status == "done" ? "ok" : step.Status == "failed" ? "failed" : "skipped";
        result.Add(new RenameReportEntry
        {
            OldPath = from,
            NewPath = step.To,
            Status = status,
            TimestampUtc = DateTime.TryParse(step.StartedUtc, out var ts) ? ts : DateTime.UtcNow
        });
    }

    return result;
}

/// <summary>
/// Watch Folder (FR-D4, Pro): theo dõi thư mục và tự áp dụng preset cho tệp mới.
/// </summary>
int WatchCommand(string[] rest)
{
    if (!RequirePro(ProFeature.CommandLine)) return 1;

    if (rest.Length >= 2 && rest[0] == "--rules") return WatchRules(rest[1]);

    string? folder = null, presetName = null, include = null, exclude = null;
    bool dryRun = false, recursive = true;
    int settle = 1500;

    for (int i = 0; i < rest.Length; i++)
    {
        switch (rest[i])
        {
            case "--preset": presetName = Next(rest, ref i); break;
            case "--include": include = Next(rest, ref i); break;
            case "--exclude": exclude = Next(rest, ref i); break;
            case "--settle": settle = ParseInt(Next(rest, ref i), 1500); break;
            case "--dry-run": dryRun = true; break;
            case "--no-recursive": recursive = false; break;
            default:
                if (rest[i].StartsWith("--")) { Console.Error.WriteLine($"Tham số không hợp lệ: {rest[i]}"); return 1; }
                folder = rest[i];
                break;
        }
    }

    if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
    {
        Console.Error.WriteLine("Cần một thư mục hợp lệ: bfr watch <thư mục> --preset <tên>");
        return 1;
    }

    if (string.IsNullOrWhiteSpace(presetName))
    {
        Console.Error.WriteLine("Cần --preset <tên>.");
        return 1;
    }

    var preset = BuiltInPresets.All.FirstOrDefault(p => p.Name.Equals(presetName, StringComparison.OrdinalIgnoreCase))
                 ?? presetStore.Load(presetName);
    if (preset is null)
    {
        Console.Error.WriteLine($"Không tìm thấy preset: {presetName}");
        return 1;
    }

    var options = new WatchFolderOptions
    {
        Folder = folder!,
        PresetName = preset.Name,
        Recursive = recursive,
        DryRun = dryRun,
        SettleDelayMs = settle,
        IncludeGlob = include,
        ExcludeGlob = exclude
    };

    var processor = new WatchFolderProcessor(fs, journalDir);
    using var service = new WatchFolderService(options, preset, processor, fs, entry =>
    {
        Console.WriteLine(entry.Format());
    });

    using var stop = new ManualResetEventSlim(false);
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        stop.Set();
    };

    try
    {
        service.Start();
        Console.WriteLine("Đang theo dõi. Nhấn Ctrl+C để dừng.");
        stop.Wait();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Không theo dõi được: {ex.Message}");
        return 4;
    }
    finally
    {
        service.Stop();
    }

    return 0;
}

int WatchRules(string rulesFile)
{
    if (!licenseService.Allows(ProFeature.Workflow))
    {
        Console.Error.WriteLine(FeatureMessages.For(ProFeature.Workflow));
        return 1;
    }

    var rules = new WatchRuleStore(fs, rulesFile).Load();
    if (rules.Count == 0)
    {
        Console.Error.WriteLine("Không có quy tắc hợp lệ trong tệp.");
        return 1;
    }

    var errors = WatchFolderManager.Validate(rules);
    if (errors.Count > 0)
    {
        foreach (var e in errors) Console.Error.WriteLine(e);
        return 1;
    }

    var processor = new WorkflowWatchProcessor(fs, journalDir, new CompositeMetadataProvider(new ExifMetadataReader(), new MediaMetadataReader(), new AudioTagReader()));
    using var manager = new WatchFolderManager(fs, processor);
    manager.Logged += (_, e) => Console.WriteLine($"[{(string.IsNullOrWhiteSpace(e.Rule.Name) ? e.Rule.Folder : e.Rule.Name)}] {e.Entry.Format()}");

    using var stop = new ManualResetEventSlim(false);
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Set(); };

    try
    {
        manager.Start(rules);
        Console.WriteLine($"Đang theo dõi {manager.RunningCount} thư mục. Nhấn Ctrl+C để dừng.");
        stop.Wait();
    }
    catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"Không theo dõi được: {ex.Message}");
        return 4;
    }
    finally
    {
        manager.Stop();
    }

    return 0;
}

/// <summary>
/// Lệnh giấy phép: xem trạng thái, bắt đầu dùng thử hoặc mua Pro.
/// </summary>
async Task<int> LicenseCommand(string[] rest)
{
    var sub = rest.Length == 0 ? "status" : rest[0].ToLowerInvariant();

    switch (sub)
    {
        case "status":
        {
            var status = licenseService.Status;
            Console.WriteLine($"Cấp giấy phép: {status.Tier}{(status.IsTrial ? " (dùng thử)" : string.Empty)}");
            Console.WriteLine($"Nguồn: {status.Source}");
            if (status.TrialEndsUtc is not null)
                Console.WriteLine($"Dùng thử đến: {status.TrialEndsUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm}");
            if (status.ProductId is not null)
                Console.WriteLine($"Sản phẩm đã mua: {status.ProductId}");
            Console.WriteLine($"Số quy tắc tối đa: {(status.Limits.MaxRules == int.MaxValue ? "không giới hạn" : status.Limits.MaxRules.ToString())}");
            Console.WriteLine($"Độ sâu quét tối đa: {(status.Limits.MaxRecursionDepth == int.MaxValue ? "không giới hạn" : status.Limits.MaxRecursionDepth.ToString())}");
            Console.WriteLine($"Số preset tối đa: {(status.Limits.MaxSavedPresets == int.MaxValue ? "không giới hạn" : status.Limits.MaxSavedPresets.ToString())}");
            Console.WriteLine($"Số lần hoàn tác: {(status.Limits.MaxUndoEntries == int.MaxValue ? "không giới hạn" : status.Limits.MaxUndoEntries.ToString())}");
            Console.WriteLine($"EXIF/media: {(status.Limits.AllowExifDate ? "có" : "không")}; xuất báo cáo: {(status.Limits.AllowReportExport ? "có" : "không")}; CLI: {(status.Limits.AllowCommandLine ? "có" : "không")}");
            return 0;
        }
        case "trial":
        {
            if (licenseService.StartTrial())
            {
                Console.WriteLine("Đã bắt đầu dùng thử Pro 30 ngày.");
                return 0;
            }
            Console.Error.WriteLine("Không bắt đầu được dùng thử (đã dùng trước đó hoặc bản portable không hỗ trợ).");
            return 1;
        }
        case "buy":
        {
            if (licenseCoordinator.Provider is null || !licenseCoordinator.Provider.IsAvailable)
            {
                Console.Error.WriteLine("Chỉ mua được Pro trong bản cài từ Microsoft Store.");
                return 1;
            }

            var outcome = await licenseCoordinator.PurchaseProAsync();
            switch (outcome.Status)
            {
                case PurchaseStatus.Completed:
                    Console.WriteLine("Đã mua Pro. Cảm ơn bạn!");
                    return 0;
                case PurchaseStatus.UserCancelled:
                    Console.WriteLine("Đã huỷ giao dịch.");
                    return 0;
                default:
                    Console.Error.WriteLine($"Không mua được: {outcome.Error ?? outcome.Status.ToString()}");
                    return 1;
            }
        }
        default:
            Console.Error.WriteLine("license status|trial|buy");
            return 1;
    }
}

int ContextMenuCommand(string[] rest)
{
    if (!OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("Menu ngữ cảnh Explorer chỉ hỗ trợ trên Windows.");
        return 1;
    }

    if (rest.Length == 0)
    {
        Console.Error.WriteLine("bfr context-menu register|unregister|status|export-reg");
        return 1;
    }

    var sub = rest[0].ToLowerInvariant();
    switch (sub)
    {
        case "status":
            var registered = ShellIntegration.IsRegistered();
            var comRegistered = ShellIntegration.IsComRegistered();
            Console.WriteLine(registered ? "Đã đăng ký menu ngữ cảnh Explorer (HKCU)." : "Chưa đăng ký menu ngữ cảnh Explorer.");
            Console.WriteLine(comRegistered
                ? "COM handler IExplorerCommand: đã đăng ký."
                : "COM handler IExplorerCommand: chưa đăng ký (sẽ dùng fallback command-line).");
            return 0;

        case "register":
        {
            string? exePath = null;
            string? comServerPath = null;
            string displayName = ShellIntegration.DefaultDisplayName;
            for (int i = 1; i < rest.Length; i++)
            {
                if (rest[i] == "--path") exePath = Next(rest, ref i);
                else if (rest[i] is "--comserver" or "--comhost") comServerPath = Next(rest, ref i);
                else if (rest[i] == "--name") displayName = Next(rest, ref i) ?? displayName;
            }

            if (string.IsNullOrWhiteSpace(exePath))
            {
                var candidate = Path.Combine(AppContext.BaseDirectory, "BatchFileRenamer.exe");
                if (File.Exists(candidate)) exePath = candidate;
                else
                {
                    var appDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "BFR.App"));
                    var candidate2 = Path.Combine(appDir, "BatchFileRenamer.exe");
                    if (File.Exists(candidate2)) exePath = candidate2;
                    else exePath = Environment.ProcessPath ?? "BatchFileRenamer.exe";
                }
            }

            comServerPath ??= ShellIntegration.ResolveComServerPath(exePath);
            var ok = ShellIntegration.Register(exePath, displayName, comServerPath: comServerPath);
            if (ok)
            {
                Console.WriteLine($"Đã đăng ký menu ngữ cảnh thành công cho: {exePath}");
                Console.WriteLine(ShellIntegration.IsComRegistered()
                    ? $"COM server: {comServerPath}"
                    : "Cảnh báo: không tìm thấy BFR.Shell.exe hoặc đăng ký COM thất bại; đã dùng fallback command-line (không hỗ trợ tối ưu lô lớn).");
                return 0;
            }
            Console.Error.WriteLine("Không thể đăng ký menu ngữ cảnh.");
            return 1;
        }

        case "unregister":
        {
            var ok = ShellIntegration.Unregister();
            if (ok)
            {
                Console.WriteLine("Đã hủy đăng ký menu ngữ cảnh Explorer.");
                return 0;
            }
            Console.Error.WriteLine("Lỗi khi hủy đăng ký menu ngữ cảnh.");
            return 1;
        }

        case "export-reg":
        {
            if (rest.Length < 2) { Console.Error.WriteLine("bfr context-menu export-reg <output.reg> [--path <exe-path>]"); return 1; }
            var outPath = rest[1];
            string? exePath = null;
            string? comServerPath = null;
            for (int i = 2; i < rest.Length; i++)
            {
                if (rest[i] == "--path") exePath = Next(rest, ref i);
                else if (rest[i] is "--comserver" or "--comhost") comServerPath = Next(rest, ref i);
            }
            exePath ??= Path.Combine(AppContext.BaseDirectory, "BatchFileRenamer.exe");
            comServerPath ??= ShellIntegration.ResolveComServerPath(exePath);
            var content = ShellIntegration.GenerateRegContent(exePath, comServerPath: comServerPath);
            File.WriteAllText(outPath, content);
            Console.WriteLine($"Đã xuất tệp registry: {outPath}");
            return 0;
        }

        default:
            Console.Error.WriteLine("bfr context-menu register|unregister|status|export-reg");
            return 1;
    }
}

int RunTool(string[] toolArgs)
{
    // Thư mục tạm riêng của lần chạy này (nằm trong vùng tạm của BFR), dọn khi xong.
    var tempRoot = Path.Combine(PathGuard.BfrTempRoot, "cli-" + Guid.NewGuid().ToString("N"));
    try
    {
        var tools = new ToolCommands(fs, licenseService, Console.Out, Console.Error, Console.In, journalDir, tempRoot,
            historyStore, appVersion,
            new CompositeMetadataProvider(new ExifMetadataReader(), new MediaMetadataReader(), new AudioTagReader()),
            new WicImageProcessor());
        return tools.Run(toolArgs);
    }
    finally
    {
        try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

int Help() { PrintHelp(); return 0; }
int Unknown(string cmd) { Console.Error.WriteLine($"Lệnh không hỗ trợ: {cmd}"); PrintHelp(); return 1; }

static string? Next(string[] args, ref int i) => i + 1 < args.Length ? args[++i] : null;
static int ParseInt(string? s, int fallback) => int.TryParse(s, out var v) ? v : fallback;

static void PrintHelp()
{
    Console.WriteLine("""
Batch File Renamer CLI

  bfr rename <paths...> [options]
    --file-list <list.txt>           Nạp danh sách tệp từ tệp văn bản
    --preset <name|file.bfrpreset>   Dùng preset
    --find <pattern> --replace <s>   Tìm/thay nhanh (--regex, --ignore-case)
    --number <start>[:step[:width]]  Đánh số nhanh
    --recursive                      Quét thư mục con
    --depth <n>                      Độ sâu quét tối đa (Free: tối đa 2)
    --include <glob> / --exclude <glob>
    --allow-extension-change
    --dry-run                        Chỉ in kết quả, không ghi gì
    --yes                            Bỏ qua xác nhận
    --format text|json|csv

  bfr undo [--last | --id <id> | --journal <path>]
  bfr history
  bfr preset list|export|import
  bfr report export [--last|--id <id>] [--format csv|json] [--out <tệp>]
  bfr watch <thư mục> --preset <tên> [--include <glob>] [--exclude <glob>]
               [--settle <ms>] [--no-recursive] [--dry-run]
  bfr watch --rules <rules.json>               Theo dõi nhiều thư mục, mỗi thư mục một workflow
  bfr organize <paths...> --template "{exif.year}\{exif.month:00}" [--to <thư mục gốc>] [--copy] [--skip-existing]
  bfr flatten <thư mục> [--prefix-path] [--keep-empty]
  bfr empty-dirs <thư mục> [--no-junk]
  bfr wrap <paths...> [--per-file]
  bfr touch <paths...> --source fixed|shift|exif|filename|copy [--set created,modified,accessed]
               [--value <ngày giờ>] [--offset +2h] [--pattern "IMG_{yyyyMMdd_HHmmss}"] [--from <tệp>]
  bfr attrib <paths...> [--set readonly,hidden,archive] [--clear ...]
  bfr strip-gps <paths...>                      Xoá GPS khỏi ảnh JPEG (không mã hoá lại ảnh)
  bfr dupes <paths...> [--similar] [--keep newest|oldest|shortest|clean] [--prefer <thư mục>]
               [--apply] [--action quarantine|move --to <thư mục>]
  bfr hash <paths...> [--algo md5|sha1|sha256] [--out <tệp>]    bfr verify <tệp.sha256>
  bfr analyze <thư mục> [--top N] [--older-than <năm>] [--format json]
  bfr zip <paths...> [--group all|folder|files:N|mb:N] [--out <thư mục>] [--name <mẫu>] [--level fast|normal|small]
  bfr unzip <tệp.zip...> [--to <thư mục>] [--single]
  bfr image <paths...> [--format-out jpg|png|bmp|tif] [--long-edge N | --percent N | --fit WxH]
               [--quality 60-95] [--suffix _edit] [--out <thư mục>] [--overwrite] [--no-orient]
  bfr workflow run <tệp.json> <paths...>
  bfr quarantine status|purge [--days N]
    (mọi lệnh ghi đều có --dry-run, --yes, --format json, --recursive và hoàn tác được bằng bfr undo)
  bfr license status|trial|buy
  bfr context-menu register [--path <exe>] [--comserver <BFR.Shell.exe>] [--name <label>]
  bfr context-menu unregister|status
  bfr context-menu export-reg <output.reg> [--path <exe>]

Mã thoát: 0 thành công; 1 lỗi tham số; 2 xung đột/tên không hợp lệ;
          3 thực thi thất bại một phần; 4 lỗi hệ thống.
""");
}
