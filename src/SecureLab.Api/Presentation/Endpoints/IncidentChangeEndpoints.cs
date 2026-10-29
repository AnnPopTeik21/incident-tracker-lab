using SecureLab.Api.Application.Access;
using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Presentation.Contracts;
using SecureLab.Api.Security;

namespace SecureLab.Api.Presentation.Endpoints;

// ЛР 4: змінювальні маршрути інциденту. Потрібні вхід і CSRF-токен, як для POST у ЛР 3.
// Право на конкретний запис вирішує IncidentAccessGuard усередині IncidentChangeService.
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
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", RemoveAsync)
            .WithName("RemoveIncident")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/triage", TriageAsync)
            .WithName("TriageIncident")
            .Produces<TriageResultDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> ChangeContentAsync(
        Guid id,
        UpdateIncidentRequest request,
        HttpContext httpContext,
        IncidentChangeService changes,
        CancellationToken cancellationToken)
    {
        if (!CallerProfile.TryRead(httpContext.User, out var caller))
        {
            return AccessProblems.NoVerifiedCaller();
        }

        return ToHttp(await changes.ChangeContentAsync(caller, id, request, cancellationToken), httpContext, caller, id);
    }

    private static async Task<IResult> RemoveAsync(
        Guid id,
        HttpContext httpContext,
        IncidentChangeService changes,
        CancellationToken cancellationToken)
    {
        if (!CallerProfile.TryRead(httpContext.User, out var caller))
        {
            return AccessProblems.NoVerifiedCaller();
        }

        return ToHttp(await changes.RemoveAsync(caller, id, cancellationToken), httpContext, caller, id);
    }

    private static async Task<IResult> TriageAsync(
        Guid id,
        TriageIncidentRequest? request,
        HttpContext httpContext,
        IncidentChangeService changes,
        CancellationToken cancellationToken)
    {
        if (!CallerProfile.TryRead(httpContext.User, out var caller))
        {
            return AccessProblems.NoVerifiedCaller();
        }

        return ToHttp(await changes.TriageAsync(caller, id, request, cancellationToken), httpContext, caller, id);
    }

    // Один перетворювач результату на HTTP для всіх трьох маршрутів: однакові 403 і 404 скрізь.
    private static IResult ToHttp(ChangeOutcome outcome, HttpContext httpContext, CallerProfile caller, Guid id)
    {
        switch (outcome)
        {
            case ChangeOutcome.Denied denied:
                // Подія аудиту пишеться саме тут, у гілці відмови, з тим самим trace id, що піде у відповідь.
                AccessDenialLog.Denied(httpContext, caller.UserId, denied.Action, id);
                return AccessProblems.Forbidden();
            case ChangeOutcome.Missing:
                return AccessProblems.IncidentMissing(id);
            case ChangeOutcome.Rejected rejected:
                return Results.ValidationProblem(rejected.Problems);
            case ChangeOutcome.WrongStatus wrong:
                return Results.Problem(
                    title: "Перехід зі стану неможливий",
                    detail: $"Triage виконується лише для статусу New, поточний статус {wrong.Current}.",
                    statusCode: StatusCodes.Status409Conflict);
            case ChangeOutcome.Changed changed:
                return Results.Ok(changed.Incident);
            case ChangeOutcome.Removed:
                return Results.NoContent();
            case ChangeOutcome.Triaged triaged:
                return Results.Ok(triaged.Result);
            default:
                throw new InvalidOperationException("Невідомий результат зміни.");
        }
    }
}
