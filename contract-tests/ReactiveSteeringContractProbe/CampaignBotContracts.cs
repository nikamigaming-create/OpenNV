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
        ActiveSkillMenus();
        WrittenCheckpoint();
        StartupAndSafety();
        AttemptOwnedSkills();
        Console.WriteLine("Campaign bot: source goal/portal selection, no fake primitive completion, offered paused menus during owned skills, bounded modal waits, real save receipts, startup, binding/fault/stale/missing-objective refusal PASS.");
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
        Require(ReactiveCampaignBot.RequestsConversation("Talk to an actor about your reward.") &&
            !ReactiveCampaignBot.RequestsConversation("Follow an actor.") &&
            !ReactiveCampaignBot.RequestsConversation("Return to the well.") &&
            ReactiveCampaignBot.RequestsFollow("Follow the actor.") &&
            !ReactiveCampaignBot.RequestsFollow("Bring the ingredients to the actor.") &&
            ReactiveCampaignBot.RequestsDefeat("Kill the creatures at the wells.") &&
            !ReactiveCampaignBot.RequestsDefeat("Talk to an actor about the creatures."),
            "Displayed conversation intent did not remain distinct from follow/travel goals.");
        Require(ReactiveCampaignBot.NearbyUnselectedQuestBonus(20) > ReactiveCampaignBot.NearbyUnselectedQuestBonus(200) &&
            ReactiveCampaignBot.NearbyUnselectedQuestBonus(float.NaN) == 0,
            "Unselected quest proximity did not prefer nearby finite source goals.");
        Require(ReactiveCampaignBot.SourceTargetMode(false, true, false, false) == "interact" &&
            ReactiveCampaignBot.SourceTargetMode(false, true, false, true) == "interact" &&
            ReactiveCampaignBot.SourceTargetMode(false, true, true, false) == "interact" &&
            ReactiveCampaignBot.SourceTargetMode(false, true, true, true) == "shoot" &&
            ReactiveCampaignBot.SourceTargetMode(true, false, false, false) == "travel",
            "A quest marker attacked an actor without both defeat intent and observed hostility.");
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
        var busy = new BotCampaignSkill(true, "approaching", null, null);
        Require(bot.Tick(next with { Sample = 4 }, busy, .2f).Kind == BotCampaignCommandKind.None,
            "Campaign replaced an active receipt-verified combat/navigation skill.");
        var movingScene = next with { Sample = 5, Scene = "cell-c", Goals =
        [new("other", "quest", 10, "other", "other", "interact", 100),
         new("quest:10:0:cell-c", "quest", 10, "target", "target", "interact", 101)] };
        Require(bot.Tick(movingScene, busy, .2f).Kind == BotCampaignCommandKind.None,
            "A scene identity change or equal-priority candidate displaced the still-available active target.");
        var objectiveAdvanced = State(6, 2) with
        {
            Scene = "cell-b",
            Goals = [new("quest:20:0:cell-b", "quest", 20, "next-target", "next-target", "shoot", 100)]
        };
        Require(bot.Tick(objectiveAdvanced, new(true, "combat-awaiting-shot", null, null), .2f).Kind == BotCampaignCommandKind.None,
            "A quest change replaced an in-flight shot before its impact could settle.");
        var advancedCommand = bot.Tick(objectiveAdvanced with { Sample = 7 }, new(true, "approaching", null, null), .2f);
        Require(advancedCommand.Kind == BotCampaignCommandKind.Reference && advancedCommand.Reference == "next-target" &&
            advancedCommand.Mode == "shoot" && advancedCommand.Distance == 6f,
            "An active skill was not preempted when its objective completed and a new equal-priority objective appeared.");
        var higher = State(8, 2) with
        {
            Goals = [new("side", "side-quest", 1, "side", "side", "travel", 10),
                new("active", "active-quest", 2, "active", "active", "interact", 1000)]
        };
        Require(bot.Tick(higher, Idle, .2f).Reference == "active", "Source active-quest priority was ignored.");
        var travelOnly = State(9, 3) with
        {
            Goals = [new("side-travel", "side-quest", 1, "marker", "marker", "travel", 10)]
        };
        var travelCommand = bot.Tick(travelOnly, Idle, .2f);
        Require(travelCommand.Kind == BotCampaignCommandKind.Reference && travelCommand.Mode == "travel" && travelCommand.Distance == .5f,
            "Campaign travel goal did not request close destination standoff.");
        var follow = State(10, 4) with { Goals = [new("follow", "quest", 3, "actor", "actor", "follow", 100)] };
        var followCommand = bot.Tick(follow, Idle, .2f);
        Require(followCommand.Mode == "follow" && followCommand.Distance == 1.5f,
            "Campaign follow left the player outside ordinary actor interaction distance.");

        var alternatives = new ReactiveCampaignBot(); alternatives.Start();
        var sites = State() with { Goals = [new("first", "quest", 10, "site-a", "site-a", "interact", 100),
            new("second", "quest", 10, "site-b", "site-b", "travel", 100)] };
        Require(alternatives.Tick(sites, Idle, .2f).Reference == "site-a", "Multi-target fixture did not choose its first source site.");
        var noResponse = new BotCampaignSkill(false, "blocked", "Activation had no observed gameplay response.", "bot-policy");
        Require(alternatives.Tick(sites with { Sample = 2 }, noResponse, .2f).Reference == "site-b" && alternatives.Active,
            "A no-response source site prevented exploring the remaining objective target.");
        var crossedCell = sites with { Sample = 3, Scene = "another-cell", Goals =
            [sites.Goals[0] with { Identity = "first-in-another-cell" }, sites.Goals[1]] };
        Require(alternatives.Tick(crossedCell, new(true, "approaching", null, null), .2f).Kind == BotCampaignCommandKind.None &&
            alternatives.Phase == "executing-skill", "Crossing a cell retried a failed target and interrupted its alternative.");
        Require(alternatives.Tick(crossedCell with { Sample = 4 }, noResponse, .2f).Kind == BotCampaignCommandKind.Pause && !alternatives.Active,
            "Exhausting objective targets hid the actual no-response failure.");
    }

    private static void SourceMenus()
    {
        var conversation = new ReactiveCampaignBot(); conversation.Start();
        var firstTopics = State() with { ModalInput = true, Goals = [], Choices =
            [new("a", "a-path", "First topic", "dialogue", "greeting"),
             new("b", "b-path", "Second topic", "dialogue", "greeting"),
             new("bye", "bye-path", "Goodbye", "dialogue", "greeting", true)] };
        Require(conversation.Tick(firstTopics, Idle, .2f).Path == "a-path", "Conversation did not choose its first offered topic.");
        var afterResponse = firstTopics with { Sample = 2, Choices =
            [new("a", "a-path", "First topic", "dialogue", "response-a"),
             new("b", "b-path", "Second topic", "dialogue", "response-a"),
             new("bye", "bye-path", "Goodbye", "dialogue", "response-a", true)] };
        Require(conversation.Tick(afterResponse, Idle, .2f).Path == "b-path", "A changed response identity replayed a topic in the same conversation.");
        var afterSecond = afterResponse with { Sample = 3, Choices =
            [new("a", "a-path", "First topic", "dialogue", "response-b"),
             new("b", "b-path", "Second topic", "dialogue", "response-b"),
             new("bye", "bye-path", "Goodbye", "dialogue", "response-b", true)] };
        Require(conversation.Tick(afterSecond, Idle, .2f).Path == "bye-path", "Exhausted conversation topics did not reach the offered goodbye.");
        var newResponse = new ReactiveCampaignBot(); newResponse.Start();
        Require(newResponse.Tick(firstTopics, Idle, .2f).Path == "a-path", "Dialogue response fixture did not start.");
        var revisedTopic = firstTopics with { Sample = 2, Choices =
            [new("a:new-info", "new-path", "Changed source response", "dialogue", "response-a")] };
        Require(newResponse.Tick(revisedTopic, Idle, .2f).Path == "new-path",
            "A genuinely different offered response for the same topic was suppressed.");
        BotCampaignControl[] layered = [new("under", "Topic", "topic", "dialogue", "topic", "dialogue"),
            new("top", "Cancel", "cancel", "barter-back", "cancel", "barter")];
        Require(ReactiveCampaignBot.ActiveMenuControls(layered).Select(control => control.Path).SequenceEqual(["top"]) &&
            ReactiveCampaignBot.ActiveMenuControls([layered[0]]).Single().Path == "under",
            "Barter overlay allowed a click through to its underlying dialogue or failed to restore dialogue.");
        var barter = new ReactiveCampaignBot(); barter.Start();
        Require(barter.Tick(firstTopics, Idle, .2f).Path == "a-path", "Barter fixture did not enter dialogue.");
        var barterMenu = firstTopics with { Sample = 2, Choices = [new("cancel", "cancel-path", "Cancel", "barter-back", "barter", true)] };
        Require(barter.Tick(barterMenu, Idle, .2f).Path == "cancel-path", "Campaign did not leave the observed barter menu.");
        Require(barter.Tick(afterResponse with { Sample = 3 }, Idle, .2f).Path == "b-path", "Returning from barter lost dialogue choice history.");
        var tutorial = new ReactiveCampaignBot(); tutorial.Start();
        var tutorialMenu = State() with { Paused = true, ModalInput = true, Goals = [],
            Choices = [new("end", "end-path", "[END TUTORIAL] I have learned enough.", "dialogue", "lesson"),
                new("continue", "continue-path", "Okay, I'm in.", "dialogue", "lesson")] };
        Require(tutorial.Tick(tutorialMenu, Idle, .2f).Path == "continue-path",
            "The input driver abandoned an offered tutorial while a continuation choice was available.");
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
        var blockedProgress = State(2, 2) with { CanSave = false, Goals = [] };
        Require(bot.Tick(blockedProgress, Idle, .2f).Kind == BotCampaignCommandKind.None,
            "Observed quest progress requested a save while saving was blocked.");
        var progressed = blockedProgress with { Sample = 3, CanSave = true };
        Require(bot.Tick(progressed, Idle, .2f).Kind == BotCampaignCommandKind.Save,
            "Observed quest progress did not request an ordinary complete save.");
        Require(bot.Tick(progressed with { Sample = 4 }, Idle, .2f).Kind == BotCampaignCommandKind.None,
            "A save input without a new receipt was acknowledged or repeated.");
        var pending = progressed with { Sample = 5, Save = new(1, "pending", null, null) };
        Require(bot.Tick(pending, Idle, .2f).Kind == BotCampaignCommandKind.None &&
            bot.Phase == "awaiting-complete-checkpoint", "Finite-source save waiting was replaced with a partial checkpoint.");
        var completed = pending with { Sample = 6, Save = new(1, "completed", "complete-slot", null) };
        Require(bot.Tick(completed, Idle, .2f).Kind == BotCampaignCommandKind.None &&
            bot.Phase == "awaiting-source-objective", "Actual matching complete writer receipt was not retained.");
        var another = State(7, 3) with { CanSave = true, Save = completed.Save, Goals = [] };
        Require(bot.Tick(another, Idle, .2f).Kind == BotCampaignCommandKind.Save, "New semantic progress did not request a separate checkpoint.");
        var failed = another with { Sample = 8, Save = new(2, "failed", null, "unowned actor pose") };
        Require(bot.Tick(failed, Idle, .2f).Kind == BotCampaignCommandKind.Pause && !bot.Active &&
            bot.Error!.Contains("unowned actor pose", StringComparison.Ordinal),
            "A complete-save refusal became success or lost its exact engine owner.");

        var racing = new ReactiveCampaignBot(); racing.Start(); racing.Tick(State(), Idle, .2f);
        var ready = State(2, 2) with { CanSave = true, Goals = [] };
        Require(racing.Tick(ready, Idle, .2f).Kind == BotCampaignCommandKind.Save, "Race fixture did not request a save.");
        var speaking = ready with { Sample = 3, CanSave = false, SpeechActive = true, Save = new(1, "failed", null, "speech") };
        Require(racing.Tick(speaking, Idle, .2f).Kind == BotCampaignCommandKind.None && racing.Active &&
            racing.Phase == "awaiting-save-admission", "A speech admission race stopped the campaign or claimed a save.");
        Require(racing.Tick(speaking with { Sample = 4 }, Idle, .2f).Kind == BotCampaignCommandKind.None &&
            racing.Tick(ready with { Sample = 5, Save = speaking.Save }, Idle, .2f).Kind == BotCampaignCommandKind.Save,
            "A refused save was not retried after speech cleared and save admission returned.");

        var procedureRace = new ReactiveCampaignBot(); procedureRace.Start(); procedureRace.Tick(State(), Idle, .2f);
        procedureRace.Tick(ready, Idle, .2f);
        var procedure = ready with { Sample = 3, CanSave = false,
            Save = new(1, "failed", null, "actor-procedure-initialization") };
        Require(procedureRace.Tick(procedure, Idle, .2f).Kind == BotCampaignCommandKind.None && procedureRace.Active &&
            procedureRace.Tick(procedure with { Sample = 4 }, Idle, .2f).Kind == BotCampaignCommandKind.None &&
            procedureRace.Tick(ready with { Sample = 5, Save = procedure.Save }, Idle, .2f).Kind == BotCampaignCommandKind.Save,
            "A transient actor procedure save refusal did not wait for fresh admission before retrying.");

        var interrupted = new ReactiveCampaignBot(); interrupted.Start(); interrupted.Tick(State(), Idle, .2f);
        Require(interrupted.Tick(ready, Idle, .2f).Kind == BotCampaignCommandKind.Save, "Menu race did not request a save.");
        var message = ready with { Sample = 3, CanSave = false, Paused = true, ModalInput = true,
            Choices = [new("ok", "message-path", "OK", "message", "message-form")] };
        Require(interrupted.Tick(message, Idle, .2f).Path == "message-path" && interrupted.Active,
            "A message that consumed the save pulse was starved while waiting for a nonexistent receipt.");
        Require(interrupted.Tick(ready with { Sample = 4 }, Idle, .2f).Kind == BotCampaignCommandKind.Save,
            "An unreceipted save was credited or not retried after its source message closed.");

        var pendingMenu = new ReactiveCampaignBot(); pendingMenu.Start(); pendingMenu.Tick(State(), Idle, .2f);
        pendingMenu.Tick(ready, Idle, .2f);
        Require(pendingMenu.Tick(message with { Save = new(1, "pending", null, null) }, Idle, .2f).Kind ==
            BotCampaignCommandKind.None && pendingMenu.Phase == "awaiting-complete-checkpoint",
            "An actually pending save was abandoned when a source menu appeared.");
    }

    private static void ActiveSkillMenus()
    {
        foreach (var kind in new[] { "message", "terminal", "dialogue" })
        {
            var bot = new ReactiveCampaignBot(); bot.Start();
            Require(bot.Tick(State(), Idle, .2f).Reference == "door-a", "Menu fixture did not start its own reference skill.");
            var active = new BotCampaignSkill(true, "awaiting-interaction", null, null);
            var offered = State(2) with
            {
                Paused = true, ModalInput = true, CanSave = true, Goals = [],
                Choices = [new("0", "offered-path", "Localized source caption", kind, "source-form")]
            };
            Require(bot.Tick(offered, active, .2f).Path == "offered-path" && bot.Active,
                "An owned reference skill starved an actual offered source menu.");
            Require(bot.Tick(offered with { Sample = 3 }, active, .2f).Kind == BotCampaignCommandKind.None &&
                bot.Phase == "awaiting-menu-result", "An unchanged menu replayed its consumed choice during a skill.");
            var next = offered with
            {
                Sample = 4, Choices = [new(kind == "dialogue" ? "another-topic-and-info" : "0",
                    "next-path", "Another source caption", kind, "next-source-form")]
            };
            Require(bot.Tick(next, active, .2f).Path == "next-path",
                "A source menu handoff did not deliver its actually offered next control during a skill.");
            var closed = State(5, 2) with { CanSave = true, Goals = [] };
            Require(bot.Tick(closed, active, .2f).Kind == BotCampaignCommandKind.None &&
                bot.Phase == "executing-skill", "Closing a source menu replaced an active skill or requested a save.");
            Require(bot.Tick(closed with { Sample = 6 }, Idle, .2f).Kind == BotCampaignCommandKind.Save,
                "A genuinely completed skill did not return to normal checkpoint policy.");

            var foreign = new ReactiveCampaignBot(); foreign.Start();
            Require(foreign.Tick(offered, active, .2f).Kind == BotCampaignCommandKind.None &&
                foreign.FailureKind == "input-owner" && !foreign.Active,
                "An offered menu adopted a foreign active reference skill.");
            var failed = new ReactiveCampaignBot(); failed.Start(); failed.Tick(State(), Idle, .2f);
            Require(failed.Tick(offered, active with { Error = "Original skill fault.", FailureKind = "engine-owner" },
                .2f).Kind == BotCampaignCommandKind.None && failed.Error == "Original skill fault." && !failed.Active,
                "An offered source choice hid an owned skill failure.");
            var faulted = new ReactiveCampaignBot(); faulted.Start(); faulted.Tick(State(), Idle, .2f);
            Require(faulted.Tick(offered with { Error = "Current source fault." }, active, .2f).Kind ==
                BotCampaignCommandKind.None && faulted.Error == "Current source fault." && !faulted.Active,
                "A paused source menu waived a current authoritative execution fault.");
        }

        foreach (var choices in new IReadOnlyList<BotCampaignChoice>[]
        {
            [new("0", "stalled-path", "Unchanged source caption", "message", "source-form")], []
        })
        {
            var bot = new ReactiveCampaignBot(); bot.Start(); bot.Tick(State(), Idle, .2f);
            var active = new BotCampaignSkill(true, "waiting-for-player-control", null, null);
            for (var sample = 2; sample <= 94 && bot.Active; ++sample)
                bot.Tick(State(sample) with { Paused = true, ModalInput = true, Choices = choices }, active, 1);
            Require(!bot.Active && bot.FailureKind == "bot-policy" &&
                bot.Error!.Contains("progress", StringComparison.Ordinal),
                "A stalled or unoffered modal became an unbounded active-skill wait.");
        }
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
        var confirmation = title with { Sample = 3, Choices = [new("sYes", "yes-path", "Yes", "startup", "opening-menu")] };
        Require(bot.Tick(confirmation, Idle, .2f).Path == "yes-path" &&
            bot.Tick(confirmation with { Sample = 4 }, Idle, .2f).Kind == BotCampaignCommandKind.None,
            "New-game confirmation was not delivered exactly once after the observed menu changed.");
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
        var speaking = new ReactiveCampaignBot(); speaking.Start();
        for (var index = 1; index <= 100; index++)
            speaking.Tick(State(index) with { Goals = [], SpeechActive = true }, Idle, 1);
        Require(speaking.Active && speaking.Phase == "awaiting-source-objective",
            "Active dialogue speech was treated as an unprogressed stall.");
        var stale = new ReactiveCampaignBot(); stale.Start(); stale.Tick(State(), Idle, .2f);
        stale.Tick(State(), Idle, 3);
        Require(!stale.Active && stale.FailureKind == "observation-loss", "Stale game state advanced a campaign.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
