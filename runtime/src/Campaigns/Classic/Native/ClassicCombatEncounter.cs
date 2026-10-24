using System.Buffers.Binary;
using System.Text.Json;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

internal enum ClassicCombatPhase { Unknown, Exploration, PlayerTurn, AttackPending, ActorTurn, Blocked, Defeated }
internal sealed record ClassicCombatParticipant(string Map, string MapSha256, int Serial, int ObjectId,
    int Pid, string PrototypeSha256, int Tile, int Elevation, int HitPoints, int ActionPoints,
    int MaximumActionPoints, int Results, int AiPacket, int Team, int Rotation = 0);
internal sealed record ClassicCombatRequest(long Sequence, string Map, int ActorHandle, int TargetHandle,
    ClassicAttackIntent? Intent, string Boundary);
internal sealed record ClassicCombatTurnReceipt(long Number, int Handle, int StartingAp, int EndingAp);
internal sealed record ClassicCombatDeath(string Map, int Serial, long ActionSequence, int Experience, int KillType);
internal sealed record ClassicCombatEncounterSave(string Schema, string? ContractIdentity, ClassicCombatPhase Phase,
    int PlayerActionPoints, int TurnHandle, long TurnNumber, long NextRequest,
    ClassicCombatParticipant[] Participants, ClassicCombatRequest[] Requests, string? Failure,
    ClassicCombatAction[]? Actions = null, ClassicCombatTurnReceipt[]? Turns = null,
    ClassicCombatDeath[]? Deaths = null, int PendingExperience = 0, int PlayerHitPoints = -1, int AwardedExperience = 0, string[]? Engaged = null);

/// <summary>Source participants, admitted attack transactions and retained boundaries.
/// Gameplay resolves damage; presentation consumes the owned action clock.</summary>
internal sealed class ClassicCombatEncounter
{
    private const string Schema = "opennv-classic-combat/v1";
    internal const int PlayerHandle = int.MaxValue;
    private readonly ClassicPlayerSession _player;
    private readonly ClassicAttackModeCatalog? _modes;
    private readonly string? _contractIdentity;
    private readonly ClassicCombatContracts? _execution;
    private readonly List<ClassicCombatAction> _actions = [];
    private readonly List<ClassicCombatTurnReceipt> _turns = [];
    private readonly List<ClassicCombatDeath> _deaths = [];
    private readonly HashSet<string> _engaged = new(StringComparer.Ordinal);
    private int _pendingExperience;
    private int _awardedExperience;
    internal int? ActorRotation(int serial) => _participants.TryGetValue(Key(_player.MapPath, serial), out var row) ? row.Rotation : null;
    internal IReadOnlyList<ClassicCombatDeath> Deaths => _deaths;
    internal int? ActorHitPoints(int serial) => _participants.TryGetValue(Key(_player.MapPath, serial), out var row) ? row.HitPoints : null;
    internal int? ActorResults(int serial) => _participants.TryGetValue(Key(_player.MapPath, serial), out var row) ? row.Results : null;
    internal bool Dead(int serial) => Dead(_player.MapPath, serial);
    internal bool Dead(string map, int serial) => _participants.TryGetValue(Key(map, serial), out var actor) && actor.HitPoints == 0;
    internal event Action? WorldChanged;
    private Func<string>? _playerIdle;
    private Func<uint, string>? _resolveCritter;
    internal ClassicCombatAction? PendingAction => _actions.LastOrDefault(row => !row.Committed);
    internal ClassicActionPose? PlayerActionPose => Phase == ClassicCombatPhase.AttackPending && PendingAction is { } action
        ? action.ActorHandle == PlayerHandle ? action.PlayerPose : action.TargetPose
        : _player.HitPoints == 0 ? _actions.LastOrDefault(row => row.ActorHandle != PlayerHandle && row.Committed && row.HitPointsAfter == 0)?.TargetPose : null;
    internal ClassicActionPose? ActorActionPose(int serial)
    {
        if (Phase == ClassicCombatPhase.AttackPending && PendingAction is { } action && action.Map == _player.MapPath)
        {
            if (action.ActorHandle == serial + 1) return new(action.Attack.Path, action.Attack.Frame(action.Elapsed), action.TargetRotation);
            if (action.ActorHandle == PlayerHandle && action.TargetSerial == serial) return action.TargetPose;
        }
        return Dead(serial) ? _actions.Last(row => row.Map == _player.MapPath && row.TargetSerial == serial && row.Committed && row.HitPointsAfter == 0).TargetPose : null;
    }
    internal event Action<string>? ActionChanged;
    private readonly Dictionary<string, ClassicCombatParticipant> _participants = new(StringComparer.Ordinal);
    private readonly List<ClassicCombatRequest> _requests = [];
    private long _nextRequest;
    internal ClassicCombatPhase Phase { get; private set; }
    internal int PlayerActionPoints { get; private set; }
    internal int TurnHandle { get; private set; }
    internal long TurnNumber { get; private set; }
    internal string? Failure { get; private set; }
    // Unknown admission/legacy state is not equivalent to an empty UI or peace.
    internal bool? Active => Phase switch
    {
        ClassicCombatPhase.Unknown or ClassicCombatPhase.Blocked => null,
        ClassicCombatPhase.Exploration => false,
        _ => true,
    };
    internal IReadOnlyList<ClassicCombatRequest> Requests => _requests;
    internal event Action? CommittedExit;
    private static string Key(string map, int serial) => map + ":" + serial;

