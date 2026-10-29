using SecureLab.Api.Application.Access;
using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Presentation.Endpoints;

// ЛР 4: дочірній ресурс за прямим id. Без перевірки parent це був би другий шлях до чужого інциденту.
public static class IncidentCommentEndpoints
{
    public static IEndpointRouteBuilder MapIncidentCommentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/incident-comments/{id:guid}", GetCommentAsync)
            .WithName("GetIncidentComment")
            .WithTags("Incidents")
            .RequireAuthorization()
            .Produces<IncidentCommentReadDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> GetCommentAsync(
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

        var lookup = await queries.GetCommentAsync(id, cancellationToken);
        if (lookup is null)
        {
            return AccessProblems.CommentMissing(id);
        }

        return guard.DecideComment(caller, lookup.Parent, lookup.IsInternal) == AccessVerdict.Granted
            ? Results.Ok(lookup.Comment)
            : AccessProblems.Forbidden();
    }
}
