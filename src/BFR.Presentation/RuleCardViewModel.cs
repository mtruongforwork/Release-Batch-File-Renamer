namespace BFR.Presentation;

using BFR.Core;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows.Input;

public enum RuleParameterKind
{
    Text,
    Number,
    Toggle,
    Choice,
    /// <summary>Ô nhập nhiều dòng (danh sách dán từ Excel).</summary>
    Multiline
}

/// <summary>
/// Một tham số của quy tắc, sửa tại chỗ trên thẻ quy tắc (không mở hộp thoại).
/// Ghi thẳng về <see cref="RuleDefinition.Params"/> nên preset luôn ở dạng JSON chuẩn.
/// </summary>
/// <summary>Một lựa chọn của tham số quy tắc: giá trị thô (lưu vào preset) + nhãn hiển thị.</summary>
public sealed class RuleParameterOption
{
    public RuleParameterOption(string value, string label)
    {
        Value = value;
        Label = label;
    }

    /// <summary>Giá trị thô (ví dụ "ReplaceAll") — thứ được ghi vào preset.</summary>
    public string Value { get; }

    /// <summary>Nhãn đã địa phương hóa (ví dụ "Thay tất cả").</summary>
    public string Label { get; }

    public override string ToString() => Label;
}

/// <summary>Nhãn địa phương hóa cho các giá trị lựa chọn của tham số quy tắc.</summary>
public static class RuleOptionLabels
{
    public static string Get(string value, IStringProvider s) => value switch
    {
        "Name" => s.T("OptionName"),
        "Extension" => s.T("OptionExtension"),
        "Both" => s.T("OptionBoth"),
        "Start" => s.T("OptionStart"),
        "End" => s.T("OptionEnd"),
        "ReplaceAll" => s.T("OptionReplaceAll"),
        "Index" => s.T("OptionIndex"),
        "AtIndex" => s.T("OptionAtIndex"),
        "Decimal" => s.T("OptionDecimal"),
        "Letters" => s.T("OptionLetters"),
        "Roman" => s.T("OptionRoman"),
        "None" => s.T("OptionNone"),
        "ByFolder" => s.T("OptionByFolder"),
        "ByTokenChange" => s.T("OptionByTokenChange"),
        "Lower" => s.T("OptionLower"),
        "Upper" => s.T("OptionUpper"),
        "Title" => s.T("OptionTitle"),
        "Sentence" => s.T("OptionSentence"),
        "Keep" => s.T("OptionKeep"),
        "Dash" => s.T("OptionDash"),
        "Underscore" => s.T("OptionUnderscore"),
        "Remove" => s.T("OptionRemove"),
        "Set" => s.T("OptionSet"),
        "Created" => s.T("OptionCreated"),
        "Modified" => s.T("OptionModified"),
        "Taken" => s.T("OptionTaken"),
        "Media" => s.T("OptionMedia"),
        "Now" => s.T("OptionNow"),
        "Map" => s.T("OptionListMap"),
        "ByOrder" => s.T("OptionListByOrder"),
        "FileName" => s.T("OptionFileName"),
        _ => value
    };
}

public sealed class RuleParameterViewModel : ObservableObject
{
    private readonly RuleDefinition _rule;
    private readonly IStringProvider _strings;

    public RuleParameterViewModel(
        RuleDefinition rule,
        string key,
        string label,
        RuleParameterKind kind,
        IReadOnlyList<string>? options = null,
        IStringProvider? strings = null)
    {
        _rule = rule;
        _strings = strings ?? new KeyOnlyStringProvider();
        Key = key;
        Label = label;
        Kind = kind;
        Options = (options ?? Array.Empty<string>())
            .Select(value => new RuleParameterOption(value, RuleOptionLabels.Get(value, _strings)))
            .ToArray();
    }

    public string Key { get; }

    public string Label { get; }

    public RuleParameterKind Kind { get; }

    /// <summary>True nếu tham số là danh sách có thể nhập từ tệp CSV/TXT (nút "Nhập từ tệp").</summary>
    public bool CanImportFile => Kind == RuleParameterKind.Multiline && Key is "mapping" or "pairs";

    /// <summary>Các lựa chọn (giá trị thô + nhãn đã địa phương hóa).</summary>
    public IReadOnlyList<RuleParameterOption> Options { get; }

    /// <summary>Lựa chọn đang chọn (đồng bộ hai chiều với ComboBox).</summary>
    public RuleParameterOption? SelectedOption
    {
        get
        {
            var current = ChoiceValue;
            return Options.FirstOrDefault(o => o.Value == current);
        }
        set
        {
            if (value is null) return;
            ChoiceValue = value.Value;
        }
    }

