using System.Xml.Linq;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeOwnedRecipeMenu
{
    private sealed record SourceRow(string Label, int Alpha, Action Focus, Action Activate, FalloutFormKey Form);
    private sealed record RenderedRow(XElement Tile, NativeOwnedTileTarget Control, SourceRow Source, float Top, float Height);
    private sealed class SourceList(XElement tile, XElement bar, float step)
    {
        internal XElement Tile { get; } = tile;
        internal XElement Bar { get; } = bar;
        internal float SmallStep { get; } = step;
        internal List<RenderedRow> Rows { get; } = [];
        internal float Offset, Height;
        internal int Selected;
    }
    private readonly List<SourceList> _lists = [];
    private readonly List<(XElement Tile, NativeOwnedTileTarget Control, SourceList List)> _scrollTargets = [];
    private SourceList? _activeList;
    private (SourceList View, RenderedRow Row, bool EnsureVisible)? _pendingFocus;
    private bool GuiCaptureHeld => _actions.Any(value => value.Control.IsPressed()) ||
        _lists.Any(view => view.Rows.Any(row => row.Control.IsPressed())) ||
        _scrollTargets.Any(value => value.Control.IsPressed());

    private void AddAction(XElement tile, Action action)
    {
        var button = new NativeOwnedTileTarget { Name = (string)tile.Attribute("name")!, Text = _tiles.String(tile), FocusMode = FocusModeEnum.All };
        _actionOwners.Add(tile, action);
        button.Pressed += () => { if (_quantity is null) { if ((string?)tile.Attribute("name") == "RM_ButtonB") Close(); else Try(action); } };
        button.MouseEntered += () => { _tiles.Bind(tile, "mouseover", 1); QueueRedraw(); };
        button.MouseExited += () => { _tiles.Bind(tile, "mouseover", 0); QueueRedraw(); };
        AddChild(button); _actions.Add((tile, button));
    }
    private void InitializeList(XElement tile)
    {
        var bar = tile.Descendants().Single(value => (string?)value.Attribute("name") == "lb_scrollbar");
        var view = new SourceList(tile, bar, _tiles.Number(tile, "_scroll_delta")); _lists.Add(view);
        _activeList ??= view;
        _tiles.Bind(tile, "_scroll_delta", 1); _tiles.Bind(bar, "_SetInCode", 1);
        _tiles.Bind(bar, "_current_value", 0);
        var marker = bar.Descendants().Single(value => (string?)value.Attribute("name") == "scrollbar_vert_marker");
        _tiles.Bind(marker, "dragy", 0); _tiles.Bind(marker, "dragoffsety", 0); _tiles.Bind(marker, "dragstarty", -1);
        foreach (var (name, direction, page) in new[]
        {
            ("scrollbar_vert_up", -1, false), ("scrollbar_vert_down", 1, false),
            ("scrollbar_vert_page_up", -1, true), ("scrollbar_vert_page_down", 1, true)
        })
        {
            var part = bar.Descendants().Single(value => (string?)value.Attribute("name") == name);
            var button = new NativeOwnedTileTarget { Name = name, FocusMode = FocusModeEnum.None };
            button.Pressed += () => Try(() => Scroll(view, direction * (page ? _tiles.Number(tile, "height") : view.SmallStep)));
            AddChild(button); _scrollTargets.Add((part, button, view));
        }
        var thumb = new NativeOwnedTileTarget { Name = "RecipeScrollbarThumb", FocusMode = FocusModeEnum.None };
        var dragging = false; var startY = 0f; var startOffset = 0f;
        thumb.GuiInput += input =>
        {
            if (_quantity is not null) return;
            Try(() =>
            {
                if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } mouse)
                {
                    dragging = mouse.Pressed; startY = GetLocalMousePosition().Y; startOffset = view.Offset;
                    _tiles.Bind(marker, "dragstarty", dragging ? 0 : -1); thumb.AcceptEvent();
                }
                else if (dragging && input is InputEventMouseMotion motion && (motion.ButtonMask & MouseButtonMask.Left) != 0)
                {
                    var step = _tiles.Number(marker, "_step_height");
                    if (!float.IsFinite(step) || step <= 0) throw new InvalidDataException("Recipe scrollbar has no positive source drag step.");
                    Scroll(view, startOffset + (GetLocalMousePosition().Y - startY) / step - view.Offset);
                    thumb.AcceptEvent();
                }
            });
        };
        AddChild(thumb); _scrollTargets.Add((marker, thumb, view));
    }
    private void ResetList(XElement tile) => _lists.Single(view => view.Tile == tile).Offset = 0;
    private void SetRows(XElement tile, IReadOnlyList<SourceRow> rows)
    {
        var view = _lists.Single(value => value.Tile == tile);
        var selected = view.Rows.ElementAtOrDefault(view.Selected)?.Source.Form;
        _tiles.Forget(view.Rows.Select(row => row.Tile));
        foreach (var row in view.Rows) { row.Tile.Remove(); RemoveChild(row.Control); row.Control.QueueFree(); }
        view.Rows.Clear(); view.Height = 0;
        foreach (var (model, index) in rows.Select((model, index) => (model, index)))
        {
            var source = new XElement(_template); source.SetAttributeValue("name", "RecipeRow_" + index); tile.Add(source);
            _tiles.BindText(source, "string", model.Label); _tiles.BindText(source, "_Value", "");
            _tiles.Bind(source, "listindex", index); _tiles.Bind(source, "_y", view.Height);
            _tiles.Bind(source, "_IsEquipped", 0); _tiles.Bind(source, "_IsRecipeSelected", 0);
            var text = source.Descendants("text").Single(value => (string?)value.Attribute("name") == "ListItemText");
            _tiles.Bind(text, "alpha", model.Alpha);
            var height = _tiles.Number(text, "height") + _tiles.Number(source, "_VerticalSpacing");
            if (!float.IsFinite(height) || height <= 0) throw new InvalidDataException("Recipe source row has no positive measured height.");
            _tiles.Bind(source, "height", height);
            var button = new NativeOwnedTileTarget { Name = "Recipe_" + index, Text = model.Label, FocusMode = FocusModeEnum.All };
            var row = new RenderedRow(source, button, model, view.Height, height); view.Rows.Add(row); AddChild(button);
            button.MouseEntered += () => Try(() => Focus(view, row, false));
            button.FocusEntered += () => Try(() => Focus(view, row, true));
            button.Pressed += () => Try(() => { if (_quantity is null) { Focus(view, row, false); model.Activate(); } });
            view.Height += height;
        }
        view.Selected = selected is { } form ? Math.Max(0, view.Rows.FindIndex(row => row.Source.Form == form)) : 0;
    }
    private void Focus(SourceList view, RenderedRow row, bool ensureVisible)
    {
        if (_quantity is not null || _closed) return;
        if (GuiCaptureHeld) { _pendingFocus = (view, row, ensureVisible); return; }
        _pendingFocus = null;
        _activeList = view; view.Selected = view.Rows.IndexOf(row);
        if (ensureVisible)
        {
            var visible = _tiles.Number(view.Tile, "height");
            if (row.Top < view.Offset) view.Offset = row.Top;
            else if (row.Top + row.Height > view.Offset + visible) view.Offset = row.Top + row.Height - visible;
        }
        row.Source.Focus(); LayoutLists(); QueueRedraw();
    }
    private void LayoutLists()
    {
        foreach (var view in _lists)
        {
            var visible = _tiles.Number(view.Tile, "height");
            view.Offset = Math.Clamp(view.Offset, 0, Math.Max(0, view.Height - visible));
            _tiles.Bind(view.Tile, "_enabled", _activeList == view ? 1 : 0);
            _tiles.Bind(view.Tile, "_scrollbar_vis", view.Height > visible ? 1 : 0);
            _tiles.Bind(view.Tile, "_num_filtered", view.Rows.Count);
            _tiles.Bind(view.Bar, "visible", view.Height > visible ? 1 : 0);
            _tiles.Bind(view.Bar, "_number_of_items", view.Height);
            _tiles.Bind(view.Bar, "_number_of_visible_items", visible);
            _tiles.Bind(view.Bar, "_current_value", view.Offset);
            var selected = view.Rows.ElementAtOrDefault(view.Selected);
            _tiles.Bind(view.Tile, "_highlight_y", selected is null ? -1 : selected.Top - view.Offset);
            _tiles.Bind(view.Tile, "_selected_height", selected?.Height ?? 0);
            var bounds = new Rect2(_tiles.Position(view.Tile), new(_tiles.Number(view.Tile, "width"), visible));
            foreach (var row in view.Rows)
            {
                var rect = new Rect2(_tiles.Position(row.Tile), new(_tiles.Number(row.Tile, "width"), row.Height)).Intersection(bounds);
                row.Control.Visible = rect.HasArea(); row.Control.Position = rect.Position; row.Control.Size = rect.Size;
            }
        }
        _tiles.Bind(_tiles.Root, "_IsContainerListSelected", _activeList?.Tile == _componentList ? 1 : 0);
        foreach (var (tile, control, view) in _scrollTargets)
        {
            control.Visible = _tiles.Number(view.Bar, "visible") != 0 && _tiles.Number(tile, "visible") != 0;
            if (!control.Visible) continue;
            control.Position = _tiles.Position(tile); control.Size = new(_tiles.Number(tile, "width"), _tiles.Number(tile, "height"));
        }
    }
    private void Scroll(SourceList view, float change)
    {
        if (_quantity is not null) return;
        view.Offset += change; LayoutLists(); QueueRedraw();
    }
    private void ScrollAt(Vector2 point, int direction)
    {
        var view = _lists.FirstOrDefault(value => new Rect2(_tiles.Position(value.Tile),
            new(_tiles.Number(value.Tile, "width"), _tiles.Number(value.Tile, "height"))).HasPoint(point)) ?? _activeList;
        if (view is not null) Scroll(view, direction * view.SmallStep);
    }
    private void MoveSelection(int direction)
    {
        var view = _activeList;
        if (view is null || view.Rows.Count == 0) return;
        var row = view.Rows[Math.Clamp(view.Selected + direction, 0, view.Rows.Count - 1)];
        Focus(view, row, true); row.Control.GrabFocus();
    }
}
