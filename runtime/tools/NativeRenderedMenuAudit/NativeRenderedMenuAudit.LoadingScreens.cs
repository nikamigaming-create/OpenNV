using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Cells;

public partial class NativeRenderedMenuAudit
{
    private async Task LoadingScreens(string baseRoot, string mod, string root, string markerId, string[] dependencies)
    {
        if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("Loading pixels need an actual renderer.");
        var setup = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(baseRoot);
        RuntimeLiveContentSource.Configure(baseRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins, setup.Settings);
        SubViewport? view = null;
        var paused = GetTree().Paused;
        try
        {
            using var records = FalloutPluginStack.Load(RuntimeLiveContentSource.Current!.PluginSources);
            using var world = new FalloutReferenceWorld(records);
            var marker = FalloutDialogueTopic.Find(records, "REFR", markerId);
            var cell = FalloutCellSceneReader.ReadDefinition(records, world.Placement(marker.FormKey).Cell);
            var eligible = FalloutLoadingScreenCatalog.InGame(records, cell, true);
            if (eligible.Count == 0 || eligible.Any(screen => screen.Type is null || screen.Description.Length == 0))
                throw new InvalidDataException("Selected owned loading fixture requires source images and tips.");
            view = new SubViewport
            {
                Size = new(1280, 720),
                Disable3D = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
                ProcessMode = ProcessModeEnum.Always
            };
            AddChild(view);
            var loading = new NativeOwnedLoadingScreens(records, cell, true);
            view.AddChild(loading); loading.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            GetTree().Paused = true;
            for (var frame = 0; frame < 3; ++frame) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = view.GetTexture().GetImage();
            if (image.IsEmpty()) throw new InvalidDataException("Owned loading screen drew no pixels.");
            var background = loading.GetChildren().OfType<NativeGamebryoLoadingBackground>().Single();
            var slide = background.GetChildren().OfType<TextureRect>().Single(tile => tile.Name == "loading_tile_slide_01");
            var selected = eligible.Single(screen => screen.Identity.ToString() == slide.GetMeta("opennv_source_record").AsString());
            var tip = FalloutLoadingScreenType.Tip(records.GetEffective(selected.Type!.Value))!;
            var pixels = image.GetData();
            // Hide the independent tip component, then compare actual native pixels
            // in its declared source bounds. Do not save fixture frames.
            foreach (var control in loading.GetChildren().OfType<Control>().Where(control => control != background)) control.Hide();
            for (var frame = 0; frame < 2; ++frame) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var bare = view.GetTexture().GetImage();
            var without = bare.GetData(); var differences = 0; var colorCount = 0;
            var scale = view.Size.Y / 960f;
            for (var y = (int)(tip.Y * scale); y < Math.Min(view.Size.Y, (tip.Y + tip.Height) * scale); ++y)
                for (var x = (int)(tip.X * scale); x < Math.Min(view.Size.X, (tip.X + tip.Width) * scale); ++x)
                {
                    var offset = (y * view.Size.X + x) * 4;
                    if (!pixels.AsSpan(offset, 4).SequenceEqual(without.AsSpan(offset, 4))) differences++;
                    if (pixels[offset] != pixels[offset + 1] || pixels[offset + 1] != pixels[offset + 2]) colorCount++;
                }
            if (differences == 0 || colorCount == 0 || slide.Texture.GetWidth() <= 0)
                throw new InvalidOperationException("Winning loading image and source tip did not reach native pixels.");
            GD.Print($"OPENNV_NATIVE_LOADING_PIXELS_PASS cell={cell.FormKey} selected={selected.Identity} type={selected.Type} " +
                $"changedTipPixels={differences} sourceImage=true paused=true sourceFont=true recording=false retainedFrames=0 fixture=true " +
                "ancillary-ui=unbound retailParity=unverified");
        }
        finally { GetTree().Paused = paused; view?.Free(); RuntimeLiveContentSource.Clear(); }
    }
}
