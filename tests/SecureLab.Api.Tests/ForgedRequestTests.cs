using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SecureLab.Api.Data;

namespace SecureLab.Api.Tests;

// Відмінний рівень: CSRF-модель. Змінювальний запит з cookie сеансу проходить лише з токеном
// цього самого користувача в заголовку X-CSRF-TOKEN. Значення токенів тести не друкують.
public sealed class ForgedRequestTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>, IAsyncLifetime
{
    private const string Header = "X-CSRF-TOKEN";
    private readonly List<Guid> _created = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => IncidentDb.DeleteAsync(factory, _created);

    private static object Incident(string title) => new
    {
        title,
        description = "Перевірка CSRF-токена.",
        severity = "Low",
        occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-3)
    };

    private static async Task<HttpResponseMessage> PostWithTokenAsync(HttpClient client, string path, object? body, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (token is not null) request.Headers.Add(Header, token);
        return await client.SendAsync(request);
    }

    private static async Task AssertForgeryProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("CSRF", problem.GetProperty("title").GetString());
    }

    // Видача токена: назва заголовка, токен у тілі, службова cookie HttpOnly і SameSite=Strict,
    // відповідь не кешується.
    [Fact]
    public async Task TokenEndpoint_GivesHeaderNameAndStrictHttpOnlyCookie()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });

        using var response = await client.GetAsync("/api/auth/csrf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(Header, body.GetProperty("headerName").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("token").GetString()));
        Assert.Equal(["headerName", "token"], body.EnumerateObject().Select(property => property.Name));
        var cookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("SecureLab.Csrf=", StringComparison.Ordinal));
        var attributes = cookie.Split(';').Skip(1).Select(part => part.Trim().ToLowerInvariant()).ToArray();
        Assert.Contains("httponly", attributes);
        Assert.Contains("samesite=strict", attributes);
        Assert.Contains("secure", attributes);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? "");
    }

    // Cookie Аліси є, токена немає: так виглядає запит, який браузер надсилає з чужої сторінки.
    [Fact]
    public async Task CreateWithCookieButWithoutToken_Gets400AndAddsNothing()
    {
        using var alice = await factory.LoginAsync("alice", StudySecret.Password, attachCsrf: false);
        var title = $"csrf-none-{Guid.NewGuid():N}";

        using var response = await PostWithTokenAsync(alice, "/api/incidents", Incident(title), token: null);

        await AssertForgeryProblemAsync(response);
        Assert.Equal(0, await IncidentDb.CountByTitleAsync(factory, title));
    }

    // Токен Боба в запиті з cookie Аліси не підходить: токен належить іншому користувачу.
    [Fact]
    public async Task CreateWithAnotherUsersToken_Gets400()
    {
        using var bob = await factory.LoginAsync("bob", StudySecret.Password, attachCsrf: false);
        var bobsToken = await bob.GetCsrfTokenAsync();
        using var alice = await factory.LoginAsync("alice", StudySecret.Password, attachCsrf: false);
        await alice.GetCsrfTokenAsync();
        var title = $"csrf-foreign-{Guid.NewGuid():N}";

        using var response = await PostWithTokenAsync(alice, "/api/incidents", Incident(title), bobsToken);

        await AssertForgeryProblemAsync(response);
        Assert.Equal(0, await IncidentDb.CountByTitleAsync(factory, title));
    }

    // Токен, виданий до входу, після входу недійсний. Тому клієнт бере новий токен після входу.
    [Fact]
    public async Task TokenIssuedBeforeLogin_IsRejectedAfterLogin_FreshTokenWorks()
    {
        using var client = TestHosts.HttpsClient(factory);
        var anonymousToken = await client.GetCsrfTokenAsync();
        using var login = await TestHosts.LoginAsync(client, "alice", StudySecret.Password);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

        using var stale = await PostWithTokenAsync(client, "/api/incidents", Incident($"csrf-stale-{Guid.NewGuid():N}"), anonymousToken);
        await AssertForgeryProblemAsync(stale);

        var freshToken = await client.GetCsrfTokenAsync();
        using var accepted = await PostWithTokenAsync(client, "/api/incidents", Incident($"csrf-fresh-{Guid.NewGuid():N}"), freshToken);
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        var created = await accepted.Content.ReadFromJsonAsync<JsonElement>();
        _created.Add(created.GetProperty("id").GetGuid());
        Assert.Equal(DbSeeder.AliceId, await IncidentDb.OwnerOfAsync(factory, created.GetProperty("id").GetGuid()));
    }

    // Logout теж змінює стан. Без токена сеанс лишається, з токеном завершується.
    [Fact]
    public async Task LogoutWithoutToken_KeepsSession_WithTokenEndsIt()
    {
        using var alice = await factory.LoginAsync("alice", StudySecret.Password, attachCsrf: false);

        using var forged = await PostWithTokenAsync(alice, "/api/auth/logout", body: null, token: null);
        await AssertForgeryProblemAsync(forged);
        using var stillIn = await alice.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, stillIn.StatusCode);

        var token = await alice.GetCsrfTokenAsync();
        using var logout = await PostWithTokenAsync(alice, "/api/auth/logout", body: null, token);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var after = await alice.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    // Анонімний POST на захищений endpoint і далі зупиняє автентифікація: 401, а не 400.
    [Fact]
    public async Task AnonymousCreate_StillGets401BeforeTokenCheck()
    {
        using var anonymous = TestHosts.HttpsClient(factory);

        using var response = await PostWithTokenAsync(anonymous, "/api/incidents", Incident($"csrf-anon-{Guid.NewGuid():N}"), token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
