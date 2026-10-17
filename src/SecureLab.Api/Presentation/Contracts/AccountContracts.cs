namespace SecureLab.Api.Presentation.Contracts;

// Що клієнт може надіслати під час реєстрації. Роль, Id, PasswordHash і будь-який
// інший стан, яким керує сервер, сюди не входять: невідомі поля JSON просто відкидаються.
public sealed record SignUpRequest(
    string? UserName,
    string? Email,
    string? DisplayName,
    string? Password);

// Відповідь після успішної реєстрації: лише три безпечні поля.
public sealed record SignUpResult(
    Guid Id,
    string UserName,
    string DisplayName);

// Профіль поточного користувача. Усі три поля беруться з claims перевіреного principal.
public sealed record MeResponse(
    Guid UserId,
    string UserName,
    IReadOnlyList<string> Roles);
