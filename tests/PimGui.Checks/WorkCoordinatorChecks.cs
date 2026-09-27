using PimGui.Core;

internal static class WorkCoordinatorChecks
{
    public static void Run(Action<string, Action> check)
    {
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        check("Independent catalog, build, package and update work can overlap", () =>
        {
            var gate = new WorkCoordinator();
            using var catalog = gate.TryStart(WorkKind.Catalog);
            using var build = gate.TryStart(WorkKind.Build);
            using var packages = gate.TryStart(WorkKind.Packages);
            using var update = gate.TryStart(WorkKind.AppUpdate);
            Require(catalog is not null && build is not null && packages is not null && update is not null, "Unrelated work blocked");
            Require(!gate.CanStart(WorkKind.NetworkSettings), "Network policy changed under a consumer");
        });
        check("Runtime changes exclude dependent consumers and cleanup but allow catalog refresh", () =>
        {
            var gate = new WorkCoordinator(); using var mutation = gate.TryStart(WorkKind.RuntimeMutation);
            foreach (var kind in new[] { WorkKind.Runtimes, WorkKind.Build, WorkKind.Packages, WorkKind.Environments, WorkKind.Storage, WorkKind.Configuration })
                Require(!gate.CanStart(kind), "Unsafe overlap: " + kind);
            Require(gate.CanStart(WorkKind.Catalog) && gate.CanStart(WorkKind.AppUpdate), "Queries blocked by mutation");
        });
        check("A completed or twice-disposed lease cannot unlock another task", () =>
        {
            var gate = new WorkCoordinator(); var first = gate.TryStart(WorkKind.Catalog)!;
            using var update = gate.TryStart(WorkKind.AppUpdate); first.Dispose();
            using var next = gate.TryStart(WorkKind.Catalog); first.Dispose();
            Require(gate.Any && !gate.CanStart(WorkKind.Catalog) && !gate.CanStart(WorkKind.AppUpdate), "Stale completion unlocked active work");
        });
        check("Failure releases only its own resources and duplicate work cannot enter", () =>
        {
            var gate = new WorkCoordinator(); using var build = gate.TryStart(WorkKind.Build);
            try { using var update = gate.TryStart(WorkKind.AppUpdate); throw new IOException("fixture"); } catch (IOException) { }
            Require(gate.CanStart(WorkKind.AppUpdate) && !gate.CanStart(WorkKind.Build), "Failure recovery released wrong owner");
            Require(gate.TryStart(WorkKind.Build) is null, "Duplicate build entered");
        });
        check("Work ownership is atomic under concurrent starts", () =>
        {
            var gate = new WorkCoordinator(); var leases = new System.Collections.Concurrent.ConcurrentBag<IDisposable>();
            Parallel.For(0, 32, _ => { if (gate.TryStart(WorkKind.Storage) is { } lease) leases.Add(lease); });
            Require(leases.Count == 1, "Multiple exclusive owners acquired");
            foreach (var lease in leases) lease.Dispose();
            Require(!gate.Any, "Ownership leaked");
        });
    }
}
