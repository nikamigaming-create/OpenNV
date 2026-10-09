using System.Xml.Linq;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Ui;

// Key adaptation preserves the existing source _PCButton convention. It does
// not supply Reset/Continue keys or substitute an action for a missing target.
internal static class NativeMenuPcButtons
{
    internal static FalloutMenuPcButtonBindings<Key> Bind(XElement menu, Func<FalloutMenuPcButton, Action> actionOwner) =>
        new(menu, ReadKey, actionOwner);

    private static Key ReadKey(string token)
    {
        if (!Enum.TryParse<Key>(token, out var key) || key == Key.None || Enum.GetName(key) != token)
            throw new NotSupportedException("Source PC shortcut key has no native owner: " + token);
        return key;
    }
}
