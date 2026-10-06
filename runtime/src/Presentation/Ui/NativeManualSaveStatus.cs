using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeManualSaveStatus(Action cancel, Action dismiss) : Control
{
    private Label _status = null!;
    private Button _cancel = null!;
    private ColorRect _background = null!;
    private RuntimeManualSaveReceipt? _receipt;

    public override void _Ready()
    {
        Name = "ManualSaveStatus"; ProcessMode = ProcessModeEnum.Always;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        if (RuntimeLiveContentSource.Current is { } source)
        {
            var font = NativeBitmapFontAsset.Read(FalloutInstallationSettings.Read(source), 3);
            Theme = new Theme { DefaultFont = font.CreateFontFile(), DefaultFontSize = 26 };
        }
        _background = new ColorRect { Color = new(.035f, .025f, .015f, .92f), MouseFilter = MouseFilterEnum.Ignore };
        _background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(_background);
        var body = new VBoxContainer { Position = new(40, 40), CustomMinimumSize = new(760, 0), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(body);
        _status = new Label { CustomMinimumSize = new(760, 90), AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore };
        body.AddChild(_status);
        _cancel = new Button { Name = "CancelSave", Text = "CANCEL SAVE", CustomMinimumSize = new(760, 50) };
        _cancel.Pressed += cancel; body.AddChild(_cancel);
        if (_receipt is not null) ShowReceipt(_receipt);
    }

    internal void ShowReceipt(RuntimeManualSaveReceipt receipt)
    {
        _receipt = receipt;
        if (_status is null) return;
        var pending = receipt.Disposition == "pending";
        _status.Text = RuntimeManualSaveFeedback.Describe(receipt) + (pending ? "" : "\nEscape: dismiss save status");
        _background.Visible = pending; _cancel.Visible = pending;
        MouseFilter = pending ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
        if (pending && !_cancel.HasFocus()) _cancel.GrabFocus();
        else if (!pending) _cancel.ReleaseFocus();
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true, Echo: false } key ||
            key.PhysicalKeycode != Key.Escape && key.Keycode != Key.Escape) return;
        GetViewport().SetInputAsHandled();
        if (_receipt?.Disposition == "pending") cancel(); else dismiss();
    }
}
