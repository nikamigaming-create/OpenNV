using System.Text.Json;

namespace OpenNV.LiveHarness;

// The inspector consumes immutable diagnostic evidence. It never sends input
// to either game and does not call bounds candidates exact pixel ownership.
internal sealed class HarnessTraceInspector : Form
{
    private readonly JsonDocument _report;
    private readonly TreeView _tree = new() { Dock = DockStyle.Fill, HideSelection = false };
    private readonly TextBox _search = new() { Dock = DockStyle.Top, PlaceholderText = "Find a node, source model, material or missing owner…" };
    private readonly TextBox _details = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false };
    private readonly PictureBox _image = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
    private readonly ComboBox _viewport = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _notice = new() { Dock = DockStyle.Bottom, Height = 44, AutoEllipsis = true };
    private readonly Dictionary<string, JsonElement> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonElement> _views = new(StringComparer.Ordinal);
    private JsonElement? _selection;
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    internal static string LoadLatest(string directory)
    {
        using var latest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "trace-latest.json")));
        var path = latest.RootElement.GetProperty("report").GetString()!;
        if (!File.Exists(path)) throw new InvalidDataException("Latest trace report is missing.");
        return path;
    }

    internal HarnessTraceInspector(string report)
    {
        _report = JsonDocument.Parse(File.ReadAllText(report));
        Text = "OpenNV · bytes → bound state → captured pixels";
        Size = new Size(1450, 900); MinimumSize = new Size(1000, 650);
        var layout = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 350, Width = 1450 };
        var right = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Height = 850, SplitterDistance = 430 };
        layout.Panel1.Controls.Add(_tree); layout.Panel1.Controls.Add(_search);
        right.Panel1.Controls.Add(_image); right.Panel1.Controls.Add(_viewport);
        right.Panel2.Controls.Add(_details); right.Panel2.Controls.Add(_notice);
        layout.Panel2.Controls.Add(right); Controls.Add(layout);
        foreach (var node in _report.RootElement.GetProperty("before").GetProperty("nodes").EnumerateArray())
            _nodes.Add(node.GetProperty("path").GetString()!, node);
        foreach (var viewport in _report.RootElement.GetProperty("viewports").EnumerateArray())
        {
            var path = viewport.GetProperty("path").GetString()!;
            _views.Add(path, viewport); _viewport.Items.Add(path);
        }
        _search.TextChanged += (_, _) => Populate();
        _tree.AfterSelect += (_, e) => SelectEntry(e.Node?.Tag);
        _viewport.SelectedIndexChanged += (_, _) => ShowViewport();
        _image.Paint += (_, e) => DrawSelection(e.Graphics);
        _image.MouseClick += (_, e) => PickCandidates(e.Location);
        Populate();
        if (_viewport.Items.Count != 0) _viewport.SelectedIndex = 0;
        _notice.Text = "INCOMPLETE · " + string.Join(" · ", _report.RootElement.GetProperty("missing").EnumerateArray().Take(4).Select(value => value.GetString()));
    }

    private void Populate()
    {
        _tree.BeginUpdate(); _tree.Nodes.Clear();
        var filter = _search.Text.Trim();
        var nodes = _tree.Nodes.Add("Scene instances");
        foreach (var (path, node) in _nodes)
            if (filter.Length == 0 || node.GetRawText().Contains(filter, StringComparison.OrdinalIgnoreCase))
                nodes.Nodes.Add(new TreeNode(path) { Tag = node });
        var sources = _tree.Nodes.Add("Bytes on disk and decoded resources");
        foreach (var source in _report.RootElement.GetProperty("before").GetProperty("sources").EnumerateArray())
            if (filter.Length == 0 || source.GetRawText().Contains(filter, StringComparison.OrdinalIgnoreCase))
                sources.Nodes.Add(new TreeNode(source.GetProperty("identity").GetString()) { Tag = source });
        var records = _tree.Nodes.Add("Winning / observed records");
        foreach (var record in _report.RootElement.GetProperty("before").GetProperty("records").EnumerateArray())
            if (filter.Length == 0 || record.GetRawText().Contains(filter, StringComparison.OrdinalIgnoreCase))
                records.Nodes.Add(new TreeNode(record.GetProperty("signature").GetString() + " " + record.GetProperty("identity").GetString()) { Tag = record });
        var missing = _tree.Nodes.Add("Missing evidence / owners");
        foreach (var row in _report.RootElement.GetProperty("missing").EnumerateArray())
            if (filter.Length == 0 || row.GetString()!.Contains(filter, StringComparison.OrdinalIgnoreCase))
                missing.Nodes.Add(new TreeNode(row.GetString()) { Tag = row, ForeColor = Color.Firebrick });
        nodes.Expand(); _tree.EndUpdate();
    }

    private void SelectEntry(object? tag)
    {
        if (tag is not JsonElement selected) return;
        _selection = selected;
        var related = new Dictionary<string, JsonElement>();
        FindResources(selected, related);
        var sources = new List<JsonElement>();
        if (selected.ValueKind == JsonValueKind.Object && selected.TryGetProperty("properties", out var properties) &&
            properties.TryGetProperty("sourceModel", out var model) && model.ValueKind == JsonValueKind.String)
            sources.AddRange(_report.RootElement.GetProperty("before").GetProperty("sources").EnumerateArray().Where(source =>
                source.GetProperty("identity").GetString()!.Replace('\\', '/').EndsWith(model.GetString()!.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)));
        _details.Text = JsonSerializer.Serialize(new { selected, boundResources = related, sourceBytes = sources }, Pretty);
        if (selected.ValueKind == JsonValueKind.Object && selected.TryGetProperty("viewport", out var view) && view.ValueKind == JsonValueKind.String && _views.ContainsKey(view.GetString()!))
            _viewport.SelectedItem = view.GetString();
        _image.Invalidate();
    }

    private void FindResources(JsonElement element, Dictionary<string, JsonElement> result)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("resource", out var id))
            {
                var key = id.ToString();
                if (!result.ContainsKey(key) && _report.RootElement.GetProperty("before").GetProperty("resources").TryGetProperty(key, out var resource))
                { result.Add(key, resource); FindResources(resource, result); }
            }
            foreach (var property in element.EnumerateObject()) FindResources(property.Value, result);
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) FindResources(child, result);
    }

    private void ShowViewport()
    {
        if (_viewport.SelectedItem is not string key) return;
        var previous = _image.Image;
        using var file = Image.FromFile(_views[key].GetProperty("preview").GetString()!);
        _image.Image = new Bitmap(file); previous?.Dispose();
    }

    private RectangleF ImageBounds()
    {
        if (_image.Image is not { } image) return RectangleF.Empty;
        var scale = Math.Min((float)_image.ClientSize.Width / image.Width, (float)_image.ClientSize.Height / image.Height);
        return new((_image.ClientSize.Width - image.Width * scale) / 2, (_image.ClientSize.Height - image.Height * scale) / 2, image.Width * scale, image.Height * scale);
    }

    private static RectangleF? ProjectedBounds(JsonElement node)
    {
        if (!node.TryGetProperty("properties", out var properties) || !properties.TryGetProperty("projectedBounds", out var box) || box.ValueKind != JsonValueKind.Object) return null;
        return new RectangleF(box.GetProperty("x").GetSingle(), box.GetProperty("y").GetSingle(), box.GetProperty("width").GetSingle(), box.GetProperty("height").GetSingle());
    }

    private void DrawSelection(Graphics graphics)
    {
        if (_selection is not { ValueKind: JsonValueKind.Object } selected || !selected.TryGetProperty("path", out _) || ProjectedBounds(selected) is not { } box || _image.Image is null) return;
        if (selected.GetProperty("viewport").GetString() != _viewport.SelectedItem?.ToString()) return;
        var image = ImageBounds(); var scale = image.Width / _image.Image.Width;
        using var pen = new Pen(Color.Orange, 2);
        graphics.DrawRectangle(pen, image.X + box.X * scale, image.Y + box.Y * scale, box.Width * scale, box.Height * scale);
    }

    private void PickCandidates(Point point)
    {
        var image = ImageBounds(); if (_image.Image is null || !image.Contains(point)) return;
        var pixel = new PointF((point.X - image.X) * _image.Image.Width / image.Width, (point.Y - image.Y) * _image.Image.Height / image.Height);
        var candidates = _nodes.Values.Where(node => node.GetProperty("viewport").GetString() == _viewport.SelectedItem?.ToString() &&
            ProjectedBounds(node) is { } box && box.Contains(pixel)).OrderBy(node => ProjectedBounds(node)!.Value.Width * ProjectedBounds(node)!.Value.Height).ToArray();
        _tree.Nodes.Clear();
        var group = _tree.Nodes.Add($"{candidates.Length} bounds candidates at {pixel.X:0}, {pixel.Y:0}");
        foreach (var node in candidates) group.Nodes.Add(new TreeNode(node.GetProperty("path").GetString()) { Tag = node });
        group.Expand();
        _notice.Text = "Candidates only: occlusion, alpha coverage and skinning require a GPU contributor pass. Select a candidate to inspect its bytes and bindings.";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _image.Image?.Dispose(); _report.Dispose(); }
        base.Dispose(disposing);
    }
}
