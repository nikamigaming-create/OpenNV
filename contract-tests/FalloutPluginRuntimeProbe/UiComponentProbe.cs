using System.Text;
using OpenNV.Runtime.Content;

internal static class UiComponentProbe
{
    internal static void Run()
    {
        var document = FalloutMenuXml.Parse(Encoding.UTF8.GetBytes(
            "<menu name='StartMenu'><rect name='Parent'><rect name='Item'><value>2</value><label>base</label></rect>" +
            "<rect name='Item'><value>3</value></rect><rect name='Mirror'><value><copy src='parent()' trait='childcount'/></value></rect></rect></menu>"));
        var store = FalloutUiComponentStore.Synthetic(document.Elements("menu").Single());
        Require(store.GetFloat("StartMenu/Parent/Item:1/value") == 3, "Indexed UI child lookup failed.");
        Require(store.GetFloat("StartMenu/Parent/*:1/value", alt: true) == 3, "Alt wildcard UI child lookup failed.");
        Require(store.GetFloat("StartMenu/Parent/Mirror/value") == 3, "UI source trait evaluation failed.");
        Require(store.GetString("StartMenu/Parent/Item/label") == "base", "UI string read failed.");
        Require(store.SetFloat("StartMenu/Parent/Item:0/value", 7), "UI float write was rejected.");
        Require(store.GetFloat("StartMenu/Parent/Item/value") == 7, "UI float write did not own the new value.");
        Require(store.SetString("StartMenu/Parent/Item/label", "changed"), "UI string write was rejected.");
        Require(store.GetString("StartMenu/Parent/Item/label") == "changed", "UI string write did not own the new value.");
        Require(store.Unload("StartMenu/Parent/Item:0"), "UI component unload did not resolve its source tile.");
        Require(store.GetFloat("StartMenu/Parent/Item:1/value") == 3 && store.GetString("StartMenu/Parent/Item/label") == string.Empty,
            "Unloaded UI component retained mutable state.");
        Console.WriteLine("OPENNV_UI_COMPONENT_PASS paths=true expressions=true typedState=true unload=true");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
