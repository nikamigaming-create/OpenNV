using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// This owner writes the actual source XML graph. Native HUD nodes only read
// it later; a window, renderer or delayed presentation is not HUD presence.
internal sealed partial class FalloutPlayerControlHudState
{
    private readonly FalloutPlayerControlHudSource _source;
    private readonly string _stack;
    private readonly Guid _process, _factory = Guid.NewGuid();
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly FalloutUiComponentStore _ui;
    private readonly FalloutPlayerThreadContextState _threadContext;
    private readonly IFalloutPlayerControlHudContext _context;
    private readonly FalloutUiComponentBinding _root, _message;
    private readonly FalloutUiComponentBinding[] _visibility;
    private readonly FalloutIniValue _safeX, _safeY, _wideX, _wideY;
    private readonly HashSet<Guid> _invocations = [];
    private readonly List<FalloutPlayerControlHudLifetime> _lifetimes = [];
    private uint _menuMode = 4, _allowed;
    private int _mode = 4, _messageHandle = -1;
    private uint? _visibilityMask;
    private byte _subtitle, _visibilityChanged;
    private Guid? _pendingMessage;
    private long _sequence, _reentry;
    private bool _writing, _displayInitialized, _initializationReturned, _retired;
    private FalloutPlayerControlDisplayRead? _display;
    private FalloutPlayerControlHudCall? _call;
    private FalloutPlayerControlHudInitialization? _initialization;
    private string? _failure;
    private FalloutPlayerControlHudColdHandoff? _cold;
    private IReadOnlyList<FalloutPlayerControlHudTransition>? _historicalTransitions;
    private string? _coldBindingFailure;
    private FalloutPlayerControlHudTimerConstructor? _localTimerConstructor;

    internal Guid Factory => _factory;
    internal FalloutPlayerControlHudSource Source => _source;
    internal string Stack => _stack;
    internal Guid Process => _process;
    internal string? SaveBlocker => _retired ? "source-HUD-control-retired" : _writing ? "source-HUD-control-writer-entered" : _failure ?? _coldBindingFailure ??
        (!_initializationReturned ? "source-HUD-original-initialization-not-returned" :
        _ui.CaptureComponentValues(CurrentTiles()).Any(tile => !tile.Alive || tile.Animated) ? "source-HUD-current-XML-lifetime-or-gradual-writer-unbound" : null);

