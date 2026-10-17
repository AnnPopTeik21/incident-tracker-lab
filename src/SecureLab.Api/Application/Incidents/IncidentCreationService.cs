using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Application.Incidents;

public abstract record CreationOutcome
{
    public sealed record Rejected(Dictionary<string, string[]> Problems) : CreationOutcome;
    public sealed record DuplicateActiveTitle : CreationOutcome;
    public sealed record Stored(IncidentCreatedDto Incident) : CreationOutcome;
}

public sealed class IncidentCreationService(
    SecureLabDbContext dbContext,
    TimeProvider clock,
    ILogger<IncidentCreationService> logger)
{
    public async Task<CreationOutcome> CreateAsync(CreateIncidentRequest request, CancellationToken cancellationToken)
    {
        // Поточний UTC-час фіксуємо один раз на початку обробки.
        var nowUtc = clock.GetUtcNow();

        var draft = IncidentCreationRules.Check(request, nowUtc, out var problems);
        if (draft is null)
        {
            // 400 повертається ще до будь-якого звернення до БД.
            return new CreationOutcome.Rejected(problems);
        }

        // Предметне правило: той самий title (після Trim, з урахуванням регістру) не можна
        // відкрити вдруге, поки наявний інцидент не Closed. "=" у PostgreSQL чутливий до регістру.
        var duplicateIsOpen = await dbContext.Incidents.AnyAsync(
            incident => incident.Title == draft.Title && incident.Status != IncidentStatus.Closed,
            cancellationToken);
        if (duplicateIsOpen)
        {
            logger.LogInformation("Incident creation refused: active title already exists");
            return new CreationOutcome.DuplicateActiveTitle();
        }

        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            // Тимчасовий demo context ЛР 2: власника задає сервер, а не body.
            // У ЛР 3 тут буде verified principal.
            OwnerUserId = DbSeeder.AliceId,
            Title = draft.Title,
            Description = draft.Description,
            Severity = draft.Severity,
            Status = IncidentStatus.New,
            OccurredAtUtc = draft.OccurredAtUtc,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };
        dbContext.Incidents.Add(incident);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Incident {IncidentId} created with severity {Severity}", incident.Id, incident.Severity);

        return new CreationOutcome.Stored(new IncidentCreatedDto(
            incident.Id,
            incident.Title,
            incident.Severity.ToString(),
            incident.Status.ToString(),
            incident.OccurredAtUtc,
            incident.CreatedAtUtc));
    }
}
