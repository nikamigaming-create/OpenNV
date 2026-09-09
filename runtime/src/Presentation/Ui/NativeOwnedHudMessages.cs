using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeOwnedHudMessages : Control
{
    private readonly FalloutPluginStack _records;
    private readonly FalloutHudNotifications _queue;
    private readonly Func<bool> _shown;
    private readonly FalloutHudMessageDeclarations _declaration;
    private readonly FalloutInstallationSettings _settings;
    private readonly NativeOwnedMenuTree _tiles;
    private readonly XElement _messages, _icon, _text, _bracket;
    private readonly XElement _quest, _objective, _questTitle, _checkbox, _filledCheckbox;
    private readonly FalloutQuestState _quests;
    private readonly double _questFadeIn, _questHold, _questFadeOut;
    private readonly Dictionary<FalloutHudEvent, (string Text, string? Icon, double Seconds)> _resolved = [];
    private long _displayed;
    private string? _displayIcon;
    internal string? Error { get; private set; }
    internal object State => new
    {
        current = _queue.Current,
        visible = Visible,
        elapsed = _queue.Elapsed,
        text = _tiles.String(IsObjective(_queue.Current?.Event) ? _objective : _text),
        icon = _displayIcon,
        error = Error,
        source = "owned-HUDMainMenu-templates-fonts-declarations",
        unbound = "native-fade-clock,glow,event-alignment"
    };

    internal NativeOwnedHudMessages(FalloutPluginStack records, FalloutHudNotifications queue, Func<bool>? shown = null, FalloutQuestState? quests = null)
    {
        Name = "HUDMainMenuMessages";
        MouseFilter = MouseFilterEnum.Ignore;
        ProcessMode = ProcessModeEnum.Always;
        _records = records;
        _queue = queue;
        _quests = quests ?? new(records);
        _shown = shown ?? (() => true);
        _questFadeIn = FalloutGameSettingFloats.Read(records, "fQuestCinematicObjectiveFadeIn");
        _questHold = FalloutGameSettingFloats.Read(records, "fQuestCinematicObjectivePauseTime");
        _questFadeOut = FalloutGameSettingFloats.Read(records, "fQuestCinematicObjectiveFadeOut");
        if (_questFadeIn <= 0 || _questHold <= 0 || _questFadeOut <= 0)
            throw new NotSupportedException("Quest reminder has an unsupported fade/hold clock.");
        var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned HUD source is absent.");
        _settings = FalloutInstallationSettings.Read(source);
        _declaration = FalloutExecutableStringTable.ReadHudMessageDeclarations(
            Path.Combine(Path.GetDirectoryName(source.ContentRoot)!, "FalloutNV.exe"));
        var sourceMenu = FalloutMenuXml.Expand(FalloutMenuXml.Read("menus/main/hud_main_menu.xml")).Elements("menu").Single();
        var menu = new XElement(sourceMenu.Name, sourceMenu.Attributes(),
            sourceMenu.Elements().Where(element => element.Attribute("name") is null).Select(element => new XElement(element)));
        _messages = new XElement(sourceMenu.Elements().Single(element => (string?)element.Attribute("name") == "Messages"));
        menu.Add(_messages);
        XElement Template(string name)
        {
            var tile = new XElement(sourceMenu.Elements("template").Single(element => (string?)element.Attribute("name") == name).Elements().Single());
            _messages.Add(tile);
            return tile;
        }
        _bracket = Template("template_message_bracket");
        _icon = Template("template_message_icon");
        _text = Template("template_justify_left_text");
        _quest = new XElement(sourceMenu.Elements().Single(element => (string?)element.Attribute("name") == "QuestReminder"));
        menu.Add(_quest);
        var stages = _quest.Elements().Single(element => (string?)element.Attribute("name") == "QuestStages");
        XElement QuestTemplate(string name)
        {
            var tile = new XElement(_quest.Elements("template").Single(element => (string?)element.Attribute("name") == name).Elements().Single());
            stages.Add(tile); return tile;
        }
        _checkbox = QuestTemplate("template_checkbox"); _filledCheckbox = QuestTemplate("template_filled_checkbox");
        _objective = new XElement(_text); stages.Add(_objective);
        _questTitle = new XElement(_text); _quest.Add(_questTitle);
        foreach (var template in _quest.Elements("template").ToArray()) template.Remove();
        _tiles = new(menu);
        _tiles.Bind(menu, "visible", 1);
        foreach (var (id, value) in _declaration.TextTraits)
            _tiles.Bind(_text, id switch
            {
                4001 => "x",
                4002 => "y",
                4003 => "visible",
                4009 => "alpha",
                4013 => "depth",
                4026 => "wrapwidth",
                _ => throw new NotSupportedException($"HUD text trait {id} is unbound."),
            }, value);
        _tiles.Bind(_messages, "visible", 0);
        _tiles.Bind(_quest, "visible", 0);
        foreach (var tile in new[] { _objective, _questTitle })
        {
            _tiles.Bind(tile, "visible", 1); _tiles.Bind(tile, "alpha", 255);
            _tiles.Bind(tile, "x", 0); _tiles.Bind(tile, "y", 0);
            _tiles.Bind(tile, "wrapwidth", _tiles.Number(_quest, "width"));
        }
        foreach (var tile in new[] { _checkbox, _filledCheckbox }) _tiles.Bind(tile, "alpha", 255);
        SetMeta("opennv_ui_source", "menus/main/hud_main_menu.xml;menus/prefabs/HUDTemplates.xml");
        SetMeta("opennv_ui_unbound", "native-fade-clock,glow,event-alignment,remaining-HUD-branches");
    }

    public override void _Ready()
    {
        Layout();
    }
    private NativeViewportLayout? _viewportLayout;
    public override void _EnterTree() => _viewportLayout = new(this, Layout);
    public override void _ExitTree() => _viewportLayout?.Dispose();
    private void Layout()
    {
        var size = GetViewportRect().Size;
        var scale = size.Y / 960;
        Scale = Vector2.One * scale;
        Size = _tiles.Screen = size / scale;
        _tiles.ResolutionConverter = 1 / scale;
        var wide = size.X / size.Y >= 16f / 9;
        _tiles.Bind(_messages, "x", _declaration.XInset + _declaration.SafeZoneScale *
            _settings.Number("Interface", wide ? "iSafeZoneXWide" : "iSafeZoneX"));
        _tiles.Bind(_messages, "y", _declaration.YInset + _declaration.SafeZoneScale *
            _settings.Number("Interface", wide ? "iSafeZoneYWide" : "iSafeZoneY"));
        _tiles.Bind(_quest, "x", (Size.X - _tiles.Number(_quest, "width")) / 2);
        _tiles.Bind(_quest, "y", (Size.Y - _tiles.Number(_quest, "height")) / 2);
        QueueRedraw();
    }
    public override void _Process(double delta)
    {
        if (Error is not null) return;
        try
        {
            Visible = _shown();
            if (!Visible) return; // Hidden menus must not consume unseen notifications.
            _queue.Advance(delta, value => Resolve(value).Seconds);
            var current = _queue.Current;
            var objective = IsObjective(current?.Event);
            if (objective)
            {
                var elapsed = _queue.Elapsed;
                var alpha = elapsed < _questFadeIn ? elapsed / _questFadeIn :
                    elapsed < _questFadeIn + _questHold ? 1 : 1 - (elapsed - _questFadeIn - _questHold) / _questFadeOut;
                _tiles.Bind(_quest, "alpha", (float)(255 * Math.Clamp(alpha, 0, 1))); QueueRedraw();
            }
            if ((current?.Ordinal ?? 0) == _displayed) return;
            _displayed = current?.Ordinal ?? 0;
            _displayIcon = null;
            _tiles.Bind(_messages, "visible", current is not null && !objective ? 1 : 0);
            _tiles.Bind(_quest, "visible", objective ? 1 : 0);
            if (current is not null)
            {
                var value = Resolve(current.Event);
                _displayIcon = value.Icon;
                _tiles.Text[_text] = value.Text;
                if (objective)
                {
                    _tiles.Text[_objective] = value.Text;
                    var record = _records.GetEffective(current.Event.Source);
                    _tiles.Text[_questTitle] = FalloutDialogueTopic.Text(record.ReadSubrecords().Single(field => field.Signature == "FULL").Data.Span);
                    _tiles.Bind(_checkbox, "visible", current.Event.Kind == FalloutHudEventKind.ObjectiveDisplayed ? 1 : 0);
                    _tiles.Bind(_filledCheckbox, "visible", current.Event.Kind == FalloutHudEventKind.ObjectiveCompleted ? 1 : 0);
                }
                _tiles.Bind(_text, "alpha", 255);
                _tiles.Bind(_bracket, "alpha", 255);
                _tiles.Bind(_icon, "alpha", value.Icon is null ? 0 : 255);
                if (value.Icon is not null) _tiles.SetFilename(_icon, value.Icon);
                _tiles.ValidateDrawing();
                GD.Print($"OPENNV_HUD_NOTICE ordinal={current.Ordinal} source={current.Event.Source} kind={current.Event.Kind} seconds={value.Seconds:R} presentation=owned-tiles parity=unverified");
            }
            QueueRedraw();
        }
        catch (Exception error)
        {
            Error = error.Message;
            Hide();
            GD.PushError($"OPENNV_HUD_NOTICE_UNBOUND {error.Message}");
        }
    }
    public override void _Draw()
    {
        if (Error is null) _tiles.Draw(this);
    }

    private (string Text, string? Icon, double Seconds) Resolve(FalloutHudEvent value)
    {
        if (_resolved.TryGetValue(value, out var result)) return result;
        var record = _records.GetEffective(value.Source);
        if (value.Kind is FalloutHudEventKind.ItemAdded or FalloutHudEventKind.ItemRemoved)
        {
            var name = FalloutDialogueTopic.Text(record.ReadSubrecords().Single(field => field.Signature == "FULL").Data.Span);
            var added = FalloutGameSettingStrings.Read(_records, value.Kind == FalloutHudEventKind.ItemAdded ? "sAddItemtoInventory" : "sRemoveItemfromInventory");
            result = (value.Count == 1
                ? Format(_declaration.SingleItemFormat, [name, added])
                : Format(_declaration.MultipleItemFormat, [value.Count.ToString(CultureInfo.InvariantCulture), name,
                    FalloutGameSettingStrings.Read(_records, "sPlural"), added]), _declaration.ItemIcon, _declaration.ItemSeconds);
        }
        else if (value.Kind == FalloutHudEventKind.Message)
        {
            var message = FalloutSourceMessage.Read(record);
            if (message.AutomaticTime || message.DisplaySeconds is null or 0)
                throw new NotSupportedException($"HUD message {value.Source} automatic lifetime needs an owner.");
            string? icon = null;
            if (message.Icon is { } key)
            {
                var iconRecord = _records.GetEffective(key);
                if (iconRecord.Signature != "MICN") throw new InvalidDataException("Message icon is not MICN.");
                icon = FalloutDialogueTopic.Text(iconRecord.ReadSubrecords().Single(field => field.Signature == "ICON").Data.Span);
            }
            result = (message.Text, icon, message.DisplaySeconds.Value);
        }
        else if (IsObjective(value))
            result = (_quests.ObjectiveText(value.Source, value.ObjectiveIndex!.Value), null, _questFadeIn + _questHold + _questFadeOut);
        else throw new NotSupportedException($"HUD event {value.Kind} has no presentation owner.");
        _resolved.Add(value, result);
        return result;
    }
    private static bool IsObjective(FalloutHudEvent? value) => value?.Kind is FalloutHudEventKind.ObjectiveDisplayed or FalloutHudEventKind.ObjectiveCompleted;
    private static string Format(string format, IReadOnlyList<string> arguments)
    {
        var result = new StringBuilder(); var argument = 0;
        for (var index = 0; index < format.Length; index++)
        {
            if (format[index] != '%') { result.Append(format[index]); continue; }
            if (++index == format.Length) throw new InvalidDataException("HUD format is incomplete.");
            if (format[index] == '%') { result.Append('%'); continue; }
            if (format[index] is not ('s' or 'i') || argument == arguments.Count)
                throw new NotSupportedException("HUD format has an unbound conversion.");
            result.Append(arguments[argument++]);
        }
        if (argument != arguments.Count) throw new InvalidDataException("HUD format did not consume its source arguments.");
        return result.ToString();
    }
}