    public string TextValue
    {
        get => Kind == RuleParameterKind.Number ? ReadNumberText() : ReadString();
        set
        {
            if (Kind == RuleParameterKind.Number)
            {
                // Tham số số phải lưu dạng JSON number, nếu không quy tắc sẽ mất giá trị.
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) return;
                if (ReadNumber() == number) return;

                Write(number);
                Raise();
                Raise(nameof(ValueText));
                return;
            }

            if (ReadString() == value) return;
            Write(value);
            Raise();
            Raise(nameof(ValueText));
        }
    }

    public bool BoolValue
    {
        get => ReadBool();
        set
        {
            if (ReadBool() == value) return;
            Write(value);
            Raise();
        }
    }

    public string ChoiceValue
    {
        get => ReadString();
        set
        {
            if (ReadString() == value) return;
            Write(value);
            Raise();
            Raise(nameof(ValueText));
            RaiseSelection();
        }
    }

    /// <summary>Giá trị hiển thị dạng chuỗi (dùng cho tóm tắt 1 dòng).</summary>
    public string ValueText => Kind switch
    {
        RuleParameterKind.Toggle => BoolValue ? "✓" : "✗",
        RuleParameterKind.Number => TextValue,
        _ => TextValue
    };

    public bool HasError
    {
        get => _hasError;
        set { if (Set(ref _hasError, value)) Raise(); }
    }

    private bool _hasError;

    public string ErrorMessage
    {
        get => _errorMessage;
        set { if (Set(ref _errorMessage, value)) Raise(); }
    }

    private string _errorMessage = string.Empty;

    private void Raise() => Raise(nameof(TextValue));

    /// <summary>Raise khi lựa chọn thay đổi (ComboBox cần cập nhật lại SelectedOption).</summary>
    public void RaiseSelection() => Raise(nameof(SelectedOption));

    private string ReadString() =>
        _rule.Params.TryGetValue(Key, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : string.Empty;

    private int ReadNumber() =>
        _rule.Params.TryGetValue(Key, out var element) && element.ValueKind == JsonValueKind.Number
            ? element.GetInt32()
            : 0;

    private string ReadNumberText() =>
        _rule.Params.TryGetValue(Key, out var element) && element.ValueKind == JsonValueKind.Number
            ? element.GetInt32().ToString(CultureInfo.InvariantCulture)
            : string.Empty;

    private bool ReadBool() =>
        _rule.Params.TryGetValue(Key, out var element) && element.ValueKind == JsonValueKind.True;

    private void Write(object? value) =>
        _rule.Params[Key] = JsonSerializer.SerializeToElement(value);
}

/// <summary>
/// Thẻ quy tắc trong Rule Builder: bật/tắt, tóm tắt 1 dòng, mở rộng tại chỗ để sửa tham số.
/// </summary>
public sealed class RuleCardViewModel : ObservableObject
{
    private readonly RuleDefinition _rule;
    private readonly IStringProvider _strings;
    private readonly Action<RuleCardViewModel, int>? _moveHandler;

