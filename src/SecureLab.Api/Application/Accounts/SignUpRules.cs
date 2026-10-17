using System.Net.Mail;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Application.Accounts;

// Нормалізовані дані реєстрації після перевірки формату. Пароль не змінюється.
public sealed record SignUpDraft(string UserName, string Email, string DisplayName, string Password);

// Перевірка формату реєстрації до звернення до Identity. Межі взято з моделі (DisplayName 120,
// UserName і Email 256) і звужено для навчального контракту.
public static class SignUpRules
{
    public const int UserNameMin = 4;
    public const int UserNameMax = 40;
    public const int EmailMax = 120;
    public const int DisplayNameMin = 2;
    public const int DisplayNameMax = 80;
    public const int PasswordMax = 128;

    public static SignUpDraft? Check(SignUpRequest request, out Dictionary<string, string[]> problems)
    {
        problems = [];

        // Trim лише для імені, email і відображуваного імені. Пароль лишається як є.
        var userName = request.UserName?.Trim() ?? string.Empty;
        var email = request.Email?.Trim() ?? string.Empty;
        var displayName = request.DisplayName?.Trim() ?? string.Empty;
        var password = request.Password ?? string.Empty;

        if (userName.Length is < UserNameMin or > UserNameMax || !userName.All(IsUserNameSymbol))
        {
            problems["userName"] =
                [$"Ім'я користувача: {UserNameMin}–{UserNameMax} символів, латинські літери, цифри, крапка, дефіс або підкреслення."];
        }

        if (email.Length is 0 or > EmailMax || !LooksLikeEmail(email))
        {
            problems["email"] = [$"Потрібна коректна адреса email довжиною до {EmailMax} символів."];
        }

        if (displayName.Length is < DisplayNameMin or > DisplayNameMax || displayName.Any(char.IsControl))
        {
            problems["displayName"] = [$"Відображуване ім'я: від {DisplayNameMin} до {DisplayNameMax} символів."];
        }

        // Вимоги до складу пароля перевіряє Identity. Тут лише наявність і розумна верхня межа.
        if (password.Length is 0 or > PasswordMax)
        {
            problems["password"] = [$"Пароль обов'язковий і не довший за {PasswordMax} символів."];
        }

        return problems.Count == 0 ? new SignUpDraft(userName, email, displayName, password) : null;
    }

    private static bool IsUserNameSymbol(char symbol) =>
        char.IsAsciiLetterOrDigit(symbol) || symbol is '.' or '-' or '_';

    private static bool LooksLikeEmail(string value) =>
        MailAddress.TryCreate(value, out var parsed)
        && string.Equals(parsed.Address, value, StringComparison.Ordinal)
        && parsed.Host.Contains('.', StringComparison.Ordinal);
}
