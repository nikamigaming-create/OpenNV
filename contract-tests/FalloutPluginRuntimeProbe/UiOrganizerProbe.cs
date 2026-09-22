using System.Text;
using System.Xml.Linq;
using OpenNV.Runtime.Content;

internal static class UiOrganizerProbe
{
    internal static void Run()
    {
        var source = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["uio/supported.txt"] = Encoding.UTF8.GetBytes(
                "mcm\\mcm.xml:startmenu\n?The Mod Configuration Menu.esp || ?Project Nevada - Core.esm\n" +
                "jam\\jam.xml:hudmainmenu\nfalse\n"),
            ["uio/public/example.txt"] = Encoding.UTF8.GetBytes(
                "extra\\extra.xml:StartMenu:Branch\n?The Mod Configuration Menu.esp\n"),
            ["menus/prefabs/mcm/mcm.xml"] = [],
            ["menus/prefabs/extra/extra.xml"] = [],
        };
        var organizer = new FalloutUiOrganizer(
            path => source.TryGetValue(path, out var bytes) ? bytes : null,
            ["The Mod Configuration Menu.esp"],
            directory => directory.Equals("uio/public", StringComparison.OrdinalIgnoreCase)
                ? ["uio/public/example.txt"] : []);
        var document = FalloutMenuXml.Parse(Encoding.UTF8.GetBytes(
            "<menu name='StartMenu'><rect name='Branch'/></menu>"));
        organizer.Apply("menus/options/start_menu.xml", document);
        var root = document.Elements().Single();
        var rootIncludes = root.Elements("include").Select(element => (string)element.Attribute("src")!).ToArray();
        var branchIncludes = root.Descendants().Single(element => (string?)element.Attribute("name") == "Branch")
            .Elements("include").Select(element => (string)element.Attribute("src")!).ToArray();
        Require(rootIncludes.SequenceEqual(["mcm\\mcm.xml"]) && branchIncludes.SequenceEqual(["extra\\extra.xml"]),
            "UI organizer did not inject source fragments at the declared target tiles.");

        var inactive = new FalloutUiOrganizer(
            path => source.TryGetValue(path, out var bytes) ? bytes : null,
            ["Other.esp"],
            directory => ["uio/public/example.txt"]);
        var inactiveDocument = FalloutMenuXml.Parse(Encoding.UTF8.GetBytes("<menu name='StartMenu'/>"));
        inactive.Apply("menus/options/start_menu.xml", inactiveDocument);
        Require(!inactiveDocument.Descendants("include").Any(), "UI organizer admitted an inactive plugin condition.");
        var corrected = FalloutMenuXml.Parse(Encoding.UTF8.GetBytes("<rect><_KeyInut>0</_KeyInput></rect>"));
        Require(corrected.Descendants("_KeyInput").Single().Value.Trim() == "0",
            "The owned MCM tile typo was not normalized at the XML source boundary.");

        Reject(() => new FalloutUiOrganizer(_ => Encoding.UTF8.GetBytes("bad\\bad.xml:StartMenu\n?\n"), [], _ => [])
            .Apply("menus/options/start_menu.xml", FalloutMenuXml.Parse(Encoding.UTF8.GetBytes("<menu name='StartMenu'/>"))));
        Console.WriteLine("OPENNV_UI_ORGANIZER_PASS sourceInjection=true targetScope=true conditions=true inactiveRejected=true");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Invalid UI organizer condition was accepted.");
    }
}