    internal ClassicCombatEncounter(ClassicPlayerSession player, ClassicAttackModeCatalog? modes,
        bool freshCampaign, ClassicCombatEncounterSave? saved, ClassicCombatContracts? execution = null)
    {
        _player = player; _modes = modes; _execution = execution;
        execution?.Validate();
        if (modes is not null)
        {
            modes.Validate();
            if (player.Choice.Campaign != "fallout-2" || modes.ExactBuild != "fallout2-retail-1.02")
                throw new NotSupportedException("Combat contract does not belong to this campaign.");
            _contractIdentity = ClassicPremadeReader.Hash(JsonSerializer.SerializeToUtf8Bytes(new { modes, execution }));
        }
        PlayerActionPoints = player.Stats.ActionPoints;
        // A newly created campaign has no previous encounter. Continue must
        // restore one; legacy saves cannot silently acquire an idle combat flag.
        Phase = freshCampaign && modes is not null ? ClassicCombatPhase.Exploration : ClassicCombatPhase.Unknown;
        Failure = Phase == ClassicCombatPhase.Unknown ? "Combat lifecycle requires an admitted fresh campaign or saved encounter." : null;
        if (saved is not null) Restore(saved);
    }

    internal void BindActionArt(Func<string> playerIdle, Func<uint, string> resolveCritter)
    {
        // Source bytes are requalified on load; resolved outcomes are not rerolled.
        foreach (var action in _actions)
            foreach (var clip in new[] { action.Attack, action.Reaction })
                if (ClassicActionClip.Read(path => _player.Catalog.Read(path, out _), clip.Path) != clip)
                    throw new InvalidDataException("Saved attack source animation changed.");
        _playerIdle = playerIdle; _resolveCritter = resolveCritter;
    }

