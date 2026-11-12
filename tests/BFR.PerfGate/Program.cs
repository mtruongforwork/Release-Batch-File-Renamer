namespace BFR.PerfGate;

using BFR.Core;
using BFR.Core.WatchFolder;
using System.Diagnostics;
using System.Globalization;
using System.Text;

/// <summary>
/// Cổng kiểm tra hiệu năng cho Giai đoạn 4 (rà soát hiệu năng & độ ổn định).
///
/// Đo trực tiếp các mục tiêu NFR-1..NFR-5 và trả về mã thoát khác 0 nếu vượt ngưỡng,
/// để CI chặn được hồi quy hiệu năng.
/// </summary>
public static class Program
{
    private sealed record Measurement(string Name, string Target, double Milliseconds, double LimitMs, string Note);

    public static int Main(string[] args)
    {
        var reportPath = args.Length > 0 ? args[0] : null;
        var measurements = new List<Measurement>();

        measurements.Add(MeasurePreview(10_000, limitMs: 300));
        measurements.Add(MeasurePreview(100_000, limitMs: 3_000));
        measurements.Add(MeasurePreviewMemory(100_000, limitMb: 250));
        measurements.Add(MeasureMetadataSkip(100_000, limitMs: 1_000));
        measurements.Add(MeasureScan(10_000, limitMs: 2_000));
        measurements.Add(MeasureApply(1_000));
        measurements.Add(MeasureWatchPlanner(100_000, limitMs: 1_000));
        measurements.Add(MeasureListMap(10_000, limitMs: 500));
        measurements.Add(MeasureReplaceDict(10_000, pairs: 500, limitMs: 1_500));
        measurements.Add(MeasureAutoResolve(100_000, limitMs: 3_000));

        var sb = new StringBuilder();
        sb.AppendLine("# Báo cáo hiệu năng — Batch File Renamer (Giai đoạn 4)");
        sb.AppendLine();
        sb.AppendLine($"Ngày đo: {DateTime.Now:yyyy-MM-dd HH:mm:ss} · Máy: {Environment.ProcessorCount} luồng xử lý");
        sb.AppendLine();
        sb.AppendLine("| Phép đo | Mục tiêu | Kết quả | Ngưỡng | Đạt |");
        sb.AppendLine("| :--- | :--- | ---: | ---: | :---: |");

        var failed = 0;
        foreach (var m in measurements)
        {
            var ok = m.Milliseconds <= m.LimitMs;
            if (!ok) failed++;

            sb.AppendLine($"| {m.Name} | {m.Target} | {m.Milliseconds:F0} ms | {m.LimitMs:F0} ms | {(ok ? "CÓ" : "**KHÔNG**")} |");

            if (!string.IsNullOrEmpty(m.Note))
            {
                sb.AppendLine($"| ↳ {m.Note} | | | | |");
            }
        }

        sb.AppendLine();
        sb.AppendLine(failed == 0
            ? "**Kết luận: đạt toàn bộ ngưỡng hiệu năng.**"
            : $"**Kết luận: {failed} phép đo vượt ngưỡng.**");

        Console.WriteLine(sb.ToString());

        if (reportPath is not null)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(reportPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(reportPath, sb.ToString(), new UTF8Encoding(false));
            Console.WriteLine($"Đã ghi báo cáo: {Path.GetFullPath(reportPath)}");
        }

        return failed == 0 ? 0 : 1;
    }

    private static Measurement MeasurePreview(int count, double limitMs)
    {
        var files = BuildItems(count);
        var pipeline = new RulePipeline();
        pipeline.Add(new FindReplaceRule { Pattern = " ", Replacement = "_", Scope = FindReplaceRule.ApplyScope.Name });
        pipeline.Add(new NumberingRule { Start = 1, Step = 1, Width = 3, Position = NumberingRule.NumberPosition.Start });

        var ctx = new RuleContext(DateTime.Now, "perf");
        var sw = Stopwatch.StartNew();
        var result = pipeline.Run(files, ctx);
        sw.Stop();

        return new Measurement(
            $"Preview {count:N0} tệp (NFR-1)",
            count == 10_000 ? "10.000 tệp < 300 ms" : "100.000 tệp < 3 s",
            sw.Elapsed.TotalMilliseconds,
            limitMs,
            $"{result.Items.Count:N0} dòng kết quả");
    }

