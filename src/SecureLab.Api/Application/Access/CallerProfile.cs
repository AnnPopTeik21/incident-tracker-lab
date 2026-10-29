using System.Security.Claims;
using SecureLab.Api.Data;
using SecureLab.Api.Security;

namespace SecureLab.Api.Application.Access;

// Хто надіслав запит. Id і роль беруться лише з principal, який сформувала перевірена cookie.
// Тіло запиту, URL і заголовки клієнта сюди не потрапляють.
public sealed record CallerProfile(Guid UserId, bool IsAnalyst)
{
    public static bool TryRead(ClaimsPrincipal principal, out CallerProfile caller)
    {
        caller = null!;
        if (!VerifiedUser.TryGetId(principal, out var userId))
        {
            return false;
        }

        caller = new CallerProfile(userId, principal.IsInRole(DbSeeder.AnalystRole));
        return true;
    }
}
