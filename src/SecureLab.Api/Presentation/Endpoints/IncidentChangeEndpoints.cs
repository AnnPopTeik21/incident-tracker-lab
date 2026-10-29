using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Presentation.Contracts;
using SecureLab.Api.Security;

namespace SecureLab.Api.Presentation.Endpoints;

// ЛР 4: змінювальні маршрути інциденту. Потрібні вхід і CSRF-токен, як для POST у ЛР 3.
public static class IncidentChangeEndpoints
{
    public static IEndpointRouteBuilder MapIncidentChangeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/incidents")
            .WithTags("Incidents")
            .RequireAuthorization()
            .RequireForgeryToken();

        group.MapPatch("/{id:guid}", ChangeContentAsync)
            .WithName("ChangeIncidentContent")
            .Produces<IncidentChangedDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> ChangeContentAsync(
        Guid id,
        UpdateIncidentRequest request,
        IncidentChangeService changes,
        CancellationToken cancellationToken)
    {
        var outcome = await changes.ChangeContentAsync(id, request, cancellationToken);
        return outcome switch
        {
            ChangeOutcome.Missing => Results.Problem(
                title: "Інцидент не знайдено",
                detail: $"Інцидент '{id}' не існує.",
                statusCode: StatusCodes.Status404NotFound),
            ChangeOutcome.Rejected rejected => Results.ValidationProblem(rejected.Problems),
            ChangeOutcome.Changed changed => Results.Ok(changed.Incident),
            _ => throw new InvalidOperationException("Невідомий результат зміни.")
        };
    }
}
