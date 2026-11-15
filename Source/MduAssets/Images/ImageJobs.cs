namespace MduAssets.Images;

/// <summary>
/// Limits how many image jobs run at once, so memory stays predictable when many uploads or new variants arrive together
/// (NF-07). Jobs run on the thread pool because libvips blocks while it works.
/// </summary>
public sealed class ImageJobs(ServerSettings settings)
{
    private readonly SemaphoreSlim _gate = new(settings.ImageJobs);

    public async Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(work, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}
