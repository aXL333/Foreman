using Foreman.McpServer;

namespace Foreman.McpServer.Tests;

public sealed class SseSessionManagerTests
{
    [Theory]
    [InlineData("claude-code", "Claude Code", null, true)]   // title "Claude Code" ⇄ "claude-code"
    [InlineData("claude-code", "claude-code", null, true)]
    [InlineData("claude-code", null, "Claude Code", true)]   // matched via title arg
    [InlineData("codex", "codex", null, true)]
    [InlineData("codex", "Codex CLI", null, true)]           // "codexcli".Contains("codex")
    [InlineData("opencode", "OpenCode", null, true)]
    public void Matches_AnnouncedClientNames(string harnessId, string? name, string? title, bool expected)
        => Assert.Equal(expected, SseSessionManager.MatchesHarness(name, title, harnessId));

    [Theory]
    [InlineData("claude-code", "codex", null)]               // different harness
    [InlineData("codex", "Claude Code", null)]
    [InlineData("claude-code", null, null)]                  // no announced identity
    [InlineData("claude-code", "", "")]
    [InlineData("opencode", "code", null)]                   // generic "code" must NOT match opencode
    [InlineData("t3-code", "code", null)]                    // ...nor t3-code (prefix, not substring)
    public void DoesNotMatch_OtherOrUnknownClients(string harnessId, string? name, string? title)
        => Assert.False(SseSessionManager.MatchesHarness(name, title, harnessId));

    [Fact]
    public void ShortIdsDoNotMatch()  // guard against tiny-token false positives
        => Assert.False(SseSessionManager.MatchesHarness("anything", null, "a"));
}
