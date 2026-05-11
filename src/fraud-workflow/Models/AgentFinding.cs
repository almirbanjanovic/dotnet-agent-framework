using System.Text.Json;

namespace Contoso.FraudWorkflow.Models;

// Output of one specialist agent. Each of OrderHistoryAgent,
// ReturnConditionAgent, LoyaltyContextAgent emits one of these.
//
// The agents are LLM-backed and asked to produce JSON with this shape;
// `Parse` is forgiving and falls back to `RiskScore = 0.5` (manual review)
// if the model's reply isn't valid JSON.

internal sealed record AgentFinding(
    string AgentName,
    double RiskScore,
    string Findings,
    IReadOnlyList<string> Evidence)
{
    public static AgentFinding Parse(string agentName, string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new AgentFinding(agentName, 0.5, "(no response)", []);
        }

        try
        {
            // Models often wrap JSON in ```json fences — strip them before parsing.
            var cleaned = StripFences(raw);
            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement;

            var score = root.TryGetProperty("riskScore", out var s) && s.ValueKind == JsonValueKind.Number
                ? s.GetDouble()
                : 0.5;
            score = Math.Clamp(score, 0.0, 1.0);

            var findings = root.TryGetProperty("findings", out var f) && f.ValueKind == JsonValueKind.String
                ? f.GetString() ?? ""
                : "";

            var evidence = new List<string>();
            if (root.TryGetProperty("evidence", out var ev) && ev.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in ev.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var value = item.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            evidence.Add(value);
                        }
                    }
                }
            }

            return new AgentFinding(agentName, score, findings, evidence);
        }
        catch (JsonException)
        {
            // Free-text fallback — surface the raw answer to the operator
            // rather than dropping it on the floor.
            return new AgentFinding(agentName, 0.5, raw.Trim(), []);
        }
    }

    private static string StripFences(string s)
    {
        var trimmed = s.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline > 0)
            {
                trimmed = trimmed[(firstNewline + 1)..];
            }
            if (trimmed.EndsWith("```", StringComparison.Ordinal))
            {
                trimmed = trimmed[..^3];
            }
        }
        return trimmed.Trim();
    }
}
