using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using SecureLab.Api.Application.Accounts;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;
using SecureLab.Api.Security;

namespace SecureLab.Api.Scaffolding;

public static class Lab03Identity
{
    public static void AddLab03Identity(this WebApplicationBuilder builder)
    {
        builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 10;
        }).AddEntityFrameworkStores<SecureLabDbContext>().AddDefaultTokenProviders();
        builder.Services.AddAuthorization();
        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });
    }

    public static void MapLab03Identity(this WebApplication app)
    {
        app.MapPost("/api/auth/register", SignUpAsync)
            .WithTags("Auth")
            .Produces<SignUpResult>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);
        // Відмова однакова для невідомого імені й хибного пароля: той самий 401 без причини.
        // Ліміт спроб (політика "login") стоїть перед кодом endpoint.
        app.MapPost("/api/auth/login", async (LoginInput input, SignInManager<ApplicationUser> signIn, HttpContext http) =>
        {
            if (string.IsNullOrWhiteSpace(input.UserName) || string.IsNullOrEmpty(input.Password))
            {
                AuthAuditLog.LoginRejected(http);
                return Results.Unauthorized();
            }
            var result = await signIn.PasswordSignInAsync(input.UserName, input.Password, false, false);
            if (!result.Succeeded)
            {
                AuthAuditLog.LoginRejected(http);
                return Results.Unauthorized();
            }
            var user = await signIn.UserManager.FindByNameAsync(input.UserName);
            AuthAuditLog.LoginSucceeded(http, user?.Id ?? Guid.Empty);
            return Results.NoContent();
        }).RequireRateLimiting(LoginThrottling.PolicyName);
        // ЛР 3, добрий рівень: стандартний sign-out Identity. Відповідь видаляє cookie через Set-Cookie.
        // Повторний logout без cookie теж дає 204: вийти вдруге не помилка.
        app.MapPost("/api/auth/logout", async (SignInManager<ApplicationUser> signIn, HttpContext http) =>
        {
            Guid? userId = VerifiedUser.TryGetId(http.User, out var id) ? id : null;
            await signIn.SignOutAsync();
            AuthAuditLog.SignedOut(http, userId);
            return Results.NoContent();
        }).WithTags("Auth");
        // ЛР 3: профіль лише з перевіреного principal; заголовок X-Demo-UserId більше нічого не означає.
        app.MapGet("/api/me", GetCurrentUser)
            .WithTags("Auth")
            .Produces<MeResponse>()
            .RequireAuthorization();
        // Приклад лише mechanics: не повертає identity і не реалізує оцінювану operation.
        app.MapGet("/api/auth/session-check", () => Results.NoContent()).RequireAuthorization();
    }

    private static async Task<IResult> SignUpAsync(
        SignUpRequest request,
        UserManager<ApplicationUser> users,
        HttpContext http)
    {
        var draft = SignUpRules.Check(request, out var problems);
        if (draft is null)
        {
            AuthAuditLog.SignUpRefused(http, "format");
            return Results.ValidationProblem(problems);
        }

        // PasswordHash рахує UserManager; код endpoint пароль не обробляє і не зберігає.
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = draft.UserName, Email = draft.Email, DisplayName = draft.DisplayName
        };
        var result = await users.CreateAsync(user, draft.Password);
        if (!result.Succeeded)
        {
            var refusal = MapIdentityRefusal(result, out var reason);
            AuthAuditLog.SignUpRefused(http, reason);
            return refusal;
        }

        var role = await users.AddToRoleAsync(user, DbSeeder.ReporterRole);
        if (!role.Succeeded) throw new InvalidOperationException("Default role assignment failed.");
        AuthAuditLog.SignUpCompleted(http, user.Id);
        return Results.Created("/api/me", new SignUpResult(user.Id, draft.UserName, draft.DisplayName));
    }

    // Контракт відмов: зайняте ім'я або email дає 409 з однаковим текстом для обох випадків,
    // слабкий пароль чи недозволений формат дає 400 з ключем поля. Пароль у відповідь не потрапляє.
    private static IResult MapIdentityRefusal(IdentityResult result, out string reason)
    {
        reason = "identity-policy";
        var codes = result.Errors.Select(error => error.Code).ToHashSet(StringComparer.Ordinal);
        var problems = new Dictionary<string, string[]>();
        if (codes.Any(code => code.StartsWith("Password", StringComparison.Ordinal)))
        {
            problems["password"] =
                ["Пароль не відповідає вимогам: від 10 символів, велика і мала латинські літери, цифра та інший символ."];
        }
        if (codes.Contains("InvalidUserName"))
        {
            problems["userName"] = ["Ім'я користувача містить недозволені символи."];
        }
        if (codes.Contains("InvalidEmail"))
        {
            problems["email"] = ["Потрібна коректна адреса email."];
        }
        if (problems.Count > 0)
        {
            return Results.ValidationProblem(problems);
        }
        if (codes.Contains("DuplicateUserName") || codes.Contains("DuplicateEmail"))
        {
            reason = "duplicate";
            return Results.Problem(
                title: "Реєстрацію не завершено",
                detail: "Обліковий запис з таким ім'ям користувача або email уже існує.",
                statusCode: StatusCodes.Status409Conflict);
        }
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["registration"] = ["Реєстрацію не завершено. Перевірте дані форми."]
        });
    }

    private static IResult GetCurrentUser(HttpContext httpContext)
    {
        // Сюди доходить лише автентифікований запит. Якщо claim немає, це помилка конфігурації,
        // а не привід шукати id у query, body чи заголовку.
        if (!VerifiedUser.TryGetId(httpContext.User, out var verifiedUserId))
        {
            return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);
        }

        var userName = httpContext.User.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
        var roles = httpContext.User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).Order().ToArray();
        return Results.Ok(new MeResponse(verifiedUserId, userName, roles));
    }
}

public sealed record LoginInput(string? UserName, string? Password);
