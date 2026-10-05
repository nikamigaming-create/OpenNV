using System.Globalization;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private FalloutNativeFinishedSpeechSnapshot? _restoreFinishedSpeech;
    private FalloutFinishedSpeechStageScope? _restoreFinishedSpeechStage;
    private FalloutFinishedSpeechStageScope? CaptureFinishedSpeechStage()
    {
        if (_speech?.CanCaptureFinishedFailure != true || _speechStage is null) return null;
        var separator = _speechStage.LastIndexOf(':');
        if (separator <= 0) throw new InvalidDataException("Finished speech has an invalid original source-stage scope.");
        var quest = FalloutDialogueTopic.Find(_pluginStack, "QUST", _speechStage[..separator]);
        return new(quest.FormKey, short.Parse(_speechStage[(separator + 1)..], CultureInfo.InvariantCulture));
    }

    private void ConfigureDetectionAndFinishedSpeech()
    {
        var world = _scripts.References ?? throw new InvalidOperationException("Detection has no shared world.");
        world.Detection.Bind(reference =>
        {
            var position = _player.GlobalPosition / _player.UnitsToMeters;
            return world.DetectionPlacement(reference, new(_activeCell, [position.X, -position.Z, position.Y], [0, 0, 0]), _player.UnitsToMeters);
        }, actor =>
        {
            // The source player class owns a HighProcess. Other actor process
            // tiers remain unknown until a separate shared process owner binds
            // them; presentation residency cannot prove those source classes.
            if (actor == _pluginStack.RuntimeFormKey(0x14) && _player.IsInsideTree()) return FalloutDetectionProcessLevel.High;
            return null;
        });
        _speech!.CanResumeSourceCompletion = receipt => _resultScripts!.CanResumeSpeechCompletion(receipt);
        _speech.ResumeSourceCompletion = receipt =>
        {
            var result = _resultScripts!.ResumeSpeechCompletion(receipt);
            if (result.Error is { } error) throw new NotSupportedException($"Source SayToDone actor {receipt.Speaker} failed: {error}");
        };
        _speech.RestoreFinishedState(_restoreFinishedSpeech);
        if (_restoreFinishedSpeechStage is { } stage)
        {
            var fields = _pluginStack.GetEffective(stage.Quest).ReadSubrecords().Where(field => field.Signature == "EDID").ToArray();
            if (fields.Length != 1) throw new InvalidDataException("Finished speech source quest has no unique editor identity.");
            _speechStage = FalloutDialogueTopic.Text(fields[0].Data.Span) + ":" + stage.Stage.ToString(CultureInfo.InvariantCulture);
        }
        _restoreFinishedSpeech = null;
        _restoreFinishedSpeechStage = null;
    }
}
