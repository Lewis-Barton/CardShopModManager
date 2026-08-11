namespace CardShopModManager.Core;

/// <summary>
/// An open stream of the file's bytes plus the metadata the downloader needs.
/// <see cref="StartOffset"/> is the offset within the file this stream begins at
/// (0 for a fresh download, the existing partial length for a resume).
/// </summary>
public sealed class DownloadStream : IDisposable
{
    public DownloadStream(long? totalBytes, long startOffset, Stream content, IDisposable? owner = null)
    {
        TotalBytes = totalBytes;
        StartOffset = startOffset;
        Content = content;
        _owner = owner;
    }

    public long? TotalBytes { get; }
    public long StartOffset { get; }
    public Stream Content { get; }

    private readonly IDisposable? _owner;

    public void Dispose()
    {
        Content.Dispose();
        _owner?.Dispose();
    }
}

/// <summary>
/// How bytes become available. Implementations only answer "open the file and
/// start reading from here" — every safety concern (partial files, hash checks,
/// retries, cache) lives in <see cref="ModDownloader"/>, not in the source.
/// NexusModSource will be another implementation of this interface.
/// </summary>
public interface IModSource
{
    /// <summary>
    /// Open the file's bytes, optionally starting at <paramref name="resumeFromByte"/>.
    /// Throws <see cref="DownloadException"/> for failures the source can describe.
    /// </summary>
    Task<DownloadStream> OpenAsync(ModReference mod, long? resumeFromByte, CancellationToken cancellationToken);
}