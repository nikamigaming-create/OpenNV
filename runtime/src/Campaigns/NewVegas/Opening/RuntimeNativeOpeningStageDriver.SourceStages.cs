using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    // Called by the real shared command/effect host, never by a diagnostic
    // stage inventory, attachment, completion callback or predicted edge.
    internal void RequestSourceStage(FalloutFormKey quest, short stage)
    {
        var owner = _stageResults ?? throw new InvalidOperationException("Quest stage result owner is absent.");
        var source = _controls.Quests.Values.SelectMany(values => values.Values)
            .SingleOrDefault(value => value.Quest == quest && value.Stage == stage);
        if (source is not null) _sourceQuestEditorId = source.QuestEditorId;
        Godot.GD.Print($"OPENNV_NATIVE_SET_STAGE quest={quest} stage={stage} owner=shared-script-host");
        owner.Enter(quest, stage);
        // The authoritative quest owns the entered prefix and any nested
        // target. Observation executes no result or predicted control mask.
        Synchronize();
    }

    internal void ApplySourcePlayerControls(FalloutReferenceScriptEffect effect)
    {
        if (effect.Kind != FalloutReferenceEffectKind.PlayerControls)
            throw new InvalidDataException("Player controls require their actual typed effect.");
        var command = new FalloutPlayerControlCommand(effect.Enable, effect.Controls ??
            throw new InvalidDataException("Player controls are absent."));
        _sourceControls = command.Apply(_sourceControls);
        _player.ApplySourceControls(PlayerControls);
    }

    private IReadOnlyCollection<string> ActiveSourcePresentationOwners()
    {
        var owners = new List<string>();
        if (_moviePlaying) owners.Add("playbink");
        if (_nameEntry is not null) owners.Add("getplayername");
        if (_raceSexEntry is not null) owners.Add(_raceMenuCommand);
        if (_vigorEntry is not null) owners.Add("special-menu");
        if (_specialBookEntry is not null) owners.Add("special-book-menu");
        if (_tagSkillEntry is not null) owners.Add("settagskills");
        if (_traitEntry is not null) owners.Add("showtraitmenu");
        if (_levelUpEntry is not null) owners.Add("level-up-menu");
        if (_speech?.Active == true) owners.Add("speech");
        return owners;
    }
}
