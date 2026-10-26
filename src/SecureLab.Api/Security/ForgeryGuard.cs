using Microsoft.AspNetCore.Antiforgery;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Security;

// Захист від CSRF для запитів, що змінюють дані від імені користувача з cookie.
// Модель: synchronizer token з ASP.NET Core Antiforgery. Сервер видає пару: службову cookie
// SecureLab.Csrf і токен у тілі відповіді. Змінювальний запит має нести токен у заголовку
// X-CSRF-TOKEN. Чужий сайт не може прочитати відповідь з токеном (CORS не ввімкнено),
// тому не може й підставити заголовок. Токен прив'язаний до користувача з cookie сеансу.
// SameSite=Lax лишається першим шаром; токен його не замінює, а доповнює.
public static class ForgeryGuard
{
    public const string HeaderName = "X-CSRF-TOKEN";
    public const string CookieName = "SecureLab.Csrf";

    public static void AddForgeryGuard(this WebApplicationBuilder builder)
    {
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = HeaderName;
            options.Cookie.Name = CookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });
    }

    // Видача токена. Після входу і після виходу клієнт бере новий токен, бо старий
    // належить попередньому користувачу. Antiforgery сам ставить Cache-Control: no-store.
    public static void MapForgeryTokenEndpoint(this WebApplication app)
    {
        app.MapGet("/api/auth/csrf", (IAntiforgery antiforgery, HttpContext http) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(http);
            return Results.Ok(new CsrfTokenResponse(HeaderName, tokens.RequestToken!));
        })
        .WithTags("Auth")
        .Produces<CsrfTokenResponse>();
    }

    // Фільтр endpoint: без чинного токена запит зупиняється до коду endpoint з 400.
    // Стоїть після автентифікації, тому анонімний POST на захищений endpoint і далі дає 401.
    public static TBuilder RequireForgeryToken<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var antiforgery = http.RequestServices.GetRequiredService<IAntiforgery>();
            if (!await antiforgery.IsRequestValidAsync(http))
            {
                AuthAuditLog.ForgeryRejected(http);
                return Results.Problem(
                    title: "Запит без чинного CSRF-токена",
                    detail: $"Отримайте токен через GET /api/auth/csrf і надішліть його в заголовку {HeaderName}.",
                    statusCode: StatusCodes.Status400BadRequest);
            }
            return await next(context);
        });
}
