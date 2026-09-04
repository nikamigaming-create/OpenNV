using Godot;
using OpenNV.Runtime.Campaigns.Fallout2.Native;

namespace OpenNV.Runtime.Tools;

public sealed partial class Fo2NativeOwnedAudit : Node
{
    public override void _Ready()
    {
        try
        {
            var arguments = OS.GetCmdlineUserArgs();
            var option = Array.IndexOf(arguments, "--fo2-owned-profile");
            if (option < 0 || option + 1 >= arguments.Length)
                throw new InvalidOperationException("Fo2NativeOwnedAudit requires --fo2-owned-profile.");
            using var source = Fo2NativeOwnedSource.Load(arguments[option + 1]);
            var map3 = Fo2NativeMapReader.Read(source.Read("maps\\arcaves.map", out _));
            var temple = Fo2NativeMapReader.Read(source.Read("maps\\artemple.map", out _));
            var tileNames = System.Text.Encoding.ASCII.GetString(
                    source.Read("art\\tiles\\tiles.lst", out _))
                .Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
            var tileId = map3.Elevations[0]
                .Select(value => (int)(value & 0x0fffU)).First(id => id != 1);
            if (tileId < 0 || tileId >= tileNames.Length || string.IsNullOrWhiteSpace(tileNames[tileId]))
                throw new InvalidDataException("Map 3 floor tile does not resolve through tiles.lst.");
            var frame = Fo2NativeFrmReader.ReadFirstFrame(
                source.Read($"art\\tiles\\{tileNames[tileId].Trim()}", out _));
            var palette = source.Read("color.pal", out _);
            var image = Image.CreateFromData(
                frame.Width,
                frame.Height,
                false,
                Image.Format.Rgba8,
                ExpandPalette(frame.PaletteIndexes, palette));
            var texture = ImageTexture.CreateFromImage(image);
            if (texture.GetWidth() != frame.Width || texture.GetHeight() != frame.Height)
                throw new InvalidOperationException("Godot rejected the direct owned FRM texture.");
            var presentationParent = new Node3D { Name = "FO2_NATIVE_AUDIT_ROOT" };
            AddChild(presentationParent);
            var coverage = Fo2NativeMap3Presentation.Build(presentationParent, source);
            GD.Print(
                $"OPENNV_FO2_NATIVE_GODOT_PASS profile={source.ProfileId} map3={map3.MapIndex} " +
                $"temple={temple.MapIndex} tile={tileId} frm={frame.Width}x{frame.Height} " +
                $"floorPatches={coverage.FloorPatches} floorFrms={coverage.FloorFrmResources} " +
                $"objects={coverage.PresentedObjects + coverage.SemanticObjects} " +
                $"presentedObjects={coverage.PresentedObjects} objectFrms={coverage.ObjectFrmResources} " +
                $"semanticObjects={coverage.SemanticObjects} unclassifiedObjects={coverage.UnclassifiedObjects} " +
                $"inventoryObjects={coverage.NestedInventoryObjects} scriptSlots={coverage.ScriptSlots} " +
                $"liveScripts={coverage.LiveScripts} semanticBuckets={coverage.SemanticBuckets} " +
                "scripts=fail-closed interactions=fail-closed " +
                "preparedInputs=0 writes=0");
        }
        catch (Exception error)
        {
            GD.PrintErr($"OPENNV_FO2_NATIVE_GODOT_FAIL {error.Message}");
            GetTree().Quit(2);
            return;
        }
        GetTree().Quit();
    }

    private static byte[] ExpandPalette(byte[] indexes, byte[] palette)
    {
        const int paletteEntries = 256;
        const int paletteChannels = 3;
        const int outputChannels = 4;
        const int maximumSixBitChannel = 63;
        const int sixBitScale = 4;
        if (palette.Length < paletteEntries * paletteChannels)
            throw new InvalidDataException("Fallout COLOR.PAL is truncated.");
        var output = new byte[checked(indexes.Length * outputChannels)];
        for (var index = 0; index < indexes.Length; ++index)
        {
            var paletteIndex = indexes[index];
            var paletteOffset = paletteIndex * paletteChannels;
            var outputOffset = index * outputChannels;
            for (var channel = 0; channel < paletteChannels; ++channel)
            {
                var value = palette[paletteOffset + channel];
                output[outputOffset + channel] = value <= maximumSixBitChannel
                    ? checked((byte)(value * sixBitScale))
                    : (byte)0;
            }
            output[outputOffset + paletteChannels] = paletteIndex == 0 ? (byte)0 : byte.MaxValue;
        }
        return output;
    }
}
