using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private ulong _nativeSaveInputGeneration;
    private void PrepareNativeSaveOrderSelection()
    {
        var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Bootstrap has no source save selection.");
        var path = Path.GetFullPath(RequireOption(_options, "save-path"));
        (_nativeReferences ?? throw new InvalidOperationException("Bootstrap has no source execution world.")).ScriptManualSaves.BindSelection(
            new(content.SaveCompatibilityId, path, id => Path.Combine(path + RuntimeSaveSlotCatalog.SlotDirectorySuffix, id.ToString("N") + ".json")),
            Engine.GetProcessFrames);
    }
    private RuntimeSaveNativeSite CreateNativeSaveSite(FalloutFormKey? reference = null,
        FalloutFormKey? previousCell = null, FalloutFormKey? arrivalDoor = null)
    {
        var records = _nativePluginStack ?? throw new InvalidOperationException("Native save input has no actual record owner.");
        var cell = _nativeActiveCell?.Cell.FormKey ?? throw new InvalidOperationException("Native save input has no actual active cell.");
        string? Digest(FalloutFormKey? form) => form is { } key
            ? Convert.ToHexString(SHA256.HashData(records.GetEffective(key).ReadData())).ToLowerInvariant() : null;
        return new(_nativeManualSaveSession, checked(++_nativeSaveInputGeneration), records.RuntimeFormKey(0x14), cell,
            reference, Digest(reference), previousCell, arrivalDoor, Digest(arrivalDoor));
    }

    private void RequestNativeInteractionSave(FalloutFormKey reference)
    {
        if (_nativeOpeningStageDriver is not { HasCampaignSave: true } driver || driver.Vitals.HitPoints <= 0) return;
        driver.RequestNativeSave(RuntimeSaveRequestOrigin.NativeInteraction, CreateNativeSaveSite(reference));
    }

    private void RequestNativePipBoySave()
    {
        if (_nativeOpeningStageDriver is not { HasCampaignSave: true } driver || driver.Vitals.HitPoints <= 0) return;
        driver.RequestNativeSave(RuntimeSaveRequestOrigin.NativePipBoyClose, CreateNativeSaveSite());
    }

    private void RequestNativeDoorTransportSave(FalloutFormKey previousCell, FalloutFormKey sourceDoor, FalloutFormKey arrivalDoor)
    {
        (_nativeOpeningStageDriver ?? throw new InvalidOperationException("Door transport has no actual save request owner."))
            .RequestNativeSave(RuntimeSaveRequestOrigin.NativeDoorTransport, CreateNativeSaveSite(sourceDoor, previousCell, arrivalDoor));
    }
}
