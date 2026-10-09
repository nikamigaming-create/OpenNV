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
            saved.PlayerCell.LastCall is { Disposition: FalloutMainPlayerCellDisposition.Failed } || saved.PlayerCell.Pending.Error is not null ||
            saved.PlayerCell.PendingConsumers.Error is not null || saved.PlayerCell.PendingConsumers.ExteriorLoaders?.Error is not null ||
            saved.PlayerCell.PendingConsumers.CharacterController?.Error is not null ||
            saved.PlayerCell.Pending.Pending is { SourcePayload: null } || saved.PlayerCell.Pending.Pending?.SourcePayload?.Callback is not null ||
            saved.Utilities.LastFrame?.Error is not null || saved.Utilities.LastCommand?.Error is not null ||
            saved.Utilities.LastCallbackAttempt?.Error is not null || saved.Utilities.Callback is { Registered: true })
            throw new NotSupportedException("Campaign save refuses a retained failed Main source child; diagnostic snapshots do not fabricate playable continuation.");
    }
    internal static void RequireSource(FalloutPluginStack records, FalloutAdvancementRuntimeSource actual,
        FalloutSharedScriptRuntimeSnapshot? saved)
    {
        if (saved is null) return;
        var expected = FalloutMainScriptCallerSource.Read(FalloutImmediateScriptSource.Read(actual.Receipt));
        if (saved.PlayerCell.Source != FalloutMainPlayerCellSource.Read(expected)) throw new InvalidDataException("Cold Player child changed selected source/dependencies.");
        if (saved.MainCaller.Source != expected) throw new InvalidDataException("Cold shared script source changed selected executable/dependencies/settings.");
        var commands = FalloutExecutableStringTable.ReadMainUtilityCommandSource(actual.OwnedSource.FalloutExecutablePath, FalloutMainUtilitySource.Read(expected));
        if (saved.UtilityCommands.Source != commands || saved.UtilityCommands.ConsoleSource != FalloutConsoleActivitySource.Read(actual.Receipt))
            throw new InvalidDataException("Cold shared utility commands changed the actual selected executable/controller declaration.");
        using var contexts = new FalloutScriptEngineContexts(FalloutImmediateScriptSource.Read(actual.Receipt), saved.Contexts);
        contexts.RequireSources(records);
    }
}
