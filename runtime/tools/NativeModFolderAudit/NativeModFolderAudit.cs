using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.Presentation.Ui;

/// <summary>Opt-in owned-folder and native launcher checks; never campaign or pixel parity acceptance.</summary>
public partial class NativeModFolderAudit : Control
{
    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            if (args.Length < 5) throw new ArgumentException("Expected mod id, game folder, mod folder, private output folder, textures|launcher, then additional folders.");
            var stackSelections = args[4] == "launcher-stack"
                ? JsonSerializer.Deserialize<FalloutModSelection[]>(File.ReadAllText(args[5])) ?? throw new InvalidDataException("Missing stack fixture.") : null;
            var selection = stackSelections?.Single(mod => mod.Id == args[0]) ?? new FalloutModSelection(args[0], args[2], args[5..]);
            var setup = selection.Resolve(args[1]);
            var output = Path.GetFullPath(args[3]);
            Directory.CreateDirectory(output);
            if (args[4] == "textures")
            {
                using var source = setup.OpenSource();
                var modFiles = new FalloutContentLayers(setup.ContentRoots.Skip(1));
                var textures = modFiles.ResourcePathsUnder("textures").Where(path => path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (textures.Length == 0) throw new InvalidDataException("The selected mod folders contain no textures.");
                var results = new List<object>();
                var partialCount = 0;
                foreach (var path in textures)
                {
                    var winner = modFiles.ResolveFile(path)!;
                    if (!source.TryRead(path, null, out var bytes, out var origin) || origin != winner)
                        throw new InvalidDataException($"Runtime texture winner differs: {path}");
                    if (FalloutDdsMipChain.ReadPartial(bytes) is { } partial)
                    {
                        using var uploaded = NativeDdsTexture.Load(bytes, origin) as NativePartialMipTexture ??
                            throw new InvalidDataException("A partial mip texture did not use its authored-level owner.");
                        if (!uploaded.ReadAuthoredBytes().AsSpan().SequenceEqual(bytes.AsSpan(128)))
                            throw new InvalidDataException($"Uploaded authored mip bytes differ: {origin}");
                        partialCount++;
                        results.Add(new
                        {
                            path,
                            source = origin,
                            sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                            width = partial.Levels[0].Width,
                            height = partial.Levels[0].Height,
                            mips = partial.Levels.Count - 1,
                            format = partial.Format,
                            gpuReadback = "exact authored bytes"
                        });
                    }
                    else
                    {
                        using var image = new Image();
                        if (image.LoadDdsFromBuffer(bytes) != Error.Ok || image.IsEmpty()) throw new InvalidDataException($"Native DDS decoding failed: {origin}");
                        NativeDdsTexture.PreserveAlpha(image);
                        results.Add(new
                        {
                            path,
                            source = origin,
                            sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                            width = image.GetWidth(),
                            height = image.GetHeight(),
                            mips = image.GetMipmapCount(),
                            format = image.GetFormat().ToString()
                        });
                    }
                    if (results.Count % 64 == 0) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                File.WriteAllText(Path.Combine(output, "textures.private.json"), JsonSerializer.Serialize(new
                { selection.Id, source.SaveCompatibilityId, textures = results, presentation = "unverified", parity = "unverified" }));
                GD.Print($"OPENNV_MOD_TEXTURE_DECODE_PASS textures={results.Count} sourceWinners=verified nativeDds=true partialChains={partialCount} presentation=unverified");
            }
            else if (args[4] is "launcher" or "launcher-stack")
            {
                var profiles = GodotLauncherProfileStore.Open(Path.Combine(output, "profiles.private.json"), Path.Combine(output, "saves"));
                profiles.Save("newvegas", args[1]);
                profiles.Save(selection.Id, selection.Root, args[1], selection.AdditionalRoots);
                if (stackSelections is not null)
                {
                    foreach (var mod in stackSelections) profiles.Save(mod.Id, mod.Root, args[1], mod.AdditionalRoots);
                    profiles.SetEnabledMods("newvegas", []);
                }
                var launcher = new NativeGodotLauncher();
                launcher.Configure(profiles);
                AddChild(launcher);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var cards = launcher.FindChildren("Campaign_*", "Button", true, false).OfType<Button>().ToArray();
                if (cards.Length != 4 + FalloutModCatalog.All.Count) throw new InvalidOperationException("The native launcher omitted a campaign or target mod.");
                if (stackSelections is not null)
                {
                    foreach (var mod in stackSelections)
                        ((CheckBox)launcher.FindChild("EnableMod_" + mod.Id, true, false)).ButtonPressed = true;
                    if (profiles.EnabledMods("newvegas").Count != stackSelections.Length)
                        throw new InvalidOperationException("Enabling a mod disabled another mod.");
                }
                cards.Single(card => card.Name == "Campaign_" + selection.Id).EmitSignal(BaseButton.SignalName.Pressed);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var list = launcher.FindChild("AdditionalModFolders", true, false) as ItemList ?? throw new InvalidOperationException("Mod folder list missing.");
                if (list.ItemCount != selection.AdditionalRoots.Count) throw new InvalidOperationException("Native launcher lost additional folders.");
                var search = (LineEdit)launcher.FindChild("LibrarySearch", true, false);
                search.Text = "__no_matching_mod__";
                search.EmitSignal(LineEdit.SignalName.TextChanged, search.Text);
                if (cards.Any(card => card.IsVisibleInTree())) throw new InvalidOperationException("The library search did not filter rows.");
                search.Text = string.Empty;
                search.EmitSignal(LineEdit.SignalName.TextChanged, search.Text);
                if (stackSelections is not null && profiles.ModStack("newvegas")!.Resolve(args[1]).Mods.Count != stackSelections.Length)
                    throw new InvalidOperationException("Inspecting or searching reset the enabled mod stack.");
                if (DisplayServer.GetName() != "headless")
                {
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using var image = GetViewport().GetTexture().GetImage();
                    if (image.SavePng(Path.Combine(output, "launcher.png")) != Error.Ok) throw new IOException("Launcher screenshot failed.");
                }
                if (stackSelections is not null)
                {
                    GetViewport().GuiEmbedSubwindows = true;
                    ((Button)launcher.FindChild("OpenModLoadOrder", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    var order = launcher.FindChild("EnabledModOrder", true, false) as ItemList ?? throw new InvalidOperationException("Automatic mod order is missing.");
                    if (order.ItemCount != stackSelections.Length) throw new InvalidOperationException("The order view omitted an enabled mod.");
                    if (DisplayServer.GetName() != "headless")
                    {
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        using var orderImage = GetViewport().GetTexture().GetImage();
                        if (orderImage.SavePng(Path.Combine(output, "automatic-order.png")) != Error.Ok) throw new IOException("Order screenshot failed.");
                    }
                    ((Window)launcher.FindChild("ModLoadOrder", true, false)).EmitSignal(Window.SignalName.CloseRequested);
                }
                GD.Print($"OPENNV_MOD_LAUNCHER_PASS cards={cards.Length} selected={selection.Id} additionalFolders={list.ItemCount} enabledMods={profiles.EnabledMods("newvegas").Count} automaticOrder={profiles.AutomaticModOrder("newvegas")} gameplay=unverified");
                launcher.QueueFree();
            }
            else throw new ArgumentException("Select textures or launcher.");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError($"OPENNV_MOD_FOLDER_AUDIT_FAIL {error}");
            GetTree().Quit(1);
        }
    }
}
