namespace BFR.Core;

using System;
using System.IO;
using System.Text.Json;

/// <summary>
/// Đọc preset từ nguồn ngoài (tệp nhập, bảng nhớ tạm, thư mục preset) với giới hạn kích thước,
/// độ sâu JSON và độ dài chuỗi. Mọi vi phạm đều ném <see cref="InvalidDataException"/> (không bao giờ OutOfMemory/StackOverflow).
/// </summary>
public static class PresetValidator
{
    public const int MaxFileBytes = 1 * 1024 * 1024;
    public const int MaxJsonDepth = 32;
    public const int MaxRules = 200;
    public const int MaxNameLength = 200;
    public const int MaxDescriptionLength = 2000;
    public const int MaxParamsPerRule = 64;
    public const int MaxParamStringLength = 100_000;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = MaxJsonDepth
    };

    /// <summary>Đọc tệp preset, từ chối tệp quá lớn trước khi nạp vào bộ nhớ.</summary>
    public static Preset ReadFile(IFileSystem fs, string path)
    {
        long length;
        try { length = fs.GetLength(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            throw new InvalidDataException("Không đọc được tệp preset.", ex);
        }

        if (length > MaxFileBytes) throw new InvalidDataException("Tệp preset quá lớn.");
        return Parse(fs.ReadAllText(path));
    }

    public static Preset Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("Preset trống.");
        if (json.Length > MaxFileBytes) throw new InvalidDataException("Preset quá lớn.");

        Preset? preset;
        try
        {
            preset = JsonSerializer.Deserialize<Preset>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Preset không hợp lệ.", ex);
        }

        if (preset is null) throw new InvalidDataException("Preset không hợp lệ.");
        Validate(preset);
        return preset;
    }

    public static void Validate(Preset preset)
    {
        if (string.IsNullOrWhiteSpace(preset.Name) || preset.Name.Length > MaxNameLength || HasControl(preset.Name))
            throw new InvalidDataException("Tên preset không hợp lệ.");

        if (preset.Description is { Length: > MaxDescriptionLength })
            throw new InvalidDataException("Mô tả preset quá dài.");

        if (preset.Rules is null || preset.Rules.Count > MaxRules)
            throw new InvalidDataException("Preset có quá nhiều quy tắc.");

        foreach (var rule in preset.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Type) || rule.Type.Length > 64 || HasControl(rule.Type))
                throw new InvalidDataException("Loại quy tắc không hợp lệ.");

            if (rule.Name is { Length: > MaxNameLength })
                throw new InvalidDataException("Tên quy tắc quá dài.");

            if (rule.Params is null) continue;
            if (rule.Params.Count > MaxParamsPerRule)
                throw new InvalidDataException("Quy tắc có quá nhiều tham số.");

            foreach (var (_, value) in rule.Params)
            {
                if (value.ValueKind == JsonValueKind.String && (value.GetString()?.Length ?? 0) > MaxParamStringLength)
                    throw new InvalidDataException("Tham số quy tắc quá dài.");
            }
        }
    }

    private static bool HasControl(string s)
    {
        foreach (var c in s)
        {
            if (char.IsControl(c)) return true;
        }
        return false;
    }
}
