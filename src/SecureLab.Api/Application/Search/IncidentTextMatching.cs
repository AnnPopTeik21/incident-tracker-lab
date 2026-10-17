using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Application.Search;

public static class IncidentTextMatching
{
    // Умова «збіг у Title АБО в Description» в одному місці.
    // EF.Functions.ILike стає оператором ILIKE, а pattern іде в PostgreSQL параметром,
    // тому значення не може змінити структуру команди.
    public static IQueryable<Incident> WhereTitleOrDescriptionLike(this IQueryable<Incident> incidents, string pattern) =>
        incidents.Where(incident =>
            EF.Functions.ILike(incident.Title, pattern, IlikeText.MarkText)
            || EF.Functions.ILike(incident.Description, pattern, IlikeText.MarkText));
}
