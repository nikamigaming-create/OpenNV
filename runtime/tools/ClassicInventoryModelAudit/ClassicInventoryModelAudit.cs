using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;
using OpenNV.Runtime.Campaigns.Classic.Native;
using OpenNV.Runtime.Campaigns.Fallout2.Native;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

/// <summary>Source-PRO-to-native-mesh admission. This does not create campaign items or certify likeness.</summary>
public partial class ClassicInventoryModelAudit : Node
{
    private int _exitCode = 1, _frames;
    private string? _preview;
    private bool _capturing, _finishing;
    public override void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            if (args.Length is < 3 or > 5) throw new ArgumentException("Expected classic installation, campaign, owned FNV Data directory, optional diagnostic PNG path and optional comma-separated item PIDs.");
            _preview = args.Length >= 4 ? args[3] : null;
            using IFalloutClassicOwnedSource source = args[1] == "fallout-1" ? Fallout1OwnedContentSource.LoadInstall(args[0]) : Fo2NativeOwnedSource.LoadInstall(args[0]);
            using var catalog = new ClassicMapCatalog(source, args[1]);
            using var assets = new ClassicOwnedWorldAssets(args[2]);
            var art = new ClassicArtCache(path => source.Read(path, out _));
            var definitions = new ClassicItemDefinitions(catalog);
            var bindings = assets.Recipe.Props ?? throw new InvalidDataException("Item bindings are absent.");
            var pids = bindings.Where(row => row.Pids is not null &&
                (row.Campaigns is null || row.Campaigns.Contains(args[1], StringComparer.Ordinal))).SelectMany(row => row.Pids!).Distinct().Order().ToArray();
            var failures = new List<string>(); var models = new HashSet<string>();
            foreach (var pid in pids)
            {
                Node3D? model = null;
                try
                {
                    var item = definitions.Read(pid);
                    var prototype = Fallout1NativeObjectGraphReader.ResolvePrototype(catalog, pid);
                    var path = Fallout1NativePrototypeReader.ResolveArt(catalog, prototype.Fid!.Value);
                    var frame = art.Frame(path).Frame;
                    var candidates = bindings.Where(row => row.Pids?.Contains(pid) == true &&
                        (row.Campaigns is null || row.Campaigns.Contains(args[1], StringComparer.Ordinal)) &&
                        Regex.IsMatch(Path.GetFileName(path.Replace('\\', '/')), row.ArtPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray();
                    if (candidates.Length != 1) throw new InvalidDataException("Item has absent or ambiguous source-art binding.");
                    model = assets.Prop(path, frame, 32, sourcePid: pid, campaign: args[1]) ?? throw new InvalidDataException("Item model was not admitted.");
                    AddChild(model);
                    var clone = assets.Prop(path, frame, 32, sourcePid: pid, campaign: args[1]) ?? throw new InvalidDataException("Second item model was not admitted.");
                    try
                    {
                        AddChild(clone);
                        var firstSkin = model.FindChildren("*", "", true, false).OfType<RuntimeNifEmbeddedSkin>().FirstOrDefault();
                        if (firstSkin is not null)
                        {
                            var secondSkin = clone.FindChildren("*", "", true, false).OfType<RuntimeNifEmbeddedSkin>().First();
                            var firstPose = firstSkin.GetBoneGlobalPose(0); var secondPose = secondSkin.GetBoneGlobalPose(0);
                            if (!firstPose.IsEqualApprox(secondPose)) throw new InvalidDataException("Repeated embedded skins have different initial poses.");
                            secondSkin.GetNode<Node3D>(secondSkin.SourceBones[0]).Position += new Vector3(.125f, .25f, 0);
                            secondSkin.Publish();
                            if (secondPose.IsEqualApprox(secondSkin.GetBoneGlobalPose(0)) || !firstPose.IsEqualApprox(firstSkin.GetBoneGlobalPose(0)))
                                throw new InvalidDataException("Embedded skin instances do not retain independent live bones.");
                        }
                    }
                    finally { clone.Free(); }
                    var bounds = ClassicSceneryPlacement.Bounds(model);
                    if (!bounds.Size.IsFinite() || bounds.Size.Length() <= 0) throw new InvalidDataException("Item model has invalid bounds.");
                    models.Add(model.GetMeta("owned_model").AsString());
                    GD.Print(JsonSerializer.Serialize(new { campaign = args[1], pid, item.Name, bounds = bounds.Size.ToString(), model = model.GetMeta("owned_model").AsString() }));
                }
                catch (Exception error) { failures.Add($"PID {pid}: {error.Message}"); }
                finally { model?.Free(); }
            }
            GD.Print(JsonSerializer.Serialize(new
            {
                campaign = args[1],
                itemBindings = pids.Length,
                models = models.Count,
                failures,
                scope = "native mesh and source binding admission only; no gameplay, animation or likeness acceptance"
            }));
            _exitCode = failures.Count == 0 ? 0 : 1;
            if (_preview is not null)
            {
                GetWindow().Size = new Vector2I(1600, 960);
                var canvas = new Control { Size = new(1600, 960) }; AddChild(canvas);
                canvas.AddChild(new ColorRect { Size = canvas.Size, Color = new Color("171b19") });
                var title = new Label { Position = new(24, 14), Text = "Native inventory component — original icons and owned 3D candidates (not campaign acquisition)" };
                title.AddThemeFontSizeOverride("font_size", 23); canvas.AddChild(title);
                var views = new ClassicInventoryModels(); views.Configure(new(art.Read("font1.aaf"), art.Read("color.pal"), 992)); canvas.AddChild(views);
                var slots = new List<ClassicInventoryModelSlot>();
                int[] selected = args.Length == 5 ? args[4].Split(',').Select(int.Parse).ToArray() : [1, 2, 17, 85, 12, 118, 48, 47];
                if (selected.Length is < 1 or > 8) throw new ArgumentException("A diagnostic page contains one to eight source items.");
                for (var index = 0; index < selected.Length; index++)
                {
                    var pid = selected[index]; var item = definitions.Read(pid);
                    var proto = Fallout1NativeObjectGraphReader.ResolvePrototype(catalog, pid);
                    var path = Fallout1NativePrototypeReader.ResolveArt(catalog, proto.Fid!.Value); var frame = art.Frame(path).Frame;
                    var origin = new Vector2((index % 4) * 400, 72 + (index / 4) * 442);
                    canvas.AddChild(new Label { Position = origin + new Vector2(12, 0), Size = new(376, 32), HorizontalAlignment = HorizontalAlignment.Center, Text = item.Name });
                    canvas.AddChild(new TextureRect
                    {
                        Position = origin + new Vector2(60, 42),
                        Size = new(280, 116),
                        Texture = art.Frame(item.Icon!).Texture,
                        TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
                    });
                    slots.Add(new($"preview:{pid}", item.Name, new(origin + new Vector2(24, 180), new(352, 230)), () => assets.Prop(path, frame, 32, sourcePid: pid, campaign: args[1])));
                }
                views.Display(slots, true, 1);
            }
        }
        catch (Exception error) { GD.PushError(error.ToString()); _exitCode = 1; }
    }
    public override async void _Process(double delta)
    {
        if (++_frames < 3) return;
        if (_finishing) return;
        if (_preview is not null)
        {
            if (_capturing) return;
            _capturing = true;
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            if (image.SavePng(_preview) != Error.Ok) _exitCode = 1;
            foreach (var child in GetChildren()) child.QueueFree();
            _preview = null; _frames = 0; return;
        }
        // Release the component run's transient Godot resource wrappers after
        // the owned libraries/prototypes have left their using scopes.
        _finishing = true;
        GC.Collect(); GC.WaitForPendingFinalizers();
        // Shape disposal reaches the physics server asynchronously.
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetTree().Quit(_exitCode);
    }
}