    public RuleCardViewModel(
        RuleDefinition rule,
        IStringProvider? strings = null,
        Action<RuleCardViewModel, int>? moveHandler = null)
    {
        _rule = rule;
        _strings = strings ?? new KeyOnlyStringProvider();
        _moveHandler = moveHandler;
        Parameters = new ObservableCollection<RuleParameterViewModel>(BuildParameters(rule, _strings));

        MoveUpCommand = new RelayCommand(() => _moveHandler?.Invoke(this, -1), () => CanMoveUp);
        MoveDownCommand = new RelayCommand(() => _moveHandler?.Invoke(this, 1), () => CanMoveDown);

        foreach (var parameter in Parameters)
        {
            parameter.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(RuleParameterViewModel.HasError)) return;
                Raise(nameof(Summary));
                Changed?.Invoke(this, EventArgs.Empty);
            };
        }
    }

    /// <summary>Đưa quy tắc lên trong chuỗi.</summary>
    public ICommand MoveUpCommand { get; }

    /// <summary>Đưa quy tắc xuống trong chuỗi.</summary>
    public ICommand MoveDownCommand { get; }

    /// <summary>True nếu quy tắc này có thể mở Trình thử Regex (quy tắc Tìm và thay).</summary>
    public bool CanOpenRegexTester => string.Equals(_rule.Type, "FindReplace", StringComparison.Ordinal);

    /// <summary>True nếu quy tắc có thể đưa lên.</summary>
    public bool CanMoveUp
    {
        get => _canMoveUp;
        private set
        {
            if (!Set(ref _canMoveUp, value)) return;
            (MoveUpCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    private bool _canMoveUp;

    /// <summary>True nếu quy tắc có thể đưa xuống.</summary>
    public bool CanMoveDown
    {
        get => _canMoveDown;
        private set
        {
            if (!Set(ref _canMoveDown, value)) return;
            (MoveDownCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    private bool _canMoveDown;

    /// <summary>Cập nhật vị trí của quy tắc trong chuỗi (để bật/tắt nút lên/xuống).</summary>
    public void UpdateOrder(int index, int count)
    {
        CanMoveUp = index > 0;
        CanMoveDown = index >= 0 && index < count - 1;
    }

    /// <summary>Báo khi tham số hoặc trạng thái quy tắc thay đổi (để tính lại preview).</summary>
    public event EventHandler? Changed;

    public RuleDefinition Definition => _rule;

    public string Type => _rule.Type;

    public bool IsEnabled
    {
        get => _rule.Enabled;
        set
        {
            if (_rule.Enabled == value) return;
            _rule.Enabled = value;
            Raise();
            Raise(nameof(Title));
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => Set(ref _isExpanded, value);
    }

    private bool _isExpanded;

    public ObservableCollection<RuleParameterViewModel> Parameters { get; }

    public bool HasError => Parameters.Any(p => p.HasError);

    /// <summary>Tên quy tắc (đã địa phương hóa).</summary>
    public string Title => RuleTitles.Get(_rule.Type, _rule.Name, _strings);

    /// <summary>Tóm tắt 1 dòng: "bắt đầu 1, bước 1, 3 chữ số".</summary>
    public string Summary => RuleSummaries.Build(_rule, _strings);

    /// <summary>Danh sách loại quy tắc có thể thêm (dùng cho flyout "+ Thêm quy tắc").</summary>
    public static IReadOnlyList<string> AllRuleTypes { get; } = new[]
    {
        "FindReplace", "Numbering", "Trim", "Insert", "Case", "Sanitize", "Date", "Extension", "Template",
        "ListMap", "ReplaceDict"
    };

    private static IEnumerable<RuleParameterViewModel> BuildParameters(RuleDefinition rule, IStringProvider s)
    {
        switch (rule.Type)
        {
            case "FindReplace":
                yield return new RuleParameterViewModel(rule, "pattern", s.T("ParamFindReplacePattern"), RuleParameterKind.Text, strings: s);
                yield return new RuleParameterViewModel(rule, "replacement", s.T("ParamFindReplaceReplacement"), RuleParameterKind.Text, strings: s);
                yield return new RuleParameterViewModel(rule, "isRegex", s.T("ParamFindReplaceUseRegex"), RuleParameterKind.Toggle, strings: s);
                yield return new RuleParameterViewModel(rule, "ignoreCase", s.T("ParamFindReplaceIgnoreCase"), RuleParameterKind.Toggle, strings: s);
                yield return new RuleParameterViewModel(rule, "replaceAll", s.T("ParamFindReplaceReplaceAll"), RuleParameterKind.Toggle, strings: s);
                yield return new RuleParameterViewModel(rule, "scope", s.T("ParamFindReplaceScope"), RuleParameterKind.Choice,
                    new[] { "Name", "Extension", "Both" }, s);
                break;

            case "Numbering":
                yield return new RuleParameterViewModel(rule, "start", s.T("ParamNumberingStart"), RuleParameterKind.Number, strings: s);
                yield return new RuleParameterViewModel(rule, "step", s.T("ParamNumberingStep"), RuleParameterKind.Number, strings: s);
                yield return new RuleParameterViewModel(rule, "width", s.T("ParamNumberingWidth"), RuleParameterKind.Number, strings: s);
                yield return new RuleParameterViewModel(rule, "position", s.T("ParamNumberingPosition"), RuleParameterKind.Choice,
                    new[] { "Start", "End", "ReplaceAll", "Index" }, s);
                yield return new RuleParameterViewModel(rule, "separator", s.T("ParamNumberingSeparator"), RuleParameterKind.Text, strings: s);
                yield return new RuleParameterViewModel(rule, "kind", s.T("ParamNumberingStyle"), RuleParameterKind.Choice,
                    new[] { "Decimal", "Letters", "Roman" }, s);
                yield return new RuleParameterViewModel(rule, "resetMode", s.T("ParamNumberingResetBy"), RuleParameterKind.Choice,
                    new[] { "None", "ByFolder", "ByTokenChange" }, s);
                break;

            case "Trim":
                yield return new RuleParameterViewModel(rule, "removeChars", s.T("ParamTrimChars"), RuleParameterKind.Text, strings: s);
                yield return new RuleParameterViewModel(rule, "collapseSpaces", s.T("ParamTrimCollapseSpaces"), RuleParameterKind.Toggle, strings: s);
                yield return new RuleParameterViewModel(rule, "trimEnds", s.T("ParamTrimTrimEnds"), RuleParameterKind.Toggle, strings: s);
                yield return new RuleParameterViewModel(rule, "removeTrailingDotSpace", s.T("ParamTrimRemoveTrailingDot"), RuleParameterKind.Toggle, strings: s);
                yield return new RuleParameterViewModel(rule, "removeFirstN", s.T("ParamTrimRemoveFirstN"), RuleParameterKind.Number, strings: s);
                yield return new RuleParameterViewModel(rule, "removeLastN", s.T("ParamTrimRemoveLastN"), RuleParameterKind.Number, strings: s);
                break;

            case "Insert":
                yield return new RuleParameterViewModel(rule, "text", s.T("ParamInsertText"), RuleParameterKind.Text, strings: s);
                yield return new RuleParameterViewModel(rule, "position", s.T("ParamNumberingPosition"), RuleParameterKind.Choice,
                    new[] { "Start", "End", "AtIndex" }, s);
                yield return new RuleParameterViewModel(rule, "index", s.T("ParamInsertIndex"), RuleParameterKind.Number, strings: s);
                yield return new RuleParameterViewModel(rule, "countFromEnd", s.T("ParamInsertCountFromEnd"), RuleParameterKind.Toggle, strings: s);
                break;

            case "Case":
                yield return new RuleParameterViewModel(rule, "mode", s.T("ParamCaseStyle"), RuleParameterKind.Choice,
                    new[] { "Lower", "Upper", "Title", "Sentence" }, s);
                yield return new RuleParameterViewModel(rule, "scope", s.T("ParamFindReplaceScope"), RuleParameterKind.Choice,
                    new[] { "Name", "Extension", "Both" }, s);
                yield return new RuleParameterViewModel(rule, "exceptions", s.T("ParamCaseExceptions"), RuleParameterKind.Text, strings: s);
                break;

            case "Sanitize":
                yield return new RuleParameterViewModel(rule, "removeDiacritics", s.T("ParamSanitizeDiacritics"), RuleParameterKind.Toggle, strings: s);
                yield return new RuleParameterViewModel(rule, "normalizeNfc", s.T("ParamSanitizeNfc"), RuleParameterKind.Toggle, strings: s);
                yield return new RuleParameterViewModel(rule, "replaceInvalidChars", s.T("ParamSanitizeInvalidChars"), RuleParameterKind.Toggle, strings: s);
                yield return new RuleParameterViewModel(rule, "invalidCharReplacement", s.T("ParamSanitizeInvalidCharReplacement"), RuleParameterKind.Text, strings: s);
                yield return new RuleParameterViewModel(rule, "spaces", s.T("ParamSanitizeSpaces"), RuleParameterKind.Choice,
                    new[] { "Keep", "Dash", "Underscore", "Remove" }, s);
                yield return new RuleParameterViewModel(rule, "slugify", s.T("ParamSanitizeSlug"), RuleParameterKind.Toggle, strings: s);
                break;

            case "Date":
                yield return new RuleParameterViewModel(rule, "source", s.T("ParamDateSource"), RuleParameterKind.Choice,
                    new[] { "Created", "Modified", "Taken", "Media", "Now" }, s);
                yield return new RuleParameterViewModel(rule, "format", s.T("ParamDateFormat"), RuleParameterKind.Text, strings: s);
                yield return new RuleParameterViewModel(rule, "position", s.T("ParamNumberingPosition"), RuleParameterKind.Choice,
                    new[] { "Start", "End", "ReplaceAll" }, s);
                yield return new RuleParameterViewModel(rule, "separator", s.T("ParamNumberingSeparator"), RuleParameterKind.Text, strings: s);
                yield return new RuleParameterViewModel(rule, "useUtc", s.T("ParamDateUtc"), RuleParameterKind.Toggle, strings: s);
                break;

            case "Extension":
                yield return new RuleParameterViewModel(rule, "mode", s.T("ParamExtensionMode"), RuleParameterKind.Choice,
                    new[] { "Keep", "Lower", "Upper", "Set", "Remove" }, s);
                yield return new RuleParameterViewModel(rule, "newExtension", s.T("ParamExtensionNew"), RuleParameterKind.Text, strings: s);
                yield return new RuleParameterViewModel(rule, "allowExtensionChange", s.T("ParamExtensionAllowChange"), RuleParameterKind.Toggle, strings: s);
                break;

            case "Template":
                yield return new RuleParameterViewModel(rule, "template", s.T("ParamTemplateText"), RuleParameterKind.Text, strings: s);
                break;

            case "ListMap":
                yield return new RuleParameterViewModel(rule, "mode", s.T("ParamListMode"), RuleParameterKind.Choice,
                    new[] { "Map", "ByOrder" }, s);
                yield return new RuleParameterViewModel(rule, "matchBy", s.T("ParamListMatchBy"), RuleParameterKind.Choice,
                    new[] { "Name", "FileName" }, s);
                yield return new RuleParameterViewModel(rule, "ignoreCase", s.T("ParamFindReplaceIgnoreCase"), RuleParameterKind.Toggle, strings: s);
                yield return new RuleParameterViewModel(rule, "mapping", s.T("ParamListMapping"), RuleParameterKind.Multiline, strings: s);
                break;

            case "ReplaceDict":
                yield return new RuleParameterViewModel(rule, "ignoreCase", s.T("ParamFindReplaceIgnoreCase"), RuleParameterKind.Toggle, strings: s);
                yield return new RuleParameterViewModel(rule, "pairs", s.T("ParamReplaceDictPairs"), RuleParameterKind.Multiline, strings: s);
                break;
        }
    }
}

/// <summary>Tên hiển thị của loại quy tắc (địa phương hóa).</summary>
public static class RuleTitles
{
    public static string Get(string type, string? customName, IStringProvider s) =>
        !string.IsNullOrWhiteSpace(customName)
            ? customName!
            : type switch
            {
                "FindReplace" => s.T("RuleTypeFindReplace"),
                "Numbering" => s.T("RuleTypeNumbering"),
                "Trim" => s.T("RuleTypeTrim"),
                "Insert" => s.T("RuleTypeInsert"),
                "Case" => s.T("RuleTypeCase"),
                "Sanitize" => s.T("RuleTypeSanitize"),
                "Date" => s.T("RuleTypeDate"),
                "Extension" => s.T("RuleTypeExtension"),
                "Template" => s.T("RuleTypeTemplate"),
                "ListMap" => s.T("RuleTypeListMap"),
                "ReplaceDict" => s.T("RuleTypeReplaceDict"),
                _ => type
            };
}

/// <summary>Tóm tắt 1 dòng cho thẻ quy tắc.</summary>
public static class RuleSummaries
{
    public static string Build(RuleDefinition rule, IStringProvider s)
    {
        string P(string key, string fallback = "") =>
            rule.Params.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.String
                ? element.GetString() ?? string.Empty
                : fallback;

        string N(string key, int fallback) =>
            rule.Params.TryGetValue(key, out var element) && element.TryGetInt32(out var value)
                ? value.ToString(CultureInfo.InvariantCulture)
                : fallback.ToString(CultureInfo.InvariantCulture);

        string B(string key) =>
            rule.Params.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.True
                ? s.T("ValueOn")
                : s.T("ValueOff");

        // Nhãn lựa chọn đã địa phương hóa (thay vì giá trị thô như "ReplaceAll").
        string O(string key) => RuleOptionLabels.Get(P(key), s);

        return rule.Type switch
        {
            "FindReplace" => s.F(
                "RuleSummaryFindReplace",
                P("pattern"),
                P("replacement"),
                B("isRegex") == s.T("ValueOn") ? "(Regex)" : string.Empty),
            "Numbering" => s.F(
                "RuleSummaryNumbering",
                N("start", 1),
                N("step", 1),
                N("width", 3)),
            "Trim" => s.T("RuleSummaryTrim"),
            "Insert" => s.F("RuleSummaryInsert", O("position"), P("text")),
            "Case" => s.F("RuleSummaryCase", O("mode"), O("scope")),
            "Sanitize" => s.T("RuleSummarySanitize"),
            "Date" => s.F("RuleSummaryDate", O("source"), P("format")),
            "Extension" => s.F("RuleSummaryExtension", O("mode")),
            "Template" => s.F("RuleSummaryTemplate", P("template", "{name}")),
            "ListMap" => s.F("RuleSummaryListMap", ListParser.CountRows(P("mapping")), O("mode")),
            "ReplaceDict" => s.F("RuleSummaryReplaceDict", ListParser.CountRows(P("pairs"))),
            _ => string.Empty
        };
    }
}
