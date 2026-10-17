using System.Security.Claims;

namespace SecureLab.Api.Security;

// Єдине місце, де код читає ідентифікатор поточного користувача.
// Джерело лише HttpContext.User, який сформував обробник cookie-схеми.
public static class VerifiedUser
{
    public static bool TryGetId(ClaimsPrincipal principal, out Guid userId)
    {
        userId = Guid.Empty;
        return principal.Identity?.IsAuthenticated == true
            && Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
    }
}
