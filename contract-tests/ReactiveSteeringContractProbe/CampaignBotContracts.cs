using OpenNV.Runtime.Gameplay.Bots;

internal static class CampaignBotContracts
{
    private static readonly BotSkillBinding Binding = new("build", "source", "flat", "player");
    private static readonly BotCampaignSkill Idle = new(false, "idle", null, null);
    private static BotCampaignObservation State(long sample = 1, long progress = 1) =>
        new(Binding, sample, "cell-a", progress, false, false, false, false, false,
            [new("quest:10:0:cell-a", "quest", 10, "target", "door-a", "interact", 100)], []);

    internal static void Run()
    {
        SourceGoalSelection();
        SourceMenus();
        WrittenCheckpoint();
        StartupAndSafety();
        AttemptOwnedSkills();
        Console.WriteLine("Campaign bot: source goal/portal selection, no fake primitive completion, offered paused menus, real save receipts, startup, binding/fault/stale/missing-objective refusal PASS.");
    }

    private static void AttemptOwnedSkills()
    {
        var priorFailure = new BotCampaignSkill(false, "stopped", "Earlier manual activation had no result.", "bot-policy");
        var bot = new ReactiveCampaignBot(); bot.Start();
        Require(bot.Tick(State(), priorFailure, .2f).Reference == "door-a" && bot.Active && bot.Error is null,
            "A stale failed manual skill blocked a campaign before it issued any reference action.");
        Require(bot.Tick(State(2), priorFailure, .2f).Kind == BotCampaignCommandKind.Pause && !bot.Active &&
            bot.Error == priorFailure.Error,
            "An error after the campaign issued its own reference action was ignored.");
        bot.Start();
        Require(bot.Tick(State(3), priorFailure, .2f).Reference == "door-a" && bot.Active,
            "Restarting a stopped campaign retained ownership of its earlier skill.");

        var foreign = new ReactiveCampaignBot(); foreign.Start();
        Require(foreign.Tick(State(), new(true, "approaching", null, null), .2f).Kind == BotCampaignCommandKind.Pause &&
            foreign.FailureKind == "input-owner",
            "An already active unowned reference skill was adopted or replaced by a new campaign.");
        var engine = new ReactiveCampaignBot(); engine.Start();
        Require(engine.Tick(State() with { Error = "Actual source execution fault." }, priorFailure, .2f).Kind ==
            BotCampaignCommandKind.Pause && engine.Error == "Actual source execution fault.",
            "Ignoring stale skill feedback waived a current authoritative engine fault.");
    }

    private static void SourceGoalSelection()
    {
        var bot = new ReactiveCampaignBot(); bot.Start();
        var first = bot.Tick(State(), Idle, .2f);
        Require(first.Kind == BotCampaignCommandKind.Reference && first.Reference == "door-a",
            "Campaign did not choose its own source objective action.");
        var completion = new BotCampaignSkill(false, "interaction-observed", null, null);
        Require(bot.Tick(State(2), completion, .2f).Kind == BotCampaignCommandKind.None &&
            bot.Phase == "awaiting-source-progress", "Input or interaction delivery fabricated campaign completion.");
        var next = State(3) with
        {
            Scene = "cell-b",
            Goals = [new("quest:10:0:cell-b", "quest", 10, "target", "target", "interact", 100)]
        };
        Require(bot.Tick(next, completion, .2f).Reference == "target",
            "A genuine portal transition did not advance the same objective's action hierarchy.");
        var busy = new BotCampaignSkill(true, "combat-awaiting-shot", null, null);
        Require(bot.Tick(next with { Sample = 4 }, busy, .2f).Kind == BotCampaignCommandKind.None,
            "Campaign replaced an active receipt-verified combat/navigation skill.");
        var higher = State(5, 2) with
        {
            Goals = [new("side", "side-quest", 1, "side", "side", "travel", 10),
                new("active", "active-quest", 2, "active", "active", "interact", 1000)]
        };
        Require(bot.Tick(higher, Idle, .2f).Reference == "active", "Source active-quest priority was ignored.");
    }

    private static void SourceMenus()
    {
        var bot = new ReactiveCampaignBot(); bot.Start();
        var offered = State() with
        {
            Paused = true, ModalInput = true, Goals = [],
            Choices = [new("back", "back-path", "BACK", "terminal-back", "page", true),
                new("entry", "entry-path", "Source entry", "terminal", "page")]
        };
        Require(bot.Tick(offered, Idle, .2f).Path == "entry-path", "Paused terminal input selected unrelated UI or left before source choices.");
        Require(bot.Tick(offered with { Sample = 2 }, Idle, .2f).Kind == BotCampaignCommandKind.None,
            "Unchanged source menu replayed a consumed choice.");
        var result = offered with
        {
            Sample = 3, Choices = [new("back", "back-path", "BACK", "terminal-back", "result", true)]
        };
        Require(bot.Tick(result, Idle, .2f).Path == "back-path", "Observed source result did not navigate its actual offered Back action.");
        var closed = State(4);
        Require(bot.Tick(closed, Idle, .2f).Reference == "door-a", "Closed menu did not return to the source campaign objective.");
    }

