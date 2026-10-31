using Function = OpenNV.Runtime.Content.Definitions.FalloutConditionFunction;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

// One function implementation serves CTDA consumers and script query adapters.
// Capabilities describe actual runtime owners. Absence never supplies a result.
internal sealed partial class FalloutConditionRuntime(FalloutPluginStack records, FalloutQuestState quests,
    FalloutReferenceWorld? world = null)
{
    internal FalloutGlobalState? Globals { get; init; }
    internal Func<float>? Hour { get; init; }
    internal Func<uint>? RandomPercent { get; init; }
    internal Func<FalloutFormKey, FalloutFormKey>? BaseIdentity { get; init; }
    internal Func<FalloutFormKey, FalloutDialogueSpeaker?>? Identity { get; init; }
    internal Func<FalloutFormKey, FalloutFormKey>? Race { get; init; }
    internal Func<bool>? PlayerFemale { get; init; }
    internal Func<FalloutFormKey, IReadOnlyDictionary<FalloutFormKey, sbyte>>? Factions { get; init; }
    internal Func<FalloutFormKey, int, float>? ActorValue { get; init; }
    internal Func<FalloutFormKey, FalloutFormKey, double>? ItemCount { get; init; }
    internal Func<FalloutFormKey, FalloutFormKey, bool?>? Equipped { get; init; }
    internal Func<FalloutFormKey, FalloutFormKey, bool>? HasPerk { get; init; }
    internal Func<FalloutFormKey, bool>? IsDead { get; init; }
    internal Func<FalloutFormKey, float>? HealthPercentage { get; init; }
    internal Func<FalloutFormKey, bool>? TalkedToPlayer { get; init; }
    internal Func<FalloutFormKey, int>? DeadCount { get; init; }
    internal Func<FalloutFormKey, int>? Sitting { get; init; }
    internal Func<FalloutFormKey, int>? Sleeping { get; init; }
    internal Func<FalloutFormKey, FalloutFormKey?>? CurrentPackage { get; init; }
    internal Func<FalloutFormKey, FalloutCondition, FalloutFormKey?>? Cell { get; init; }
    internal Func<FalloutFormKey, FalloutFormKey, float>? Distance { get; init; }
    internal Func<FalloutFormKey, FalloutFormKey, bool>? SameCell { get; init; }
    internal Func<FalloutFormKey, FalloutFormKey, bool>? InCell { get; init; }
    internal Func<FalloutFormKey, bool>? Interior { get; init; }
    internal Func<FalloutFormKey, bool>? InCombat { get; init; }
    internal Func<FalloutFormKey, bool>? Sneaking { get; init; }
    internal Func<FalloutFormKey, bool>? WeaponOut { get; init; }
    internal Func<FalloutFormKey, uint?>? WeaponAnimationType { get; init; }
    internal Func<FalloutFormKey, int>? OpenState { get; init; }
    internal Func<FalloutFormKey, FalloutFormKey, bool>? InZone { get; init; }
    internal Func<FalloutReferencePlacement?>? PlayerPlacement { get; init; }
    internal Func<float> UnitsToMetres { get; init; } = () => 1;
    internal Func<int>? Vampire { get; init; }
    internal Func<FalloutFormKey, FalloutConditionQuery, float>? Native { get; init; }
    internal Func<FalloutCondition, float>? Global { get; init; }

    internal float Evaluate(FalloutCondition declaration, FalloutConditionScope scope) => Evaluate(new FalloutConditionQuery(declaration), scope);

    internal float Evaluate(FalloutConditionQuery condition, FalloutConditionScope scope)
    {
        var result = EvaluateValue(condition, scope);
        return float.IsFinite(result) ? result : throw new InvalidDataException("Condition returned a non-finite result.");
    }

    private float EvaluateValue(FalloutConditionQuery condition, FalloutConditionScope scope)
    {
        if (condition.RunOn > 4 || condition.RunOn == 3)
            throw new NotSupportedException($"Condition run-on {condition.RunOn} has no qualified invocation owner.");
        if (condition.Kind is Function.GetQuestRunning or Function.GetStage or Function.GetStageDone or Function.GetQuestVariable or
            Function.GetObjectiveCompleted or Function.GetObjectiveDisplayed or Function.GetQuestCompleted)
            return quests.Evaluate(condition.Kind, condition.FormArgument1, condition.Argument2);
        if (FalloutPlatformConditions.Evaluate(condition.Declaration) is { } platform)
        { NoArguments(condition); return platform; }
        switch (condition.Kind)
        {
            case Function.GetCurrentTime: NoArguments(condition); return Hour is { } hour ? hour() : GlobalValue(condition);
            case Function.GetGlobalValue: return Globals is { } globals ? globals.Get(Form(condition, "GLOB")) : GlobalValue(condition);
            case Function.GetScriptVariable:
                return world is not null ? (float)world.ReadVariable(quests, condition.FormArgument1, condition.Argument2) :
                records.GetEffective(condition.FormArgument1).Signature == "QUST" ?
                    (float)quests.Variable(condition.FormArgument1, condition.Argument2) : GlobalValue(condition);
            case Function.GetRandomPercent:
                NoArguments(condition);
                var draw = Required(RandomPercent, "invocation RNG")();
                return draw < 100 ? draw : throw new InvalidDataException("Condition RNG returned an invalid percent.");
            case Function.GetDeadCount: SecondUnused(condition); return (DeadCount ?? Required(world, "death history").GetDeadCount)(condition.FormArgument1);
            case Function.GetPCIsSex:
                SecondUnused(condition);
                if (condition.Argument1 > 1) throw new InvalidDataException("GetPCIsSex has an invalid sex argument.");
                return PlayerFemale is { } female ? female() == (condition.Argument1 == 1) ? 1 : 0 :
                    Required(world, "player appearance").QueryNativeCondition(records.RuntimeFormKey(0x14), condition.Declaration);
            case Function.GetVampire: NoArguments(condition); return (Vampire ?? Required(world, "actor query state").ActorQueries.GetVampire)();
            case Function.GetHasNote: return world?.EvaluatePlayerNoteCondition(condition.Declaration) ?? GlobalValue(condition);
            case Function.GetReputation or Function.GetReputationPct or Function.GetReputationThreshold: return world?.EvaluateReputationCondition(condition.Declaration) ?? GlobalValue(condition);
            case Function.PlayerInRegion:
                SecondUnused(condition); Form(condition, "REGN");
                var regionPlayer = PlayerPlacement?.Invoke();
                if (regionPlayer is null) return NativeValue(records.RuntimeFormKey(0x14), condition);
                return Required(world, "region placement").IsInRegion(records.RuntimeFormKey(0x14), condition.FormArgument1,
                    regionPlayer, UnitsToMetres()) ? 1 : 0;
        }
        var subject = scope.Resolve(condition.Declaration);
        if (condition.Kind != Function.GetIsReference && (condition.RunOn != 0 || Identity?.Invoke(subject) is null)) RequireReference(subject);
        return EvaluateSubject(condition, subject, scope);
    }

    private float EvaluateSubject(FalloutConditionQuery condition, FalloutFormKey subject, FalloutConditionScope scope)
    {
        switch (condition.Kind)
        {
            case Function.GetDistance:
                SecondUnused(condition); RequireReference(condition.FormArgument1);
                var separation = Distance is { } distance ? distance(subject, condition.FormArgument1) :
                    Required(world, "spatial state").Distance(subject, condition.FormArgument1, PlayerPlacement?.Invoke(), UnitsToMetres());
                return separation >= 0 ? separation : throw new InvalidDataException("Condition spatial owner returned a negative distance.");
            case Function.GetPos:
                SecondUnused(condition);
                if (condition.Argument1 is < 'X' or > 'Z')
                    throw new InvalidDataException("GetPos axis is not X, Y or Z.");
                return Required(world, "spatial state").SpatialPlacement(subject, PlayerPlacement?.Invoke(), UnitsToMetres())
                    .Position[checked((int)(condition.Argument1 - 'X'))];
            case Function.GetInSameCell:
                SecondUnused(condition); RequireReference(condition.FormArgument1);
                return (SameCell is { } same ? same(subject, condition.FormArgument1) :
                    Required(world, "spatial state").InSameCell(subject, condition.FormArgument1, PlayerPlacement?.Invoke(), UnitsToMetres())) ? 1 : 0;
            case Function.GetActorValue:
                SecondUnused(condition);
                return ActorValue is { } value ? value(subject, checked((int)condition.Argument1)) :
                    subject == records.RuntimeFormKey(0x14) ? NativeValue(subject, condition) :
                    Required(world, "actor values").ActorValue(subject, ActorValueName(condition.Argument1));
            case Function.GetDisabled: NoArguments(condition); return Required(world, "reference lifecycle").IsEnabled(subject) ? 0 : 1;
            case Function.GetDead: NoArguments(condition); return IsDead is null && subject == records.RuntimeFormKey(0x14) ? NativeValue(subject, condition) : Dead(subject) ? 1 : 0;
            case Function.GetItemCount:
                SecondUnused(condition);
                if (ItemCount is not { } count) return NativeValue(subject, condition);
                var quantity = count(subject, condition.FormArgument1);
                return double.IsFinite(quantity) && quantity >= 0 && quantity == Math.Truncate(quantity) ? (float)quantity :
                    throw new InvalidDataException("Condition inventory owner returned an invalid item count.");
            case Function.GetSleeping:
                NoArguments(condition); return Sleeping is null && subject == records.RuntimeFormKey(0x14) ? NativeValue(subject, condition) :
                PhysicalPhase((Sleeping ?? Required(world, "sleeping state").GetSleeping)(subject));
            case Function.GetTalkedToPC: NoArguments(condition); return (TalkedToPlayer ?? Required(world, "conversation history").GetTalkedToPlayer)(subject) ? 1 : 0;
            case Function.GetInCell:
                SecondUnused(condition);
                Form(condition, "CELL");
                if (InCell is { } inCell) return inCell(subject, condition.FormArgument1) ? 1 : 0;
                if (world is not null)
                {
                    var playerCell = subject == records.RuntimeFormKey(0x14) ? PlayerPlacement?.Invoke()?.Cell : null;
                    if (subject == records.RuntimeFormKey(0x14) && playerCell is null) return NativeValue(subject, condition);
                    return world.IsInCell(subject, condition.FormArgument1, playerCell) ? 1 : 0;
                }
                return Cell is { } cell ? FalloutCellQueries.InCell(records, cell(subject, condition.Declaration), condition.FormArgument1) ? 1 : 0 :
                    throw new NotSupportedException("Condition has no authoritative spatial cell owner.");
            case Function.GetIsRace: SecondUnused(condition); return ActorRace(subject) == Form(condition, "RACE") ? 1 : 0;
            case Function.GetIsSex:
                SecondUnused(condition);
                if (condition.Argument1 > 1) throw new InvalidDataException("GetIsSex has an invalid sex argument.");
                if (subject == records.RuntimeFormKey(0x14) && PlayerFemale is null) return NativeValue(subject, condition);
                return Female(subject) == (condition.Argument1 == 1) ? 1 : 0;
            case Function.GetInFaction or Function.GetFactionRank:
                SecondUnused(condition);
                // A null faction is the native absent-membership sentinel,
                // independent of the declaring plugin's master namespace.
                var rank = condition.IsNullFormArgument1(records) ? (sbyte)-1 :
                    ActorFactions(subject).GetValueOrDefault(Form(condition, "FACT"), (sbyte)-1);
                return condition.Kind == Function.GetInFaction ? rank >= 0 ? 1 : 0 : rank;
            case Function.GetIsID: SecondUnused(condition); _ = records.GetEffective(condition.FormArgument1); return Base(subject) == condition.FormArgument1 ? 1 : 0;
            case Function.GetIsReference: SecondUnused(condition); return FalloutReferenceIdentity.Matches(records, subject, condition.FormArgument1) ? 1 : 0;
            case Function.GetSitting:
                NoArguments(condition); return Sitting is null && subject == records.RuntimeFormKey(0x14) ? NativeValue(subject, condition) :
                PhysicalPhase(Sitting is { } sitting ? sitting(subject) : Required(world, "sitting state").GetSitting(subject));
            case Function.GetIsCurrentPackage:
                SecondUnused(condition);
                return FalloutAiPackages.IsCurrentPackage(records, subject, Form(condition, "PACK"),
                    CurrentPackage ?? Required(world, "package state").CurrentPackage) ? 1 : 0;
            case Function.GetEquipped: SecondUnused(condition); return Equipped?.Invoke(subject, condition.FormArgument1) is { } equipped ? equipped ? 1 : 0 : NativeValue(subject, condition);
            case Function.GetKnockedState:
                NoArguments(condition); return subject == records.RuntimeFormKey(0x14) ? NativeValue(subject, condition) :
                    Required(world, "actor knockdown process").GetKnockedState(subject);
            case Function.IsEssential:
                NoArguments(condition); return Required(world, "actor flags").IsEssential(subject) ? 1 : 0;
            case Function.GetUnconscious: NoArguments(condition); return subject == records.RuntimeFormKey(0x14) ? NativeValue(subject, condition) : Required(world, "unconscious state").IsUnconscious(subject) ? 1 : 0;
            case Function.GetRestrained: NoArguments(condition); return Required(world, "restraint state").Get(subject).Restrained ? 1 : 0;
            case Function.IsWeaponOut: NoArguments(condition); return WeaponOut is { } weaponOut ? weaponOut(subject) ? 1 : 0 : NativeValue(subject, condition);
            case Function.GetWeaponAnimType: NoArguments(condition); return WeaponAnimationType?.Invoke(subject) is { } weaponType ? weaponType : NativeValue(subject, condition);
            case Function.IsSneaking: NoArguments(condition); return Sneaking is { } sneaking ? sneaking(subject) ? 1 : 0 : NativeValue(subject, condition);
            case Function.IsInCombat:
                NoArguments(condition); return InCombat is { } combat ? combat(subject) ? 1 : 0 :
                subject == records.RuntimeFormKey(0x14) ? NativeValue(subject, condition) : Required(world, "combat state").IsInCombat(subject) ? 1 : 0;
            case Function.IsInInterior:
                NoArguments(condition); return (Interior is { } interior ? interior(subject) :
                Required(world, "spatial state").IsInInterior(subject, PlayerPlacement?.Invoke()?.Cell)) ? 1 : 0;
            case Function.IsChild: NoArguments(condition); return ActorRace(subject) is { } race && FalloutRaceProperties.IsChild(records.GetEffective(race)) ? 1 : 0;
            case Function.IsInList: SecondUnused(condition); return FalloutReferenceIdentity.IsInList(records, subject, condition.FormArgument1, world?.FormLists) ? 1 : 0;
            case Function.GetIsVoiceType: SecondUnused(condition); return Voice(subject) == Form(condition, "VTYP") ? 1 : 0;
            case Function.GetHealthPercentage:
                NoArguments(condition); return HealthPercentage is { } health ? health(subject) :
                subject == records.RuntimeFormKey(0x14) ? NativeValue(subject, condition) : Health(subject);
            case Function.GetInZone:
                SecondUnused(condition);
                Form(condition, "ECZN");
                return (InZone is { } zone ? zone(subject, condition.FormArgument1) :
                    Required(world, "encounter zone").IsInZone(subject, condition.FormArgument1, PlayerPlacement?.Invoke(), UnitsToMetres())) ? 1 : 0;
            case Function.HasPerk:
                SecondUnused(condition); Form(condition, "PERK");
                if (HasPerk is null && subject == records.RuntimeFormKey(0x14)) return NativeValue(subject, condition);
                if (HasPerk is null && world is null) return NativeValue(subject, condition);
                return (HasPerk is { } perk ? perk(subject, condition.FormArgument1) :
                    Required(world, "acquired perks").AcquiredPerks(subject).Contains(condition.FormArgument1)) ? 1 : 0;
            case Function.IsLastIdlePlayed: SecondUnused(condition); return Required(world, "native idle history").IsLastIdlePlayed(subject, condition.FormArgument1);
            case Function.GetLocked: NoArguments(condition); return Required(world, "lock state").GetLocked(subject);
            case Function.GetLockLevel: NoArguments(condition); return Required(world, "lock level").GetLockLevel(subject);
            case Function.GetOpenState:
                NoArguments(condition);
                if (OpenState is { } open && (world is null || world.IsResident(subject))) return PhysicalPhase(open(subject));
                return subject != records.RuntimeFormKey(0x14) && world?.Get(subject).DoorMotion is { } motion ?
                    PhysicalPhase(motion.OpenState) : NativeValue(subject, condition);
            case Function.GetMapMarkerVisible: NoArguments(condition); return Required(world, "map marker state").MapMarkerVisibility(subject);
            default: return NativeValue(subject, condition);
        }
    }

    private static T Required<T>(T? owner, string name) where T : class => owner ??
        throw new NotSupportedException($"Condition has no authoritative {name} owner.");
    private float GlobalValue(FalloutConditionQuery condition) => Required(Global, $"global function {condition.Function}")(condition.Declaration);
    private float NativeValue(FalloutFormKey subject, FalloutConditionQuery condition) => Native is { } query
        ? query(subject, condition) : Required(world, "native process state").QueryNativeCondition(subject, condition);
    private static float PhysicalPhase(int phase) => phase is >= 0 and <= 4 ? phase :
        throw new InvalidDataException("Condition physical owner returned an invalid phase.");
    private static void SecondUnused(FalloutConditionQuery condition)
    { if (condition.Argument2 != 0) throw new InvalidDataException("Condition has an unexpected second argument."); }
    private static void NoArguments(FalloutConditionQuery condition)
    { SecondUnused(condition); if (condition.Argument1 != 0) throw new InvalidDataException("Condition has unexpected arguments."); }
    private FalloutFormKey Form(FalloutConditionQuery condition, string signature)
    {
        var form = condition.FormArgument1;
        if (records.GetEffective(form).Signature != signature) throw new InvalidDataException($"Condition requires a {signature} argument.");
        return form;
    }
    private void RequireReference(FalloutFormKey subject)
    {
        if (subject != records.RuntimeFormKey(0x14) && records.GetEffective(subject).Signature is not ("REFR" or "ACHR" or "ACRE"))
            throw new InvalidDataException("Condition subject is not a placed reference.");
    }
    internal static string ActorValueName(uint index) => Definitions.FalloutActorValueNames.Name(
        (Definitions.FalloutActorValueId)checked((int)index));
}
