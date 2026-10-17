namespace SecureLab.Api.Presentation.Contracts;

/// <summary>
/// Один рядок підсумку: рівень критичності та кількість інцидентів із ним.
/// Контракт навмисно містить лише два поля і не повторює entity Incident.
/// </summary>
public sealed record SeverityCountDto(
    string Severity,
    int Count);
