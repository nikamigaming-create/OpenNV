using Godot;

namespace OpenNV.Runtime.Content;

internal static partial class NativeOwnedSoundPlayback
{
    internal static AudioStreamPlayer CreateSourceMenu(FalloutSoundRecord descriptor,
        FalloutPluginStack records, FalloutMenuCuePlaybackSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        // All unsupported selector/flags/path branches refuse before any native
        // stream/player allocation. This path constructs and draws no RNG.
        var selected = source.ExactFile(records, descriptor);
        var player = CreateTwoDimensional(selected, records);
        try
        {
            player.SetMeta("opennv_menu_sound_source", descriptor.FormKey.ToString());
            player.SetMeta("opennv_menu_sound_source_flags", (int)descriptor.Flags);
            player.SetMeta("opennv_menu_sound_variant", selected.LogicalPath);
            player.SetMeta("opennv_menu_sound_playback_source", source.Identity);
            player.SetMeta("opennv_menu_sound_selection", "original-exact-file-no-random-draw");
            return player;
        }
        catch (Exception original)
        {
            try { player.Free(); }
            catch (Exception cleanup)
            { throw new AggregateException("Exact menu cue retained its original and native retirement failures.", original, cleanup); }
            throw;
        }
    }
}
