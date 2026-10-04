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
    Action<FalloutPackageEvent, FalloutFormKey> execute, Func<int> playerLevel)
{
    internal const string ContinuationBlocker = "Unloaded actor package assignment awaits its native procedure continuation.";
    private readonly Dictionary<FalloutFormKey, FalloutPackageEvents> _actors = [];
    private readonly HashSet<FalloutFormKey> _evaluating = [];

    internal object State => _actors.Select(value => new
    {
        reference = value.Key.ToString(),
        package = value.Value.Active?.Form.ToString(),
        value.Value.Done,
        value.Value.Error,
        value.Value.LastEvent,
        procedure = "deferred-to-resident-native-owner"
    }).ToArray();

    internal FalloutFormKey? CurrentPackage(FalloutFormKey actor)
    {
        if (world.IsResident(actor)) throw new NotSupportedException($"Resident actor {actor} has no native package owner.");
        var state = world.Get(actor);
        FalloutReferencePackageEvents.RequireActor(records, actor);
        if (state.ScriptError is { } failure) throw new NotSupportedException(failure);
        if (state.PackageBindingFailure is { } stopped) return stopped.Package;
        if (state.SelectionFailure is not null) return null;
        var templates = state.Templates ?? world.InitializeActorTemplates(actor, playerLevel(), globals);
        if (!_actors.TryGetValue(actor, out var events))
        {
            _actors.Add(actor, events = new((package, kind) => Dispatch(actor, package, kind)));
            if (state.PackageAssignment is { } retained)
                retained.Bind(records, events);
        }
        if (!_evaluating.Add(actor)) return events.Active?.Form;
        try
        {
            var selected = FalloutAiPackages.Select(records, state.Base, condition => Evaluate(actor, condition),
                templates, clock, evaluateRunOn: true,
                eligible: package => world.PackageEligible(actor, package, clock, events.Active?.Form, events.Done));
            events.Change(selected is null ? null : FalloutScriptPackage.Read(selected));
            Remember(actor, events);
            if (events.Active is { } active && state.PackageMotion?.Package != active.Form && state.FurnitureContinuation?.Assignment.Package != active.Form && state.DialogueContinuation?.Assignment.Package != active.Form)
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
        else if (events.Active is { } active) retained.Restore(active, events.Done);
        _actors.Add(actor, retained);
        Remember(actor, retained);
    }

    internal void BindNative(FalloutFormKey actor, FalloutPackageEvents events)
    {
        var state = world.Get(actor);
        FalloutScriptPackage? active;
        bool done;
        if (_actors.Remove(actor, out var previous))
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
        if (state.ProcedureCaptureBlocker == ContinuationBlocker) state.ProcedureCaptureBlocker = null;
    }

    private float Evaluate(FalloutFormKey caller, FalloutCondition condition)
    {
        if (world.EvaluateActorReferenceCondition(caller, condition) is { } referenceQuery) return referenceQuery;
        if (condition.Function is 56 or 58 or 59 or 79 or 546) return quests.Evaluate(condition);
        if (condition.Function == 18) return (clock ?? throw new NotSupportedException("Unloaded AI has no simulation clock.")).Hour;
        if (condition.Function == 74) return (globals ?? throw new NotSupportedException("Unloaded AI has no global state.")).Get(condition.FormArgument1);
        if (condition.Function == 84) return world.GetDeadCount(condition.FormArgument1);
        if (condition.Function == 53) return (float)world.ReadVariable(quests, condition.FormArgument1, condition.Argument2);
        if (condition.Function == 161) return FalloutAiPackages.IsCurrentPackage(condition, caller, world.CurrentPackage) ? 1 : 0;
        if (condition.Function == 50) return FalloutAiPackages.HasTalkedToPlayer(condition, caller,
            reference => world.Get(reference).TalkedToPlayer) ? 1 : 0;
        var actor = FalloutAiPackages.ConditionSubject(condition, caller);
        return condition.Function switch
        {
            35 => world.IsEnabled(actor) ? 0 : 1,
            72 => FalloutReferenceIdentity.Base(records, actor) == condition.FormArgument1 ? 1 : 0,
            71 => world.ActorFactions(actor).GetValueOrDefault(condition.FormArgument1, (sbyte)-1) >= 0 ? 1 : 0,
            73 => world.ActorFactions(actor).GetValueOrDefault(condition.FormArgument1, (sbyte)-1),
            289 => world.IsInCombat(actor) ? 1 : 0,
            300 => world.IsInInterior(actor) ? 1 : 0,
            _ => throw new NotSupportedException($"Unloaded actor {actor} package condition {condition.Owner.FormKey}/{condition.Function} is unbound.")
        };
    }

    private void Remember(FalloutFormKey actor, FalloutPackageEvents events) =>
        world.Get(actor).PackageAssignment = FalloutActorPackageAssignment.Capture(records, events);

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
