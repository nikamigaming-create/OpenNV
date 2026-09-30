using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Ui;
using System.Xml.Linq;

internal static class UiAnimationProbe
{
    internal static void Run()
    {
        var document = FalloutMenuXml.Parse(Encoding.UTF8.GetBytes("<menu name='StartMenu'><rect name='Animator'>" +
            "<value>7</value><rect name='Mirror'><value><copy src='parent()' trait='value'/></value></rect></rect></menu>"));
        var store = FalloutUiComponentStore.Synthetic(document.Elements().Single());
        const string path = "StartMenu/Animator/value";
        void Advance(double seconds, float expected)
        {
            store.AdvanceAnimations(seconds);
            Require(Math.Abs(store.GetFloat(path) - expected) < 0.00001 &&
                Math.Abs(store.GetFloat("StartMenu/Animator/Mirror/value") - expected) < 0.00001,
                "UI interpolation or dependent source traits read a stale value.");
        }
        store.SetFloatGradual(path, 0, 10, 2);
        Advance(0.5, 2.5f); Advance(1.5, 10);
        var projection = new XElement(document.Elements().Single());
        var tile = projection.Elements().Single();
        Require(NativeOwnedMenuTree.SourcePath(projection, tile) == "StartMenu/Animator" &&
            store.TryFloatOverride("StartMenu/Animator/value", out var projected) && projected == 10 &&
            !store.TryFloatOverride("StartMenu/Animator/Missing/value", out _) &&
            NativeOwnedMenuTree.SourcePath(projection, new XElement("rect", new XAttribute("name", "Detached"))) is null,
            "Native tile projection lost canonical script values or invented a source path.");
        var duplicate = new XElement(tile); projection.Add(duplicate);
        Require(NativeOwnedMenuTree.SourcePath(projection, duplicate) == "StartMenu/Animator:1",
            "Native projection lost duplicate source component ordinals.");
        var revision = store.Revision;
        Advance(100, 10);
        Require(store.Revision == revision, "Finished animation kept advancing its menu state.");
        store.SetFloatGradual(path, 0, 6, 6, 1);
        Advance(0.5, 3); Advance(0.5, 6); Advance(4, 6); Advance(0.5, 3); Advance(0.5, 0); Advance(1, 0);
        store.SetFloatGradual(path, -2, 2, 2, 2);
        Advance(0.5, 0); Advance(0.5, 2); Advance(0.5, 0); Advance(0.5, -2); Advance(100.5, 0);
        store.SetFloatGradual(path, 0, 4, 2, 3);
        Advance(1, 2); Advance(1, 0); Advance(0.5, 1);
        store.SetFloatGradual(path);
        Advance(10, 1);
        store.SetFloatGradual(path, 0, 10, 2);
        store.SetFloat(path, 100);
        Advance(1, 5); // Only the explicit gradual command stops the clock.
        store.SetFloatGradual(path, 9);
        Advance(10, 9);
        foreach (var action in new Action[] { () => store.SetFloatGradual(path, 0, 10, 0),
            () => store.SetFloatGradual(path, float.NaN, 10, 1), () => store.SetFloatGradual(path, 0, 10, 1, 4),
            () => store.AdvanceAnimations(-1), () => store.AdvanceAnimations(double.PositiveInfinity) }) Reject(action);
        Advance(0, 9);
        Require(!store.SetFloatGradual("StartMenu/Missing/value", 0, 10, 1), "Missing source tile created an animation.");
        store.SetFloatGradual(path, -float.MaxValue, float.MaxValue, double.MaxValue, 2);
        store.AdvanceAnimations(1.2e308); store.AdvanceAnimations(1.2e308);
        Require(float.IsFinite(store.GetFloat(path)), "Finite endpoints or repeated elapsed time overflowed interpolation.");
        store.Unload("StartMenu/Animator");
        Advance(100, 0);
        store.Reset();
        Advance(100, 7);
        SignedArguments();
        Console.WriteLine("OPENNV_UI_ANIMATION_PASS modes=4 signedArguments=true sourceDependencies=live stop=true missingTile=false unload=true reset=true finite=true");
    }

    private static void SignedArguments()
    {
        var arrays = new FalloutScriptArrayStore();
        using var scope = arrays.BeginExecution();
        var list = arrays.Construct("array"); arrays.Append(list, 4);
        var values = new FalloutScriptValueContext(name => name == "items" ? list : name == "offset" ? 2 :
            throw new InvalidDataException("Unknown signed argument."), (_, _) => throw new InvalidOperationException(), Arrays: arrays);
        var args = FalloutGameModeProgram.ResolveCommandArguments(FalloutGameModeProgram.Tokens(
            "\"StartMenu/Animator/value\" -0.025 +0.025 0.15 2 -offset -(offset + 1) -items[0]"), values);
        Require(args.SequenceEqual(["\"StartMenu/Animator/value\"", "-0.025", "0.025", "0.15", "2", "-2", "-3", "-4"]),
            "Signed bare/grouped/indexed command operands changed arity, order or value.");
        Reject(() => FalloutGameModeProgram.ResolveCommandArguments(FalloutGameModeProgram.Tokens("-(items)"), values));
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is NotSupportedException or InvalidDataException or ArgumentException) { return; }
        throw new InvalidOperationException("Undefined UI animation behavior was admitted.");
    }
}
