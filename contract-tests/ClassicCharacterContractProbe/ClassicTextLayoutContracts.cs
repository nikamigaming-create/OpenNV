using OpenNV.Runtime.Campaigns.Classic.Native;

internal static class ClassicTextLayoutContracts
{
    internal static void Run()
    {
        static int Advance(char character) => character == ' ' ? 3 : character == 'i' ? 3 : 7;
        static string[] Lines(string value, int width) => ClassicTextLayout.Wrap(value, width, Advance, 1).Select(line => line.Text).ToArray();
        var title = ClassicTextLayout.Wrap("Bones", 75, Advance, 1).Single();
        Require(title.Width == 34 && title.Text == "Bones", "Centered titles must exclude the final letter spacing.");
        Require(Lines("Bones", 34).SequenceEqual(new[] { "Bones" }), "An exact fit must not wrap.");
        Require(Lines("Knife and ammo", 40).SequenceEqual(new[] { "Knife", "and", "ammo" }), "Normal words must stay together.");
        Require(Lines("Min ST: 2.", 38).SequenceEqual(new[] { "Min", "ST: 2." }), "Item descriptions must retain their stat labels.");
        Require(string.Concat(Lines("ABCDEFGHIJK", 20)) == "ABCDEFGHIJK", "Overlong words must not lose letters.");
        Require(Lines("Title\r\n\r\nBody", 100).SequenceEqual(new[] { "Title", "", "Body" }), "Explicit paragraph spacing changed.");
        Require(Lines("Bones", 0).Length == 0, "A zero-width display cannot contain text.");
        Console.WriteLine("OPENNV_CLASSIC_TEXT_LAYOUT_PASS measuredTitles=1 wrappingCases=6");
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
