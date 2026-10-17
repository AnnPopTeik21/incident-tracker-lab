using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace SecureLab.Api.Tests;

// Ліміт входів: фіксоване вікно, ключ IP. Кожен сценарій бере свій новий host, бо лічильники
// належать host, а не HttpClient. У тестовому сервері IP немає, тож усі клієнти host ділять ліміт.
public sealed class LoginThrottleTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    // Справжні числа беру з appsettings.json застосунку, а не переписую в тест.
    private (int PermitLimit, int WindowSeconds) ConfiguredLimit()
    {
        var root = factory.Services.GetRequiredService<IWebHostEnvironment>().ContentRootPath;
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "appsettings.json")));
        var section = json.RootElement.GetProperty("Auth").GetProperty("LoginThrottle");
        return (section.GetProperty("PermitLimit").GetInt32(), section.GetProperty("WindowSeconds").GetInt32());
    }

    private static string WrongPassword() => "Miss-" + Guid.NewGuid().ToString("N")[..8] + "Z7";

    // Після вичерпання ліміту навіть правильний пароль і невідоме ім'я отримують той самий 429.
    [Fact]
    public async Task AttemptAfterLimit_Gets429Problem_ForAnyAccount()
    {
        var (limit, window) = ConfiguredLimit();
        Assert.Equal((4, 30), (limit, window));
        using var host = TestHosts.Fresh(factory, permitLimit: limit, windowSeconds: window);
        using var client = TestHosts.HttpsClient(host);

        for (var attempt = 0; attempt < limit; attempt++)
        {
            using var refused = await TestHosts.LoginAsync(client, "alice", WrongPassword());
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        }
        using var rightPassword = await TestHosts.LoginAsync(client, "alice", StudySecret.Password);
        using var unknownUser = await TestHosts.LoginAsync(client, "no-such-user", WrongPassword());

        Assert.Equal(HttpStatusCode.TooManyRequests, rightPassword.StatusCode);
        Assert.Equal("application/problem+json", rightPassword.Content.Headers.ContentType?.MediaType);
        Assert.True(rightPassword.Headers.RetryAfter is not null, "Очікувався заголовок Retry-After.");
        Assert.False(rightPassword.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.TooManyRequests, unknownUser.StatusCode);
        var a = await ProblemFingerprint.ReadAsync(rightPassword);
        var b = await ProblemFingerprint.ReadAsync(unknownUser);
        Assert.Equal(a.Stable, b.Stable);
    }

    // Відновлення: після вікна вхід знову проходить. Новий host має власний чистий лічильник.
    [Fact]
    public async Task QuotaRecoversAfterWindow_AndFreshHostIsIndependent()
    {
        var (limit, _) = ConfiguredLimit();
        const int shortWindowSeconds = 2;
        using var host = TestHosts.Fresh(factory, permitLimit: limit, windowSeconds: shortWindowSeconds);
        using var first = TestHosts.HttpsClient(host);
        using var second = TestHosts.HttpsClient(host);

        for (var attempt = 0; attempt < limit; attempt++)
        {
            using var refused = await TestHosts.LoginAsync(first, "bob", WrongPassword());
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        }
        // Інший HttpClient того самого host ділить той самий ключ і той самий лічильник.
        using var blocked = await TestHosts.LoginAsync(second, "bob", StudySecret.Password);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);

        using var otherHost = TestHosts.Fresh(factory, permitLimit: limit, windowSeconds: shortWindowSeconds);
        using var third = TestHosts.HttpsClient(otherHost);
        using var independent = await TestHosts.LoginAsync(third, "bob", StudySecret.Password);
        Assert.Equal(HttpStatusCode.NoContent, independent.StatusCode);

        await Task.Delay(TimeSpan.FromSeconds(shortWindowSeconds) + TimeSpan.FromMilliseconds(400));
        using var recovered = await TestHosts.LoginAsync(second, "bob", StudySecret.Password);
        Assert.Equal(HttpStatusCode.NoContent, recovered.StatusCode);
    }
}
