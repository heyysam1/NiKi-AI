using NikiAI.Core.Browser;

namespace NikiAI.Browser;

/// <summary>
/// Registry responsible for selecting compatible browser adapters based on verified capabilities.
/// </summary>
public class BrowserAdapterRegistry : IBrowserAdapterRegistry
{
    private readonly List<IBrowserAdapter> _adapters = new();
    private readonly object _lock = new();

    public BrowserAdapterRegistry()
    {
        // Default verified Chromium CDP adapter
        _adapters.Add(new ChromiumCdpAdapter());
    }

    public void RegisterAdapter(IBrowserAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        lock (_lock)
        {
            _adapters.RemoveAll(a => a.AdapterKey == adapter.AdapterKey);
            _adapters.Add(adapter);
        }
    }

    public async Task<IBrowserAdapter?> ResolveAdapterAsync(BrowserDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        List<IBrowserAdapter> snapshot;
        lock (_lock)
        {
            snapshot = _adapters.ToList();
        }

        foreach (var adapter in snapshot)
        {
            if (await adapter.CanAutomateAsync(descriptor, cancellationToken))
            {
                return adapter;
            }
        }

        return null;
    }

    public IReadOnlyList<IBrowserAdapter> GetRegisteredAdapters()
    {
        lock (_lock)
        {
            return _adapters.AsReadOnly();
        }
    }
}
