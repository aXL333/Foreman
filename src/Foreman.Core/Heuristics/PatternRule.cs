using Foreman.Core.Models;
using System.Text.Json.Serialization;

namespace Foreman.Core.Heuristics;

public sealed class PatternRule
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "medium";

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("pattern")]
    public string Pattern { get; set; } = string.Empty;

    [JsonPropertyName("platforms")]
    public string[] Platforms { get; set; } = [];

    [JsonPropertyName("guidance")]
    public string Guidance { get; set; } = string.Empty;

    [JsonPropertyName("falsePositiveTags")]
    public string[] FalsePositiveTags { get; set; } = [];

    public ForemanSeverity ParsedSeverity => Severity.ToLowerInvariant() switch
    {
        "critical" => ForemanSeverity.Critical,
        "high"     => ForemanSeverity.High,
        "medium"   => ForemanSeverity.Medium,
        "low"      => ForemanSeverity.Low,
        _          => ForemanSeverity.Info,
    };
}

public sealed class PatternFile
{
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("rules")]
    public PatternRule[] Rules { get; set; } = [];
}