    internal void AdvanceAction(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (Phase != ClassicCombatPhase.AttackPending || PendingAction is not { } action ||
            _playerIdle is null || _resolveCritter is null) return;
        action = action with { Elapsed = Math.Min(action.Duration, action.Elapsed + seconds) };
        _actions[^1] = action;
        if (action.Elapsed < action.Duration) return;
        if (_player.Inventory.Held is not null)
        { Block("Held item changed before prepared attack completion."); return; }
        try
        {
            if (action.ActorHandle == PlayerHandle)
            {
                var key = Key(action.Map, action.TargetSerial); var target = _participants[key];
                if (target.HitPoints != action.HitPointsBefore) { Block("Target changed before attack completion."); return; }
                if (action.HitPointsAfter < action.HitPointsBefore)
                    _player.Actors.Dispatch(target.Serial, 14, action.HitPointsBefore - action.HitPointsAfter, PlayerHandle, hitPoints: action.HitPointsAfter);
                if (action.HitPointsAfter == 0 && _player.Actors.Dispatch(target.Serial, 18, sourceObject: PlayerHandle, hitPoints: action.HitPointsAfter))
                { Block("Source destroy callback overrides unowned lethal consequences."); return; }
                _participants[key] = target with { HitPoints = action.HitPointsAfter, Results = action.HitPointsAfter == 0 ? target.Results | 0x80 : target.Results };
                if (action.HitPointsAfter == 0)
                {
                    var declaration = new ClassicCombatant(_player, target);
                    _deaths.Add(new(action.Map, action.TargetSerial, action.Sequence, declaration.Experience, declaration.KillType));
                    _pendingExperience = checked(_pendingExperience + declaration.Experience);
                    _player.Doors.RemoveActorTimers(target.Serial);
                    _player.Actors.Retire(target.Serial);
                }
            }
            else _player.CommitCombatHitPoints(action.HitPointsBefore, action.HitPointsAfter);
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or FileNotFoundException or OverflowException)
        { Block("Attack consequence: " + error.Message); return; }
        _actions[^1] = action with { Committed = true };
        var request = _requests.FindIndex(row => row.Sequence == action.Sequence);
        _requests[request] = _requests[request] with { Boundary = "completed" };
        Phase = action.ActorHandle == PlayerHandle ? ClassicCombatPhase.PlayerTurn : ClassicCombatPhase.ActorTurn;
        Failure = null;
        WorldChanged?.Invoke();
        ActionChanged?.Invoke(action.Hit ? $"{(action.ActorHandle == PlayerHandle ? "Hit" : "Enemy hit")} for {action.HitPointsBefore - action.HitPointsAfter}. HP {action.HitPointsAfter}." : "Attack missed.");
        if (_player.HitPoints == 0) { Phase = ClassicCombatPhase.Defeated; ActionChanged?.Invoke("You have died. Return to the menu to start or load a game."); return; }
        if (action.ActorHandle != PlayerHandle) ContinueEnemyTurn();
        else if (_engaged.All(key => _participants[key].HitPoints == 0))
        {
            try { CommitCompletedExit(); }
            catch (NotSupportedException error) { Block(error.Message); }
        }
    }

    private void Block(string reason)
    { Phase = ClassicCombatPhase.Blocked; Failure = reason; ActionChanged?.Invoke(reason); }

    private void StartTurn(int handle, int ap)
    {
        TurnHandle = handle; TurnNumber = _turns.Count + 1;
        _turns.Add(new(TurnNumber, handle, ap, ap));
        if (handle == PlayerHandle) PlayerActionPoints = ap;
        Phase = handle == PlayerHandle ? ClassicCombatPhase.PlayerTurn : ClassicCombatPhase.ActorTurn;
    }

    private void SpendTurnAp(int ap)
    {
        _turns[^1] = _turns[^1] with { EndingAp = ap };
        if (TurnHandle == PlayerHandle) PlayerActionPoints = ap;
        else
        {
            var key = Key(_player.MapPath, TurnHandle - 1);
            _participants[key] = _participants[key] with { ActionPoints = ap };
        }
    }

    internal ClassicCombatParticipant SourceParticipant(int serial)
    {
        var key = Key(_player.MapPath, serial);
        if (_participants.TryGetValue(key, out var current)) return current;
        var source = _player.Level.Objects.TopLevelObjects.SingleOrDefault(row => row.Serial == serial)
            ?? throw new InvalidDataException("Combat target is absent from the active source MAP.");
        current = ReadParticipant(_player.Level, source);
        _participants.Add(key, current);
        return current;
    }

    internal bool IsLivingTarget(int serial)
    {
        var source = _player.Level.Objects.TopLevelObjects.SingleOrDefault(row => row.Serial == serial);
        if (source is null || source.Prototype.ObjectType != 1 || source.Elevation != _player.Elevation || (source.Flags & 1) != 0) return false;
        var actor = SourceParticipant(serial);
        return actor.HitPoints > 0 && (actor.Results & 0x80) == 0;
    }

