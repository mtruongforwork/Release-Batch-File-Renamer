namespace BFR.Core;

/// <summary>
/// Biên dịch Preset thành RulePipeline để dùng chung cho UI và CLI.
/// </summary>
public sealed class PresetCompiler
{
    public RulePipeline Compile(Preset preset, int maxListRows = int.MaxValue)
    {
        var pipeline = new RulePipeline();
        foreach (var def in preset.Rules)
        {
            if (!def.Enabled) continue;
            pipeline.Add(RuleFactory.Create(def, preset.Options.AllowExtensionChange, maxListRows));
        }
        return pipeline;
    }
}
