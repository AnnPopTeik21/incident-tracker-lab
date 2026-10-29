using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SecureLab.Api.Data;

namespace SecureLab.Api.Tests;

// Змінювальний периметр: PATCH і DELETE. Кожна відмова перевіряється ще й повторним читанням стану з БД:
// статус 403 сам по собі не доводить, що запис не змінився.
public sealed class ForeignIncidentChangeTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>, IAsyncLifetime
{
    private readonly List<Guid> _created = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => IncidentDb.DeleteAsync(factory, _created);

    private static string Url(Guid id) => $"/api/incidents/{id}";

    [Fact]
    public async Task Owner_ChangesDescription_AndOnlyContentMoves()
    {
        using var alice = await factory.LoginAsync("alice", StudySecret.Password);
        var id = await OwnIncident.CreateAsync(alice, "patch-own", _created);
        var before = await IncidentDb.SnapshotAsync(factory, id);

        using var response = await alice.PatchJsonAsync(Url(id), new { description = "Флешку передано черговому інженеру." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = await IncidentDb.SnapshotAsync(factory, id);
        Assert.Equal("Флешку передано черговому інженеру.", after!.Description);
        Assert.Equal(before!.Title, after.Title);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.Severity, after.Severity);
        Assert.Equal(DbSeeder.AliceId, after.OwnerUserId);
    }

    // Той самий запит, що в PoC: cookie і CSRF-токен Аліси, змінено лише id на інцидент Боба.
    [Fact]
    public async Task Reporter_PatchingBobsIncident_GetsForbidden_AndRecordStaysTheSame()
    {
        using var alice = await factory.LoginAsync("alice", StudySecret.Password);
        var before = await IncidentDb.SnapshotAsync(factory, DbSeeder.BobIncidentId);

        using var response = await alice.PatchJsonAsync(Url(DbSeeder.BobIncidentId),
            new { title = "Заголовок змінено з облікового запису Alice" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, await IncidentDb.SnapshotAsync(factory, DbSeeder.BobIncidentId));
    }

    [Fact]
    public async Task Reporter_DeletingBobsIncident_GetsForbidden_AndRecordRemains()
    {
        using var alice = await factory.LoginAsync("alice", StudySecret.Password);
        var before = await IncidentDb.SnapshotAsync(factory, DbSeeder.BobIncidentId);

        using var response = await alice.DeleteAsync(Url(DbSeeder.BobIncidentId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, await IncidentDb.SnapshotAsync(factory, DbSeeder.BobIncidentId));
    }

    // Право читати для аналізу не є правом редагувати чужий зміст.
    [Fact]
    public async Task Analyst_PatchingReporterIncident_GetsForbidden_AndNothingChanges()
    {
        using var morgan = await factory.LoginAsync("morgan", StudySecret.Password);
        var before = await IncidentDb.SnapshotAsync(factory, DbSeeder.AliceIncidentId);

        using var response = await morgan.PatchJsonAsync(Url(DbSeeder.AliceIncidentId), new { description = "Аналітик переписав опис." });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, await IncidentDb.SnapshotAsync(factory, DbSeeder.AliceIncidentId));
    }

    [Fact]
    public async Task Owner_DeletesOwnNewIncident()
    {
        using var bob = await factory.LoginAsync("bob", StudySecret.Password);
        var id = await OwnIncident.CreateAsync(bob, "delete-own", _created);

        using var response = await bob.DeleteAsync(Url(id));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await IncidentDb.SnapshotAsync(factory, id));
    }

    // Умова матриці для delete: власник, і лише поки запис New. Інцидент Аліси вже Triaged.
    [Fact]
    public async Task Owner_CannotDeleteIncidentAlreadyTakenByAnalyst()
    {
        using var alice = await factory.LoginAsync("alice", StudySecret.Password);
        var before = await IncidentDb.SnapshotAsync(factory, DbSeeder.AliceIncidentId);

        using var response = await alice.DeleteAsync(Url(DbSeeder.AliceIncidentId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, await IncidentDb.SnapshotAsync(factory, DbSeeder.AliceIncidentId));
    }

    // A-08b: звичайний PATCH з полем, якого немає в UpdateIncidentRequest, відхиляється цілком.
    // Навіть дозволений title з того самого запиту не застосовується.
    [Theory]
    [InlineData("ownerUserId", "10000000-0000-0000-0000-000000000002")]
    [InlineData("status", "Closed")]
    [InlineData("severity", "Critical")]
    [InlineData("role", "Analyst")]
    [InlineData("isTransitionAllowed", "true")]
    public async Task ExtraProtectedField_Gets400_AndWholeRecordStays(string field, string value)
    {
        using var alice = await factory.LoginAsync("alice", StudySecret.Password);
        var id = await OwnIncident.CreateAsync(alice, "overpost", _created);
        var before = await IncidentDb.SnapshotAsync(factory, id);
        var body = new Dictionary<string, object> { ["title"] = "Новий заголовок", [field] = value };

        using var response = await alice.PatchJsonAsync(Url(id), body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(before, await IncidentDb.SnapshotAsync(factory, id));
    }

    [Fact]
    public async Task UnknownIncident_GetsNotFound()
    {
        using var alice = await factory.LoginAsync("alice", StudySecret.Password);

        using var response = await alice.PatchJsonAsync(Url(Guid.NewGuid()), new { title = "x" });
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Інцидент не знайдено", problem.GetProperty("title").GetString());
    }
}
