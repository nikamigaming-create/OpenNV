using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private readonly Dictionary<FalloutFormKey, (FalloutPluginRecord Record, FalloutScriptPackage Definition, string Hash)>
        _scriptPackageSources = [];

    internal IEnumerable<FalloutFormKey> ScriptPackageActors => _instances.Values
        .Where(actor => actor.ScriptPackage?.Package is not null).Select(actor => actor.Reference);

    internal long ActorScriptPackageRevision(FalloutFormKey actor) => Get(actor).ScriptPackage?.Revision ?? 0;

    private (FalloutPluginRecord Record, FalloutScriptPackage Definition, string Hash) ScriptPackageSource(FalloutFormKey package)
    {
        if (_scriptPackageSources.TryGetValue(package, out var source)) return source;
        var record = records.GetEffective(package);
        var definition = FalloutScriptPackage.Read(record);
        source = (record, definition, Convert.ToHexString(SHA256.HashData(record.ReadData())));
        _scriptPackageSources.Add(package, source);
        return source;
    }

    internal bool ApplyActorScriptPackage(FalloutFormKey actor, FalloutFormKey? package)
    {
        FalloutReferencePackageEvents.RequireActor(records, actor);
        var state = Actor(actor);
        if (state.Deleted || state.DeletePending)
            throw new InvalidOperationException("Cannot assign a script package to a retired actor.");
        if (package is null && state.ScriptPackage?.Package is null) return false;
        var source = package is { } key ? ScriptPackageSource(key) : default;
        var revision = checked(ActorScriptPackageRevision(actor) + 1);
        var next = new FalloutActorScriptPackageSnapshot(actor, revision, package,
            package is null ? null : source.Hash, package is not null);
        next.Validate();
        state.PendingPackageChoice = null;
        state.ScriptPackage = next;
        return true;
    }

    // AddScriptPackage directly selects the caller's override. Normal PKID
    // schedules, conditions and once-per-day eligibility are not this command's
    // election. They remain authoritative when the override is retired.
    internal FalloutPluginRecord? SelectActorPackage(FalloutFormKey actor, Func<FalloutCondition, float> evaluate,
        FalloutActorTemplateSelection? templates, FalloutGameTime? clock, FalloutFormKey? current, bool done,
        bool reevaluateScript = true, bool locationReached = false)
    {
        FalloutReferencePackageEvents.RequireActor(records, actor);
        var state = Actor(actor);
        if (state.Deleted) { RetireDeletedActorScriptPackage(state); return null; }
        if (state.ScriptPackage is { Package: { } package } slot)
        {
            var source = ScriptPackageSource(package);
            if (!source.Hash.Equals(slot.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Actor script-package source changed within its immutable selection.");
            if (slot.Pending || !reevaluateScript) return source.Record;
            if (current != package)
                throw new NotSupportedException("Actor script package lost its actual assigned procedure continuation.");
            var flags = source.Definition.Flags;
            if (!done && ((flags & 4) != 0 || (flags & 2) != 0 && !locationReached)) return source.Record;
            RetireActorScriptPackage(actor, package, slot.Revision);
        }
        if (!done && current is { } existing && FalloutAiPackages.OnPriorityList(records, state.Base, existing, templates))
        {
            var previous = ScriptPackageSource(existing);
            if ((previous.Definition.Flags & 4) != 0 || (previous.Definition.Flags & 2) != 0 && !locationReached)
                return previous.Record;
        }
        return FalloutAiPackages.Select(records, state.Base, evaluate, templates, clock,
            evaluateRunOn: true, eligible: candidate => PackageEligible(actor, candidate, clock, current, done));
    }

    internal long BeginActorScriptPackage(FalloutFormKey actor, FalloutFormKey package, long selectedRevision)
    {
        var state = Actor(actor);
        if (ActorScriptPackageRevision(actor) != selectedRevision)
            throw new InvalidOperationException("Superseded package selection cannot publish a begin event.");
        if (state.ScriptPackage is not { Package: { } assigned } slot || assigned != package ||
            slot.Revision != selectedRevision) return 0;
        state.ScriptPackage = slot with { Pending = false };
        return slot.Revision;
    }

    internal bool RetireActorScriptPackage(FalloutFormKey actor, FalloutFormKey package, long startedRevision)
    {
        var state = Actor(actor);
        if (state.ScriptPackage is not { Package: { } assigned, Pending: false } slot ||
            assigned != package || slot.Revision != startedRevision) return false;
        var next = slot with { Revision = checked(slot.Revision + 1), Package = null, Sha256 = null, Pending = false };
        next.Validate();
        state.ScriptPackage = next;
        return true;
    }

    private void RetireDeletedActorScriptPackage(FalloutReferenceInstance actor)
    {
        RetireActualActorUpdate(actor, "actual-source-reference-deletion");
        actor.PendingPackageChoice = null;
        if (actor.ScriptPackage is not { Package: not null } slot) return;
        var next = slot with { Revision = checked(slot.Revision + 1), Package = null, Sha256 = null, Pending = false };
        next.Validate();
        actor.ScriptPackage = next;
    }

    private void RestoreActorScriptPackage(FalloutReferenceInstance actor, FalloutReferenceSnapshot snapshot)
    {
        if (snapshot.ScriptPackage is { } slot)
        {
            slot.Validate(records, actor);
            actor.ScriptPackage = slot;
        }
        if (snapshot.PendingPackageChoice is { } choice)
        {
            _ = choice.Bind(records, actor);
            actor.PendingPackageChoice = choice;
        }
    }

    internal FalloutActorPackageChoice QueueActorPackageChoice(FalloutFormKey actor, FalloutPluginRecord? package)
    {
        var state = Actor(actor);
        var choice = new FalloutActorPackageChoice(actor, ActorScriptPackageRevision(actor), package?.FormKey,
            package is null ? null : ScriptPackageSource(package.FormKey).Hash);
        _ = choice.Bind(records, state);
        state.PendingPackageChoice = choice;
        return choice;
    }

    private void ValidateActorScriptPackageCapture(IReadOnlyList<FalloutReferenceSnapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            snapshot.ScriptPackage?.ValidateContinuation(snapshot);
            snapshot.ScriptPackage?.Validate(records, Get(snapshot.Reference));
            if (snapshot.PackageAssignment is { ScriptPackageRevision: > 0 } && snapshot.ScriptPackage is null)
                throw new InvalidDataException("Actor procedure lost its script-package epoch owner.");
            if (snapshot.PendingPackageChoice is { } choice) _ = choice.Bind(records, Get(snapshot.Reference));
        }
    }

    internal bool RetainedPackageLocationReached(FalloutFormKey actor, FalloutFormKey? package)
    {
        var state = Actor(actor);
        if (package is null) return false;
        if (state.PackageMotion is { } motion && motion.Package == package)
            return motion.Travel?.Complete == true || motion.EditorTravel?.Complete == true ||
                motion.Escort?.Complete == true || motion.Guard?.Complete == true || motion.DialogueCompleted;
        return state.FurnitureContinuation is { Phase: 3 or 4 } furniture && furniture.Assignment.Package == package;
    }

    internal bool HasRetainedNativePackageState(FalloutFormKey actor)
    {
        var state = Actor(actor);
        return state.PackageMotion is not null || state.FurnitureContinuation is not null || state.DialogueContinuation is not null ||
            state.PackageBindingFailure is not null || state.SelectionFailure is not null || state.PendingPackageSelection is not null ||
            state.Animation.Resource.Length != 0;
    }
}
