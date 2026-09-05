using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace OpenNV.Runtime.Content;

// Engine entity names remain tokens; XML never resolves external entities.
internal static class FalloutMenuXml
{
    internal static XElement Read(string path)
    {
        var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned menu source is absent.");
        if (!source.TryRead(path, null, out var bytes, out _)) throw new FileNotFoundException("Owned menu XML is missing.", path);
        return Parse(bytes);
    }

    internal static XElement Parse(ReadOnlySpan<byte> bytes)
    {
        var text = Regex.Replace(Encoding.UTF8.GetString(bytes), @"<!--.*?-->", "", RegexOptions.Singleline);
        // The owned tile grammar admits an empty property whose opening tag
        // omits its final bracket. Keep it empty; never invent a trait value.
        text = Regex.Replace(text, @"<([A-Za-z_][A-Za-z0-9_.-]*)</\1\s*>", "<$1></$1>");
        text = Regex.Replace(text, @"&(-?[A-Za-z_][A-Za-z0-9_]*);", match => "entity_" + match.Groups[1].Value);
        return XElement.Parse("<source>" + text + "</source>");
    }

    internal static float Number(XElement property, Func<string, string, float> reference)
        => Number(property, reference, 0);

    private static float Number(XElement property, Func<string, string, float> reference, float accumulator)
    {
        if (property.Attribute("src") is { } source)
        {
            var trait = (string?)property.Attribute("trait") ?? throw new InvalidDataException("Menu reference has no trait.");
            if (trait.EndsWith('_'))
            {
                if (!float.IsFinite(accumulator) || accumulator != MathF.Truncate(accumulator))
                    throw new InvalidDataException("Menu indexed trait requires an integral accumulator.");
                trait += accumulator.ToString(CultureInfo.InvariantCulture);
            }
            return reference(source.Value, trait);
        }
        if (!property.HasElements)
        {
            var token = property.Value.Trim();
            if (float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var literal) && float.IsFinite(literal)) return literal;
            return token switch
            {
                "entity_true" => 1,
                "entity_false" or "entity_console" or "entity_left" => 0,
                "entity_center" => 1,
                "entity_right" => 2,
                _ => throw new NotSupportedException($"Menu numeric token has no owner: {token}"),
            };
        }
        var value = 0.0f;
        foreach (var operation in property.Elements())
        {
            var operand = Number(operation, reference, value);
            value = operation.Name.LocalName switch
            {
                "copy" => operand,
                "add" => value + operand,
                "sub" => value - operand,
                "mul" or "mult" => value * operand,
                "min" => Math.Min(value, operand),
                "max" => Math.Max(value, operand),
                "div" when operand != 0 => value / operand,
                "eq" => value == operand ? 1 : 0,
                "neq" => value != operand ? 1 : 0,
                "gt" => value > operand ? 1 : 0,
                "gte" => value >= operand ? 1 : 0,
                "lt" => value < operand ? 1 : 0,
                "lte" => value <= operand ? 1 : 0,
                "and" => value != 0 && operand != 0 ? 1 : 0,
                "or" => value != 0 || operand != 0 ? 1 : 0,
                "not" => operand == 0 ? 1 : 0,
                "onlyif" => operand != 0 ? value : 0,
                "onlyifnot" => operand == 0 ? value : 0,
                _ => throw new NotSupportedException($"Menu operator has no owner: {operation.Name}"),
            };
        }
        if (!float.IsFinite(value)) throw new InvalidDataException("Owned menu expression produced a non-finite result.");
        return value;
    }

    internal static string String(XElement property, FalloutPluginStack records)
    {
        if (property.HasElements) throw new NotSupportedException("Menu string expression needs a tile-state owner.");
        var token = property.Value.Trim();
        if (!token.StartsWith("entity_-", StringComparison.Ordinal)) return token;
        return FalloutGameSettingStrings.Read(records, token[8..]);
    }

    internal static XElement Expand(XElement source)
    {
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        XElement Build(XElement tile)
        {
            var result = new XElement(tile.Name, tile.Attributes());
            void Merge(XElement value)
            {
                if (value.Attribute("name") is { } name)
                {
                    var existing = result.Elements().SingleOrDefault(child => child.Name == value.Name && (string?)child.Attribute("name") == name.Value);
                    if (existing is null) result.Add(new XElement(value));
                    else
                    {
                        var combined = new XElement(value.Name, value.Attributes(), existing.Elements().Select(child => new XElement(child)));
                        foreach (var property in value.Elements())
                        {
                            combined.Elements(property.Name).Where(child => (string?)child.Attribute("name") == (string?)property.Attribute("name")).Remove();
                            combined.Add(new XElement(property));
                        }
                        existing.ReplaceWith(Build(combined));
                    }
                }
                else { result.Elements(value.Name).Remove(); result.Add(new XElement(value)); }
            }
            foreach (var include in tile.Elements("include"))
            {
                var path = "menus/prefabs/" + include.Attribute("src")!.Value;
                if (!active.Add(path)) throw new InvalidDataException("Owned menu prefab cycle.");
                foreach (var value in Build(Read(path)).Elements()) Merge(value);
                active.Remove(path);
            }
            foreach (var value in tile.Elements().Where(value => value.Name != "include"))
                Merge(value.Attribute("name") is null ? value : Build(value));
            return result;
        }
        return Build(source);
    }
}