    private static Measurement MeasurePreviewMemory(int count, double limitMb)
    {
        var files = BuildItems(count);
        var pipeline = new RulePipeline();
        pipeline.Add(new FindReplaceRule { Pattern = " ", Replacement = "_", Scope = FindReplaceRule.ApplyScope.Name });
        var ctx = new RuleContext(DateTime.Now, "perf");

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetTotalAllocatedBytes(precise: true);
        var sw = Stopwatch.StartNew();
        pipeline.Run(files, ctx);
        sw.Stop();
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        var mb = allocated / 1024d / 1024d;
        return new Measurement(
            $"Bộ nhớ preview {count:N0} tệp (NFR-5)",
            $"< {limitMb:F0} MB cấp phát",
            mb,
            limitMb,
            $"cấp phát {mb:F1} MB trong {sw.Elapsed.TotalMilliseconds:F0} ms");
    }

    private static Measurement MeasureMetadataSkip(int count, double limitMs)
    {
        var files = BuildItems(count, extension: "txt");
        var provider = new CompositeMetadataProvider(new ExifMetadataReader(), new MediaMetadataReader());

        var sw = Stopwatch.StartNew();
        foreach (var file in files) provider.Read(file);
        sw.Stop();

        return new Measurement(
            $"Bỏ qua metadata cho {count:N0} tệp không phải media",
            $"< {limitMs:F0} ms (không mở tệp)",
            sw.Elapsed.TotalMilliseconds,
            limitMs,
            "lọc theo phần mở rộng + bộ nhớ đệm có giới hạn");
    }

    private static Measurement MeasureScan(int count, double limitMs)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bfr-perf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            for (int i = 0; i < count; i++)
            {
                File.WriteAllText(Path.Combine(dir, $"file{i:D6}.txt"), "x");
            }

            var scanner = new FileScanner();
            var sw = Stopwatch.StartNew();
            var files = scanner.Scan(new[] { dir }, new ScanOptions { Recursive = true });
            sw.Stop();

            return new Measurement(
                $"Quét {count:N0} tệp thật (NFR-2)",
                "10.000 tệp < 2 s",
                sw.Elapsed.TotalMilliseconds,
                limitMs,
                $"{files.Count:N0} tệp trên SSD");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    private static Measurement MeasureApply(int count)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bfr-apply-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            for (int i = 0; i < count; i++)
            {
                File.WriteAllText(Path.Combine(dir, $"f{i:D6}.txt"), "x");
            }

            var pairs = new List<(string From, string To)>();
            for (int i = 0; i < count; i++)
            {
                pairs.Add((Path.Combine(dir, $"f{i:D6}.txt"), Path.Combine(dir, $"g{i:D6}.txt")));
            }

            var steps = new RenamePlanner().Plan(pairs);
            var sw = Stopwatch.StartNew();
            var result = new RenameExecutor(new RealFileSystem(), Path.Combine(dir, "journal")).Execute(steps);
            sw.Stop();

            return new Measurement(
                $"Áp dụng {count:N0} lần đổi tên thật (NFR-4)",
                "10.000 lần đổi tên trong 10–30 s (SSD)",
                sw.Elapsed.TotalMilliseconds,
                30_000,
                $"{result.Succeeded:N0} thành công, {result.Failed} lỗi, {count:N0} tệp trên SSD");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    private static Measurement MeasureWatchPlanner(int count, double limitMs)
    {
        var planner = new WatchFolderPlanner();
        var now = DateTime.UtcNow;

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < count; i++) planner.Notify($@"C:\watch\f{i}.jpg", now);
        sw.Stop();

