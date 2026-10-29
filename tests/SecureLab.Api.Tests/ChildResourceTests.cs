using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SecureLab.Api.Data;

namespace SecureLab.Api.Tests;

// Непрямий шлях до інциденту через коментар: GET /api/incident-comments/{id}.
// Рішення приймається за батьківським інцидентом, потім діє правило internal.
public sealed class ChildResourceTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private static readonly Guid AlicePublicCommentId = Guid.Parse("30000000-0000-0000-0000-000000000001");

    private static string Comment(Guid id) => $"/api/incident-comments/{id}";

    [Fact]
    public async Task Reporter_ReadingCommentOfBobsIncident_GetsForbidden_WithoutText()
    {
        using var alice = await factory.LoginAsync("alice", StudySecret.Password);

        using var response = await alice.GetAsync(Comment(DbSeeder.BobIncidentCommentId));
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("Розпочато перевірку журналу", text);
        Assert.DoesNotContain(DbSeeder.BobIncidentId.ToString(), text);
    }

    [Fact]
    public async Task Owner_ReadsPublicCommentOfOwnIncident()
    {
        using var alice = await factory.LoginAsync("alice", StudySecret.Password);

        var body = await alice.GetFromJsonAsync<JsonElement>(Comment(AlicePublicCommentId));

        Assert.Equal(DbSeeder.AliceIncidentId, body.GetProperty("incidentId").GetGuid());
        Assert.False(body.TryGetProperty("isInternal", out _));
    }

    // Власник батьківського інциденту все одно Reporter: внутрішня нотатка аналітика йому закрита.
    [Fact]
    public async Task Owner_CannotOpenInternalNote_ByDirectId()
    {
        using var alice = await factory.LoginAsync("alice", StudySecret.Password);

        using var response = await alice.GetAsync(Comment(DbSeeder.AliceInternalNoteId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("списку блокування", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Analyst_ReadsInternalNote_AfterParentCheck()
    {
        using var morgan = await factory.LoginAsync("morgan", StudySecret.Password);

        var body = await morgan.GetFromJsonAsync<JsonElement>(Comment(DbSeeder.AliceInternalNoteId));

        Assert.Contains("списку блокування", body.GetProperty("text").GetString());
    }

    // Те саме правило в проєкції details: Reporter не бачить нотатку, Analyst бачить.
    [Fact]
    public async Task DetailsProjection_HidesInternalNoteFromOwner_ButShowsItToAnalyst()
    {
        using var alice = await factory.LoginAsync("alice", StudySecret.Password);
        using var morgan = await factory.LoginAsync("morgan", StudySecret.Password);
        var url = $"/api/incidents/{DbSeeder.AliceIncidentId}";

        var forOwner = await alice.GetFromJsonAsync<JsonElement>(url);
        var forAnalyst = await morgan.GetFromJsonAsync<JsonElement>(url);

        static List<Guid> Ids(JsonElement details) =>
            details.GetProperty("comments").EnumerateArray().Select(comment => comment.GetProperty("id").GetGuid()).ToList();
        Assert.DoesNotContain(DbSeeder.AliceInternalNoteId, Ids(forOwner));
        Assert.Contains(AlicePublicCommentId, Ids(forOwner));
        Assert.Contains(DbSeeder.AliceInternalNoteId, Ids(forAnalyst));
    }

    [Fact]
    public async Task UnknownComment_GetsNotFound()
    {
        using var alice = await factory.LoginAsync("alice", StudySecret.Password);

        using var response = await alice.GetAsync(Comment(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
