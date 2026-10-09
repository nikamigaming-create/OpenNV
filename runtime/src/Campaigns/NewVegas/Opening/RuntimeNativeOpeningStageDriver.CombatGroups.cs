using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private bool _combatGroupInputsAttached;
    private Func<FalloutExperienceLevelIntroInput>? _combatGroupCountReader;
    private FalloutReferenceWorld CombatGroupWorld => _scripts.References ??
        throw new InvalidOperationException("CombatManager has no actual shared reference world.");
    internal object? CombatGroupState => _scripts.References?.CombatGroupState;
    internal string? CombatGroupSaveBlocker => !_combatGroupInputsAttached ?
        "source-combat-group-native-player-attachment-absent" : CombatGroupWorld.CombatGroupSaveBlocker;

    // Configure before source programs can start combat. Cold lists are exact
    // retained state; neither a native attachment nor an IsInCombat Boolean
    // recreates targets or grants an absent detection decision.
    internal void ConfigureSourceCombatGroups(FalloutCombatGroupsSnapshot? restore)
    {
        var live = _pluginStack.OwnedSource ?? throw new InvalidOperationException("Combat group source selection is absent.");
        var declaration = FalloutCombatGroupDeclaration.Read(ExperienceHudSource.Declaration);
        if (CombatGroupWorld.CombatGroupsConfigured) CombatGroupWorld.RequireCombatGroupBinding(declaration, live.StackId, restore);
        else CombatGroupWorld.ConfigureCombatGroups(declaration, live.StackId, restore);
    }

    internal void AttachSourceCombatGroups()
    {
        if (_combatGroupInputsAttached || !IsInsideTree() || !_player.CombatGroupPlayerPublished)
            throw new InvalidOperationException("Combat group input requires its unique actual attached player body.");
        _ = CombatGroupWorld.CombatGroups;
        _combatGroupCountReader = ReadActualPlayerCombatGroupTargets;
        BindExperienceLevelIntroInput(_combatGroupCountReader);
        _combatGroupInputsAttached = true;
    }

    private FalloutExperienceLevelIntroInput ReadActualPlayerCombatGroupTargets()
    {
        if (!_combatGroupInputsAttached || !IsInsideTree() || !_player.CombatGroupPlayerPublished)
            return new(null, "source-combat-group-actual-player-body-unavailable");
        return CombatGroupWorld.ReadPlayerCombatGroupTargets();
    }

    private void AdvanceSourceCombatGroups(double delta)
    {
        if (!double.IsFinite(delta) || delta < 0 || delta > float.MaxValue)
            throw new InvalidDataException("CombatManager received an invalid engine simulation interval.");
        if (!_combatGroupInputsAttached || !IsInsideTree() || !_player.CombatGroupPlayerPublished)
            throw new NotSupportedException("CombatManager cannot advance without the actual published player body.");
        // Ordered source save preparation temporarily makes the driver Always.
        // That invocation drain does not advance the paused combat update.
        if (GetTree().Paused || !CanProcess()) return;
        CombatGroupWorld.AdvanceCombatGroups((float)delta);
    }

    private FalloutCombatGroupsSnapshot CaptureSourceCombatGroups()
    {
        if (CombatGroupSaveBlocker is { } blocker) throw new NotSupportedException(blocker);
        return CombatGroupWorld.CaptureCombatGroups();
    }

    private void RetireCombatGroupInputs()
    {
        _combatGroupInputsAttached = false;
        if (_experienceLevelIntroInput == _combatGroupCountReader) _experienceLevelIntroInput = null;
        _combatGroupCountReader = null;
        // The world retains member/target lists across presentation retirement.
        // Its own disposal retires that authoritative owner independently.
    }
}
