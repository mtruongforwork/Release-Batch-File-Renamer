namespace BFR.Core;

using System;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// Thư viện preset dựng sẵn (PHẦN 9.3).
/// </summary>
public static class BuiltInPresets
{
    private static JsonElement E(object? value) => JsonSerializer.SerializeToElement(value);

    // Tên preset dựng sẵn là khoá ổn định (lưu trong tệp, dùng ở --preset); chỉ phần hiển thị được dịch.
    private static readonly Dictionary<string, string> DisplayKeys = new(StringComparer.Ordinal)
    {
        ["Dọn dẹp tên tệp"] = "PresetBuiltinClean",
        ["Bỏ dấu tiếng Việt + gạch nối"] = "PresetBuiltinAscii",
        ["Đánh số theo thứ tự"] = "PresetBuiltinNumber",
        ["Ảnh theo ngày chụp"] = "PresetBuiltinDate",
        ["Chữ thường toàn bộ"] = "PresetBuiltinLower",
        ["Thêm tiền tố thư mục cha"] = "PresetBuiltinParent",
        ["Chuẩn hóa Unicode (NFC)"] = "PresetBuiltinNfc"
    };

    /// <summary>Tên hiển thị theo ngôn ngữ giao diện; preset của người dùng giữ nguyên tên.</summary>
    public static string DisplayName(string name) =>
        DisplayKeys.TryGetValue(name, out var key) ? CoreText.TryGet(key + "Name") ?? name : name;

    /// <summary>Mô tả hiển thị theo ngôn ngữ giao diện, hoặc mô tả gốc nếu chưa có bản dịch.</summary>
    public static string DisplayDescription(string name, string description) =>
        DisplayKeys.TryGetValue(name, out var key) ? CoreText.TryGet(key + "Desc") ?? description : description;

    public static IReadOnlyList<Preset> All { get; } = new List<Preset>
    {
        new()
        {
            Name = "Dọn dẹp tên tệp",
            Description = "Bỏ (1), - Copy, gộp khoảng trắng, cắt hai đầu",
            Rules = new List<RuleDefinition>
            {
                new() { Type = "FindReplace", Params = new() { ["pattern"] = E("(1)"), ["replacement"] = E(""), ["isRegex"] = E(false), ["scope"] = E("Name") } },
                new() { Type = "FindReplace", Params = new() { ["pattern"] = E("- Copy"), ["replacement"] = E(""), ["isRegex"] = E(false), ["scope"] = E("Name") } },
                new() { Type = "Trim", Params = new() { ["removeChars"] = E("()"), ["collapseSpaces"] = E(true), ["trimEnds"] = E(true), ["removeTrailingDotSpace"] = E(true) } }
            }
        },
        new()
        {
            Name = "Bỏ dấu tiếng Việt + gạch nối",
            Description = "Báo cáo tháng 5.docx thành bao-cao-thang-5.docx",
            Rules = new List<RuleDefinition>
            {
                new() { Type = "Sanitize", Params = new() { ["removeDiacritics"] = E(true), ["normalizeNfc"] = E(true), ["slugify"] = E(true) } }
            }
        },
        new()
        {
            Name = "Đánh số theo thứ tự",
            Description = "001, 002, ... theo natural sort",
            Rules = new List<RuleDefinition>
            {
                new() { Type = "Numbering", Params = new() { ["start"] = E(1), ["step"] = E(1), ["width"] = E(3), ["position"] = E("ReplaceAll"), ["kind"] = E("Decimal") } }
            }
        },
        new()
        {
            Name = "Ảnh theo ngày chụp",
            Description = "YYYY-MM-DD_001.jpg từ EXIF, dự phòng ngày tạo",
            Rules = new List<RuleDefinition>
            {
                new() { Type = "Date", Params = new() { ["source"] = E("Taken"), ["format"] = E("yyyy-MM-dd"), ["position"] = E("Start"), ["separator"] = E("_") } },
                new() { Type = "Numbering", Params = new() { ["start"] = E(1), ["step"] = E(1), ["width"] = E(3), ["position"] = E("End"), ["separator"] = E("_") } }
            },
            DateSourceFallback = new() { "Taken", "Created", "Modified" }
        },
        new()
        {
            Name = "Chữ thường toàn bộ",
            Description = "Chuyển tên và đuôi sang chữ thường",
            Rules = new List<RuleDefinition>
            {
                new() { Type = "Case", Params = new() { ["mode"] = E("Lower"), ["scope"] = E("Both") } }
            }
        },
        new()
        {
            Name = "Thêm tiền tố thư mục cha",
            Description = "{parent}_{name}",
            Rules = new List<RuleDefinition>
            {
                new() { Type = "Template", Params = new() { ["template"] = E("{parent}_{name}") } }
            }
        },
        new()
        {
            Name = "Chuẩn hóa Unicode (NFC)",
            Description = "Sửa tên tệp dấu rời (NFD)",
            Rules = new List<RuleDefinition>
            {
                new() { Type = "Sanitize", Params = new() { ["normalizeNfc"] = E(true) } }
            }
        }
    };
}
