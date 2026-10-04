using System.Buffers.Binary;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeSpeech
{
    private readonly Dictionary<FalloutFormKey, FalloutRadioConversation> _radioConversations = [];
    private Dictionary<FalloutFormKey, List<FalloutFormKey>>? _radioReceivers;

    internal void StartRadioConversation(FalloutFormKey station, FalloutFormKey? topic)
    {
        if (Error is not null) throw new InvalidOperationException(Error);
        try
        {
            if (!_radioConversations.TryGetValue(station, out var conversation))
                _radioConversations.Add(station, conversation = new(_stack,
                    _references ?? throw new InvalidOperationException("Radio has no reference world."),
                    _quests ?? throw new InvalidOperationException("Radio has no quest state."), EvaluateRadioCondition,
                    _questStage, _said, _dialogueRandom));
            if (IsDialogueBusy(station))
                throw new NotSupportedException("Radio interruption requires its retained result/cursor owner.");
            conversation.Start(station, topic);
            if (conversation.Info is { } info)
                StartCore(new(station.ToString(), "", conversation.Topic!.Value.ToString()), _stack.GetEffective(station),
                    conversation.Topic.Value, null, selectedInfo: info, radio: conversation);
            GD.Print($"OPENNV_NATIVE_RADIO_REQUEST station={station} topic={conversation.Topic} active={conversation.Active} continuous=false owner=source-radio-conversation");
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or FileNotFoundException or InvalidOperationException)
        { Fail(error); throw; }
    }

    private float EvaluateRadioCondition(FalloutFormKey station, FalloutCondition condition) => new FalloutDialogueConditions(
        _stack, _quests!, station, SpeakerIdentity(station), _conditionContext, actorValue: _actorValue,
        playerFemale: _playerFemale, actorRace: _actorRace, currentPackage: _currentPackage, vampireQuery: _vampireQuery,
        itemCount: _itemCount, referenceDistance: _referenceDistance, referenceInZone: _referenceInZone,
        sitting: _sitting, deadCount: _deadCount).Evaluate(condition);

    private void BindRadioSpeaker(Voice voice, FalloutPluginRecord station, Node3D? presentation, FalloutRadioConversation radio)
    {
        EndListenerAnimation(voice);
        voice.Speaker = null; voice.Creature = null; voice.Presentation = presentation;
        voice.TalkingActivator = true;
        voice.DialogueSubject = station.FormKey;
        voice.Identity = radio.VoiceIdentity();
        voice.Radio = radio;
        UpdateRadioListener(voice);
    }

    private IReadOnlyList<FalloutFormKey> PhysicalRadioListeners(FalloutFormKey stationBase)
    {
        if (_radioReceivers is null)
        {
            _radioReceivers = [];
            var receivers = new Dictionary<FalloutFormKey, FalloutFormKey>();
            foreach (var record in _stack.EffectiveRecords("ACTI"))
            {
                var fields = record.ReadSubrecords().Where(field => field.Signature == "RNAM").ToArray();
                if (fields.Length == 0) continue;
                if (fields.Length != 1 || fields[0].Data.Length != 4)
                    throw new InvalidDataException($"Radio receiver {record.FormKey} has invalid station identity.");
                var source = record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(fields[0].Data.Span));
                if (source is { } form) receivers.Add(record.FormKey, form);
            }
            foreach (var reference in _stack.EffectiveRecords("REFR"))
                if (receivers.TryGetValue(FalloutDialogueTopic.RequiredForm(reference, "NAME"), out var source))
                {
                    if (!_radioReceivers.TryGetValue(source, out var list)) _radioReceivers.Add(source, list = []);
                    list.Add(reference.FormKey);
                }
        }
        return (_radioReceivers.GetValueOrDefault(stationBase) ?? []).Where(reference =>
            _presentation?.Invoke(reference) is { } node && node.IsVisibleInTree() && _references!.IsEnabled(reference)).ToArray();
    }

    private void UpdateRadioListener(Voice voice)
    {
        var station = voice.Radio!.Station!;
        var audible = PhysicalRadioListeners(station.Base).Count != 0 || _references!.PipBoyRadio.CurrentStation == station.Reference;
        // The transmitter clock continues without a receiver. Actual resident
        // ACTI radios and the player receiver determine whether it is audible.
        // Spatial attenuation and receiver toggle state remain explicit lanes.
        voice.Player.VolumeDb = audible ? 0 : -80;
        _unbound.Add("radio-physical-attenuation-and-receiver-toggle-state");
    }

    private object RadioState => _radioConversations.Select(pair => new
    {
        station = pair.Key.ToString(),
        topic = pair.Value.Topic?.ToString(),
        info = pair.Value.Info?.Record.FormKey.ToString(),
        pair.Value.Active,
        pair.Value.CompletedLines,
        physicalListeners = pair.Value.Station is { } source ?
            PhysicalRadioListeners(source.Base).Select(form => form.ToString()).ToArray() : [],
        continuousGeneration = "unbound",
        interruption = "unbound",
        activeSave = "unbound"
    }).ToArray();
}
