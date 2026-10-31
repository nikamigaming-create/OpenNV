using System.Text.Json;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Diagnostics.Parity;

internal sealed partial class RuntimeLiveHarness
{
    private Func<Guid, RuntimeSaveSlotMetadata>? _saveCheckpoint;
    private Func<Guid, bool, RuntimeSaveSlotMetadata>? _loadCheckpoint;
    private Func<bool>? _checkpointTransitioning;
    private Func<RuntimeSaveSlotMetadata?>? _restoredCheckpoint;
    private Func<Guid, RuntimeManualSaveReceipt>? _prepareCheckpoint;
    private Func<Guid, RuntimeManualSaveReceipt?>? _preparedCheckpoint;
    private (ulong Request, Guid Slot, ulong Generation)? _pendingCheckpoint;
    private object? _lastCheckpoint;

    internal void ConfigureCheckpoints(Func<Guid, RuntimeSaveSlotMetadata> save, Func<Guid, bool, RuntimeSaveSlotMetadata> load,
        Func<bool> transitioning, Func<RuntimeSaveSlotMetadata?> restored,
        Func<Guid, RuntimeManualSaveReceipt>? prepare = null, Func<Guid, RuntimeManualSaveReceipt?>? prepared = null)
    {
        if ((prepare is null) != (prepared is null)) throw new ArgumentException("Checkpoint preparation and receipt owners must be paired.");
        (_saveCheckpoint, _loadCheckpoint, _checkpointTransitioning, _restoredCheckpoint) = (save, load, transitioning, restored);
        (_prepareCheckpoint, _preparedCheckpoint) = (prepare, prepared);
        _replayCheckpointPrepared = restored() is not null;
    }

    private bool DispatchCheckpoint(JsonElement command, ulong request)
    {
        var operation = command.GetProperty("op").GetString();
        if (operation is not ("checkpoint.save" or "checkpoint.load")) return false;
        if (!Guid.TryParseExact(command.GetProperty("id").GetString(), "N", out var id))
            throw new ArgumentException("A checkpoint requires a GUID save-slot identity.");
        if (_checkpointTransitioning?.Invoke() == true)
            throw new InvalidOperationException("Another checkpoint or manual save still owns the session.");
        _bot?.Stop();
        ReleaseAll();
        var pauseAfterLoad = command.TryGetProperty("pauseAfterLoad", out var paused) && paused.GetBoolean();
        if (operation == "checkpoint.save")
        {
            var admitted = (_prepareCheckpoint ?? throw new NotSupportedException("Checkpoint save requires shared save preparation."))(id);
            if (admitted.Slot != id) throw new InvalidDataException("Checkpoint preparation admitted another slot identity.");
            _pendingCheckpoint = (request, id, admitted.Generation);
            _replayCheckpointPrepared = false;
            PublishState();
            return true;
        }
        _ = ObserveCheckpoint();
        var slot = (_loadCheckpoint ?? throw new NotSupportedException("The native campaign checkpoint owner is unavailable."))(id, pauseAfterLoad);
        _replayCheckpointPrepared = operation == "checkpoint.load";
        // These requests restore reached campaign state. They are explicit
        // diagnostic preparation, never ordinary-input traversal evidence.
        _lastCheckpoint = new { request, operation, slot, pauseAfterLoad, ordinaryInput = false, owner = "shared-campaign-save" };
        PublishState();
        return true;
    }

    private object? ObserveCheckpoint()
    {
        if (_pendingCheckpoint is not { } pending) return _lastCheckpoint;
        var receipt = _preparedCheckpoint!(pending.Slot) ?? throw new InvalidDataException("Checkpoint preparation lost its request receipt.");
        if (receipt.Slot != pending.Slot || receipt.Generation != pending.Generation)
            throw new InvalidDataException("Checkpoint preparation changed its source generation.");
        var observed = new
        {
            request = pending.Request,
            operation = "checkpoint.save",
            requestedSlot = pending.Slot.ToString("N"),
            disposition = receipt.CleanupError is null ? receipt.Disposition : "failed",
            error = receipt.CleanupError ?? receipt.Error,
            slot = receipt.CommittedSlot,
            preparation = receipt,
            pauseAfterLoad = false,
            ordinaryInput = false,
            owner = "shared-campaign-save-preparation"
        };
        if (receipt.Disposition != "pending") { _lastCheckpoint = observed; _pendingCheckpoint = null; }
        return observed;
    }
}