    private static void WrittenCheckpoint()
    {
        var bot = new ReactiveCampaignBot(); bot.Start();
        bot.Tick(State(), Idle, .2f);
        var progressed = State(2, 2) with { CanSave = true, Goals = [] };
        Require(bot.Tick(progressed, Idle, .2f).Kind == BotCampaignCommandKind.Save,
            "Observed quest progress did not request an ordinary complete save.");
        Require(bot.Tick(progressed with { Sample = 3 }, Idle, .2f).Kind == BotCampaignCommandKind.None,
            "A save input without a new receipt was acknowledged or repeated.");
        var pending = progressed with { Sample = 4, Save = new(1, "pending", null, null) };
        Require(bot.Tick(pending, Idle, .2f).Kind == BotCampaignCommandKind.None &&
            bot.Phase == "awaiting-complete-checkpoint", "Finite-source save waiting was replaced with a partial checkpoint.");
        var completed = pending with { Sample = 5, Save = new(1, "completed", "complete-slot", null) };
        Require(bot.Tick(completed, Idle, .2f).Kind == BotCampaignCommandKind.None &&
            bot.Phase == "awaiting-source-objective", "Actual matching complete writer receipt was not retained.");
        var another = State(6, 3) with { CanSave = true, Save = completed.Save, Goals = [] };
        Require(bot.Tick(another, Idle, .2f).Kind == BotCampaignCommandKind.Save, "New semantic progress did not request a separate checkpoint.");
        var failed = another with { Sample = 7, Save = new(2, "failed", null, "unowned actor pose") };
        Require(bot.Tick(failed, Idle, .2f).Kind == BotCampaignCommandKind.Pause && !bot.Active &&
            bot.Error!.Contains("unowned actor pose", StringComparison.Ordinal),
            "A complete-save refusal became success or lost its exact engine owner.");
    }

    private static void StartupAndSafety()
    {
        var bot = new ReactiveCampaignBot(); bot.Start();
        var title = State() with
        {
            Binding = null, Goals = [],
            Choices = [new("sNew", "new-path", "New", "startup", "opening-menu")]
        };
        Require(bot.Tick(title, Idle, .2f).Path == "new-path", "Brand-new entry did not use its actual observed New control.");
        Require(bot.Tick(title with { Sample = 2 }, Idle, .2f).Kind == BotCampaignCommandKind.None,
            "Starting a new owned game repeated its input.");
        Require(bot.Tick(State(3) with { Loading = true }, Idle, .2f).Kind == BotCampaignCommandKind.None,
            "Loading injected gameplay or invented a ready source binding.");
        Require(bot.Tick(State(4), Idle, .2f).Reference == "door-a", "Real new-game source binding did not admit source goals.");
        Require(bot.Tick(State(5) with { Binding = Binding with { Source = "different-stack" } }, Idle, .2f).Kind ==
            BotCampaignCommandKind.Pause && bot.FailureKind == "observation-loss", "Changed owned graph replayed old campaign decisions.");

        foreach (var state in new[]
        {
            State() with { Error = "source query failed" },
            State() with { Goals = [new("bad", "quest", 10, "target", "target", "interact", 100, "unowned source target")] },
            State() with { Defeated = true },
            State() with { Binding = Binding with { InputMode = "physical-xr" } }
        })
        {
            var refused = new ReactiveCampaignBot(); refused.Start();
            refused.Tick(state, Idle, .2f);
            Require(!refused.Active && refused.Error is not null, "A missing engine/input owner fabricated campaign progress.");
        }
        var waiting = new ReactiveCampaignBot(); waiting.Start();
        for (var index = 1; index <= 100; index++)
            waiting.Tick(State(index) with { Goals = [] }, Idle, 1);
        Require(!waiting.Active && waiting.Phase == "blocked" && waiting.Error!.Contains("progress", StringComparison.Ordinal),
            "No source objective became game completion or an unbounded polling loop.");
        var stale = new ReactiveCampaignBot(); stale.Start(); stale.Tick(State(), Idle, .2f);
        stale.Tick(State(), Idle, 3);
        Require(!stale.Active && stale.FailureKind == "observation-loss", "Stale game state advanced a campaign.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
