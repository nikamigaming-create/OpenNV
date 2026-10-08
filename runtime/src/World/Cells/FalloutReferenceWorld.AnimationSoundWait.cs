using System.Security.Cryptography;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    // Every unsettled ledger blocks a complete save. Only exact retained
    // finite native voices may wait for Finished; an absent or looping owner
    // remains a visible continuation refusal without stopping later gameplay.
    internal string? AnimationSoundSaveBlocker => PendingAnimationSoundCaptureCount == 0 ? null :
        PendingAnimationSoundFiniteVoiceWait() is { Count: > 0 } ? "source-finite-audio" : "animation-sound-continuation";

    // Read-only wait admission covers every retained ledger, including doors,
    // cameras, off-cell instances and multiple native sound owners per ref.
    // It never relaxes complete capture or interprets elapsed time as Finished.
    internal IReadOnlyList<FalloutFiniteSoundVoice>? PendingAnimationSoundFiniteVoiceWait()
    {
        var pending = _instances.Values.Where(instance => instance.AnimationSoundCaptureDiagnostic is { Ready: false })
            .OrderBy(instance => records.RuntimeFormId(instance.Reference)).ToArray();
        if (pending.Length == 0) return null;
        var result = new List<FalloutFiniteSoundVoice>();
        foreach (var instance in pending)
        {
            var events = instance.AnimationSoundEvents;
            if (!events.CanAwaitNativeCompletion || records.SoundVoices.PendingFiniteSourceVoices(instance.Reference) is not { Count: > 0 } voices)
                return null;
            var active = events.PendingNativeCompletion
                .ToDictionary(entry => entry.Generation);
            if (voices.Count != active.Count) return null;
            foreach (var proof in voices)
            {
                proof.Validate();
                if (proof.Reference != instance.Reference || !active.Remove(proof.Generation, out var entry) ||
                    proof.Sound != entry.Sound || proof.SoundSha256 != entry.SoundSha256 ||
                    proof.Path != entry.Path || proof.MediaSha256 != entry.MediaSha256)
                    throw new InvalidDataException("Finite audio wait lacks its exact retained source/native generation.");
                var source = records.GetEffective(proof.Sound);
                if (source.Signature != "SOUN" ||
                    !Convert.ToHexString(SHA256.HashData(source.ReadData())).Equals(proof.SoundSha256, StringComparison.OrdinalIgnoreCase) ||
                    FalloutSoundLoop.Read(FalloutSoundRecordReader.Read(source)).Mode != FalloutSoundLoopMode.None)
                    throw new InvalidDataException("Finite audio wait differs from its winning source sound.");
                result.Add(proof);
            }
            if (active.Count != 0) return null;
        }
        if (result.DistinctBy(proof => (proof.Reference, proof.Generation)).Count() != result.Count)
            throw new InvalidDataException("Finite audio wait repeats a retained source generation.");
        return result.AsReadOnly();
    }
}
