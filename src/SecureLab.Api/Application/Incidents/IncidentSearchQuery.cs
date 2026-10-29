using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Application.Access;
using SecureLab.Api.Application.Search;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Application.Incidents;

// Як упорядкувати результат: вираз з allowlist і напрям.
public sealed record SearchOrdering(Expression<Func<Incident, object>> Key, bool Descending);

public sealed class IncidentSearchQuery(SecureLabDbContext dbContext, IncidentScope scope)
{
    public const int MaxRows = 50;

    // Allowlist сортування: клієнт обирає лише КЛЮЧ, а вираз LINQ задає сервер.
    // Назва колонки чи SQL від клієнта сюди не доходить. Регістр ключа не важливий.
    // Severity і Status лежать у БД рядками, тому порядок задаємо явним рангом, а не алфавітом.
    private static readonly Dictionary<string, Expression<Func<Incident, object>>> SortKeys =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["createdAtUtc"] = incident => incident.CreatedAtUtc,
            ["severity"] = incident =>
                incident.Severity == IncidentSeverity.Critical ? 0 :
                incident.Severity == IncidentSeverity.High ? 1 :
                incident.Severity == IncidentSeverity.Medium ? 2 : 3,
            ["status"] = incident =>
                incident.Status == IncidentStatus.New ? 0 :
                incident.Status == IncidentStatus.Triaged ? 1 :
                incident.Status == IncidentStatus.InProgress ? 2 :
                incident.Status == IncidentStatus.Resolved ? 3 : 4
        };

    // Напрям за замовчуванням: нові інциденти першими; для severity і status від рангу 0.
    private static readonly HashSet<string> NewestFirstByDefault = new(StringComparer.OrdinalIgnoreCase) { "createdAtUtc" };

    public static SearchOrdering? ResolveOrdering(string? sortBy, string? order, out Dictionary<string, string[]> problems)
    {
        problems = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var key = string.IsNullOrEmpty(sortBy) ? "createdAtUtc" : sortBy;

        if (!SortKeys.TryGetValue(key, out var expression))
        {
            problems["sortBy"] = ["Допустимі значення: createdAtUtc, severity, status."];
        }

        bool descending = NewestFirstByDefault.Contains(key);
        if (!string.IsNullOrEmpty(order))
        {
            if (order.Equals("asc", StringComparison.OrdinalIgnoreCase)) descending = false;
            else if (order.Equals("desc", StringComparison.OrdinalIgnoreCase)) descending = true;
            else problems["order"] = ["Допустимі значення: asc, desc."];
        }

        return problems.Count == 0 ? new SearchOrdering(expression!, descending) : null;
    }

    public async Task<IReadOnlyList<IncidentSearchItemDto>> FindAsync(
        string? text,
        SearchOrdering ordering,
        CallerProfile caller,
        CancellationToken cancellationToken)
    {
        // Ввід стає буквальним шаблоном; далі він іде в LINQ лише як значення.
        var pattern = IlikeText.AnywhereLiteral(text ?? string.Empty);
        // ЛР 4: спершу межа видимості caller, потім текстова умова, сортування і Take(50).
        var visible = scope.VisibleTo(dbContext.Incidents.AsNoTracking(), caller);
        var matches = visible.WhereTitleOrDescriptionLike(pattern);

        var ordered = ordering.Descending
            ? matches.OrderByDescending(ordering.Key)
            : matches.OrderBy(ordering.Key);

        return await ordered
            .ThenBy(incident => incident.Id)
            .Take(MaxRows)
            .Select(incident => new IncidentSearchItemDto(
                incident.Id,
                incident.Title,
                incident.Description,
                incident.Severity.ToString(),
                incident.Status.ToString(),
                incident.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }
}
