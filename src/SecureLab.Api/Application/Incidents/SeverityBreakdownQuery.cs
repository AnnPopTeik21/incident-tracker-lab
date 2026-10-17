using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Application.Incidents;

/// <summary>
/// Рахує інциденти за рівнем критичності.
/// Політика: повний перелік рівнів. Відповідь завжди має всі значення enum IncidentSeverity,
/// рівень без інцидентів повертається з count = 0.
/// Порядок: як оголошено в enum (Low, Medium, High, Critical), тобто за зростанням критичності.
/// </summary>
public sealed class SeverityBreakdownQuery(
    SecureLabDbContext dbContext,
    ILogger<SeverityBreakdownQuery> logger)
{
    public async Task<IReadOnlyList<SeverityCountDto>> CountBySeverityAsync(
        IncidentStatus? statusFilter,
        CancellationToken cancellationToken)
    {
        var incidents = dbContext.Incidents.AsNoTracking();
        if (statusFilter is { } requiredStatus)
        {
            incidents = incidents.Where(incident => incident.Status == requiredStatus);
        }

        // База повертає лише ті рівні, які реально є в таблиці.
        var countedInDatabase = await incidents
            .GroupBy(incident => incident.Severity)
            .Select(group => new { Level = group.Key, Amount = group.Count() })
            .ToListAsync(cancellationToken);

        var amountByLevel = countedInDatabase.ToDictionary(row => row.Level, row => row.Amount);

        // Доповнюємо відсутні рівні нулями вже в пам'яті.
        // Enum.GetValues повертає значення в порядку оголошення, тому сортувати рядки не потрібно.
        var breakdown = Enum.GetValues<IncidentSeverity>()
            .Select(level => new SeverityCountDto(
                level.ToString(),
                amountByLevel.GetValueOrDefault(level)))
            .ToList();

        logger.LogInformation(
            "Severity breakdown ready: {LevelCount} levels, {IncidentTotal} incidents, status filter {StatusFilter}",
            breakdown.Count,
            breakdown.Sum(row => row.Count),
            statusFilter?.ToString() ?? "none");

        return breakdown;
    }
}
