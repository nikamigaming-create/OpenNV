using System.Xml.Linq;

namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutUiComponentStore
{
    // A new native Menu receives a new source component tree. Other menus and
    // their current bindings retain their own independent lifetimes.
    internal FalloutUiComponentBinding RecreateMenuRoot(string menuName)
    {
        if (!_menuDocuments.TryGetValue(menuName, out var document))
            throw new InvalidDataException("Owned UI menu declaration is absent: " + menuName);
        var prefix = menuName + "/";
        bool Belongs(string path) => path.Equals(menuName, StringComparison.OrdinalIgnoreCase) || path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        foreach (var path in _tiles.Keys.Where(Belongs).ToArray()) _tiles.Remove(path);
        foreach (var path in _floatOverrides.Keys.Where(Belongs).ToArray()) _floatOverrides.Remove(path);
        foreach (var path in _stringOverrides.Keys.Where(Belongs).ToArray()) _stringOverrides.Remove(path);
        foreach (var path in _animations.Keys.Where(Belongs).ToArray()) _animations.Remove(path);
        _detachedPaths.RemoveWhere(Belongs);
        _menus[menuName] = new(Build(new XElement(document), null, menuName, 0));
        ++Revision;
        return BindMenuRoot(menuName);
    }
}
