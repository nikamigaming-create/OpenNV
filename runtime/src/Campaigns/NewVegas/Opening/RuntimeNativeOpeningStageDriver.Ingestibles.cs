using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private readonly FalloutSoundRandomState _ingestibleSoundRandom = new(0x41d);
    internal FalloutIngestiblesSnapshot IngestibleState => _ingestibles.Capture();

    internal void UseAid(FalloutFormKey form) => CommitAidUse(form, _ingestibles.Prepare(form));

    internal void UseAid(FalloutFormKey form, int variant, long expectedRevision) =>
        CommitAidUse(form, _ingestibles.Prepare(form, variant, expectedRevision));

    private void CommitAidUse(FalloutFormKey form, FalloutIngestibleUse use)
    {
        AudioStreamPlayer? sound = null;
        var soundRandom = new FalloutSoundRandomState(_ingestibleSoundRandom.State);
        try
        {
            // Resolve audio before committing the inventory/vitals transaction.
            if (use.Sound is { } soundForm)
                sound = NativeOwnedSoundPlayback.CreateMenu(FalloutSoundRecordReader.Read(_pluginStack, soundForm), _pluginStack,
                    RuntimeLiveContentSource.Current!, soundRandom);
            var result = use.Commit();
            if (sound is not null && result.Consumed)
            {
                _ingestibleSoundRandom.Restore(soundRandom.State);
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
