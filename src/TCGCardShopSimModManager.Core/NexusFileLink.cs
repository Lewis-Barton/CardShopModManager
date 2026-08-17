namespace TCGCardShopSimModManager.Core;

public sealed record NexusFileLink(long ModId, long FileId)
{
    public static bool TryParse(string value, out NexusFileLink? link)
    {
        link = null;
        var stable = value.Trim().Split(':');
        if (stable.Length == 3 && stable[0].Equals("nexus", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(stable[1], out var stableModId) && stableModId > 0 &&
            long.TryParse(stable[2], out var stableFileId) && stableFileId > 0)
        {
            link = new NexusFileLink(stableModId, stableFileId);
            return true;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme.Equals("nxm", StringComparison.OrdinalIgnoreCase))
            return TryParseNxm(uri, out link);

        if (uri.Scheme is not ("http" or "https") ||
            !(uri.Host.Equals("nexusmods.com", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.EndsWith(".nexusmods.com", StringComparison.OrdinalIgnoreCase)))
            return false;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var modsIndex = Array.FindIndex(segments, segment =>
            segment.Equals("mods", StringComparison.OrdinalIgnoreCase));
        if (modsIndex < 0 || modsIndex + 1 >= segments.Length ||
            !long.TryParse(segments[modsIndex + 1], out var modId) || modId <= 0)
            return false;

        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in query)
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 &&
                Uri.UnescapeDataString(parts[0]).Equals("file_id", StringComparison.OrdinalIgnoreCase) &&
                long.TryParse(Uri.UnescapeDataString(parts[1]), out var fileId) && fileId > 0)
            {
                link = new NexusFileLink(modId, fileId);
                return true;
            }
        }

        return false;
    }

    private static bool TryParseNxm(Uri uri, out NexusFileLink? link)
    {
        link = null;
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 4 ||
            !segments[0].Equals("mods", StringComparison.OrdinalIgnoreCase) ||
            !segments[2].Equals("files", StringComparison.OrdinalIgnoreCase) ||
            !long.TryParse(segments[1], out var modId) || modId <= 0 ||
            !long.TryParse(segments[3], out var fileId) || fileId <= 0)
            return false;

        link = new NexusFileLink(modId, fileId);
        return true;
    }
}

public sealed record NexusModLink(long ModId)
{
    public static bool TryParse(string value, out NexusModLink? link)
    {
        link = null;
        var trimmed = value.Trim();
        if (long.TryParse(trimmed, out var numericId) && numericId > 0)
        {
            link = new NexusModLink(numericId);
            return true;
        }

        if (trimmed.StartsWith("nexus:", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(trimmed[6..], out var stableId) && stableId > 0)
        {
            link = new NexusModLink(stableId);
            return true;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") ||
            !(uri.Host.Equals("nexusmods.com", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.EndsWith(".nexusmods.com", StringComparison.OrdinalIgnoreCase)))
            return false;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var modsIndex = Array.FindIndex(segments, segment =>
            segment.Equals("mods", StringComparison.OrdinalIgnoreCase));
        if (modsIndex < 0 || modsIndex + 1 >= segments.Length ||
            !long.TryParse(segments[modsIndex + 1], out var modId) || modId <= 0)
            return false;

        link = new NexusModLink(modId);
        return true;
    }
}
