using Godot;
using OpenNV.Runtime.Campaigns.Classic.Native;
using OpenNV.Runtime.Campaigns.Fallout2.Native;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Tools;

public sealed partial class ClassicArtAudit : Node
{
    public override void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            if (args.Length < 3) throw new ArgumentException("Provide owned install, private output directory and source FRM paths.");
            var installation = NativeGameInstallation.Detect(args[0]);
            Func<string, byte[]> read;
            IDisposable? disposable = null;
            if (installation.Game == NativeGame.Fallout1)
            {
                var source = Fallout1OwnedContentSource.LoadInstall(args[0]);
                read = path => source.Read(path).Bytes;
            }
            else if (installation.Game == NativeGame.Fallout2)
            {
                var source = Fo2NativeOwnedSource.LoadInstall(args[0]);
                disposable = source;
                read = path => source.Read(path, out _);
            }
            else throw new ArgumentException("Art inspection requires an owned Fallout 1 or Fallout 2 installation.");
            using var sourceLifetime = disposable;
            var art = new ClassicArtCache(read);
            Directory.CreateDirectory(args[1]);
            foreach (var path in args.Skip(2))
            {
                using var image = art.Frame(path).Texture.GetImage();
                var target = Path.Combine(args[1], Path.GetFileNameWithoutExtension(path.Replace('\\', '/')) + ".png");
                if (image.SavePng(target) != Error.Ok) throw new IOException("Cannot write private art inspection.");
                GD.Print($"OPENNV_CLASSIC_ART source={path} width={image.GetWidth()} height={image.GetHeight()} output={target}");
            }
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
