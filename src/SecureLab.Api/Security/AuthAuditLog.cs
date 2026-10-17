using System.Diagnostics;

namespace SecureLab.Api.Security;

// Події автентифікації. У повідомлення потрапляють лише тип події, результат, час UTC,
// userId після успіху і trace id. Ім'я з форми, пароль, cookie, hash і тіло запиту сюди не йдуть.
public static partial class AuthAuditLog
{
    public const string Category = "SecureLab.AuthAudit";

    public static void LoginSucceeded(HttpContext http, Guid userId) =>
        LogLoginSucceeded(For(http), userId, Now(http), TraceOf(http));

    public static void LoginRejected(HttpContext http) =>
        LogLoginRejected(For(http), Now(http), TraceOf(http));

    public static void LoginThrottled(HttpContext http) =>
        LogLoginThrottled(For(http), Now(http), TraceOf(http));

    public static void SignedOut(HttpContext http, Guid? userId) =>
        LogSignedOut(For(http), userId, Now(http), TraceOf(http));

    public static void SignUpCompleted(HttpContext http, Guid userId) =>
        LogSignUpCompleted(For(http), userId, Now(http), TraceOf(http));

    public static void SignUpRefused(HttpContext http, string reason) =>
        LogSignUpRefused(For(http), reason, Now(http), TraceOf(http));

    // Той самий формат, що й traceId у Problem Details: подію легко знайти за відповіддю.
    public static string TraceOf(HttpContext http) => Activity.Current?.Id ?? http.TraceIdentifier;

    private static ILogger For(HttpContext http) =>
        http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(Category);

    private static DateTimeOffset Now(HttpContext http) =>
        http.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();

    [LoggerMessage(EventId = 3101, Level = LogLevel.Information,
        Message = "auth.login result=success userId={UserId} at={AtUtc:O} trace={TraceId}")]
    private static partial void LogLoginSucceeded(ILogger logger, Guid userId, DateTimeOffset atUtc, string traceId);

    [LoggerMessage(EventId = 3102, Level = LogLevel.Warning,
        Message = "auth.login result=rejected at={AtUtc:O} trace={TraceId}")]
    private static partial void LogLoginRejected(ILogger logger, DateTimeOffset atUtc, string traceId);

    [LoggerMessage(EventId = 3103, Level = LogLevel.Warning,
        Message = "auth.login result=throttled at={AtUtc:O} trace={TraceId}")]
    private static partial void LogLoginThrottled(ILogger logger, DateTimeOffset atUtc, string traceId);

    [LoggerMessage(EventId = 3104, Level = LogLevel.Information,
        Message = "auth.logout result=done userId={UserId} at={AtUtc:O} trace={TraceId}")]
    private static partial void LogSignedOut(ILogger logger, Guid? userId, DateTimeOffset atUtc, string traceId);

    [LoggerMessage(EventId = 3105, Level = LogLevel.Information,
        Message = "auth.signup result=created userId={UserId} at={AtUtc:O} trace={TraceId}")]
    private static partial void LogSignUpCompleted(ILogger logger, Guid userId, DateTimeOffset atUtc, string traceId);

    [LoggerMessage(EventId = 3106, Level = LogLevel.Information,
        Message = "auth.signup result=refused reason={Reason} at={AtUtc:O} trace={TraceId}")]
    private static partial void LogSignUpRefused(ILogger logger, string reason, DateTimeOffset atUtc, string traceId);
}
