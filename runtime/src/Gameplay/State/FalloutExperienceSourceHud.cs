using System.Xml.Linq;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutExperienceSourceHudHandoff(Guid PreviousProcess, Guid PreviousFactory,
    Guid CurrentProcess, Guid CurrentFactory, Guid? RefusedFrameProcess = null, Guid? RefusedFrameFactory = null);
internal sealed record FalloutExperienceSourceFrameRefusal(Guid Observation, Guid Process, Guid OwnerFactory,
    FalloutExperienceClockReading DeliveredClock, FalloutExperienceNotificationAdmission Admission,
    bool LevelRequestPending, bool CharacterGenerationEnded, string Boundary);
internal sealed record FalloutExperienceSourceHudSnapshot(string Schema, string Contract, string Engine,
    string Stack, Guid Process, Guid Factory, Guid UiFactory, long UiEpoch, Guid HudFactory,
    FalloutPlayerControlDisplayRead Display, Guid Initialization, uint ConstructorWallTick,
    int InitialQueueItems, ushort InitialSuppression, IReadOnlyList<FalloutUiComponentValues> Tiles,
    FalloutExperienceSourceHudHandoff? ColdHandoff = null,
    FalloutPlayerControlHudTimerConstructor? LocalTimerConstructor = null,
    FalloutExperienceSourceFrameRefusal? FrameRefusal = null);

// The actual source HUD and its retained children exist independently of a
// rendering view. This borrows their completed constructor; it does not show
// a hidden menu, consume a notice, advance a timer or manufacture a draw.
internal sealed class FalloutExperienceSourceHud
{
    internal const string Schema = "opennv-experience-source-HUD/v1";
    internal const string FrameBoundary = "source-experience-current-UI-frame-and-gradual-value-owner-unbound";
    private readonly FalloutExperienceHudDeclaration _declaration;
    private readonly string _contract;
    private readonly FalloutUiComponentStore _ui;
    private readonly FalloutPlayerControlHudState _hud;
    private readonly FalloutPlayerControlDisplayState _display;
    private readonly IReadOnlyList<FalloutUiComponentBinding> _tiles;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly Guid _factory = Guid.NewGuid();
    private readonly FalloutPlayerControlHudTimerConstructor _timerConstructor;
    private readonly FalloutExperienceSourceHudHandoff? _cold;
    private FalloutExperienceSourceFrameRefusal? _frameRefusal;
    internal string Contract => _contract;
    internal string Engine => _declaration.ExecutableSha256;
    internal Guid Factory => _factory;
    internal Guid Process => _hud.Process;
    internal string Stack => _hud.Stack;
    internal float ReadFloat(string name, string trait)
    {
        RequireCurrent();
        var index = Array.IndexOf(FalloutExperienceHudSource.Tiles, name);
        if (index < 0) throw new InvalidDataException("XP source read selected a foreign child.");
        return _ui.ReadFloat(_tiles[index + 1], trait);
    }
    internal string ReadString(string name)
    {
        RequireCurrent();
        var index = Array.IndexOf(FalloutExperienceHudSource.Tiles, name);
        if (index < 0) throw new InvalidDataException("XP source string read selected a foreign child.");
        return _ui.GetString(_tiles[index + 1].Path + "/string");
    }
    internal float ReadRootFloat(string trait) { RequireCurrent(); return _ui.ReadFloat(_tiles[0], trait); }
    internal FalloutPlayerControlDisplayRead ReadDisplay() { RequireCurrent(); return _display.Read(); }

    internal FalloutExperienceSourceHud(FalloutExperienceHudSource source, FalloutUiComponentStore ui,
        FalloutPlayerControlHudState hud, FalloutPlayerControlDisplayState display,
        FalloutExperienceSourceHudSnapshot? saved = null)
        : this(source.Declaration, source.Contract, source.Menu, ui, hud, display, saved) { }

