using System.Security.Cryptography;
using OpenNV.Runtime.Content;

internal static class OwnedHackingProbe
{
    // The harness supplies the qualified shared RNG/clock, perk and allowance
    // owners. There is deliberately no CLI seed, guessed timeout or four-attempt
    // substitute. A successful construction is not an ordinary-interaction proof.
    internal static FalloutHackingCandidates Run(string dataRoot, uint terminalRuntimeId,
        float currentScience, Func<float, float> applyScienceBonus,
        FalloutHackingGenerationOwners owners)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(dataRoot), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var key = records.RuntimeFormKey(terminalRuntimeId);
        var terminal = FalloutTerminal.Read(records, key);
        if ((terminal.Flags & 1) != 0)
            throw new NotSupportedException("Leveled owned TERM requires its reference-aware difficulty owner.");
        if (terminal.Difficulty > 4)
            throw new NotSupportedException("Selected owned TERM is key-only and cannot generate a hacking puzzle.");
        var terminalHash = SHA256.HashData(terminal.Record.ReadData());
        if (!content.TryRead("menus/falloutdict.txt", null, out var bytes, out var source))
            throw new FileNotFoundException("Selected owned content has no menus/falloutdict.txt.");
        var dictionary = FalloutHackingDictionary.ReadCanonical(bytes);
        var request = FalloutHackingRequest.Read(records, key, terminal.Difficulty, currentScience);
        var result = FalloutHackingCandidates.Generate(dictionary, request, applyScienceBonus, owners);
        if (!SHA256.HashData(terminal.Record.ReadData()).SequenceEqual(terminalHash) ||
            !content.TryRead("menus/falloutdict.txt", null, out var after, out var afterSource) ||
            !bytes.SequenceEqual(after) || source != afterSource)
            throw new InvalidDataException("Owned hacking resources changed during construction.");
        Console.WriteLine($"OPENNV_OWNED_HACKING_CONSTRUCTION_PASS terminal={key} " +
            $"terminalHash={Convert.ToHexString(terminalHash)} dictionaryHash={dictionary.SourceHash} " +
            $"wordLength={request.WordLength} requested={result.RequestedCount} retained={result.Words.Count} " +
            $"maximumAttempts={result.MaximumAttempts} sourceBytesUnchanged=true " +
            "ownerQualification=callerRequired oracleComparison=callerRequired ordinaryInteraction=unverified");
        return result;
    }
}
