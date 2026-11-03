using Godot;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeVatsMenu
{
    // Explicit inspection only: these are the same current authored rectangles
    // consumed by PartAt/CommandAt. No projection, target scan or gameplay call.
    internal object ObservedControls
    {
        get
        {
            var transform = GetGlobalTransformWithCanvas();
            float[] Rectangle(System.Xml.Linq.XElement tile)
            {
                var local = new Rect2(_tiles.Position(tile), new(_tiles.Number(tile, "width"), _tiles.Number(tile, "height")));
                var corners = new[] { transform * local.Position, transform * new Vector2(local.End.X, local.Position.Y),
                    transform * local.End, transform * new Vector2(local.Position.X, local.End.Y) };
                var minimum = new Vector2(corners.Min(value => value.X), corners.Min(value => value.Y));
                var maximum = new Vector2(corners.Max(value => value.X), corners.Max(value => value.Y));
                return [minimum.X, minimum.Y, maximum.X - minimum.X, maximum.Y - minimum.Y];
            }
            return new
            {
                source = "menus/vats_menu.xml;current-PartAt-and-CommandAt-rectangles",
                instanceId = GetInstanceId().ToString(System.Globalization.CultureInfo.InvariantCulture),
                menuId = MenuId,
                visible = IsVisibleInTree() && !_closed && !_faulted,
                preparing = _preparing(),
                phase = _session.CurrentPhase.ToString(),
                viewport = new[] { GetViewportRect().Size.X, GetViewportRect().Size.Y },
                selectedActor = _selectedActor?.ToString(),
                selectedPart = _selectedPart,
                availableAP = _session.AvailableActionPoints - _session.ReservedActionPoints,
                notice = _tiles.Text.GetValueOrDefault(_notice),
                parts = _parts.Select(value => new
                {
                    sourceChoiceKind = "vats-part",
                    sourceChoiceId = value.Target.Reference + ":part:" + value.Target.Part,
                    sourceChoiceMenu = "VATSMenu:" + MenuId,
                    reference = value.Target.Reference.ToString(),
                    @base = value.Target.Base.ToString(),
                    part = value.Target.Part,
                    name = value.Target.PartName,
                    visible = _tiles.Number(value.Tile, "visible") != 0,
                    targetVisible = value.Target.Visible,
                    selected = value.Target.Reference == _selectedActor && value.Target.Part == _selectedPart,
                    chance = value.Target.Chance,
                    actionPointCost = value.Target.ActionPointCost,
                    rect = Rectangle(value.Text)
                }).ToArray(),
                commands = _glow.Elements().Where(tile => (string?)tile.Attribute("name") is
                    "accept_button" or "return_button" or "left_arrow" or "right_arrow").Select(tile => new
                    {
                        sourceChoiceKind = "vats-command",
                        sourceChoiceId = (string?)tile.Attribute("name"),
                        sourceChoiceMenu = "VATSMenu:" + MenuId,
                        visible = _tiles.Number(tile, "visible") != 0,
                        rect = Rectangle(tile)
                    }).ToArray()
            };
        }
    }
}
