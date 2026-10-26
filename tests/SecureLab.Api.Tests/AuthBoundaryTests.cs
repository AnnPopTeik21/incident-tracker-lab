using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SecureLab.Api.Data;

namespace SecureLab.Api.Tests;

// Межа автентифікації: хто без cookie, той 401; профіль бере лише principal із cookie;
// невдалий вхід виглядає однаково для невідомого імені й хибного пароля.
public sealed class AuthBoundaryTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    // Назви полів, яких не повинно бути в профілі (порівняння без урахування регістру).
    private static readonly string[] ForbiddenProfileFields =
        ["password", "passwordHash", "securityStamp", "concurrencyStamp", "cookie", "token", "email"];

    private HttpClient NewAnonymousClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });

    // A-01: захищений профіль без cookie дає 401 Problem Details, без redirect і без Bearer.
    [Fact]
    public async Task ProfileWithoutCookie_Gets401ProblemInsteadOfRedirect()
    {
        using var anonymous = NewAnonymousClient();

        using var response = await anonymous.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Empty(response.Headers.WwwAuthenticate);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    // A-01 для операції зі зміною даних: анонімний POST не доходить до створення запису.
    [Fact]
    public async Task CreateWithoutCookie_Gets401AndAddsNoIncident()
    {
        using var anonymous = NewAnonymousClient();
        var title = $"anon-{Guid.NewGuid():N}";

        using var response = await anonymous.PostAsJsonAsync("/api/incidents", new
        {
            title,
            description = "Спроба створити запис без входу.",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal(0, await IncidentDb.CountByTitleAsync(factory, title));
    }

    // A-02: після входу профіль належить саме Алісі й містить лише три дозволені поля.
    [Fact]
    public async Task AfterLogin_ProfileComesFromAliceCookie()
    {
        using var alice = await factory.LoginAsync("alice", StudySecret.Password);

        using var response = await alice.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var profile = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(DbSeeder.AliceId, profile.GetProperty("userId").GetGuid());
        Assert.Equal("alice", profile.GetProperty("userName").GetString());
        Assert.Equal([DbSeeder.ReporterRole], profile.GetProperty("roles").EnumerateArray().Select(role => role.GetString()));
        var names = profile.EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal(3, names.Length);
        Assert.DoesNotContain(names, name => ForbiddenProfileFields.Contains(name, StringComparer.OrdinalIgnoreCase));
    }

    // A-03: та сама чинна cookie Аліси і заголовок з id Боба. Профіль лишається Алісиним.
    [Fact]
    public async Task ForeignDemoHeader_WithAliceCookie_DoesNotSwitchProfile()
    {
        using var alice = await factory.LoginAsync("alice", StudySecret.Password);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Add("X-Demo-UserId", DbSeeder.BobId.ToString());

        using var response = await alice.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(DbSeeder.AliceId, profile.GetProperty("userId").GetGuid());
        Assert.Equal("alice", profile.GetProperty("userName").GetString());
    }

    // Колишній дефект scaffold: заголовок без cookie більше не відкриває чужий профіль.
    [Fact]
    public async Task DemoHeaderWithoutCookie_StillGets401()
    {
        using var anonymous = NewAnonymousClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Add("X-Demo-UserId", DbSeeder.BobId.ToString());

        using var response = await anonymous.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // A-07: невідоме ім'я і хибний пароль Аліси дають однаковий status, тип вмісту і поля Problem Details.
    [Fact]
    public async Task UnknownUserAndWrongPassword_ProduceIdenticalRefusal()
    {
        using var first = NewAnonymousClient();
        using var second = NewAnonymousClient();
        // Інший пароль формую локально й ніде не друкую.
        var wrongPassword = "Wrong-" + Guid.NewGuid().ToString("N")[..8] + "Q1";

        using var unknownUser = await first.PostAsJsonAsync("/api/auth/login",
            new { userName = $"ghost{Guid.NewGuid():N}"[..14], password = StudySecret.Password });
        using var wrongSecret = await second.PostAsJsonAsync("/api/auth/login",
            new { userName = "alice", password = wrongPassword });

        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);
        Assert.Equal(unknownUser.StatusCode, wrongSecret.StatusCode);
        Assert.Equal(unknownUser.Content.Headers.ContentType?.MediaType, wrongSecret.Content.Headers.ContentType?.MediaType);
        Assert.False(unknownUser.Headers.Contains("Set-Cookie"));
        Assert.False(wrongSecret.Headers.Contains("Set-Cookie"));
        var a = await ProblemFingerprint.ReadAsync(unknownUser);
        var b = await ProblemFingerprint.ReadAsync(wrongSecret);
        Assert.Equal(a.Stable, b.Stable);
        // traceId діагностичний: він є в обох відповідях, але рівним бути не мусить.
        Assert.True(a.HasTraceId && b.HasTraceId);
    }

    // Події входу і виходу: результат, userId після успіху і trace id. Ні імені з форми,
    // ні пароля, ні cookie, ні hash немає в жодному рядку журналу host.
    [Fact]
    public async Task AuthEvents_CarryOutcomeAndTrace_ButNoSecrets()
    {
        var sink = new MemoryLogSink();
        using var host = TestHosts.Fresh(factory, logSink: sink);
        using var client = TestHosts.HttpsClient(host);
        var wrongPassword = "Nope-" + Guid.NewGuid().ToString("N")[..8] + "K3";

        using var refused = await TestHosts.LoginAsync(client, "alice", wrongPassword);
        using var accepted = await TestHosts.LoginAsync(client, "alice", StudySecret.Password);
        await client.AttachCsrfTokenAsync();
        using var logout = await client.PostAsync("/api/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        var refusedTrace = (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("traceId").GetString();
        var audit = sink.Entries.Where(entry => entry.Category == "SecureLab.AuthAudit").Select(entry => entry.Message).ToArray();
        Assert.Contains(audit, line => line.StartsWith("auth.login result=rejected", StringComparison.Ordinal)
            && line.EndsWith($"trace={refusedTrace}", StringComparison.Ordinal));
        Assert.Contains(audit, line => line.StartsWith($"auth.login result=success userId={DbSeeder.AliceId}", StringComparison.Ordinal));
        Assert.Contains(audit, line => line.StartsWith($"auth.logout result=done userId={DbSeeder.AliceId}", StringComparison.Ordinal));
        Assert.DoesNotContain(audit, line => line.Contains("alice", StringComparison.OrdinalIgnoreCase));
        foreach (var (_, message) in sink.Entries)
        {
            Assert.False(message.Contains(StudySecret.Password, StringComparison.Ordinal), "Пароль потрапив у журнал.");
            Assert.False(message.Contains(wrongPassword, StringComparison.Ordinal), "Пароль потрапив у журнал.");
            Assert.False(message.Contains(".AspNetCore.Identity", StringComparison.Ordinal), "Cookie потрапила в журнал.");
            Assert.False(message.Contains("AQAAAA", StringComparison.Ordinal), "Hash потрапив у журнал.");
        }
    }
}
