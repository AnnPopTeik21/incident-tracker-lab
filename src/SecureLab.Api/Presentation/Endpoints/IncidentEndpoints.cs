using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Presentation.Endpoints;

public static class IncidentEndpoints
{
    public static IEndpointRouteBuilder MapIncidentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/incidents")
            .WithTags("Incidents");

        group.MapGet("/", GetListAsync)
            .WithName("GetIncidents")
            .Produces<IReadOnlyList<IncidentListItemResponse>>()
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}", GetDetailsAsync)
            .WithName("GetIncidentDetails")
            .Produces<IncidentDetailsResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/severity-summary", GetSeverityBreakdownAsync)
            .WithName("GetIncidentSeveritySummary")
            .Produces<IReadOnlyList<SeverityCountDto>>()
            .ProducesValidationProblem();

        return endpoints;
    }

    private static async Task<IResult> GetListAsync(
        string? status,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
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

        return Results.Ok(await queries.GetListAsync(parsedStatus, cancellationToken));
    }

    private static async Task<IResult> GetSeverityBreakdownAsync(
        string? status,
        SeverityBreakdownQuery breakdownQuery,
        CancellationToken cancellationToken)
    {
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

        return Results.Ok(await breakdownQuery.CountBySeverityAsync(statusFilter, cancellationToken));
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

    private static async Task<IResult> GetDetailsAsync(
        Guid id,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        var incident = await queries.GetDetailsAsync(id, cancellationToken);
        return incident is null
            ? Results.Problem(
                title: "Інцидент не знайдено",
                detail: $"Інцидент '{id}' не існує.",
                statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(incident);
    }
}
