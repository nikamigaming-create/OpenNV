using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Ui;

// One input surface for flat and XR. Card art and names come from the winning
// CCRD sources; the first-party layout is not a matched retail-pixel claim.
internal sealed partial class NativeCaravanMenu : Control
{
    // Documented engine identity: https://geckwiki.com/index.php?title=MenuMode_(Function)
    internal const uint SourceMenuId = 1083;
    private readonly FalloutCaravanSession _session;
    private readonly string _opponent;
    private readonly Func<FalloutCaravanMatch.Card, Texture2D> _face;
    private readonly Action _close;
    private readonly Action<Exception> _failed;
    private readonly VBoxContainer _content;
    private readonly HashSet<FalloutCaravanBoard.CardIdentity> _selectedDeck;
    private FalloutCaravanBoard.CardIdentity? _selectedCard;
    private int _wager;
    private double _opponentDelay = .6;
    private bool _closing, _confirmForfeit, _faulted;
    private string? _message;
    private NativeViewportLayout? _viewportLayout;
    internal uint MenuId { get; }
    internal object State => new
    {
        opponent = _session.Request.Opponent.ToString(),
        deck = _session.Request.Deck.Form.ToString(),
        difficulty = _session.Request.AiType,
        maximumBetFraction = _session.Request.BetPercentage,
        selectedCards = _selectedDeck.Count,
        wager = _session.Wager,
        started = _session.Match is not null,
        finished = _session.Finished,
        playerWon = _session.Finished ? (bool?)_session.PlayerWon : null,
        player = _session.Match?.CurrentPlayer,
        phase = _session.Match?.CurrentPhase.ToString(),
        action = _session.LastAction,
        error = _session.Error ?? (_faulted ? _message : null),
        unverified = "native-opponent-strategy;opening-draw-policy;exhausted-stock-outcome;exact-denomination-selection;matched-pixels;physical-XR"
    };

