using System.Xml.Linq;

namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutUiComponentStore
{
    internal string InstantiateTemplate(string parentPath, string templateName)
    {
        var parent = RequireComponent(parentPath);
        var menu = _menus[parent.MenuName];
        var definitions = menu.Root.Source.Descendants("template").Where(element =>
            string.Equals((string?)element.Attribute("name"), templateName, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (definitions.Length != 1 || definitions[0].Elements().Where(IsTile).ToArray() is not { Length: 1 } roots)
            throw new InvalidDataException("Owned UI template has no unique source definition/root: " + templateName);
        var source = new XElement(roots[0]);
        var name = (string?)source.Attribute("name") ?? throw new InvalidDataException("Owned template root has no source name.");
        var ordinal = parent.Children.Count(child => child.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        var instance = Build(source, parent, parent.MenuName, ordinal);
        parent.Children.Add(instance); parent.Source.Add(source); ++Revision;
        return instance.Path;
    }
    internal XElement CopyComponent(string path) => new(RequireComponent(path).Source);
    internal float ReadComponentFloat(string component, string trait) =>
        EvaluateFloat(RequireComponent(component), trait, []);
    internal void WriteComponentFloat(string component, string trait, float value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trait);
        if (!float.IsFinite(value)) throw new InvalidDataException("Native tile setter requires a finite source Float32 value.");
        WriteFloat(Key(RequireComponent(component), trait), value);
    }
    private Tile RequireComponent(string path)
    {
        if (!TrySplitComponent(path, out var segments) || ResolveTile(segments, alt: true) is not { } tile || IsDetached(tile))
            throw new NotSupportedException("Actual owned UI component is absent or retired: " + path);
        return tile;
    }
}
