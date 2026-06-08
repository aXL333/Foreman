using Foreman.Core.Models;

namespace Foreman.Core.Heuristics;

public sealed record RuleMatch(
    string RuleId,
    string RuleName,
    string Description,
    string Guidance,
    ForemanSeverity Severity,
    string Category,
    string MatchedText
);
