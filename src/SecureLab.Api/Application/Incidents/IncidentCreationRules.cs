using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Application.Incidents;

// Перевірені й нормалізовані значення, з якими далі працює сервер.
public sealed record IncidentDraft(
    string Title,
    string Description,
    IncidentSeverity Severity,
    DateTimeOffset OccurredAtUtc);

// Серверні правила контракту створення (baseline 2-A). Без доступу до БД.
public static class IncidentCreationRules
{
    public const int TitleMaxLength = 160;
    public const int DescriptionMaxLength = 4000;
    public const int SeriousDescriptionMinLength = 40;
    public static readonly TimeSpan ClockSkewAllowance = TimeSpan.FromMinutes(5);

    public static IncidentDraft? Check(
        CreateIncidentRequest request,
        DateTimeOffset serverNowUtc,
        out Dictionary<string, string[]> problems)
    {
        problems = new Dictionary<string, string[]>(StringComparer.Ordinal);

        // Trim() робимо один раз; ці самі значення йдуть у всі перевірки, пошук дубліката і entity.
        var title = request.Title?.Trim() ?? string.Empty;
        var description = request.Description?.Trim() ?? string.Empty;

        if (title.Length == 0)
            problems["title"] = ["Заголовок обов'язковий."];
        else if (title.Length > TitleMaxLength)
            problems["title"] = [$"Заголовок не довший за {TitleMaxLength} символів."];

        if (description.Length == 0)
            problems["description"] = ["Опис обов'язковий."];
        else if (description.Length > DescriptionMaxLength)
            problems["description"] = [$"Опис не довший за {DescriptionMaxLength} символів."];

        var severityKnown = TryReadSeverity(request.Severity, out var severity);
        if (!severityKnown)
            problems["severity"] = ["Допустимі значення: Low, Medium, High, Critical."];

        if (request.OccurredAtUtc is not { } occurredAt)
            problems["occurredAtUtc"] = ["Час інциденту обов'язковий."];
        else if (occurredAt > serverNowUtc + ClockSkewAllowance)
            problems["occurredAtUtc"] = ["Час інциденту не може бути пізніше за поточний більш ніж на 5 хвилин."];

        // Cross-field правило 2-A: серйозний інцидент потребує змістовного опису.
        if (severityKnown
            && severity is IncidentSeverity.High or IncidentSeverity.Critical
            && description.Length is > 0 and < SeriousDescriptionMinLength
            && !problems.ContainsKey("description"))
        {
            problems["description"] =
                [$"Для High і Critical опис має містити щонайменше {SeriousDescriptionMinLength} символів."];
        }

        return problems.Count == 0
            ? new IncidentDraft(title, description, severity, request.OccurredAtUtc!.Value.ToUniversalTime())
            : null;
    }

    // Лише назва з enum. Цифри і кому відсікаємо до TryParse: "2" він прочитав би як High,
    // а "Medium,High" як прапорці 1|2 = Critical, і обидва пройшли б Enum.IsDefined.
    private static bool TryReadSeverity(string? raw, out IncidentSeverity severity)
    {
        severity = default;
        if (string.IsNullOrWhiteSpace(raw)
            || raw.Any(symbol => char.IsDigit(symbol) || symbol == ',' || char.IsWhiteSpace(symbol)))
        {
            return false;
        }

        return Enum.TryParse(raw, ignoreCase: true, out severity)
            && Enum.IsDefined(severity);
    }
}
