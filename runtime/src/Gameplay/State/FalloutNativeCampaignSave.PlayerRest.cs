using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal static partial class FalloutNativeCampaignSave
{
    private static void ValidateCurrentRestContinuation(FalloutNativeCampaignState state)
    {
        var rest = state.SleepWait ?? throw new InvalidDataException("Current save has no player rest state.");
        rest.Validate(); rest.RequirePhysical(state.PlayerPhysical ??
            throw new InvalidDataException("Current rest has no physical sleep-flag continuation."));
        var fade = state.InterfaceFades ?? throw new InvalidDataException("Current save has no interface fade state.");
        fade.Validate();
        if (fade.Source.RuntimeSha256 != rest.Source.RuntimeSha256 ||
            fade.Source.Declaration.EngineSha256 != rest.Source.EngineSha256)
            throw new InvalidDataException("Current rest and interface fades have different selected sources.");
        var elapsed = state.RestWorldTime ?? throw new InvalidDataException("Current save has no source elapsed-world-time state.");
        FalloutRestWorldTime.RequireSnapshot(FalloutRestWorldTimeSource.Read(rest.Source), elapsed);
        if (elapsed.Last is { Origin: FalloutRestWorldTimeOrigin.RestHour } hour)
        {
            if (hour.RestRequest > rest.RequestOrdinal || hour.RestRequest == rest.RequestOrdinal &&
                (rest.LastHour is not { PreludeCommitted: true } actual || actual.Ordinal != hour.RestHour ||
                 BitConverter.SingleToUInt32Bits(actual.SimulationSeconds) != hour.AddedBits))
                throw new InvalidDataException("Rest elapsed time lost its actual source-hour prefix.");
        }
        var autosave = state.RestAutoSave ?? throw new InvalidDataException("Current save has no rest autosave state.");
        if (autosave.Schema != FalloutRestAutoSave.Schema || autosave.RestSourceSha256 != rest.Source.Identity ||
            !FalloutPlayerPhysicalSource.Digest(autosave.PolicySha256) || autosave.LastRequestOrdinal < 0 ||
            autosave.LastRequestOrdinal > rest.RequestOrdinal ||
            (autosave.LastRequestOrdinal == 0) != (autosave.Kind is null) || autosave.SaveOrder == 0 ||
            autosave.Kind is { } kind && !Enum.IsDefined(kind) ||
            autosave.SaveOrder is not null && autosave.Kind is null ||
            autosave.Failure is not null && string.IsNullOrWhiteSpace(autosave.Failure) ||
            autosave.LastRequestOrdinal == 0 && autosave.Failure is not null)
            throw new InvalidDataException("Rest autosave lost its complete source-policy/queue prefix.");
        if (autosave.SaveOrder is { } order)
        {
            var row = state.SaveOrder?.Requests.SingleOrDefault(row => row.Order == order) ??
                throw new InvalidDataException("Rest autosave has no actual retained ordered queue row.");
            if (row.Origin != RuntimeSaveRequestOrigin.NativeRestStart || row.Destination != RuntimeSaveRequestDestination.Continue ||
                row.Native?.Rest is not { } site || site.RequestOrdinal != autosave.LastRequestOrdinal ||
                site.Request.Kind != autosave.Kind || site.RestSourceSha256 != rest.Source.Identity ||
                site.PolicySha256 != autosave.PolicySha256)
                throw new InvalidDataException("Rest autosave row changed its original policy/site/destination.");
        }
        if (state.Scripts?.Session?.Hardcore == true && state.HardcoreNeeds is null)
            throw new InvalidDataException("Current Hardcore mode has no source modifier pools and minute baselines.");
        if (state.HardcoreNeeds is { } needs)
        {
            needs.Validate();
            if (!rest.Source.HasHardcoreConsumer || needs.SourceSha256 != rest.Source.Identity)
                throw new InvalidDataException("Hardcore needs differ from the selected rest source.");
        }
    }

    private static void ValidateCurrentRestSource(FalloutPluginStack records, FalloutNativeCampaignState state)
    {
        ValidateCurrentRestContinuation(state);
        using var runtime = FalloutAdvancementRuntimeSource.Open(records);
        var saved = state.SleepWait!;
        saved.RequireSource(records, runtime.Receipt);
        state.InterfaceFades!.Source.RequireCurrent(FalloutInterfaceFadeSource.Read(runtime));
        var policy = FalloutRestAutoSavePolicy.Read(saved.Source, records.IniSettings);
        var autosave = state.RestAutoSave!;
        if (autosave.PolicySha256 != policy.Identity || autosave.Kind is { } kind &&
            (policy.Enabled(kind) && autosave.SaveOrder is null && autosave.Failure is null ||
             !policy.Enabled(kind) && autosave.SaveOrder is not null))
            throw new InvalidDataException("Cold rest autosave changed its actual selected preference evaluation.");
        var source = records.OwnedSource ?? throw new InvalidDataException("Rest continuation has no exact selected installation.");
        var globals = FalloutGlobalState.Read(records);
        globals.Restore(state.Globals ?? throw new InvalidDataException("Rest continuation has no exact saved globals."));
        var clock = new FalloutGameTime(globals, FalloutGameTimeBindings.Read(records), FalloutCalendar.Read(source.FalloutExecutablePath));
        clock.Restore(state.GameTime ?? throw new InvalidDataException("Rest continuation has no exact saved calendar clock."));
        static Exception Replay() => new InvalidOperationException("Cold rest validation attempted a living source/native operation.");
        var host = new FalloutSleepWaitHost((_, _) => throw Replay(), _ => throw Replay(), (_, _) => throw Replay(),
            _ => throw Replay(), _ => throw Replay(), _ => throw Replay(), _ => throw Replay(), (_, _) => throw Replay(),
            () => throw Replay())
        {
            AfterMenuPlayerHours = _ => throw Replay(), BeforeCancelPlayerHours = _ => throw Replay(),
        };
        var rest = new FalloutSleepWait(saved.Source, clock, host, saved);
        var sound = state.RestInterfaceSounds ?? throw new InvalidDataException("Current save has no rest cue state.");
        _ = new FalloutRestInterfaceSounds(records, rest, new(sound.RandomState), sound);
        // The RNG above restores only the exact captured value for validation.
        // It is never bound as a new living interface cue provider.
    }
}
