using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutChallenges
{
    private FalloutIndexedInterfaceSounds? _interfaceSounds;
    internal void BindInterfaceSounds(FalloutIndexedInterfaceSounds sounds)
    {
        ObjectDisposedException.ThrowIf(_retired, this); ArgumentNullException.ThrowIfNull(sounds);
        if (_interfaceSounds is not null || _executing.Count != 0 || !ReferenceEquals(sounds.Source.Records, _records) ||
            Source is null || Source.EngineSha256 != sounds.Source.Catalogue.EngineSha256 ||
            Source.RuntimeSha256 != sounds.Source.Playback.RuntimeSha256)
            throw new InvalidOperationException("Challenge cue binding requires the actual same selected campaign sound/source owner.");
        _interfaceSounds = sounds;
        RequireInterfaceSoundSources(_restoredLast);
    }

    private void PlayInterfaceCompletion(AttemptFrame attempt)
    {
        var sounds = _interfaceSounds ?? throw new NotSupportedException("source-challenge-indexed-native-sound-owner-unbound");
        if (!_executing.TryPeek(out var frame) || !ReferenceEquals(frame.Attempts.LastOrDefault(), attempt) ||
            attempt.Value.Prefix != FalloutChallengePrefix.InterfaceCueEntered || attempt.Value.NoticeOrdinal is null)
            throw new InvalidOperationException("Challenge cue has no actual entered HUD/completion caller.");
        // This numeric argument belongs to the original completion call. The
        // general indexed source chooses its real SOUN; there is no name or
        // challenge-specific successful audio branch in the playback owner.
        var call = new FalloutInterfaceSoundCall("source-challenge-completion", frame.Ordinal, 21,
            attempt.Value.Form, frame.Attempts.Count);
        var before = sounds.LastOrdinal;
        try
        {
            var voice = sounds.Play(call);
            sounds.RequireCall(voice.Ordinal, call, returned: true);
            attempt.Value = attempt.Value with { InterfaceCueOrdinal = voice.Ordinal,
                Prefix = FalloutChallengePrefix.InterfaceCueReturned };
        }
        finally
        {
            // Allocation/source failure can throw after entering its real
            // indexed ledger. Keep that attempted ordinal without replaying
            // the earlier script/statistic/HUD suffix on cold reconstruction.
            if (sounds.LastOrdinal > before)
            {
                var actual = sounds.Voice(checked(before + 1));
                if (actual.Call != call) throw new InvalidOperationException("Challenge cue lost its correlated synchronous native request.");
                attempt.Value = attempt.Value with { InterfaceCueOrdinal = actual.Ordinal };
            }
        }
    }

    private void RequireInterfaceSoundSources(FalloutChallengeDispatch? dispatch)
    {
        if (dispatch is null) return;
        for (var index = 0; index < dispatch.Attempts.Count; ++index)
        {
            var attempt = dispatch.Attempts[index]; var definition = Definition(attempt.Form);
            var completionNotice = attempt.After >= definition.Threshold && Source?.ShowNotices == true &&
                definition.Name.Length != 0 && definition.Description.Length != 0;
            var returned = completionNotice && attempt.Prefix >= FalloutChallengePrefix.InterfaceCueReturned;
            if (attempt.InterfaceCueOrdinal is not null && (!completionNotice || attempt.NoticeOrdinal is null ||
                attempt.Prefix < FalloutChallengePrefix.InterfaceCueEntered) || returned && attempt.InterfaceCueOrdinal is null)
                throw new InvalidDataException("Challenge continuation invented an unentered indexed audio suffix.");
            if (attempt.InterfaceCueOrdinal is not { } ordinal) continue;
            var sounds = _interfaceSounds ?? throw new InvalidDataException("Cold challenge cue lacks its actual restored indexed sound owner.");
            sounds.RequireCall(ordinal, new("source-challenge-completion", dispatch.Ordinal, 21, attempt.Form, index + 1), returned);
        }
        foreach (var child in dispatch.Children) RequireInterfaceSoundSources(child);
    }
}
