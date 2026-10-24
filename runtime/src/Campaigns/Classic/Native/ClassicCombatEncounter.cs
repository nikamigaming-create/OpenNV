using System.Buffers.Binary;
using System.Text.Json;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

internal enum ClassicCombatPhase { Unknown, Exploration, PlayerTurn, AttackPending, ActorTurn, Blocked }
internal sealed record ClassicCombatParticipant(string Map, string MapSha256, int Serial, int ObjectId,
    int Pid, string PrototypeSha256, int Tile, int Elevation, int HitPoints, int ActionPoints,
    int MaximumActionPoints, int Results, int AiPacket, int Team);
internal sealed record ClassicCombatRequest(long Sequence, string Map, int ActorHandle, int TargetHandle,
    ClassicAttackIntent? Intent, string Boundary);
internal sealed record ClassicCombatEncounterSave(string Schema, string? ContractIdentity, ClassicCombatPhase Phase,
    int PlayerActionPoints, int TurnHandle, long TurnNumber, long NextRequest,
    ClassicCombatParticipant[] Participants, ClassicCombatRequest[] Requests, string? Failure,
    ClassicCombatAction[]? Actions = null);

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
    private Func<string>? _playerIdle;
    private Func<uint, string>? _resolveCritter;
    internal ClassicCombatAction? PendingAction => _actions.LastOrDefault(row => !row.Committed);
    internal ClassicActionPose? PlayerActionPose => Phase == ClassicCombatPhase.AttackPending ? PendingAction?.PlayerPose : null;
    internal ClassicActionPose? ActorActionPose(int serial) => Phase == ClassicCombatPhase.AttackPending && PendingAction is { } action &&
        action.Map == _player.MapPath && action.TargetSerial == serial ? action.TargetPose : null;
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
        var key = Key(action.Map, action.TargetSerial);
        var target = _participants[key];
        if (target.HitPoints != action.HitPointsBefore || _player.Inventory.Held is not null ||
            _playerIdle()[..^6] + "aq.frm" != action.Attack.Path)
        {
            Phase = ClassicCombatPhase.Blocked;
            Failure = "Prepared attack target or held-item state changed before completion.";
            ActionChanged?.Invoke(Failure); return;
        }
        // Actual queued source frames complete before HP is published. Basic
        // punch consumes zero ammunition; firearm expenditure is not claimed.
        _participants[key] = target with { HitPoints = action.HitPointsAfter };
        _actions[^1] = action with { Committed = true };
        var request = _requests.FindIndex(row => row.Sequence == action.Sequence);
        _requests[request] = _requests[request] with { Boundary = "completed" };
        Phase = ClassicCombatPhase.PlayerTurn;
        Failure = null;
        ActionChanged?.Invoke(action.Hit ? $"Hit for {action.HitPointsBefore - action.HitPointsAfter}. Target HP {action.HitPointsAfter}. AP {PlayerActionPoints}." : $"Miss. AP {PlayerActionPoints}.");
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
        var words = source.InstanceValues;
        var maximum = Stat(8);
        if (maximum <= 0 || words[2] < 0 || words[2] > maximum || words[8] < 0 || words[5] < 0 || words[6] < 0)
            throw new InvalidDataException("Source critter AP, HP, AI packet or team is invalid.");
        return new(level.Path, level.Sha256, source.Serial, source.ObjectId, source.Pid,
            ClassicPremadeReader.Hash(pro), source.Tile, source.Elevation, words[8], words[2],
            maximum, words[3], words[5], words[6]);
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
            var source = ClassicAttackModeOwner.PrepareSource(mode, Math.Max(_player.Stats.Special[0] - 5, 1));
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
            var liveActors = _player.Level.Objects.TopLevelObjects.Where(row => row.Prototype.ObjectType == 1 &&
                row.Elevation == _player.Elevation && (row.Flags & 1) == 0).ToArray();
            // Scripted starts and additional acquisition cannot be approximated
            // by a two-person duel. Preserve the request without that start.
            if (liveActors.Any(row => row.ScriptId != uint.MaxValue) || liveActors.Count(row => IsLivingTarget(row.Serial)) != 1)
                throw new NotSupportedException("Source combat-start scripts or additional participant acquisition are not yet owned.");
            if (entering)
            {
                PlayerActionPoints = ClassicCombatTurnOwner.BeginPlayerTurn(PlayerActionPoints, _player.Stats.ActionPoints);
                // The initial combat lifecycle clears participant AP before its
                // first turn. Later turns must be supplied by the actual AI owner.
                foreach (var actor in liveActors)
                {
                    var participant = SourceParticipant(actor.Serial);
                    _participants[Key(participant.Map, participant.Serial)] = participant with { ActionPoints = 0 };
                }
                target = SourceParticipant(serial);
            }
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or FileNotFoundException or OverflowException)
        {
            Failure = "Attack retained; encounter admission refused: " + error.Message;
            return Failure;
        }
        TurnHandle = PlayerHandle; if (entering) TurnNumber = 1;
        Phase = ClassicCombatPhase.PlayerTurn;
        if (intent?.Boundary is ClassicAttackBoundary.OutOfRange or ClassicAttackBoundary.InsufficientActionPoints)
            return Failure = boundary;
        try
        {
            if (_execution is null || _playerIdle is null || _resolveCritter is null)
                throw new NotSupportedException("Attack execution contracts and source animation binding are required.");
            var prepared = ClassicCombatAction.PreparePunch(_player, target, _modes!.Require("basic-punch"),
                _execution, request.Sequence, PlayerActionPoints, _playerIdle, _resolveCritter);
            _player.CommitRandom(prepared.Random);
            _actions.Add(prepared.Action);
            PlayerActionPoints = prepared.Action.ActionPointsAfter;
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

    internal void RequireExploration()
    {
        if (Active == true || Phase == ClassicCombatPhase.Blocked)
            throw new NotSupportedException(Failure ?? "Finish the active encounter before exploration or a map transition.");
    }

    internal string EndTurn()
    {
        if (Phase is ClassicCombatPhase.Unknown or ClassicCombatPhase.Blocked)
            throw new NotSupportedException(Failure ?? "Combat state is unknown.");
        if (Phase == ClassicCombatPhase.Exploration) return "No active combat turn.";
        if (Phase != ClassicCombatPhase.PlayerTurn)
            throw new NotSupportedException(Failure ?? "Complete the pending action before ending the turn.");
        // Being selected by the player is not itself an NPC attack request.
        // BeginTargetTurn's attackRequested argument needs the actual AI/script
        // owner; supplying true or false here would invent that decision.
        throw new NotSupportedException("Source target hostility/attack intent and AI packet execution are required before its turn can begin.");
    }

    internal void CommitCompletedExit()
    {
        // Only a connected action/AI owner may reach this once all source
        // participants have completed combat; no input shortcut forces exit.
        if (Phase != ClassicCombatPhase.ActorTurn || _requests.Any(row => row.Boundary != "completed"))
            throw new NotSupportedException("Combat exit has no completed source turn/action prefix.");
        if (_participants.Values.Any(row => row.Map == _player.MapPath && row.Elevation == _player.Elevation &&
            row.HitPoints > 0 && (row.Results & 0x80) == 0))
            throw new NotSupportedException("Combat-end eligibility for surviving participants is unresolved.");
        Phase = ClassicCombatPhase.Exploration; TurnHandle = 0; TurnNumber = 0; Failure = null;
        PlayerActionPoints = _player.Stats.ActionPoints;
        CommittedExit?.Invoke();
    }

    internal ClassicCombatEncounterSave Save() => new(Schema, _contractIdentity, Phase, PlayerActionPoints,
        TurnHandle, TurnNumber, _nextRequest, _participants.Values.OrderBy(row => row.Map, StringComparer.Ordinal)
            .ThenBy(row => row.Serial).ToArray(), _requests.ToArray(), Failure, _actions.ToArray());

    private void Restore(ClassicCombatEncounterSave saved)
    {
        if (saved.Schema != Schema || saved.ContractIdentity != _contractIdentity || !Enum.IsDefined(saved.Phase) ||
            saved.Participants is null || saved.Requests is null || saved.PlayerActionPoints < 0 ||
            saved.PlayerActionPoints > _player.Stats.ActionPoints || saved.NextRequest < 0 || saved.TurnNumber < 0 ||
            saved.Phase is ClassicCombatPhase.Unknown or ClassicCombatPhase.Blocked && saved.Failure is null)
            throw new InvalidDataException("Saved combat lifecycle/contract is invalid.");
        foreach (var row in saved.Participants)
        {
            var level = _player.Catalog.Load(row.Map);
            var source = level.Objects.TopLevelObjects.SingleOrDefault(item => item.Serial == row.Serial)
                ?? throw new InvalidDataException("Saved combat participant disappeared.");
            var original = ReadParticipant(level, source);
            if (original with { ActionPoints = row.ActionPoints, HitPoints = row.HitPoints } != row || row.HitPoints <= 0 || row.HitPoints > original.HitPoints ||
                row.ActionPoints < 0 || row.ActionPoints > original.MaximumActionPoints ||
                !_participants.TryAdd(Key(row.Map, row.Serial), row))
                throw new InvalidDataException("Saved combat participant differs from its source identity/state.");
        }
        long sequence = 0;
        foreach (var request in saved.Requests)
        {
            if (request.Sequence != sequence++ || request.ActorHandle != PlayerHandle ||
                !_participants.ContainsKey(Key(request.Map, request.TargetHandle - 1)) || string.IsNullOrWhiteSpace(request.Boundary))
                throw new InvalidDataException("Saved attack request prefix is invalid.");
            _requests.Add(request);
        }
        if (sequence != saved.NextRequest) throw new InvalidDataException("Saved combat request cursor differs from its prefix.");
        var expectedHp = _participants.Values.ToDictionary(row => Key(row.Map, row.Serial), row =>
            ReadParticipant(_player.Catalog.Load(row.Map), _player.Catalog.Load(row.Map).Objects.TopLevelObjects.Single(item => item.Serial == row.Serial)).HitPoints);
        long previousAction = -1;
        var expectedAp = _player.Stats.ActionPoints;
        foreach (var action in saved.Actions ?? [])
        {
            if (_execution is null) throw new InvalidDataException("Saved attacks require execution contracts.");
            action.Validate(_execution, _player.Stats.ActionPoints, Math.Max(_player.Stats.Special[0] - 5, 1) + 2);
            var request = _requests.SingleOrDefault(row => row.Sequence == action.Sequence);
            var key = Key(action.Map, action.TargetSerial);
            if (action.Sequence <= previousAction || _actions.Any(row => !row.Committed) || request is null ||
                request.Map != action.Map || request.TargetHandle != action.TargetSerial + 1 ||
                !expectedHp.TryGetValue(key, out var hp) || hp != action.HitPointsBefore ||
                action.Committed != (request.Boundary == "completed") || action.ActionPointsBefore != expectedAp)
                throw new InvalidDataException("Saved attack result prefix differs from its source participant.");
            if (action.Committed) expectedHp[key] = action.HitPointsAfter;
            expectedAp = action.ActionPointsAfter;
            _actions.Add(action); previousAction = action.Sequence;
        }
        if (_participants.Values.Any(row => row.HitPoints != expectedHp[Key(row.Map, row.Serial)]))
            throw new InvalidDataException("Saved target HP has no committed attack prefix.");
        if (_actions.Count != 0 && saved.PlayerActionPoints != _actions[^1].ActionPointsAfter ||
            saved.Phase == ClassicCombatPhase.AttackPending && PendingAction is null)
            throw new InvalidDataException("Saved action/AP cursor is inconsistent.");
        var active = saved.Phase is ClassicCombatPhase.PlayerTurn or ClassicCombatPhase.AttackPending or ClassicCombatPhase.ActorTurn;
        if (active && (saved.TurnNumber <= 0 || saved.Requests.Length == 0 ||
            saved.TurnHandle != PlayerHandle && !_participants.ContainsKey(Key(_player.MapPath, saved.TurnHandle - 1))) ||
            !active && saved.Phase != ClassicCombatPhase.Blocked && (saved.TurnHandle != 0 || saved.TurnNumber != 0))
            throw new InvalidDataException("Saved combat turn cursor is inconsistent.");
        Phase = saved.Phase; PlayerActionPoints = saved.PlayerActionPoints; TurnHandle = saved.TurnHandle;
        TurnNumber = saved.TurnNumber; _nextRequest = saved.NextRequest; Failure = saved.Failure;
    }
}
