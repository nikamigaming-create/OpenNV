using System.Diagnostics;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Tools;

public partial class NativeActiveRadioAudit
{
    private readonly Dictionary<ulong, string> _nativeAudioIds = [];
    private readonly Dictionary<ulong, AudioStreamPlayback> _nativePlaybackBindings = [];

    private AudioStreamPlayback TrackNativeRadioAudio(RuntimeNativeSpeech speech, FalloutFormKey station)
    {
        var player = Player(speech, station);
        var stream = Pcm(speech, station).Stream;
        _nativeAudioIds.TryAdd(player.GetInstanceId(), "player");
        _nativeAudioIds.TryAdd(stream.GetInstanceId(), "stream");
        var playback = player.GetStreamPlayback() ?? throw new InvalidDataException("Native source radio has no actual playback.");
        var id = playback.GetInstanceId();
        _nativeAudioIds.TryAdd(id, "playback");
        _nativePlaybackBindings.TryAdd(id, playback);
        return playback;
    }

    private async Task WaitForNativeRadioProgress(RuntimeNativeSpeech speech, FalloutFormKey station)
    {
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            var clock = Pcm(speech, station).Capture();
            Require(speech.Error is null && clock.Playing && clock.Position < clock.Frames,
                "Native mixer finished or failed before its selected finite radio clock was observed.");
            if (clock.Position > 0) return;
            Require(elapsed.Elapsed.TotalSeconds < 2,
                $"Native radio mixer stalled: driver={AudioServer.GetDriverName()} lastMixSeconds={AudioServer.GetTimeSinceLastMix():R} wallSeconds={elapsed.Elapsed.TotalSeconds:R}");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private async Task RetireNativeRadioAudio()
    {
        using var query = new Expression();
        Require(query.Parse("is_instance_id_valid(id)", new[] { "id" }) == Error.Ok && !NativeRadioInstanceAlive(query, 0),
            "Native radio retirement query was not admitted.");
        try
        {
            AudioServer.Lock();
            try
            {
                foreach (var (id, playback) in _nativePlaybackBindings)
                {
                    var references = playback.GetReferenceCount();
                    for (var index = 0; index < 8; index++)
                        Require(NativeRadioInstanceAlive(query, id), "Native radio playback disappeared before original binding release.");
                    Require(playback.GetReferenceCount() == references, "Native retirement query changed playback reference ownership.");
                }
            }
            finally { AudioServer.Unlock(); }
        }
        finally
        {
            // Release only this fixture's original bindings after their actual
            // players retire. A validity query never creates another binding.
            foreach (var playback in _nativePlaybackBindings.Values) playback.Dispose();
            _nativePlaybackBindings.Clear();
        }
        var elapsed = Stopwatch.StartNew();
        string[] retained;
        while (true)
        {
            retained = _nativeAudioIds.Where(value => NativeRadioInstanceAlive(query, value.Key))
                .Select(value => $"{value.Value}={value.Key}").ToArray();
            if (retained.Length == 0 || elapsed.Elapsed.TotalSeconds >= 2) break;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        Require(retained.Length == 0, "Native radio audio remained live after bounded source retirement: " +
            string.Join(", ", retained) + $"; driver={AudioServer.GetDriverName()} lastMixSeconds={AudioServer.GetTimeSinceLastMix():R} wallSeconds={elapsed.Elapsed.TotalSeconds:R}");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GD.Print($"OPENNV_NATIVE_RADIO_RETIREMENT_PASS nativeOwners={_nativeAudioIds.Count} nonbindingValidity=true nativeIdsGone=true boundedWallSeconds=2");
    }

    private static bool NativeRadioInstanceAlive(Expression query, ulong id)
    {
        using var inputs = new Godot.Collections.Array { unchecked((long)id) };
        using var result = query.Execute(inputs, showError: false);
        Require(!query.HasExecuteFailed() && result.VariantType == Variant.Type.Bool,
            "Native radio retirement query failed: " + query.GetErrorText());
        return result.AsBool();
    }
}
