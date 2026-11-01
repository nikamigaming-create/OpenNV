using System.Xml.Linq;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeOwnedPipBoyMenu
{
    private void DescribeQuest(FalloutQuestSnapshot? quest)
    {
        var list = Tile("MM_QuestObjectivesList");
        var rows = new List<(XElement Tile, float Y)>();
        var total = 0f;
        foreach (var objective in quest?.Objectives?.Where(objective => objective.Displayed) ?? [])
        {
            var row = new XElement(_templates["MM_ListMarkerTemplate"]);
            row.SetAttributeValue("name", "QuestObjective" + objective.Index);
            list.Add(row);
            _tiles.Text[row] = _quests.ObjectiveText(quest!.Quest, objective.Index);
            _tiles.Bind(row, "listindex", rows.Count);
            _tiles.Bind(row, "_ShowEmptyMarker", 1);
            _tiles.Bind(row, "_selected", objective.Completed ? 1 : 0);
            _tiles.Bind(row, "_TextAlpha", objective.Completed ? 128 : 255);
            var text = row.Descendants("text").Single(tile => (string?)tile.Attribute("name") == "ListItemText");
            var height = _tiles.Number(text, "height") + _tiles.Number(row, "_VerticalSpacing");
            if (!float.IsFinite(height) || height <= 0)
                throw new InvalidDataException("Quest objective row has no finite source text extent.");
            _tiles.Bind(row, "height", height);
            rows.Add((row, total)); total += height;
        }
        var available = _tiles.Number(list, "height");
        if (!float.IsFinite(available) || available <= 0)
            throw new InvalidDataException("Quest objective list has no finite source extent.");
        _textOffset = Math.Clamp(_textOffset, 0, Math.Max(0, total - available));
        _tiles.Bind(list, "_enabled", 0);
        _tiles.Bind(list, "_num_filtered", rows.Count);
        foreach (var (row, y) in rows)
        {
            _tiles.Bind(row, "_y", y);
            _tiles.Bind(row, "y", y - _textOffset);
        }
        BindListNavigation(list, total, available, _textOffset, 40, offset =>
        {
            _textOffset = offset;
            foreach (var (row, y) in rows) _tiles.Bind(row, "y", y - offset);
            Layout(); QueueRedraw();
        });
    }
}
