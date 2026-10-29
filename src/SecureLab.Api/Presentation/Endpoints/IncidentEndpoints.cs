using SecureLab.Api.Application.Access;
using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Presentation.Endpoints;

public static class IncidentEndpoints
{
    public static IEndpointRouteBuilder MapIncidentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // ЛР 4: уся група читання вимагає входу. Анонім отримує 401 ще до коду endpoint.
        var group = endpoints.MapGroup("/api/incidents")
            .WithTags("Incidents")
            .RequireAuthorization();

        group.MapGet("/", GetListAsync)
            .WithName("GetIncidents")
            .Produces<IReadOnlyList<IncidentListItemResponse>>()
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}", GetDetailsAsync)
            .WithName("GetIncidentDetails")
            .Produces<IncidentDetailsResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/severity-summary", GetSeverityBreakdownAsync)
            .WithName("GetIncidentSeveritySummary")
            .Produces<IReadOnlyList<SeverityCountDto>>()
            .ProducesValidationProblem();

        return endpoints;
    }

    private static async Task<IResult> GetListAsync(
        string? status,
        HttpContext httpContext,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        if (!CallerProfile.TryRead(httpContext.User, out var caller))
        {
            return AccessProblems.NoVerifiedCaller();
        }

        IncidentStatus? parsedStatus = null;
        if (status is not null)
        {
            if (!Enum.TryParse<IncidentStatus>(status, ignoreCase: true, out var candidate)
                || !Enum.IsDefined(candidate))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] = ["Допустимі значення: New, Triaged, InProgress, Resolved, Closed."]
                });
            }

            parsedStatus = candidate;
        }

        return Results.Ok(await queries.GetListAsync(parsedStatus, caller, cancellationToken));
    }

    private static async Task<IResult> GetSeverityBreakdownAsync(
        string? status,
        HttpContext httpContext,
        SeverityBreakdownQuery breakdownQuery,
        CancellationToken cancellationToken)
    {
        if (!CallerProfile.TryRead(httpContext.User, out var caller))
        {
            return AccessProblems.NoVerifiedCaller();
        }

        IncidentStatus? statusFilter = null;
        if (status is not null)
        {
            if (!TryReadStatusFilter(status, out var allowedStatus))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] = ["Вкажіть одне значення з точним регістром: New, Triaged, InProgress, Resolved, Closed."]
                });
            }

            statusFilter = allowedStatus;
        }

        return Results.Ok(await breakdownQuery.CountBySeverityAsync(statusFilter, caller, cancellationToken));
    }

    // Приймаємо лише назву, оголошену в enum IncidentStatus, у точному регістрі.
    // Цифри і кома відсікаються окремо: Enum.TryParse пропустив би "2" або "New,Triaged".
    private static bool TryReadStatusFilter(string rawValue, out IncidentStatus parsedStatus)
    {
        parsedStatus = default;
        if (rawValue.Length == 0 || rawValue.Any(symbol => char.IsDigit(symbol) || symbol == ',' || char.IsWhiteSpace(symbol)))
        {
            return false;
        }

        return Enum.TryParse(rawValue, ignoreCase: false, out parsedStatus)
            && Enum.IsDefined(parsedStatus);
    }

    // Спершу факти (власник, статус) і рішення сторожа, лише потім зміст інциденту.
    private static async Task<IResult> GetDetailsAsync(
        Guid id,
        HttpContext httpContext,
        IncidentQueries queries,
        IncidentAccessGuard guard,
        CancellationToken cancellationToken)
    {
        if (!CallerProfile.TryRead(httpContext.User, out var caller))
        {
            return AccessProblems.NoVerifiedCaller();
        }

        var verdict = guard.Decide(caller, IncidentAction.ReadDetails, await queries.GetFactsAsync(id, cancellationToken));
        if (verdict != AccessVerdict.Granted)
        {
            return AccessProblems.ForIncident(verdict, id);
        }

        var incident = await queries.GetDetailsAsync(id, guard.CanSeeInternalNotes(caller), cancellationToken);
        return incident is null ? AccessProblems.IncidentMissing(id) : Results.Ok(incident);
    }
}
