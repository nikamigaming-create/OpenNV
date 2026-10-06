using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Ui;

// The shared save/session controls use the owned font. Layout is OpenNV's;
// this is not a claim of reproducing the retail save-browser pixels.
internal sealed partial class NativeGameSessionMenu : Control
{
    private readonly RuntimeSaveSlotCatalog _catalog;
    private readonly Action _resume, _cancelSave, _title, _quit;
    private readonly Func<RuntimeManualSaveReceipt> _save;
    private readonly Action<RuntimeSaveSlotMetadata> _load;
    private readonly Func<RuntimeSaveSlotMetadata, string> _describe;
    private readonly bool _inGame;
    private readonly bool _defeated;
    private VBoxContainer _body = null!;
    private Label _status = null!;
    private bool _browser, _confirming, _busy, _saving;
    private RuntimeManualSaveReceipt? _manualSaveReceipt;
    private readonly Dictionary<Button, bool> _saveDisabledButtons = [];
    private Button? _cancelSaveButton;
    private int _page;
    private string? _selected;
    private readonly List<Button> _rows = [];
    private IReadOnlyList<RuntimeSaveSlotMetadata> _slots = [];
    private int PageSize => _inGame && !_defeated ? 3 : 4;

    internal NativeGameSessionMenu(RuntimeSaveSlotCatalog catalog, bool inGame, bool showSaves, bool defeated,
        Action resume, Func<RuntimeManualSaveReceipt> save, Action cancelSave,
        Action<RuntimeSaveSlotMetadata> load, Action title, Action quit,
        Func<RuntimeSaveSlotMetadata, string> describe)
    {
        Name = "SessionMenu"; ProcessMode = ProcessModeEnum.Always;
        _catalog = catalog; _inGame = inGame; _browser = showSaves; _defeated = defeated;
        _resume = resume; _save = save; _cancelSave = cancelSave; _load = load; _title = title; _quit = quit; _describe = describe;
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var font = NativeBitmapFontAsset.Read(FalloutInstallationSettings.Read(RuntimeLiveContentSource.Current!), 3);
        Theme = new Theme { DefaultFont = font.CreateFontFile(), DefaultFontSize = 26 };
        var background = new ColorRect { Color = new(0.035f, .025f, .015f, .96f), MouseFilter = MouseFilterEnum.Stop };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(background);
        var center = new CenterContainer(); center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(center);
        _body = new VBoxContainer { CustomMinimumSize = new(780, 0) };
        _body.AddThemeConstantOverride("separation", 10); center.AddChild(_body);
        if (_browser) ShowSaves(); else ShowPause();
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true, Echo: false } key ||
            key.PhysicalKeycode != Key.Escape && key.Keycode != Key.Escape) return;
        GetViewport().SetInputAsHandled(); Back();
    }

    internal void Back()
    {
        if (_saving) { _cancelSave(); return; }
        if (_busy) { _status.Text = "Loading is still in progress."; return; }
        if (_confirming) { if (_browser) ShowSaves(); else ShowPause(); }
        else if (_browser && _inGame) ShowPause();
        else if (!_defeated) _resume();
    }

    private void BeginPage(string title)
    {
        _confirming = false; _rows.Clear(); _cancelSaveButton = null;
        foreach (var node in _body.GetChildren()) { _body.RemoveChild(node); node.QueueFree(); }
        _body.AddChild(new Label { Text = title, HorizontalAlignment = HorizontalAlignment.Center });
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new(760, 36) };
        _body.AddChild(_status);
    }

    private Button AddButton(string name, string text, Action action)
    {
        var button = new Button { Name = name, Text = text, CustomMinimumSize = new(760, 50) };
        button.Pressed += () => { if (!_busy || name == "CancelSave" && _saving) Run(action); };
        _body.AddChild(button); return button;
    }

    private void Run(Action action)
    {
        try { action(); }
        catch (Exception error)
        {
            _busy = false;
            _status.Text = "Unable to complete this action: " + error.Message;
            GD.PushError($"OPENNV_SESSION_MENU_FAILURE {error}");
        }
    }

    internal void ShowFailure(string message)
    {
        _busy = false;
        _status.Text = "Unable to load: " + message;
    }

    internal void ShowManualSaveReceipt(RuntimeManualSaveReceipt? receipt)
    {
        if (receipt is null) return;
        _manualSaveReceipt = receipt;
        if (_status is null || _confirming || _busy && !_saving) return;
        var wasSaving = _saving;
        _saving = receipt.Disposition == "pending";
        _busy = _saving;
        if (_saving)
        {
            foreach (var button in _body.FindChildren("*", "", true, false).OfType<Button>().Where(button => button != _cancelSaveButton))
            {
                _saveDisabledButtons.TryAdd(button, button.Disabled); button.Disabled = true;
            }
        }
        else if (wasSaving)
        {
            foreach (var (button, disabled) in _saveDisabledButtons)
                if (GodotObject.IsInstanceValid(button)) button.Disabled = disabled;
            _saveDisabledButtons.Clear();
            if (receipt.Disposition == "completed" && _browser)
            {
                _page = 0; _selected = receipt.CommittedSlot!.Id; ShowSaves();
            }
        }
        if (_cancelSaveButton is { } cancel)
        {
            cancel.Visible = _saving; cancel.Disabled = !_saving;
            if (_saving && !cancel.HasFocus()) cancel.GrabFocus();
        }
        _status.Text = RuntimeManualSaveFeedback.Describe(receipt);
    }

    private void AddSaveCancel()
    {
        _cancelSaveButton = AddButton("CancelSave", "CANCEL SAVE", _cancelSave);
        _cancelSaveButton.Visible = false; _cancelSaveButton.Disabled = true;
    }

    private void ShowPause()
    {
        _browser = false; BeginPage(_defeated ? "YOU DIED" : "PAUSED");
        if (!_defeated) AddButton("Resume", "RESUME", _resume).GrabFocus();
        if (_inGame && !_defeated) AddSaveCancel();
        var saves = AddButton("SaveLoad", _defeated ? "LOAD GAME" : "SAVE / LOAD", ShowSaves);
        if (_defeated) saves.GrabFocus();
        AddButton("Title", "MAIN MENU", () => Confirm("Return to the title screen? Unsaved progress will be lost.", _title));
        AddButton("Quit", "QUIT GAME", () => Confirm("Quit the game? Unsaved progress will be lost.", _quit));
        _status.Text = _defeated ? "Health reached zero. Load an earlier save to continue." :
            "Escape / controller Menu / right stick click: back";
        ShowManualSaveReceipt(_manualSaveReceipt);
    }

    private void ShowSaves()
    {
        _browser = true; BeginPage("SAVE / LOAD");
        var failures = new List<string>();
        _slots = _catalog.ReadSlots(includeCurrent: true, (path, error) => failures.Add(Path.GetFileName(path) + ": " + error.Message));
        _page = Math.Clamp(_page, 0, Math.Max(0, (_slots.Count - 1) / PageSize));
        if (_selected is null || !_slots.Any(slot => slot.Id == _selected)) _selected = _slots.FirstOrDefault()?.Id;
        if (_inGame && !_defeated) AddButton("CreateSave", "CREATE NEW SAVE", () =>
        {
            ShowManualSaveReceipt(_save());
        });
        if (_inGame && !_defeated) AddSaveCancel();
        foreach (var slot in _slots.Skip(_page * PageSize).Take(PageSize))
        {
            var row = AddButton("Slot_" + slot.Id, _describe(slot), () =>
            {
                _selected = slot.Id;
                foreach (var button in _rows) button.ButtonPressed = button.Name == "Slot_" + slot.Id;
            });
            row.ToggleMode = true; row.ButtonPressed = slot.Id == _selected; _rows.Add(row);
        }
        if (_slots.Count > PageSize)
        {
            var navigation = new HBoxContainer(); _body.AddChild(navigation);
            void Page(string text, int step)
            {
                var button = new Button { Text = text, CustomMinimumSize = new(375, 45), Disabled = _page + step < 0 || (_page + step) * PageSize >= _slots.Count };
                button.Pressed += () => { if (!_busy) Run(() => { _page += step; ShowSaves(); }); }; navigation.AddChild(button);
            }
            Page("PREVIOUS", -1); Page("NEXT", 1);
        }
        AddButton("LoadSelected", "LOAD SELECTED", () =>
        {
            var slot = _slots.Single(value => value.Id == _selected);
            void Load()
            {
                _status.Text = "Loading saved game..."; _busy = true; _load(slot);
            }
            if (_inGame) Confirm("Load this save? Unsaved progress will be lost.", Load); else Load();
        }).Disabled = _slots.Count == 0;
        AddButton("Back", "BACK", Back);
        _status.Text = failures.Count > 0 ? $"{failures.Count} unavailable save(s): {failures[0]}" :
            _slots.Count == 0 ? "No saved games yet." : $"{_slots.Count} saved games / page {_page + 1}";
        ShowManualSaveReceipt(_manualSaveReceipt);
    }

    private void Confirm(string question, Action action)
    {
        BeginPage(question); _confirming = true;
        AddButton("Confirm", "YES", action);
        AddButton("Cancel", "CANCEL", Back).GrabFocus();
    }
}
