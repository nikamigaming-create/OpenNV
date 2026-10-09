using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private FalloutIndexedInterfaceSounds? _indexedInterfaceSounds;
    private NativeOwnedIndexedInterfaceSounds? _indexedInterfaceSoundsNative;
    internal object? SourceIndexedInterfaceSoundState => _indexedInterfaceSounds?.State;
    internal string? SourceIndexedInterfaceSoundFailure => _indexedInterfaceSounds?.Failure;
    internal string? SourceIndexedInterfaceSoundSaveBlocker => _indexedInterfaceSounds is null ?
        "source-indexed-interface-sound-owner-absent" : _indexedInterfaceSoundsNative is null ?
        "source-indexed-interface-native-publication-absent" : _indexedInterfaceSoundsNative.SaveBlocker ?? _indexedInterfaceSounds.SaveBlocker;
    internal FalloutIndexedInterfaceSounds RequireIndexedInterfaceSounds() => _indexedInterfaceSounds ??
        throw new NotSupportedException("Current campaign has no actual source-constructed indexed interface sound state.");

    // Prepare current state before challenge restore or any initial script can
    // produce a cue. Native publication follows after actual tree attachment.
    internal void PrepareSourceIndexedInterfaceSounds()
    {
        if (_indexedInterfaceSounds is not null) throw new InvalidOperationException("Indexed interface source state cannot be replaced.");
        var world = _scripts.References ?? throw new NotSupportedException("Indexed sounds have no actual campaign world.");
        var actual = world.CampaignIndexedInterfaceSounds;
        if (!ReferenceEquals(actual.Source.Records, _pluginStack))
            throw new InvalidDataException("Driver indexed sounds differ from the actual campaign record authority.");
        _indexedInterfaceSounds = actual;
    }
    internal void BindSourceIndexedInterfacePlayback()
    {
        if (_indexedInterfaceSoundsNative is not null || !IsInsideTree())
            throw new InvalidOperationException("Indexed interface playback has no new actual campaign tree publication.");
        _indexedInterfaceSoundsNative = NativeOwnedIndexedInterfaceSounds.Attach(this, RequireIndexedInterfaceSounds(), RetainDriverFailure);
    }
    internal FalloutIndexedInterfaceSoundSnapshot CaptureSourceIndexedInterfaceSounds() => RequireIndexedInterfaceSounds().Capture();

    // Stop/free the real shared voices while rest still observes their callback
    // receipts. RetireSourceIndexedInterfaceState follows rest/CHAL retirement.
    private void RetireSourceIndexedInterfacePlayback() => _indexedInterfaceSoundsNative?.RetireForSession();
    private void RetireSourceIndexedInterfaceState()
    {
        if (_indexedInterfaceSounds is null) return;
        // The world owns state retirement. Driver releases this exact alias
        // after native closure and rest caller retirement, never a copy.
        if (_indexedInterfaceSounds.SaveBlocker is not null)
            throw new InvalidOperationException("Driver indexed state release precedes actual voice/resource closure.");
        _indexedInterfaceSounds = null; _indexedInterfaceSoundsNative = null;
    }
}
