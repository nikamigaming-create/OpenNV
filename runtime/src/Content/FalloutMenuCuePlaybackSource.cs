using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

// The original named-sound resolver treats a trailing directory separator as
// a folder request. Exact nonrandom files enter no directory selector or RNG.
// Folder enumeration/history and random playback remain separate consumers.
internal sealed record FalloutMenuCuePlaybackSource(string EngineSha256,
    string RuntimeSha256, string ContractSha256)
{
    private const string Contract = "named-SOUN-winning-source;current-raw-path;trailing-separator-directory;" +
        "exact-wave-no-folder-selection-no-random-draw;menu-position-independent;" +
        "finite-source-gain-fixed-pitch;unowned-random-frequency-start-history-refuses";
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(EngineSha256 + "\0" + RuntimeSha256 + "\0" + ContractSha256);
    internal static string CurrentContractSha256 => FalloutAdvancementRuntimeReceipt.Hash(Contract);

    internal static FalloutMenuCuePlaybackSource Read(FalloutSleepWaitSource source)
    {
        source.Validate();
        return new(source.EngineSha256, source.RuntimeSha256, CurrentContractSha256);
    }

    internal void Validate()
    {
        if (EngineSha256 is not ("518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57" or
            "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e") ||
            !FalloutAdvancementRuntimeReceipt.Digest(RuntimeSha256) || ContractSha256 != CurrentContractSha256)
            throw new InvalidDataException("Menu cue playback has another selected source consumer.");
    }

    internal FalloutSoundRecord ExactFile(FalloutPluginStack records, FalloutSoundRecord sound)
    {
        Validate(); ArgumentNullException.ThrowIfNull(records); ArgumentNullException.ThrowIfNull(sound);
        // Canonical archive paths deliberately trim separators. Inspect the
        // actual mutable FNAM before canonicalization so a dotted directory
        // cannot masquerade as an exact file after that normalization.
        var raw = records.SoundPaths.Read(sound.FormKey).File;
        if (raw.EndsWith('\\') || raw.EndsWith('/') || !Path.HasExtension(raw))
            throw Unsupported(sound, "the original ordered directory selector and shared anti-repeat history");
        var current = FalloutBsaArchive.CanonicalPath("sound\\" + raw);
        if (!string.Equals(current, sound.LogicalPath, StringComparison.Ordinal))
            throw new InvalidDataException("Menu cue changed its actual prepared source path before allocation.");
        if (!Path.GetExtension(current).Equals(".wav", StringComparison.OrdinalIgnoreCase))
            throw Unsupported(sound, "the selected compressed-codec/rewrite consumer");
        var flags = sound.Flags & ~(FalloutSoundFlags.EnvironmentIgnored | FalloutSoundFlags.MuteWhenSubmerged);
        const FalloutSoundFlags admitted = FalloutSoundFlags.MenuSound | FalloutSoundFlags.TwoDimensional |
            FalloutSoundFlags.DialogueSound;
        if ((flags & ~admitted) != 0 || sound.RandomChancePercent != 0 || sound.StopTime != 0 || sound.StartTime != 0 ||
            sound.LoopStartSample != 0 || sound.LoopEndSample != 0 || sound.FixedPitchScale <= 0)
            throw Unsupported(sound, "random, looping, timed, envelope or nonpositive-pitch playback");
        return sound with { Flags = flags | FalloutSoundFlags.MenuSound };
    }

    internal FalloutSoundRecord PreparedFile(FalloutPluginStack records, FalloutPreparedMenuSound prepared,
        FalloutMenuSoundSelection selection, bool sourceLoop = false)
    {
        Validate(); ArgumentNullException.ThrowIfNull(prepared); ArgumentNullException.ThrowIfNull(selection);
        var sound = prepared.Descriptor;
        if (!ReferenceEquals(records, selection.Records) || selection.Source.EngineSha256 != EngineSha256 ||
            selection.Source.RuntimeSha256 != RuntimeSha256)
            throw new InvalidDataException("Menu playback differs from its actual selected source/random owner.");
        var receipt = selection.Attempt(prepared.SelectionOrdinal);
        selection.RequirePrepared(receipt.Ordinal, sound.FormKey, sound.LogicalPath, receipt.Winner, receipt.RecordSha256);
        if (Path.GetExtension(sound.LogicalPath).ToLowerInvariant() is not (".wav" or ".ogg"))
            throw Unsupported(sound, "the selected codec consumer outside wave/Ogg");
        var flags = sound.Flags & ~(FalloutSoundFlags.EnvironmentIgnored | FalloutSoundFlags.MuteWhenSubmerged);
        const FalloutSoundFlags admitted = FalloutSoundFlags.MenuSound | FalloutSoundFlags.TwoDimensional | FalloutSoundFlags.DialogueSound;
        var supported = admitted | (sourceLoop ? FalloutSoundFlags.Loop : 0);
        if ((flags & ~supported) != 0 || sound.RandomChancePercent != 0 || sound.StopTime != 0 || sound.StartTime != 0 ||
            !sourceLoop && (sound.LoopStartSample != 0 || sound.LoopEndSample != 0) || sound.FixedPitchScale <= 0)
            throw Unsupported(sound, "random-frequency, scheduled, looping, envelope or nonpositive-pitch playback");
        return sound with { Flags = flags | FalloutSoundFlags.MenuSound };
    }

    private static NotSupportedException Unsupported(FalloutSoundRecord sound, string consumer) =>
        new($"Exact menu SOUN {sound.FormKey} requires {consumer}; no random provider or completion is fabricated.");
}
