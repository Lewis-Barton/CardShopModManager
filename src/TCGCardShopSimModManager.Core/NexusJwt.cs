using System.Text.Json;

namespace TCGCardShopSimModManager.Core;

/// <summary>
/// Decodes the Nexus OAuth <c>access_token</c> (a JWT) to read the signed-in
/// user without an extra API call. The token's signature is NOT verified here —
/// the Nexus API verifies it on every request, and we only ever hold tokens
/// Nexus issued to us. We just need the claims (username, premium status).
/// </summary>
public static class NexusJwt
{
    public static NexusUser? DecodeAccessToken(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2)
            return null;

        try
        {
            var payload = Base64Url.DecodeToString(parts[1]);
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            long id = 0;
            if (root.TryGetProperty("sub", out var sub))
            {
                if (sub.ValueKind == JsonValueKind.String && long.TryParse(sub.GetString(), out var lid))
                    id = lid;
                else if (sub.ValueKind == JsonValueKind.Number)
                    id = sub.GetInt64();
            }

            string name = "";
            var premium = false;

            if (root.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object)
            {
                if (user.TryGetProperty("username", out var un))
                    name = un.GetString() ?? "";

                if (user.TryGetProperty("membership_roles", out var roles) &&
                    roles.ValueKind == JsonValueKind.Array)
                {
                    foreach (var role in roles.EnumerateArray())
                    {
                        if (role.GetString() is { } r && (r == "premium" || r == "lifetimepremium"))
                        {
                            premium = true;
                            break;
                        }
                    }
                }
            }

            return new NexusUser(id, name, premium);
        }
        catch
        {
            return null;
        }
    }
}
