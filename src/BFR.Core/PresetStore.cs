namespace BFR.Core;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

/// <summary>
/// Lưu/đọc preset (.bfrpreset) với ghi nguyên tử + tệp .bak (PHẦN 9.1).
/// </summary>
public sealed class PresetStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly IFileSystem _fs;
    private readonly string _directory;

    public PresetStore(IFileSystem fs, string? directory = null)
    {
        _fs = fs;
        _directory = directory ?? Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "BatchFileRenamer", "presets");
    }

    public IReadOnlyList<Preset> LoadAll()
    {
        var list = new List<Preset>();
        foreach (var file in _fs.GetFiles(_directory, "*.bfrpreset", false))
        {
            try
            {
                list.Add(PresetValidator.ReadFile(_fs, file));
            }
            catch (InvalidDataException) { /* bỏ qua preset hỏng hoặc vượt giới hạn */ }
        }
        return list;
    }

    public Preset? Load(string nameOrPath)
    {
        string path;
        if (File.Exists(nameOrPath) || _fs.FileExists(nameOrPath)) path = nameOrPath;
        else path = Path.Combine(_directory, SanitizeFileName(nameOrPath) + ".bfrpreset");

        if (!_fs.FileExists(path)) return null;
        return PresetValidator.ReadFile(_fs, path);
    }

    public void Save(Preset preset)
    {
        _fs.EnsureDirectory(_directory);
        var path = Path.Combine(_directory, SanitizeFileName(preset.Name) + ".bfrpreset");
        WriteAtomic(path, JsonSerializer.Serialize(preset, JsonOptions));
    }

    public void Export(Preset preset, string path) =>
        WriteAtomic(path, JsonSerializer.Serialize(preset, JsonOptions));

    /// <summary>Xuất preset thành chuỗi JSON (để chia sẻ qua bảng nhớ tạm, email, trò chuyện).</summary>
    public string ToJson(Preset preset) => JsonSerializer.Serialize(preset, JsonOptions);

    /// <summary>Nhập preset từ chuỗi JSON (dùng cho chia sẻ).</summary>
    public Preset FromJson(string json) => PresetValidator.Parse(json);

    public Preset Import(string path) => PresetValidator.ReadFile(_fs, path);

    /// <summary>Xoá preset khỏi thư viện cục bộ (trả về true nếu đã xoá).</summary>
    public bool Delete(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        var path = Path.Combine(_directory, SanitizeFileName(name) + ".bfrpreset");
        if (!_fs.FileExists(path)) return false;

        try
        {
            _fs.Delete(path, recursive: false);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void WriteAtomic(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) _fs.EnsureDirectory(dir);
        var temp = path + ".tmp";
        var backup = path + ".bak";
        _fs.WriteAllText(temp, content);
        if (_fs.FileExists(path)) _fs.Move(path, backup, overwrite: true);
        _fs.Move(temp, path, overwrite: true);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
