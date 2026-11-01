using System.Numerics;
using System.Xml.Linq;
using OpenNV.Runtime.Content;

internal static partial class SharedConditionContracts
{
    private static void VatsLabelLayoutContracts()
    {
        Require(FalloutVatsLabelLayout.ReferenceSize(new(3840, 2160)) == new Vector2((float)(3840d / 2160 * 960), 960) &&
            FalloutVatsLabelLayout.ReferenceSize(new(1080, 1920)) == new Vector2(1280, (float)(1920d / 1080 * 1280)) &&
            FalloutVatsLabelLayout.ReferenceSize(new(1000, 1000)) == new Vector2(1280, 960),
            "VATS source virtual extent lost its landscape/portrait/square renderer rules.");
        var texture = XElement.Parse("<filename><copy src='me()' trait='mouseover'/><add>1</add><copy src='me()' trait='_art_'/></filename>");
        foreach (var hover in new[] { 0f, 1f })
            Require(FalloutMenuXml.Filename(texture, (_, key) => key == "mouseover" ? hover : throw new InvalidOperationException(),
                (_, key) => key switch { "_art_1" => "normal.dds", "_art_2" => "hover.dds", _ => throw new InvalidOperationException() }) ==
                (hover == 0 ? "normal.dds" : "hover.dds"), "Owned texture selector arithmetic did not follow changing source hover state.");
        Require(FalloutMenuXml.Filename(XElement.Parse("<filename><copy src='parent()' trait='icon'/></filename>"),
            (_, _) => throw new InvalidOperationException(), (source, key) => source == "parent()" && key == "icon" ? "icon.dds" : "wrong") == "icon.dds",
            "Direct texture copying was incorrectly treated as a numeric selector.");
        Reject(() => FalloutMenuXml.Filename(texture, (_, _) => .5f, (_, _) => "wrong.dds"));
        var samples = new FalloutVatsLabelSample[]
        {
            new(0, new(640, 480), Vector2.Zero), new(1, new(640, 480), Vector2.Zero),
            new(3, new(320, 480), Vector2.Zero), new(4, new(960, 480), Vector2.Zero)
        };
        var result = FalloutVatsLabelLayout.Calculate(new(1280, 960), new(15, 15), 127, samples);
        Require(result[0] == new FalloutVatsLabelPlacement(new(640, 480), 6) &&
            result[1] == new FalloutVatsLabelPlacement(new(640, 380), 5) &&
            result[2] == new FalloutVatsLabelPlacement(new(220, 430), 4) &&
            result[3] == new FalloutVatsLabelPlacement(new(1060, 430), 1),
            "VATS percentage placement lost part-family displacement, center equality or source justification.");
        var edge = FalloutVatsLabelLayout.Calculate(new(1280, 960), new(15, 15), 127,
            [new(0, new(-100, 2000), Vector2.Zero)]).Single();
        Require(edge.Position == new Vector2(130, 853), "VATS percentage clamp did not reserve source safe-zone/AP extents.");
        var hidden = FalloutVatsLabelLayout.Calculate(new(1280, 960), new(15, 15), 127,
            [new(0, null, new(5000, 5000))]).Single();
        Require(hidden.Position == new Vector2(5000, 5000), "Unprojected VATS data invented a point or reset retained tile position.");
        var overlap = FalloutVatsLabelLayout.Calculate(new(1280, 960), new(15, 15), 127,
            [new(0, new(500, 500), Vector2.Zero), new(13, new(500, 500), Vector2.Zero)]);
        Require(overlap.All(value => float.IsFinite(value.Position.X) && float.IsFinite(value.Position.Y)) &&
            overlap[0].Position.X < 500 && overlap[1].Position.X > 500 &&
            Math.Abs(overlap[1].Position.X - overlap[0].Position.X - MathF.Sqrt(32500)) < .001f,
            "Coincident VATS labels did not separate with the source bounded-pair rule.");
        Reject(() => FalloutVatsLabelLayout.Calculate(new(0, 960), new(15, 15), 127, samples));
        Reject(() => FalloutVatsLabelLayout.Calculate(new(1280, 960), new(15, 15), 127,
            [new(0, new(float.NaN, 0), Vector2.Zero)]));
    }
}
