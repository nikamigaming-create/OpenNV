using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessRuntimeState
{
    private void RequireMainUtilityBoundary(bool retiring)
    {
        if (_utilityActive is not null)
        { FaultMainUtility(); throw new NotSupportedException("Actual platform utility cannot capture or retire its entered consumer."); }
        if (retiring && (_utilityPlatformLease != Guid.Empty || _utilityCommandLease != Guid.Empty))
            throw new NotSupportedException("Actual utility provider leases must retire before the source Main process.");
    }
    internal FalloutMainUtilitySnapshot CaptureMainUtilities()
    {
        RequireNotBusy(); RequireMainUtilityBoundary(false);
        var value = new FalloutMainUtilitySnapshot(MainUtilitySchema, MainUtilitySource, _stack, _process, _sequence,
            _utilityConstructed, _utilityAchievementsConstructed, _utilityLoginConstructed, _utilityThirdConstructed,
            _utilityLoggedOn, _utilityLoginChanged, _utilityRequests, _utilityClearedThrough, _utilityPending.ToArray(),
            _utilityCalls, _utilityFrame, _utilityCommand, _utilityCallbackReceipt, _utilityLastCleared.ToArray(),
            _utilityRegistrations, _utilityCallbackAttempt);
        ValidateMainUtilities(value); RequireMainUtilityCaller(value, CaptureMainScriptCaller()); return value;
    }
    internal void RestoreMainUtilities(FalloutMainUtilitySnapshot value)
    {
        RequireNotBusy(); RequireMainUtilityBoundary(false); ValidateMainUtilities(value);
        if (_utilityConstructed || _utilityAchievementsConstructed || _utilityLoginConstructed || _utilityThirdConstructed ||
            _utilityCalls != 0 || _utilityRequests != 0 || _utilityCallbackReceipt is not null || value.Source != MainUtilitySource ||
            value.Stack != _stack || value.CapturedProcess == _process || _cold?.PreviousProcess != value.CapturedProcess || value.Changed > _sequence)
            throw new InvalidDataException("Cold utilities must reuse the actual selected captured Main epoch exactly once.");
        if (value.Callback is { Registered: true })
            throw new NotSupportedException("Original platform login callback has no typed portable source function authority; cold cannot reconstruct an opaque delegate.");
        RequireMainUtilityCaller(value, CaptureMainScriptCaller());
        _utilityConstructed = value.UtilityConstructed; _utilityAchievementsConstructed = value.AchievementsConstructed;
        _utilityLoginConstructed = value.LoginConstructed; _utilityThirdConstructed = value.ThirdConstructed;
        _utilityLoggedOn = value.LoggedOn; _utilityLoginChanged = value.LoginChanged; _utilityRequests = value.Requests;
        _utilityClearedThrough = value.ClearedThrough; _utilityPending.AddRange(value.Pending); _utilityCalls = value.Calls;
        _utilityFrame = value.LastFrame; _utilityCommand = value.LastCommand; _utilityCallbackReceipt = value.Callback;
        _utilityRegistrations = value.Registrations; _utilityCallbackAttempt = value.LastCallbackAttempt; _utilityLastCleared = value.LastCleared.ToArray();
        // No queue request, payload destruction, Steam query, cache store,
        // callback, award or original utility frame is replayed on cold.
    }
    internal static void RequireMainUtilityCaller(FalloutMainUtilitySnapshot utility, FalloutMainScriptCallerSnapshot main)
    {
        if (utility.Source.Main != main.Source || utility.Stack != main.Stack || utility.Changed > main.Changed ||
            utility.Calls > main.Calls || utility.LastFrame is { MainOrdinal: { } ordinal } && ordinal > main.Calls)
            throw new InvalidDataException("Utility prefix belongs to another actual Main source/captured invocation.");
        if (main.LastCall is not { } parent) return;
        var child = parent.Children.SingleOrDefault(step => step.Step == FalloutMainScriptCallerStep.Prologue);
        if (child is null) return; // Actual OS early-return skips this child.
        var frame = utility.LastFrame;
        if (frame is null || frame.MainInvocation != parent.Invocation || frame.MainOrdinal != parent.Ordinal ||
            frame.SourceProcess != parent.SourceProcess || frame.Entered <= child.Entered ||
            child.Returned is { } end && (frame.Changed >= end || !frame.Returned) ||
            child.Returned is null && frame.Error is null)
            throw new InvalidDataException("Actual Prologue and utility children lost their same entered/returned source prefix.");
    }
    internal static void ValidateMainUtilities(FalloutMainUtilitySnapshot value)
    {
        if (value is null || value.Schema != MainUtilitySchema || value.Source is null || string.IsNullOrWhiteSpace(value.Stack) ||
            value.CapturedProcess == Guid.Empty || value.Changed < 1 || value.Requests < 0 || value.ClearedThrough < 0 ||
            value.ClearedThrough > value.Requests || value.Calls < 0 || (value.Calls == 0) != (value.LastFrame is null) ||
            value.LoginChanged < 0 || value.LoginChanged > value.Changed || value.LoginChanged == 0 && value.LoggedOn ||
            !value.LoginConstructed && (value.LoggedOn || value.LoginChanged != 0 || value.Callback is not null) ||
            !value.AchievementsConstructed && (value.Requests != 0 || value.ClearedThrough != 0) ||
            value.Pending is null || value.LastCleared is null || value.Pending.Count != value.Requests - value.ClearedThrough ||
            value.Registrations < 0 || (value.Registrations == 0) != (value.Callback is null) ||
            (value.Registrations == 0) != (value.LastCallbackAttempt is null))
            throw new InvalidDataException("Main utility snapshot omitted its source lazy constructors/cache/FIFO prefix.");
        value.Source.Validate();
        var expected = value.ClearedThrough;
        foreach (var request in value.Pending)
        {
            expected = checked(expected + 1);
            RequireRequest(request, expected);
        }
        if (expected != value.Requests) throw new InvalidDataException("Utility pending FIFO omitted a genuine request ordinal.");
        if (value.LastCleared.Count != 0)
        {
            var first = value.LastCleared[0]?.Request ?? throw new InvalidDataException("Cleared source payload is absent.");
            for (var index = 0; index < value.LastCleared.Count; index++) RequireRequest(value.LastCleared[index], checked(first + index));
            if (value.LastCleared[^1].Request != value.ClearedThrough) throw new InvalidDataException("Last genuine queue clear omitted its final request.");
        }
        else if (value.ClearedThrough != 0) throw new InvalidDataException("Source queue clear lost its actual request identities.");
        if (value.LastFrame is { } frame)
        {
            RequireCall(frame, frameCall: true);
            if (!value.UtilityConstructed || frame.MainOrdinal is null or < 1 || frame.MainInvocation is null)
                throw new InvalidDataException("Main utility frame lost its genuine Prologue invocation/constructor.");
            RequireFrameOrder(frame);
            if (frame.Returned && (!value.AchievementsConstructed || !value.LoginConstructed || !value.ThirdConstructed ||
                frame.Effects.Last().Step != FalloutMainUtilityStep.ThirdNoOp))
                throw new InvalidDataException("Returned utility frame omitted a required source child or genuine queue retirement.");
            var stored = frame.Effects.LastOrDefault(effect => effect.Step == FalloutMainUtilityStep.StoreLogin);
            if (stored?.Returned is not null && (stored.Boolean != value.LoggedOn || value.LoginChanged != stored.Entered + 1))
                throw new InvalidDataException("Source login cache changed without its actual prior store.");
        }
        if (value.LastCommand is { } command)
        {
            RequireCall(command, frameCall: false);
            var steps = command.Effects.Select(effect => effect.Step).ToArray();
            var position = 0; var stopped = false;
            Take(FalloutMainUtilityStep.SuppressionByte);
            var suppression = command.Effects[0];
            if (suppression.Returned is not null && suppression.Boolean == false)
            {
                Optional(FalloutMainUtilityStep.ConstructAchievements); if (!stopped) Take(FalloutMainUtilityStep.QueueAchievement);
            }
            if (position != steps.Length) throw new InvalidDataException("Achievement command inserted an unowned source effect.");
            void Take(FalloutMainUtilityStep step)
            {
                if (position < steps.Length && steps[position] == step)
                { stopped = command.Effects[position].Returned is null; position++; }
                else throw new InvalidDataException("Achievement command skipped its source guard or queue effect.");
            }
            void Optional(FalloutMainUtilityStep step) { if (position < steps.Length && steps[position] == step) Take(step); }
        }
        if (value.Callback is { } callback)
        {
            RequireCall(callback.Call, frameCall: false);
            if (callback.Registration != value.Registrations || callback.Registration < 1 || string.IsNullOrWhiteSpace(callback.Owner) || callback.Changed != callback.Call.Changed)
                throw new InvalidDataException("Login callback registration lost its actual invoked lifetime.");
            var registration = callback.Call.Effects.SingleOrDefault(effect => effect.Step == FalloutMainUtilityStep.RegisterCallback);
            if (registration is null || registration.Argument != callback.Owner || registration.Returned is not null && registration.Boolean != callback.Registered)
                throw new InvalidDataException("Login callback publication did not commit its actual pointer before delivery.");
            RequireRegistrationOrder(callback.Call);
        }
        if (value.LastCallbackAttempt is { } attempted)
        { RequireCall(attempted, frameCall: false); RequireRegistrationOrder(attempted); }
        if (value.LastFrame is { } correlated)
        {
            var known = value.Pending.Concat(value.LastCleared).ToDictionary(request => request.Request);
            foreach (var effect in correlated.Effects.Where(effect => effect.Request is not null))
            {
                if (!known.TryGetValue(effect.Request!.Value, out var request))
                    throw new InvalidDataException("Utility frame referenced a payload outside its actual pending/cleared FIFO.");
                if (effect.Step == FalloutMainUtilityStep.RetirePayload && effect.Returned is not null && request.PayloadRetired != effect.Entered + 1 ||
                    effect.Step == FalloutMainUtilityStep.SetAchievement && effect.Argument != FalloutMainUtilitySource.AchievementIdentifier(request.Id) ||
                    effect.Step == FalloutMainUtilityStep.RangeDiagnostic && request.Id <= 100)
                    throw new InvalidDataException("Utility platform argument or payload retirement drifted from its original queued request.");
            }
        }
        void RequireRegistrationOrder(FalloutMainUtilityCall call)
        {
            var position = 0; var stopped = false;
            if (call.Effects[position].Step == FalloutMainUtilityStep.ConstructLogin) Take(FalloutMainUtilityStep.ConstructLogin);
            if (!stopped)
            {
                var registration = Take(FalloutMainUtilityStep.RegisterCallback);
                if (!stopped && registration.Boolean == true) Take(FalloutMainUtilityStep.InitialCallback);
            }
            if (position != call.Effects.Count)
                throw new InvalidDataException("Login callback registration inserted an unowned source consumer.");
            FalloutMainUtilityEffect Take(FalloutMainUtilityStep step)
            {
                if (position >= call.Effects.Count || call.Effects[position].Step != step)
                    throw new InvalidDataException("Login registration omitted its exact pointer-before-callback prefix.");
                var effect = call.Effects[position++]; stopped = effect.Returned is null; return effect;
            }
        }
        void RequireRequest(FalloutPlatformAchievementRequest request, long ordinal)
        {
            if (request is null || request.Request != ordinal || ordinal < 1 || request.Queued < 1 || request.Queued > value.Changed ||
                request.PayloadRetired is { } retired && (retired <= request.Queued || retired > value.Changed))
                throw new InvalidDataException("Achievement FIFO changed an actual signed payload, order or retirement point.");
        }
        void RequireCall(FalloutMainUtilityCall call, bool frameCall)
        {
            if (call.Identity == Guid.Empty || call.SourceProcess == Guid.Empty || call.Entered < 1 || call.Changed <= call.Entered ||
                call.Changed > value.Changed || call.Effects is null || call.Effects.Count == 0 ||
                frameCall != (call.MainInvocation is not null && call.MainOrdinal is not null) ||
                call.Returned == (call.Error is not null) || (call.Error is null) != (call.FailureType is null))
                throw new InvalidDataException("Utility call fabricated a source return or omitted its failed prefix.");
            var previous = call.Entered;
            foreach (var effect in call.Effects)
            {
                if (effect is null || !Enum.IsDefined(effect.Step) || effect.Entered <= previous || effect.Entered >= call.Changed ||
                    effect.Returned is { } end && (end <= effect.Entered || end >= call.Changed) ||
                    (effect.Error is null) != (effect.FailureType is null) || effect.Error is not null && effect.Returned is not null ||
                    effect.Returned is null && (effect != call.Effects[^1] || call.Returned))
                    throw new InvalidDataException("Main utility effect lost its exact source entered/returned order.");
                previous = effect.Returned ?? effect.Entered;
                var booleanStep = effect.Step is FalloutMainUtilityStep.StatisticsTest or FalloutMainUtilityStep.StatisticsQuery or
                    FalloutMainUtilityStep.SetAchievement or FalloutMainUtilityStep.StoreStatisticsTest or
                    FalloutMainUtilityStep.StoreStatisticsQuery or FalloutMainUtilityStep.StoreStatistics or
                    FalloutMainUtilityStep.PlatformRunning or FalloutMainUtilityStep.UserTest or FalloutMainUtilityStep.UserQuery or
                    FalloutMainUtilityStep.LoggedOn or FalloutMainUtilityStep.StoreLogin or FalloutMainUtilityStep.ChangedCallback or
                    FalloutMainUtilityStep.SuppressionByte or FalloutMainUtilityStep.RegisterCallback or FalloutMainUtilityStep.InitialCallback;
                var requestStep = effect.Step is FalloutMainUtilityStep.RetirePayload or FalloutMainUtilityStep.RangeDiagnostic or
                    FalloutMainUtilityStep.StatisticsTest or FalloutMainUtilityStep.StatisticsQuery or FalloutMainUtilityStep.SetAchievement;
                var argumentStep = effect.Step is FalloutMainUtilityStep.SetAchievement or FalloutMainUtilityStep.QueueAchievement or
                    FalloutMainUtilityStep.RegisterCallback;
                if (effect.Returned is not null && booleanStep != (effect.Boolean is not null) ||
                    effect.Returned is null && effect.Boolean is not null || requestStep != (effect.Request is not null) ||
                    argumentStep != (effect.Argument is not null))
                    throw new InvalidDataException("Utility effect changed its actual typed argument/query/payload observation.");
            }
        }
    }
    private static void RequireFrameOrder(FalloutMainUtilityCall frame)
    {
        var effects = frame.Effects; var position = 0; var stopped = false;
        Optional(FalloutMainUtilityStep.ConstructUtility); Optional(FalloutMainUtilityStep.ConstructAchievements);
        while (!stopped && At(FalloutMainUtilityStep.RetirePayload))
        {
            var request = effects[position].Request;
            Take(FalloutMainUtilityStep.RetirePayload);
            if (!stopped && At(FalloutMainUtilityStep.RangeDiagnostic)) Take(FalloutMainUtilityStep.RangeDiagnostic);
            else if (!stopped)
            {
                var present = Take(FalloutMainUtilityStep.StatisticsTest)?.Boolean == true;
                if (present && !stopped)
                { Take(FalloutMainUtilityStep.StatisticsQuery); if (!stopped) Take(FalloutMainUtilityStep.SetAchievement); }
            }
            foreach (var effect in effects.Where(effect => effect.Request == request && effect.Step == FalloutMainUtilityStep.SetAchievement))
                if (effect.Argument is null) throw new InvalidDataException("Actual achievement API argument was lost.");
        }
        if (!stopped && At(FalloutMainUtilityStep.StoreStatisticsTest))
        {
            var present = Take(FalloutMainUtilityStep.StoreStatisticsTest)?.Boolean == true;
            if (present && !stopped)
            { Take(FalloutMainUtilityStep.StoreStatisticsQuery); if (!stopped) Take(FalloutMainUtilityStep.StoreStatistics); }
            if (!stopped) Take(FalloutMainUtilityStep.ClearQueue);
        }
        if (!stopped)
        {
            Optional(FalloutMainUtilityStep.ConstructLogin);
            var running = Take(FalloutMainUtilityStep.PlatformRunning)?.Boolean == true;
            if (running && !stopped)
            {
                var present = Take(FalloutMainUtilityStep.UserTest)?.Boolean == true;
                if (present && !stopped)
                { Take(FalloutMainUtilityStep.UserQuery); if (!stopped) Take(FalloutMainUtilityStep.LoggedOn); }
            }
            if (!stopped) Take(FalloutMainUtilityStep.StoreLogin);
            if (!stopped) Optional(FalloutMainUtilityStep.ChangedCallback);
            if (!stopped) Optional(FalloutMainUtilityStep.ConstructThird);
            if (!stopped) Take(FalloutMainUtilityStep.ThirdNoOp);
        }
        if (position != effects.Count || frame.Returned && stopped)
            throw new InvalidDataException("Prologue utility skipped or reordered a genuine child.");
        bool At(FalloutMainUtilityStep step) => position < effects.Count && effects[position].Step == step;
        void Optional(FalloutMainUtilityStep step) { if (!stopped && At(step)) _ = Take(step); }
        FalloutMainUtilityEffect? Take(FalloutMainUtilityStep step)
        {
            if (stopped) return null;
            if (!At(step)) throw new InvalidDataException("Prologue utility omitted its next exact source child: " + step);
            var effect = effects[position++]; if (effect.Returned is null) stopped = true; return effect;
        }
    }
}
