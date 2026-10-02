using System.Globalization;
using Godot;

namespace OpenNV.Runtime.Content;

internal static class NativeOwnedSoundVoice
{
    internal static FalloutFormKey? Reference(FalloutPluginStack records, Node emitter)
    {
        for (Node? node = emitter; node is not null; node = node.GetParent())
        {
            if (!node.HasMeta("opennv_reference_form_key")) continue;
            var text = node.GetMeta("opennv_reference_form_key").AsString();
            var colon = text.LastIndexOf(':');
            if (colon <= 0 || !uint.TryParse(text.AsSpan(colon + 1), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out var id))
                throw new InvalidDataException("Owned sound emitter has malformed reference identity.");
            var source = records.GetEffective(new(text[..colon], id));
            if (source.Signature is not ("ACHR" or "ACRE" or "REFR"))
                throw new InvalidDataException("Owned sound emitter identity is not an object reference.");
            return source.FormKey;
        }
        return null;
    }

    // A prepared player may be discarded before entering the tree. Register
    // only while its native presentation owner is resident; retirement is
    // idempotent across Finished, tree exit and explicit StopSound.
    internal static void Bind(FalloutPluginStack records, Node player, FalloutFormKey sound,
        Func<FalloutFormKey?> reference, string owner, Func<bool> playing, Action stop)
    {
        IDisposable? registration = null;
        void Retire() { registration?.Dispose(); registration = null; }
        void Enter()
        {
            if (registration is not null) throw new InvalidOperationException("Native SOUN voice registered twice.");
            registration = records.SoundVoices.Register(sound, reference(), owner, playing, () =>
            {
                stop(); Retire(); player.QueueFree();
            });
        }
        player.TreeEntered += Enter;
        player.TreeExiting += Retire;
        if (player is AudioStreamPlayer spatiallyFlat) spatiallyFlat.Finished += Retire;
        else if (player is AudioStreamPlayer3D spatial) spatial.Finished += Retire;
        else throw new ArgumentException("Native SOUN registration requires an audio player.", nameof(player));
        if (player.IsInsideTree()) Enter();
    }
}
