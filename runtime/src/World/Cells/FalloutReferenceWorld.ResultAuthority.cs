using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutPackageResultSnapshot(FalloutFormKey Package, string Kind,
    long Executions, FalloutScriptResultReceipt Receipt);

internal sealed partial class FalloutReferenceWorld
{
    private readonly Dictionary<FalloutFormKey,
        Dictionary<(FalloutFormKey Package, string Kind), FalloutPackageResultSnapshot>> _packageResults = [];

    internal void RecordPackageResult(FalloutPackageEvent program, FalloutFormKey actor, FalloutScriptResultReceipt receipt)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        FalloutReferencePackageEvents.RequireActor(records, actor);
        program.Scope.RequireSource(records.GetEffective(program.Package.FormKey));
        receipt.Require(program.Scope, actor);
        _ = Get(actor);
        if (!_packageResults.TryGetValue(actor, out var results)) _packageResults.Add(actor, results = []);
        var key = (program.Package.FormKey, program.Kind);
        var count = checked((results.GetValueOrDefault(key)?.Executions ?? 0) + 1);
        results[key] = new(program.Package.FormKey, program.Kind, count, receipt);
    }

    private IReadOnlyList<FalloutPackageResultSnapshot>? CapturePackageResults(FalloutFormKey actor) =>
        _packageResults.TryGetValue(actor, out var results) && results.Count > 0
            ? results.Values.OrderBy(result => records.RuntimeFormId(result.Package))
                .ThenBy(result => result.Kind, StringComparer.Ordinal).ToArray()
            : null;

    private void RestorePackageResults(IReadOnlyList<FalloutReferenceSnapshot> snapshots)
    {
        if (_packageResults.Count != 0) throw new InvalidOperationException("Package result restoration requires a fresh owner.");
        ValidateRecordedPackageResults(records, snapshots);
        foreach (var snapshot in snapshots)
            if (snapshot.PackageResults is { Count: > 0 } results)
                _packageResults.Add(snapshot.Reference, results.ToDictionary(result => (result.Package, result.Kind)));
    }

    internal static void ValidateRecordedPackageResults(FalloutPluginStack records,
        IReadOnlyList<FalloutReferenceSnapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            var completed = new Dictionary<(FalloutFormKey Package, string Kind), FalloutPackageResultSnapshot>();
            foreach (var result in snapshot.PackageResults ?? [])
            {
                FalloutReferencePackageEvents.RequireActor(records, snapshot.Reference);
                if (result is null || result.Executions <= 0 || result.Receipt is null ||
                    result.Kind is not ("POBA" or "POEA" or "POCA") ||
                    !completed.TryAdd((result.Package, result.Kind), result))
                    throw new InvalidDataException("Saved package result identity, execution count or receipt is invalid or duplicated.");
                var source = records.GetEffective(result.Package);
                if (source.Signature != "PACK" || source.IsDeleted)
                    throw new InvalidDataException("Saved package result has no winning lifecycle owner.");
                result.Receipt.Require(FalloutScriptScope.PackageEvent(source, result.Kind), snapshot.Reference);
            }
            var events = new HashSet<(FalloutFormKey Package, string Kind)>();
            // Source initial furniture placement can bind a physical procedure
            // before any lifecycle event. It supplies no result authority.
            var initialPlacement = snapshot.FurnitureContinuation is { InitialPlacement: true, EventRevision: 0 };
            if (!initialPlacement && snapshot.PackageAssignment is { } assigned)
            {
                events.Add((assigned.Package, "POBA"));
                if (assigned.Done) events.Add((assigned.Package, "POEA"));
            }
            if (!initialPlacement && snapshot.PackageMotion is { } motion)
            {
                events.Add((motion.Package, "POBA"));
                if (motion.DialogueCompleted || motion.Patrol?.Complete == true || motion.Escort?.Complete == true ||
                    motion.EditorTravel?.Complete == true || motion.Travel?.Complete == true)
                    events.Add((motion.Package, "POEA"));
            }
            if (snapshot.DeferredPackageContinuation is { } deferred) events.Add((deferred.Assignment.Package, "POBA"));
            if (snapshot.DialogueContinuation is { LastPackage: { } dialoguePackage, LastEvent: { } dialogueEvent })
                events.Add((dialoguePackage, dialogueEvent));
            if (snapshot.FurnitureContinuation is { EventRevision: > 0, LastPackage: { } furniturePackage, LastEvent: { } furnitureEvent })
                events.Add((furniturePackage, furnitureEvent));
            foreach (var retirement in new[] { snapshot.PackageBindingFailure?.Retirement, snapshot.SelectionFailure?.Retirement,
                snapshot.PendingPackageSelection?.Retirement }.OfType<FalloutPackageRetirement>())
                if (retirement.LastPackage is { } package && retirement.LastEvent is { } kind) events.Add((package, kind));
            foreach (var (package, kind) in events)
            {
                var source = records.GetEffective(package);
                if (source.Signature != "PACK" || source.IsDeleted || kind is not ("POBA" or "POEA" or "POCA"))
                    throw new InvalidDataException("Recorded package result has no winning original lifecycle owner.");
                var definition = FalloutScriptPackage.Read(source);
                if (definition.EventPrograms.GetValueOrDefault(kind) is { } program &&
                    FalloutScriptResultReceipt.HasProgram(program.Scope))
                {
                    if (!completed.TryGetValue((package, kind), out var actual))
                        throw new NotSupportedException("Consumed package result has no completed source-range execution receipt.");
                    actual.Receipt.Require(program.Scope, snapshot.Reference);
                }
            }
            // Pending actor OnPackage marks and future selected packages are
            // declarations, not consumed PACK result receipts. They cannot
            // lend authority to a different event or force it to execute.
        }
    }
}
