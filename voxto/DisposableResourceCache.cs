namespace Voxto;

internal sealed class DisposableResourceCache<T> : IDisposable where T : class, IDisposable
{
    private readonly object _syncRoot = new();
    private string? _key;
    private T? _resource;

    public T GetOrCreate(string key, Func<string, T> create)
    {
        lock (_syncRoot)
        {
            if (_resource is not null && string.Equals(_key, key, StringComparison.Ordinal))
                return _resource;

            var replacement = create(key);
            var previous = _resource;

            _resource = replacement;
            _key = key;
            previous?.Dispose();

            return _resource;
        }
    }

    public void Clear()
    {
        lock (_syncRoot)
        {
            _resource?.Dispose();
            _resource = null;
            _key = null;
        }
    }

    public void Dispose() => Clear();
}
