using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SecureLab.Api.Data;

namespace SecureLab.Api.Tests;

// Межа володіння для GET /api/incidents/{id}: власник читає, інший Reporter отримує 403,
// Analyst читає для аналізу (вхідний контракт ЛР 5), неіснуючий id дає 404, анонім 401.
public sealed class OwnershipBoundaryTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private static string Details(Guid id) => $"/api/incidents/{id}";

    [Fact]
    public async Task Owner_ReadsOwnDetails()
    {
        using var alice = await factory.LoginAsync("alice", StudySecret.Password);

        using var response = await alice.GetAsync(Details(DbSeeder.AliceIncidentId));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(DbSeeder.AliceIncidentId, body.GetProperty("id").GetGuid());
        Assert.Equal("Аліса Коваль", body.GetProperty("ownerDisplayName").GetString());
    }

    [Fact]
    public async Task OtherReporter_GetsForbidden_AndNoForeignFieldsInBody()
    {
        using var alice = await factory.LoginAsync("alice", StudySecret.Password);

        using var response = await alice.GetAsync(Details(DbSeeder.BobIncidentId));
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Доступ до інциденту заборонено", text);
        Assert.DoesNotContain("Невідома спроба входу", text);
        Assert.DoesNotContain("Боб Мельник", text);
        Assert.DoesNotContain("нового пристрою", text);
    }

    // Політика 403/404: неіснуючий id не плутається з чужим. A-10 за цієї політики не застосовується.
    [Fact]
    public async Task UnknownId_GetsNotFound_WhichDiffersFromForeignRefusal()
    {
        using var alice = await factory.LoginAsync("alice", StudySecret.Password);

        using var unknown = await alice.GetAsync(Details(Guid.NewGuid()));
        using var foreign = await alice.GetAsync(Details(DbSeeder.BobIncidentId));

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.Equal("application/problem+json", unknown.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Analyst_ReadsReporterIncident_ForAnalysis()
    {
        using var morgan = await factory.LoginAsync("morgan", StudySecret.Password);

        using var response = await morgan.GetAsync(Details(DbSeeder.AliceIncidentId));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Аліса Коваль", body.GetProperty("ownerDisplayName").GetString());
    }

    // Least privilege: роль Administrator сама по собі не відкриває чужі інциденти.
    [Fact]
    public async Task Administrator_WithoutOwnership_GetsForbidden()
    {
        using var admin = await factory.LoginAsync("admin", StudySecret.Password);

        using var response = await admin.GetAsync(Details(DbSeeder.BobIncidentId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    public static TheoryData<string, string> ProtectedRoutes => new()
    {
        { "GET", "/api/incidents" },
        { "GET", "/api/incidents/20000000-0000-0000-0000-000000000001" },
        { "GET", "/api/incidents/search?q=лист" },
        { "GET", "/api/incidents/severity-summary" },
        { "GET", "/api/incident-comments/30000000-0000-0000-0000-000000000001" },
        { "PATCH", "/api/incidents/20000000-0000-0000-0000-000000000001" },
        { "DELETE", "/api/incidents/20000000-0000-0000-0000-000000000003" },
        { "POST", "/api/incidents/20000000-0000-0000-0000-000000000003/triage" }
    };

    [Theory]
    [MemberData(nameof(ProtectedRoutes))]
    public async Task Anonymous_Gets401Problem_NotRedirect(string method, string url)
    {
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method is "PATCH" or "POST")
        {
            request.Content = JsonContent.Create(new { title = "anonymous" });
        }

        using var response = await anonymous.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }
}
