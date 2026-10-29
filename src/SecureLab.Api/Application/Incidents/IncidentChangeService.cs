using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Application.Incidents;

public abstract record ChangeOutcome
{
    public sealed record Missing : ChangeOutcome;
    public sealed record Rejected(Dictionary<string, string[]> Problems) : ChangeOutcome;
    public sealed record Changed(IncidentChangedDto Incident) : ChangeOutcome;
}

// Змінювальні дії над інцидентом. Entity завантажується БЕЗ AsNoTracking: інакше EF не побачить змін.
public sealed class IncidentChangeService(
    SecureLabDbContext dbContext,
    TimeProvider clock,
    ILogger<IncidentChangeService> logger)
{
    public async Task<ChangeOutcome> ChangeContentAsync(
        Guid incidentId,
        UpdateIncidentRequest request,
        CancellationToken cancellationToken)
    {
        // Запис шукається лише за id з URL. Хто надіслав запит, тут не враховано.
        var incident = await dbContext.Incidents
            .SingleOrDefaultAsync(row => row.Id == incidentId, cancellationToken);
        if (incident is null)
        {
            return new ChangeOutcome.Missing();
        }

        var patch = IncidentContentRules.Check(request, incident.Severity, out var problems);
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
}
