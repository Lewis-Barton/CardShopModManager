namespace TCGCardShopSimModManager.Core;

public sealed record ValidationResult(bool IsValid, List<string> Errors)
{
    public static ValidationResult Success() => new(true, new List<string>());
    public static ValidationResult Failure(List<string> errors) => new(false, errors);
}

public sealed class ManifestValidator
{
    private static readonly HashSet<string> KnownInstallTypes = new()
    {
        "BepInExPlugin",
        "BepInEx"
    };

    public ValidationResult Validate(ModListManifest manifest)
    {
        var errors = new List<string>();

        if (manifest.ManifestVersion != 1)
            errors.Add($"Unsupported manifest version: {manifest.ManifestVersion}");

        if (string.IsNullOrWhiteSpace(manifest.Name))
            errors.Add("Manifest name is required.");

        // BUG-028: an empty mod list installs nothing useful — surface it.
        var mods = manifest.Mods;
        if (mods is null || mods.Count == 0)
            errors.Add("Manifest declares no mods; nothing will be installed.");

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in mods ?? System.Linq.Enumerable.Empty<ModEntry>())
        {
            if (string.IsNullOrWhiteSpace(mod.Name))
                errors.Add("A mod entry is missing a name.");
            else if (!IsSafeDirectoryName(mod.Name))
                errors.Add($"{mod.Name}: mod name cannot be used as a safe folder name.");
            else if (!seenNames.Add(mod.Name))
                errors.Add($"Duplicate mod name: {mod.Name}");

            if (string.IsNullOrWhiteSpace(mod.Id))
                errors.Add($"{mod.Name}: missing 'id' (dependencies and profiles reference mods by id).");
            else if (!seenIds.Add(mod.Id))
                errors.Add($"Duplicate mod id: {mod.Id}");

            if (string.IsNullOrWhiteSpace(mod.Sha256))
                errors.Add($"{mod.Name}: missing SHA-256 hash.");

            // BUG-025 / BUG-032: the "BepInEx" install type is reserved for the
            // framework entry (id == bepinex). It must not be used by ordinary
            // mods, and the framework must use exactly this type.
            if (!KnownInstallTypes.Contains(mod.InstallType))
                errors.Add($"{mod.Name}: unknown install type '{mod.InstallType}'.");
            else if (mod.InstallType == ModListConventions.BepInExInstallType &&
                     !mod.Id.Equals(ModListConventions.BepInExModId, StringComparison.OrdinalIgnoreCase))
                errors.Add($"{mod.Name}: install type '{mod.InstallType}' is reserved for the framework entry (id '{ModListConventions.BepInExModId}').");
            else if (mod.Id.Equals(ModListConventions.BepInExModId, StringComparison.OrdinalIgnoreCase) &&
                     mod.InstallType != ModListConventions.BepInExInstallType)
                errors.Add($"{mod.Name}: the framework entry (id '{ModListConventions.BepInExModId}') must use install type '{ModListConventions.BepInExInstallType}'.");

            // BUG-024: reject real traversal (a ".." path segment or a rooted
            // path), but allow ".." to appear inside a filename (e.g. MyMod..v1.zip).
            if (IsUnsafeRelativePath(mod.Archive))
                errors.Add($"{mod.Name}: archive path is unsafe ('{mod.Archive}')");
        }

        return errors.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(errors);
    }

    private static bool IsUnsafeRelativePath(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            return true;

        foreach (var segment in relativePath.Split('/', '\\'))
            if (segment is ".." or "")
                return true;

        return false;
    }

    private static bool IsSafeDirectoryName(string name)
    {
        if (name is "." or ".." || name.IndexOfAny(new[] { '/', '\\' }) >= 0)
            return false;

        return name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }
}
