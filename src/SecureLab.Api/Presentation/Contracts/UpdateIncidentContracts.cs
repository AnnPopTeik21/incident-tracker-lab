using System.Text.Json.Serialization;

namespace SecureLab.Api.Presentation.Contracts;

// Що власник може змінити звичайним PATCH: лише зміст запису. Severity, Status, OwnerUserId,
// роль і службові часи сюди не входять. Невідоме поле в JSON не ігнорується мовчки:
// Disallow зупиняє такий запит з 400 ще до коду endpoint (контракт A-08b).
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateIncidentRequest(
    string? Title,
    string? Description);

// Відповідь після зміни: поточний стан змісту і службові поля лише для читання.
public sealed record IncidentChangedDto(
    Guid Id,
    string Title,
    string Description,
    string Severity,
    string Status,
    DateTimeOffset UpdatedAtUtc);

// Привілейований перехід New -> Triaged. Клієнт може передати лише коментар до переходу.
// Наступний статус, поточний статус, роль і власника визначає сервер; зайве поле дає 400.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TriageIncidentRequest(string? Note);

public sealed record TriageResultDto(
    Guid Id,
    string PreviousStatus,
    string Status,
    DateTimeOffset ChangedAtUtc);
