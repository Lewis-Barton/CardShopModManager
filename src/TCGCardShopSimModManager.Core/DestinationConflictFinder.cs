namespace TCGCardShopSimModManager.Core;

public sealed record DestinationConflict(string Destination, string ModA, string ModB);

/// <summary>
/// Compares resolved install plans across mods and finds any destination path
/// that more than one mod wants to write. This is a file conflict: which mod
/// "wins" an existing file should never be a guess.
/// </summary>
public static class DestinationConflictFinder
{
    public static List<DestinationConflict> Find(IReadOnlyList<InstallPlan> plans)
        => Find(plans, null);

    /// <summary>
    /// Overload that also treats already-installed mods as destination owners, so
    /// a pending mod that would overwrite a file owned by an installed one is
    /// refused at pre-flight too — not only when two pending mods collide
    /// (BUG-019).
    /// </summary>
    public static List<DestinationConflict> Find(
        IReadOnlyList<InstallPlan> plans,
        IReadOnlyList<InstallPlan>? installedPlans)
    {
        var conflicts = new List<DestinationConflict>();
        var destinations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Seed with files already on disk, owned by installed mods, so a pending
        // plan colliding with them is reported exactly like a pending-vs-pending
        // collision.
        foreach (var plan in installedPlans ?? new List<InstallPlan>())
            foreach (var file in plan.Files)
                destinations.TryAdd(file.DestinationRelativePath, plan.Mod.Name);

        foreach (var plan in plans)
        {
            foreach (var file in plan.Files)
            {
                var destination = file.DestinationRelativePath;

                if (destinations.TryGetValue(destination, out var firstOwner))
                {
                    if (!conflicts.Any(c =>
                            c.Destination.Equals(destination, StringComparison.OrdinalIgnoreCase) &&
                            c.ModA == firstOwner &&
                            c.ModB == plan.Mod.Name))
                    {
                        conflicts.Add(new DestinationConflict(destination, firstOwner, plan.Mod.Name));
                    }
                }
                else
                {
                    destinations.Add(destination, plan.Mod.Name);
                }
            }
        }

        return conflicts;
    }
}