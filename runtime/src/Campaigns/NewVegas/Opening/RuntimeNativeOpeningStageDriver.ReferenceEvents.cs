using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private RuntimeNativeConversation? _conversation;
    private FalloutBodyPartData? _dialoguePlayerBodyParts;
    private FalloutReferenceScripts? _resultScripts;
    private FalloutQuestStages? _stageResults;
    private OpenNV.Runtime.Gameplay.State.FalloutPipBoyState? _pipBoy;
    internal Func<RuntimeNativeReferencePresentation> ReferencePresentation { get; set; } =
        () => throw new InvalidOperationException("Reference presentation has no current cell owner.");
    internal object? ConversationState => _conversation?.State;
    internal object? StageResultState => _stageResults?.State;
    internal FalloutFormKey? PresentedConversationSpeaker => _conversation?.PresentedSpeaker;
    internal FalloutFormKey? PendingConversationSpeaker => _conversation?.PendingSpeaker;
    internal void ExecutePackageEvent(FalloutPackageEvent program, FalloutFormKey actor) =>
        (_resultScripts ?? throw new InvalidOperationException("Package results have no shared script owner."))
            .ExecutePackageEvent(program, actor);
    internal void DispatchSpeechCompletion(FalloutSpeechCompletionReceipt receipt)
    {
        var result = (_resultScripts ?? throw new InvalidOperationException("Speech completion has no shared script owner."))
            .DispatchSpeechCompletion(receipt);
        if (result.Error is { } error)
            throw new NotSupportedException($"Source SayToDone actor {receipt.Speaker} failed: {error}");
    }
    internal OpenNV.Runtime.Gameplay.State.FalloutPipBoyState PipBoy => _pipBoy ?? throw new InvalidOperationException("Pip-Boy state is absent.");
    internal FalloutQuestState Quests => _quests;
    internal IReadOnlyList<FalloutNativeSkillIdentity> Skills => _skillCatalog;

    internal void AttachBootstrapPlayerPackage(FalloutReferenceScriptEffect effect)
    {
        if (effect.Kind != FalloutReferenceEffectKind.ScriptPackage || effect.Target != _pluginStack.RuntimeFormKey(0x14))
            throw new InvalidDataException("Startup handoff is not a typed player package assignment.");
        ApplyReferenceEffect(effect);
    }
    internal IReadOnlyList<FalloutNativeSkillIdentity> Tags => _tagSkills.Selection;
    internal FalloutPlayerTagSkills PlayerTagSkills => _tagSkills;
    internal IReadOnlyList<FalloutNativeTraitIdentity> Traits => _traits.Where(trait =>
        _scripts.References!.PerkRank(_pluginStack.RuntimeFormKey(0x14), _pluginStack.RuntimeFormKey(trait.RuntimeFormId)) > 0).ToArray();
    internal FalloutRadioStations Radio => _scripts.Radio ?? throw new InvalidOperationException("Radio station owner is absent.");

    private void RefreshRadioStations(bool force = false)
    {
        var position = _player.GlobalPosition / _player.UnitsToMeters;
        Radio.Refresh(new(_activeCell, [position.X, -position.Z, position.Y], [0, 0, 0]), force);
    }

    internal bool IsInSameCell(FalloutFormKey caller, FalloutFormKey target)
    {
        var position = _player.GlobalPosition / _player.UnitsToMeters;
        return _scripts.References!.InSameCell(caller, target,
            new(_activeCell, [position.X, -position.Z, position.Y], [0, 0, 0]), _player.UnitsToMeters);
    }

    internal float ReferenceDistance(FalloutFormKey caller, FalloutFormKey target)
    {
        var position = _player.GlobalPosition / _player.UnitsToMeters;
        return _scripts.References!.Distance(caller, target,
            new(_activeCell, [position.X, -position.Z, position.Y], [0, 0, 0]), _player.UnitsToMeters);
    }

    internal bool ReferenceInZone(FalloutFormKey caller, FalloutFormKey zone)
    {
        var position = _player.GlobalPosition / _player.UnitsToMeters;
        return _scripts.References!.IsInZone(caller, zone,
            new(_activeCell, [position.X, -position.Z, position.Y], [0, 0, 0]), _player.UnitsToMeters);
    }

    internal float ReferenceHeadingAngle(FalloutFormKey caller, FalloutFormKey target)
    {
        FalloutReferencePlacement? Live(FalloutFormKey reference)
        {
            var node = reference == _pluginStack.RuntimeFormKey(0x14) ? _player :
                ReferencePresentation().Nodes.GetValueOrDefault(reference);
            if (node is null) return null;
            var position = node.GlobalPosition / _player.UnitsToMeters;
            var rotation = GamebryoCoordinate.ReferenceEuler(node.GlobalBasis);
            var cell = reference == _pluginStack.RuntimeFormKey(0x14) ? _activeCell : _scripts.References!.Placement(reference).Cell;
            return new(cell, [position.X, -position.Z, position.Y], [rotation.X, rotation.Y, rotation.Z]);
        }
        return _scripts.References!.HeadingAngle(caller, target, Live(_pluginStack.RuntimeFormKey(0x14)), _player.UnitsToMeters, Live);
    }

    internal bool IsInInterior(FalloutFormKey reference) => _scripts.References!.IsInInterior(reference, _activeCell);

    internal bool IsInCell(FalloutFormKey reference, FalloutFormKey cell) => _scripts.References!.IsInCell(reference, cell, _activeCell);

    private int ActorSitting(FalloutFormKey reference) => _pluginStack.RuntimeFormId(reference) == 0x14
        ? _player.SittingState : _scripts.References!.GetSitting(reference);

    private void ConfigureConversation()
    {
        _pipBoy = new(_pluginStack, _inventory);
        Radio.SignalDiscovered = () =>
        {
            var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Radio discovery has no owned content source.");
            _radioHudDeclaration ??= FalloutExecutableStringTable.ReadRadioHudDeclaration(
                Path.Combine(Path.GetDirectoryName(source.ContentRoot)!,
                    source.Game == RuntimeLiveContentSource.Fallout3Game ? "Fallout3.exe" : "FalloutNV.exe"));
            _scripts.Sounds.Play(_pluginStack.RuntimeFormKey(0x14),
                FalloutDialogueTopic.Find(_pluginStack, "SOUN", _radioHudDeclaration.SoundEditorId).FormKey);
        };
        var results = new FalloutReferenceScripts(_pluginStack, _scripts.References!, _quests,
            new((actor, furniture) => _pluginStack.RuntimeFormId(actor) == 0x14 ? _player.CurrentFurniture == furniture :
                GetTree().Root.FindChildren("*", "", true, false).OfType<RuntimeNativeNpc>()
                    .Single(npc => npc.Appearance.Reference == actor).CurrentFurniture == furniture, ApplyReferenceEffect,
                _scripts.MessageResults.Take, actor => _speech!.IsTalking(actor), ActorValue, IsPlayerTagSkill, _globals,
                ApplyNativeSourceCommand, IsInCombat, IsInSameCell, _scripts.Events, ReferenceDistance, IsInInterior,
                (reference, group, initialization) => ReferencePresentation().PlayGroup(reference, group, initialization),
                (reference, group) => ReferencePresentation().IsAnimPlaying(reference, group), () => Vitals.Level,
                () => _scripts.Session.LocationSpecificLoadScreensOnly, () => _scripts.Session.InCharGen,
                reference => ReferencePresentation().GetOpenState(reference),
                ReadActorValue: ReadActorValue, ChangeActorValue: ChangeActorValue, Inventory: InventoryCommands, Challenges: _scripts.Challenges,
                HeadingAngle: ReferenceHeadingAngle, ResetPlayerHealth: _vitals.ResetHealth,
                CurrentPackage: CurrentActorPackage, Sitting: ActorSitting, TagSkills: _tagSkills, IsInCell: IsInCell, IsHardcore: () => _scripts.Session.Hardcore,
                RewardXp: RewardPlayerExperience, GameTime: _gameTime,
                Placement: ReferenceScriptPlacement, IsPcSleeping: _player.IsPcSleeping,
                Sleeping: ActorSleeping, KnockedState: ActorKnockedState,
                SleepWait: PlayerRest, OpenSleepWaitMenu: OpenCurrentPlayerRest, Statistics: PlayerStatistics));
        results.BindCampaignChallengeRewards();
        _resultScripts = results;
        _stageResults = new(_pluginStack, _quests, results.StageSteps,
            EvaluateMessageCondition, () => !_moviePlaying, evaluateRunOn: true);
        _scripts.References!.ExecutePerkQuestStage = ExecutePlayerPerkQuestStage;
        RestoreStageResults();
        _scriptHost = _scriptHost with
        {
            ExecuteProgram = results.ExecuteProgram,
            ExecuteCompiledProgram = results.ExecuteProgram,
            CanContinueCompiled = gameMode => !_moviePlaying && _stageResults?.HasPendingResults != true && (!gameMode || !GetTree().Paused),
            InvokeFunction = results.InvokeFunction,
            TagSkills = _tagSkills,
            IsInCell = IsInCell
        };
        _scripts.Host = _scriptHost;
        _speech!.ExecuteOwnedResults = results.ExecuteResultOwned;
        _conversation = new();
        _conversation.Configure(_pluginStack, _quests, _player, _speech!, condition =>
        {
            if (FalloutPlatformConditions.Evaluate(condition) is { } platform) return platform;
            if (condition.Function is 56 or 58 or 59 or 79 or 420 or 421 or 546) return _quests.Evaluate(condition);
            if (condition.Function == 74) return (_globals ?? throw new InvalidOperationException("Dialogue has no global state owner.")).Get(condition.FormArgument1);
            if (condition.Function == 53) return (float)_scripts.References!.ReadVariable(_quests, condition.FormArgument1, condition.Argument2);
            if (condition.Function == 492 && condition.RunOn == 2)
                return _scripts.References!.MapMarkerVisibility(condition.Owner.Plugin.AdjustFormId(condition.Reference));
            if (condition.Function == 612) return FalloutExteriorClimate.ContainsRegion(_pluginStack, condition.FormArgument1,
                FalloutCellSceneReader.ParentWorldspace(_pluginStack.GetEffective(_activeCell)),
                _player.GlobalPosition.X / _player.UnitsToMeters, -_player.GlobalPosition.Z / _player.UnitsToMeters) ? 1 : 0;
            if (FalloutInventoryConditions.EvaluateDialoguePlayer(_pluginStack, _inventory, _playerSkills.HasPerk, condition) is { } inventory)
                return inventory;
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
            actor => _scripts.References!.ActorFactions(actor), () => _character.Female,
            actor => _scripts.References!.ActorRace(actor), _scripts.ScriptValues.RandomBounded, CurrentActorPackage,
            _scripts.ActorQueries.GetVampire, InventoryCommands.ItemCount, ReferenceDistance, ReferenceInZone, ActorSitting,
            _scripts.References!.GetDeadCount);
        AddChild(_conversation);
    }

    private float DialogueActorValue(FalloutFormKey actor, int value)
    {
        if (value is >= 62 and <= 71)
            return _scripts.References!.ActorValue(actor, FalloutActorValue.UserSlot(value));
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

    private FalloutActorAppearanceState PlayerCreationState =>
        FalloutNativeCharacterCreation.ActorState(_pluginStack, _raceSexContract.Player, _character) with { PlayerYoung = _scripts.Session.PlayerYoung };

    private FalloutActorAppearanceState PlayerActorState
    {
        get
        {
            var player = _pluginStack.RuntimeFormKey(0x14);
            var original = PlayerCreationState;
            var changed = _scripts.References!.ActorAppearanceOverride(player);
            return original with
            {
                Race = _scripts.References.ActorRace(player),
                Height = _scripts.References.ActorHeight(player),
                Hair = changed?.HairOverridden == true ? changed.Hair : original.Hair,
                HairOverridden = changed?.HairOverridden == true,
            };
        }
    }

    internal FalloutNpcAppearance PlayerAppearance => FalloutNpcAppearanceResolver.Resolve(_pluginStack, _raceSexContract.Player,
        equippedArmor: _inventory.Equipped.Select(_pluginStack.RuntimeFormKey).Where(key => _pluginStack.GetEffective(key).Signature == "ARMO").ToArray(),
        appearanceState: PlayerActorState);

    internal int TakeMessageButton(FalloutFormKey caller) => _scripts.MessageResults.Take(caller);
    internal bool IsTalking(FalloutFormKey actor) => _speech?.IsTalking(actor) ??
        throw new InvalidOperationException("Actor speech owner is absent.");
    internal bool IsDialogueBusy(FalloutFormKey actor) => _speech?.IsDialogueBusy(actor) ??
        throw new InvalidOperationException("Actor speech owner is absent.");
    internal bool IsNpcDialogueActive(FalloutFormKey actor) => _speech?.IsNpcDialogueActive(actor) ??
        throw new InvalidOperationException("Actor speech owner is absent.");
    internal bool IsInCombat(FalloutFormKey actor) => _scripts.References!.IsInCombat(actor,
        _scripts.References.PlayerInCombat);
    internal void RequestPackageDialogue(FalloutFormKey speaker, FalloutDialoguePackage package, Action completed)
    {
        if (package.Type == 1)
            (_speech ?? throw new InvalidOperationException("Speech owner is absent.")).StartPackageSpeech(speaker, package.Target,
                package.Topic ?? throw new InvalidDataException("SayTo dialogue package has no source topic."), completed);
        else if (_pluginStack.RuntimeFormId(package.Target) != 0x14)
            (_speech ?? throw new InvalidOperationException("Speech owner is absent.")).StartNpcConversation(speaker, package.Target,
                package.Topic ?? FalloutDialogueTopic.Find(_pluginStack, "DIAL", "GREETING").FormKey, completed);
        else (_conversation ?? throw new InvalidOperationException("Conversation owner is absent.")).Request(speaker, package.Target, package.Topic, completed);
    }
    internal double ActorValue(FalloutFormKey actor, string name) => _pluginStack.RuntimeFormId(actor) == 0x14 ?
        ReadPlayerActorValue(name, FalloutActorValueRead.Current) : _scripts.References!.ActorValue(actor, name);
    private static bool IsSpecial(string name) => FalloutNativeVigorResolver.AttributeNames.Any(value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
    private double ReadPlayerActorValue(string name, FalloutActorValueRead kind) => IsSpecial(name)
        ? _playerActorValues.Read(FalloutPlayerActorValues.SpecialValue(name), kind)
        : _playerSkills.IsSkill(name) ? _playerSkills.ReadSkill(name, kind)
        : kind == FalloutActorValueRead.Current ? _scriptHost.PlayerActorValue(name) :
            throw new NotSupportedException($"Player base/permanent value {name} has no pool/formula owner.");
    internal double ReadActorValue(FalloutFormKey actor, string name, FalloutActorValueRead kind) => _pluginStack.RuntimeFormId(actor) == 0x14
        ? ReadPlayerActorValue(name, kind) : kind == FalloutActorValueRead.Current ? _scripts.References!.ActorValue(actor, name) :
            throw new NotSupportedException($"Reference {actor} base/permanent value {name} has no query owner.");
    internal void ChangeActorValue(FalloutFormKey actor, string name, string operation, double value)
    {
        if (_pluginStack.RuntimeFormId(actor) == 0x14) ChangePlayerActorValue(name, operation, value);
        else _scripts.References!.ChangeActorValue(actor, name, operation, (float)value);
    }
    internal bool IsPlayerTagSkill(string name) => _tagSkills.IsTagged(name);
    internal float PlayerSkillValue(string name) => _playerSkills.Value(name);
    internal float PlayerCombatValue(int value) => _playerSkills.Value(value);
    internal IReadOnlyList<FalloutPerkEntry> PlayerPerkEntries => _playerSkills.PerkEntries;
    private int SourcePlayerLevel => _vitals?.State.Level ??
        throw new InvalidOperationException("Player level has no initialized persistent vitals owner.");

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
            case FalloutReferenceEffectKind.LoadingScreenPolicy:
                _scripts.Session.LocationSpecificLoadScreensOnly = effect.Enable;
                break;
            case FalloutReferenceEffectKind.CharacterGeneration:
                _scripts.Session.SetInCharGen(effect.Enable);
                break;
            case FalloutReferenceEffectKind.PlayerToddler:
                _scripts.Session.SetPlayerToddler(effect.Enable);
                break;
            case FalloutReferenceEffectKind.PlayerScale:
                _scripts.Session.SetPlayerScale(effect.Scale);
                _player.ApplySourceScale(_scripts.Session.PlayerScale);
                break;
            case FalloutReferenceEffectKind.PlayerYouth:
                _scripts.Session.SetPlayerYoung(effect.Enable);
                break;
            case FalloutReferenceEffectKind.Achievement:
                _scripts.Session.AddAchievement(effect.Value);
                break;
            case FalloutReferenceEffectKind.AddItem or FalloutReferenceEffectKind.EquipItem or FalloutReferenceEffectKind.RemoveItem:
                InventoryCommands.Execute(new(effect.Kind switch
                {
                    FalloutReferenceEffectKind.AddItem => FalloutInventoryCommandKind.Add,
                    FalloutReferenceEffectKind.RemoveItem => FalloutInventoryCommandKind.Remove,
                    _ => FalloutInventoryCommandKind.Equip
                }, effect.Target!.Value, Item: effect.Argument!.Value, Count: effect.Value,
                    Silent: effect.Kind == FalloutReferenceEffectKind.EquipItem || effect.Enable));
                break;
            case FalloutReferenceEffectKind.AddNote:
                if (_inventory.Item(effect.Target!.Value) is null)
                    _inventory.Add(_pluginStack, effect.Target.Value, 1, SourcePlayerLevel, silent: true, _globals);
                break;
            case FalloutReferenceEffectKind.SayTo:
                _speechStage = $"{QuestEditorId}:{Stage}";
                _speech!.SayTo(effect.Target!.Value, effect.Argument!.Value, effect.Topic!.Value, effect.ForceSubtitles);
                break;
            case FalloutReferenceEffectKind.Say:
                _speechStage = $"{QuestEditorId}:{Stage}";
                _speech!.Say(effect.Target!.Value, effect.Topic!.Value, effect.ForceSubtitles);
                break;
            case FalloutReferenceEffectKind.PackageEventTopic:
                _speechStage = $"{QuestEditorId}:{Stage}";
                _speech!.StartPackageEventTopic(effect.Target!.Value, effect.Argument!.Value,
                    effect.PackageEvent!, effect.Topic!.Value);
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
                    if (_scripts.References!.ApplyActorScriptPackage(effect.Target.Value, effect.Argument))
                        EvaluateActorPackages(effect.Target.Value, false);
                }
                else _playerPackage!.Apply(effect.Argument);
                break;
            case FalloutReferenceEffectKind.ImageSpace:
                if (effect.Enable) _imageSpaceState.Apply(FalloutImageSpaceModifierReader.Read(_pluginStack.GetEffective(effect.Target!.Value)));
                else _imageSpaceState.Remove(effect.Target!.Value);
                break;
            case FalloutReferenceEffectKind.Texture or FalloutReferenceEffectKind.ReferenceEnable:
                if (!_scripts.References!.IsResident(effect.Target!.Value)) break;
                ReferencePresentation().Apply(effect);
                break;
            case FalloutReferenceEffectKind.DoorOpenState:
                ReferencePresentation().Apply(effect);
                break;
            case FalloutReferenceEffectKind.Conversation:
                (_conversation ?? throw new InvalidOperationException("Conversation owner is absent.")).Request(
                    effect.Target ?? throw new InvalidDataException("Conversation speaker is absent."),
                    effect.Argument ?? throw new InvalidDataException("Conversation target is absent."), effect.Topic);
                break;
            case FalloutReferenceEffectKind.SetStage:
                Godot.GD.Print($"OPENNV_NATIVE_REFERENCE_SET_STAGE source={effect.Source} quest={effect.Target} stage={effect.Stage}");
                _scriptHost.PrepareSetStage(effect.Target ?? throw new InvalidDataException("SetStage target is absent."), effect.Stage)();
                break;
            case FalloutReferenceEffectKind.PlayerControls:
                ApplySourcePlayerControls(effect);
                break;
            case FalloutReferenceEffectKind.Message when effect.Message is { } messageCall:
                _scripts.ShowCompiledMessage(effect.Target ?? throw new InvalidDataException("Compiled message target is absent."), messageCall);
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
