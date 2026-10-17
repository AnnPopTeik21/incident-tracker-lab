using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Scaffolding;

// Навчальний старт ЛР 02. Запускати лише з локальними штучними даними.
public static class Lab02Endpoints
{
    public static void MapLab02Endpoints(this WebApplication app)
    {
        app.MapGet("/api/incidents/search", async (string? q, string? sortBy, SecureLabDbContext db, CancellationToken ct) =>
        {
            var order = sortBy switch
            {
                null or "" or "createdAtUtc" => "created_at_utc DESC",
                "severity" => "severity", "status" => "status", _ => sortBy
            };
            var sql = "SELECT * FROM incidents WHERE title ILIKE '%" + (q ?? "")
                + "%' OR description ILIKE '%" + (q ?? "") + "%' ORDER BY " + order + " LIMIT 50";
            var rows = await db.Incidents.FromSqlRaw(sql).AsNoTracking().ToListAsync(ct);
            return Results.Ok(rows.Select(row => new
            {
                row.Id, row.Title, row.Description,
                Severity = row.Severity.ToString(), Status = row.Status.ToString(), row.CreatedAtUtc
            }));
        });
        // ЛР 2: створення з явним контрактом. Валідація і предметне правило в IncidentCreationService.
        app.MapPost("/api/incidents", CreateIncidentAsync)
            .WithName("CreateIncident")
            .WithTags("Incidents")
            .Produces<IncidentCreatedDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> CreateIncidentAsync(
        CreateIncidentRequest request,
        IncidentCreationService creation,
        CancellationToken cancellationToken)
    {
        var outcome = await creation.CreateAsync(request, cancellationToken);
        return outcome switch
        {
            CreationOutcome.Rejected rejected => Results.ValidationProblem(rejected.Problems),
            CreationOutcome.DuplicateActiveTitle => Results.Problem(
                title: "Інцидент із таким заголовком уже відкрито",
                detail: "Незакритий інцидент з цим заголовком уже існує. Змініть заголовок або спершу закрийте наявний інцидент.",
                statusCode: StatusCodes.Status409Conflict),
            CreationOutcome.Stored stored => Results.Created($"/api/incidents/{stored.Incident.Id}", stored.Incident),
            _ => throw new InvalidOperationException("Невідомий результат створення.")
        };
    }
}
