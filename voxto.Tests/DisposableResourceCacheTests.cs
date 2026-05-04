using Voxto;
using Xunit;

namespace Voxto.Tests;

public sealed class DisposableResourceCacheTests
{
    [Fact]
    public void GetOrCreate_ReusesCachedInstance_ForSameKey()
    {
        using var cache = new DisposableResourceCache<FakeDisposableResource>();
        var createCount = 0;

        var first = cache.GetOrCreate("small", _ => new FakeDisposableResource(++createCount));
        var second = cache.GetOrCreate("small", _ => new FakeDisposableResource(++createCount));

        Assert.Same(first, second);
        Assert.Equal(1, createCount);
        Assert.False(first.IsDisposed);
    }

    [Fact]
    public void GetOrCreate_ReplacesAndDisposesPreviousInstance_WhenKeyChanges()
    {
        using var cache = new DisposableResourceCache<FakeDisposableResource>();

        var first = cache.GetOrCreate("small", _ => new FakeDisposableResource(1));
        var second = cache.GetOrCreate("medium", _ => new FakeDisposableResource(2));

        Assert.NotSame(first, second);
        Assert.True(first.IsDisposed);
        Assert.False(second.IsDisposed);
    }

    [Fact]
    public void Clear_DisposesCachedInstance()
    {
        using var cache = new DisposableResourceCache<FakeDisposableResource>();
        var resource = cache.GetOrCreate("small", _ => new FakeDisposableResource(1));

        cache.Clear();

        Assert.True(resource.IsDisposed);
    }

    [Fact]
    public void GetOrCreate_WhenReplacementCreationFails_PreservesOriginalInstance()
    {
        using var cache = new DisposableResourceCache<FakeDisposableResource>();
        var original = cache.GetOrCreate("small", _ => new FakeDisposableResource(1));

        Assert.Throws<InvalidOperationException>(() =>
            cache.GetOrCreate("medium", _ => throw new InvalidOperationException("boom")));

        var replacement = cache.GetOrCreate("small", _ => new FakeDisposableResource(2));

        Assert.Same(original, replacement);
        Assert.False(original.IsDisposed);
    }

    private sealed class FakeDisposableResource(int id) : IDisposable
    {
        public int Id { get; } = id;

        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}
