using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private FalloutFormKey? _hackingReference;
    private FalloutHackingTransaction? _hackingSession;
    private bool _hackingOutcomeApplied;
    private int _hackingReward;

    internal bool TerminalHacked(FalloutFormKey reference)
    {
        // Missing legacy evidence is not a hacked result. Ordinary unlocked or
        // password access remains independent; starting a puzzle requires known
        // history explicitly below.
        if (_scripts.References!.Get(reference).TerminalHackState is null) return false;
        var state = _scripts.References!.TerminalHackResult(reference);
        if (state.Error is not null) throw new InvalidOperationException("Terminal hacking stopped: " + state.Error);
        return state.Hacked;
    }

    internal FalloutHackingTransaction BeginTerminalHacking(FalloutFormKey reference)
    {
        if (_hackingReference is not null || _terminalMenus.Values.Any(menu => menu.Active))
            throw new InvalidOperationException("Another terminal interaction owns the player.");
        RequireTerminalReference(reference);
        var world = _scripts.References!;
        var instance = world.Get(reference);
        var terminal = FalloutTerminal.Read(_pluginStack, instance.Base);
        _ = world.TerminalHackResult(reference);
        if (terminal.Difficulty > 4 || (terminal.Flags & 1) != 0 || instance.LockState is not null)
            throw new NotSupportedException("This terminal needs its key-only, leveled or overridden difficulty owner.");
        if (TerminalHacked(reference)) throw new InvalidOperationException("Terminal is already hacked.");
        if (world.TerminalHackLockedOut(reference, () => FalloutPlayerPerkNumericEffects.Apply(
                20, 0, _playerSkills.PerkEntries, PlayerExperienceCondition)))
            throw new InvalidOperationException("This terminal is locked out.");
        RequireHackingConsequences(reference);
        // Generation's Science entry-point context remains separate from the
        // qualified lockout entry. Do not silently ignore acquired effects.
        if (_playerSkills.PerkEntries.Count != 0)
            throw new NotSupportedException("Hacking generation with acquired perk entries needs its Science bonus owner.");
        var request = FalloutHackingRequest.Read(_pluginStack, instance.Base, terminal.Difficulty, PlayerSkillValue("Science"));
        if (!float.IsFinite(request.CurrentScience) || request.CurrentScience < request.MinimumScience)
            throw new InvalidOperationException("Current Science is below this terminal's requirement.");
        _ = request.RequestedWords(value => value);
        var result = -1; var fallback = 0;
        foreach (var name in new[] { "VeryEasy", "Easy", "Average", "Hard", "VeryHard" })
        {
            if (result >= 0) break;
            var threshold = unchecked((int)FalloutGameSettingIntegers.Read(_pluginStack, "iXPLevelHackComputer" + name));
            var reward = unchecked((int)FalloutGameSettingIntegers.Read(_pluginStack, "iXPRewardHackComputer" + name));
            if (terminal.Difficulty <= threshold) result = reward; else fallback = reward;
        }
        var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned dictionary source is absent.");
        if (!content.TryRead("menus/falloutdict.txt", null, out var bytes, out _))
            throw new FileNotFoundException("Owned hacking dictionary is missing.");
        var dictionary = FalloutHackingDictionary.ReadSpaceDelimited(bytes);
        if (dictionary.Words.Any(word => word.Bytes.ToArray().Any(value => value is < (byte)'A' or > (byte)'Z')))
            throw new NotSupportedException("Hacking dictionary requires its non-ASCII character classification owner.");
        _ = FalloutSourceMilliseconds.Read();
        var candidates = FalloutHackingCandidates.GenerateFromProcess(dictionary, request, value => value);
        var session = FalloutHackingLayout.CreateFromProcess(candidates)
            .CreateTransaction(value => value is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z');
        _hackingReference = reference; _hackingSession = session; _hackingOutcomeApplied = false;
        _hackingReward = result >= 0 ? result : fallback;
        return session;
    }

    private void RequireHackingConsequences(FalloutFormKey reference)
    {
        if (_scripts.References!.Ownership(reference).Owner is not null)
            throw new NotSupportedException("Owned terminal hacking requires its crime/witness consequence owner.");
        _scripts.MiscStatistics.RequireIncrement(5);
        _scripts.Challenges.RequireStatisticIncrement(5);
    }

    internal void ApplyTerminalHackingOutcome(FalloutFormKey reference, FalloutHackingTransaction session)
    {
        if (_hackingReference != reference || !ReferenceEquals(session, _hackingSession) || _hackingOutcomeApplied)
            throw new InvalidOperationException("Terminal hacking outcome has no unconsumed session.");
        if (session.Stage is not (FalloutHackingStage.Succeeded or FalloutHackingStage.LockedOut))
            throw new InvalidOperationException("Terminal hacking has no final outcome.");
        RequireTerminalReference(reference);
        if (session.Stage == FalloutHackingStage.Succeeded) RequireHackingConsequences(reference);
        _hackingOutcomeApplied = true;
        try
        {
            if (session.Stage == FalloutHackingStage.LockedOut)
                _scripts.References!.SetTerminalHackFailure(reference, true);
            else
            {
                _scripts.References!.SetTerminalHacked(reference, true);
                RewardExperience(_hackingReward);
                _scripts.MiscStatistics.Increment(5);
            }
        }
        catch (Exception error) { FailTerminalHacking(reference, error); throw; }
    }

    internal void FailTerminalHacking(FalloutFormKey reference, Exception error)
    {
        var world = _scripts.References!;
        var state = world.TerminalHackResult(reference);
        world.Get(reference).TerminalHackState = state with { Error = state.Error ?? error.Message };
    }

    internal void EndTerminalHacking(FalloutFormKey reference)
    {
        if (_hackingReference != reference) return;
        _hackingReference = null; _hackingSession = null;
    }
}
