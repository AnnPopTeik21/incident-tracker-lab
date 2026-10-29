using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SecureLab.Api.Tests;

// Позитивні змінювальні тести працюють лише з записом, який сам тест створив від імені користувача.
// Новий запис завжди New, тож повторний запуск не залежить від попереднього. Прибирання у DisposeAsync класу.
internal static class OwnIncident
{
    public static async Task<Guid> CreateAsync(HttpClient client, string label, List<Guid> cleanup)
    {
        using var response = await client.PostAsJsonAsync("/api/incidents", new
        {
            title = $"{label}-{Guid.NewGuid():N}",
            description = "Запис створено тестом ЛР 4 і буде видалено після тесту.",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-30)
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        cleanup.Add(id);
        return id;
    }

    public static Task<HttpResponseMessage> PatchJsonAsync(this HttpClient client, string url, object body) =>
        client.PatchAsync(url, JsonContent.Create(body));
}
