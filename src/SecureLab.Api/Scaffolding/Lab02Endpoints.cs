using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Presentation.Contracts;
using SecureLab.Api.Security;

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
        // ЛР 3: лише для того, хто ввійшов; анонімний запит отримує 401 ще до коду endpoint.
        app.MapPost("/api/incidents", CreateIncidentAsync)
            .WithName("CreateIncident")
            .WithTags("Incidents")
            .RequireAuthorization()
            .Produces<IncidentCreatedDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
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
        HttpContext httpContext,
        IncidentCreationService creation,
        CancellationToken cancellationToken)
    {
        // Власник нового запису: id з перевіреного principal. Поле owner у JSON не входить у DTO.
        if (!VerifiedUser.TryGetId(httpContext.User, out var ownerUserId))
        {
            return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);
        }

        var outcome = await creation.CreateAsync(request, ownerUserId, cancellationToken);
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
