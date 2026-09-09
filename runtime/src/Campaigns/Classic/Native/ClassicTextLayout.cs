namespace OpenNV.Runtime.Campaigns.Classic.Native;

internal sealed record ClassicTextLine(string Text, int Width);

/// <summary>Word wrapping and visible line widths in original bitmap-font pixels.</summary>
internal static class ClassicTextLayout
{
    internal static IReadOnlyList<ClassicTextLine> Wrap(string text, int width, Func<char, int> advance, int letterSpacing)
    {
        if (width <= 0) return [];
        int Measure(string value) => value.Length == 0 ? 0 : value.Sum(advance) - letterSpacing;
        var lines = new List<ClassicTextLine>();
        void Add(string value) => lines.Add(new(value, Measure(value)));
        foreach (var paragraph in text.Replace("\r", "", StringComparison.Ordinal).Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var joined = line.Length == 0 ? word : line + " " + word;
                if (Measure(joined) <= width) { line = joined; continue; }
                if (line.Length != 0) { Add(line); line = ""; }
                // Only an individual word wider than the panel is split.
                // No source character is dropped at a line boundary.
                foreach (var character in word)
                {
                    if (line.Length != 0 && Measure(line + character) > width) { Add(line); line = ""; }
                    line += character;
                }
            }
            Add(line);
        }
        return lines;
    }
}
