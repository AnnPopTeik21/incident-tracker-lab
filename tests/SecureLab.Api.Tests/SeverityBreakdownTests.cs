using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Tests;

// Тести спираються на відомий seed: Low, Medium і High по одному інциденту, Critical немає.
// Перед запуском після ручних експериментів потрібен --reset-database.
public sealed class SeverityBreakdownTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private const string BreakdownUrl = "/api/incidents/severity-summary";
    private readonly HttpClient _http = factory.CreateClient();

    [Fact]
    public async Task Seed_GivesAllFourLevels_FromLowToCritical_WithZeroForCritical()
    {
        using var response = await _http.GetAsync(BreakdownUrl);
        var levels = await response.Content.ReadFromJsonAsync<List<SeverityCountDto>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(levels);
        Assert.Equal(["Low", "Medium", "High", "Critical"], levels.Select(level => level.Severity));
        Assert.Equal([1, 1, 1, 0], levels.Select(level => level.Count));
    }

    [Fact]
    public async Task EachItem_HasOnlySeverityAndCount()
    {
        var json = await _http.GetStringAsync(BreakdownUrl);
        using var document = JsonDocument.Parse(json);

        foreach (var item in document.RootElement.EnumerateArray())
        {
            var names = item.EnumerateObject().Select(property => property.Name).ToArray();
            Assert.Equal(["severity", "count"], names);
        }
    }

    [Fact]
    public async Task TriagedFilter_CountsOnlyTheMediumIncident()
    {
        var levels = await _http.GetFromJsonAsync<List<SeverityCountDto>>($"{BreakdownUrl}?status=Triaged");

        Assert.NotNull(levels);
        Assert.Equal(4, levels.Count);
        Assert.Equal(1, levels.Single(level => level.Severity == "Medium").Count);
        Assert.Equal(1, levels.Sum(level => level.Count));
    }

    [Fact]
    public async Task ResolvedFilter_OnSeed_ReturnsFourZeros()
    {
        var levels = await _http.GetFromJsonAsync<List<SeverityCountDto>>($"{BreakdownUrl}?status=Resolved");

        Assert.NotNull(levels);
        Assert.Equal(4, levels.Count);
        Assert.All(levels, level => Assert.Equal(0, level.Count));
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("triaged")]
    [InlineData("3")]
    [InlineData("New,Triaged")]
    public async Task StatusOutsideAllowlist_Returns400ValidationProblem(string status)
    {
        using var response = await _http.GetAsync($"{BreakdownUrl}?status={Uri.EscapeDataString(status)}");
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("status", out _));
    }
}