    private ClassicCombatParticipant ReadParticipant(ClassicMapLevel level, Fallout1NativeMapObject source)
    {
        if (_modes is null || _player.Choice.Campaign != "fallout-2")
            throw new NotSupportedException("This campaign's critter combat contract is unavailable.");
        if (source.Prototype.ObjectType != 1 || source.Prototype.LogicalPath is null || source.InstanceValues.Count != 11)
            throw new InvalidDataException("Combat participant requires a source critter PRO and eleven MAP instance words.");
        var pro = _player.Catalog.Read(source.Prototype.LogicalPath, out _);
        // Neutral FO2 file contract: common/critter header 44 bytes, flags,
        // 35 base stats, 35 bonus stats, 18 skills, four trailing attributes.
        if (pro.Length is not (412 or 416) || BinaryPrimitives.ReadInt32BigEndian(pro) != source.Pid)
            throw new NotSupportedException("Source critter PRO layout/identity is not admitted.");
        int Stat(int index) => checked(BinaryPrimitives.ReadInt32BigEndian(pro.AsSpan(48 + index * 4)) +
            BinaryPrimitives.ReadInt32BigEndian(pro.AsSpan(188 + index * 4)));
        var instance = ClassicCritterInstance.Read(source);
        var maximum = Stat(8);
        var ai = _player.Actors?.Trait(source.Serial, 5, instance.AiPacket) ?? instance.AiPacket;
        var team = _player.Actors?.Trait(source.Serial, 6, instance.Team) ?? instance.Team;
        // Authored MAPs can retain AP above the current PRO maximum (the
        // owned temple scorpions retain6 with maximum5). Preserve that prefix;
        // entering an actual turn refills through the turn owner.
        if (maximum <= 0 || instance.ActionPoints < 0 || instance.HitPoints < 0 || ai < 0 || team < 0)
            throw new InvalidDataException($"Source critter {source.Serial} AP {instance.ActionPoints}/{maximum}, HP {instance.HitPoints}, AI {ai} or team {team} is invalid.");
        return new(level.Path, level.Sha256, source.Serial, source.ObjectId, source.Pid,
            ClassicPremadeReader.Hash(pro), source.Tile, source.Elevation, instance.HitPoints, instance.ActionPoints,
            maximum, instance.Results, ai, team,
            _player.Actors?.Trait(source.Serial, 10, source.Rotation) ?? source.Rotation);
    }

