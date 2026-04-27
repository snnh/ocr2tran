namespace Ocr2Tran.Translation;

public sealed class RateLimiter : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _spacing;
    private DateTimeOffset _nextAllowed = DateTimeOffset.MinValue;

    public RateLimiter(double rps)
    {
        _spacing = rps <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(1 / rps);
    }

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        if (_spacing == TimeSpan.Zero)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (_nextAllowed > now)
            {
                await Task.Delay(_nextAllowed - now, cancellationToken).ConfigureAwait(false);
                now = DateTimeOffset.UtcNow;
            }

            _nextAllowed = now + _spacing;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
    }
}
