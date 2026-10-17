namespace SecureLab.Api.Presentation.Contracts;

// Що клієнт МОЖЕ надіслати. Id, OwnerUserId, Status і audit-часи сюди навмисно не входять:
// їх визначає сервер. Nullable чесно показує, що клієнт може поле не надіслати;
// обов'язковість перевіряє IncidentCreationRules, а не анотація.
public sealed record CreateIncidentRequest(
    string? Title,
    string? Description,
    string? Severity,
    DateTimeOffset? OccurredAtUtc);

// Що API повертає після створення: лише ці шість полів, без owner і без опису.
public sealed record IncidentCreatedDto(
    Guid Id,
    string Title,
    string Severity,
    string Status,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset CreatedAtUtc);