    internal string RequestPlayerAttack(int serial)
    {
        if (_player.Moving) throw new InvalidOperationException("Finish the current hex before attacking.");
        if (Active != false && Phase != ClassicCombatPhase.PlayerTurn)
            throw new NotSupportedException(Failure ?? "The current encounter still owns its pending action.");
        var entering = Phase == ClassicCombatPhase.Exploration;
        if (!IsLivingTarget(serial)) throw new InvalidOperationException("Attack target is not a living source critter on this elevation.");
        var target = SourceParticipant(serial);
        ClassicAttackIntent? intent = null;
        string boundary;
        var held = _player.Inventory.Held;
        if (held?.Definition.Weapon is { } weapon)
        {
            var source = new ClassicAttackSource(held.Definition.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture),
                weapon.MinimumDamage, weapon.MaximumDamage, weapon.DamageType, weapon.PrimaryRange,
                weapon.PrimaryAp, weapon.Animation, ClassicAttackOwner.EngineRollRequired);
            intent = ClassicAttackOwner.Prepare("player", Key(target.Map, target.Serial),
                ClassicHexGrid.Distance(_player.Tile, target.Tile), PlayerActionPoints, source);
            boundary = intent.Boundary.ToString();
        }
        else
        {
            var mode = _modes!.Require("basic-punch");
            var source = ClassicAttackModeOwner.PrepareSource(mode, ClassicCombatAction.PlayerMaximumDamage(_player) - 2);
            intent = ClassicAttackOwner.Prepare("player", Key(target.Map, target.Serial),
                ClassicHexGrid.Distance(_player.Tile, target.Tile), PlayerActionPoints, source);
            boundary = intent.Boundary.ToString();
        }
        var request = new ClassicCombatRequest(_nextRequest++, _player.MapPath, PlayerHandle, serial + 1, intent, boundary);
        _requests.Add(request);
        // Once a request is retained, a participant-admission failure must also
        // be retained. Never leave the saved request looking like exploration
        // succeeded after a later PRO/layout check threw.
        Phase = ClassicCombatPhase.Blocked;
        try
        {
            _player.Actors.RequireReady(serial);
            if (entering)
            {
                PlayerActionPoints = ClassicCombatTurnOwner.BeginPlayerTurn(PlayerActionPoints, _player.Stats.ActionPoints);
                _engaged.Clear(); _engaged.Add(Key(target.Map, target.Serial));
                _participants[Key(target.Map, target.Serial)] = target with { ActionPoints = 0 };
                target = SourceParticipant(serial);
            }
            else if (!_engaged.Contains(Key(target.Map, target.Serial)))
                throw new NotSupportedException("Attacking another actor requires the additional-participant acquisition owner.");
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or FileNotFoundException or OverflowException)
        {
            Failure = "Attack retained; encounter admission refused: " + error.Message;
            return Failure;
        }
        if (entering) StartTurn(PlayerHandle, PlayerActionPoints);
        Phase = ClassicCombatPhase.PlayerTurn;
        if (intent?.Boundary is ClassicAttackBoundary.OutOfRange or ClassicAttackBoundary.InsufficientActionPoints)
            return Failure = boundary;
        try
        {
            _player.Actors.RequireReady(serial);
            if (_execution is null || _playerIdle is null || _resolveCritter is null)
                throw new NotSupportedException("Attack execution contracts and source animation binding are required.");
            var prepared = ClassicCombatAction.PreparePunch(_player, target, _modes!.Require("basic-punch"),
                _execution, request.Sequence, PlayerActionPoints, _playerIdle, _resolveCritter);
            _player.CommitRandom(prepared.Random);
            _actions.Add(prepared.Action with { TurnNumber = TurnNumber });
            SpendTurnAp(prepared.Action.ActionPointsAfter);
            _player.FaceCombatTarget(target.Tile);
            Failure = prepared.Action.Boundary;
            Phase = Failure is null ? ClassicCombatPhase.AttackPending : ClassicCombatPhase.Blocked;
            _requests[^1] = request with { Boundary = Failure is null ? "executing" : "resolved-effect-unbound" };
            return Failure ?? $"Basic punch queued. AP {PlayerActionPoints}.";
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or FileNotFoundException or OverflowException)
        {
            Failure = error.Message;
            _requests[^1] = request with { Boundary = "preflight-refused: " + Failure };
            return Failure;
        }
    }

    internal void ApplyActorTraits(string map, IReadOnlyList<ClassicIntTraitAssignment> changes)
    {
        foreach (var row in changes)
        {
            var key = Key(map, row.ObjectHandle - 1);
            if (!_participants.TryGetValue(key, out var actor)) continue;
            _participants[key] = row.Trait switch
            {
                5 => actor with { AiPacket = row.Amount },
                6 => actor with { Team = row.Amount },
                10 => actor with { Rotation = row.Amount },
                _ => actor,
            };
        }
    }

    internal void RequireExploration()
    {
        if (Active == true || Phase == ClassicCombatPhase.Blocked || Phase == ClassicCombatPhase.Defeated)
            throw new NotSupportedException(Failure ?? "Finish the active encounter before exploration or a map transition.");
    }

    internal string EndTurn()
    {
        if (Phase is ClassicCombatPhase.Unknown or ClassicCombatPhase.Blocked || Phase == ClassicCombatPhase.Defeated)
            throw new NotSupportedException(Failure ?? "Combat state is unknown.");
        if (Phase == ClassicCombatPhase.Exploration) return "No active combat turn.";
        if (Phase != ClassicCombatPhase.PlayerTurn)
            throw new NotSupportedException(Failure ?? "Complete the pending action before ending the turn.");
        if (!_actions.Any(row => row.ActorHandle == PlayerHandle && row.Committed && row.Map == _player.MapPath))
            throw new NotSupportedException("A refused selection has not established an executed attack/retaliation prefix.");
        var opponent = _engaged.Select(key => _participants[key]).SingleOrDefault(row => row.HitPoints > 0);
        if (opponent is null) { CommitCompletedExit(); return "Combat ended."; }
        var declaration = new ClassicCombatant(_player, opponent);
        if (declaration.Stat(13) > (2 * _player.Stats.Special[1] + (_player.Choice.Character.Traits.Contains(5) ? 5 : 0)) ||
            declaration.Stat(13) == (2 * _player.Stats.Special[1] + (_player.Choice.Character.Traits.Contains(5) ? 5 : 0)) && declaration.Stat(6) > _player.Stats.Special[6])
            throw new NotSupportedException("Next-round initiative needs the faster enemy's consecutive-turn scheduling owner.");
        var turn = ClassicCombatTurnOwner.BeginTargetTurn(opponent.ActionPoints, opponent.MaximumActionPoints,
            opponent.AiPacket, opponent.Team, true, opponent.Tile, _player.Tile, ClassicHexGrid.Neighbors(opponent.Tile).ToHashSet());
        // The retained player's actual attack is the retaliation source. No neutral actor is acquired here.
        _participants[Key(opponent.Map, opponent.Serial)] = opponent with { ActionPoints = turn.ActionPoints };
        if (_player.Actors.Dispatch(opponent.Serial, 13, 4))
            throw new NotSupportedException("Source actor overrides its ordinary AI turn.");
        StartTurn(opponent.Serial + 1, turn.ActionPoints);
        ContinueEnemyTurn();
        return Failure ?? "Enemy turn.";
    }

    private void ContinueEnemyTurn()
    {
        var actor = _participants[Key(_player.MapPath, TurnHandle - 1)];
        if (actor.ActionPoints < 3)
        {
            try { RequireAcquisition(); }
            catch (NotSupportedException error) { Block(error.Message); return; }
            _player.Doors.CompleteCombatRound();
            StartTurn(PlayerHandle, _player.Stats.ActionPoints);
            ActionChanged?.Invoke($"Your turn. AP {PlayerActionPoints}."); return;
        }
        try
        {
            if (_execution is null || _playerIdle is null || _resolveCritter is null)
                throw new NotSupportedException("Enemy turn has no execution/source art binding.");
            var prepared = ClassicEnemyAttack.Prepare(_player, actor, _execution, _nextRequest, PlayerActionPoints, _playerIdle, _resolveCritter);
            _player.CommitRandom(prepared.Random);
            _requests.Add(new(_nextRequest++, _player.MapPath, actor.Serial + 1, PlayerHandle, null, "executing"));
            _actions.Add(prepared.Action with { TurnNumber = TurnNumber });
            SpendTurnAp(prepared.Action.ActionPointsAfter);
            _participants[Key(actor.Map, actor.Serial)] = _participants[Key(actor.Map, actor.Serial)] with { Rotation = prepared.Action.TargetRotation };
            Phase = ClassicCombatPhase.AttackPending;
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or FileNotFoundException or OverflowException)
        { Block("Enemy turn: " + error.Message); }
    }

    private void RequireAcquisition()
    {
        foreach (var source in _player.Level.Objects.TopLevelObjects.Where(row => row.Prototype.ObjectType == 1 &&
            row.Elevation == _player.Elevation && (row.Flags & 1) == 0))
            if (!_engaged.Contains(Key(_player.MapPath, source.Serial)) && IsLivingTarget(source.Serial))
                throw new NotSupportedException("Combat sequence requires source perception/hostility acquisition for additional living actors.");
    }

    internal void CommitCompletedExit()
    {
        if (Phase is not (ClassicCombatPhase.PlayerTurn or ClassicCombatPhase.ActorTurn) || PendingAction is not null ||
            _engaged.Any(key => _participants[key].HitPoints > 0))
            throw new NotSupportedException("Combat exit still has a living opponent or unresolved action.");
        RequireAcquisition();
        _player.Doors.CompleteCombatRound();
        _player.AwardCombatExperience(_pendingExperience);
        _awardedExperience = checked(_awardedExperience + _pendingExperience); _pendingExperience = 0;
        Phase = ClassicCombatPhase.Exploration; TurnHandle = 0; TurnNumber = 0; Failure = null;
        PlayerActionPoints = _player.Stats.ActionPoints;
        CommittedExit?.Invoke(); WorldChanged?.Invoke();
        ActionChanged?.Invoke("Combat ended.");
    }

    internal ClassicCombatEncounterSave Save() => new(Schema, _contractIdentity, Phase, PlayerActionPoints,
        TurnHandle, TurnNumber, _nextRequest, _participants.Values.OrderBy(row => row.Map, StringComparer.Ordinal)
            .ThenBy(row => row.Serial).ToArray(), _requests.ToArray(), Failure, _actions.ToArray(), _turns.ToArray(), _deaths.ToArray(), _pendingExperience, _player.HitPoints, _awardedExperience, _engaged.Order(StringComparer.Ordinal).ToArray());

    internal void Restore(ClassicCombatEncounterSave saved)
    {
        if (saved.Schema != Schema || saved.ContractIdentity != _contractIdentity || !Enum.IsDefined(saved.Phase) ||
            saved.Participants is null || saved.Requests is null || saved.PlayerActionPoints < 0 ||
            saved.PlayerActionPoints > _player.Stats.ActionPoints || saved.NextRequest < 0 || saved.TurnNumber < 0 ||
            saved.PendingExperience < 0 || saved.AwardedExperience < 0 || saved.Phase is ClassicCombatPhase.Unknown or ClassicCombatPhase.Blocked && saved.Failure is null)
            throw new InvalidDataException("Saved combat lifecycle/contract is invalid.");
        var originals = new Dictionary<string, ClassicCombatParticipant>(StringComparer.Ordinal);
        foreach (var row in saved.Participants)
        {
            var level = _player.Catalog.Load(row.Map);
            var source = level.Objects.TopLevelObjects.SingleOrDefault(item => item.Serial == row.Serial)
                ?? throw new InvalidDataException("Saved combat participant disappeared.");
            var original = ReadParticipant(level, source);
            if (original with { ActionPoints = row.ActionPoints, HitPoints = row.HitPoints, Results = row.Results, Rotation = row.Rotation } != row ||
                row.HitPoints < 0 || row.HitPoints > original.HitPoints || row.Rotation is < 0 or >= 6 ||
                row.Results != (row.HitPoints == 0 ? original.Results | 0x80 : original.Results) ||
                row.ActionPoints < 0 || row.ActionPoints > original.MaximumActionPoints && row.ActionPoints != original.ActionPoints ||
                !_participants.TryAdd(Key(row.Map, row.Serial), row))
                throw new InvalidDataException("Saved combat participant differs from its source identity/state.");
            originals.Add(Key(row.Map, row.Serial), original);
        }
        foreach (var key in saved.Engaged ?? [])
            if (!_participants.ContainsKey(key) || !_engaged.Add(key)) throw new InvalidDataException("Saved engaged actor identity is invalid.");
        long sequence = 0;
        foreach (var request in saved.Requests)
        {
            if (request.Sequence != sequence++ || string.IsNullOrWhiteSpace(request.Boundary) ||
                (request.ActorHandle == PlayerHandle
                    ? !_participants.ContainsKey(Key(request.Map, request.TargetHandle - 1))
                    : request.TargetHandle != PlayerHandle || !_participants.ContainsKey(Key(request.Map, request.ActorHandle - 1))))
                throw new InvalidDataException("Saved attack request prefix is invalid.");
            _requests.Add(request);
        }
        if (sequence != saved.NextRequest) throw new InvalidDataException("Saved combat request cursor differs from its prefix.");
        var hp = originals.ToDictionary(row => row.Key, row => row.Value.HitPoints);
        var playerHp = _player.Stats.HitPoints;
        var expectedDeaths = new List<ClassicCombatDeath>();
        var actions = saved.Actions ?? [];
        long previousAction = -1;
        long turnNumber = 0;
        foreach (var turn in saved.Turns ?? [])
        {
            if (turn.Number != ++turnNumber || turn.Handle != PlayerHandle && !originals.ContainsKey(Key(_player.MapPath, turn.Handle - 1)))
                throw new InvalidDataException("Saved turn cursor or actor is invalid.");
            var maximumAp = turn.Handle == PlayerHandle ? _player.Stats.ActionPoints : originals[Key(_player.MapPath, turn.Handle - 1)].MaximumActionPoints;
            if (turn.StartingAp != maximumAp || turn.EndingAp < 0 || turn.EndingAp > maximumAp)
                throw new InvalidDataException("Saved turn AP does not match its source maximum.");
            var ap = turn.StartingAp;
            foreach (var action in actions.Where(row => row.TurnNumber == turn.Number))
            {
                if (_execution is null) throw new InvalidDataException("Saved attack has no execution contract.");
                var maximumDamage = action.ActorHandle == PlayerHandle ? ClassicCombatAction.PlayerMaximumDamage(_player) :
                    new ClassicCombatant(_player, originals[Key(action.Map, action.ActorHandle - 1)]).Stat(11) + 2;
                action.Validate(_execution, maximumAp, maximumDamage);
                var request = _requests.SingleOrDefault(row => row.Sequence == action.Sequence);
                var target = action.ActorHandle == PlayerHandle ? Key(action.Map, action.TargetSerial) : null;
                if (action.Sequence <= previousAction || _actions.Any(row => !row.Committed) || action.ActorHandle != turn.Handle ||
                    request is null || request.ActorHandle != action.ActorHandle || request.Map != action.Map ||
                    request.TargetHandle != (target is null ? PlayerHandle : action.TargetSerial + 1) ||
                    action.HitPointsBefore != (target is null ? playerHp : hp.GetValueOrDefault(target, -1)) ||
                    action.ActionPointsBefore != ap || action.Committed != (request.Boundary == "completed"))
                    throw new InvalidDataException("Saved action result differs from its HP/AP/request prefix.");
                ap = action.ActionPointsAfter;
                if (action.Committed)
                {
                    if (target is null) playerHp = action.HitPointsAfter;
                    else
                    {
                        hp[target] = action.HitPointsAfter;
                        if (action.HitPointsAfter == 0)
                        {
                            var declaration = new ClassicCombatant(_player, originals[target]);
                            expectedDeaths.Add(new(action.Map, action.TargetSerial, action.Sequence, declaration.Experience, declaration.KillType));
                        }
                    }
                }
                _actions.Add(action); previousAction = action.Sequence;
            }
            if (ap != turn.EndingAp) throw new InvalidDataException("Saved turn AP has no action prefix.");
            _turns.Add(turn);
        }
        if (_actions.Count != actions.Length || _participants.Values.Any(row => row.HitPoints != hp[Key(row.Map, row.Serial)]) ||
            saved.PlayerHitPoints >= 0 && saved.PlayerHitPoints != playerHp ||
            !(saved.Deaths ?? []).SequenceEqual(expectedDeaths) ||
            saved.PendingExperience + saved.AwardedExperience != expectedDeaths.Sum(row => row.Experience) ||
            saved.Phase == ClassicCombatPhase.Exploration && saved.PendingExperience != 0)
            throw new InvalidDataException("Saved HP/death/reward prefix is inconsistent.");
        _deaths.AddRange(expectedDeaths); _pendingExperience = saved.PendingExperience; _awardedExperience = saved.AwardedExperience;
        var active = saved.Phase is ClassicCombatPhase.PlayerTurn or ClassicCombatPhase.AttackPending or ClassicCombatPhase.ActorTurn or ClassicCombatPhase.Defeated;
        if (active && (_turns.Count == 0 || saved.TurnNumber != _turns[^1].Number || saved.TurnHandle != _turns[^1].Handle) ||
            saved.Phase == ClassicCombatPhase.AttackPending && PendingAction is null ||
            PendingAction is not null && saved.Phase is not (ClassicCombatPhase.AttackPending or ClassicCombatPhase.Blocked) ||
            saved.Phase == ClassicCombatPhase.Defeated != (playerHp == 0) ||
            saved.Phase == ClassicCombatPhase.Exploration && (saved.TurnHandle != 0 || saved.TurnNumber != 0) ||
            _turns.LastOrDefault(row => row.Handle == PlayerHandle) is { } playerTurn &&
            saved.PlayerActionPoints != (saved.Phase == ClassicCombatPhase.Exploration ? _player.Stats.ActionPoints : playerTurn.EndingAp))
            throw new InvalidDataException("Saved phase/turn/action cursor is inconsistent.");
        foreach (var actor in _participants.Values)
            if (_turns.LastOrDefault(row => row.Handle == actor.Serial + 1) is { } actorTurn && actor.ActionPoints != actorTurn.EndingAp)
                throw new InvalidDataException("Saved actor AP differs from its last turn.");
        Phase = saved.Phase; PlayerActionPoints = saved.PlayerActionPoints; TurnHandle = saved.TurnHandle;
        TurnNumber = saved.TurnNumber; _nextRequest = saved.NextRequest; Failure = saved.Failure;
    }
}
