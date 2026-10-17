using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SecureLab.Api.Data;

namespace SecureLab.Api.Tests;

// Власник нового інциденту: лише id з principal. Виконавець Боб, щоб константа Аліси
// з ЛР 2 не могла випадково дати правильну відповідь.
public sealed class OwnerFromPrincipalTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>, IAsyncLifetime
{
    private readonly List<Guid> _created = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => IncidentDb.DeleteAsync(factory, _created);

    private async Task<JsonElement> CreateAsBobAsync(HttpClient bob, object body)
    {
        using var response = await bob.PostAsJsonAsync("/api/incidents", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        _created.Add(created.GetProperty("id").GetGuid());
        return created;
    }

    // A-04: у JSON немає поля власника. Сервер ставить власником Боба, який увійшов.
    [Fact]
    public async Task BobCreatesWithoutOwnerField_RecordBelongsToBob()
    {
        using var bob = await factory.LoginAsync("bob", StudySecret.Password);

        var created = await CreateAsBobAsync(bob, new
        {
            title = $"owner-plain-{Guid.NewGuid():N}",
            description = "Боб повідомляє про загублену перепустку.",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-20)
        });

        var id = created.GetProperty("id").GetGuid();
        Assert.Equal(DbSeeder.BobId, await IncidentDb.OwnerOfAsync(factory, id));
        Assert.False(created.TryGetProperty("ownerUserId", out _));
    }

    // A-05: Боб дописує в JSON ownerUserId Аліси. Поле ігнорується, власник лишається Бобом.
    [Fact]
    public async Task BobSendsAliceAsOwner_ForgedFieldIsIgnored()
    {
        using var bob = await factory.LoginAsync("bob", StudySecret.Password);

        var created = await CreateAsBobAsync(bob, new
        {
            title = $"owner-forged-{Guid.NewGuid():N}",
            description = "У тілі запиту підставлено чужого власника.",
            severity = "Medium",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-15),
            ownerUserId = DbSeeder.AliceId
        });

        var id = created.GetProperty("id").GetGuid();
        Assert.Equal(DbSeeder.BobId, await IncidentDb.OwnerOfAsync(factory, id));
        // Той самий факт через публічний API: деталі показують ім'я Боба, а не Аліси.
        var details = await bob.GetFromJsonAsync<JsonElement>($"/api/incidents/{id}");
        Assert.Equal("Боб Мельник", details.GetProperty("ownerDisplayName").GetString());
    }
}
