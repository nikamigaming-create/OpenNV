using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private RuntimeNativeConversation? _conversation;
    private FalloutBodyPartData? _dialoguePlayerBodyParts;
    private FalloutReferenceScripts? _resultScripts;
    private FalloutQuestStages? _stageResults;
    private OpenNV.Runtime.Gameplay.State.FalloutPipBoyState? _pipBoy;
    internal object? ConversationState => _conversation?.State;
    internal OpenNV.Runtime.Gameplay.State.FalloutPipBoyState PipBoy => _pipBoy ?? throw new InvalidOperationException("Pip-Boy state is absent.");
    internal FalloutQuestState Quests => _quests;
    internal IReadOnlyList<FalloutNativeSkillIdentity> Skills => _tagSkillContract.Skills;
    internal IReadOnlyList<FalloutNativeSkillIdentity> Tags => _tagSkills;
    internal IReadOnlyList<FalloutNativeTraitIdentity> Traits => _traits;

    internal bool IsInSameCell(FalloutFormKey caller, FalloutFormKey target)
    {
        var position = _player.GlobalPosition / _player.UnitsToMeters;
        return _scripts.References!.InSameCell(caller, target,
            new(_activeCell, [position.X, -position.Z, position.Y], [0, 0, 0]), _player.UnitsToMeters);
    }

    private void ConfigureConversation()
    {
        _pipBoy = new(_pluginStack, _inventory);
        var results = new FalloutReferenceScripts(_pluginStack, _scripts.References!, _quests,
            new((actor, furniture) => _pluginStack.RuntimeFormId(actor) == 0x14 ? _player.CurrentFurniture == furniture :
                GetTree().Root.FindChildren("*", "", true, false).OfType<RuntimeNativeNpc>()
                    .Single(npc => npc.Appearance.Reference == actor).CurrentFurniture == furniture, ApplyReferenceEffect,
                _scripts.MessageResults.Take, actor => _speech!.IsTalking(actor), ActorValue, IsPlayerTagSkill, _globals,
                ApplyNativeSourceCommand, IsInCombat, IsInSameCell));
        _resultScripts = results;
        _stageResults = new(_pluginStack, _quests, results.StageSteps,
            condition => FalloutPlatformConditions.Evaluate(condition) ?? _quests.Evaluate(condition), () => !_moviePlaying);
        _scriptHost = _scriptHost with { ExecuteProgram = results.ExecuteProgram };
        _scripts.Host = _scriptHost;
        _speech!.ExecuteResults = results.ExecuteResult;
        _conversation = new();
        _conversation.Configure(_pluginStack, _quests, _player, _speech!, condition =>
        {
            if (FalloutPlatformConditions.Evaluate(condition) is { } platform) return platform;
            if (condition.Function is 56 or 58 or 59 or 79 or 420 or 421 or 546) return _quests.Evaluate(condition);
            if (condition.Function == 74) return (_globals ?? throw new InvalidOperationException("Dialogue has no global state owner.")).Get(condition.FormArgument1);
            if (condition.Function == 53) return (float)_scripts.References!.Get(condition.FormArgument1).Read(condition.Argument2);
            if (condition.Function == 492 && condition.RunOn == 2)
                return _scripts.References!.MapMarkerVisibility(condition.Owner.Plugin.AdjustFormId(condition.Reference));
            if (condition.Function == 612) return FalloutExteriorClimate.ContainsRegion(_pluginStack, condition.FormArgument1,
                FalloutCellSceneReader.ParentWorldspace(_pluginStack.GetEffective(_activeCell)),
                _player.GlobalPosition.X / _player.UnitsToMeters, -_player.GlobalPosition.Z / _player.UnitsToMeters) ? 1 : 0;
            if (condition.RunOn == 1 && condition.Function == 70 && condition.Argument1 <= 1)
                return (condition.Argument1 == 1) == _character.Female ? 1 : 0;
            throw new NotSupportedException($"Conversation condition {condition.Owner.FormKey}/{condition.Function}/{condition.RunOn} is unbound.");
        }, results.ExecuteResult, _scripts.SaidInfos, (speaker, condition) => condition.RunOn switch
        {
            0 => _scripts.References!.Placement(speaker).Cell,
            1 => _activeCell,
            2 => condition.Owner.Plugin.AdjustOptionalFormId(condition.Reference) is { } reference
                ? _pluginStack.RuntimeFormId(reference) == 0x14 ? _activeCell : _scripts.References!.Placement(reference).Cell
                : null,
            _ => null,
        }, actor =>
        {
            if (_pluginStack.RuntimeFormId(actor) == 0x14)
                return Vitals.ExactHitPoints / Vitals.MaximumHitPoints;
            var health = _scripts.References!.Health(actor);
            var maximum = health.Base + health.Permanent + health.Temporary;
            return maximum <= 0 ? 0 : Math.Clamp(health.Current / maximum, 0, 1);
        }, DialogueActorValue, actor => _scripts.References!.Get(actor).Templates,
            actor => _scripts.References!.Get(actor).TalkedToPlayer = true,
            actor => _scripts.References!.Get(actor).TalkedToPlayer,
            actor => _scripts.References!.ActorFactions(actor));
        AddChild(_conversation);
    }

    private float DialogueActorValue(FalloutFormKey actor, int value)
    {
        if (_pluginStack.RuntimeFormId(actor) == 0x14)
        {
            if (value == 16) return Vitals.ExactHitPoints;
            if (value == 12) return Vitals.ActionPoints;
            if (value == 54) return Vitals.RadiationRads;
            if (value is >= 25 and <= 30)
            {
                _dialoguePlayerBodyParts ??= FalloutBodyPartData.Read(_pluginStack.GetEffective(_pluginStack.RuntimeFormKey(0x1d)));
                return _dialoguePlayerBodyParts.LimbCondition(value, Vitals.MaximumHitPoints, Vitals.LimbDamage);
            }
            return _playerSkills.Value(value);
        }
        var world = _scripts.References!;
        if (value == 16) return world.Health(actor).Current;
        if (value is >= 25 and <= 30)
            return world.BodyParts(actor).LimbCondition(value, world.HealthSource(actor).Health, world.Get(actor).Injury?.LimbDamage);
        throw new NotSupportedException($"Dialogue actor {actor} value {value} has no state owner.");
    }

    internal FalloutNpcAppearance PlayerAppearance => FalloutNpcAppearanceResolver.Resolve(_pluginStack, _raceSexContract.Player,
        equippedArmor: _inventory.Equipped.Select(_pluginStack.RuntimeFormKey).Where(key => _pluginStack.GetEffective(key).Signature == "ARMO").ToArray(),
        appearanceState: FalloutNativeCharacterCreation.ActorState(_pluginStack, _raceSexContract.Player, _character));

    internal int TakeMessageButton(FalloutFormKey caller) => _scripts.MessageResults.Take(caller);
    internal bool IsTalking(FalloutFormKey actor) => _speech?.IsTalking(actor) ??
        throw new InvalidOperationException("Actor speech owner is absent.");
    internal bool IsInCombat(FalloutFormKey actor) => _pluginStack.RuntimeFormId(actor) == 0x14
        ? GetTree().GetNodesInGroup("OpenNVNativeCombatActors").OfType<RuntimeNativeActorCombat>().Any(owner => owner.EngagedWith(actor))
        : !_scripts.References!.IsDead(actor) && _scripts.References.Get(actor).Engagement is { Action: not "idle" };
    internal void RequestPackageDialogue(FalloutFormKey speaker, FalloutDialoguePackage package, Action completed) =>
        (_conversation ?? throw new InvalidOperationException("Conversation owner is absent."))
            .Request(speaker, package.Target, package.Topic, completed);
    internal double ActorValue(FalloutFormKey actor, string name) => _pluginStack.RuntimeFormId(actor) == 0x14 ?
        _scriptHost.PlayerActorValue(name) : _scripts.References!.ActorValue(actor, name);
    internal bool IsPlayerTagSkill(string name) => _tagSkills.Any(skill => SkillName(skill.EditorId).Equals(name, StringComparison.OrdinalIgnoreCase));
    internal float PlayerSkillValue(string name) => _playerSkills.Value(name);
    internal float PlayerCombatValue(int value) => _playerSkills.Value(value);
    internal IReadOnlyList<FalloutPerkEntry> PlayerPerkEntries => _playerSkills.PerkEntries;
    private static string SkillName(string editorId) => FalloutPlayerSkills.SkillName(editorId);
    private int SourcePlayerLevel
    {
        get
        {
            var data = _pluginStack.GetEffective(_raceSexContract.Player).ReadSubrecords().Single(field => field.Signature == "ACBS").Data;
            if (data.Length != 24) throw new InvalidDataException("Player ACBS extent is invalid.");
            var level = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(data.Span[8..]);
            return level >= 1 ? level : throw new InvalidDataException("Initial player level is not positive.");
        }
    }

    internal void ApplyReferenceEffect(FalloutReferenceScriptEffect effect)
    {
        switch (effect.Kind)
        {
            case FalloutReferenceEffectKind.PipBoyReset:
                (_pipBoy ?? throw new InvalidOperationException("Pip-Boy state owner is absent.")).Reset();
                break;
            case FalloutReferenceEffectKind.ScriptActivate:
                GetTree().Root.FindChildren("*", "", true, false).OfType<RuntimeNativeReferenceEvents>()
                    .Single(owner => owner.IsProcessing()).ScriptActivate(effect.Target!.Value, effect.Argument!.Value, effect.Enable);
                break;
            case FalloutReferenceEffectKind.Hardcore:
                _scripts.Session.Hardcore = effect.Enable;
                break;
            case FalloutReferenceEffectKind.AutoDisplayObjectives:
                _scripts.Session.AutoDisplayObjectives = effect.Enable;
                break;
            case FalloutReferenceEffectKind.Achievement:
                _scripts.Session.AddAchievement(effect.Value);
                break;
            case FalloutReferenceEffectKind.AddItem or FalloutReferenceEffectKind.EquipItem or FalloutReferenceEffectKind.RemoveItem:
                if (_pluginStack.RuntimeFormId(effect.Target!.Value) != 0x14)
                    throw new NotSupportedException("Non-player scripted inventory has no shared container owner.");
                if (effect.Kind == FalloutReferenceEffectKind.AddItem)
                    _inventory.Add(_pluginStack, effect.Argument!.Value, effect.Value, SourcePlayerLevel, effect.Enable, _globals);
                else if (effect.Kind == FalloutReferenceEffectKind.RemoveItem) _inventory.Remove(effect.Argument!.Value, effect.Value, effect.Enable);
                else _inventory.Equip(_pluginStack, effect.Argument!.Value);
                break;
            case FalloutReferenceEffectKind.AddNote:
                if (_inventory.Item(effect.Target!.Value) is null)
                    _inventory.Add(_pluginStack, effect.Target.Value, 1, SourcePlayerLevel, silent: true, _globals);
                break;
            case FalloutReferenceEffectKind.SayTo:
                _speechStage = $"{_machine.QuestEditorId}:{_machine.Stage}";
                _speech!.SayTo(effect.Target!.Value, effect.Argument!.Value, effect.Topic!.Value);
                break;
            case FalloutReferenceEffectKind.HeadTracking:
                ApplyLookCommand(new(0, effect.Target!.Value, effect.Argument));
                break;
            case FalloutReferenceEffectKind.EvaluatePackages:
                EvaluateActorPackages(effect.Target!.Value, effect.Enable);
                break;
            case FalloutReferenceEffectKind.ScriptPackage:
                if (_pluginStack.RuntimeFormId(effect.Target!.Value) != 0x14)
                {
                    if (effect.Argument is not null) throw new NotSupportedException("NPC script package simulation is unbound.");
                    // NPC AddScriptPackage is rejected, so no script override can
                    // be resident. Reevaluate without deleting any base package.
                    EvaluateActorPackages(effect.Target.Value, false);
                }
                else _playerPackage!.Apply(effect.Argument is { } package ? FalloutDialogueTopic.Text(_pluginStack.GetEffective(package)
                    .ReadSubrecords().Single(field => field.Signature == "EDID").Data.Span) : null);
                break;
            case FalloutReferenceEffectKind.ImageSpace:
                if (effect.Enable) _imageSpaceState.Apply(FalloutImageSpaceModifierReader.Read(_pluginStack.GetEffective(effect.Target!.Value)));
                else _imageSpaceState.Remove(effect.Target!.Value);
                break;
            case FalloutReferenceEffectKind.Texture or FalloutReferenceEffectKind.ReferenceEnable:
                if (!_scripts.References!.IsResident(effect.Target!.Value)) break;
                GetTree().Root.FindChildren("*", "", true, false).OfType<RuntimeNativeReferencePresentation>().Single().Apply(effect);
                break;
            case FalloutReferenceEffectKind.Conversation:
                (_conversation ?? throw new InvalidOperationException("Conversation owner is absent.")).Request(
                    effect.Target ?? throw new InvalidDataException("Conversation speaker is absent."),
                    effect.Argument ?? throw new InvalidDataException("Conversation target is absent."), effect.Topic);
                break;
            case FalloutReferenceEffectKind.SetStage:
                _scriptHost.PrepareSetStage(effect.Target ?? throw new InvalidDataException("SetStage target is absent."), effect.Stage)();
                break;
            case FalloutReferenceEffectKind.PlayerControls:
                _machine.ApplyControls(new(effect.Enable, effect.Controls ?? throw new InvalidDataException("Player controls are absent.")));
                _player.ApplySourceControls(_machine.ControlState);
                break;
            case FalloutReferenceEffectKind.Message:
                var messageOwner = _pluginStack.GetEffective(effect.Source);
                var script = messageOwner.Signature == "QUST" ? FalloutScriptLocals.AttachedScript(_pluginStack, messageOwner)?.FormKey :
                    _scripts.References?.Get(effect.Source).Script?.Record.FormKey;
                _scripts.ShowMessage(effect.Target ?? throw new InvalidDataException("ShowMessage target is absent."), script,
                    messageOwner.Signature == "QUST" ? null : effect.Source);
                break;
            case FalloutReferenceEffectKind.SpecialMenu:
                OpenVigorMenu(effect.Value);
                break;
            default:
                throw new NotSupportedException($"Reference effect {effect.Kind} from {effect.Source} has no ordinary runtime owner.");
        }
    }
}
