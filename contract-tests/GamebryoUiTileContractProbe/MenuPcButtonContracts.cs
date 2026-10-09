using System.Text;
using System.Xml.Linq;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Ui;

internal static class MenuPcButtonContracts
{
    internal static void Run()
    {
        AlternateSourceKeys();
        RefusedAndAbsentActions();
        InvalidDeclarations();
        CommittedCallbackFailure();
        Console.WriteLine("OPENNV_MENU_PC_BUTTON_CONTRACT_PASS sourceXml=true alternateKeys=true sharedActions=true noFallback=true missingBackRetained=true unownedRefusal=true echoRelease=true invalid=true committedCallbackFailure=true");
    }

    private static void AlternateSourceKeys()
    {
        var source = Menu("""
            <_PCButton_Z> ResetAction </_PCButton_Z>
            <_PCButton_X> ContinueAction </_PCButton_X>
            <rect name="ContinueAction" />
            <rect name="ResetAction" />
            """);
        var selected = new List<string> { "First", "Second" };
        IReadOnlyList<string>? accepted = null;
        var resets = 0; var submissions = 0; var submitted = false;
        Action reset = () => { if (submitted) return; resets++; selected.Clear(); };
        Action submit = () => { if (submitted) return; submitted = true; submissions++; accepted = selected.ToArray(); };
        var actions = new Dictionary<string, Action> { ["ResetAction"] = reset, ["ContinueAction"] = submit };
        var bindings = NativeMenuPcButtons.Bind(source, declaration => actions[declaration.TargetName]);
        Check(bindings.Declarations.Select(value => value.Key).SequenceEqual(new[] { "Z", "X" }) &&
            bindings.Declarations.All(value => value.Target is not null && (string?)value.Target.Attribute("name") == value.TargetName),
            "PC declarations lost their actual source key, target identity or source order.");
        Check(!bindings.TryDispatch(Key.R, true, false) && !bindings.TryDispatch(Key.A, true, false) &&
            !bindings.TryDispatch(Key.Z, false, false) && !bindings.TryDispatch(Key.Z, true, true) &&
            resets == 0 && submissions == 0 && selected.Count == 2,
            "Old hardcoded keys, release or echo invoked an alternate source action.");
        Check(bindings.TryDispatch(Key.Z, true, false) && resets == 1 && selected.Count == 0 && accepted is null,
            "The alternate source Reset did not edit only its actual draft.");
        // The same delegate is used by the mouse target and source shortcut.
        selected.Add("Third"); reset();
        Check(resets == 2 && selected.Count == 0 && accepted is null, "Mouse/source Reset callbacks diverged.");
        selected.Add("Second");
        Check(bindings.TryDispatch(Key.X, true, false) && submissions == 1 && accepted is not null &&
            accepted.SequenceEqual(new[] { "Second" }), "Alternate source Continue invoked another callback or retained a stale draft.");
        bindings.TryDispatch(Key.X, true, false); bindings.TryDispatch(Key.Z, true, false);
        Check(submissions == 1 && resets == 2, "A completed callback replayed submission or changed the committed draft.");

        // An independently authored source can map multiple declared keys to
        // one real action. No positional Reset/Continue convention is used.
        var aliases = NativeMenuPcButtons.Bind(Menu("""
            <_PCButton_G>SharedAction</_PCButton_G>
            <_PCButton_H>SharedAction</_PCButton_H>
            <rect name="SharedAction" />
            """), _ => () => resets++);
        Check(aliases.TryDispatch(Key.G, true, false) && aliases.TryDispatch(Key.H, true, false) && resets == 4,
            "Two source-declared keys for the same target were silently dropped or rebound.");
    }

