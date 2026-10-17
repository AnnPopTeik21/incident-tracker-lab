using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Tests;

// Стан ПІСЛЯ виправлення: S-02 (той самий контрольний ввід, що й у S-01), T-04, T-05,
// буквальні %, _, ! і \, порядок сортування. Лише локальний стенд і штучний seed.
public sealed class SearchInjectionTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>, IAsyncLifetime
{
    private const string SearchUrl = "/api/incidents/search";
    // Один зафіксований контрольний ввід. Інших payload у роботі немає.
    private const string ControlInput = "nothing%' OR 1=1 --";

    private static readonly string[] SeverityRank = ["Critical", "High", "Medium", "Low"];
    private static readonly string[] StatusRank = ["New", "Triaged", "InProgress", "Resolved", "Closed"];

    private readonly HttpClient _http = factory.CreateClient();
    private readonly List<Guid> _createdIds = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        await db.Incidents.Where(incident => _createdIds.Contains(incident.Id)).ExecuteDeleteAsync();
    }

    private async Task<List<IncidentSearchItemDto>> SearchOkAsync(string query, string extra = "")
    {
        using var response = await _http.GetAsync($"{SearchUrl}?q={Uri.EscapeDataString(query)}{extra}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        return (await response.Content.ReadFromJsonAsync<List<IncidentSearchItemDto>>())!;
    }

    private async Task<Guid> CreateAsync(string title)
    {
        using var response = await _http.PostAsJsonAsync("/api/incidents", new
        {
            title,
            description = "Запис створено тестом пошуку.",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddHours(-1)
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        _createdIds.Add(id);
        return id;
    }

    [Fact]
    public async Task S02_ControlInput_AfterFix_ReturnsSameEmptySetAsPlainNoMatch()
    {
        var baseline = await SearchOkAsync("nothing");
        var control = await SearchOkAsync(ControlInput);

        // Ввід шукається як звичайний текст; запис з таким текстом у seed немає, отже множина порожня.
        Assert.Empty(baseline);
        Assert.Empty(control);
    }

    [Theory]
    [InlineData("лист")]
    [InlineData("ЛИСТ")]
    public async Task NormalSearch_ByWordFromSeed_FindsOnlyMatchingIncident(string word)
    {
        var found = await SearchOkAsync(word);

        Assert.Equal(["Підозрілий лист із вкладенням"], found.Select(item => item.Title));
    }

    [Fact]
    public async Task T04_ApostropheFromSeed_IsOrdinarySearchText()
    {
        // Апостроф саме U+0027, як у seed.
        var found = await SearchOkAsync("комп'ютерного");

        Assert.Equal(["Перевірка журналу комп'ютерного класу"], found.Select(item => item.Title));
    }

    [Fact]
    public async Task T04_ApostropheInCreatedTitle_IsFoundExactly()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var id = await CreateAsync($"Скарга D'Angelo {marker}");

        var found = await SearchOkAsync($"D'Angelo {marker}");

        Assert.Equal([id], found.Select(item => item.Id));
    }

    // Метасимволи LIKE шукаються буквально: у seed їх немає, тому результат порожній.
    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("!")]
    [InlineData("\\")]
    public async Task LikeMetacharacter_OnSeed_MatchesNothing(string symbol)
    {
        Assert.Empty(await SearchOkAsync(symbol));
    }

    [Fact]
    public async Task LikeMetacharacters_InsideTitle_AreFoundAsLiteralText()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var id = await CreateAsync($"Акція {marker} 50%_знижки!\\промо");

        var found = await SearchOkAsync($"{marker} 50%_знижки!\\");
        var near = await SearchOkAsync($"{marker} 50xyзнижки");

        Assert.Equal([id], found.Select(item => item.Id));
        Assert.Empty(near);
    }

    [Fact]
    public async Task SearchItem_KeepsTheSixFieldsOfScaffoldResponse()
    {
        var json = await _http.GetStringAsync($"{SearchUrl}?q={Uri.EscapeDataString("лист")}");
        using var document = JsonDocument.Parse(json);

        var fields = document.RootElement[0].EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal(["id", "title", "description", "severity", "status", "createdAtUtc"], fields);
    }

    [Fact]
    public async Task NoSortBy_MeansNewestCreatedFirst()
    {
        var rows = await SearchOkAsync("");

        Assert.True(rows.Count >= 5);
        Assert.Equal(rows.OrderByDescending(row => row.CreatedAtUtc).Select(row => row.CreatedAtUtc),
            rows.Select(row => row.CreatedAtUtc));
    }

    [Theory]
    [InlineData("severity")]
    [InlineData("SEVERITY")]
    public async Task SortBySeverity_GoesFromCriticalToLow(string key)
    {
        var rows = await SearchOkAsync("", $"&sortBy={key}");
        var ranks = rows.Select(row => Array.IndexOf(SeverityRank, row.Severity)).ToList();

        Assert.DoesNotContain(-1, ranks);
        Assert.Equal(ranks.Order(), ranks);
        Assert.Equal("High", rows[0].Severity);   // у seed немає Critical, тому першим іде High
    }

    [Fact]
    public async Task SortBySeverityDesc_GoesFromLowToCritical()
    {
        var rows = await SearchOkAsync("", "&sortBy=severity&order=desc");
        var ranks = rows.Select(row => Array.IndexOf(SeverityRank, row.Severity)).ToList();

        Assert.Equal(ranks.OrderDescending(), ranks);
    }

    [Fact]
    public async Task SortByStatus_FollowsWorkflowOrder()
    {
        var rows = await SearchOkAsync("", "&sortBy=status");
        var ranks = rows.Select(row => Array.IndexOf(StatusRank, row.Status)).ToList();

        Assert.DoesNotContain(-1, ranks);
        Assert.Equal(ranks.Order(), ranks);
    }

    [Theory]
    [InlineData("sortBy", "title")]
    [InlineData("sortBy", "created_at_utc DESC")]
    [InlineData("sortBy", "severity;")]
    [InlineData("sortBy", "1")]
    [InlineData("order", "sideways")]
    public async Task T05_ValueOutsideAllowlist_Returns400ForThatParameter(string parameter, string value)
    {
        using var response = await _http.GetAsync($"{SearchUrl}?q=a&{parameter}={Uri.EscapeDataString(value)}");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var keys = problem.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name);
        Assert.Equal([parameter], keys);
    }
}
