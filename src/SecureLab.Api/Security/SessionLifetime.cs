using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace SecureLab.Api.Security;

// Строк дії сеансу. За замовчуванням 20 хвилин без активності: квиток у cookie містить
// час завершення, і сервер сам відкидає прострочений квиток, навіть якщо браузер його надіслав.
public sealed class SessionLifetimeOptions
{
    public const string Section = "Auth:Session";

    public int IdleMinutes { get; set; } = 20;
}

public static class SessionLifetime
{
    public static void AddSessionLifetime(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<SessionLifetimeOptions>()
            .Bind(builder.Configuration.GetSection(SessionLifetimeOptions.Section))
            .Validate(options => options.IdleMinutes is >= 1 and <= 120,
                "Auth:Session:IdleMinutes має бути від 1 до 120.")
            .ValidateOnStart();

        // Налаштування застосовується до cookie Identity під час першого звернення,
        // тому тестовий host може задати свій строк через конфігурацію.
        builder.Services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<IOptions<SessionLifetimeOptions>>((cookie, session) =>
            {
                cookie.ExpireTimeSpan = TimeSpan.FromMinutes(session.Value.IdleMinutes);
                // Sliding: якщо минуло більше половини строку, сервер видає квиток заново.
                // Активна людина не вилітає посеред роботи, а покинутий сеанс гасне сам.
                cookie.SlidingExpiration = true;
            });
    }
}
