using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Application.Access;

// Обмеження запиту за суб'єктом для переліків. Умова потрапляє в сам SQL (WHERE owner_user_id = @p),
// тому стоїть до сортування, Take і GroupBy, а чужі рядки з БД навіть не читаються.
public sealed class IncidentScope(IncidentAccessGuard guard)
{
    public IQueryable<Incident> VisibleTo(IQueryable<Incident> incidents, CallerProfile caller)
    {
        if (guard.SeesEveryIncident(caller))
        {
            return incidents;
        }

        var ownerId = caller.UserId;
        return incidents.Where(incident => incident.OwnerUserId == ownerId);
    }
}
