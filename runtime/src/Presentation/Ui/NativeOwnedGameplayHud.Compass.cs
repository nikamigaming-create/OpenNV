using System.Xml.Linq;
using Godot;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Presentation.Ui;

internal partial class NativeOwnedGameplayHud
{
    private readonly Func<float, FalloutCompassFrame>? _compassSnapshot;
    private XElement? _compassWindow, _compassGroup, _compassNpcGroup, _compassLandmarkTemplate, _compassQuestTemplate, _compassNpcTemplate;
    private readonly Dictionary<(string Kind, string Reference), XElement> _compassIcons = [];
    private FalloutCompassFrame? _lastCompass;
    private string? _compassError;

    private void InitializeCompass(XElement source, XElement parent)
    {
        XElement Template(string name) => new(source.Elements("template")
            .Single(element => (string?)element.Attribute("name") == name).Elements().Single());
        _compassWindow = Template("template_compass_window");
        _compassGroup = Template("template_compass_icon_group");
        _compassLandmarkTemplate = Template("template_compass_icon_marker");
        _compassQuestTemplate = Template("template_compass_icon_quest");
        _compassNpcGroup = Template("template_compass_icon_group");
        _compassNpcTemplate = Template("compass_npc_icon");
        parent.Add(_compassWindow); _compassWindow.Add(_compassGroup);
        _compassWindow.Add(_compassNpcGroup);
        // Native creates all groups from the common source template, then
        // positions NPC ticks separately from landmarks and objectives.
        _tiles.Bind(_compassGroup, "y", 30);
        _tiles.Bind(_compassNpcGroup, "y", 10);
        _tiles.Bind(_compassNpcGroup, "depth", 2);
        _tiles.Bind(_compassWindow, "visible", 0);
    }

    private void UpdateCompass()
    {
        if (_compassWindow is null || _compassSnapshot is null) return;
        try
        {
            var frame = _compassSnapshot(_tiles.Number(_compassWindow, "width"));
            if (_compassError is null && _lastCompass is { } previous && previous.HeadingRadians == frame.HeadingRadians &&
                previous.StripOffset == frame.StripOffset && previous.Markers.SequenceEqual(frame.Markers) && previous.Unbound.SequenceEqual(frame.Unbound)) return;
            var validate = _lastCompass?.StripOffset is null;
            _compassError = null; _lastCompass = frame;
            _tiles.Bind(_compassWindow, "visible", frame.StripOffset is null ? 0 : 1);
            if (frame.StripOffset is { } offset) _tiles.BindTextureOffset(_compassWindow, new(offset, 0));
            var active = new HashSet<(string, string)>();
            foreach (var marker in frame.Markers)
            {
                var key = (marker.Kind, marker.Reference.ToString());
                active.Add(key);
                if (!_compassIcons.TryGetValue(key, out var tile))
                {
                    tile = new XElement(marker.Kind switch
                    {
                        "quest" => _compassQuestTemplate!,
                        "landmark" => _compassLandmarkTemplate!,
                        "actor" => _compassNpcTemplate!,
                        _ => throw new NotSupportedException($"Owned compass marker kind {marker.Kind} is unbound."),
                    });
                    _compassIcons.Add(key, tile);
                    (marker.Kind == "actor" ? _compassNpcGroup! : _compassGroup!).Add(tile);
                    validate = true;
                }
                _tiles.Bind(tile, "_Heading", marker.Heading); _tiles.Bind(tile, "_Distance", marker.Distance);
                if (marker.Kind == "actor")
                    tile.SetElementValue("systemcolor", marker.Hostile ? "entity_hudalt" : "entity_hudmain");
                if (marker.Kind == "landmark")
                {
                    var filename = marker.Discovered ? "Interface/HUD/glow_hud_compass_landmark_discovered.dds" :
                        "Interface/HUD/glow_hud_compass_landmark.dds";
                    if (tile.Element("filename")?.Value != filename) { _tiles.SetFilename(tile, filename); validate = true; }
                }
            }
            foreach (var key in _compassIcons.Keys.Where(key => !active.Contains(key)).ToArray())
            {
                var tile = _compassIcons[key]; _tiles.Forget(tile); tile.Remove(); _compassIcons.Remove(key);
                validate = true;
            }
            if (validate) _tiles.ValidateDrawing();
            QueueRedraw();
        }
        catch (Exception error)
        {
            if (_compassError != error.Message) GD.PushError($"OPENNV_GAMEPLAY_COMPASS_FAIL {error.Message}");
            _compassError = error.Message; _lastCompass = null;
            _tiles.Bind(_compassWindow, "visible", 0); QueueRedraw();
        }
    }
}
