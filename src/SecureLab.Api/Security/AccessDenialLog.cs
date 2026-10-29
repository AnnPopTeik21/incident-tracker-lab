using SecureLab.Api.Application.Access;

namespace SecureLab.Api.Security;

// Аудит відмов у змінювальних діях над інцидентом (PATCH, DELETE, triage).
// Поля: id суб'єкта з principal, дія, id ресурсу, результат, час сервера UTC і trace id,
// той самий, що в Problem Details відповіді. Імені користувача, cookie, пароля, CSRF-токена
// і тіла запиту тут немає. Відмови читання не пишуться: їх багато, а користі для аудиту мало.
public static partial class AccessDenialLog
{
    public const string Category = "SecureLab.AccessAudit";

    public static void Denied(HttpContext http, Guid subjectId, IncidentAction action, Guid incidentId) =>
        LogDenied(
            http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(Category),
            subjectId,
            NameOf(action),
            incidentId,
            http.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow(),
            AuthAuditLog.TraceOf(http));

    private static string NameOf(IncidentAction action) => action switch
    {
        IncidentAction.EditContent => "incident.edit",
        IncidentAction.Remove => "incident.delete",
        IncidentAction.Triage => "incident.triage",
        _ => "incident.other"
    };

    [LoggerMessage(EventId = 4101, Level = LogLevel.Warning,
        Message = "authorization.denied subjectId={SubjectId} action={Action} resource=incident:{IncidentId} result=denied at={AtUtc:O} trace={TraceId}")]
    private static partial void LogDenied(
        ILogger logger, Guid subjectId, string action, Guid incidentId, DateTimeOffset atUtc, string traceId);
}
