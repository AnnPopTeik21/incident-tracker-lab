using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Scaffolding;

// Навчальний старт ЛР 02. Запускати лише з локальними штучними даними.
public static class Lab02Endpoints
{
    public static void MapLab02Endpoints(this WebApplication app)
    {
        // ЛР 2: пошук без склеювання SQL. q іде параметром, sortBy обирається з allowlist.
        app.MapGet("/api/incidents/search", SearchIncidentsAsync)
            .WithName("SearchIncidents")
            .WithTags("Incidents")
            .Produces<IReadOnlyList<IncidentSearchItemDto>>()
            .ProducesValidationProblem();

        // ЛР 2: створення з явним контрактом. Валідація і предметне правило в IncidentCreationService.
        app.MapPost("/api/incidents", CreateIncidentAsync)
            .WithName("CreateIncident")
            .WithTags("Incidents")
            .Produces<IncidentCreatedDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> SearchIncidentsAsync(
        string? q,
        string? sortBy,
        string? order,
        IncidentSearchQuery search,
        CancellationToken cancellationToken)
    {
        // Невідомий ключ сортування зупиняється тут і до SQL не доходить.
        var ordering = IncidentSearchQuery.ResolveOrdering(sortBy, order, out var problems);
        if (ordering is null)
        {
            return Results.ValidationProblem(problems);
        }

        return Results.Ok(await search.FindAsync(q, ordering, cancellationToken));
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
