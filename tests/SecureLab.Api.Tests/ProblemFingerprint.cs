using System.Text.Json;

namespace SecureLab.Api.Tests;

// Стабільна частина Problem Details: type, title, status, detail і їхня наявність.
// traceId сюди не входить, бо він різний у кожного запиту.
internal sealed record ProblemFingerprint(string Stable, bool HasTraceId)
{
    private static readonly string[] StableFields = ["type", "title", "status", "detail"];

    public static async Task<ProblemFingerprint> ReadAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        var parts = StableFields.Select(name =>
            root.TryGetProperty(name, out var value) ? $"{name}={value.GetRawText()}" : $"{name}:absent");
        return new ProblemFingerprint(string.Join("|", parts), root.TryGetProperty("traceId", out _));
    }
}
