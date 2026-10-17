using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace SecureLab.Api.Security;

// Налаштування ліміту входів. Значення за замовчуванням: 4 спроби за 30 секунд з однієї IP-адреси.
public sealed class LoginThrottleOptions
{
    public const string Section = "Auth:LoginThrottle";

    public int PermitLimit { get; set; } = 4;
    public int WindowSeconds { get; set; } = 30;
}

// Обмеження частоти лише для POST /api/auth/login: вбудований rate limiter, фіксоване вікно,
// окремий лічильник для кожної IP-адреси клієнта.
public static class LoginThrottling
{
    public const string PolicyName = "login";
    private const string NoAddressKey = "no-remote-ip";

    public static void AddLoginThrottling(this WebApplicationBuilder builder)
    {
        // Configure читає секцію під час першого звернення, тож тестовий host може задати свої числа.
        builder.Services.Configure<LoginThrottleOptions>(builder.Configuration.GetSection(LoginThrottleOptions.Section));
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(PolicyName, httpContext =>
            {
                var settings = Settings(httpContext);
                // Ключ розділу: лише IP. Ні ім'я з форми, ні пароль ключем не є.
                var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? NoAddressKey;
                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = settings.PermitLimit,
                    Window = TimeSpan.FromSeconds(settings.WindowSeconds),
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
            });
            options.OnRejected = WriteRejectionAsync;
        });
    }

    // 429 з Problem Details і Retry-After. Текст однаковий для будь-якого імені, тому
    // відповідь не підказує, чи існує обліковий запис.
    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait)
            ? (int)Math.Ceiling(wait.TotalSeconds)
            : Settings(http).WindowSeconds;
        http.Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);
        AuthAuditLog.LoginThrottled(http);

        var problems = http.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problems.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Забагато спроб входу",
                Detail = "Спробуйте увійти пізніше."
            }
        });
    }

    private static LoginThrottleOptions Settings(HttpContext http) =>
        http.RequestServices.GetRequiredService<IOptions<LoginThrottleOptions>>().Value;
}