    private static void RefusedAndAbsentActions()
    {
        var source = Menu("""
            <_PCButton_Q>OwnedAction</_PCButton_Q>
            <_PCButton_B>AbsentBackAction</_PCButton_B>
            <rect name="OwnedAction" />
            """);
        var calls = 0;
        var bindings = NativeMenuPcButtons.Bind(source, declaration => declaration.Target is not null
            ? () => calls++
            : () => throw new NotSupportedException("Source Back transition has no actual target or owner."));
        Check(bindings.Declarations is { Count: 2 } && bindings.Declarations[1] is { TargetName: "AbsentBackAction", Target: null },
            "An absent Back target was dropped or a native target was invented.");
        Check(bindings.TryDispatch(Key.Q, true, false) && calls == 1, "An unused missing target prevented the admitted source action.");
        Reject(() => bindings.TryDispatch(Key.B, true, false));
        Check(calls == 1 && !bindings.TryDispatch(Key.E, true, false), "Missing Back created a return route or inherited a fixed physical key.");
        var presentUnowned = NativeMenuPcButtons.Bind(Menu("""
            <_PCButton_B>OtherTransition</_PCButton_B>
            <rect name="OtherTransition" />
            """), _ => () => throw new NotSupportedException("Original menu transition has no current owner."));
        Reject(() => presentUnowned.TryDispatch(Key.B, true, false));
        Check(calls == 1, "A present source tile was mistaken for owned transition execution.");
        var noShortcuts = NativeMenuPcButtons.Bind(Menu("<rect name=\"OwnedAction\" />"), _ => () => calls++);
        Check(noShortcuts.Declarations.Count == 0 && !noShortcuts.TryDispatch(Key.R, true, false) &&
            !noShortcuts.TryDispatch(Key.A, true, false) && calls == 1,
            "A source with no shortcut declarations inherited manufactured Reset/Continue bindings.");
    }

    private static void InvalidDeclarations()
    {
        foreach (var body in new[]
        {
            "<_PCButton_Z></_PCButton_Z><rect name=\"Target\" />",
            "<_PCButton_>Target</_PCButton_><rect name=\"Target\" />",
            "<_PCButton_Z>Target</_PCButton_Z><_PCButton_Z>Target</_PCButton_Z><rect name=\"Target\" />",
            "<_PCButton_Z>Target</_PCButton_Z><rect name=\"Target\" /><rect name=\"Target\" />",
            "<_PCButton_Z><copy src=\"me()\" trait=\"_UnknownTarget\" /></_PCButton_Z><rect name=\"Target\" />",
            "<_PCButton_Z src=\"me()\" trait=\"_UnknownTarget\">Target</_PCButton_Z><rect name=\"Target\" />",
            "<_PCButton_NotAKnownNativeKey>Target</_PCButton_NotAKnownNativeKey><rect name=\"Target\" />",
            "<_PCButton_90>Target</_PCButton_90><rect name=\"Target\" />",
            "<_PCButton_None>Target</_PCButton_None><rect name=\"Target\" />"
        })
        {
            var calls = 0;
            Reject(() => NativeMenuPcButtons.Bind(Menu(body), _ => () => calls++));
            Check(calls == 0, "Malformed/unknown source admission invoked an action to manufacture acceptance.");
        }
        Reject(() => FalloutMenuPcButtons.Read(XElement.Parse("<rect name=\"NotAMenu\" />")));
        Reject(() => new FalloutMenuPcButtonBindings<int>(Menu("""
            <_PCButton_Z>Target</_PCButton_Z><_PCButton_X>Target</_PCButton_X><rect name="Target" />
            """), _ => 1, _ => () => { }));
    }

    private static void CommittedCallbackFailure()
    {
        var committed = false; var commits = 0;
        var failure = new IOException("The actual callback failed after its commit.");
        var bindings = NativeMenuPcButtons.Bind(Menu("""
            <_PCButton_X>Submit</_PCButton_X><rect name="Submit" />
            """), _ => () =>
        {
            if (committed) return;
            committed = true; commits++;
            throw failure;
        });
        try { bindings.TryDispatch(Key.X, true, false); throw new InvalidOperationException("Callback failure disappeared."); }
        catch (IOException error) { Check(ReferenceEquals(error, failure), "Source dispatch replaced the genuine callback failure."); }
        Check(committed && commits == 1, "A callback failure rolled back or lost its actual committed prefix.");
        bindings.TryDispatch(Key.X, true, false);
        Check(commits == 1, "The callback's current submission owner was bypassed after committed failure.");
    }

    private static XElement Menu(string body) => FalloutMenuXml.Parse(Encoding.UTF8.GetBytes(
        "<menu name=\"AuthoredMenu\">" + body + "</menu>")).Elements("menu").Single();

    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Unowned source menu input was accepted.");
    }
}
