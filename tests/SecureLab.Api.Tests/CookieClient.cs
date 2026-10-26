using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SecureLab.Api.Tests;

public static class CookieClient
{
    // Вхід через cookie. Після входу клієнт одразу бере CSRF-токен і додає його до всіх
    // наступних запитів, як це робить браузерний клієнт. attachCsrf: false лишає клієнт без токена.
    public static async Task<HttpClient> LoginAsync(this SecureLabApiFactory factory, string userName, string password,
        bool attachCsrf = true)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
        });
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { userName, password });
        if (!response.IsSuccessStatusCode)
        {
            client.Dispose();
            throw new InvalidOperationException("Study login failed (credentials omitted).");
        }
        if (attachCsrf)
        {
            await client.AttachCsrfTokenAsync();
        }
        return client;
    }

    // Новий токен для поточного користувача клієнта; старий заголовок замінюється.
    public static async Task AttachCsrfTokenAsync(this HttpClient client)
    {
        var token = await client.GetCsrfTokenAsync();
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);
    }

    public static async Task<string> GetCsrfTokenAsync(this HttpClient client)
    {
        using var response = await client.GetAsync("/api/auth/csrf");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("token").GetString()!;
    }
}
