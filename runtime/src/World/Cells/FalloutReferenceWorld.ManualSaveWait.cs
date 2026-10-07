using System.Security.Cryptography;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    // A pending save may wait only when every currently refused procedure has
    // the same live native binding owner and only its finite audio is unfinished.
    internal IReadOnlyList<FalloutFiniteSoundVoice>? PendingProcedureFiniteVoiceWait()
    {
        var pending = _instances.Values.Where(instance => instance.ProcedureCaptureBlocker is not null &&
            !instance.PackageBindingFailureCaptureReady && !instance.FurnitureCaptureReady &&
            !instance.SelectionFailureCaptureReady && !instance.DialogueCaptureReady &&
            !instance.PendingPackageSelectionCaptureReady).ToArray();
        if (pending.Length == 0) return null;
        var result = new List<FalloutFiniteSoundVoice>();
        foreach (var instance in pending)
        {
            var voices = instance.PendingPackageBindingFiniteVoices?.Invoke();
            if (voices is not { Count: > 0 } || !instance.AnimationSoundEvents.CanAwaitNativeCompletion ||
                voices.Count != instance.AnimationSoundEvents.PendingNativeCompletion.Count()) return null;
            foreach (var voice in voices)
            {
                voice.Validate();
                if (voice.Reference != instance.Reference) throw new InvalidDataException("Manual wait belongs to another reference.");
                var source = records.GetEffective(voice.Sound);
                if (source.Signature != "SOUN" ||
                    !Convert.ToHexString(SHA256.HashData(source.ReadData())).Equals(voice.SoundSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Manual wait differs from the winning source sound.");
                var entry = instance.AnimationSoundEvents.Events.SingleOrDefault(entry => entry.Generation == voice.Generation);
                if (entry is null || entry.End != FalloutAnimationSoundEnd.Active || entry.Sound != voice.Sound ||
                    entry.SoundSha256 != voice.SoundSha256 || entry.Path != voice.Path || entry.MediaSha256 != voice.MediaSha256)
                    throw new InvalidDataException("Manual wait has no matching live source generation.");
                result.Add(voice);
            }
        }
        if (result.DistinctBy(voice => (voice.Reference, voice.Generation)).Count() != result.Count)
            throw new InvalidDataException("Manual wait repeats a live source generation.");
        return result.AsReadOnly();
    }
}
