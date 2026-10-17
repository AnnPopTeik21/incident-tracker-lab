using System.Net;
using System.Text.Json;

namespace SecureLab.Api.Tests;

// Стан ДО виправлення: тест S-01 фіксує, що контрольний read-only ввід розширює вибірку.
// Запускати лише на локальному стенді зі штучним seed (після --reset-database).
public sealed class SearchInjectionTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private const string SearchUrl = "/api/incidents/search";
    // Один зафіксований контрольний ввід. Інших payload у роботі немає.
    private const string ControlInput = "nothing%' OR 1=1 --";

    private readonly HttpClient _http = factory.CreateClient();

    private async Task<(HttpStatusCode Status, string[] Titles)> SearchAsync(string query)
    {
        using var response = await _http.GetAsync($"{SearchUrl}?q={Uri.EscapeDataString(query)}");
        var body = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return (response.StatusCode, []);
        }

        using var document = JsonDocument.Parse(body);
        var titles = document.RootElement.EnumerateArray()
            .Select(item => item.GetProperty("title").GetString()!)
            .ToArray();
        return (response.StatusCode, titles);
    }

    [Fact]
    public async Task S01_ControlInput_OnVulnerableState_WidensResultToEveryIncident()
    {
        var baseline = await SearchAsync("nothing");
        var control = await SearchAsync(ControlInput);

        // Без дефекту обидва запити мали б повернути порожній список.
        Assert.Equal(HttpStatusCode.OK, baseline.Status);
        Assert.Empty(baseline.Titles);
        Assert.Equal(HttpStatusCode.OK, control.Status);
        Assert.Contains("Підозрілий лист із вкладенням", control.Titles);
        Assert.Contains("Невідома спроба входу", control.Titles);
        Assert.True(control.Titles.Length >= 5, "Контрольний ввід мав повернути всі записи seed.");
    }

    [Fact]
    public async Task NormalSearch_ByWordFromSeed_FindsOnlyMatchingIncident()
    {
        var found = await SearchAsync("лист");

        Assert.Equal(HttpStatusCode.OK, found.Status);
        Assert.Equal(["Підозрілий лист із вкладенням"], found.Titles);
    }
}
