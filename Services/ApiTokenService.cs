using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace ArizaSikayet.Services;

public class ApiTokenService
{
    private readonly IDataProtector _protector;
    private static readonly TimeSpan TokenOmru = TimeSpan.FromDays(30);

    public ApiTokenService(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider.CreateProtector("ArizaSikayet.ApiToken.v1");
    }

    public ApiTokenResult TokenOlustur(IEnumerable<Claim> claims)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(TokenOmru);
        var payload = new ApiTokenPayload
        {
            ExpiresAt = expiresAt,
            Claims = claims
                .Where(x => !string.IsNullOrWhiteSpace(x.Type))
                .Select(x => new ApiTokenClaim(x.Type, x.Value ?? ""))
                .ToList()
        };

        var json = JsonSerializer.Serialize(payload);
        return new ApiTokenResult(_protector.Protect(json), expiresAt);
    }

    public ClaimsPrincipal? TokenCoz(string token)
    {
        try
        {
            var json = _protector.Unprotect(token);
            var payload = JsonSerializer.Deserialize<ApiTokenPayload>(json);

            if (payload == null || payload.ExpiresAt < DateTimeOffset.UtcNow)
            {
                return null;
            }

            var claims = payload.Claims.Select(x => new Claim(x.Type, x.Value)).ToList();
            var identity = new ClaimsIdentity(claims, ApiTokenAuthenticationDefaults.AuthenticationScheme);
            return new ClaimsPrincipal(identity);
        }
        catch
        {
            return null;
        }
    }

    private sealed class ApiTokenPayload
    {
        public DateTimeOffset ExpiresAt { get; set; }
        public List<ApiTokenClaim> Claims { get; set; } = new();
    }

    private sealed record ApiTokenClaim(string Type, string Value);
}

public sealed record ApiTokenResult(string Token, DateTimeOffset ExpiresAt);

public static class ApiTokenAuthenticationDefaults
{
    public const string AuthenticationScheme = "ApiToken";
}
