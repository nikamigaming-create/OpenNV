using System.Text.Json;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Diagnostics.Parity;

internal sealed partial class RuntimeLiveHarness
{
    private Func<Guid, RuntimeSaveSlotMetadata>? _saveCheckpoint;
    private Func<Guid, bool, RuntimeSaveSlotMetadata>? _loadCheckpoint;
    private Func<bool>? _checkpointTransitioning;
    private Func<RuntimeSaveSlotMetadata?>? _restoredCheckpoint;
    private object? _lastCheckpoint;

    internal void ConfigureCheckpoints(Func<Guid, RuntimeSaveSlotMetadata> save, Func<Guid, bool, RuntimeSaveSlotMetadata> load,
        Func<bool> transitioning, Func<RuntimeSaveSlotMetadata?> restored)
    {
        (_saveCheckpoint, _loadCheckpoint, _checkpointTransitioning, _restoredCheckpoint) = (save, load, transitioning, restored);
        _replayCheckpointPrepared = restored() is not null;
    }

    private bool DispatchCheckpoint(JsonElement command, ulong request)
    {
        var operation = command.GetProperty("op").GetString();
        if (operation is not ("checkpoint.save" or "checkpoint.load")) return false;
        if (!Guid.TryParseExact(command.GetProperty("id").GetString(), "N", out var id))
            throw new ArgumentException("A checkpoint requires a GUID save-slot identity.");
        _bot?.Stop();
        ReleaseAll();
        var pauseAfterLoad = command.TryGetProperty("pauseAfterLoad", out var paused) && paused.GetBoolean();
        var slot = operation == "checkpoint.save"
            ? (_saveCheckpoint ?? throw new NotSupportedException("The native campaign checkpoint owner is unavailable."))(id)
            : (_loadCheckpoint ?? throw new NotSupportedException("The native campaign checkpoint owner is unavailable."))(id, pauseAfterLoad);
        _replayCheckpointPrepared = operation == "checkpoint.load";
        // These requests restore reached campaign state. They are explicit
        // diagnostic preparation, never ordinary-input traversal evidence.
        _lastCheckpoint = new { request, operation, slot, pauseAfterLoad, ordinaryInput = false, owner = "shared-campaign-save" };
        PublishState();
        return true;
    }
}
