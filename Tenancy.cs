using System.Security.Claims;
namespace SpazaSnap;

public sealed class TenantContext(IHttpContextAccessor accessor)
{
    // Tenant identity comes only from the encrypted, authenticated cookie, never from a request body.
    public int ShopId => int.TryParse(accessor.HttpContext?.User.FindFirstValue("shop_id"), out var id) && id > 0
        ? id : throw new UnauthorizedAccessException("Sign in to your shop.");
}
public record RegisterInput(string ShopName, string Email, string Password, bool IncludeSamples = false);
public record LoginInput(string Email, string Password);
public record Account(int UserId, int ShopId, string ShopName, string Email, string Plan);

public static class AccountRules
{
    public static string Email(string? email)
    {
        var normalized = email?.Trim().ToLowerInvariant() ?? "";
        if (normalized.Length > 254 || !System.Net.Mail.MailAddress.TryCreate(normalized, out var parsed) || parsed.Address != normalized)
            throw new ArgumentException("Enter a valid email address.");
        return normalized;
    }
    public static void Password(string? password)
    {
        if (password is null || password.Length is < 12 or > 128 || !password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            throw new ArgumentException("Use 12–128 characters, including a letter and a number.");
    }
}
