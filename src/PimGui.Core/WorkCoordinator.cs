namespace PimGui.Core;

public enum WorkKind
{
    Connection, Runtimes, Catalog, RuntimeMutation, OfflineDownload,
    Build, BuildVersions, Environments, Packages, Storage, Configuration, NetworkSettings, AppUpdate
}

/// <summary>Exclusive ownership of affected resources, not a global application lock.</summary>
public sealed class WorkCoordinator
{
    [Flags]
    private enum Resource { Manager = 1, Inventory = 2, Catalog = 4, Build = 8, Environments = 16, Versions = 32, Update = 64 }
    private readonly object sync = new();
    private readonly Dictionary<long, WorkKind> running = [];
    private long nextId;
    private static Resource Resources(WorkKind kind) => kind switch
    {
        WorkKind.Connection => Resource.Manager | Resource.Inventory | Resource.Catalog,
        WorkKind.Runtimes => Resource.Manager | Resource.Inventory,
        WorkKind.Catalog => Resource.Catalog,
        WorkKind.RuntimeMutation => Resource.Manager | Resource.Inventory | Resource.Build | Resource.Environments,
        WorkKind.OfflineDownload => Resource.Manager,
        WorkKind.Build => Resource.Build,
        WorkKind.BuildVersions => Resource.Versions,
        WorkKind.Environments => Resource.Environments | Resource.Manager,
        WorkKind.Packages => Resource.Environments,
        WorkKind.Storage => Resource.Build | Resource.Environments | Resource.Inventory,
        WorkKind.Configuration => Resource.Manager | Resource.Catalog,
        // Network/source delegates are read at multiple stages by PIM. Keep their settings
        // stable until consumers finish, while appearance and local filters stay editable.
        WorkKind.NetworkSettings => Resource.Manager | Resource.Catalog | Resource.Build | Resource.Environments | Resource.Versions | Resource.Update,
        WorkKind.AppUpdate => Resource.Update,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    public bool Any { get { lock (sync) return running.Count > 0; } }
    public bool Contains(WorkKind kind) { lock (sync) return running.ContainsValue(kind); }
    public bool CanStart(WorkKind kind)
    {
        var requested = Resources(kind);
        lock (sync) return running.Values.All(active => (Resources(active) & requested) == 0);
    }
    public IDisposable? TryStart(WorkKind kind, Action? released = null)
    {
        lock (sync)
        {
            if (!CanStart(kind)) return null;
            var id = ++nextId; running.Add(id, kind);
            return new Lease(() => { lock (sync) running.Remove(id); released?.Invoke(); });
        }
    }
    private sealed class Lease(Action release) : IDisposable
    {
        private Action? releaseOnce = release;
        public void Dispose() => Interlocked.Exchange(ref releaseOnce, null)?.Invoke();
    }
}
