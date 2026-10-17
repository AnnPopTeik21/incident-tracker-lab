using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Tests;

// Контракт POST /api/incidents: T-01, T-02, T-03, T-06, T-09 і CP-01.
// Створені тестом записи видаляються після класу, щоб підсумок за severity лишався як у seed.
public sealed class CreateIncidentContractTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>, IAsyncLifetime
{
    private const string CreateUrl = "/api/incidents";
    // Рядки, яких не повинно бути в жодній відповіді з помилкою.
    private static readonly string[] InternalMarkers =
        ["Exception", "   at ", "Npgsql", "SELECT", "Password", "Host=", "SecureLab.Api."];

    // ЛР 3: POST /api/incidents вимагає входу, тому клас працює через cookie-клієнт Аліси.
    private HttpClient _http = null!;
    private readonly List<Guid> _createdIds = [];

    public async Task InitializeAsync() => _http = await factory.LoginAsync("alice", StudySecret.Password);

    public async Task DisposeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        await db.Incidents.Where(incident => _createdIds.Contains(incident.Id)).ExecuteDeleteAsync();
        _http.Dispose();
    }

    private static object ValidBody(string title, string severity = "Medium", string? description = null) => new
    {
        title,
        description = description ?? "Студентка помітила невідомий USB-накопичувач у лабораторії.",
        severity,
        occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-30)
    };

    private async Task<(HttpResponseMessage Response, JsonElement Body)> PostAsync(object body)
    {
        var response = await _http.PostAsJsonAsync(CreateUrl, body);
        var text = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(text).RootElement.Clone();
        if (response.StatusCode == HttpStatusCode.Created)
        {
            _createdIds.Add(json.GetProperty("id").GetGuid());
        }
        return (response, json);
    }

    private static void AssertSafeProblem(HttpResponseMessage response, JsonElement body, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var raw = body.GetRawText();
        foreach (var marker in InternalMarkers)
        {
            Assert.DoesNotContain(marker, raw, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task T01_ValidRequest_Returns201AndOnlyContractFields()
    {
        var title = $"Regression-{Guid.NewGuid():N}";
        var (response, body) = await PostAsync(ValidBody($"  {title}  "));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/api/incidents/{body.GetProperty("id").GetGuid()}", response.Headers.Location?.OriginalString);
        var fields = body.EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal(["id", "title", "severity", "status", "occurredAtUtc", "createdAtUtc"], fields);
        Assert.Equal(title, body.GetProperty("title").GetString());
        Assert.Equal("New", body.GetProperty("status").GetString());
    }

    // Кожен рядок порушує рівно одне правило; ключ помилки має назвати саме це поле.
    [Theory]
    [InlineData("empty-title", "title")]
    [InlineData("spaces-title", "title")]
    [InlineData("long-title", "title")]
    [InlineData("empty-description", "description")]
    [InlineData("severity-7", "severity")]
    [InlineData("severity-word", "severity")]
    [InlineData("severity-digit-2", "severity")]
    [InlineData("severity-flags", "severity")]
    [InlineData("no-occurred", "occurredAtUtc")]
    [InlineData("future-10min", "occurredAtUtc")]
    public async Task T02_BrokenField_Returns400WithThatFieldOnly(string brokenCase, string expectedKey)
    {
        var now = DateTimeOffset.UtcNow;
        var title = $"Invalid-{Guid.NewGuid():N}";
        object body = brokenCase switch
        {
            "empty-title" => new { title = "", description = "Опис є.", severity = "Low", occurredAtUtc = now },
            "spaces-title" => new { title = "    ", description = "Опис є.", severity = "Low", occurredAtUtc = now },
            "long-title" => new { title = new string('Т', 161), description = "Опис є.", severity = "Low", occurredAtUtc = now },
            "empty-description" => new { title, description = "   ", severity = "Low", occurredAtUtc = now },
            "severity-7" => new { title, description = "Опис є.", severity = "7", occurredAtUtc = now },
            "severity-word" => new { title, description = "Опис є.", severity = "Urgent", occurredAtUtc = now },
            "severity-digit-2" => new { title, description = "Опис є.", severity = "2", occurredAtUtc = now },
            "severity-flags" => new { title, description = "Опис є.", severity = "Medium,High", occurredAtUtc = now },
            "no-occurred" => new { title, description = "Опис є.", severity = "Low" },
            "future-10min" => new { title, description = "Опис є.", severity = "Low", occurredAtUtc = now.AddMinutes(10) },
            _ => throw new ArgumentOutOfRangeException(nameof(brokenCase))
        };

        var (response, problem) = await PostAsync(body);

        AssertSafeProblem(response, problem, HttpStatusCode.BadRequest);
        var keys = problem.GetProperty("errors").EnumerateObject().Select(error => error.Name).ToArray();
        Assert.Equal([expectedKey], keys);
    }

    [Fact]
    public async Task T02_BrokenJson_Returns400ProblemNot500()
    {
        using var content = new StringContent("{\"title\":\"обірваний", System.Text.Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(CreateUrl, content);
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

        AssertSafeProblem(response, problem, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task OccurredAt_FourMinutesAhead_IsInsideClockAllowance()
    {
        var (response, _) = await PostAsync(new
        {
            title = $"Clock-{Guid.NewGuid():N}",
            description = "Годинник ноутбука трохи поспішає.",
            severity = "low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(4)
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Theory]
    [InlineData("Невідома спроба входу")]       // seed, статус InProgress
    [InlineData("  Невідома спроба входу  ")]   // той самий title після Trim()
    public async Task T03_TitleOfOpenSeedIncident_Returns409(string title)
    {
        var (response, problem) = await PostAsync(ValidBody(title, "Low"));

        AssertSafeProblem(response, problem, HttpStatusCode.Conflict);
        Assert.Equal(409, problem.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task T03_SameNewTitleTwice_Returns201ThenConflict()
    {
        var title = $"Regression-{Guid.NewGuid():N}";

        var (first, _) = await PostAsync(ValidBody(title));
        var (second, problem) = await PostAsync(ValidBody(title));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        AssertSafeProblem(second, problem, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task T03_TitleInOtherLetterCase_IsNotDuplicate()
    {
        var (response, _) = await PostAsync(ValidBody("невідома спроба входу", "Low"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task T03_ClosedIncident_DoesNotBlockSameTitle()
    {
        var title = $"Closed-{Guid.NewGuid():N}";
        var (first, created) = await PostAsync(ValidBody(title));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // Переведення в Closed не входить до API ЛР 2, тому робимо його напряму в тестовій БД.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
            var id = created.GetProperty("id").GetGuid();
            await db.Incidents.Where(incident => incident.Id == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(incident => incident.Status, IncidentStatus.Closed));
        }

        var (second, _) = await PostAsync(ValidBody(title));
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    // T-09: межа cross-field правила рахується ПІСЛЯ Trim(); пробіли навколо не додають довжини.
    [Theory]
    [InlineData("High", 39, HttpStatusCode.BadRequest)]
    [InlineData("High", 40, HttpStatusCode.Created)]
    [InlineData("Critical", 39, HttpStatusCode.BadRequest)]
    [InlineData("Critical", 40, HttpStatusCode.Created)]
    public async Task T09_SeriousSeverity_NeedsFortyCharacterDescription(
        string severity, int length, HttpStatusCode expected)
    {
        var description = "   " + new string('д', length) + "   ";
        var (response, body) = await PostAsync(ValidBody($"CrossField-{Guid.NewGuid():N}", severity, description));

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.BadRequest)
        {
            AssertSafeProblem(response, body, HttpStatusCode.BadRequest);
            Assert.True(body.GetProperty("errors").TryGetProperty("description", out _));
        }
    }

    [Fact]
    public async Task CP01_ServerManagedFieldsInBody_AreIgnored()
    {
        var title = $"Overposting-{Guid.NewGuid():N}";
        var (response, created) = await PostAsync(new
        {
            title,
            description = "Клієнт спробував сам обрати власника і статус.",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow,
            ownerUserId = DbSeeder.BobId,
            status = "Closed",
            createdAtUtc = "2020-01-01T00:00:00Z"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("New", created.GetProperty("status").GetString());
        var details = await _http.GetFromJsonAsync<JsonElement>($"/api/incidents/{created.GetProperty("id").GetGuid()}");
        Assert.Equal("Аліса Коваль", details.GetProperty("ownerDisplayName").GetString());
        Assert.NotEqual(2020, created.GetProperty("createdAtUtc").GetDateTimeOffset().Year);
    }

    [Fact]
    public async Task T06_UnknownIncidentId_Returns404Problem()
    {
        using var response = await _http.GetAsync("/api/incidents/99999999-9999-9999-9999-999999999999");
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

        AssertSafeProblem(response, problem, HttpStatusCode.NotFound);
        Assert.Equal("Інцидент не знайдено", problem.GetProperty("title").GetString());
    }
}
