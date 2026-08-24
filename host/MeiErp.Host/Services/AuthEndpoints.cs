using System.Security.Claims;
using MeiErp.Platform.Identity;
using MeiErp.Platform.Kernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using System.Text;

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
            // Nullable on purpose: an unticked checkbox posts nothing at all,
            // so a non-nullable bool here makes the ordinary sign-in - the one
            // where nobody ticked "keep me signed in" - throw a 400.
            [FromForm] bool? rememberMe,
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
                user, password, rememberMe ?? false, lockoutOnFailure: true);

            if (result.IsLockedOut)
                return Redirect("/sign-in?error=locked");

            if (result.RequiresTwoFactor)
                return Redirect($"/two-factor?rememberMe={(rememberMe ?? false).ToString().ToLowerInvariant()}&returnUrl={Uri.EscapeDataString(SafeReturn(returnUrl))}");

            if (result.IsNotAllowed)
                return Redirect("/sign-in?error=confirm");

            if (!result.Succeeded)
                return Redirect($"/sign-in?error=1&returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}");

            user.LastLoginUtc = clock.UtcNow;
            await users.UpdateAsync(user);

            if (user.MustChangePassword)
                return Redirect("/change-password");

            return Redirect(SafeReturn(returnUrl));
        }).DisableAntiforgery().RequireRateLimiting("sign-in");

        group.MapPost("/two-factor", async ([FromForm] string code, [FromForm] bool? rememberMe,
            [FromForm] bool? rememberMachine, [FromForm] string? returnUrl,
            SignInManager<ApplicationUser> signIn) =>
        {
            code = code.Replace(" ", "").Replace("-", "");
            var result = await signIn.TwoFactorAuthenticatorSignInAsync(code, rememberMe ?? false, rememberMachine ?? false);
            if (result.IsLockedOut) return Redirect("/sign-in?error=locked");
            return result.Succeeded ? Redirect(SafeReturn(returnUrl))
                : Redirect($"/two-factor?error=1&rememberMe={(rememberMe ?? false).ToString().ToLowerInvariant()}&returnUrl={Uri.EscapeDataString(SafeReturn(returnUrl))}");
        }).DisableAntiforgery().RequireRateLimiting("sign-in");

        group.MapPost("/recovery-code", async ([FromForm] string code, [FromForm] string? returnUrl,
            SignInManager<ApplicationUser> signIn) =>
        {
            var result = await signIn.TwoFactorRecoveryCodeSignInAsync(code.Replace(" ", ""));
            if (result.IsLockedOut) return Redirect("/sign-in?error=locked");
            return result.Succeeded ? Redirect(SafeReturn(returnUrl))
                : Redirect($"/recovery-code?error=1&returnUrl={Uri.EscapeDataString(SafeReturn(returnUrl))}");
        }).DisableAntiforgery().RequireRateLimiting("sign-in");

        group.MapPost("/forgot-password", async ([FromForm] string email, UserManager<ApplicationUser> users,
            IAccountEmailSender sender, CancellationToken ct) =>
        {
            var user = await users.FindByEmailAsync(email);
            if (user is not null && user.IsActive && user.EmailConfirmed)
            {
                var token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await users.GeneratePasswordResetTokenAsync(user)));
                var path = $"/reset-password?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
                await sender.SendAsync(email, "Reset your MEI ERP password",
                    "A password reset was requested for your account. If this was not you, ignore this message.", path, ct);
            }
            return Redirect("/forgot-password?sent=1");
        }).DisableAntiforgery().RequireRateLimiting("sign-in");

        group.MapPost("/reset-password", async ([FromForm] string email, [FromForm] string token,
            [FromForm] string password, UserManager<ApplicationUser> users) =>
        {
            var user = await users.FindByEmailAsync(email);
            if (user is null) return Redirect("/reset-password?error=1");
            try { token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token)); }
            catch (FormatException) { return Redirect("/reset-password?error=1"); }
            var result = await users.ResetPasswordAsync(user, token, password);
            if (!result.Succeeded) return Redirect("/reset-password?error=1");
            user.MustChangePassword = false;
            await users.UpdateSecurityStampAsync(user);
            return Redirect("/sign-in?reset=1");
        }).DisableAntiforgery().RequireRateLimiting("sign-in");

        group.MapGet("/confirm-email", async (string userId, string token, UserManager<ApplicationUser> users) =>
        {
            var user = await users.FindByIdAsync(userId);
            if (user is null) return Redirect("/sign-in?error=confirm-invalid");
            try { token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token)); }
            catch (FormatException) { return Redirect("/sign-in?error=confirm-invalid"); }
            var result = await users.ConfirmEmailAsync(user, token);
            return Redirect(result.Succeeded ? "/sign-in?confirmed=1" : "/sign-in?error=confirm-invalid");
        }).RequireRateLimiting("sign-in");

        group.MapPost("/send-confirmation", async ([FromForm] string email, UserManager<ApplicationUser> users,
            IAccountEmailSender sender, CancellationToken ct) =>
        {
            var user = await users.FindByEmailAsync(email);
            if (user is not null && user.IsActive && !user.EmailConfirmed)
            {
                var token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await users.GenerateEmailConfirmationTokenAsync(user)));
                var path = $"/auth/confirm-email?userId={Uri.EscapeDataString(user.Id)}&token={Uri.EscapeDataString(token)}";
                await sender.SendAsync(email, "Confirm your MEI ERP email",
                    "Confirm this address to activate your MEI ERP account.", path, ct);
            }
            return Redirect("/forgot-password?confirmationSent=1");
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
