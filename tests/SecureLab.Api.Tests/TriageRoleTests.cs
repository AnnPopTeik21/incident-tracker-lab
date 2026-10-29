using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SecureLab.Api.Data;

namespace SecureLab.Api.Tests;

// Привілейована дія POST /api/incidents/{id}/triage: лише Analyst, лише New -> Triaged,
// наступний статус задає сервер. Відмова пише подію authorization.denied.
public sealed class TriageRoleTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>, IAsyncLifetime
{
    private readonly List<Guid> _created = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => IncidentDb.DeleteAsync(factory, _created);

    private static string Triage(Guid id) => $"/api/incidents/{id}/triage";

    // Вертикальне підвищення: Аліса пробує перевести свій же New-інцидент. Власність не дає ролі Analyst.
    [Fact]
    public async Task Reporter_TriagingOwnNewIncident_GetsForbidden_AndStatusStaysNew()
    {
        using var alice = await factory.LoginAsync("alice", StudySecret.Password);
        var before = await IncidentDb.SnapshotAsync(factory, DbSeeder.SafeTextIncidentId);

        using var response = await alice.PostAsJsonAsync(Triage(DbSeeder.SafeTextIncidentId), new { note = "Сама собі аналітик" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var after = await IncidentDb.SnapshotAsync(factory, DbSeeder.SafeTextIncidentId);
        Assert.Equal("New", after!.Status);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Analyst_TriagesNewIncident_ServerSetsTriaged_AndRecordsHistory()
    {
        using var alice = await factory.LoginAsync("alice", StudySecret.Password);
        using var morgan = await factory.LoginAsync("morgan", StudySecret.Password);
        var id = await OwnIncident.CreateAsync(alice, "triage-ok", _created);

        using var response = await morgan.PostAsJsonAsync(Triage(id), new { note = "Перевірено журнал, загрози немає." });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("New", body.GetProperty("previousStatus").GetString());
        Assert.Equal("Triaged", body.GetProperty("status").GetString());
        var after = await IncidentDb.SnapshotAsync(factory, id);
        Assert.Equal("Triaged", after!.Status);
        Assert.Equal(DbSeeder.AliceId, after.OwnerUserId);
        Assert.Equal([(DbSeeder.MorganId, "New", "Triaged")], await IncidentDb.HistoryOfAsync(factory, id));
    }

    // Роль є, але контекст не той: інцидент уже Triaged. Це конфлікт стану, а не відмова в доступі.
    [Fact]
    public async Task Analyst_OnAlreadyTriagedIncident_GetsConflict_AndStatusStays()
    {
        using var morgan = await factory.LoginAsync("morgan", StudySecret.Password);
        var before = await IncidentDb.SnapshotAsync(factory, DbSeeder.AliceIncidentId);

        using var response = await morgan.PostAsJsonAsync(Triage(DbSeeder.AliceIncidentId), new { note = "ще раз" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(before, await IncidentDb.SnapshotAsync(factory, DbSeeder.AliceIncidentId));
    }

    // Клієнт не обирає наступний статус: поле status у тілі triage відхиляється з 400.
    [Fact]
    public async Task StatusFieldInTriageBody_Gets400_AndIncidentStaysNew()
    {
        using var bob = await factory.LoginAsync("bob", StudySecret.Password);
        using var morgan = await factory.LoginAsync("morgan", StudySecret.Password);
        var id = await OwnIncident.CreateAsync(bob, "triage-forged", _created);

        using var response = await morgan.PostAsJsonAsync(Triage(id), new { note = "x", status = "Closed" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("New", (await IncidentDb.SnapshotAsync(factory, id))!.Status);
    }

    [Fact]
    public async Task Administrator_GetsForbidden_BecauseRuleGivesTriageOnlyToAnalyst()
    {
        using var admin = await factory.LoginAsync("admin", StudySecret.Password);

        using var response = await admin.PostAsJsonAsync(Triage(DbSeeder.SafeTextIncidentId), new { note = "адмін" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("New", (await IncidentDb.SnapshotAsync(factory, DbSeeder.SafeTextIncidentId))!.Status);
    }

    // Подія відмови: є subjectId, дія, ресурс, результат і той самий trace id, що й у відповіді.
    // Немає імені користувача, пароля, cookie, CSRF-токена і тіла запиту.
    [Fact]
    public async Task DeniedTriage_LeavesSafeAuditEvent_WithResponseTraceId()
    {
        var sink = new MemoryLogSink();
        await using var host = TestHosts.Fresh(factory, logSink: sink);
        using var bob = TestHosts.HttpsClient(host);
        using (var login = await TestHosts.LoginAsync(bob, "bob", StudySecret.Password))
        {
            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        }
        var csrfToken = await bob.GetCsrfTokenAsync();
        bob.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrfToken);
        const string secretNote = "нотатка-яку-не-можна-логувати";

        using var response = await bob.PostAsJsonAsync(Triage(DbSeeder.SafeTextIncidentId), new { note = secretNote });
        var traceId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("traceId").GetString()!;

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var events = sink.Entries.Where(entry => entry.Category == "SecureLab.AccessAudit").Select(entry => entry.Message).ToList();
        var line = Assert.Single(events);
        Assert.StartsWith("authorization.denied ", line);
        Assert.Contains($"subjectId={DbSeeder.BobId}", line);
        Assert.Contains("action=incident.triage", line);
        Assert.Contains($"resource=incident:{DbSeeder.SafeTextIncidentId}", line);
        Assert.Contains("result=denied", line);
        Assert.Contains($"trace={traceId}", line);
        Assert.DoesNotContain("bob", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(StudySecret.Password, line);
        Assert.DoesNotContain(csrfToken, line);
        Assert.DoesNotContain(secretNote, line);
        Assert.DoesNotContain(".AspNetCore", line);
    }
}
