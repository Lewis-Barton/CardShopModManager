namespace TCGCardShopSimModManager.Core;

/// <summary>
/// Treats a folder on disk as a source of mod files — the offline counterpart
/// to <see cref="HttpModSource"/>. Useful for testing and for "stage the files
/// here, let the downloader verify and cache them" workflows.
/// </summary>
public sealed class LocalFileSource : IModSource
{
    private readonly string _rootDirectory;

    public LocalFileSource(string rootDirectory)
    {
        _rootDirectory = rootDirectory;
    }

    public Task<DownloadStream> OpenAsync(ModReference mod, long? resumeFromByte, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_rootDirectory, mod.FileName);
        if (!File.Exists(path))
            throw new DownloadException($"Source file not found: {path}", retryable: false);

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (resumeFromByte is long from)
            stream.Seek(from, SeekOrigin.Begin);

        return Task.FromResult(new DownloadStream(stream.Length, resumeFromByte ?? 0, stream));
    }
}