using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal static class FalloutSharedScriptRuntimeSaveContract
{
    internal static void ValidateShape(FalloutSharedScriptRuntimeSnapshot? saved,
        FalloutPlayerStatisticsSnapshot statistics, FalloutActorProcessRuntimeSnapshot runtime)
    {
        var required = statistics.Source.ChallengeEvent == 11;
        if (!required)
        {
            if (saved is not null) throw new InvalidDataException("Source-absent family has foreign challenge/Main-script owners.");
            return;
        }
        if (saved is null) throw new InvalidDataException("Current save omitted its actual shared interpreter/Main-caller lifetime.");
        saved.Validate(runtime);
        if (saved.MainCaller.Source.EngineSha256 != statistics.Source.EngineSha256 ||
            saved.MainCaller.Source.RuntimeSha256 != statistics.Source.RuntimeSha256)
            throw new InvalidDataException("Main/script continuation selected another campaign source lifetime.");
        if (saved.MainField.Error is not null || saved.MainCaller.LastCall is { Disposition: FalloutMainScriptCallerDisposition.Failed } ||
            saved.Utilities.LastFrame?.Error is not null || saved.Utilities.LastCommand?.Error is not null ||
            saved.Utilities.LastCallbackAttempt?.Error is not null || saved.Utilities.Callback is { Registered: true })
            throw new NotSupportedException("Campaign save refuses a retained failed Main source child; diagnostic snapshots do not fabricate playable continuation.");
    }
    internal static void RequireSource(FalloutPluginStack records, FalloutAdvancementRuntimeSource actual,
        FalloutSharedScriptRuntimeSnapshot? saved)
    {
        if (saved is null) return;
        var expected = FalloutMainScriptCallerSource.Read(FalloutImmediateScriptSource.Read(actual.Receipt));
        if (saved.MainCaller.Source != expected) throw new InvalidDataException("Cold shared script source changed selected executable/dependencies/settings.");
        using var contexts = new FalloutScriptEngineContexts(FalloutImmediateScriptSource.Read(actual.Receipt), saved.Contexts);
        contexts.RequireSources(records);
    }
}
