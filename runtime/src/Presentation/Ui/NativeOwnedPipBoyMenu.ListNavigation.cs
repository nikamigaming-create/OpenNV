using System.Xml.Linq;
using Godot;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeOwnedPipBoyMenu
{
    private void BindListNavigation(XElement list, int count, int visibleRows)
        => BindListNavigation(list, count, Math.Max(1, visibleRows), _offset, 1, value =>
        {
            _offset = (int)value;
            Refresh();
        }, false, Math.Max(0, count - 1));

    private void BindListNavigation(XElement list, float count, float visible, float offset,
        float smallStep, Action<float> changed, bool draggable = true, float? maximum = null)
    {
        var limit = maximum ?? Math.Max(0, count - visible);
        var needed = offset > 0 || count > visible;
        _tiles.Bind(list, "_scrollbar_vis", needed ? 1 : 0);
        foreach (var bar in list.Descendants().Where(tile => (string?)tile.Attribute("name") == "lb_scrollbar"))
        {
            _tiles.Bind(bar, "visible", needed ? 1 : 0);
            _tiles.Bind(bar, "_SetInCode", 1);
            _tiles.Bind(bar, "_number_of_items", count);
            _tiles.Bind(bar, "_number_of_visible_items", visible);
            _tiles.Bind(bar, "_current_value", offset);
            var marker = bar.Descendants().SingleOrDefault(tile => (string?)tile.Attribute("name") == "scrollbar_vert_marker");
            if (marker is not null)
            {
                _tiles.Bind(marker, "dragy", 0); _tiles.Bind(marker, "dragoffsety", 0);
                _tiles.Bind(marker, "dragstarty", -1);
            }
            void Publish(float value, bool dragging = false)
            {
                offset = Math.Clamp(value, 0, limit);
                _tiles.Bind(bar, "_current_value", offset);
                if (marker is not null)
                {
                    var step = _tiles.Number(marker, "_step_height");
                    _tiles.Bind(marker, "dragy", float.IsFinite(step) ? offset * step : 0);
                    _tiles.Bind(marker, "dragstarty", dragging ? 0 : -1);
                }
                changed(offset);
            }
            foreach (var (name, direction, page) in new[]
            {
                ("scrollbar_vert_up", -1, false), ("scrollbar_vert_down", 1, false),
                ("scrollbar_vert_page_up", -1, true), ("scrollbar_vert_page_down", 1, true)
            })
            {
                var tile = bar.Descendants().SingleOrDefault(tile => (string?)tile.Attribute("name") == name);
                if (tile is null) continue;
                var button = new NativeOwnedTileTarget { Name = name, Text = name, FocusMode = FocusModeEnum.None };
                button.Pressed += () => RunAction(() =>
                {
                    Publish(offset + direction * (page ? visible : smallStep));
                });
                AddChild(button); _buttons.Add((tile, button));
            }
            if (draggable && marker is not null)
            {
                var thumb = new NativeOwnedTileTarget { Name = "ScrollbarThumb", FocusMode = FocusModeEnum.None };
                var dragging = false; var startY = 0f; var startValue = offset;
                thumb.GuiInput += input =>
                {
                    if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left } &&
                        !(dragging && input is InputEventMouseMotion move && (move.ButtonMask & MouseButtonMask.Left) != 0)) return;
                    RunAction(() =>
                    {
                        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } mouse)
                        {
                            dragging = mouse.Pressed;
                            if (dragging) { startY = GetLocalMousePosition().Y; startValue = offset; }
                            Publish(offset, dragging);
                            thumb.AcceptEvent();
                        }
                        else if (dragging && input is InputEventMouseMotion motion && (motion.ButtonMask & MouseButtonMask.Left) != 0)
                        {
                            var step = _tiles.Number(marker, "_step_height");
                            if (float.IsFinite(step) && step > 0)
                                Publish(startValue + (GetLocalMousePosition().Y - startY) / step, true);
                            thumb.AcceptEvent();
                        }
                    });
                };
                AddChild(thumb); _buttons.Add((marker, thumb));
            }
        }
    }
}
