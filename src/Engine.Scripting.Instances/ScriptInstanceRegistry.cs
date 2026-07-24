using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Engine.Scripting.Instances;

/// <summary>
/// Thread-safe map from stable <see cref="ScriptHandle"/> identities to the current script
/// instance, letting the host swap instances behind an identity without consumers noticing.
/// </summary>
/// <remarks>
/// Consumers keep the handle and resolve the instance on every use via <see cref="GetAs{T}"/> —
/// the returned reference must not be stored in long-lived fields (that is precisely the strong
/// reference that prevents the old generation from unloading). The reload pipeline uses
/// <see cref="GetLiveEntries"/>, <see cref="DetachAll"/> and <see cref="Attach"/> to swap
/// generations.
/// </remarks>
public sealed class ScriptInstanceRegistry
{
    private readonly ILogger<ScriptInstanceRegistry> _logger;
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, ScriptInstanceEntry> _entries = [];

    /// <summary>Creates the registry.</summary>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    public ScriptInstanceRegistry(ILogger<ScriptInstanceRegistry>? logger = null)
    {
        _logger = logger ?? NullLogger<ScriptInstanceRegistry>.Instance;
    }

    /// <summary>Snapshot of every registered handle.</summary>
    public IReadOnlyList<ScriptHandle> Handles
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries.Select(pair => new ScriptHandle(pair.Key, pair.Value.TypeFullName))];
            }
        }
    }

    /// <summary>Registers a live instance under a new stable identity.</summary>
    /// <param name="instance">The script instance.</param>
    /// <returns>The stable handle for this script.</returns>
    public ScriptHandle Register(object instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var type = instance.GetType();
        return Register(type.FullName ?? type.Name, instance);
    }

    /// <summary>Registers a live instance under a new stable identity with an explicit type name.</summary>
    /// <param name="typeFullName">Full name used to re-create the instance from future generations.</param>
    /// <param name="instance">The script instance.</param>
    /// <returns>The stable handle for this script.</returns>
    public ScriptHandle Register(string typeFullName, object instance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeFullName);
        ArgumentNullException.ThrowIfNull(instance);

        var handle = new ScriptHandle(Guid.NewGuid(), typeFullName);
        lock (_gate)
        {
            _entries.Add(handle.Id, new ScriptInstanceEntry { TypeFullName = typeFullName, Instance = instance });
        }

        Log.InstanceRegistered(_logger, handle.Id, typeFullName);
        return handle;
    }

    /// <summary>Removes a script identity entirely.</summary>
    /// <param name="handle">The handle to remove.</param>
    /// <returns><see langword="true"/> when the handle was registered.</returns>
    public bool Unregister(ScriptHandle handle)
    {
        bool removed;
        lock (_gate)
        {
            removed = _entries.Remove(handle.Id);
        }

        if (removed)
        {
            Log.InstanceUnregistered(_logger, handle.Id, handle.TypeFullName);
        }

        return removed;
    }

    /// <summary>
    /// Resolves the current instance behind <paramref name="handle"/> as <typeparamref name="T"/>,
    /// or <see langword="null"/> when the handle is unknown, detached, or of another type.
    /// </summary>
    /// <typeparam name="T">Contract type to resolve the instance as.</typeparam>
    /// <param name="handle">The stable script identity.</param>
    public T? GetAs<T>(ScriptHandle handle) where T : class
    {
        lock (_gate)
        {
            return _entries.TryGetValue(handle.Id, out var entry) ? entry.Instance as T : null;
        }
    }

    /// <summary>Resolves the current instance behind <paramref name="handle"/>.</summary>
    /// <param name="handle">The stable script identity.</param>
    /// <param name="instance">The current instance, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when a live instance is attached.</returns>
    public bool TryGetInstance(ScriptHandle handle, out object? instance)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(handle.Id, out var entry) && entry.Instance is not null)
            {
                instance = entry.Instance;
                return true;
            }
        }

        instance = null;
        return false;
    }

    /// <summary>
    /// Snapshot of every handle with a live instance — the reload pipeline's working set.
    /// Callers inside the pipeline must drop the returned list before unload verification.
    /// </summary>
    public IReadOnlyList<KeyValuePair<ScriptHandle, object>> GetLiveEntries()
    {
        lock (_gate)
        {
            var live = new List<KeyValuePair<ScriptHandle, object>>(_entries.Count);
            foreach (var (id, entry) in _entries)
            {
                if (entry.Instance is not null)
                {
                    live.Add(new KeyValuePair<ScriptHandle, object>(new ScriptHandle(id, entry.TypeFullName), entry.Instance));
                }
            }

            return live;
        }
    }

    /// <summary>
    /// Drops every instance reference while keeping the identities — the step that makes the
    /// registry stop rooting the old generation right before it is unloaded.
    /// </summary>
    public void DetachAll()
    {
        int detached;
        lock (_gate)
        {
            detached = 0;
            foreach (var entry in _entries.Values)
            {
                if (entry.Instance is not null)
                {
                    entry.Instance = null;
                    detached++;
                }
            }
        }

        Log.InstancesDetached(_logger, detached);
    }

    /// <summary>Attaches the new generation's instance behind an existing identity.</summary>
    /// <param name="handle">The stable script identity.</param>
    /// <param name="newInstance">Instance created from the new generation.</param>
    /// <returns><see langword="false"/> when the handle is not registered.</returns>
    public bool Attach(ScriptHandle handle, object newInstance)
    {
        ArgumentNullException.ThrowIfNull(newInstance);

        lock (_gate)
        {
            if (_entries.TryGetValue(handle.Id, out var entry))
            {
                entry.Instance = newInstance;
                return true;
            }
        }

        Log.AttachUnknownHandle(_logger, handle.Id);
        return false;
    }
}
