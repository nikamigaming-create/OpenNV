using System.Xml.Linq;
using Godot;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeOwnedPipBoyMenu
{
    private void BindListNavigation(XElement list, int count, int visibleRows)
    {
        var needed = _offset > 0 || count > visibleRows;
        _tiles.Bind(list, "_scrollbar_vis", needed ? 1 : 0);
        foreach (var bar in list.Descendants().Where(tile => (string?)tile.Attribute("name") == "lb_scrollbar"))
        {
            _tiles.Bind(bar, "visible", needed ? 1 : 0);
            _tiles.Bind(bar, "_SetInCode", 1);
            _tiles.Bind(bar, "_number_of_items", count);
            _tiles.Bind(bar, "_number_of_visible_items", Math.Max(1, visibleRows));
            _tiles.Bind(bar, "_current_value", _offset);
            foreach (var marker in bar.Descendants().Where(tile => (string?)tile.Attribute("name") == "scrollbar_vert_marker"))
            { _tiles.Bind(marker, "dragy", 0); _tiles.Bind(marker, "dragoffsety", 0); }
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
                    _offset = Math.Clamp(_offset + direction * (page ? Math.Max(1, visibleRows) : 1), 0, Math.Max(0, count - 1));
                    Refresh();
                });
                AddChild(button); _buttons.Add((tile, button));
            }
        }
    }
}
