using System.Security.Cryptography;
using System.Diagnostics;
using System.Globalization;
using OpenNV.Runtime.Content;

internal static class OwnedHackingProbe
{
    internal static void RunProcess(string dataRoot)
    {
        uint terminal;
        float science;
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(dataRoot), includeImplicitPlugins: false);
        using (var content = RuntimeLiveContentSource.Current!)
        using (var records = FalloutPluginStack.Load(content.PluginSources))
        {
            var selected = records.EffectiveRecords("TERM").Select(record => FalloutTerminal.Read(records, record.FormKey))
                .First(value => (value.Flags & 1) == 0 && value.Difficulty <= 4);
            terminal = records.RuntimeFormId(selected.Record.FormKey);
            science = FalloutHackingRequest.Read(records, selected.Record.FormKey, selected.Difficulty, 0).MinimumScience;
        }
        var before = FalloutSharedUnsignedRandom.Process.Observation;
        _ = Run(dataRoot, terminal, science, value => value);
        var middle = FalloutSharedUnsignedRandom.Process.Observation;
        _ = Run(dataRoot, terminal, science, value => value);
        var after = FalloutSharedUnsignedRandom.Process.Observation;
        if (middle.Seed is null || after.Seed != middle.Seed ||
            middle.Draws <= before.Draws || after.Draws <= middle.Draws)
            throw new InvalidDataException("Owned generation reset or failed to consume its process unsigned owner.");
        Console.WriteLine($"OPENNV_OWNED_HACKING_PROCESS_PASS terminal={terminal:x8} " +
            $"seed={after.Seed} firstDraws={middle.Draws} secondDraws={after.Draws} " +
            "sameProcessAcrossSourceRetirement=true sourceClock=Win32-GetTickCount retryWindowMilliseconds=100 " +
            "scienceAndPerkInputs=controlled sourceResourcesReadonly=true nativeAdmissionAndRetail=unverified");
    }

    internal static void RunAllowance(string dataRoot)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(dataRoot), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        if (!content.TryRead("menus/falloutdict.txt", null, out var bytes, out var source))
            throw new FileNotFoundException("Owned content has no hacking dictionary.");
        var dictionary = FalloutHackingDictionary.ReadSpaceDelimited(bytes);
        var cases = 0;
        foreach (var length in Enumerable.Range(4, 9))
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var words = dictionary.Words.Where(word => word.Length == length &&
                seen.Add(Convert.ToHexString(word.Bytes))).Take(20).ToArray();
            if (words.Length == 0) continue;
            var allowance = FalloutHackingAllowance.Calculate(words);
            if (allowance == 0 || allowance > words.Length + 1 ||
                allowance != FalloutHackingAllowance.Calculate(words.Reverse().ToArray()))
                throw new InvalidDataException("Owned candidate allowance changed with candidate order.");
            ++cases;
            Console.WriteLine($"OPENNV_OWNED_HACKING_ALLOWANCE_CASE length={length} " +
                $"candidates={words.Length} calculated={allowance} maximumAttempts={Math.Max(4u, allowance)}");
        }
        if (cases == 0 || !content.TryRead("menus/falloutdict.txt", null, out var after, out var afterSource) ||
            source != afterSource || !bytes.SequenceEqual(after))
            throw new InvalidDataException("Owned hacking allowance had no cases or changed dictionary bytes.");
        Console.WriteLine($"OPENNV_OWNED_HACKING_ALLOWANCE_PASS cases={cases} dictionaryHash={dictionary.SourceHash} " +
            "controlledCandidateSelection=true generationRngAndClock=unverified ordinaryHackingAndRetail=unverified");
    }

    // Generation uses the process unsigned/clock boundary. The caller must still
    // supply the real Science/perk inputs for an ordinary interaction; this
    // resource construction helper does not prove reference access admission.
    internal static FalloutHackingCandidates Run(string dataRoot, uint terminalRuntimeId,
        float currentScience, Func<float, float> applyScienceBonus)
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
        var dictionary = FalloutHackingDictionary.ReadSpaceDelimited(bytes);
        var request = FalloutHackingRequest.Read(records, key, terminal.Difficulty, currentScience);
        var randomBefore = FalloutSharedUnsignedRandom.Process.Observation.Draws;
        var started = Stopwatch.GetTimestamp();
        var result = FalloutHackingCandidates.GenerateFromProcess(dictionary, request, applyScienceBonus);
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var draws = FalloutSharedUnsignedRandom.Process.Observation.Draws - randomBefore;
        HackingDisplayContracts.CheckOwned(result);
        if (!SHA256.HashData(terminal.Record.ReadData()).SequenceEqual(terminalHash) ||
            !content.TryRead("menus/falloutdict.txt", null, out var after, out var afterSource) ||
            !bytes.SequenceEqual(after) || source != afterSource)
            throw new InvalidDataException("Owned hacking resources changed during construction.");
        Console.WriteLine($"OPENNV_OWNED_HACKING_CONSTRUCTION_PASS terminal={key} " +
            $"terminalHash={Convert.ToHexString(terminalHash)} dictionaryHash={dictionary.SourceHash} " +
            $"wordLength={request.WordLength} requested={result.RequestedCount} retained={result.Words.Count} " +
            $"maximumAttempts={result.MaximumAttempts} sourceBytesUnchanged=true " +
            $"selectionAttempts={result.SelectionAttempts} retryWindows={result.RetryWindows} sourceDraws={draws} " +
            $"elapsedWallMilliseconds={elapsed.ToString("F3", CultureInfo.InvariantCulture)} " +
            "randomAndClock=process-owner scienceAndPerkQualification=callerRequired " +
            "oracleComparison=unverified ordinaryInteraction=unverified");
        return result;
    }
}
