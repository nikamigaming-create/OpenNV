using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeConversation
{
    private void BindConversationSource(FalloutFormKey speaker)
    {
        _speaker = speaker;
        var actor = _records.GetEffective(_speaker);
        var npc = _records.GetEffective(FalloutDialogueTopic.RequiredForm(actor, "NAME"));
        _dialogueSubject = _speech.DialogueSubject(_speaker);
        var identity = _speech.SpeakerIdentity(_speaker);
        _identity = identity;
        var resolveRunOnCell = _resolveRunOnCell;
        Func<FalloutCondition, FalloutFormKey?>? currentCell = resolveRunOnCell is null ? null :
            condition => resolveRunOnCell(_dialogueSubject, condition);
        _conditions = new(_records, _quests, _dialogueSubject, identity,
            _runtimeConditions, currentCell, _healthPercentage, _actorValue, _talkedToPlayer, _factions, _playerFemale, _actorRace,
            currentPackage: _currentPackage, vampireQuery: _vampireQuery, itemCount: _itemCount, referenceDistance: _referenceDistance,
            referenceInZone: _referenceInZone, sitting: _sitting, deadCount: _deadCount);
        _speakerName = FalloutDialogueTopic.Text(npc.ReadSubrecords().Single(field => field.Signature == "FULL").Data.Span);
    }
    private void BindConversationFacing()
    {
        _facingSpeaker = _speech.ResolveSpeaker(_speaker);
        var turnSpeed = FalloutGameSettingFloats.Read(_records, "fCharacterDefaultTurningSpeed");
        if (_facingSpeaker is RuntimeNativeNpc facingNpc)
            facingNpc.BeginConversationFacing(() => _player.Camera.GlobalPosition, turnSpeed);
        else if (_facingSpeaker is RuntimeNativeCreature facingCreature)
            facingCreature.BeginConversationFacing(() => _player.Camera.GlobalPosition, turnSpeed);
    }
}