    internal FalloutExperienceSourceHud(FalloutExperienceHudDeclaration declaration, string contract, XElement menu,
        FalloutUiComponentStore ui, FalloutPlayerControlHudState hud, FalloutPlayerControlDisplayState display,
        FalloutExperienceSourceHudSnapshot? saved = null)
    {
        declaration.Validate(); ArgumentNullException.ThrowIfNull(menu); ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(hud); ArgumentNullException.ThrowIfNull(display);
        _declaration = declaration; _contract = contract; _ui = ui; _hud = hud; _display = display;
        if (declaration.ExecutableSha256 != FalloutSourceMainFamily.Fallout3 ||
            hud.Source.Controls.Main.EngineSha256 != declaration.ExecutableSha256 ||
            !FalloutAdvancementRuntimeReceipt.Digest(contract) || (string?)menu.Attribute("name") != FalloutPlayerControlHudSource.Menu)
            throw new InvalidDataException("Experience source publication selected a foreign actual HUD constructor.");
        var root = ui.BindMenuRoot(FalloutPlayerControlHudSource.Menu);
        _tiles = new[] { root }.Concat(FalloutExperienceHudSource.Tiles.Select(name =>
            ui.BindComponent(Path(menu.DescendantsAndSelf().Single(tile => (string?)tile.Attribute("name") == name))))).ToArray();
        RequireCurrent();
        _timerConstructor = hud.RequireLocalTimerConstructor();
        if (saved is null) return;
        Validate(saved);
        if (saved.Contract != Contract || saved.Engine != Engine || saved.Stack != Stack || saved.Process == Process ||
            saved.Initialization != RequiredHud().Initialization!.Invocation || saved.LocalTimerConstructor != _timerConstructor ||
            !saved.Tiles.Select(tile => tile.Path).SequenceEqual(_tiles.Select(tile => tile.Path)))
            throw new InvalidDataException("Cold XP source publication changed its exact constructor, source or child graph.");
        var current = ui.CaptureComponentValues(_tiles);
        for (var index = 0; index < _tiles.Count; ++index)
        {
            // HUD/control restoration already imports the shared root and
            // meter. Verify those same values; never replay them twice.
            if (index <= 1)
            {
                if (!SameValues(current[index], saved.Tiles[index]))
                    throw new InvalidDataException("Cold XP publication replaced the shared source HUD/control tile state.");
            }
            else ui.RestoreComponentValues(_tiles[index], saved.Tiles[index]);
        }
        _frameRefusal = saved.FrameRefusal;
        _cold = new(saved.Process, saved.Factory, Process, _factory, _frameRefusal?.Process, _frameRefusal?.OwnerFactory);
    }

