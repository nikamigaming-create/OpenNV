using System.Xml.Linq;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutMenuPcButton(string Key, string TargetName, XElement? Target);

// These are literal declarations on the expanded source menu. The target may
// be absent: retain that declaration instead of inventing a native action.
internal static class FalloutMenuPcButtons
{
    private const string Prefix = "_PCButton_";

    internal static IReadOnlyList<FalloutMenuPcButton> Read(XElement menu)
    {
        if (menu.Name != "menu") throw new InvalidDataException("PC shortcuts require the expanded source menu.");
        var result = new List<FalloutMenuPcButton>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in menu.Elements().Where(value => value.Name.LocalName.StartsWith(Prefix, StringComparison.Ordinal)))
        {
            if (property.Name.Namespace != XNamespace.None || property.HasElements || property.HasAttributes)
                throw new NotSupportedException("Source PC shortcut requires an unowned expression or namespace.");
            var key = property.Name.LocalName[Prefix.Length..];
            var target = property.Value.Trim();
            if (key.Length == 0 || target.Length == 0)
                throw new InvalidDataException("Source PC shortcut has an empty key or target.");
            if (!keys.Add(key)) throw new InvalidDataException("Source PC shortcut key is duplicated: " + key);
            var targets = menu.DescendantsAndSelf().Where(tile => (string?)tile.Attribute("name") == target).ToArray();
            if (targets.Length > 1) throw new InvalidDataException("Source PC shortcut target is ambiguous: " + target);
            result.Add(new(key, target, targets.SingleOrDefault()));
        }
        return result;
    }
}

internal sealed class FalloutMenuPcButtonBindings<TKey> where TKey : notnull
{
    private readonly Dictionary<TKey, Action> _actions = [];
    internal IReadOnlyList<FalloutMenuPcButton> Declarations { get; }

    internal FalloutMenuPcButtonBindings(XElement menu, Func<string, TKey> keyOwner, Func<FalloutMenuPcButton, Action> actionOwner)
    {
        Declarations = FalloutMenuPcButtons.Read(menu);
        foreach (var declaration in Declarations)
        {
            var key = keyOwner(declaration.Key);
            var action = actionOwner(declaration) ?? throw new InvalidOperationException("Source PC shortcut has no action or refusal owner.");
            if (!_actions.TryAdd(key, action)) throw new InvalidDataException("Source PC shortcut maps to an ambiguous native key.");
        }
    }

    // A returned true acknowledges input dispatch, not a completed gameplay result.
    internal bool TryDispatch(TKey key, bool pressed, bool echo)
    {
        if (!pressed || echo || !_actions.TryGetValue(key, out var action)) return false;
        action();
        return true;
    }
}