        return new Measurement(
            $"Bộ gom sự kiện Watch Folder ({count:N0} sự kiện)",
            $"< {limitMs:F0} ms",
            sw.Elapsed.TotalMilliseconds,
            limitMs,
            $"{planner.PendingCount:N0} tệp đang chờ");
    }

    /// <summary>Đổi tên theo danh sách (v1.4 B2): 10.000 dòng ánh xạ cho 10.000 tệp.</summary>
    private static Measurement MeasureListMap(int count, double limitMs)
    {
        var files = BuildItems(count);
        var mapping = new StringBuilder();
        for (int i = 0; i < count; i++) mapping.Append("file").Append(i).Append('\t').Append("new_").Append(i).Append('\n');

        var pipeline = new RulePipeline();
        pipeline.Add(new ListMapRule { Mode = ListMapRule.ListMode.Map, Mapping = mapping.ToString() });

        var ctx = new RuleContext(DateTime.Now, "perf");
        var sw = Stopwatch.StartNew();
        var result = pipeline.Run(files, ctx);
        sw.Stop();

        var changed = result.Items.Count(i => i.NewName.StartsWith("new_", StringComparison.Ordinal));
        return new Measurement(
            $"Đổi tên theo danh sách {count:N0} dòng (v1.4 B2)",
            $"{count:N0} tệp < {limitMs:F0} ms",
            sw.Elapsed.TotalMilliseconds,
            changed == count ? limitMs : double.MaxValue,
            $"{changed:N0}/{count:N0} tệp được ánh xạ đúng");
    }

    /// <summary>Từ điển thay thế (v1.4 B2): nhiều cặp tìm → thay chạy một lượt trên nhiều tệp.</summary>
    private static Measurement MeasureReplaceDict(int fileCount, int pairs, double limitMs)
    {
        var files = BuildItems(fileCount);
        var dict = new StringBuilder();
        for (int i = 0; i < pairs; i++) dict.Append("zz").Append(i).Append('\t').Append("yy").Append(i).Append('\n');
        dict.Append("file\tdoc\n");

        var pipeline = new RulePipeline();
        pipeline.Add(new ReplaceDictRule { Pairs = dict.ToString() });

        var ctx = new RuleContext(DateTime.Now, "perf");
        var sw = Stopwatch.StartNew();
        var result = pipeline.Run(files, ctx);
        sw.Stop();

        var changed = result.Items.Count(i => i.NewName.StartsWith("doc", StringComparison.Ordinal));
        return new Measurement(
            $"Từ điển thay thế {pairs:N0} cặp x {fileCount:N0} tệp (v1.4 B2)",
            $"< {limitMs:F0} ms",
            sw.Elapsed.TotalMilliseconds,
            changed == fileCount ? limitMs : double.MaxValue,
            $"{changed:N0}/{fileCount:N0} tệp được thay");
    }

    /// <summary>Tự xử lý trùng tên (v1.4 B3): 100.000 tệp cùng đổi về một tên.</summary>
    private static Measurement MeasureAutoResolve(int count, double limitMs)
    {
        var files = BuildItems(count);
        var pipeline = new RulePipeline();
        pipeline.Add(new FindReplaceRule { Pattern = "file[0-9]+", Replacement = "same", IsRegex = true });

        var engine = new PreviewEngine(pipeline) { AutoResolveConflicts = true };
        var ctx = new RuleContext(DateTime.Now, "perf");
        var sw = Stopwatch.StartNew();
        var result = engine.Run(files, ctx);
        sw.Stop();

        var distinct = result.Items.Select(i => i.NewName.ToLowerInvariant()).Distinct().Count();
        return new Measurement(
            $"Tự xử lý trùng tên {count:N0} tệp (v1.4 B3)",
            $"< {limitMs:F0} ms",
            sw.Elapsed.TotalMilliseconds,
            distinct == count ? limitMs : double.MaxValue,
            $"{distinct:N0}/{count:N0} tên khác nhau sau khi xử lý ({engine.AutoResolvedCount:N0} tệp được thêm hậu tố)");
    }

    private static List<FileItem> BuildItems(int count, string extension = "jpg")
    {
        var items = new List<FileItem>(count);
        var now = DateTime.UtcNow;

        for (int i = 0; i < count; i++)
        {
            items.Add(new FileItem(
                $@"C:\data\file{i}.{extension}",
                @"C:\data",
                $"file{i}",
                extension,
                false,
                1024,
                now,
                now,
                i));
        }

        return items;
    }
}
