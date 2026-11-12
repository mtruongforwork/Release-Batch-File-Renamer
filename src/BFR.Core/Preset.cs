namespace BFR.Core;

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Mô hình preset (.bfrpreset) — xem docs/specs/08-mo-hinh-du-lieu-preset-cli.md.
/// </summary>
public sealed class Preset
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "1.0.0";

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>Tên để hiển thị: preset dựng sẵn được dịch theo ngôn ngữ giao diện.</summary>
    [JsonIgnore]
    public string DisplayName => BuiltInPresets.DisplayName(Name);

    [JsonIgnore]
    public string DisplayDescription => BuiltInPresets.DisplayDescription(Name, Description);

    [JsonPropertyName("options")]
    public PresetOptions Options { get; set; } = new();

    [JsonPropertyName("rules")]
    public List<RuleDefinition> Rules { get; set; } = new();

    [JsonPropertyName("dateSourceFallback")]
    public List<string> DateSourceFallback { get; set; } = new() { "Taken", "Created", "Modified" };
}

public sealed class PresetOptions
{
    [JsonPropertyName("recursive")]
    public bool Recursive { get; set; }

    [JsonPropertyName("includeFolders")]
    public bool IncludeFolders { get; set; }

    [JsonPropertyName("allowExtensionChange")]
    public bool AllowExtensionChange { get; set; }

    [JsonPropertyName("sortBy")]
    public string SortBy { get; set; } = "Name.Natural";
}

public sealed class RuleDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("params")]
    public Dictionary<string, JsonElement> Params { get; set; } = new();

    [JsonIgnore]
    public string Display => string.IsNullOrEmpty(Name) ? Type : $"{Name} ({Type})";
}
