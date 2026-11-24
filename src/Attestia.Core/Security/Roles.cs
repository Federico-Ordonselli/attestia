namespace Attestia.Core.Security;

/// <summary>Ruoli applicativi, ricevuti come attributo SAML <c>role</c>.</summary>
public static class Roles
{
    public const string Employee = "Employee";
    public const string Hr = "HR";
    public const string Admin = "Admin";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { Employee, Hr, Admin };

    public static bool IsValid(string? role) => role is not null && All.Contains(role);
}

/// <summary>Nomi dei claim usati nel <c>ClaimsPrincipal</c> dell'API.</summary>
public static class AttestiaClaims
{
    public const string EmployeeNumber = "attestia:employee_number";
    public const string Role = "attestia:role";
    public const string DisplayName = "attestia:display_name";
}
