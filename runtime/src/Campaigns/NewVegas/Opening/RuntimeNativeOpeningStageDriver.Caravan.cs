using System.Globalization;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private CanvasLayer? _caravanLayer;
    private NativeCaravanMenu? _caravanMenu;
    private Action? _releaseCaravanInput;
    private (int Won, int Lost)? _caravanStatistics;
    internal object? CaravanState => _caravanMenu?.State;

    private void OpenCaravanMenu(FalloutCaravanCommandRequest request)
    {
        if (_caravanMenu is not null || _player.FurnitureActive || _moviePlaying || _recipeMenu is not null ||
            _barterMenu is not null || _levelUpMenu is not null || _nameEntry is not null || _raceSexEntry is not null ||
            _vigorEntry is not null || _specialBookEntry is not null || _tagSkillEntry is not null || _traitEntry is not null ||
            TerminalSaveBlocker is not null)
            throw new InvalidOperationException("Caravan cannot open while another player interaction owns input.");
        var world = _scripts.References ?? throw new InvalidOperationException("Caravan has no shared world owner.");
        var actor = world.Get(request.Opponent);
        if (actor.Deleted || actor.Destroyed || world.IsUnconscious(request.Opponent) || DialogueActorDead(request.Opponent))
            throw new InvalidOperationException("The Caravan opponent is unavailable.");
        var actorSource = _pluginStack.GetEffective(actor.Base);
        var fields = actorSource.ReadSubrecords().ToArray();
        var name = fields.SingleOrDefault(field => field.Signature == "FULL").Data;
        var opponentName = name.IsEmpty ? FalloutDialogueTopic.Text(fields.Single(field => field.Signature == "EDID").Data.Span) :
            FalloutDialogueTopic.Text(name.Span);
        var opponentInventory = world.Inventory(request.Opponent, SourcePlayerLevel, _globals).Contents;
        var caps = FalloutDialogueTopic.Find(_pluginStack, "MISC", "Caps001").FormKey;
        var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Caravan owned presentation source is absent.");
        var statistics = _caravanStatistics ??= ResolveCaravanStatistics(source);
        var session = new FalloutCaravanSession(_pluginStack, world.PlayerCaravanCards, request, _inventory, opponentInventory, caps,
            prepareCompleted: won =>
            {
                var index = won ? statistics.Won : statistics.Lost;
                _scripts.MiscStatistics.RequireIncrement(index);
                _scripts.Challenges.RequireStatisticIncrement(checked((ushort)index));
            }, completed: won =>
            {
                var index = won ? statistics.Won : statistics.Lost;
                // The statistic's changed event owns challenge publication.
                _scripts.MiscStatistics.Increment(index);
                GD.Print($"OPENNV_NATIVE_CARAVAN_RESULT opponent={request.Opponent} playerWon={won} statistic={index} " +
                    "owner=shared-inventory;misc-statistic;source-challenges parity=unverified");
            });
        _ = session.MaximumWager;
        var path = FalloutExecutableStringTable.ReadCaravanMenuPath(source.FalloutExecutablePath);
        _ = FalloutMenuXml.Read(source, path);
        var textures = new Dictionary<FalloutFormKey, Texture2D>();
        Texture2D Face(FalloutCaravanMatch.Card card)
        {
            if (textures.TryGetValue(card.Identity.Form, out var cached)) return cached;
            var cardSource = FalloutCaravanCard.Read(_pluginStack, _pluginStack.GetEffective(card.Identity.Form));
            var texturePath = cardSource.FaceTexture.Replace('\\', '/');
            if (!texturePath.StartsWith("textures/", StringComparison.OrdinalIgnoreCase)) texturePath = "textures/" + texturePath;
            if (!source.TryRead(texturePath, null, out var payload, out var identity))
                throw new FileNotFoundException("Owned Caravan card face is absent.", texturePath);
            var texture = NativeDdsTexture.LoadCached(source, payload, identity, logicalPath: texturePath);
            textures.Add(card.Identity.Form, texture); return texture;
        }
        var layer = new CanvasLayer { Name = "NativeCaravanLayer", Layer = 98, ProcessMode = ProcessModeEnum.Always };
        NativeCaravanMenu? menu = null;
        try
        {
            menu = new(session, opponentName, NativeCaravanMenu.SourceMenuId, Face, CloseCaravanMenu, error =>
            {
                ExecutionError ??= error.Message;
                GD.PushError("OPENNV_NATIVE_CARAVAN_DIVERGENCE: " + error.Message);
            });
            _caravanLayer = layer; _caravanMenu = menu;
            var paused = GetTree().Paused;
            var release = AcquireInteractionInput();
            _releaseCaravanInput = () =>
            {
                try { release(); }
                finally { GetTree().Paused = paused; }
            };
            GetTree().Paused = true;
            layer.TreeExiting += () => Callable.From(() =>
            {
                if (!GodotObject.IsInstanceValid(layer) || !layer.IsInsideTree()) CloseCaravanMenu();
            }).CallDeferred();
            AddChild(layer); layer.AddChild(menu);
        }
        catch
        {
            CloseCaravanMenu();
            if (menu is not null && menu.GetParent() is null && GodotObject.IsInstanceValid(menu)) menu.Free();
            if (GodotObject.IsInstanceValid(layer) && !layer.IsInsideTree()) layer.Free();
            throw;
        }
        GD.Print($"OPENNV_NATIVE_CARAVAN_MENU_OPEN opponent={request.Opponent} deck={request.Deck.Form} difficulty={request.AiType} " +
            $"maxBetFraction={request.BetPercentage.ToString(CultureInfo.InvariantCulture)} source={path} " +
            "owner=shared-caravan-match;source-card-collection;source-currency-inventories native-ai-strategy-and-matched-ui=unverified");
    }

    private static (int Won, int Lost) ResolveCaravanStatistics(RuntimeLiveContentSource source)
    {
        var names = FalloutExecutableStringTable.ReadMiscStatisticNames(source.FalloutExecutablePath);
        int Resolve(bool won)
        {
            var matches = names.Where(name => name.Contains("Caravan", StringComparison.OrdinalIgnoreCase) &&
                (won ? name.EndsWith("Won", StringComparison.OrdinalIgnoreCase) || name.EndsWith("Wins", StringComparison.OrdinalIgnoreCase) :
                    name.EndsWith("Lost", StringComparison.OrdinalIgnoreCase) || name.EndsWith("Losses", StringComparison.OrdinalIgnoreCase))).ToArray();
            if (matches.Length != 1) throw new NotSupportedException("Owned Caravan outcome statistic name is absent or ambiguous.");
            return FalloutMiscStatisticNames.Index(source, matches[0]);
        }
        return (Resolve(true), Resolve(false));
    }

    private void CloseCaravanMenu()
    {
        if (_caravanMenu is null) return;
        var layer = _caravanLayer;
        _caravanLayer = null; _caravanMenu = null;
        var release = _releaseCaravanInput; _releaseCaravanInput = null;
        try { release?.Invoke(); }
        finally { if (layer is not null && GodotObject.IsInstanceValid(layer) && !layer.IsQueuedForDeletion()) layer.QueueFree(); }
    }

    public override void _ExitTree() => CloseCaravanMenu();
}
