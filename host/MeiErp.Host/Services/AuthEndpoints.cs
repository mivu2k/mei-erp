using System.Security.Claims;
using MeiErp.Platform.Identity;
using MeiErp.Platform.Kernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace MeiErp.Host.Services;

/// <summary>
/// Sign-in and sign-out as plain form posts.
///
/// These are endpoints rather than Blazor components on purpose: setting an
/// authentication cookie needs a real HTTP response, and an interactive circuit
/// has already sent its headers by the time a button is clicked.
/// </summary>
public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/auth");

        group.MapPost("/sign-in", async (
            [FromForm] string email,
            [FromForm] string password,
            [FromForm] bool rememberMe,
            [FromForm] string? returnUrl,
            SignInManager<ApplicationUser> signIn,
            UserManager<ApplicationUser> users,
            IClock clock,
            HttpContext http) =>
        {
            var user = await users.FindByEmailAsync(email);

            // A deactivated account must not sign in, but the message stays the
            // same as a wrong password: telling an attacker which of the two it
            // was hands them a valid-username oracle.
            if (user is null || !user.IsActive)
                return Redirect($"/sign-in?error=1&returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}");

            var result = await signIn.PasswordSignInAsync(
                user, password, rememberMe, lockoutOnFailure: true);

            if (result.IsLockedOut)
                return Redirect("/sign-in?error=locked");

            if (!result.Succeeded)
                return Redirect($"/sign-in?error=1&returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}");

            user.LastLoginUtc = clock.UtcNow;
            await users.UpdateAsync(user);

            if (user.MustChangePassword)
                return Redirect("/change-password");

            return Redirect(SafeReturn(returnUrl));
        }).DisableAntiforgery().RequireRateLimiting("sign-in");

        group.MapPost("/sign-out", async (SignInManager<ApplicationUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Redirect("/sign-in");
        }).DisableAntiforgery();
    }

    /// <summary>
    /// Only ever redirect within this site. An open redirect turns the login
    /// page into a convincing launch pad for a phishing link.
    /// </summary>
    private static string SafeReturn(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl)
        && returnUrl.StartsWith('/')
        && !returnUrl.StartsWith("//", StringComparison.Ordinal)
            ? returnUrl
            : "/";

    private static IResult Redirect(string url) => Results.Redirect(url);
}

/// <summary>
/// The signed-in person, read from the request principal.
///
/// Permissions and module access were stamped onto the principal at sign-in, so
/// answering "can they?" costs nothing - no database hit per page render.
/// </summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public string? UserId => Principal?.FindFirstValue(ClaimTypes.NameIdentifier);

    public string? Name =>
        Principal?.FindFirstValue("full_name")
        ?? Principal?.FindFirstValue(ClaimTypes.Name);

    public string? Email => Principal?.FindFirstValue(ClaimTypes.Email);

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public bool Can(string permission) =>
        Principal is not null
        && (Principal.IsInRole(PlatformPermissions.SuperAdminRole)
            || Principal.HasClaim(PermissionClaim.Type, permission));

    public bool InModule(string moduleKey) =>
        Principal is not null
        && (Principal.IsInRole(PlatformPermissions.SuperAdminRole)
            || Principal.HasClaim(PermissionClaim.ModuleType, moduleKey));

    public IReadOnlyCollection<string> Roles =>
        Principal?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray() ?? [];
}
