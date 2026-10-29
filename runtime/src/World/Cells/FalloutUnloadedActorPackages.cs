using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using System.Security.Cryptography;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutActorPackageAssignment(FalloutFormKey Package, string Sha256, bool Done)
{
    internal static FalloutActorPackageAssignment? Capture(FalloutPluginStack records, FalloutPackageEvents? events) => events?.Active is { } active
        ? new(active.Form, Convert.ToHexString(SHA256.HashData(records.GetEffective(active.Form).ReadData())), events.Done) : null;
    internal void Validate()
    {
        if (Package.ObjectId == 0 || string.IsNullOrWhiteSpace(Package.OwnerPlugin) ||
            Sha256 is not { Length: 64 } || !Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Saved actor package assignment is invalid.");
    }

    internal void Bind(FalloutPluginStack records, FalloutPackageEvents events)
    {
        Validate();
        var source = records.GetEffective(Package);
        if (source.Signature != "PACK" || !Convert.ToHexString(SHA256.HashData(source.ReadData())).Equals(Sha256, StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Saved actor package assignment differs from its winning source.");
        events.Restore(FalloutScriptPackage.Read(source), Done);
    }
}

// Assignment exists independently of a resident body. This owner never
// invents native movement, arrival, actor presentation or package completion.
internal sealed class FalloutUnloadedActorPackages(FalloutPluginStack records, FalloutReferenceWorld world,
    FalloutQuestState quests, FalloutGameTime? clock, FalloutGlobalState? globals,
    Action<FalloutPackageEvent, FalloutFormKey> execute, Func<int> playerLevel,
    Func<FalloutFormKey, int>? sitting = null, float unitsToMetres = 1,
    Func<FalloutReferencePlacement?>? playerPlacement = null)
{
    internal const string ContinuationBlocker = "Unloaded actor package assignment awaits its native procedure continuation.";
    private readonly Dictionary<FalloutFormKey, FalloutPackageEvents> _actors = [];
    private readonly HashSet<FalloutFormKey> _evaluating = [];
    private readonly HashSet<FalloutFormKey> _deferred = [];

    internal object State => _actors.Select(value => new
    {
        reference = value.Key.ToString(),
        package = value.Value.Active?.Form.ToString(),
        value.Value.Done,
        value.Value.Error,
        value.Value.LastEvent,
        value.Value.Revision,
        deferred = world.Get(value.Key).DeferredPackageContinuation,
        procedure = "deferred-to-resident-native-owner"
    }).ToArray();

    internal FalloutFormKey? CurrentPackage(FalloutFormKey actor)
    {
        if (world.IsResident(actor)) throw new NotSupportedException($"Resident actor {actor} has no native package owner.");
        var state = world.Get(actor);
        FalloutReferencePackageEvents.RequireActor(records, actor);
        if (state.ScriptError is { } failure) throw new NotSupportedException(failure);
        if (state.PendingPackageSelection is { } pending) return pending.Package;
        if (state.PendingSandboxStart is { } pendingStart) return pendingStart.Package;
        if (state.PackageBindingFailure is { } stopped) return stopped.Package;
        if (state.SandboxAdmissionFailure is { } sandbox) return sandbox.Package;
        if (state.SandboxMotionFailure is { } motionFailure) return motionFailure.Package;
        if (state.SelectionFailure is not null) return null;
        var templates = state.Templates ?? world.InitializeActorTemplates(actor, playerLevel(), globals);
        if (!_actors.TryGetValue(actor, out var events))
        {
            _actors.Add(actor, events = new((package, kind) => Dispatch(actor, package, kind)));
            if (state.DeferredPackageContinuation is { } deferred)
            {
                deferred.Assignment.Bind(records, events);
                events.RestoreHistory(deferred.Revision, "POBA", deferred.Assignment.Package);
                _deferred.Add(actor);
            }
            else if (state.PackageAssignment is { } retained)
                retained.Bind(records, events);
            else _deferred.Add(actor);
        }
        if (!_evaluating.Add(actor)) return events.Active?.Form;
        try
        {
            var selected = FalloutAiPackages.Select(records, state.Base, condition => Evaluate(actor, condition),
                templates, clock, evaluateRunOn: true,
                eligible: package => world.PackageEligible(actor, package, clock, events.Active?.Form, events.Done));
            var predecessor = state.DeferredPackageContinuation?.RetainedAssignment ??
                (state.PackageMotion is { } previousMotion && state.PackageAssignment is { } previousAssignment &&
                    previousMotion.Package == previousAssignment.Package ? previousAssignment : null);
            var revision = events.Revision;
            try { events.Change(selected is null ? null : FalloutScriptPackage.Read(selected)); }
            catch
            {
                state.DeferredPackageContinuation = null;
                state.ProcedureCaptureBlocker = ContinuationBlocker;
                throw;
            }
            if (events.Revision != revision) _deferred.Add(actor);
            Remember(actor, events, predecessor);
            if (events.Active is { } active && state.PackageMotion?.Package != active.Form && state.FurnitureContinuation?.Assignment.Package != active.Form && state.DialogueContinuation?.Assignment.Package != active.Form ||
                _deferred.Contains(actor) && state.PackageMotion is not null)
                state.ProcedureCaptureBlocker = ContinuationBlocker;
            else if (state.ProcedureCaptureBlocker == ContinuationBlocker) state.ProcedureCaptureBlocker = null;
            return events.Active?.Form;
        }
        finally { _evaluating.Remove(actor); }
    }

    internal void Retain(FalloutFormKey actor, FalloutPackageEvents events)
    {
        if (_actors.ContainsKey(actor)) throw new InvalidOperationException("Actor already has an unloaded package lifecycle.");
        var retained = new FalloutPackageEvents((package, kind) => Dispatch(actor, package, kind));
        if (events.Error is { } failure) world.Get(actor).ScriptError ??= failure;
        else if (events.Active is { } active)
        {
            retained.Restore(active, events.Done);
            if (world.Get(actor).SandboxAdmissionFailure is { } sandbox)
            {
                sandbox.Validate(records, world.Get(actor));
                if (active.Form != sandbox.Package || events.Done || events.Revision != sandbox.EventRevision ||
                    events.LastEvent != sandbox.LastEvent || events.LastPackage != sandbox.LastPackage)
                    throw new InvalidDataException("Retiring Sandbox differs from its stopped lifecycle.");
            }
            if (world.Get(actor).SandboxMotionFailure is { } motionFailure)
            {
                motionFailure.Validate(records, world.Get(actor));
                if (active.Form != motionFailure.Package || events.Done || events.Revision != motionFailure.EventRevision ||
                    events.LastEvent != motionFailure.LastEvent || events.LastPackage != motionFailure.LastPackage)
                    throw new InvalidDataException("Retiring Sandbox motion differs from its stopped lifecycle.");
            }
            if (world.Get(actor).PendingSandboxStart is { } pendingStart)
            {
                pendingStart.Validate(records, world.Get(actor));
                if (active.Form != pendingStart.Package || events.Done || events.Revision != pendingStart.EventRevision ||
                    events.LastEvent != pendingStart.LastEvent || events.LastPackage != pendingStart.LastPackage)
                    throw new InvalidDataException("Retiring pending Sandbox start differs from its consumed lifecycle.");
            }
        }
        if (events.Error is null) retained.RestoreHistory(events.Revision, events.LastEvent, events.LastPackage);
        var state = world.Get(actor);
        var deferred = state.DeferredPackageContinuation;
        if (deferred is not null && (retained.Active?.Form != deferred.Assignment.Package ||
            retained.Done || retained.Error is not null || retained.Revision != deferred.Revision ||
            retained.LastEvent != "POBA" || retained.LastPackage != deferred.Assignment.Package))
        {
            state.DeferredPackageContinuation = null;
            state.ProcedureCaptureBlocker = ContinuationBlocker;
            throw new NotSupportedException("Native retirement differs from its deferred package boundary.");
        }
        _actors.Add(actor, retained);
        if (deferred is not null)
        {
            // Retirement precedes callback removal. Keep the actual boundary;
            // its unloaded admission becomes true only after native unbinding.
            _deferred.Add(actor);
            state.PackageAssignment = FalloutActorPackageAssignment.Capture(records, retained);
        }
        else
        {
            _deferred.Remove(actor);
            Remember(actor, retained);
        }
    }

    internal void BindNative(FalloutFormKey actor, FalloutPackageEvents events)
    {
        var state = world.Get(actor);
        if (state.PendingSandboxStart is { } pendingStart)
        {
            pendingStart.Validate(records, state);
            if (_actors.TryGetValue(actor, out var prior) && (prior.Error is not null || prior.Done ||
                prior.Active?.Form != pendingStart.Package || prior.Revision != pendingStart.EventRevision ||
                prior.LastEvent != pendingStart.LastEvent || prior.LastPackage != pendingStart.LastPackage))
                throw new InvalidDataException("Unloaded pending Sandbox start differs from its consumed lifecycle.");
            _actors.Remove(actor); _deferred.Remove(actor);
            pendingStart.RestoreEvents(records, state, events);
            return;
        }
        if (state.SandboxMotionFailure is { } motionFailure)
        {
            motionFailure.Validate(records, state);
            if (_actors.TryGetValue(actor, out var prior) && (prior.Error is not null || prior.Done ||
                prior.Active?.Form != motionFailure.Package || prior.Revision != motionFailure.EventRevision ||
                prior.LastEvent != motionFailure.LastEvent || prior.LastPackage != motionFailure.LastPackage))
                throw new InvalidDataException("Unloaded Sandbox motion differs from its stopped lifecycle.");
            _actors.Remove(actor); _deferred.Remove(actor);
            motionFailure.RestoreEvents(records, state, events);
            return;
        }
        if (state.SandboxAdmissionFailure is { } sandbox)
        {
            sandbox.Validate(records, state);
            if (_actors.TryGetValue(actor, out var prior) && (prior.Error is not null || prior.Done ||
                prior.Active?.Form != sandbox.Package || prior.Revision != sandbox.EventRevision ||
                prior.LastEvent != sandbox.LastEvent || prior.LastPackage != sandbox.LastPackage))
                throw new InvalidDataException("Unloaded Sandbox differs from its stopped lifecycle.");
            // The resident owner restores the exact history into fresh events.
            _actors.Remove(actor); _deferred.Remove(actor);
            return;
        }
        if (state.DeferredPackageContinuation is { } deferred)
        {
            deferred.BindNative(records, state, events);
            _actors.Remove(actor); _deferred.Remove(actor);
            return;
        }
        if (_deferred.Contains(actor) && state.ProcedureCaptureBlocker == ContinuationBlocker)
            throw new NotSupportedException("Unloaded package transition has no retained native continuation.");
        FalloutScriptPackage? active;
        bool done;
        if (events.Active is not null || events.Revision != 0 || events.LastEvent is not null || events.LastPackage is not null || events.Error is not null)
            throw new InvalidOperationException("Native binding requires a fresh package lifecycle.");
        if (_actors.TryGetValue(actor, out var previous))
        {
            if (previous.Error is { } failure) throw new NotSupportedException(failure);
            active = previous.Active; done = previous.Done;
        }
        else
        {
            active = state.PackageAssignment is { } retained ? FalloutScriptPackage.Read(records.GetEffective(retained.Package)) : null;
            done = state.PackageAssignment?.Done == true;
        }
        // Existing retained motion restores its lifecycle in the native
        // procedure owner. A newly selected assignment has no motion yet.
        if (active is not null && state.PackageMotion?.Package != active.Form && state.FurnitureContinuation?.Assignment.Package != active.Form && state.DialogueContinuation?.Assignment.Package != active.Form)
            events.Restore(active, done);
        // These native continuations restore their own complete lifecycle.
        // Ordinary movement has no second history owner.
        if (previous is not null && state.FurnitureContinuation is null && state.DialogueContinuation is null && state.SandboxMarker is null &&
            state.PackageBindingFailure is null && state.SelectionFailure is null && state.PendingPackageSelection is null)
            events.RestoreHistory(previous.Revision, previous.LastEvent, previous.LastPackage);
        _actors.Remove(actor);
        _deferred.Remove(actor);
        if (state.ProcedureCaptureBlocker == ContinuationBlocker) state.ProcedureCaptureBlocker = null;
    }

    private FalloutConditionRuntime? _conditions;
    private FalloutConditionRuntime Conditions => _conditions ??= new(records, quests, world)
    {
        Globals = globals,
        Hour = () => (clock ?? throw new NotSupportedException("Unloaded AI has no simulation clock.")).Hour,
        Sitting = sitting,
        ItemCount = (subject, item) => FalloutInventoryItemCounts.Count(records,
            world.Inventory(subject, playerLevel(), globals).Contents, item),
        PlayerPlacement = playerPlacement,
        UnitsToMetres = () => unitsToMetres,
    };

    private float Evaluate(FalloutFormKey caller, FalloutCondition condition) =>
        Conditions.Evaluate(condition, new(caller, LinkedReference: world.GetLinkedRef));
    private void Remember(FalloutFormKey actor, FalloutPackageEvents events, FalloutActorPackageAssignment? predecessor = null)
    {
        var state = world.Get(actor);
        state.PackageAssignment = FalloutActorPackageAssignment.Capture(records, events);
        state.DeferredPackageContinuation = _deferred.Contains(actor)
            ? FalloutActorDeferredPackageContinuation.Capture(records, state, events, predecessor) : null;
    }

    private void Dispatch(FalloutFormKey actor, FalloutScriptPackage package, string kind)
    {
        var state = world.Get(actor);
        try
        {
            if (kind == "POBA") world.MarkPackageStart(actor, records.GetEffective(package.Form), clock);
            world.PackageEvents.Mark(actor, package.Form, kind switch
            {
                "POBA" => FalloutReferencePackageEventKind.Start,
                "POCA" => FalloutReferencePackageEventKind.Change,
                _ => throw new InvalidDataException("Unloaded actor cannot publish native arrival.")
            });
            if (package.EventPrograms.GetValueOrDefault(kind) is { } program) execute(program, actor);
            if (package.Events.GetValueOrDefault(kind) is not null)
                throw new NotSupportedException("Unloaded package event idle requires its native animation continuation.");
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or FileNotFoundException)
        { state.ScriptError ??= $"Package {kind} {package.Form}: {error.Message}"; throw; }
    }
}
