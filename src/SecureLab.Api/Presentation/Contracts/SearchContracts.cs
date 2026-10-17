namespace SecureLab.Api.Presentation.Contracts;

// Один рядок результату пошуку. Ті самі шість полів, що й у наданому scaffold:
// виправлення не змінює форму відповіді для клієнта. OwnerUserId і зв'язки не віддаються.
public sealed record IncidentSearchItemDto(
    Guid Id,
    string Title,
    string Description,
    string Severity,
    string Status,
    DateTimeOffset CreatedAtUtc);
