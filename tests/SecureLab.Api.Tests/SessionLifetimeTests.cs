using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace SecureLab.Api.Tests;

// Відмінний рівень: строк дії сеансу. Cookie сеансу без expires, але квиток усередині має
// час завершення. Обробник cookie порівнює його з годинником сервера, тому тест підміняє
// лише цей годинник і не чекає реальних хвилин.
public sealed class SessionLifetimeTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private const string SessionCookie = ".AspNetCore.Identity.Application=";
    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(20);

    private (WebApplicationFactory<Program> Host, ShiftableClock Clock) HostWithClock()
    {
        var clock = new ShiftableClock();
        var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme,
                options => options.TimeProvider = clock)));
        return (host, clock);
    }

    private static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> host, string userName)
    {
        var client = TestHosts.HttpsClient(host);
        using var login = await TestHosts.LoginAsync(client, userName, StudySecret.Password);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    private static bool ReissuesSessionCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
        && values.Any(value => value.StartsWith(SessionCookie, StringComparison.Ordinal));

    // Налаштування з appsettings: 20 хвилин і sliding, а не стандартні 14 днів.
    [Fact]
    public void CookieOptions_AreTwentyMinutesWithSliding()
    {
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);

        Assert.Equal(Idle, options.ExpireTimeSpan);
        Assert.True(options.SlidingExpiration);
    }

    // Покинутий сеанс: 20 хвилин і 1 секунда без запитів. Та сама cookie більше не діє.
    [Fact]
    public async Task IdleSession_AfterTwentyMinutes_Gets401EvenWithSameCookie()
    {
        var (host, clock) = HostWithClock();
        using (host)
        {
            using var bob = await SignInAsync(host, "bob");
            await bob.AttachCsrfTokenAsync();

            clock.Advance(Idle + TimeSpan.FromSeconds(1));

            using var me = await bob.GetAsync("/api/me");
            Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
            var title = $"expired-{Guid.NewGuid():N}";
            using var create = await bob.PostAsJsonAsync("/api/incidents", new
            {
                title,
                description = "Спроба після завершення строку сеансу.",
                severity = "Low",
                occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
            });
            Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
            Assert.Equal(0, await IncidentDb.CountByTitleAsync(factory, title));
        }
    }

    // До половини строку сервер квиток не переписує: зайвих Set-Cookie немає.
    [Fact]
    public async Task RequestBeforeHalfLifetime_KeepsCookieWithoutReissue()
    {
        var (host, clock) = HostWithClock();
        using (host)
        {
            using var bob = await SignInAsync(host, "bob");

            clock.Advance(TimeSpan.FromMinutes(9));
            using var me = await bob.GetAsync("/api/me");

            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
            Assert.False(ReissuesSessionCookie(me));
        }
    }

    // Sliding: на 11-й хвилині сервер видає новий квиток. Через 15 хвилин після цього
    // (26 від входу) сеанс ще живий, а після 21 хвилини тиші знову 401.
    [Fact]
    public async Task ActiveUse_AfterHalfLifetime_ReissuesCookieAndExtendsSession()
    {
        var (host, clock) = HostWithClock();
        using (host)
        {
            using var bob = await SignInAsync(host, "bob");

            clock.Advance(TimeSpan.FromMinutes(11));
            using var renewed = await bob.GetAsync("/api/me");
            Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
            Assert.True(ReissuesSessionCookie(renewed), "Очікувався новий Set-Cookie сеансу.");
            var attributes = renewed.Headers.GetValues("Set-Cookie")
                .Single(value => value.StartsWith(SessionCookie, StringComparison.Ordinal))
                .Split(';').Skip(1).Select(part => part.Trim().ToLowerInvariant()).ToArray();
            Assert.Contains("httponly", attributes);
            Assert.DoesNotContain(attributes, part => part.StartsWith("expires=", StringComparison.Ordinal));

            clock.Advance(TimeSpan.FromMinutes(15));
            using var stillAlive = await bob.GetAsync("/api/me");
            Assert.Equal(HttpStatusCode.OK, stillAlive.StatusCode);

            clock.Advance(Idle + TimeSpan.FromMinutes(1));
            using var expired = await bob.GetAsync("/api/me");
            Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        }
    }
}
