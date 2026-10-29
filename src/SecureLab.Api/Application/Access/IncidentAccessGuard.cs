using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Application.Access;

// Дія, про яку питають сторожа.
public enum IncidentAction
{
    ReadDetails,
    EditContent,
    Remove,
    Triage
}

// Відповідь сторожа. Endpoint перетворює її на HTTP: Refused -> 403, Absent -> 404.
public enum AccessVerdict
{
    Granted,
    Refused,
    Absent
}

// Що сторож знає про інцидент: власник і поточний статус. null означає «запису немає».
public sealed record IncidentFacts(Guid OwnerUserId, IncidentStatus Status);

// Єдине місце правил матриці docs/access-matrix.md. Endpoint-и не пишуть власних if про роль чи власника,
// вони лише питають сторожа. Administrator окремих прав над інцидентами не має: предметне правило 2-A
// їх не називає, тож для нього діють ті самі правила, що й для Reporter.
public sealed class IncidentAccessGuard
{
    public AccessVerdict Decide(CallerProfile caller, IncidentAction action, IncidentFacts? incident)
    {
        if (incident is null)
        {
            return AccessVerdict.Absent;
        }

        var isOwner = incident.OwnerUserId == caller.UserId;
        var allowed = action switch
        {
            // Власник читає своє, Analyst читає будь-який інцидент для аналізу.
            IncidentAction.ReadDetails => isOwner || caller.IsAnalyst,
            // Зміст редагує лише власник. Роль Analyst права на чужий зміст не дає.
            IncidentAction.EditContent => isOwner,
            // Видалити можна лише власний запис, поки аналітик його ще не взяв у роботу.
            IncidentAction.Remove => isOwner && incident.Status == IncidentStatus.New,
            // Перехід New -> Triaged: лише Analyst. Чи статус саме New, перевіряє сервіс окремо (409).
            IncidentAction.Triage => caller.IsAnalyst,
            _ => false
        };
        return allowed ? AccessVerdict.Granted : AccessVerdict.Refused;
    }

    // Коментар читається через батьківський інцидент: спершу право на parent,
    // потім правило internal. Внутрішню нотатку бачить лише Analyst, навіть не власник.
    public AccessVerdict DecideComment(CallerProfile caller, IncidentFacts? parent, bool isInternal)
    {
        var parentVerdict = Decide(caller, IncidentAction.ReadDetails, parent);
        if (parentVerdict != AccessVerdict.Granted)
        {
            return parentVerdict;
        }

        return isInternal && !CanSeeInternalNotes(caller) ? AccessVerdict.Refused : AccessVerdict.Granted;
    }

    // Правила для переліків (list, search, severity-summary): хто бачить усі записи.
    public bool SeesEveryIncident(CallerProfile caller) => caller.IsAnalyst;

    public bool CanSeeInternalNotes(CallerProfile caller) => caller.IsAnalyst;
}
