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

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in manifest.Mods)
        {
            if (string.IsNullOrWhiteSpace(mod.Name))
                errors.Add("A mod entry is missing a name.");
            else if (!seenNames.Add(mod.Name))
                errors.Add($"Duplicate mod name: {mod.Name}");

            if (string.IsNullOrWhiteSpace(mod.Id))
                errors.Add($"{mod.Name}: missing 'id' (dependencies and profiles reference mods by id).");
            else if (!seenIds.Add(mod.Id))
                errors.Add($"Duplicate mod id: {mod.Id}");

            if (string.IsNullOrWhiteSpace(mod.Sha256))
                errors.Add($"{mod.Name}: missing SHA-256 hash.");

            if (!KnownInstallTypes.Contains(mod.InstallType))
                errors.Add($"{mod.Name}: unknown install type '{mod.InstallType}'.");

            if (mod.Archive.Contains("..") || Path.IsPathRooted(mod.Archive))
                errors.Add($"{mod.Name}: archive path is unsafe ('{mod.Archive}')");
        }

        return errors.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(errors);
    }
}