using System.Globalization;
using Godot;
using OpenNV.Runtime.Content.Definitions;

namespace OpenNV.Runtime.Content;

internal static class NativeOwnedSoundVoice
{
    internal static FalloutFormKey? Reference(FalloutPluginStack records, Node emitter) =>
        Identity(records, emitter)?.RequireSource();

    internal static FalloutSpatialReferenceId? Identity(FalloutPluginStack records, Node emitter)
    {
        for (Node? node = emitter; node is not null; node = node.GetParent())
        {
            if (node.HasMeta("opennv_runtime_reference"))
            {
                var identity = FalloutSpatialReferenceId.Runtime(node.GetMeta("opennv_runtime_reference").AsUInt32());
                identity.Validate();
                return identity;
            }
            // The engine player has no placed ACHR record, but is still the
            // authoritative source reference for its weapons and child effects.
            if (node is OpenNV.Runtime.World.Cells.RuntimeNativePlayer)
                return records.RuntimeFormKey(0x14);
            if (!node.HasMeta("opennv_reference_form_key")) continue;
            var text = node.GetMeta("opennv_reference_form_key").AsString();
            var colon = text.LastIndexOf(':');
            if (colon <= 0 || !uint.TryParse(text.AsSpan(colon + 1), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out var id))
                throw new InvalidDataException("Owned sound emitter has malformed reference identity.");
            var reference = new FalloutFormKey(text[..colon], id);
            // Child effects can carry the engine player's identity before the
            // ancestor walk reaches its body. It has no placed ACHR record.
            if (reference == records.RuntimeFormKey(FalloutEngineForms.PlayerReference)) return reference;
            var source = records.GetEffective(reference);
            if (!FalloutCellSceneReader.IsReferenceType(source.Signature))
                throw new InvalidDataException("Owned sound emitter identity is not an object reference.");
            return source.FormKey;
        }
        return null;
    }

    // A prepared player may be discarded before entering the tree. Register
    // only while its native presentation owner is resident; retirement is
    // idempotent across Finished, tree exit and explicit StopSound.
    internal static void Bind(FalloutPluginStack records, Node player, FalloutFormKey sound,
        Func<FalloutFormKey?> reference, string owner, Func<bool> playing, Action stop,
        Func<bool>? checkpointReady = null)
    {
        IDisposable? registration = null;
        void Retire() { registration?.Dispose(); registration = null; }
        void Enter()
        {
            if (registration is not null) throw new InvalidOperationException("Native SOUN voice registered twice.");
            registration = records.SoundVoices.Register(sound, reference(), owner, playing, () =>
            {
                stop(); Retire(); player.QueueFree();
            }, checkpointReady: checkpointReady);
        }
        player.TreeEntered += Enter;
        player.TreeExiting += Retire;
        if (player is AudioStreamPlayer spatiallyFlat) spatiallyFlat.Finished += Retire;
        else if (player is AudioStreamPlayer3D spatial) spatial.Finished += Retire;
        else throw new ArgumentException("Native SOUN registration requires an audio player.", nameof(player));
        if (player.IsInsideTree()) Enter();
    }
}
