using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Application.Incidents;

// Нові значення змісту після Trim. null означає «поле не змінюється».
public sealed record ContentPatch(string? Title, string? Description);

// Правила звичайного редагування. Межі ті самі, що й під час створення (ЛР 2).
public static class IncidentContentRules
{
    public static ContentPatch? Check(
        UpdateIncidentRequest request,
        IncidentSeverity currentSeverity,
        out Dictionary<string, string[]> problems)
    {
        problems = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var title = request.Title?.Trim();
        var description = request.Description?.Trim();

        if (title is null && description is null)
        {
            problems["body"] = ["Передайте title або description."];
            return null;
        }

        if (title is not null && title.Length == 0)
            problems["title"] = ["Заголовок не може бути порожнім."];
        else if (title is not null && title.Length > IncidentCreationRules.TitleMaxLength)
            problems["title"] = [$"Заголовок не довший за {IncidentCreationRules.TitleMaxLength} символів."];

        if (description is not null && description.Length == 0)
            problems["description"] = ["Опис не може бути порожнім."];
        else if (description is not null && description.Length > IncidentCreationRules.DescriptionMaxLength)
            problems["description"] = [$"Опис не довший за {IncidentCreationRules.DescriptionMaxLength} символів."];
        else if (description is not null
                 && currentSeverity is IncidentSeverity.High or IncidentSeverity.Critical
                 && description.Length < IncidentCreationRules.SeriousDescriptionMinLength)
            problems["description"] =
                [$"Для High і Critical опис має містити щонайменше {IncidentCreationRules.SeriousDescriptionMinLength} символів."];

        return problems.Count == 0 ? new ContentPatch(title, description) : null;
    }
}
