namespace OpenNV.Runtime.Content;

internal sealed partial class NativeOwnedAnimationSoundPlayer
{
    // Retirement preserves the animation value graph while the existing real
    // finite host continues to own its native voice and source ledger. This is
    // not complete save admission: the global ledger still waits for Finished.
    internal bool CanRetainFiniteForEmitterRetirement
    {
        get
        {
            if (_lostCaptureAtRetirement || _retiredCaptureDiagnostic is not null) return false;
            if (_events is null) return _voices.Count == 0 && _unbound.Count == 0;
            if (!(_events.CanCapture || _events.CanAwaitNativeCompletion) ||
                _unbound.Except(_events.OwnedLanes, StringComparer.Ordinal).Any()) return false;
            // Several sound components share one actor ledger. A component
            // with no local voice must still join the other retained native
            // generations instead of mistaking their activity for a fault.
            var pending = _events.PendingNativeCompletion.ToDictionary(entry => entry.Generation);
            if (pending.Count != 0)
            {
                var proofs = _records.SoundVoices.PendingFiniteSourceVoices(_events.Reference);
                if (proofs is null || proofs.Count != pending.Count) return false;
                foreach (var proof in proofs)
                {
                    proof.Validate();
                    if (proof.Reference != _events.Reference || !pending.Remove(proof.Generation, out var entry) ||
                        proof.Sound != entry.Sound || proof.SoundSha256 != entry.SoundSha256 ||
                        proof.Path != entry.Path || proof.MediaSha256 != entry.MediaSha256) return false;
                }
                if (pending.Count != 0) return false;
            }
            return _voices.All(pair => pair.Value.Completed is null &&
                pair.Value.Attachment?.Alive == true && ReadFiniteVoice(pair.Key, pair.Value) is not null);
        }
    }
}
