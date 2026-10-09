using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private long _ingestibleSoundOccurrence;
    internal FalloutIngestiblesSnapshot IngestibleState => _ingestibles.Capture();

    internal void UseAid(FalloutFormKey form)
    {
        var use = _ingestibles.Prepare(form);
        AudioStreamPlayer? sound = null;
        try
        {
            // Resolve audio before committing the inventory/vitals transaction.
            if (use.Sound is { } soundForm)
                sound = NativeOwnedSoundPlayback.CreateMenu(FalloutSoundRecordReader.Read(_pluginStack, soundForm), _pluginStack,
                    NativeOwnedSoundPlayback.MenuCall(this, checked(++_ingestibleSoundOccurrence), soundForm));
            var result = use.Commit();
            if (sound is not null && result.Consumed)
            {
                sound.ProcessMode = ProcessModeEnum.Always;
                AddChild(sound);
                sound.Finished += sound.QueueFree;
                sound.Play();
            }
            GD.Print($"OPENNV_AID_USE source={form} consumed={result.Consumed} healthBefore={result.HealthBefore:R} healthAfter={result.HealthAfter:R} activeEffects={IngestibleState.Effects.Count}");
        }
        finally { if (sound is not null && sound.GetParent() is null) sound.Free(); }
    }
}
