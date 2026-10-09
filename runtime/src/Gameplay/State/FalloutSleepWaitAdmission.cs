using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutRestKind { Wait, Sleep }
internal enum FalloutRestOrigin { PlayerControl, BedActivation, SourceCommand, ScriptHours }
internal enum FalloutRestFact
{
    NativeMenuFactoryReady, PlayerAlive, PlayerNotTrespassing, NoAlarm, PlayerNotUnderwater,
    NoHostileActorsInSourceRadius, PlayerProcessAllowsRest, CellAllowsRest, NoRadiationDamage,
    NoOngoingHealthDamage, BedEnabled, BedOwnershipAllowsPlayer,
    WorldHourConsumerOwned, RestEffectsOwned, SourceMenuBeginConsumerOwned, BedMarkerAvailable,
}
internal enum FalloutRestFactState { Satisfied, Denied, Unowned }
internal sealed record FalloutRestObservation(FalloutRestFactState State, string Owner, string? Reason = null)
{
    internal void Validate()
    {
        if (!Enum.IsDefined(State) || string.IsNullOrWhiteSpace(Owner) || State != FalloutRestFactState.Satisfied && string.IsNullOrWhiteSpace(Reason))
            throw new InvalidDataException("Sleep/wait restriction has no factual owner or refusal.");
    }
}
internal sealed record FalloutRestRequest(FalloutRestKind Kind, FalloutRestOrigin Origin,
    FalloutFormKey? Bed = null, string? BedSha256 = null, FalloutSleepWaitBed? BedSource = null)
{
    internal void Validate()
    {
        if (!Enum.IsDefined(Kind) || !Enum.IsDefined(Origin) || Origin == FalloutRestOrigin.BedActivation &&
            (Kind != FalloutRestKind.Sleep || Bed is null || !FalloutPlayerPhysicalSource.Digest(BedSha256) ||
                BedSource is null || BedSource.Reference != Bed || BedSource.ReferenceSha256 != BedSha256) ||
            Origin == FalloutRestOrigin.PlayerControl && Kind != FalloutRestKind.Wait ||
            Origin != FalloutRestOrigin.BedActivation && (Bed is not null || BedSha256 is not null || BedSource is not null))
            throw new InvalidDataException("Sleep/wait request has an invalid genuine origin or bed identity.");
        BedSource?.Validate();
    }
}
internal sealed record FalloutRestAdmission(FalloutRestRequest Request, FalloutRestFact? Failure,
    FalloutRestObservation? Observation, string? MessageSetting)
{
    internal bool Admitted => Failure is null;
    internal void Require()
    {
        if (Failure is null) return;
        if (Observation?.State == FalloutRestFactState.Unowned)
            throw new NotSupportedException($"Sleep/wait requires {Failure}: {Observation.Owner}: {Observation.Reason}");
        throw new InvalidOperationException($"Sleep/wait refused {Failure}: {Observation?.Reason}");
    }
}
internal static class FalloutSleepWaitAdmission
{
    internal static FalloutRestAdmission Inspect(FalloutRestRequest request,
        Func<FalloutRestRequest, FalloutRestFact, FalloutRestObservation> observe)
    {
        request.Validate(); ArgumentNullException.ThrowIfNull(observe);
        // Shared source factory/required living consumers precede allocation.
        var required = new List<FalloutRestFact> { FalloutRestFact.PlayerAlive };
        if (request.Origin != FalloutRestOrigin.ScriptHours) required.Insert(0, FalloutRestFact.NativeMenuFactoryReady);
        if (request.Origin == FalloutRestOrigin.PlayerControl || request.Origin == FalloutRestOrigin.BedActivation)
        {
            if (request.Bed is not null) required.AddRange([FalloutRestFact.BedEnabled,
                FalloutRestFact.BedMarkerAvailable, FalloutRestFact.BedOwnershipAllowsPlayer, FalloutRestFact.CellAllowsRest]);
            required.Add(FalloutRestFact.PlayerNotTrespassing);
            if (request.Kind == FalloutRestKind.Wait)
                required.AddRange([FalloutRestFact.NoAlarm, FalloutRestFact.PlayerNotUnderwater]);
            required.Add(FalloutRestFact.NoHostileActorsInSourceRadius);
            if (request.Kind == FalloutRestKind.Wait) required.Add(FalloutRestFact.PlayerProcessAllowsRest);
            if (request.Kind == FalloutRestKind.Wait) required.Add(FalloutRestFact.CellAllowsRest);
            required.AddRange([FalloutRestFact.NoRadiationDamage, FalloutRestFact.NoOngoingHealthDamage]);
        }
        required.AddRange([FalloutRestFact.WorldHourConsumerOwned, FalloutRestFact.RestEffectsOwned]);
        if (request.Origin != FalloutRestOrigin.ScriptHours) required.Add(FalloutRestFact.SourceMenuBeginConsumerOwned);
        foreach (var fact in required)
        {
            var value = observe(request, fact) ?? throw new InvalidDataException("Rest fact observer returned no observation.");
            value.Validate();
            if (value.State != FalloutRestFactState.Satisfied) return new(request, fact, value, Setting(request.Kind, fact));
        }
        return new(request, null, null, null);
    }
    private static string? Setting(FalloutRestKind kind, FalloutRestFact fact) => fact switch
    {
        FalloutRestFact.BedOwnershipAllowsPlayer => "sNoSleepInOwnedBed",
        FalloutRestFact.PlayerNotTrespassing => kind == FalloutRestKind.Sleep ? "sNoSleepTrespass" : "sNoWaitTrespass",
        FalloutRestFact.NoAlarm => kind == FalloutRestKind.Sleep ? null : "sNoWaitWhileAlarmSounding",
        FalloutRestFact.PlayerNotUnderwater => kind == FalloutRestKind.Sleep ? null : "sNoWaitUnderWater",
        FalloutRestFact.NoHostileActorsInSourceRadius => kind == FalloutRestKind.Sleep ? "sNoSleepHostileActorsNear" : "sNoWaitHostilActorsNear",
        FalloutRestFact.PlayerProcessAllowsRest => kind == FalloutRestKind.Sleep ? null : "sNoWaitInAir",
        FalloutRestFact.CellAllowsRest => "sNoWaitInCell",
        FalloutRestFact.NoRadiationDamage => kind == FalloutRestKind.Sleep ? "sNoSleepInRadiation" : "sNoWaitInRadiation",
        FalloutRestFact.NoOngoingHealthDamage => kind == FalloutRestKind.Sleep ? "sNoSleepTakingHealthDamage" : "sNoWaitTakingHealthDamage",
        _ => null,
    };
}