    internal FalloutPlayerControlHudState(FalloutPlayerControlHudSource source, string stack, Guid process,
        FalloutUiComponentStore ui, FalloutNumericIniSettings settings, FalloutPlayerThreadContextState threadContext,
        IFalloutPlayerControlHudContext context, FalloutPlayerControlHudSnapshot? saved = null,
        Func<uint>? readConstructorWallTick = null)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        ArgumentNullException.ThrowIfNull(ui); ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(threadContext); ArgumentNullException.ThrowIfNull(context);
        if (process == Guid.Empty || context.Source != source || context.Stack != stack || context.Process != process || context.Factory == Guid.Empty ||
            !settings.Source.StartsWith(stack + ":", StringComparison.Ordinal) || !settings.Source.EndsWith(":" + source.Controls.Main.EngineSha256, StringComparison.Ordinal))
            throw new InvalidDataException("Source HUD constructor selected foreign declarations, XML or current process.");
        _source = source; _stack = stack; _process = process; _ui = ui; _threadContext = threadContext; _context = context;
        context.RequireCurrent();
        // The independent HUD timer is constructed before menu publication.
        // A later driver or render callback cannot supply this original read.
        // Cold history preserves the constructor receipt without sampling it.
        if (saved is null && readConstructorWallTick is not null)
            _localTimerConstructor = new(_process, _factory, readConstructorWallTick(), 0, 0, 0, 0, 0, 0);
        _root = ui.BindMenuRoot(FalloutPlayerControlHudSource.Menu);
        context.PublishMenu(_factory, ui, _root);
        _visibility = FalloutPlayerControlHudSource.VisibilityTiles.Select(name => ui.BindComponent(FalloutPlayerControlHudSource.Menu + "/" + name)).ToArray();
        var messagePath = ui.InstantiateTemplate(_visibility[8].Path, "template_justify_center_text");
        _message = ui.BindComponent(messagePath);
        var crosshair = settings.Find(FalloutIniCollection.Prefs, FalloutPlayerControlHudSource.CrosshairSetting);
        if (crosshair is not
            {
                Declaration:
                {
                    Name: FalloutPlayerControlHudSource.CrosshairSetting,
                    Collection: FalloutIniCollection.Prefs, Payload: 1
                }, Number: 0 or 1
            } || string.IsNullOrWhiteSpace(crosshair.Origin))
            throw new InvalidDataException("Source HUD constructor lost its actual Prefs Boolean: " + FalloutPlayerControlHudSource.CrosshairSetting);
        _allowed = 0xffbf | (crosshair.Number == 0 ? 0u : 0x40u);
        _safeX = Integer(FalloutPlayerControlHudSource.SafeXSetting, 15);
        _safeY = Integer(FalloutPlayerControlHudSource.SafeYSetting, 15);
        _wideX = Integer(FalloutPlayerControlHudSource.WideXSetting, 15);
        _wideY = Integer(FalloutPlayerControlHudSource.WideYSetting, 15);
        if (saved is not null) Restore(saved);
        else _lifetimes.Add(new(_process, _factory));
        FalloutIniValue Integer(string name, uint initial)
        {
            var value = settings.Find(FalloutIniCollection.Main, name);
            if (value is null || value.Declaration.Name != name || value.Declaration.Collection != FalloutIniCollection.Main ||
                value.Declaration.Kind != 'i' || value.Declaration.Payload != initial ||
                value.Number is not { } number || number != Math.Truncate(number) || number is < int.MinValue or > int.MaxValue ||
                string.IsNullOrWhiteSpace(value.Origin))
                throw new InvalidDataException("Source HUD constructor lost its actual Main setting: " + name);
            return value;
        }
    }

    internal FalloutPlayerControlHudTimerConstructor RequireLocalTimerConstructor()
    {
        RequireCurrent();
        return _localTimerConstructor ??
            throw new NotSupportedException("source-HUD-independent-local-timer-constructor-unbound");
    }

    internal void Initialize(FalloutPlayerControlDisplayState display, Func<ushort> readCurrentPlayerFlags)
    {
        RequireCurrent(); ArgumentNullException.ThrowIfNull(display); ArgumentNullException.ThrowIfNull(readCurrentPlayerFlags);
        if (_initialization is not null || _displayInitialized || _call is not null)
            throw new InvalidOperationException("Source HUD initialization replayed its original tile stores.");
        var entered = Next();
        _initialization = new(Guid.NewGuid(), entered, entered, "display-read", null, [], false, null);
        try
        {
            var fields = display.Read();
            if (fields.Process != _process || fields.Factory == Guid.Empty)
                throw new InvalidOperationException("Source HUD initialization selected foreign retained display fields.");
            var x = fields.Wide == 0 ? _safeX : _wideX; var y = fields.Wide == 0 ? _safeY : _wideY;
            var width = Truncate(BitConverter.UInt32BitsToSingle(fields.LogicalWidthBits));
            var height = Truncate(BitConverter.UInt32BitsToSingle(fields.LogicalHeightBits));
            var safeX = Truncate((float)x.Number!.Value); var safeY = Truncate((float)y.Number!.Value);
            var meterX = unchecked(width - safeX * FalloutPlayerControlHudSource.SafeZoneScale - FalloutPlayerControlHudSource.MeterInsetX);
            var meterY = unchecked(height - safeY * FalloutPlayerControlHudSource.SafeZoneScale - FalloutPlayerControlHudSource.MeterInsetY);
            _display = fields;
            _initialization = _initialization with { Display = fields, Changed = Next() };
            StoreInitial("x", meterX); StoreInitial("y", meterY); StoreInitial("user7", meterY);
            _displayInitialized = true;
            _initialization = _initialization with { Step = "input-type-five", Changed = Next() };
            Update(Guid.NewGuid(), 5, readCurrentPlayerFlags);
            _initializationReturned = true;
            _initialization = _initialization with { Step = "returned", Returned = true, Changed = Next() };
        }
        catch (Exception error)
        {
            _failure ??= error.Message;
            _initialization = _initialization with { Failure = _failure, Changed = Next() }; throw;
        }
        void StoreInitial(string trait, float value)
        {
            _initialization = _initialization! with { Step = "XP-" + trait, Changed = Next() };
            _ui.StoreFloat(_visibility[12], trait, value);
            var write = new FalloutPlayerControlHudTraitWrite(Next(), _visibility[12].Path, trait,
                BitConverter.SingleToUInt32Bits(value), _ui.Revision);
            _initialization = _initialization with { Writes = _initialization.Writes.Append(write).ToArray(), Changed = _sequence };
        }
    }

    internal void Update(Guid invocation, int requested, Func<ushort> readCurrentPlayerFlags)
    {
        RequireCurrent(); ArgumentNullException.ThrowIfNull(readCurrentPlayerFlags);
        if (!_displayInitialized) throw new NotSupportedException("source-HUD-original-display-and-XP-initialization-unbound");
        if (invocation == Guid.Empty || !_invocations.Add(invocation))
            throw new InvalidOperationException("Source HUD replayed an actual control invocation.");
        _writing = true; var fault = _reentry; var entered = Next();
        _call = new(invocation, requested, requested, entered, entered, "menu-transition", null, null, null, [], false, null, _process, _factory);
        try
        {
            if (_menuMode != 1) EnterMenuTransition(invocation, Check);
            Step("mode-selection");
            var mode = requested == 1 ? _context.ReadDefaultMode(readCurrentPlayerFlags()) : requested; Check();
            if (mode == 21) { mode = _context.ReadForcedSubtitleMode(mode); Check(); }
            var dynamic = false;
            if (mode is not (4 or 5 or 23))
            {
                var flags = readCurrentPlayerFlags(); Check();
                if ((flags & 1) != 0)
                {
                    Step("interface-movement-override");
                    if (_context.ReadInterfaceMovementOverride()) { mode = 2; dynamic = true; }
                    else if (mode != 6) mode = 12;
                    Check();
                }
            }
            _call = _call with { Selected = mode, Changed = Next() };
            Step("visibility-mask");
            uint mask;
            switch (mode)
            {
                case 2:
                case 13:
                case 19:
                case 21:
                    dynamic = true; mask = 0xfdff | ((uint)(~readCurrentPlayerFlags() & 0x20) << 4); Check(); break;
                case 3: case 7: ClearMessage(); mask = 0x100; break;
                case 4: case 5: case 14: case 17: case 18: case 20: case 23: ClearMessage(); mask = 0; break;
                case 6: ClearMessage(); mask = 0x1110; break;
                case 8: ClearMessage(); mask = 0x108; break;
                case 9: ClearMessage(); mask = 0x1000; break;
                case 10: mask = 0x1dbf; break;
                case 11: mask = 0x1fff; break;
                case 12: mask = 0x1514; break;
                case 15: case 16: ClearMessage(); mask = 0x1010; break;
                case 22: ClearMessage(); mask = 8; break;
                default: throw new NotSupportedException("source-HUD-requested-mode-mask-unowned:" + mode);
            }
            _visibilityMask = mask; Next();
            if (dynamic) { Step("dynamic-action-view"); _context.UpdateDynamicActionView(); Check(); }
            _visibilityMask &= _allowed; Next();
            Step("message-suppression"); if (_context.ReadMessageSuppression()) { _visibilityMask &= ~8u; Next(); }
            Check();
            Step("XP-position");
            var meterY = _ui.ReadFloat(_visibility[12], "user7");
            if (mode == 6) meterY = (float)((double)meterY - 330);
            Store(_visibility[12], "y", meterY); Check();
            if ((_mode == 22) != (mode == 22))
            {
                Step("forced-subtitle");
                if (_context.ReadForcedSubtitleAvailable())
                {
                    Check(); _subtitle = (byte)(mode == 22 ? 1 : 0); Next();
                    _context.ChangeForcedSubtitle(mode == 22); Check();
                }
            }
            Step("mode-store"); _mode = mode; Next();
            if (mode is not (19 or 6 or 2 or 5 or 14 or 17 or 9 or 11 or 18)) { _visibilityChanged = 1; Next(); }
            Step("tile-visibility");
            for (var index = 0; index < _visibility.Length; ++index)
            { Store(_visibility[index], "visible", _visibilityMask.Value & (1u << index)); Check(); }
            Step("message-sound");
            // This handle is independent of source menu visibility. The
            // constructor minus-one arm returns before any sound callback.
            if (_messageHandle != -1 && _context.ReadMessageSound(_messageHandle))
            { Check(); _context.SetMessageSoundPaused(_messageHandle, (_visibilityMask.Value & 4) != 0); Check(); }
            Step("returned"); _call = _call with { Returned = true, Changed = Next() };
        }
        catch (Exception error)
        {
            _failure ??= error.Message; _call = _call with { Failure = _failure, Changed = Next() }; throw;
        }
        finally { _writing = false; }
        void Check()
        {
            if (_thread != Environment.CurrentManagedThreadId || fault != _reentry)
                throw new InvalidOperationException("Source HUD swallowed reentry or changed its actual command thread.");
            _context.RequireCurrent(); _ui.RequireComponent(_root);
        }
    }

    private void ClearMessage()
    {
        Step("clear-message");
        var active = _context.ReadMenuActive(1013); _context.RequireCurrent();
        if (!active) { _ui.StoreString(_message, "string", " "); Next(); }
        Store(_message, "visible", 0); _pendingMessage = null; Next();
    }
    private void Store(FalloutUiComponentBinding component, string trait, float value)
    {
        _ui.StoreFloat(component, trait, value);
        var write = new FalloutPlayerControlHudTraitWrite(Next(), component.Path, trait, BitConverter.SingleToUInt32Bits(value), _ui.Revision);
        _call = _call! with { Writes = _call.Writes.Append(write).ToArray(), Changed = _sequence };
    }
    private void Step(string step) => _call = _call! with { Step = step, Changed = Next() };
    private long Next() => _sequence = checked(_sequence + 1);
    private void RequireCurrent(bool allowFailure = false)
    {
        if (_retired || _writing || _thread != Environment.CurrentManagedThreadId)
        { ++_reentry; throw new InvalidOperationException("Source HUD changed its current thread/lifetime or reentered a control writer."); }
        if (!allowFailure && (_failure ?? _coldBindingFailure) is { } failure) throw new NotSupportedException(failure);
        _context.RequireCurrent(); _ui.RequireComponent(_root);
    }
    private static int Truncate(float value) => !float.IsFinite(value) || value < int.MinValue || value >= 2147483648f ?
        int.MinValue : (int)value;
    internal void Retire()
    {
        if (_retired) return;
        if (_writing || _thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Source HUD retirement changed its entered writer or actual thread.");
        _retired = true; Next();
    }
}
