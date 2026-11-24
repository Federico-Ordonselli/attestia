using System.Security.Claims;
using Attestia.Core.Security;

namespace Attestia.Api.Auth;

internal static class Policies
{
    /// <summary>Qualunque utente autenticato con un ruolo valido (anche Employee).</summary>
    public const string AnyUser = "AnyUser";

    /// <summary>HR e Admin: vedono tutti i dati e importano.</summary>
    public const string Hr = "Hr";

    /// <summary>Solo Admin: gestione dei corsi.</summary>
    public const string Admin = "Admin";

    public static void Configure(Microsoft.AspNetCore.Authorization.AuthorizationOptions options)
    {
        options.AddPolicy(AnyUser, p => p.RequireAuthenticatedUser().RequireClaim(AttestiaClaims.Role, Roles.Employee, Roles.Hr, Roles.Admin));
        options.AddPolicy(Hr, p => p.RequireAuthenticatedUser().RequireClaim(AttestiaClaims.Role, Roles.Hr, Roles.Admin));
        options.AddPolicy(Admin, p => p.RequireAuthenticatedUser().RequireClaim(AttestiaClaims.Role, Roles.Admin));

        // Tutto cio' che non dichiara esplicitamente AllowAnonymous richiede un utente valido.
        options.FallbackPolicy = options.GetPolicy(AnyUser);
    }
}

internal static class ClaimsPrincipalExtensions
{
    /// <summary>La matricola viene SEMPRE dal claim emesso dopo la validazione SAML, mai da un parametro della richiesta.</summary>
    public static string EmployeeNumber(this ClaimsPrincipal user) =>
        user.FindFirstValue(AttestiaClaims.EmployeeNumber) ?? throw new InvalidOperationException("Claim matricola assente.");

    public static string Role(this ClaimsPrincipal user) =>
        user.FindFirstValue(AttestiaClaims.Role) ?? throw new InvalidOperationException("Claim ruolo assente.");
}
