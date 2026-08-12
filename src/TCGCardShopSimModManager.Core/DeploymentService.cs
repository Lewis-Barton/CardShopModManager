namespace TCGCardShopSimModManager.Core;

public sealed record DeploymentReport(bool Success, List<string> Lines)
{
    public static DeploymentReport Ok(List<string> lines) => new(true, lines);

    public static DeploymentReport Failure(List<string> lines, string? error)
    {
        if (error is not null)
            lines.Add(error);
        return new(false, lines);
    }
}

/// <summary>A per-archive preview, ready for display.</summary>
public sealed record PlanPreview(
    string ModName,
    string LayoutName,
    List<string> Files,
    List<string> Skipped,
    List<string> Rejected);

/// <summary>
/// The single orchestration path both front-ends use — the CLI commands and the
/// desktop app are thin shells around this, so behaviour stays the same no
/// matter which one you use.
/// </summary>
public sealed class DeploymentService
{
    public DeploymentReport Validate(string manifestPath, string? gameFolderPath)
    {
        Diagnostic.Write($"DeploymentService.Validate({manifestPath})");
        var lines = new List<string>();

        if (!File.Exists(manifestPath))
            return DeploymentReport.Failure(lines, $"Manifest file not found: {manifestPath}");

        var manifest = new ManifestReader().Read(manifestPath);
        var validation = new ManifestValidator().Validate(manifest);
        if (!validation.IsValid)
        {
            lines.Add("Manifest is invalid:");
            lines.AddRange(validation.Errors.Select(e => $"  - {e}"));
            return DeploymentReport.Failure(lines, null);
        }

        lines.Add($"Manifest '{manifest.Name}' is valid.");

        if (gameFolderPath is null)
            lines.Add("  (No game folder given — checking with every mod enabled.)");

        var resolution = new ModListResolver().Resolve(manifest, ResolveEnabledIds(manifest, gameFolderPath));
        if (!resolution.IsValid)
        {
            lines.Add("The enabled mod list cannot be installed:");
            lines.AddRange(resolution.Errors.Select(e => $"  - {e}"));
            return DeploymentReport.Failure(lines, null);
        }

        lines.Add($"  {resolution.OrderedMods.Count} mod(s). Valid install order:");
        lines.AddRange(resolution.OrderedMods.Select(m => $"    {Label(m)} (id: {m.Id})"));
        return DeploymentReport.Ok(lines);
    }

    public DeploymentReport Install(string manifestPath, string sourceDirectory, string gameFolderPath)
    {
        Diagnostic.Write($"DeploymentService.Install({manifestPath})");
        var lines = new List<string>();

        if (!File.Exists(manifestPath))
            return DeploymentReport.Failure(lines, $"Manifest file not found: {manifestPath}");

        var manifest = new ManifestReader().Read(manifestPath);
        var validation = new ManifestValidator().Validate(manifest);
        if (!validation.IsValid)
        {
            lines.Add("Manifest is invalid:");
            lines.AddRange(validation.Errors.Select(e => $"  - {e}"));
            return DeploymentReport.Failure(lines, null);
        }

        var resolution = new ModListResolver().Resolve(manifest, ResolveEnabledIds(manifest, gameFolderPath));
        if (!resolution.IsValid)
        {
            lines.Add("The enabled mod list cannot be installed:");
            lines.AddRange(resolution.Errors.Select(e => $"  - {e}"));
            return DeploymentReport.Failure(lines, null);
        }

        lines.Add("Install order:");
        lines.AddRange(resolution.OrderedMods.Select(m => $"  {Label(m)}"));

        var installer = new ModInstaller(gameFolderPath);
        var toInstall = resolution.OrderedMods.Where(m => !installer.IsInstalled(m.Name)).ToList();

        // Pre-flight: plan every archive so two mods claiming the same file are
        // caught before a single byte is copied.
        var planRoot = Path.Combine(Path.GetTempPath(), "cardshopmodmanager-preflight", Guid.NewGuid().ToString("N"));
        var plans = new List<InstallPlan>();
        try
        {
            Directory.CreateDirectory(planRoot);
            for (var i = 0; i < toInstall.Count; i++)
            {
                try
                {
                    plans.Add(installer.CreatePlan(toInstall[i], sourceDirectory, Path.Combine(planRoot, $"mod-{i + 1}")));
                }
                catch (Exception ex)
                {
                    return DeploymentReport.Failure(lines, $"Could not plan {toInstall[i].Name}: {ex.Message}");
                }
            }
        }
        finally
        {
            if (Directory.Exists(planRoot))
                Directory.Delete(planRoot, recursive: true);
        }

        var conflicts = DestinationConflictFinder.Find(plans);
        if (conflicts.Count > 0)
        {
            lines.Add("File conflicts detected — refusing to install:");
            lines.AddRange(conflicts.Select(c => $"  {c.Destination} is claimed by '{c.ModA}' and '{c.ModB}'"));
            return DeploymentReport.Failure(lines, null);
        }

        foreach (var mod in toInstall)
        {
            var result = installer.Install(mod, sourceDirectory);
            lines.Add(result.Success
                ? $"Installed {Label(mod)}: {result.InstalledPaths!.Count} file(s)."
                : $"Failed to install {Label(mod)}: {result.Error}");

            if (!result.Success)
                Diagnostic.Write($"install failed for {mod.Id}: {result.Error}", "install");
        }

        return DeploymentReport.Ok(lines);
    }

    public IReadOnlyList<PlanPreview> Preview(string manifestPath, string sourceDirectory)
    {
        var manifest = new ManifestReader().Read(manifestPath);
        var installer = new ModInstaller(Path.GetTempPath()); // planning never touches the journal

        var previews = new List<PlanPreview>();
        var planRoot = Path.Combine(Path.GetTempPath(), "cardshopmodmanager-preview", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(planRoot);
            for (var i = 0; i < manifest.Mods.Count; i++)
            {
                var mod = manifest.Mods[i];
                var label = Label(mod);

                try
                {
                    var plan = installer.CreatePlan(mod, sourceDirectory, Path.Combine(planRoot, $"mod-{i + 1}"));
                    previews.Add(new PlanPreview(
                        label,
                        plan.LayoutName,
                        plan.Files.Select(f => $"  {f.SourceRelativePath}  ->  {f.DestinationRelativePath}").ToList(),
                        plan.SkippedEntries.ToList(),
                        plan.RejectedEntries.ToList()));
                }
                catch (Exception ex)
                {
                    previews.Add(new PlanPreview(label, "could not plan", new List<string> { ex.Message },
                        new List<string>(), new List<string>()));
                }
            }
        }
        finally
        {
            if (Directory.Exists(planRoot))
                Directory.Delete(planRoot, recursive: true);
        }

        return previews;
    }

    private static ISet<string> ResolveEnabledIds(ModListManifest manifest, string? gameFolderPath)
    {
        var allIds = new HashSet<string>(manifest.Mods.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
        if (gameFolderPath is null)
            return allIds;

        return new ProfilesStore(gameFolderPath).EnabledIdsOrAll() ?? allIds;
    }

    private static string Label(ModEntry mod) =>
        mod.Version is null ? mod.Name : $"{mod.Name} {mod.Version}";
}