    internal void RequireCurrent()
    {
        if (_thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("XP source HUD changed its actual publication thread.");
        var hud = RequiredHud(); var display = _display.Read();
        if (hud.UiFactory != _ui.Factory || hud.UiEpoch != _ui.Epoch || display.Process != Process ||
            hud.Display is not { } initial || display.Width != initial.Width || display.Height != initial.Height ||
            display.Wide != initial.Wide || display.LogicalWidthBits != initial.LogicalWidthBits ||
            display.LogicalHeightBits != initial.LogicalHeightBits)
            throw new InvalidOperationException("XP source HUD lost its actual shared XML, retained dimensions or constructor initialization.");
        foreach (var tile in _tiles) _ui.RequireComponent(tile);
    }
    private FalloutPlayerControlHudSnapshot RequiredHud()
    {
        var hud = _hud.Capture();
        if (hud.Retired || !hud.InitializationReturned || hud.Initialization is not { Returned: true } ||
            hud.Failure is not null || hud.ColdBindingFailure is not null)
            throw new NotSupportedException("source-experience-actual-HUD-constructor-not-returned");
        return hud;
    }
    internal string? SaveBlocker
    {
        get
        {
            RequireCurrent();
            if (_frameRefusal is not null) return FrameBoundary;
            return _ui.CaptureComponentValues(_tiles).Any(tile => !tile.Alive || tile.Animated)
                ? "source-experience-current-tile-lifetime-or-gradual-writer-unbound" : null;
        }
    }
    internal FalloutExperienceSourceHudSnapshot Capture()
    {
        RequireCurrent(); var hud = RequiredHud();
        var saved = new FalloutExperienceSourceHudSnapshot(Schema, Contract, Engine, Stack, Process, _factory,
            _ui.Factory, _ui.Epoch, hud.Factory, _display.Read(), hud.Initialization!.Invocation,
            _timerConstructor.OriginWallTick, 0, 0, _ui.CaptureComponentValues(_tiles), _cold, _timerConstructor, _frameRefusal);
        Validate(saved); return saved;
    }
    internal void EnterCurrentFrame(FalloutExperienceClockReading clock, FalloutExperienceNotificationAdmission admission,
        bool levelRequestPending, bool characterGenerationEnded)
    {
        RequireCurrent();
        // This is an actual logical service entry, independent of a renderer.
        // Retain its supplied arguments and first unsupported child without
        // advancing a clock, tile writer, source flag, voice or notice queue.
        _frameRefusal ??= new(Guid.NewGuid(), Process, _factory, clock, admission,
            levelRequestPending, characterGenerationEnded, FrameBoundary);
        throw new NotSupportedException(FrameBoundary);
    }
    internal void RequireHistoricalPublication(FalloutExperienceSourceHudSnapshot saved)
    {
        Validate(saved); var current = Capture();
        if (_cold is not { } cold || cold.PreviousProcess != saved.Process || cold.PreviousFactory != saved.Factory ||
            current.Contract != saved.Contract || current.Engine != saved.Engine || current.Stack != saved.Stack ||
            current.Initialization != saved.Initialization || current.LocalTimerConstructor != saved.LocalTimerConstructor ||
            current.FrameRefusal != saved.FrameRefusal ||
            current.Display.Sequence != saved.Display.Sequence || current.Display.Width != saved.Display.Width ||
            current.Display.Height != saved.Display.Height || current.Display.Wide != saved.Display.Wide ||
            current.Tiles.Count != saved.Tiles.Count || current.Tiles.Where((tile, index) => !SameValues(tile, saved.Tiles[index])).Any())
            throw new InvalidDataException("Cold XP consumer changed its original current HUD publication or raw tile history.");
    }
    internal static void Validate(FalloutExperienceSourceHudSnapshot saved)
    {
        if (saved is null || saved.Schema != Schema || !FalloutAdvancementRuntimeReceipt.Digest(saved.Contract) ||
            saved.Engine != FalloutSourceMainFamily.Fallout3 || string.IsNullOrWhiteSpace(saved.Stack) ||
            saved.Process == Guid.Empty || saved.Factory == Guid.Empty || saved.UiFactory == Guid.Empty || saved.UiEpoch < 0 ||
            saved.HudFactory == Guid.Empty || saved.Initialization == Guid.Empty || saved.InitialQueueItems != 0 ||
            saved.InitialSuppression != 0 || saved.Display is null || saved.Display.Process != saved.Process || saved.Display.Factory == Guid.Empty ||
            saved.Display.Sequence < 0 || saved.Tiles is not { Count: 9 } || saved.Tiles.Any(tile => tile is null) ||
            saved.Tiles.Select(tile => tile.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != saved.Tiles.Count ||
            saved.Tiles[0].Path != FalloutPlayerControlHudSource.Menu || saved.LocalTimerConstructor is not { } timer ||
            timer.OriginWallTick != saved.ConstructorWallTick)
            throw new InvalidDataException("XP source HUD omitted its genuine constructor, dimensions or exact child graph.");
        FalloutPlayerControlHudState.ValidateLocalTimerConstructor(timer);
        for (var index = 0; index < saved.Tiles.Count; ++index)
        {
            var tile = saved.Tiles[index]; FalloutUiComponentStore.ValidateComponentValues(tile);
            if (!tile.Alive || tile.Animated || index > 0 && tile.Path.Split('/')[^1] != FalloutExperienceHudSource.Tiles[index - 1])
                throw new InvalidDataException("XP source HUD admitted a missing, animated or substituted constructor child.");
        }
        var logical = FalloutPlayerControlDisplayState.LogicalDimensions(saved.Display.Width, saved.Display.Height);
        if (saved.Display.Wide != FalloutPlayerControlDisplayState.WideFor(saved.Display.Width, saved.Display.Height) ||
            saved.Display.LogicalWidthBits != BitConverter.SingleToUInt32Bits(logical.Width) ||
            saved.Display.LogicalHeightBits != BitConverter.SingleToUInt32Bits(logical.Height))
            throw new InvalidDataException("XP source HUD changed its retained source display getters.");
        if (saved.ColdHandoff is { } cold && (cold.PreviousProcess == Guid.Empty || cold.PreviousFactory == Guid.Empty ||
            cold.PreviousProcess == cold.CurrentProcess || cold.PreviousFactory == cold.CurrentFactory ||
            cold.CurrentProcess != saved.Process || cold.CurrentFactory != saved.Factory ||
            (cold.RefusedFrameProcess is null) != (cold.RefusedFrameFactory is null) ||
            cold.RefusedFrameProcess is { } refusedProcess && (refusedProcess != saved.FrameRefusal?.Process ||
                cold.RefusedFrameFactory != saved.FrameRefusal?.OwnerFactory)))
            throw new InvalidDataException("XP source HUD cold publication resurrected an old process/child owner.");
        if (saved.FrameRefusal is { } frame && (frame.Observation == Guid.Empty || frame.Process == Guid.Empty || frame.OwnerFactory == Guid.Empty ||
            frame.Boundary != FrameBoundary ||
            (frame.Process != saved.Process || frame.OwnerFactory != saved.Factory) &&
            (saved.ColdHandoff is not { } historical || historical.RefusedFrameProcess != frame.Process || historical.RefusedFrameFactory != frame.OwnerFactory)))
            throw new InvalidDataException("XP source HUD lost its actual entered frame and original refused child.");
    }
    internal static void ValidateAssociation(FalloutExperienceSourceHudSnapshot saved,
        FalloutPlayerControlHudSnapshot hud, FalloutPlayerControlDisplaySnapshot display,
        Guid actualProcess, string stack)
    {
        Validate(saved); ArgumentNullException.ThrowIfNull(hud); ArgumentNullException.ThrowIfNull(display);
        var initial = hud.Display; var hudTiles = hud.Tiles;
        if (saved.Process != actualProcess || saved.Stack != stack || hud.Process != actualProcess || hud.Stack != stack ||
            display.Process != actualProcess || display.Stack != stack || display.Source != hud.Source ||
            saved.HudFactory != hud.Factory || saved.UiFactory != hud.UiFactory || saved.UiEpoch != hud.UiEpoch ||
            hud.Retired || !hud.InitializationReturned || hud.Initialization is not { Returned: true } initialization ||
            initialization.Invocation != saved.Initialization || hud.Failure is not null || hud.ColdBindingFailure is not null ||
            saved.LocalTimerConstructor != hud.LocalTimerConstructor ||
            !display.Available || display.Failure is not null || saved.Display.Factory != display.Factory ||
            saved.Display.Sequence != display.Sequence || saved.Display.Width != display.Width || saved.Display.Height != display.Height ||
            saved.Display.Wide != display.Wide || initial is null || saved.Display.Width != initial.Width ||
            saved.Display.Height != initial.Height || saved.Display.Wide != initial.Wide ||
            saved.Display.LogicalWidthBits != initial.LogicalWidthBits || saved.Display.LogicalHeightBits != initial.LogicalHeightBits ||
            hudTiles is null)
            throw new InvalidDataException("XP source publication differs from its actual saved HUD, display or current process.");
        foreach (var tile in saved.Tiles.Take(2))
        {
            var shared = hudTiles.Where(value => value.Path == tile.Path).ToArray();
            if (shared is not [var current] || !SameValues(tile, current))
                throw new InvalidDataException("XP source publication changed the raw shared HUD root or meter state.");
        }
    }
    private static string Path(XElement tile) => string.Join('/', tile.AncestorsAndSelf().Reverse()
        .Select(node => (string?)node.Attribute("name")).Where(name => name is not null));
    private static bool SameValues(FalloutUiComponentValues first, FalloutUiComponentValues second) =>
        first.Path == second.Path && first.Alive == second.Alive && first.Animated == second.Animated &&
        first.Floats.OrderBy(value => value.Trait, StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(second.Floats.OrderBy(value => value.Trait, StringComparer.OrdinalIgnoreCase)) &&
        first.Strings.OrderBy(value => value.Trait, StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(second.Strings.OrderBy(value => value.Trait, StringComparer.OrdinalIgnoreCase));
}
