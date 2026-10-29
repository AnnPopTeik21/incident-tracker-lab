using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Application.Access;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Application.Incidents;

public abstract record ChangeOutcome
{
    public sealed record Missing : ChangeOutcome;
    public sealed record Denied(IncidentAction Action) : ChangeOutcome;
    public sealed record Rejected(Dictionary<string, string[]> Problems) : ChangeOutcome;
    public sealed record WrongStatus(IncidentStatus Current) : ChangeOutcome;
    public sealed record Changed(IncidentChangedDto Incident) : ChangeOutcome;
    public sealed record Removed : ChangeOutcome;
    public sealed record Triaged(TriageResultDto Result) : ChangeOutcome;
}

// Змінювальні дії над інцидентом. Порядок у кожному методі однаковий:
// tracked entity (без AsNoTracking) -> рішення сторожа -> перевірка вмісту або статусу -> зміна -> SaveChanges.
// До рішення сторожа жодне поле entity не змінюється.
public sealed class IncidentChangeService(
    SecureLabDbContext dbContext,
    IncidentAccessGuard guard,
    TimeProvider clock,
    ILogger<IncidentChangeService> logger)
{
    public const int TriageNoteMaxLength = 500;

    public async Task<ChangeOutcome> ChangeContentAsync(
        CallerProfile caller,
        Guid incidentId,
        UpdateIncidentRequest request,
        CancellationToken cancellationToken)
    {
        var incident = await LoadTrackedAsync(incidentId, cancellationToken);
        var refusal = Ask(caller, IncidentAction.EditContent, incident);
        if (refusal is not null)
        {
            return refusal;
        }

        var patch = IncidentContentRules.Check(request, incident!.Severity, out var problems);
        if (patch is null)
        {
            return new ChangeOutcome.Rejected(problems);
        }

        incident.Title = patch.Title ?? incident.Title;
        incident.Description = patch.Description ?? incident.Description;
        incident.UpdatedAtUtc = clock.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Incident {IncidentId} content changed", incident.Id);

        return new ChangeOutcome.Changed(new IncidentChangedDto(
            incident.Id,
            incident.Title,
            incident.Description,
            incident.Severity.ToString(),
            incident.Status.ToString(),
            incident.UpdatedAtUtc));
    }

    public async Task<ChangeOutcome> RemoveAsync(
        CallerProfile caller,
        Guid incidentId,
        CancellationToken cancellationToken)
    {
        var incident = await LoadTrackedAsync(incidentId, cancellationToken);
        var refusal = Ask(caller, IncidentAction.Remove, incident);
        if (refusal is not null)
        {
            return refusal;
        }

        // Коментарі та історію статусів БД видаляє каскадом.
        dbContext.Incidents.Remove(incident!);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Incident {IncidentId} removed", incidentId);
        return new ChangeOutcome.Removed();
    }

    public async Task<ChangeOutcome> TriageAsync(
        CallerProfile caller,
        Guid incidentId,
        TriageIncidentRequest? request,
        CancellationToken cancellationToken)
    {
        var incident = await LoadTrackedAsync(incidentId, cancellationToken);
        var refusal = Ask(caller, IncidentAction.Triage, incident);
        if (refusal is not null)
        {
            return refusal;
        }

        var note = request?.Note?.Trim();
        if (note is { Length: > TriageNoteMaxLength })
        {
            return new ChangeOutcome.Rejected(new Dictionary<string, string[]>
            {
                ["note"] = [$"Коментар до переходу не довший за {TriageNoteMaxLength} символів."]
            });
        }

        // Поточний статус береться з БД, наступний задає сервер. Клієнт статус не передає.
        if (incident!.Status != IncidentStatus.New)
        {
            return new ChangeOutcome.WrongStatus(incident.Status);
        }

        var now = clock.GetUtcNow();
        incident.Status = IncidentStatus.Triaged;
        incident.UpdatedAtUtc = now;
        dbContext.IncidentStatusHistory.Add(new IncidentStatusHistory
        {
            Id = Guid.NewGuid(),
            IncidentId = incident.Id,
            ChangedByUserId = caller.UserId,
            OldStatus = IncidentStatus.New,
            NewStatus = IncidentStatus.Triaged,
            Note = string.IsNullOrEmpty(note) ? "Первинну оцінку виконано." : note,
            CreatedAtUtc = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Incident {IncidentId} triaged", incident.Id);

        return new ChangeOutcome.Triaged(new TriageResultDto(
            incident.Id, nameof(IncidentStatus.New), incident.Status.ToString(), now));
    }

    private Task<Incident?> LoadTrackedAsync(Guid incidentId, CancellationToken cancellationToken) =>
        dbContext.Incidents.SingleOrDefaultAsync(row => row.Id == incidentId, cancellationToken);

    // null означає «можна продовжувати». Інакше готовий результат відмови.
    private ChangeOutcome? Ask(CallerProfile caller, IncidentAction action, Incident? incident)
    {
        var facts = incident is null ? null : new IncidentFacts(incident.OwnerUserId, incident.Status);
        return guard.Decide(caller, action, facts) switch
        {
            AccessVerdict.Granted => null,
            AccessVerdict.Absent => new ChangeOutcome.Missing(),
            _ => new ChangeOutcome.Denied(action)
        };
    }
}
