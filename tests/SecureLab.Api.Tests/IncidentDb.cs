using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;

namespace SecureLab.Api.Tests;

// Читання фактичного стану таблиці incidents з тесту. API назовні OwnerUserId не віддає.
internal static class IncidentDb
{
    public static async Task<int> CountByTitleAsync(SecureLabApiFactory factory, string title)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        return await db.Incidents.CountAsync(incident => incident.Title == title);
    }

    public static async Task<Guid> OwnerOfAsync(SecureLabApiFactory factory, Guid incidentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        return await db.Incidents.AsNoTracking()
            .Where(incident => incident.Id == incidentId)
            .Select(incident => incident.OwnerUserId)
            .SingleAsync();
    }

    // ЛР 4: знімок стану запису для порівняння до/після відмови. Читає БД напряму,
    // тому не залежить від того, чи бачить тестовий користувач цей інцидент через API.
    public static async Task<IncidentRow?> SnapshotAsync(SecureLabApiFactory factory, Guid incidentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        return await db.Incidents.AsNoTracking()
            .Where(incident => incident.Id == incidentId)
            .Select(incident => new IncidentRow(
                incident.Title,
                incident.Description,
                incident.Severity.ToString(),
                incident.Status.ToString(),
                incident.OwnerUserId,
                incident.UpdatedAtUtc))
            .SingleOrDefaultAsync();
    }

    public static async Task<List<(Guid ChangedBy, string From, string To)>> HistoryOfAsync(
        SecureLabApiFactory factory, Guid incidentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        var rows = await db.IncidentStatusHistory.AsNoTracking()
            .Where(row => row.IncidentId == incidentId)
            .Select(row => new { row.ChangedByUserId, row.OldStatus, row.NewStatus })
            .ToListAsync();
        return rows.Select(row => (row.ChangedByUserId, row.OldStatus.ToString(), row.NewStatus.ToString())).ToList();
    }

    public static async Task DeleteAsync(SecureLabApiFactory factory, IReadOnlyCollection<Guid> ids)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        await db.Incidents.Where(incident => ids.Contains(incident.Id)).ExecuteDeleteAsync();
    }
}

internal sealed record IncidentRow(
    string Title,
    string Description,
    string Severity,
    string Status,
    Guid OwnerUserId,
    DateTimeOffset UpdatedAtUtc);
