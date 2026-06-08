namespace Foreman.Core.Models;

/// <summary>Static metadata about a supported AI coding harness.</summary>
public sealed record KnownHarness(
    string Id,
    string DisplayName,
    string Developer,
    string Description
);

/// <summary>
/// Registry of all harnesses Foreman knows how to detect.
/// IDs must match what HarnessClassifier.Classify() writes into ProcessRecord.HarnessType.
/// </summary>
public static class KnownHarnesses
{
    public static readonly IReadOnlyList<KnownHarness> All =
    [
        new("claude-code",    "Claude Code",            "Anthropic",         "AI coding agent with file editing and bash execution"),
        new("codex",          "Codex CLI",              "OpenAI",            "OpenAI's command-line AI coding agent"),
        new("gemini-cli",     "Gemini CLI",             "Google",            "Google's terminal-based AI coding agent"),
        new("amazon-q",       "Amazon Q Developer",     "Amazon / AWS",      "AWS AI development assistant CLI"),
        new("aider",          "Aider",                  "Paul Gauthier",     "AI pair programming in your terminal"),
        new("github-copilot", "GitHub Copilot CLI",     "GitHub / Microsoft","AI shell integration via 'gh copilot' commands"),
        new("cursor",         "Cursor",                 "Anysphere",         "AI-first code editor (VS Code fork)"),
        new("cline",          "Cline / Continue / Roo", "Community",         "VS Code AI coding extensions (Cline, Continue, Roo Code)"),
    ];

    public static KnownHarness? GetById(string id) =>
        All.FirstOrDefault(h => h.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}
