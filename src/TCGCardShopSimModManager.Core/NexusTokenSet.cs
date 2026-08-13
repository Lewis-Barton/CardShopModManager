using System;

namespace TCGCardShopSimModManager.Core;

/// <summary>
/// The tokens returned by the Nexus OAuth token endpoint. <see cref="ExpiresAt"/>
/// is computed from <c>expires_in</c> at the moment we receive the response.
/// </summary>
public sealed record NexusTokenSet(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt)
{
    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
}
