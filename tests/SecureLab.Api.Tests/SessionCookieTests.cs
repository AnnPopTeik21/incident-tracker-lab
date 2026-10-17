using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SecureLab.Api.Tests;

// Фактичні атрибути cookie сеансу і вихід. Значення cookie тести не читають і не друкують:
// з заголовка беруться лише частини після першої крапки з комою.
public sealed class SessionCookieTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private const string SessionCookie = ".AspNetCore.Identity.Application=";

    private static string[] AttributesOfSessionCookie(HttpResponseMessage response)
    {
        var matching = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Where(value => value.StartsWith(SessionCookie, StringComparison.Ordinal)).ToArray()
            : [];
        // Перевірка лише кількості: при падінні значення cookie у повідомлення не потрапить.
        Assert.True(matching.Length == 1, "Очікувався рівно один Set-Cookie основної cookie.");
        return matching[0].Split(';').Skip(1).Select(part => part.Trim().ToLowerInvariant()).ToArray();
    }

    private HttpClient Client(string scheme) => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri($"{scheme}://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true
    });

    // HTTPS: cookie сеансова (без expires і max-age), HttpOnly, Secure, SameSite=Lax, path=/.
    [Fact]
    public async Task HttpsLogin_IssuesHttpOnlySecureLaxSessionCookie()
    {
        using var client = Client("https");

        using var response = await client.PostAsJsonAsync("/api/auth/login",
            new { userName = "alice", password = StudySecret.Password });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var attributes = AttributesOfSessionCookie(response);
        Assert.Contains("httponly", attributes);
        Assert.Contains("secure", attributes);
        Assert.Contains("samesite=lax", attributes);
        Assert.Contains("path=/", attributes);
        Assert.DoesNotContain(attributes, part => part.StartsWith("expires=", StringComparison.Ordinal));
        Assert.DoesNotContain(attributes, part => part.StartsWith("max-age=", StringComparison.Ordinal));
    }

    // Межа локального HTTP: у Development політика SameAsRequest, тому без HTTPS атрибута secure немає.
    // Це пояснює, чому локальний trace через http://localhost:5080 не доводить Secure.
    [Fact]
    public async Task HttpLogin_InDevelopment_HasNoSecureAttribute()
    {
        using var client = Client("http");

        using var response = await client.PostAsJsonAsync("/api/auth/login",
            new { userName = "bob", password = StudySecret.Password });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var attributes = AttributesOfSessionCookie(response);
        Assert.Contains("httponly", attributes);
        Assert.DoesNotContain("secure", attributes);
    }

    // A-06: вхід, успішний захищений запит, вихід, той самий клієнт знову отримує 401.
    [Fact]
    public async Task Logout_ExpiresSessionCookie_AndSameClientGets401()
    {
        using var alice = await factory.LoginAsync("alice", StudySecret.Password);
        using var before = await alice.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        using var logout = await alice.PostAsync("/api/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        var attributes = AttributesOfSessionCookie(logout);
        Assert.Contains(attributes, part => part.StartsWith("expires=thu, 01 jan 1970", StringComparison.Ordinal));
        using var after = await alice.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
        using var create = await alice.PostAsJsonAsync("/api/incidents", new
        {
            title = $"after-logout-{Guid.NewGuid():N}",
            description = "Спроба після виходу.",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        });
        Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
    }
}
