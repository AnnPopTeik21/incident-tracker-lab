using System.Net;
using System.Net.Http.Json;
using SecureLab.Api.Data;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Tests;

// Переліки: list, search і severity-summary рахують лише записи, дозволені caller.
// Seed після reset: Аліса має 1, 3, 5 (Medium, Low, Low), Боб має 2, 4 (High, Low).
public sealed class ReadPathScopeTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private static readonly Guid BobSearchIncidentId = Guid.Parse("20000000-0000-0000-0000-000000000004");

    private async Task<HashSet<Guid>> OwnersAsync(IEnumerable<Guid> ids)
    {
        var owners = new HashSet<Guid>();
        foreach (var id in ids) owners.Add(await IncidentDb.OwnerOfAsync(factory, id));
        return owners;
    }

    [Fact]
    public async Task List_ForAlice_HoldsOnlyHerRecords()
    {
        using var alice = await factory.LoginAsync("alice", StudySecret.Password);

        var rows = await alice.GetFromJsonAsync<List<IncidentListItemResponse>>("/api/incidents");

        Assert.NotEmpty(rows!);
        Assert.Equal([DbSeeder.AliceId], await OwnersAsync(rows!.Select(row => row.Id)));
    }

    [Fact]
    public async Task List_ForBob_HoldsOnlyHisRecords()
    {
        using var bob = await factory.LoginAsync("bob", StudySecret.Password);

        var rows = await bob.GetFromJsonAsync<List<IncidentListItemResponse>>("/api/incidents");

        Assert.Contains(rows!, row => row.Id == DbSeeder.BobIncidentId);
        Assert.Equal([DbSeeder.BobId], await OwnersAsync(rows!.Select(row => row.Id)));
    }

    [Fact]
    public async Task List_ForAnalyst_HoldsBothOwners()
    {
        using var morgan = await factory.LoginAsync("morgan", StudySecret.Password);

        var rows = await morgan.GetFromJsonAsync<List<IncidentListItemResponse>>("/api/incidents");

        Assert.Contains(rows!, row => row.Id == DbSeeder.AliceIncidentId);
        Assert.Contains(rows!, row => row.Id == DbSeeder.BobIncidentId);
    }

    // A-11, головна знахідка: до виправлення Аліса знаходила запис Боба за словом з його заголовка.
    [Fact]
    public async Task Search_ForBobsTitleWord_IsEmptyForAlice_ButFindsForBob()
    {
        using var alice = await factory.LoginAsync("alice", StudySecret.Password);
        using var bob = await factory.LoginAsync("bob", StudySecret.Password);
        const string url = "/api/incidents/search?q=O'Brien";

        var forAlice = await alice.GetFromJsonAsync<List<IncidentSearchItemDto>>(url);
        var forBob = await bob.GetFromJsonAsync<List<IncidentSearchItemDto>>(url);

        Assert.Empty(forAlice!);
        Assert.Equal([BobSearchIncidentId], forBob!.Select(row => row.Id));
    }

    [Fact]
    public async Task Search_WithEmptyText_ReturnsOnlyAlicesRecords()
    {
        using var alice = await factory.LoginAsync("alice", StudySecret.Password);

        var rows = await alice.GetFromJsonAsync<List<IncidentSearchItemDto>>("/api/incidents/search?q=");

        Assert.NotEmpty(rows!);
        Assert.Equal([DbSeeder.AliceId], await OwnersAsync(rows!.Select(row => row.Id)));
    }

    [Fact]
    public async Task SeveritySummary_ForAlice_CountsOnlyHerIncidents()
    {
        using var alice = await factory.LoginAsync("alice", StudySecret.Password);

        var levels = await alice.GetFromJsonAsync<List<SeverityCountDto>>("/api/incidents/severity-summary");

        Assert.Equal(["Low", "Medium", "High", "Critical"], levels!.Select(level => level.Severity));
        Assert.Equal([2, 1, 0, 0], levels!.Select(level => level.Count));
    }

    [Fact]
    public async Task SeveritySummary_ForBob_ShowsHisHighAndLow()
    {
        using var bob = await factory.LoginAsync("bob", StudySecret.Password);

        var levels = await bob.GetFromJsonAsync<List<SeverityCountDto>>("/api/incidents/severity-summary");

        Assert.Equal([1, 0, 1, 0], levels!.Select(level => level.Count));
    }

    // Administrator власних інцидентів не має і окремого права на чужі агрегати теж.
    [Fact]
    public async Task SeveritySummary_ForAdministrator_IsAllZeros()
    {
        using var admin = await factory.LoginAsync("admin", StudySecret.Password);

        using var response = await admin.GetAsync("/api/incidents/severity-summary");
        var levels = await response.Content.ReadFromJsonAsync<List<SeverityCountDto>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.All(levels!, level => Assert.Equal(0, level.Count));
    }
}
