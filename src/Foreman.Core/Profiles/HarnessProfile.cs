using System.Text.Json.Serialization;

namespace Foreman.Core.Profiles;

public sealed class HarnessProfile
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("processMatch")]
    public ProcessMatchConfig ProcessMatch { get; set; } = new();

    [JsonPropertyName("fileSystem")]
    public FileSystemConfig FileSystem { get; set; } = new();

    [JsonPropertyName("commands")]
    public CommandConfig Commands { get; set; } = new();

    [JsonPropertyName("processLimits")]
    public ProcessLimitsConfig ProcessLimits { get; set; } = new();

    [JsonPropertyName("claudeCodeIntegration")]
    public ClaudeCodeIntegrationConfig ClaudeCodeIntegration { get; set; } = new();

    [JsonPropertyName("alerts")]
    public AlertConfig Alerts { get; set; } = new();
}

public sealed class ProcessMatchConfig
{
    [JsonPropertyName("executableNames")]
    public string[] ExecutableNames { get; set; } = [];

    [JsonPropertyName("commandLineContains")]
    public string[] CommandLineContains { get; set; } = [];

    [JsonPropertyName("parentExecutableNames")]
    public string[] ParentExecutableNames { get; set; } = [];
}

public sealed class FileSystemConfig
{
    [JsonPropertyName("allowedReadPaths")]
    public string[] AllowedReadPaths { get; set; } = [];

    [JsonPropertyName("allowedWritePaths")]
    public string[] AllowedWritePaths { get; set; } = [];

    [JsonPropertyName("deniedPaths")]
    public string[] DeniedPaths { get; set; } = [];

    /// <summary>monitor | alert | block</summary>
    [JsonPropertyName("enforceMode")]
    public string EnforceMode { get; set; } = "alert";
}

public sealed class CommandConfig
{
    [JsonPropertyName("blockedPatterns")]
    public string[] BlockedPatterns { get; set; } = [];

    [JsonPropertyName("enforceMode")]
    public string EnforceMode { get; set; } = "alert";
}

public sealed class ProcessLimitsConfig
{
    [JsonPropertyName("maxRuntimeMinutes")]
    public int MaxRuntimeMinutes { get; set; } = 480;

    [JsonPropertyName("hangThresholdMinutes")]
    public int HangThresholdMinutes { get; set; } = 10;

    [JsonPropertyName("hookJamThresholdMinutes")]
    public int HookJamThresholdMinutes { get; set; } = 5;
}

public sealed class ClaudeCodeIntegrationConfig
{
    [JsonPropertyName("readSettingsJson")]
    public bool ReadSettingsJson { get; set; } = true;

    [JsonPropertyName("trustGrantedPermissions")]
    public bool TrustGrantedPermissions { get; set; } = true;
}

public sealed class AlertConfig
{
    [JsonPropertyName("notifyOnHang")]
    public bool NotifyOnHang { get; set; } = true;

    [JsonPropertyName("notifyOnOrphan")]
    public bool NotifyOnOrphan { get; set; } = true;

    [JsonPropertyName("notifyOnViolation")]
    public bool NotifyOnViolation { get; set; } = true;

    [JsonPropertyName("suppressDuplicatesWindowMinutes")]
    public int SuppressDuplicatesWindowMinutes { get; set; } = 5;

    [JsonPropertyName("trustedHookPathMarkers")]
    public string[] TrustedHookPathMarkers { get; set; } = [];

    [JsonPropertyName("launcherSuppressedRuleIds")]
    public string[] LauncherSuppressedRuleIds { get; set; } = [];
}
