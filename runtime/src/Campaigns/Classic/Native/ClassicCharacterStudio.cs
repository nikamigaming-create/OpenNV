using Godot;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.CharacterCreation;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

/// <summary>Classic appearance editing through the shared live NIF/FaceGen owner.</summary>
internal sealed partial class ClassicCharacterStudio : Node
{
    private FalloutPluginStack? _records;
    private RuntimeNativeRaceSexEntry? _entry;
    internal event Action? Closed;
    internal event Action<ClassicAppearanceDraft>? AppearanceAccepted;

    internal void Configure(string campaign, string classicProfile, string appearanceRoot, string savePath,
        ClassicAppearanceDraft? preferredDraft = null, bool? initialFemale = null)
    {
        if (campaign is not ("fallout-1" or "fallout-2")) throw new ArgumentException("Classic character campaign is invalid.");
        RuntimeLiveContentSource.Configure(appearanceRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
        var content = RuntimeLiveContentSource.Current!;
        _records = FalloutPluginStack.Load(content.PluginSources);
        var contract = FalloutNativeRaceSexResolver.Resolve(_records);
        var draftPath = Path.GetFullPath(savePath) + ".appearance.json";
        var draft = preferredDraft ?? ClassicAppearanceDraft.Read(draftPath, campaign, classicProfile, content.StackId);
        if (draft is not null && (draft.Campaign != campaign || draft.ClassicProfileId != classicProfile || draft.AppearanceStackId != content.StackId))
            throw new InvalidDataException("Selected character appearance belongs to another owned source.");
        var backdrop = new CanvasLayer { Layer = 110, ProcessMode = ProcessModeEnum.Always }; AddChild(backdrop);
        var shade = new ColorRect { Color = new Color(0.025f, 0.03f, 0.028f, 0.96f) }; backdrop.AddChild(shade);
        shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _entry = new RuntimeNativeRaceSexEntry(); AddChild(_entry);
        _entry.Configure(contract, draft?.Character ?? contract.ForSex(initialFemale ?? contract.InitialFemale), _records);
        if (campaign == "fallout-1") _entry.EnableEnvision(ClassicPortraitMode.Illustrated);
        _entry.AddAppearanceBackButton(Close);
        // Fallout 2 deliberately keeps its sculpted, naturally shaded 3D face.
        _entry.Accepted += character =>
        {
            var accepted = new ClassicAppearanceDraft(ClassicAppearanceDraft.CurrentSchema, campaign, classicProfile,
                content.StackId, character);
            accepted.Save(draftPath);
            AppearanceAccepted?.Invoke(accepted);
            GD.Print($"OPENNV_CLASSIC_APPEARANCE_SAVED campaign={campaign} state=complete-facegen-and-source-identity campaignProgression=false");
            Close();
        };
        var chrome = new CanvasLayer { Layer = 130, ProcessMode = ProcessModeEnum.Always }; AddChild(chrome);
        var column = new VBoxContainer { Position = new(20, 16) }; chrome.AddChild(column);
        column.AddChild(new Label { Text = $"FALLOUT {(campaign == "fallout-1" ? "1" : "2")} · CUSTOM APPEARANCE", MouseFilter = Control.MouseFilterEnum.Ignore });
        _entry.Failed += error => column.AddChild(new Label
        {
            Text = error.Message,
            Modulate = new Color("ffb58e"),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new(330, 0)
        });
        GD.Print($"OPENNV_CLASSIC_APPEARANCE_OPEN campaign={campaign} restored={draft is not null} portrait={(campaign == "fallout-1" ? "illustrated" : "live3d")} likeness=unverified campaignEntry=unbound");
    }

    private void Close()
    {
        _entry?.ReleasePause(); Closed?.Invoke(); QueueFree();
    }

    public override void _ExitTree() { _entry?.ReleasePause(); _records?.Dispose(); }
}
