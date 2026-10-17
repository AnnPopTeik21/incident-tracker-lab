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

    public static async Task DeleteAsync(SecureLabApiFactory factory, IReadOnlyCollection<Guid> ids)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        await db.Incidents.Where(incident => ids.Contains(incident.Id)).ExecuteDeleteAsync();
    }
}
