using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SecureLab.Api.Data;

namespace SecureLab.Api.Tests;

// Контракт реєстрації: T-01, T-02, повторні дані (409) і безпечна відповідь без службових полів.
// Імена і email генеруються на кожен запуск, тому повторний прогін на тій самій БД не заважає.
public sealed class SignUpContractTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private HttpClient NewClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });

    private static string NewName() => "su" + Guid.NewGuid().ToString("N")[..10];

    // Пароль нового акаунта відповідає політиці і формується локально, без друку.
    private static string NewPassword() => "Kv-" + Guid.NewGuid().ToString("N")[..10] + "x9";

    // T-01: коректна реєстрація дає 201 і рівно три поля. Зайві role та id нічого не змінюють.
    [Fact]
    public async Task ValidSignUp_Returns201WithThreeSafeFields_AndIgnoresRoleAndId()
    {
        using var client = NewClient();
        var name = NewName();
        var password = NewPassword();
        var forgedId = Guid.NewGuid();

        using var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            userName = name,
            email = $"{name}@example.test",
            displayName = "  Тестова Студентка  ",
            password,
            role = DbSeeder.AdministratorRole,
            id = forgedId
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/api/me", response.Headers.Location?.OriginalString);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["id", "userName", "displayName"], body.EnumerateObject().Select(property => property.Name));
        Assert.NotEqual(forgedId, body.GetProperty("id").GetGuid());
        Assert.Equal("Тестова Студентка", body.GetProperty("displayName").GetString());

        // Новий користувач входить і бачить роль Reporter, а не Administrator з тіла запиту.
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { userName = name, password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal([DbSeeder.ReporterRole], me.GetProperty("roles").EnumerateArray().Select(role => role.GetString()));
    }

    // Дублікат: зайняте ім'я і зайнятий email дають однакову відповідь 409 без назви поля.
    [Fact]
    public async Task TakenNameOrEmail_Returns409WithOneSharedMessage()
    {
        using var client = NewClient();
        var name = NewName();
        using var first = await client.PostAsJsonAsync("/api/auth/register",
            new { userName = name, email = $"{name}@example.test", displayName = "Перша", password = NewPassword() });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var otherName = NewName();
        using var sameName = await client.PostAsJsonAsync("/api/auth/register",
            new { userName = name, email = $"{otherName}@example.test", displayName = "Друга", password = NewPassword() });
        using var sameEmail = await client.PostAsJsonAsync("/api/auth/register",
            new { userName = otherName, email = $"{name}@example.test", displayName = "Третя", password = NewPassword() });

        Assert.Equal(HttpStatusCode.Conflict, sameName.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, sameEmail.StatusCode);
        Assert.Equal("application/problem+json", sameName.Content.Headers.ContentType?.MediaType);
        var a = await ProblemFingerprint.ReadAsync(sameName);
        var b = await ProblemFingerprint.ReadAsync(sameEmail);
        Assert.Equal(a.Stable, b.Stable);
        Assert.DoesNotContain(name, a.Stable, StringComparison.Ordinal);
    }

    // T-02: некоректні поля дають 400 з ключем саме цього поля. 121 символ раніше давав 500.
    [Theory]
    [InlineData("short-name", "userName")]
    [InlineData("name-with-space", "userName")]
    [InlineData("bad-email", "email")]
    [InlineData("display-81", "displayName")]
    [InlineData("display-121", "displayName")]
    [InlineData("weak-password", "password")]
    public async Task BrokenField_Returns400ForThatFieldOnly(string brokenCase, string expectedKey)
    {
        using var client = NewClient();
        var name = NewName();
        var userName = name;
        var email = $"{name}@example.test";
        var displayName = "Студентка";
        var password = NewPassword();
        switch (brokenCase)
        {
            case "short-name": userName = "abc"; break;
            case "name-with-space": userName = "anna k"; break;
            case "bad-email": email = "anna.example.test"; break;
            case "display-81": displayName = new string('Д', 81); break;
            case "display-121": displayName = new string('Д', 121); break;
            case "weak-password": password = "onlylower1"; break;
        }

        using var response = await client.PostAsJsonAsync("/api/auth/register",
            new { userName, email, displayName, password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var keys = body.GetProperty("errors").EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal([expectedKey], keys);
        Assert.DoesNotContain(password, body.GetRawText(), StringComparison.Ordinal);
    }
}
