namespace BFR.Cli;

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BFR.Core;
using BFR.Core.Licensing;

/// <summary>
/// Các lệnh của bộ công cụ tệp v2.0 (organize, flatten, empty-dirs, wrap, touch, attrib, strip-gps, dupes, hash, verify,
/// analyze, zip, unzip, image, workflow, quarantine). Mọi lệnh ghi dùng chung một đường: lập kế hoạch → xem trước →
/// xác nhận → <see cref="OperationExecutor"/> với MỘT journal → lịch sử, nên <c>bfr undo</c> hoàn tác được mọi lệnh.
/// CLI là tính năng Pro nên các giới hạn Free theo từng module không áp dụng ở đây.
/// </summary>
internal sealed class ToolCommands
{
    public static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "organize", "flatten", "empty-dirs", "wrap", "touch", "attrib", "strip-gps", "dupes", "hash", "verify",
        "analyze", "zip", "unzip", "image", "workflow", "quarantine"
    };

    private readonly IFileSystem _fs;
    private readonly LicenseService _license;
    private readonly TextWriter _out;
    private readonly TextWriter _err;
    private readonly TextReader _in;
    private readonly string _journalDir;
    private readonly string _tempRoot;
    private readonly HistoryStore? _history;
    private readonly string _version;
    private readonly IMetadataProvider? _metadata;
    private readonly IImageProcessor? _images;
    private readonly string? _quarantineRoot;

    public ToolCommands(
        IFileSystem fs, LicenseService license, TextWriter output, TextWriter error, TextReader input,
        string journalDir, string tempRoot, HistoryStore? history, string version,
        IMetadataProvider? metadata = null, IImageProcessor? images = null, string? quarantineRoot = null)
    {
        _fs = fs;
        _license = license;
        _out = output;
        _err = error;
        _in = input;
        _journalDir = journalDir;
        _tempRoot = tempRoot;
        _history = history;
        _version = version;
        _metadata = metadata;
        _images = images;
        _quarantineRoot = quarantineRoot;
    }

    public int Run(string[] args)
    {
        if (!_license.Allows(ProFeature.CommandLine))
        {
            _err.WriteLine(FeatureMessages.For(ProFeature.CommandLine));
            _err.WriteLine("Nâng cấp Pro trong bản cài từ Microsoft Store để mở khoá tính năng này.");
            return 1;
        }

        var rest = args[1..];
        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "organize" => Organize(rest),
                "flatten" => Flatten(rest),
                "empty-dirs" => EmptyDirs(rest),
                "wrap" => Wrap(rest),
                "touch" => Touch(rest),
                "attrib" => Attrib(rest),
                "strip-gps" => StripGps(rest),
                "dupes" => Dupes(rest),
                "hash" => Hash(rest),
                "verify" => Verify(rest),
                "analyze" => Analyze(rest),
                "zip" => Zip(rest),
                "unzip" => Unzip(rest),
                "image" => Image(rest),
                "workflow" => WorkflowCommand(rest),
                "quarantine" => Quarantine(rest),
                _ => Fail($"Lệnh không hợp lệ: {args[0]}")
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException or InvalidOperationException)
        {
            _err.WriteLine($"Lỗi: {ex.Message}");
            return 4;
        }
    }

    // ---------- tiện ích dùng chung ----------

    private int Fail(string message)
    {
        _err.WriteLine(message);
        return 1;
    }

    private static bool Structured(CliArgs a) => (a.Get("--format") ?? "text").Equals("json", StringComparison.OrdinalIgnoreCase);

    private TextWriter Info(CliArgs a) => Structured(a) ? _err : _out;

    private IReadOnlyList<FileItem> Scan(CliArgs a, IEnumerable<string>? extra = null)
    {
        var paths = new List<string>(extra ?? a.Positional);
        var list = a.Get("--file-list");
        if (!string.IsNullOrWhiteSpace(list)) paths.AddRange(FileListReader.Read(list, _fs));

        return new FileScanner().Scan(paths, new ScanOptions
        {
            Recursive = a.Has("--recursive"),
            MaxDepth = a.Has("--recursive") ? (a.Get("--depth") is { } d && int.TryParse(d, out var n) ? n : int.MaxValue) : 0,
            IncludeGlob = a.Get("--include"),
            ExcludeGlob = a.Get("--exclude")
        });
    }

    private RuleContext Context(IReadOnlyList<FileItem> files)
    {
        var ctx = new RuleContext(DateTime.Now, Guid.NewGuid().ToString("N"));
        if (_metadata is null) return ctx;

        var map = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            var m = _metadata.Read(f);
            if (m.Count > 0) map[f.FullPath] = m;
        }
        return ctx with { FileMetadata = map };
    }

    private static string CommonDirectory(IReadOnlyList<FileItem> files)
    {
        var parts = files.Select(f => f.Directory.Replace('\\', '/').TrimEnd('/').Split('/')).ToList();
        var common = parts[0];
        foreach (var p in parts.Skip(1))
        {
            var len = 0;
            while (len < common.Length && len < p.Length && string.Equals(common[len], p[len], StringComparison.OrdinalIgnoreCase)) len++;
            common = common[..len];
        }
        var joined = string.Join(Path.DirectorySeparatorChar, common);
        return joined.Length == 0 ? files[0].Directory : joined;
    }

    /// <summary>Xem trước, xác nhận và thực thi một danh sách thao tác. Trả về mã thoát (0 ok, 3 có bước lỗi).</summary>
    private int Apply(string label, IReadOnlyList<Operation> ops, CliArgs a, object? extra = null)
    {
        var info = Info(a);
        if (ops.Count == 0)
        {
            info.WriteLine("Không có gì cần làm.");
            return 0;
        }

        if (Structured(a) && a.Has("--dry-run"))
        {
            _out.WriteLine(JsonSerializer.Serialize(new { command = label, dryRun = true, operations = ops.Select(Describe), extra }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        var shown = 0;
        foreach (var op in ops)
        {
            if (shown++ >= 200) { info.WriteLine($"… và {ops.Count - 200} thao tác nữa"); break; }
            info.WriteLine(Describe(op));
        }

        if (a.Has("--dry-run"))
        {
            info.WriteLine($"Dry-run: {ops.Count} thao tác, không ghi gì.");
            return 0;
        }

        if (!a.Has("--yes"))
        {
            _err.Write($"Áp dụng {ops.Count} thao tác? [y/N] ");
            var answer = _in.ReadLine();
            if (!string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase) && !string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase))
            {
                info.WriteLine("Đã hủy.");
                return 0;
            }
        }

        var result = new OperationExecutor(_fs, _journalDir, new QuarantineStore(_fs, _quarantineRoot)).Execute(ops, _version);

        _history?.Add(new HistoryEntry
        {
            Id = result.JournalId,
            StartedUtc = DateTime.UtcNow,
            Preset = label,
            Total = result.TotalSteps,
            Succeeded = result.Succeeded,
            Failed = result.Failed,
            Status = result.Cancelled ? "cancelled" : result.Failed > 0 ? "partial" : "completed",
            JournalPath = result.JournalPath
        });

        if (Structured(a))
        {
            _out.WriteLine(JsonSerializer.Serialize(new { command = label, journalId = result.JournalId, total = result.TotalSteps, succeeded = result.Succeeded, failed = result.Failed, extra }));
        }
        else
        {
            _out.WriteLine($"Đã thực hiện {result.Succeeded}/{result.TotalSteps} thao tác. Journal: {result.JournalId}");
        }

        foreach (var (step, error) in result.Errors) _err.WriteLine($"  Lỗi {step}: {error}");
        return result.Failed > 0 ? 3 : 0;
    }

    private static string Describe(Operation op) => op.Kind switch
    {
        OperationKind.Move or OperationKind.Copy or OperationKind.WriteFile => $"{op.Kind} {op.From} -> {op.To}",
        OperationKind.SetTimestamps => $"SetTimestamps {op.From}",
        OperationKind.SetAttributes => $"SetAttributes {op.From} = {op.AttributesAfter}",
        _ => $"{op.Kind} {op.From}"
    };

    private int ReportFolderPlan(FolderPlan plan, CliArgs a, string label)
    {
        foreach (var skip in plan.Skipped) _err.WriteLine($"Bỏ qua: {skip.Path} — {skip.Reason}");
        return Apply(label, plan.Operations, a);
    }

    // ---------- lệnh ----------

    private int Organize(string[] rest)
    {
        var a = CliArgs.Parse(rest, new[] { "--to", "--template" }, new[] { "--copy", "--skip-existing" });
        if (a.Error is not null) return Fail(a.Error);

        var template = a.Get("--template");
        if (string.IsNullOrWhiteSpace(template)) return Fail("Cần --template, ví dụ --template \"{exif.year}\\{exif.month:00}\".");

        var files = Scan(a);
        if (files.Count == 0) return Fail("Không tìm thấy tệp nào.");

        var root = a.Get("--to") ?? CommonDirectory(files);
        var options = new OrganizeOptions(root, template,
            a.Has("--copy") ? OrganizeMode.Copy : OrganizeMode.Move,
            a.Has("--skip-existing") ? OrganizeConflictPolicy.Skip : OrganizeConflictPolicy.Rename);
        var plan = OrganizePlanner.Plan(files, options, Context(files), _fs);

        foreach (var e in plan.TemplateErrors) _err.WriteLine(e);
        foreach (var bad in plan.Items.Where(i => i.Status is OrganizeStatus.Invalid or OrganizeStatus.Protected))
            _err.WriteLine($"{bad.File.FullPath}: {bad.Message}");
        if (plan.HasBlockingIssues)
        {
            _err.WriteLine("Có lỗi chặn. Không thay đổi gì.");
            return 2;
        }

        foreach (var item in plan.Items.Where(i => i.CrossVolume).Take(1))
            _err.WriteLine("Lưu ý: một số tệp nằm khác ổ đĩa nên sẽ được sao chép rồi xoá (chậm hơn).");

        return Apply("organize", plan.Operations, a, new { folders = plan.FolderCounts() });
    }

    private int Flatten(string[] rest)
    {
        var a = CliArgs.Parse(rest, new[] { "--separator" }, new[] { "--prefix-path", "--keep-empty" });
        if (a.Error is not null) return Fail(a.Error);
        if (a.Positional.Count != 1) return Fail("Cần đúng một thư mục gốc.");

        var plan = FolderToolsPlanner.Flatten(a.Positional[0],
            new FlattenOptions(a.Has("--prefix-path"), a.Get("--separator") ?? "_", !a.Has("--keep-empty")), _fs);
        return ReportFolderPlan(plan, a, "flatten");
    }

    private int EmptyDirs(string[] rest)
    {
        var a = CliArgs.Parse(rest, Array.Empty<string>(), new[] { "--no-junk" });
        if (a.Error is not null) return Fail(a.Error);
        if (a.Positional.Count != 1) return Fail("Cần đúng một thư mục gốc.");

        return ReportFolderPlan(FolderToolsPlanner.RemoveEmpty(a.Positional[0], new RemoveEmptyOptions(!a.Has("--no-junk")), _fs), a, "empty-dirs");
    }

    private int Wrap(string[] rest)
    {
        var a = CliArgs.Parse(rest, Array.Empty<string>(), new[] { "--per-file" });
        if (a.Error is not null) return Fail(a.Error);

        var files = Scan(a);
        if (files.Count == 0) return Fail("Không tìm thấy tệp nào.");
        return ReportFolderPlan(FolderToolsPlanner.Wrap(files, new WrapOptions(!a.Has("--per-file")), _fs), a, "wrap");
    }

    private int Touch(string[] rest)
    {
        var a = CliArgs.Parse(rest, new[] { "--source", "--set", "--value", "--offset", "--pattern", "--from" }, Array.Empty<string>());
        if (a.Error is not null) return Fail(a.Error);

        var source = (a.Get("--source") ?? "").ToLowerInvariant() switch
        {
            "fixed" => TimestampSource.Fixed,
            "shift" => TimestampSource.Shift,
            "exif" => TimestampSource.Exif,
            "filename" => TimestampSource.FileName,
            "copy" => TimestampSource.CopyFromFile,
            _ => (TimestampSource?)null
        };
        if (source is null) return Fail("Cần --source fixed|shift|exif|filename|copy.");

        bool created = false, modified = false, accessed = false;
        foreach (var part in (a.Get("--set") ?? "modified").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "created": created = true; break;
                case "modified": modified = true; break;
                case "accessed": accessed = true; break;
                default: return Fail($"--set không hợp lệ: {part}");
            }
        }

        DateTime? fixedLocal = null;
        if (source == TimestampSource.Fixed)
        {
            if (!DateTime.TryParse(a.Get("--value"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeLocal, out var parsed))
                return Fail("Cần --value ngày giờ hợp lệ (ví dụ 2024-05-12T10:30:00).");
            fixedLocal = parsed;
        }

        var offset = TimeSpan.Zero;
        if (a.Get("--offset") is { } off && !TryParseOffset(off, out offset)) return Fail("--offset không hợp lệ (ví dụ +2h, -30m, 1d).");

        var files = Scan(a);
        if (files.Count == 0) return Fail("Không tìm thấy tệp nào.");

        var plan = TimestampPlanner.Plan(files,
            new TimestampOptions(source.Value, created, modified, accessed, fixedLocal, offset, a.Get("--pattern"), a.Get("--from")),
            Context(files), _fs);

        foreach (var bad in plan.Items.Where(i => i.Status is TimestampStatus.NoData or TimestampStatus.Protected or TimestampStatus.Error))
            _err.WriteLine($"Bỏ qua: {bad.File.FullPath} — {bad.Message}");
        return Apply("touch", plan.Operations, a);
    }

    private static bool TryParseOffset(string text, out TimeSpan value)
    {
        value = TimeSpan.Zero;
        var t = text.Trim();
        if (t.Length < 2) return TimeSpan.TryParse(t, out value);

        var sign = t[0] == '-' ? -1 : 1;
        var body = t[0] is '+' or '-' ? t[1..] : t;
        var unit = char.ToLowerInvariant(body[^1]);
        if (!double.TryParse(body[..^1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n))
            return TimeSpan.TryParse(t, System.Globalization.CultureInfo.InvariantCulture, out value);

        value = unit switch
        {
            's' => TimeSpan.FromSeconds(n * sign),
            'm' => TimeSpan.FromMinutes(n * sign),
            'h' => TimeSpan.FromHours(n * sign),
            'd' => TimeSpan.FromDays(n * sign),
            _ => TimeSpan.Zero
        };
        return "smhd".Contains(unit);
    }

    private int Attrib(string[] rest)
    {
        var a = CliArgs.Parse(rest, new[] { "--set", "--clear" }, Array.Empty<string>());
        if (a.Error is not null) return Fail(a.Error);

        static bool TryFlags(string? text, out FileAttributes flags)
        {
            flags = 0;
            foreach (var part in (text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                switch (part.ToLowerInvariant())
                {
                    case "readonly": flags |= FileAttributes.ReadOnly; break;
                    case "hidden": flags |= FileAttributes.Hidden; break;
                    case "archive": flags |= FileAttributes.Archive; break;
                    default: return false;
                }
            }
            return true;
        }

        if (!TryFlags(a.Get("--set"), out var set) || !TryFlags(a.Get("--clear"), out var clear)) return Fail("Thuộc tính hợp lệ: readonly, hidden, archive.");
        if (set == 0 && clear == 0) return Fail("Cần --set hoặc --clear.");

        var files = Scan(a);
        if (files.Count == 0) return Fail("Không tìm thấy tệp nào.");
        return ReportFolderPlan(AttributePlanner.Plan(files, set, clear, _fs), a, "attrib");
    }

    private int StripGps(string[] rest)
    {
        var a = CliArgs.Parse(rest, Array.Empty<string>(), Array.Empty<string>());
        if (a.Error is not null) return Fail(a.Error);

        var files = Scan(a);
        if (files.Count == 0) return Fail("Không tìm thấy tệp nào.");

        var plan = GpsStripPlanner.Plan(files, _fs, _tempRoot);
        foreach (var item in plan.Items)
        {
            if (item.Status == GpsStripStatus.Ready && item.XmpGpsRemains)
                _err.WriteLine($"Cảnh báo: {item.File.FullPath} còn toạ độ GPS trong XMP (không xoá được mà không mã hoá lại ảnh).");
            else if (item.Status is GpsStripStatus.Error or GpsStripStatus.Protected)
                _err.WriteLine($"Bỏ qua: {item.File.FullPath} — {item.Message}");
        }
        Info(a).WriteLine($"{plan.Items.Count(i => i.Status == GpsStripStatus.Ready)} ảnh có GPS, {plan.Items.Count(i => i.Status == GpsStripStatus.NoGps)} không có, {plan.Items.Count(i => i.Status == GpsStripStatus.Unsupported)} không hỗ trợ.");

        var rc = Apply("strip-gps", plan.Operations, a);
        if (rc != 0 || a.Has("--dry-run")) CleanTemp(plan.Operations);
        return rc;
    }

    /// <summary>Xoá tệp tạm WriteFile chưa được dùng (xem trước, huỷ hoặc lỗi).</summary>
    private void CleanTemp(IReadOnlyList<Operation> ops)
    {
        foreach (var op in ops.Where(o => o.Kind == OperationKind.WriteFile))
        {
            try { if (_fs.FileExists(op.From)) _fs.Delete(op.From, recursive: false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private int Dupes(string[] rest)
    {
        var a = CliArgs.Parse(rest,
            new[] { "--keep", "--prefer", "--action", "--to", "--min-size", "--threshold" },
            new[] { "--similar", "--apply" });
        if (a.Error is not null) return Fail(a.Error);

        var files = Scan(a);
        if (files.Count == 0) return Fail("Không tìm thấy tệp nào.");

        IReadOnlyList<DuplicateGroup> groups;
        if (a.Has("--similar"))
        {
            if (_images is null) return Fail("Tìm ảnh gần giống chỉ có trên Windows.");
            groups = new SimilarImageFinder(_images).Find(files, new SimilarImageOptions(a.GetInt("--threshold", 6)));
        }
        else
        {
            groups = new DuplicateFinder(_fs).Find(files, new DuplicateOptions(MinSize: a.GetInt("--min-size", 1)));
        }

        if (groups.Count == 0)
        {
            Info(a).WriteLine("Không có tệp trùng.");
            return 0;
        }

        var rule = (a.Get("--keep") ?? "oldest").ToLowerInvariant() switch
        {
            "newest" => KeepRule.Newest,
            "oldest" => KeepRule.Oldest,
            "shortest" => KeepRule.ShortestPath,
            "clean" => KeepRule.CleanName,
            "prefer" => KeepRule.PreferFolder,
            _ => (KeepRule?)null
        };
        if (rule is null) return Fail("--keep: newest|oldest|shortest|clean|prefer.");
        var preferred = a.GetAll("--prefer");
        if (preferred.Count > 0 && rule != KeepRule.PreferFolder) rule = KeepRule.PreferFolder;

        var keepers = groups.Select(g => DuplicatePlanner.Choose(g, rule.Value, preferred)).ToList();

        if (Structured(a))
        {
            _out.WriteLine(JsonSerializer.Serialize(groups.Select((g, i) => new { keep = keepers[i].FullPath, duplicates = g.Files.Where(f => f.FullPath != keepers[i].FullPath).Select(f => f.FullPath), size = g.Size }), new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            for (var i = 0; i < groups.Count; i++)
            {
                _out.WriteLine($"Nhóm {i + 1} ({groups[i].Files.Count} tệp, {groups[i].Size} byte):");
                foreach (var f in groups[i].Files) _out.WriteLine($"  {(f == keepers[i] ? "GIỮ " : "     ")}{f.FullPath}");
            }
        }

        if (!a.Has("--apply"))
        {
            _err.WriteLine("Chỉ xem. Thêm --apply để đưa bản thừa vào khu cách ly (hoàn tác được).");
            return 0;
        }

        var action = (a.Get("--action") ?? "quarantine").ToLowerInvariant();
        if (action != "quarantine" && action != "move") return Fail("--action: quarantine|move.");
        if (action == "move" && string.IsNullOrWhiteSpace(a.Get("--to"))) return Fail("--action move cần --to <thư mục>.");

        var plan = DuplicatePlanner.Plan(groups, keepers,
            action == "move" ? DuplicateAction.MoveToFolder : DuplicateAction.Quarantine, _fs, a.Get("--to"));
        foreach (var skip in plan.Skipped) _err.WriteLine($"Bỏ qua: {skip.Path} — {skip.Reason}");
        return Apply("dupes", plan.Operations, a, new { bytesFreed = plan.BytesFreed });
    }

    private int Hash(string[] rest)
    {
        var a = CliArgs.Parse(rest, new[] { "--algo", "--out" }, Array.Empty<string>());
        if (a.Error is not null) return Fail(a.Error);

        var algo = (a.Get("--algo") ?? "sha256").ToLowerInvariant() switch
        {
            "md5" => ChecksumAlgorithm.Md5,
            "sha1" => ChecksumAlgorithm.Sha1,
            "sha256" => ChecksumAlgorithm.Sha256,
            _ => (ChecksumAlgorithm?)null
        };
        if (algo is null) return Fail("--algo: md5|sha1|sha256.");

        var files = Scan(a);
        if (files.Count == 0) return Fail("Không tìm thấy tệp nào.");

        var baseDir = CommonDirectory(files);
        var entries = new List<ChecksumEntry>();
        foreach (var f in files)
        {
            try
            {
                var hash = Checksum.Compute(_fs, f.FullPath, algo.Value);
                var relative = Path.GetRelativePath(baseDir, f.FullPath);
                entries.Add(new ChecksumEntry(hash, relative));
                if (a.Get("--out") is null) _out.WriteLine($"{hash}  {relative.Replace('\\', '/')}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _err.WriteLine($"Không đọc được {f.FullPath}: {ex.Message}");
            }
        }

        if (a.Get("--out") is { } outPath)
        {
            _fs.WriteAllText(outPath, Checksum.Format(entries));
            Info(a).WriteLine($"Đã ghi {entries.Count} dòng vào {outPath}. Kiểm tra bằng: bfr verify \"{outPath}\"");
        }
        return entries.Count == files.Count ? 0 : 3;
    }

    private int Verify(string[] rest)
    {
        var a = CliArgs.Parse(rest, new[] { "--algo" }, Array.Empty<string>());
        if (a.Error is not null) return Fail(a.Error);
        if (a.Positional.Count != 1) return Fail("Cần đúng một tệp .md5/.sha1/.sha256.");

        var file = a.Positional[0];
        var algo = Checksum.FromExtension(file) ?? (a.Get("--algo")?.ToLowerInvariant() switch
        {
            "md5" => ChecksumAlgorithm.Md5,
            "sha1" => ChecksumAlgorithm.Sha1,
            "sha256" => ChecksumAlgorithm.Sha256,
            _ => (ChecksumAlgorithm?)null
        });
        if (algo is null) return Fail("Không đoán được thuật toán: dùng tệp .md5/.sha1/.sha256 hoặc --algo.");
        if (!_fs.FileExists(file)) return Fail($"Không tìm thấy {file}");

        var entries = Checksum.Parse(_fs.ReadAllText(file), algo.Value);
        if (entries.Count == 0) return Fail("Tệp kiểm tra không có dòng hợp lệ.");

        var results = Checksum.Verify(_fs, Path.GetDirectoryName(Path.GetFullPath(file)) ?? ".", entries, algo.Value);
        foreach (var r in results) Info(a).WriteLine($"{r.Status,-9} {r.RelativePath}");

        var bad = results.Count(r => r.Status != ChecksumStatus.Match);
        if (Structured(a)) _out.WriteLine(JsonSerializer.Serialize(results.Select(r => new { path = r.RelativePath, status = r.Status.ToString() })));
        Info(a).WriteLine(bad == 0 ? $"Tất cả {results.Count} tệp khớp." : $"{bad}/{results.Count} tệp không khớp.");
        return bad == 0 ? 0 : 2;
    }

    private int Analyze(string[] rest)
    {
        var a = CliArgs.Parse(rest, new[] { "--top", "--older-than" }, new[] { "--no-ext-check" });
        if (a.Error is not null) return Fail(a.Error);
        if (a.Positional.Count != 1) return Fail("Cần đúng một thư mục.");

        var report = FolderAnalyzer.Analyze(_fs, a.Positional[0],
            new AnalyzeOptions(a.GetInt("--top", 20), a.GetInt("--older-than", 0), !a.Has("--no-ext-check")), DateTime.UtcNow);

        if (Structured(a))
        {
            _out.WriteLine(JsonSerializer.Serialize(new
            {
                totalBytes = report.TotalBytes,
                files = report.TotalFiles,
                folders = report.TotalFolders,
                byType = report.ByType.Select(t => new { type = t.Category.ToString(), t.Count, t.Bytes }),
                topFolders = report.TopFolders.Take(20),
                largest = report.Largest.Select(f => new { f.Path, f.Size }),
                old = report.Old.Select(f => new { f.Path, f.Size, f.ModifiedUtc }),
                mismatched = report.Mismatched,
                skipped = report.Skipped
            }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        _out.WriteLine($"{report.TotalFiles} tệp, {report.TotalFolders} thư mục, {report.TotalBytes:N0} byte");
        _out.WriteLine("Theo loại:");
        foreach (var t in report.ByType) _out.WriteLine($"  {t.Category,-9} {t.Count,8} tệp {t.Bytes,16:N0} byte");
        _out.WriteLine("Thư mục lớn nhất:");
        foreach (var f in report.TopFolders.Take(10)) _out.WriteLine($"  {f.Bytes,16:N0}  {f.Path}");
        _out.WriteLine("Tệp lớn nhất:");
        foreach (var f in report.Largest) _out.WriteLine($"  {f.Size,16:N0}  {f.Path}");
        if (report.Old.Count > 0)
        {
            _out.WriteLine($"Tệp cũ hơn {a.GetInt("--older-than", 0)} năm: {report.Old.Count}");
        }
        if (report.Mismatched.Count > 0)
        {
            _out.WriteLine("Đuôi sai:");
            foreach (var m in report.Mismatched) _out.WriteLine($"  {m.Path}  (đuôi .{m.CurrentExtension}, thực chất .{m.DetectedExtension})");
        }
        return 0;
    }

    private int Zip(string[] rest)
    {
        var a = CliArgs.Parse(rest, new[] { "--group", "--out", "--name", "--level" }, Array.Empty<string>());
        if (a.Error is not null) return Fail(a.Error);

        var files = Scan(a);
        if (files.Count == 0) return Fail("Không tìm thấy tệp nào.");

        var grouping = ArchiveGrouping.AllInOne;
        int maxFiles = 0;
        long maxBytes = 0;
        var group = (a.Get("--group") ?? "all").ToLowerInvariant();
        if (group == "folder") grouping = ArchiveGrouping.PerFolder;
        else if (group.StartsWith("files:") && int.TryParse(group[6..], out maxFiles) && maxFiles > 0) grouping = ArchiveGrouping.PerGroup;
        else if (group.StartsWith("mb:") && long.TryParse(group[3..], out var mb) && mb > 0) { grouping = ArchiveGrouping.PerGroup; maxBytes = mb * 1024 * 1024; }
        else if (group != "all") return Fail("--group: all|folder|files:N|mb:N.");

        var level = (a.Get("--level") ?? "normal").ToLowerInvariant() switch
        {
            "fast" => CompressionLevel.Fastest,
            "small" => CompressionLevel.SmallestSize,
            _ => CompressionLevel.Optimal
        };

        var options = new CompressOptions(grouping, a.Get("--out"), a.Get("--name") ?? "{name}", level, maxFiles, maxBytes);
        var jobs = ArchiveCompressor.Plan(files, options, _fs);
        if (jobs.Count == 0) return Fail("Không có tệp nào để nén.");

        foreach (var job in jobs) Info(a).WriteLine($"{job.ZipPath}: {job.Entries.Count} tệp");

        if (a.Has("--dry-run")) return 0;
        var run = ArchiveCompressor.Run(jobs, options, _fs, _tempRoot);
        foreach (var skip in run.Skipped) _err.WriteLine($"Bỏ qua: {skip.Path} — {skip.Reason}");

        var rc = Apply("zip", run.Operations, a, new { bytesBefore = run.BytesBefore, bytesAfter = run.BytesAfter });
        if (rc != 0) CleanTemp(run.Operations);
        return rc;
    }

    private int Unzip(string[] rest)
    {
        var a = CliArgs.Parse(rest, new[] { "--to", "--max-ratio" }, new[] { "--single" });
        if (a.Error is not null) return Fail(a.Error);

        var zips = a.Positional.Where(p => p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToList();
        if (zips.Count == 0) return Fail("Cần ít nhất một tệp .zip.");

        var options = new ExtractOptions(
            a.Has("--single") ? ExtractLayout.SingleFolder : ExtractLayout.OwnFolder,
            a.Get("--to"),
            MaxRatio: a.GetInt("--max-ratio", 100));
        var result = ArchiveExtractor.Extract(zips.Select(Path.GetFullPath).ToList(), options, _fs, _tempRoot);

        foreach (var r in result.Rejected) _err.WriteLine($"Từ chối {r.ZipPath}: {r.Reason}");
        foreach (var s in result.Skipped) _err.WriteLine($"Bỏ qua: {s.Path} — {s.Reason}");

        if (result.Operations.Count == 0)
        {
            return result.Rejected.Count > 0 ? 2 : 0;
        }

        var rc = Apply("unzip", result.Operations, a, new { rejected = result.Rejected.Count, bytes = result.BytesWritten });
        return rc != 0 ? rc : (result.Rejected.Count > 0 ? 2 : 0);
    }

    private int Image(string[] rest)
    {
        var a = CliArgs.Parse(rest,
            new[] { "--format-out", "--long-edge", "--percent", "--fit", "--quality", "--suffix", "--out" },
            new[] { "--no-orient", "--overwrite", "--upscale" });
        if (a.Error is not null) return Fail(a.Error);
        if (_images is null) return Fail("Công cụ ảnh chỉ có trên Windows.");

        var format = (a.Get("--format-out") ?? "keep").ToLowerInvariant() switch
        {
            "keep" => ImageFormat.Keep,
            "jpg" or "jpeg" => ImageFormat.Jpeg,
            "png" => ImageFormat.Png,
            "bmp" => ImageFormat.Bmp,
            "tif" or "tiff" => ImageFormat.Tiff,
            _ => (ImageFormat?)null
        };
        if (format is null) return Fail("--format-out: keep|jpg|png|bmp|tif.");

        var resize = ImageResizeMode.None;
        int value = 0, fw = 0, fh = 0;
        if (a.Get("--long-edge") is { } le) { resize = ImageResizeMode.LongEdge; value = int.TryParse(le, out var v) ? v : 0; }
        else if (a.Get("--percent") is { } pc) { resize = ImageResizeMode.Percent; value = int.TryParse(pc, out var v) ? v : 0; }
        else if (a.Get("--fit") is { } fit)
        {
            var parts = fit.ToLowerInvariant().Split('x');
            if (parts.Length != 2 || !int.TryParse(parts[0], out fw) || !int.TryParse(parts[1], out fh)) return Fail("--fit dạng RỘNGxCAO, ví dụ 1920x1080.");
            resize = ImageResizeMode.Fit;
        }

        var options = new ImageOptions(
            format.Value, resize, value, fw, fh, a.GetInt("--quality", 85), !a.Has("--no-orient"), a.Has("--upscale"),
            a.Get("--suffix") ?? "_edit", a.Get("--out"), a.Has("--overwrite"));

        var files = Scan(a);
        var plan = ImagePlanner.Plan(files, options, _fs);
        foreach (var e in plan.Errors) _err.WriteLine(e);
        if (plan.Errors.Count > 0) return 2;

        var ready = plan.Items.Count(i => i.Status == ImageStatus.Ready);
        if (ready == 0) return Fail("Không có ảnh nào xử lý được.");

        if (a.Has("--dry-run"))
        {
            foreach (var i in plan.Items.Where(i => i.Status == ImageStatus.Ready)) Info(a).WriteLine($"{i.File.FullPath} -> {i.OutputPath}");
            Info(a).WriteLine($"Dry-run: {ready} ảnh, không ghi gì.");
            return 0;
        }

        var batch = ImageBatch.Run(plan, options, _images, _fs, _tempRoot);
        foreach (var f in batch.Failures) _err.WriteLine($"Lỗi {f.Path}: {f.Error}");

        var rc = Apply("image", batch.Operations, a, new { before = batch.BytesBefore, after = batch.BytesAfter, failed = batch.Failures.Count });
        if (rc != 0) CleanTemp(batch.Operations);
        return rc != 0 ? rc : (batch.Failures.Count > 0 ? 3 : 0);
    }

    private int WorkflowCommand(string[] rest)
    {
        if (rest.Length == 0 || !rest[0].Equals("run", StringComparison.OrdinalIgnoreCase)) return Fail("Dùng: bfr workflow run <tệp.json> <đường dẫn…>");

        var a = CliArgs.Parse(rest[1..], Array.Empty<string>(), Array.Empty<string>());
        if (a.Error is not null) return Fail(a.Error);
        if (a.Positional.Count < 2) return Fail("Cần tệp workflow và ít nhất một đường dẫn.");

        var definition = WorkflowSerializer.Parse(_fs.ReadAllText(a.Positional[0]));
        var files = Scan(a, a.Positional.Skip(1));
        if (files.Count == 0) return Fail("Không tìm thấy tệp nào.");

        var plan = WorkflowPlanner.Plan(files, definition.Steps, _fs, Context(files), _metadata);
        foreach (var s in plan.Steps) Info(a).WriteLine($"Bước {s.Index + 1} ({s.Kind}): {s.Operations.Count} thao tác, {s.FilesIn} → {s.FilesOut} tệp");
        foreach (var e in plan.Errors) _err.WriteLine(e);
        if (!plan.IsValid)
        {
            _err.WriteLine("Workflow có lỗi. Không thay đổi gì.");
            return 2;
        }

        return Apply("workflow:" + definition.Name, plan.Operations, a);
    }

    private int Quarantine(string[] rest)
    {
        var a = CliArgs.Parse(rest.Skip(1).ToArray(), new[] { "--days" }, Array.Empty<string>());
        if (a.Error is not null || rest.Length == 0) return Fail(a.Error ?? "Dùng: bfr quarantine status|purge [--days N]");

        var store = new QuarantineStore(_fs, _quarantineRoot);
        switch (rest[0].ToLowerInvariant())
        {
            case "status":
            {
                var (count, bytes) = store.GetUsage();
                _out.WriteLine($"Khu cách ly: {count} tệp, {bytes:N0} byte ({store.Root})");
                return 0;
            }
            case "purge":
            {
                // Không bao giờ dọn tệp mới hơn thời hạn tối thiểu: khu cách ly là lưới an toàn cuối cùng của người dùng.
                var days = Math.Max(a.GetInt("--days", QuarantineStore.DefaultRetentionDays), QuarantineStore.DefaultRetentionDays);
                var removed = store.Purge(TimeSpan.FromDays(days));
                _out.WriteLine($"Đã dọn {removed} mục cũ hơn {days} ngày.");
                return 0;
            }
            default:
                return Fail("Dùng: bfr quarantine status|purge [--days N]");
        }
    }
}
