namespace TCGCardShopSimModManager.Core;

public sealed record ResolutionResult(
    bool IsValid,
    List<string> Errors,
    List<ModEntry> OrderedMods);

/// <summary>
/// Turns a mod list into a valid installation order, or explains exactly why
/// that list cannot be installed. Only the enabled mods are resolved — a
/// disabled mod's problems do not block the ones you actually want.
///
/// Checks, all reported together so the caller gets every reason at once:
///   1. Unique, non-empty ids across the whole list.
///   2. Every dependency of an enabled mod exists and is itself enabled.
///   3. No two enabled mods explicitly conflict.
///   4. No dependency cycles (Kahn's algorithm both orders and detects them).
/// </summary>
public sealed class ModListResolver
{
    public ResolutionResult Resolve(ModListManifest manifest, ISet<string> enabledIds)
    {
        var errors = new List<string>();

        // Whole-list structure: ids must be unique even outside the enabled set,
        // because "mod-a" and "mod-A" referencing each other must be unambiguous.
        var allById = new Dictionary<string, ModEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in manifest.Mods)
        {
            if (!allById.TryAdd(mod.Id, mod))
                errors.Add($"Duplicate mod id: {mod.Id}");
        }

        // An exact-case index. The case-insensitive map above stays the source of
        // truth for uniqueness and existence, but resolving a *reference* must be
        // exact: a dependency on "mod-A" that only matches "mod-a" by case is a
        // defect the author should fix, not something to silently accept (BUG-021).
        var allByIdExact = new Dictionary<string, ModEntry>(StringComparer.Ordinal);
        foreach (var mod in manifest.Mods)
            allByIdExact.TryAdd(mod.Id, mod);

        var enabled = new Dictionary<string, ModEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in manifest.Mods)
        {
            if (enabledIds.Contains(mod.Id))
                enabled.TryAdd(mod.Id, mod);
        }

        foreach (var mod in enabled.Values)
        {
            foreach (var dependencyId in mod.Dependencies)
            {
                if (!allByIdExact.TryGetValue(dependencyId, out var dependency))
                {
                    if (allById.TryGetValue(dependencyId, out var caseMatch))
                        errors.Add($"{mod.Name}: depends on '{dependencyId}', which matches '{caseMatch.Id}' only by case.");
                    else
                        errors.Add($"{mod.Name}: depends on '{dependencyId}', which is not in the mod list.");
                    continue;
                }

                if (!enabled.ContainsKey(dependencyId))
                    errors.Add($"{mod.Name}: depends on '{dependencyId}' ({dependency.Name}), which is not enabled in this profile.");
            }

            foreach (var conflictId in mod.Conflicts)
            {
                // A conflict only matters when the other mod is also enabled. If the
                // reference matches the real id only by case, call that out (BUG-021).
                if (enabled.TryGetValue(conflictId, out var conflicted))
                {
                    if (!allByIdExact.ContainsKey(conflictId))
                        errors.Add($"{mod.Name}: conflict with '{conflictId}', which matches '{conflicted.Id}' only by case.");
                    else
                        errors.Add($"{mod.Name} and {conflicted.Name} ('{conflictId}') conflict and cannot both be enabled.");
                }
            }
        }

        // Ordering and cycle detection run even when other errors exist, so the
        // caller learns about every defect in one go. Only edges to dependencies
        // that are present and enabled take part in the order; missing ones are
        // already reported above and cannot shape an install order.
        var dependents = enabled.Values
            .ToDictionary(m => m.Id, _ => new List<ModEntry>(), StringComparer.OrdinalIgnoreCase);

        foreach (var mod in enabled.Values)
        {
            foreach (var dependencyId in mod.Dependencies)
            {
                if (enabled.ContainsKey(dependencyId))
                    dependents[dependencyId].Add(mod);
            }
        }

        var unresolved = enabled.Values.ToDictionary(
            m => m.Id,
            m => m.Dependencies.Count(id => enabled.ContainsKey(id)),
            StringComparer.OrdinalIgnoreCase);

        var ready = new Queue<ModEntry>(
            enabled.Values.Where(m => unresolved[m.Id] == 0));

        var ordered = new List<ModEntry>();
        while (ready.Count > 0)
        {
            var mod = ready.Dequeue();
            ordered.Add(mod);

            foreach (var dependent in dependents[mod.Id])
            {
                unresolved[dependent.Id]--;
                if (unresolved[dependent.Id] == 0)
                    ready.Enqueue(dependent);
            }
        }

        if (ordered.Count < enabled.Count)
        {
            var inCycle = enabled.Values
                .Where(m => unresolved[m.Id] > 0)
                .Select(m => m.Id)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase);

            errors.Add($"Circular dependency involving: {string.Join(", ", inCycle)}");
        }

        return errors.Count == 0
            ? new ResolutionResult(true, new List<string>(), ordered)
            : new ResolutionResult(false, errors, new List<ModEntry>());
    }
}