    internal NativeCaravanMenu(FalloutCaravanSession session, string opponent, uint menuId,
        Func<FalloutCaravanMatch.Card, Texture2D> face, Action close, Action<Exception> failed)
    {
        _session = session; _opponent = opponent; MenuId = menuId; _face = face; _close = close; _failed = failed;
        _selectedDeck = session.Collection.Select(card => card.Identity).ToHashSet();
        _wager = Math.Min(1, session.MaximumWager);
        Name = "CaravanMenu"; ProcessMode = ProcessModeEnum.Always; MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(new ColorRect { Color = new(.012f, .025f, .018f, .98f), MouseFilter = MouseFilterEnum.Stop, AnchorRight = 1, AnchorBottom = 1 });
        var margin = new MarginContainer(); margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 22);
        AddChild(margin);
        _content = new VBoxContainer(); _content.AddThemeConstantOverride("separation", 10); margin.AddChild(_content);
        Theme = new Theme { DefaultFontSize = 18 };
        SetMeta("opennv_menu_id", menuId);
        SetMeta("opennv_ui_source", "ShowCaravanMenu;CDCK;CCRD;player-card-collection;conserved-currency-inventories");
        SetMeta("opennv_ui_unverified", "matched-layout-pixels;native-ai-strategy;opening-draw-policy;physical-headset");
        try { Rebuild(); }
        catch { Free(); throw; }
    }

    private void Rebuild()
    {
        foreach (var child in _content.GetChildren()) { _content.RemoveChild(child); child.QueueFree(); }
        var title = new Label { Text = "CARAVAN — " + _opponent, HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 26); _content.AddChild(title);
        if (_session.Match is null) BuildDeck(); else BuildMatch();
        if (_message is not null) _content.AddChild(new Label { Text = _message, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        var close = Button("CloseCaravan", _session.Match is not null && !_session.Finished ?
            _confirmForfeit ? "Confirm forfeit — lose wager" : "Forfeit match [Esc]" : "Exit [Esc]", Close);
        close.SetMeta("opennv_source_choice_kind", "caravan-back");
        close.SetMeta("opennv_source_choice_goodbye", true);
        _content.AddChild(close);
        if (IsInsideTree())
            Callable.From(() =>
            {
                if (IsInsideTree() && !_closing && GodotObject.IsInstanceValid(close) &&
                    close.IsInsideTree() && GetViewport().GuiGetFocusOwner() is null)
                    close.GrabFocus();
            }).CallDeferred();
    }

    private static Button Button(string name, string text, Action pressed)
    {
        var button = new Button { Name = name, Text = text, FocusMode = FocusModeEnum.All, CustomMinimumSize = new(0, 38) };
        button.Pressed += pressed; return button;
    }

    private Button Card(FalloutCaravanMatch.Card card, string name, Action pressed)
    {
        var button = Button(name, Short(card), pressed);
        button.Icon = _face(card); button.ExpandIcon = true;
        button.AddThemeConstantOverride("icon_max_width", 52);
        button.CustomMinimumSize = new(128, 82);
        button.TooltipText = FalloutCaravanSession.Label(card);
        button.SetMeta("opennv_caravan_card", card.Identity.Form.ToString());
        button.SetMeta("opennv_caravan_instance", card.Identity.Instance);
        return button;
    }

    private static string Short(FalloutCaravanMatch.Card card) =>
        (card.Kind == FalloutCaravanMatch.Kind.Number ? card.Number == 1 ? "A" : card.Number.ToString(System.Globalization.CultureInfo.InvariantCulture) : card.Kind.ToString()) +
        (card.Suit is { } suit ? " " + suit.ToString()[0] : "");

    private void BuildDeck()
    {
        _content.AddChild(new Label
        {
            Text = $"Select your deck: {_selectedDeck.Count} cards (minimum 30). Click cards to include or remove them.\n" +
                $"Your currency value: {_session.PlayerCurrency} caps. {_opponent}: {_session.OpponentCurrency}. Maximum wager: {_session.MaximumWager}.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        });
        var scroll = new ScrollContainer { FollowFocus = true, SizeFlagsVertical = SizeFlags.ExpandFill };
        _content.AddChild(scroll);
        var grid = new GridContainer { Columns = 9, SizeFlagsHorizontal = SizeFlags.ExpandFill }; scroll.AddChild(grid);
        foreach (var card in _session.Collection)
        {
            var button = Card(card, $"DeckCard_{card.Identity.Instance}", () =>
            {
                if (!_selectedDeck.Remove(card.Identity)) _selectedDeck.Add(card.Identity);
                _message = null; Rebuild();
            });
            button.ToggleMode = true; button.ButtonPressed = _selectedDeck.Contains(card.Identity); grid.AddChild(button);
        }
        var actions = new HBoxContainer(); _content.AddChild(actions);
        actions.AddChild(new Label { Text = "Wager in caps of currency value:", SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var wager = new SpinBox { Name = "CaravanWager", MinValue = 0, MaxValue = _session.MaximumWager, Step = 1, Value = _wager, CustomMinimumSize = new(170, 38) };
        wager.ValueChanged += value => _wager = checked((int)value); actions.AddChild(wager);
        var start = Button("StartCaravanMatch", "Begin match", () => Run(() => _session.Start(_selectedDeck, _wager)));
        start.Disabled = _selectedDeck.Count < 30; actions.AddChild(start);
        _content.AddChild(new Label
        {
            Text = "Build three caravans between 21 and 26. Sell all three opposing pairs and win two.\n" +
                "Number cards follow direction or suit. Kings double; queens reverse/change suit; jacks remove; jokers remove matching ranks (or the ace's suit).",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        });
    }

    private void BuildMatch()
    {
        var match = _session.Match!;
        _content.AddChild(new Label
        {
            Text = _session.Finished ? $"{(_session.PlayerWon ? "You won!" : _opponent + " won.")} Wager: {_session.Wager} caps of currency value." :
                $"{(match.CurrentPlayer == 0 ? "Your turn" : _opponent + " is thinking…")} — {match.CurrentPhase}. Wager: {_session.Wager}. " +
                $"Stock: you {match.Remaining(0)}, opponent {match.Remaining(1)}.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        });
        _content.AddChild(new Label { Text = _session.LastAction, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        var scroll = new ScrollContainer { FollowFocus = true, SizeFlagsVertical = SizeFlags.ExpandFill };
        _content.AddChild(scroll);
        var board = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; scroll.AddChild(board);
        var legal = _session.LegalMoves();
        for (var caravan = 0; caravan < 3; ++caravan)
        {
            var laneIndex = caravan;
            var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new(360, 0) };
            board.AddChild(column);
            var sale = FalloutCaravanRules.ResolveSale(FalloutCaravanSession.Total(match.Lane(0, caravan)), FalloutCaravanSession.Total(match.Lane(1, caravan)));
            column.AddChild(new Label { Text = $"CARAVAN {caravan + 1} — " + (sale == FalloutCaravanRules.Sale.First ? "Yours" : sale == FalloutCaravanRules.Sale.Second ? _opponent : "Unsold"), HorizontalAlignment = HorizontalAlignment.Center });
            foreach (var side in new[] { 1, 0 })
            {
                var targetPlayer = side;
                var lane = match.Lane(side, caravan);
                column.AddChild(new Label { Text = $"{(side == 0 ? "You" : _opponent)}: {FalloutCaravanSession.Total(lane)}   {lane.Direction} {lane.Suit}", HorizontalAlignment = HorizontalAlignment.Center });
                for (var index = 0; index < Math.Max(1, lane.Entries.Count); ++index)
                {
                    var targetIndex = index;
                    var entry = lane.Entries.ElementAtOrDefault(index);
                    var text = entry is null ? "[ Start caravan here ]" :
                        $"{entry.Number.Value} {entry.Number.Suit}" + (entry.Modifiers.Count == 0 ? "" : "  [" + string.Join(", ", entry.Modifiers.Select(value => value.Kind)) + "]");
                    var target = Button($"CaravanTarget_{side}_{caravan}_{index}", text, () =>
                    {
                        if (_selectedCard is not { } identity) { _message = "Select a card in your hand, then its destination."; Rebuild(); return; }
                        var move = legal.FirstOrDefault(move => move.Card == identity && move.Player == targetPlayer && move.Caravan == laneIndex &&
                            (move.Kind == FalloutCaravanSession.ActionKind.Number || move.Kind == FalloutCaravanSession.ActionKind.Modifier && move.Target == targetIndex));
                        if (move is null) { _message = "That card cannot be played here."; Rebuild(); return; }
                        Run(() => _session.Play(move));
                    });
                    if (entry is not null)
                    {
                        target.Icon = _face(_session.Card(entry.Identity)); target.ExpandIcon = true;
                        target.AddThemeConstantOverride("icon_max_width", 44); target.CustomMinimumSize = new(0, 58);
                    }
                    target.Disabled = !CanPlay; column.AddChild(target);
                }
                if (side == 1) column.AddChild(new HSeparator());
            }
            var disbandMove = new FalloutCaravanSession.Move(FalloutCaravanSession.ActionKind.Disband, Caravan: caravan);
            var disband = Button($"DisbandCaravan_{caravan}", "Disband your caravan", () => Run(() => _session.Play(disbandMove)));
            disband.Disabled = !CanPlay || !legal.Contains(disbandMove); column.AddChild(disband);
        }
        var hand = new HFlowContainer { Name = "CaravanHand" }; _content.AddChild(hand);
        foreach (var card in match.Hand(0))
        {
            var button = Card(card, $"HandCard_{card.Identity.Instance}", () => { _selectedCard = card.Identity; _message = null; Rebuild(); });
            button.ToggleMode = true; button.ButtonPressed = _selectedCard == card.Identity; button.Disabled = !CanPlay; hand.AddChild(button);
        }
        var discard = Button("DiscardCaravanCard", "Discard selected card" + (match.CurrentPhase == FalloutCaravanMatch.Phase.Opening ? " and redraw (opening)" : ""), () =>
        {
            if (_selectedCard is { } identity) Run(() => _session.Play(new(FalloutCaravanSession.ActionKind.Discard, identity)));
        });
        discard.Disabled = !CanPlay || _selectedCard is not { } selected || !legal.Contains(new(FalloutCaravanSession.ActionKind.Discard, selected));
        _content.AddChild(discard);
        if (!_session.Finished) _content.AddChild(new Label
        {
            Text = _selectedCard is { } chosen ?
            "Selected: " + FalloutCaravanSession.Label(match.Hand(0).Single(card => card.Identity == chosen)) + ". Choose a board card or caravan to play it." :
            "Choose a card in your hand, then a caravan or target card. Tab / arrows move focus; Enter selects.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        });
    }

    private bool CanPlay => !_faulted && !_session.Finished && _session.Match?.CurrentPlayer == 0;
    private void Run(Action action)
    {
        try { action(); _selectedCard = null; _confirmForfeit = false; _message = null; _opponentDelay = .6; Rebuild(); }
        catch (Exception error)
        {
            _message = error.Message;
            if (_session.Error is not null || error is not (InvalidOperationException or InvalidDataException or NotSupportedException))
            { _faulted = true; _failed(error); }
            Rebuild();
        }
    }

    public override void _Process(double delta)
    {
        if (_faulted || _closing || _session.Finished || _session.Match?.CurrentPlayer != 1 || (_opponentDelay -= delta) > 0) return;
        try { _session.PlayOpponent(); _opponentDelay = .6; Rebuild(); }
        catch (Exception error) { _faulted = true; _message = "Caravan stopped: " + error.Message; Rebuild(); _failed(error); }
    }

    private void Close()
    {
        if (_closing) return;
        if (_session.Match is not null && !_session.Finished)
        {
            if (!_confirmForfeit) { _confirmForfeit = true; Rebuild(); return; }
            try { _session.Forfeit(); }
            catch (Exception error) { _faulted = true; _message = "Caravan settlement failed: " + error.Message; Rebuild(); _failed(error); return; }
        }
        _closing = true; _close();
    }

    public override void _EnterTree() => _viewportLayout = new(this, Layout);
    public override void _Ready() { Layout(); _content.GetChildren().OfType<Button>().LastOrDefault()?.GrabFocus(); }
    public override void _ExitTree()
    {
        _viewportLayout?.Dispose();
        // XR moves the same canvas into its stereo viewport. Close only after
        // a real retirement, not the exit/entry pair of that reparent.
        Callable.From(() =>
        {
            if (!GodotObject.IsInstanceValid(this) || !IsInsideTree()) _close();
        }).CallDeferred();
    }
    private void Layout()
    {
        if (!IsInsideTree()) return;
        var scale = GetViewportRect().Size.Y / 900;
        Scale = Vector2.One * scale; Size = GetViewportRect().Size / scale;
    }
    public override void _Input(InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        { Close(); GetViewport().SetInputAsHandled(); }
    }
}
