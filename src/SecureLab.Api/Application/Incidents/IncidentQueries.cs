using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Application.Access;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Application.Incidents;

// Коментар разом із фактами його батьківського інциденту: сторож вирішує за parent.
public sealed record CommentLookup(IncidentFacts Parent, bool IsInternal, IncidentCommentReadDto Comment);

public sealed class IncidentQueries(
    SecureLabDbContext dbContext,
    IncidentScope scope,
    ILogger<IncidentQueries> logger)
{
    // ЛР 4: перелік обмежено тим, що дозволено бачити caller (IncidentScope).
    public async Task<IReadOnlyList<IncidentListItemResponse>> GetListAsync(
        IncidentStatus? status,
        CallerProfile caller,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading incidents with status filter {Status}", status);

        var query = scope.VisibleTo(dbContext.Incidents.AsNoTracking(), caller);
        if (status is not null)
        {
            query = query.Where(incident => incident.Status == status);
        }

        return await query
            .OrderByDescending(incident => incident.CreatedAtUtc)
            .Select(incident => new IncidentListItemResponse(
                incident.Id,
                incident.Title,
                incident.Severity.ToString(),
                incident.Status.ToString(),
                incident.OccurredAtUtc,
                incident.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    // Лише власник і статус: цього досить сторожу, щоб ухвалити рішення до читання змісту.
    public Task<IncidentFacts?> GetFactsAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Incidents
            .AsNoTracking()
            .Where(incident => incident.Id == id)
            .Select(incident => new IncidentFacts(incident.OwnerUserId, incident.Status))
            .SingleOrDefaultAsync(cancellationToken);

    // Викликається лише після дозволу. includeInternal = true тільки для Analyst.
    public Task<IncidentDetailsResponse?> GetDetailsAsync(
        Guid id,
        bool includeInternal,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading incident {IncidentId}", id);

        return dbContext.Incidents
            .AsNoTracking()
            .Where(incident => incident.Id == id)
            .Select(incident => new IncidentDetailsResponse(
                incident.Id,
                incident.Title,
                incident.Description,
                incident.Severity.ToString(),
                incident.Status.ToString(),
                incident.OccurredAtUtc,
                incident.CreatedAtUtc,
                incident.Owner.DisplayName,
                incident.Comments
                    .Where(comment => includeInternal || !comment.IsInternal)
                    .OrderBy(comment => comment.CreatedAtUtc)
                    .Select(comment => new IncidentCommentResponse(
                        comment.Id,
                        comment.Author.DisplayName,
                        comment.Text,
                        comment.CreatedAtUtc))
                    .ToList()))
            .SingleOrDefaultAsync(cancellationToken);
    }

    // Непрямий шлях: коментар завантажується разом із власником і статусом батьківського інциденту
    // одним запитом. Рішення приймає той самий сторож, що й для details.
    public Task<CommentLookup?> GetCommentAsync(Guid commentId, CancellationToken cancellationToken) =>
        dbContext.IncidentComments
            .AsNoTracking()
            .Where(comment => comment.Id == commentId)
            .Select(comment => new CommentLookup(
                new IncidentFacts(comment.Incident.OwnerUserId, comment.Incident.Status),
                comment.IsInternal,
                new IncidentCommentReadDto(
                    comment.Id,
                    comment.IncidentId,
                    comment.Author.DisplayName,
                    comment.Text,
                    comment.CreatedAtUtc)))
            .SingleOrDefaultAsync(cancellationToken);
}
