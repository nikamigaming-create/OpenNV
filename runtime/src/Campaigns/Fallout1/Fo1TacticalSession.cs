using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Presentation.CharacterCreation;


using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.Campaigns.Classic;

namespace OpenNV.Runtime.Campaigns.Fallout1;

internal static class Fo1TacticalSessionNumericContracts
{
    // Immutable format, source-art, geometry, and acceptance contracts.
    // Runtime-tunable Fallout 1 behavior remains in the versioned runtime recipe.
    internal const float PresentationFloatNEgativE18Point0f = -18.0f;
    internal const float PresentationFloat0Point0001f = 0.0001f;
    internal const float PresentationFloat0Point012f = 0.012f;
    internal const float PresentationFloat0Point018f = 0.018f;
    internal const float PresentationFloat0Point022f = 0.022f;
    internal const float PresentationFloat0Point04f = 0.04f;
    internal const float PresentationFloat0Point055f = 0.055f;
    internal const float PresentationFloat0Point10f = 0.10f;
    internal const float PresentationFloat0Point18f = 0.18f;
    internal const float PresentationFloat0Point25f = 0.25f;
    internal const float PresentationFloat0Point28f = 0.28f;
    internal const float PresentationFloat0Point35f = 0.35f;
    internal const float PresentationFloat0Point46f = 0.46f;
    internal const float PresentationFloat0Point48f = 0.48f;
    internal const float PresentationFloat0Point55f = 0.55f;
    internal const float PresentationFloat0Point5f = 0.5f;
    internal const float PresentationFloat0Point68f = 0.68f;
    internal const float PresentationFloat0Point72f = 0.72f;
    internal const float PresentationFloat0Point76f = 0.76f;
    internal const float PresentationFloat0Point77f = 0.77f;
    internal const float PresentationFloat0Point78f = 0.78f;
    internal const float PresentationFloat0Point82f = 0.82f;
    internal const float PresentationFloat0Point85f = 0.85f;
    internal const float PresentationFloat0Point86f = 0.86f;
    internal const float PresentationFloat0Point91f = 0.91f;
    internal const float PresentationFloat0Point92f = 0.92f;
    internal const float PresentationFloat0Point94f = 0.94f;
    internal const float PresentationFloat0Point95f = 0.95f;
    internal const float PresentationFloat0Point96f = 0.96f;
    internal const float PresentationFloat0Point98f = 0.98f;
    internal const float PresentationFloat1Point0f = 1.0f;
    internal const float PresentationFloat1Point28f = 1.28f;
    internal const uint PresentationUint100U = 100U;
    internal const float PresentationFloat102Point0f = 102.0f;
    internal const float PresentationFloat104Point0f = 104.0f;
    internal const float PresentationFloat112Point0f = 112.0f;
    internal const float PresentationFloat128Point0f = 128.0f;
    internal const float PresentationFloat13Point0f = 13.0f;
    internal const int PresentationInt14 = 14;
    internal const float PresentationFloat145Point0f = 145.0f;
    internal const int PresentationInt16 = 16;
    internal const float PresentationFloat16Point0f = 16.0f;
    internal const float PresentationFloat170Point0f = 170.0f;
    internal const int PresentationInt18 = 18;
    internal const float PresentationFloat18Point0f = 18.0f;
    internal const float PresentationFloat180Point0f = 180.0f;
    internal const float PresentationFloat188Point0f = 188.0f;
    internal const float PresentationFloat20Point0f = 20.0f;
    internal const int PresentationInt200 = 200;
    internal const float PresentationFloat22Point0f = 22.0f;
    internal const float PresentationFloat23Point0f = 23.0f;
    internal const int PresentationInt24 = 24;
    internal const int PresentationInt25 = 25;
    internal const float PresentationFloat28Point0f = 28.0f;
    internal const float PresentationFloat30Point0f = 30.0f;
    internal const float PresentationFloat32Point0f = 32.0f;
    internal const float PresentationFloat44Point0f = 44.0f;
    internal const float PresentationFloat440Point0f = 440.0f;
    internal const float PresentationFloat48Point0f = 48.0f;
    internal const int PresentationInt50 = 50;
    internal const float PresentationFloat532Point0f = 532.0f;
    internal const float PresentationFloat542Point0f = 542.0f;
    internal const int PresentationInt6 = 6;
    internal const int PresentationInt64 = 64;
    internal const float PresentationFloat68Point0f = 68.0f;
    internal const float PresentationFloat72Point0f = 72.0f;
    internal const int PresentationInt8 = 8;
    internal const float PresentationFloat8Point0f = 8.0f;
    internal const float PresentationFloat875Point0f = 875.0f;
    internal const float PresentationFloat88Point0f = 88.0f;
    internal const float PresentationFloat9Point0f = 9.0f;
    internal const float PresentationFloat90Point0f = 90.0f;
    internal const float PresentationFloat910Point0f = 910.0f;
}

internal sealed record Fo1PlayerPresentationIdentity(
    string CharacterId,
    string CharacterName,
    string Sex,
    string IdentityMode,
    string OwnedGcdSha256,
    string OwnedBiographySha256,
    string OwnedPortraitFrmSha256)
{
    internal static Fo1PlayerPresentationIdentity FromProfile(
        Fo1CharacterProfile profile)
    {
        profile.Identity.Validate(profile);
        var result = new Fo1PlayerPresentationIdentity(
            profile.Identity.CharacterId,
            profile.Name,
            profile.Sex,
            profile.Identity.Mode,
            profile.Identity.OwnedGcdSha256,
            profile.Identity.OwnedBiographySha256,
            profile.Identity.OwnedPortraitFrmSha256);
        result.Validate(profile);
        return result;
    }

    internal void Validate(Fo1CharacterProfile profile)
    {
        profile.Identity.Validate(profile);
        if (CharacterName != profile.Name || Sex != profile.Sex ||
            CharacterId != profile.Identity.CharacterId ||
            IdentityMode != profile.Identity.Mode ||
            OwnedGcdSha256 != profile.Identity.OwnedGcdSha256 ||
            OwnedBiographySha256 != profile.Identity.OwnedBiographySha256 ||
            OwnedPortraitFrmSha256 != profile.Identity.OwnedPortraitFrmSha256)
            throw new InvalidOperationException(
                "Fallout 1 saved player presentation identity differs from its character.");
    }
}

internal sealed record Fo1PlayerPresentationBinding(
    string CharacterId,
    string CharacterName,
    string Sex,
    string IdentityMode,
    string PresentationMode,
    string OwnedGcdSha256,
    string OwnedBiographySha256,
    string OwnedPortraitFrmSha256,
    string DonorActorFormId,
    bool UsesOwnedDonor,
    bool ActorRootBound,
    bool AnimationBound,
    bool WeaponAttachmentsBound,
    bool WeaponVisualsSuppressed,
    string Limitation)
{
    internal Fo1PlayerPresentationIdentity Identity => new(
        CharacterId,
        CharacterName,
        Sex,
        IdentityMode,
        OwnedGcdSha256,
        OwnedBiographySha256,
        OwnedPortraitFrmSha256);

    internal object Report() => new
    {
        CharacterId,
        CharacterName,
        Sex,
        IdentityMode,
        PresentationMode,
        OwnedGcdSha256,
        OwnedBiographySha256,
        OwnedPortraitFrmSha256,
        DonorActorFormId,
        UsesOwnedDonor,
        ActorRootBound,
        AnimationBound,
        WeaponAttachmentsBound,
        WeaponVisualsSuppressed,
        Limitation,
        visualParity = false,
    };
}

internal partial class Fo1TacticalSession : Node
{
    private const string SaveSchema = "opennv-fo1-hex-save/v1";
    private const string ActiveMapSchema = "opennv-fo1-active-map/v1";
    private readonly Queue<int> _movement = new();
    private bool[] _walkable = [];
    private int[] _floorIds = [];
    private IReadOnlyDictionary<int, string> _floorNames = new Dictionary<int, string>();
    private bool[] _sourceWalkable = [];
    private int[] _sourceFloorIds = [];
    private IReadOnlyDictionary<int, string> _sourceFloorNames = new Dictionary<int, string>();
    private IReadOnlyList<Fo1Mob> _sourceMobs = [];
    private IReadOnlyList<MapInventoryHost> _sourceMapInventoryHosts = [];
    private string _sceneSha256 = "";
    private string _sourceMapSha256 = "";
    private bool _sourceMultihexCoverageComplete;
    private string _savePath = "";
    private int _maximumActionPoints;
    private int _doorTile;
    private SourceDoorContract? _sourceDoor;
    private bool _sourceDoorOpen;
    private int _entryTile;
    private int _hoveredTile = -1;
    private int _selectedTile = -1;
    private int _turn = 1;
    private int _actionPoints;
    private int _playerTile;
    private Fo1ExitGridTransitionContract? _exitGridTransition;
    private int? _activatedExitGridTile;
    private string? _destinationPresentationPath;
    private Fo1DestinationPresentationContract? _loadedDestinationPresentation;
    private string? _destinationInventoryInteractionPath;
    private Fo1DestinationInventoryInteractionContract? _destinationInventoryInteraction;
    private string? _destinationFlareUsePath;
    private Fo1DestinationFlareUseContract? _destinationFlareUse;
    private ClassicScriptState _destinationFlareScriptState = new();
    private bool _destinationFlareExpired;
    private int _classicScriptGameTime;
    private string? _destinationGenericDoorPath;
    private Fo1DestinationGenericDoorContract? _destinationGenericDoor;
    private ClassicDoorSession? _destinationGenericDoorSession;
    private ClassicDoorPlayback? _destinationGenericDoorPlayback;
    private bool _destinationGenericDoorOpen;
    private string? _destinationMedicLookPath;
    private Fo1DestinationMedicLookContract? _destinationMedicLook;
    private bool _destinationMedicLookViewed;
    private string? _destinationMedicDialogueProcedure;
    private ClassicPlayerStatusState _classicPlayerStatus = new();
    private string? _destinationReturnExitGridPath;
    private Fo1ExitGridTransitionContract? _destinationReturnExitGrid;
    private int? _activatedDestinationReturnExitGridTile;
    private bool _returnedToSource;
    private readonly HashSet<int> _returnInactiveDestinationHostSerials = [];
    private Node3D _playerToken = null!;
    private Sprite3D _playerSourceSprite = null!;
    private ActorModelSlice.LoadedActor? _ownedPlayer;
    private Fo1HexSceneLoader.PlayerPresentationSource? _ownedPlayerSource;
    private readonly Dictionary<string, Fo1HexSceneLoader.PlayerPresentationSource>
        _ownedPlayerDonorsBySex = new(StringComparer.OrdinalIgnoreCase);
    private Fo1PlayerPresentationBinding? _playerPresentationBinding;
    private Fo1PlayerPresentationIdentity? _pendingSavedPlayerPresentation;
    private bool _restoredCharacterFromSave;
    private Fo1ThirdPersonWeapon.LoadedWeapon? _ownedPlayerWeapon;
    private Fo1ThirdPersonWeapon.LoadedWeapon? _ownedPlayerMeleeWeapon;
    private JsonElement? _ownedPlayerWeaponSource;
    private JsonElement? _ownedPlayerMeleeWeaponSource;
    private AnimationPlayer? _playerAnimationPlayer;
    private string _playerIdleAnimation = "";
    private string _playerMoveAnimation = "";
    private string _playerRangedAttackAnimation = "";
    private string _playerMeleeAttackAnimation = "";
    private string _playerReloadAnimation = "";
    private bool _meleeWeaponEquipped;
    private int _playerMoveAnimationPlaybacks;
    private MeshInstance3D _hoverMarker = null!;
    private MultiMeshInstance3D _pathMarkers = null!;
    private Label _turnLabel = null!;
    private Label _hexLabel = null!;
    private Label _statusLabel = null!;
    private Label _controlsLabel = null!;
    private Control _debugHudRoot = null!;
    private Fo1ClassicHud? _classicHud;
    private Fo1ClassicInventoryScreen? _classicInventory;
    private Control _targetReticle = null!;
    private Label _targetReticleLabel = null!;
    private Control _fpsCrosshair = null!;
    private Camera3D? _camera;
    private Fo1TacticalCamera? _cameraRig;
    private Fo1CameraSaveState? _restoredCameraState;
    private string _status = "Select a highlighted floor hex to move";
    private PlayerProfile _playerProfile;
    private Fo1CharacterProfile? _characterProfile;
    private IReadOnlyList<Fo1Mob> _mobs = [];
    private readonly Dictionary<int, Fo1Mob> _mobsByTile = [];
    private Fo1Mob? _selectedMob;
    private int _playerHitPoints;
    private int _attacks;
    private int _kills;
    private bool _gridVisible;
    private bool _sourceOverlayVisible;
    private bool _blockout3dVisible;
    private bool _worldGuidesVisible = true;
    private int _ratActivationDistanceHexes;
    private int _lastRatActors;
    private bool _firstPersonModeActive;
    private bool _firstPersonMoving;
    private int _fpsShots;
    private int _fpsHits;
    private int _fpsKills;
    private double _fpsShotCooldownSeconds;
    private double _fpsMeleeCooldownSeconds;
    private int _rangedAttacks;
    private int _rangedHits;
    private int _meleeAttacks;
    private int _meleeHits;
    private int _reloads;
    private int _combatSequence;
    private int _magazineRounds;
    private int _reserveRounds;
    private bool _tagInventoryApplied;
    private readonly Dictionary<string, int> _inventoryObjects = new(StringComparer.Ordinal);
    private readonly Dictionary<int, MapInventoryHost> _mapInventoryHosts = [];
    private readonly HashSet<int> _lootedMapInventoryHostSerials = [];
    private readonly HashSet<int> _inactiveMapInventoryHostSerials = [];
    private Fo1PipBoy2000? _pipBoy;
    private Fo1CombatPresentation? _combatPresentation;
    private Fo1RuntimeProfile _runtimeProfile = null!;
    private float? _ownedPlayerFloorHeightMeters;
    private float _ownedPlayerGroundErrorMeters;
    private int _ownedPlayerLitMaterials;
    private int _ownedRangedWeaponLitMaterials;
    private int _ownedMeleeWeaponLitMaterials;

    internal int PlayerTile => _playerTile;
    internal int DoorTile => _doorTile;
    internal bool SourceDoorOpen => _sourceDoorOpen;
    internal int HoveredTile => _hoveredTile;
    internal int ActionPoints => _actionPoints;
    internal int Turn => _turn;
    internal Node3D PlayerToken => _playerToken;
    internal Sprite3D PlayerSourceSprite => _playerSourceSprite;
    internal ActorModelSlice.LoadedActor? OwnedPlayer => _ownedPlayer;
    internal Fo1PlayerPresentationBinding? PlayerPresentationBinding =>
        _playerPresentationBinding;
    internal Fo1ThirdPersonWeapon.LoadedWeapon? OwnedPlayerWeapon => _ownedPlayerWeapon;
    internal Fo1ThirdPersonWeapon.LoadedWeapon? OwnedPlayerMeleeWeapon => _ownedPlayerMeleeWeapon;
    internal CanvasLayer Hud { get; private set; } = null!;
    internal bool CanWalk(int tile) => tile >= 0 && tile < _walkable.Length && _walkable[tile];
    internal bool ReturnedToSource => _returnedToSource;
    internal IReadOnlyList<Fo1Mob> Mobs => _mobs;
    internal Fo1Mob? SelectedMob => _selectedMob;
    internal int PlayerHitPoints => _playerHitPoints;
    internal int Attacks => _attacks;
    internal int Kills => _kills;
    internal int WeaponActionPointCost => _playerProfile.WeaponActionPointCost;
    internal int MeleeActionPointCost => _playerProfile.MeleeWeapon.ActionPointCost;
    internal WeaponProfile RangedWeapon => _playerProfile.RangedWeapon;
    internal WeaponProfile MeleeWeapon => _playerProfile.MeleeWeapon;
    internal int MeleeDamage => _playerProfile.MeleeDamage;
    internal string RangedWeaponSymbol => _playerProfile.Inventory.EquippedRangedSymbol;
    internal string MeleeWeaponSymbol => _playerProfile.Inventory.EquippedMeleeSymbol;
    internal string EquippedWeaponSymbol => _meleeWeaponEquipped
        ? _playerProfile.Inventory.EquippedMeleeSymbol
        : _playerProfile.Inventory.EquippedRangedSymbol;
    internal string EquippedWeaponName => _meleeWeaponEquipped
        ? _playerProfile.MeleeWeapon.Name
        : _playerProfile.RangedWeapon.Name;
    internal int EquippedWeaponActionPointCost => _meleeWeaponEquipped
        ? _playerProfile.MeleeWeapon.ActionPointCost
        : _playerProfile.RangedWeapon.ActionPointCost;
    internal int HudWeaponArtSwitches => _classicHud?.WeaponArtSwitches ?? 0;
    internal int MagazineRounds => _magazineRounds;
    internal int ReserveRounds => _reserveRounds;
    internal int RangedAttacks => _rangedAttacks;
    internal int RangedHits => _rangedHits;
    internal int MeleeAttacks => _meleeAttacks;
    internal int MeleeHits => _meleeHits;
    internal int Reloads => _reloads;
    internal IReadOnlyCollection<MapInventoryHost> MapInventoryHosts => _mapInventoryHosts.Values;
    internal bool IsMapInventoryHostLooted(int serial) => _lootedMapInventoryHostSerials.Contains(serial);
    internal double FirstPersonMeleeCooldownSeconds => _fpsMeleeCooldownSeconds;
    internal string Status => _status;
    internal float FirstPersonMaximumRangeMeters => MathF.Max(
        _runtimeProfile.Gameplay.FirstPersonMinimumRangeMeters,
        _playerProfile.RangedWeapon.RangeHexes *
            _runtimeProfile.Gameplay.FirstPersonMetersPerWeaponRangeHex);
    internal int QueuedMovementSteps => _movement.Count;
    internal int PlayerMoveAnimationPlaybacks => _playerMoveAnimationPlaybacks;
    internal Fo1CharacterProfile? CharacterProfile => _characterProfile;
    internal bool GridVisible => _gridVisible;
    internal int AlertedMobs => _mobs.Count(mob => mob.Alive && mob.Alerted);
    internal int LastRatActors => _lastRatActors;
    internal int RatActivationDistanceHexes => _ratActivationDistanceHexes;
    internal int VisibleHostileMarkers => _mobs.Count(mob => mob.HostileMarkerVisible);
    internal int VisibleHostileBeacons => _mobs.Count(mob => mob.HostileBeaconVisible);
    internal int VisibleHostileLabels => _mobs.Count(mob => mob.HostileLabelVisible);
    internal float PlayerHexCenterErrorMeters =>
        _playerToken.Position.DistanceTo(
            Fo1HexMath.Center(_playerTile) +
            Vector3.Up * _runtimeProfile.Scene.SourceSprites.GroundAnchorMeters);
    internal bool FirstPersonModeActive => _firstPersonModeActive;
    internal int FpsShots => _fpsShots;
    internal int FpsHits => _fpsHits;
    internal int FpsKills => _fpsKills;
    internal Fo1PipBoy2000? PipBoy => _pipBoy;
    internal bool ClassicInterfaceAttached => _classicHud is not null;
    internal Fo1ClassicInventoryScreen? ClassicInventory => _classicInventory;
    internal Fo1ClassicHud? ClassicHud => _classicHud;
    internal bool InventoryOpen => _classicInventory?.IsOpen == true;
    internal Key InventoryKey => _classicInventory?.PhysicalKey ?? Key.None;
    internal Fo1CombatPresentation? CombatPresentation => _combatPresentation;
    internal string SavePath => _savePath;
    internal Fo1ExitGridTransitionContract? ExitGridTransition => _exitGridTransition;
    internal int? ActivatedExitGridTile => _activatedExitGridTile;
    internal Fo1DestinationPresentationContract? LoadedDestinationPresentation => _loadedDestinationPresentation;
    internal Fo1DestinationInventoryInteractionContract? DestinationInventoryInteraction => _destinationInventoryInteraction;
    internal Fo1DestinationFlareUseContract? DestinationFlareUse => _destinationFlareUse;
    internal bool DestinationFlareLit =>
        _destinationFlareScriptState.Flag("lit") && !_destinationFlareExpired;
    internal bool DestinationFlareExpired => _destinationFlareExpired;
    internal Fo1DestinationGenericDoorContract? DestinationGenericDoor => _destinationGenericDoor;
    internal bool DestinationGenericDoorOpen => _destinationGenericDoorOpen;
    internal ClassicDoorState? DestinationGenericDoorState =>
        _destinationGenericDoorSession?.State;
    internal ClassicDoorSession? DestinationGenericDoorSession =>
        _destinationGenericDoorSession;

    internal void AttachDestinationGenericDoorPlayback(ClassicDoorPlayback playback)
    {
        if (_destinationGenericDoorSession is null)
            throw new InvalidOperationException(
                "Fallout destination door playback attached before its source session.");
        _destinationGenericDoorPlayback = playback;
        ApplyDestinationDoorState(_destinationGenericDoorSession.State);
    }
    internal Fo1DestinationMedicLookContract? DestinationMedicLook => _destinationMedicLook;
    internal bool DestinationMedicLookViewed => _destinationMedicLookViewed;
    internal string? DestinationMedicDialogueProcedure => _destinationMedicDialogueProcedure;
    internal int PlayerPoison => _classicPlayerStatus.Poison;
    internal int PlayerRadiation => _classicPlayerStatus.Radiation;
    internal IReadOnlySet<string> PlayerInjuries => _classicPlayerStatus.Injuries;
    internal Fo1ExitGridTransitionContract? DestinationReturnExitGrid => _destinationReturnExitGrid;
    internal int? ActivatedDestinationReturnExitGridTile => _activatedDestinationReturnExitGridTile;

    internal sealed record SourceDoorContract(
        int Serial, int Tile, string Pid, string Fid, string PrototypeSha256, bool InitiallyBlocked)
    {
        internal void Validate()
        {
            if (Serial < 0 || Tile is < 0 or >= Fo1HexMath.Width * Fo1HexMath.Height ||
                string.IsNullOrWhiteSpace(Pid) || string.IsNullOrWhiteSpace(Fid) ||
                PrototypeSha256.Length != 64 || !PrototypeSha256.All(Uri.IsHexDigit) || !InitiallyBlocked)
                throw new InvalidOperationException("Fallout MAP source door activation contract is incomplete.");
        }

        internal object Report(bool open) => new { Serial, Tile, Pid, Fid, PrototypeSha256, InitiallyBlocked, open };
    }
    internal bool CanContinue
    {
        get
        {
            try
            {
                _ = RequireRestoredCharacterForContinue();
                _ = RequireRestoredCameraForContinue();
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    internal void Configure(
        string sceneSha256,
        string sourceMapSha256,
        bool sourceMultihexCoverageComplete,
        bool[] walkable,
        int[] floorIds,
        IReadOnlyDictionary<int, string> floorNames,
        int entryTile,
        int doorTile,
        int actionPoints,
        int ratActivationDistanceHexes,
        PlayerProfile playerProfile,
        IReadOnlyList<Fo1Mob> mobs,
        IReadOnlyList<MapInventoryHost> mapInventoryHosts,
        string? savePath,
        Fo1RuntimeProfile runtimeProfile,
        float? ownedPlayerFloorHeightMeters,
        Fo1ExitGridTransitionContract? exitGridTransition,
        SourceDoorContract sourceDoor,
        string? destinationPresentationPath,
        string? destinationInventoryInteractionPath,
        string? destinationFlareUsePath,
        string? destinationGenericDoorPath,
        string? destinationMedicLookPath,
        string? destinationReturnExitGridPath)
    {
        if (walkable.Length != Fo1HexMath.Width * Fo1HexMath.Height ||
            floorIds.Length != Fo1HexMath.FloorWidth * Fo1HexMath.FloorHeight)
            throw new ArgumentException("Fallout tactical session received an invalid grid.");
        if (!walkable[entryTile])
            throw new InvalidOperationException($"V13ENT entry tile is not provisionally walkable: {entryTile}");
        if (ratActivationDistanceHexes is < 1 or > Fo1TacticalSessionNumericContracts.PresentationInt25)
            throw new InvalidOperationException(
                $"Fallout rat activation distance is invalid: {ratActivationDistanceHexes}");
        if (sourceMapSha256.Length != Fo1TacticalSessionNumericContracts.PresentationInt64 ||
            !sourceMapSha256.All(Uri.IsHexDigit))
            throw new ArgumentException("Fallout tactical session received an invalid source MAP hash.");
        _sceneSha256 = sceneSha256;
        _sourceMapSha256 = sourceMapSha256;
        _sourceMultihexCoverageComplete = sourceMultihexCoverageComplete;
        _runtimeProfile = runtimeProfile;
        if (ownedPlayerFloorHeightMeters is not null &&
            (!float.IsFinite(ownedPlayerFloorHeightMeters.Value) ||
             ownedPlayerFloorHeightMeters.Value > 0.0f))
            throw new ArgumentException(
                "Fallout owned player floor height must be a finite source-bound floor elevation.");
        _ownedPlayerFloorHeightMeters = ownedPlayerFloorHeightMeters;
        _exitGridTransition = exitGridTransition;
        _destinationPresentationPath = destinationPresentationPath is null ? null : VerifiedGltfLoader.ResolvePath(destinationPresentationPath);
        _destinationInventoryInteractionPath = destinationInventoryInteractionPath is null
            ? null
            : VerifiedGltfLoader.ResolvePath(destinationInventoryInteractionPath);
        _destinationFlareUsePath = destinationFlareUsePath is null
            ? null
            : VerifiedGltfLoader.ResolvePath(destinationFlareUsePath);
        _destinationGenericDoorPath = destinationGenericDoorPath is null
            ? null
            : VerifiedGltfLoader.ResolvePath(destinationGenericDoorPath);
        _destinationMedicLookPath = destinationMedicLookPath is null
            ? null
            : VerifiedGltfLoader.ResolvePath(destinationMedicLookPath);
        _destinationReturnExitGridPath = destinationReturnExitGridPath is null
            ? null
            : VerifiedGltfLoader.ResolvePath(destinationReturnExitGridPath);
        sourceDoor.Validate();
        if (sourceDoor.Tile != doorTile)
            throw new InvalidOperationException("Fallout MAP door contract does not match the tactical door tile.");
        _sourceDoor = sourceDoor;
        _sourceWalkable = walkable.ToArray();
        _sourceFloorIds = floorIds.ToArray();
        _sourceFloorNames = new Dictionary<int, string>(floorNames);
        _sourceMobs = mobs;
        _sourceMapInventoryHosts = mapInventoryHosts;
        _walkable = _sourceWalkable.ToArray();
        _floorIds = _sourceFloorIds.ToArray();
        _floorNames = _sourceFloorNames;
        _playerTile = entryTile;
        _entryTile = entryTile;
        _doorTile = doorTile;
        _maximumActionPoints = actionPoints;
        _actionPoints = actionPoints;
        _ratActivationDistanceHexes = ratActivationDistanceHexes;
        _playerProfile = playerProfile;
        playerProfile.Validate();
        _playerHitPoints = playerProfile.HitPoints;
        _magazineRounds = playerProfile.RangedWeapon.InitialLoadedRounds;
        foreach (var row in playerProfile.Inventory.Base)
            AddInventoryObjects(row.Symbol, row.Objects);
        _reserveRounds = InventoryObjects(playerProfile.Inventory.AmmunitionSymbol) *
            playerProfile.Inventory.AmmunitionRoundsPerObject;
        _mobs = _sourceMobs;
        foreach (var host in mapInventoryHosts)
        {
            host.Validate();
            if (!_mapInventoryHosts.TryAdd(host.Serial, host))
                throw new InvalidOperationException(
                    $"Fallout MAP inventory contract has duplicate host serial: {host.Serial}");
        }
        foreach (var mob in mobs.Where(mob => mob.Alive))
            _mobsByTile.Add(mob.Tile, mob);
        _savePath = ResolvePath(savePath ?? "user://saves/fo1-v13ent-hex-v1.json");
        Name = "Fo1TacticalSession";
        Load();
        BuildWorldMarkers();
    }

    internal bool TryActivateAdjacentSourceDoor()
    {
        var door = _sourceDoor ?? throw new InvalidOperationException(
            "Fallout tactical session has no source door activation contract.");
        if (_sourceDoorOpen || !Fo1HexMath.AreNeighbors(_playerTile, door.Tile))
            return false;
        if (_walkable[door.Tile])
            throw new InvalidOperationException("Fallout source door opened without its authored blocker.");
        _sourceDoorOpen = true;
        _walkable[door.Tile] = true;
        _status = "MAP door activated and opened from its adjacent source hex.";
        RefreshHud();
        Save();
        return true;
    }

    internal bool TryActivateAdjacentDestinationGenericDoor()
    {
        var door = _destinationGenericDoor ?? throw new InvalidOperationException(
            "Fallout destination has no explicit generic-door activation contract.");
        if (_destinationGenericDoorOpen || !Fo1HexMath.AreNeighbors(_playerTile, door.Door.Tile))
            return false;
        if (_walkable[door.Door.Tile])
            throw new InvalidOperationException("Fallout destination generic door opened without its authored MAP blocker.");
        var state = (_destinationGenericDoorPlayback ?? throw new InvalidOperationException(
            "Fallout destination generic door has no live source playback binding."))
            .BeginOpening();
        _status = $"Unscripted MAP door opening from source frame {state.Frame}; " +
            $"sound {state.LastSoundLogicalPath}.";
        RefreshHud();
        Save();
        return true;
    }

    internal void CompleteDestinationDoorPlaybackForHeadlessProof() =>
        (_destinationGenericDoorPlayback ?? throw new InvalidOperationException(
            "Fallout destination generic door has no live source playback binding."))
        .CompleteForHeadless();

    private void ApplyDestinationDoorState(ClassicDoorState state)
    {
        var door = _destinationGenericDoor ?? throw new InvalidOperationException(
            "Fallout destination door state changed without its source contract.");
        _destinationGenericDoorOpen = state.Open;
        _walkable[door.Door.Tile] = !state.Blocked;
    }

    internal void ApplyDestinationDoorPlaybackState(ClassicDoorState state) =>
        ApplyDestinationDoorState(state);

    internal bool TryLookAtAdjacentDestinationMedic()
    {
        var medic = _destinationMedicLook ?? throw new InvalidOperationException(
            "Fallout destination has no explicit Medic look-at contract.");
        if (!Fo1HexMath.AreNeighbors(_playerTile, medic.Tile))
            return false;
        var execution = medic.Program.ExecuteWithActions(
            "look_at_p_proc",
            new ClassicScriptState(),
            new ClassicScriptContext(false, false, _classicScriptGameTime));
        if (!execution.Executed || !execution.ScriptOverrides ||
            execution.DisplayMessages.Count != 1 ||
            execution.DisplayMessages[0].MessageId != medic.MessageId)
            throw new InvalidOperationException(
                "Fallout Medic look script did not emit its admitted message.");
        _destinationMedicLookViewed = true;
        _status = medic.MessageText;
        RefreshHud();
        Save();
        return true;
    }

    internal bool TryTalkToAdjacentDestinationMedicSeriouslyWounded()
    {
        var medic = _destinationMedicLook ?? throw new InvalidOperationException(
            "Fallout destination has no explicit Medic dialogue-result contract.");
        if (!Fo1HexMath.AreNeighbors(_playerTile, medic.Tile))
            return false;
        var execution = medic.Program.ExecuteWithActions(
            medic.DialogueEntryProcedure,
            new ClassicScriptState(),
            new ClassicScriptContext(false, false, _classicScriptGameTime));
        var node = medic.DialogueNodes[medic.DialogueEntryProcedure];
        if (!execution.Executed || execution.DialogueReply.Count != 1 ||
            execution.DialogueOptions.Count != 1 ||
            execution.DialogueReply[0].Message!.Value.MessageId !=
                node.ReplyMessageId ||
            execution.DialogueOptions[0].Message.MessageId !=
                node.OptionMessageId ||
            execution.DialogueOptions[0].Target != node.OptionTarget ||
            execution.DialogueOptions[0].Reaction != node.OptionReaction)
            throw new InvalidOperationException(
                "Fallout Medic dialogue result did not execute its admitted actions.");
        _destinationMedicDialogueProcedure = node.Procedure;
        _status = node.ReplyText;
        RefreshHud();
        Save();
        return true;
    }

    internal bool TrySelectDestinationMedicDialogueOption(int messageId)
    {
        var medic = _destinationMedicLook ?? throw new InvalidOperationException(
            "Fallout destination has no explicit Medic dialogue-result contract.");
        if (_destinationMedicDialogueProcedure is null ||
            !medic.DialogueNodes.TryGetValue(_destinationMedicDialogueProcedure, out var current))
            return false;
        var execution = medic.Program.ExecuteWithActions(
            current.Procedure,
            new ClassicScriptState(),
            new ClassicScriptContext(false, false, _classicScriptGameTime));
        var options = execution.DialogueOptions
            .Where(option => option.Message.MessageId == messageId)
            .ToArray();
        if (options.Length != 1 || options[0].Target != current.OptionTarget)
            return false;
        if (medic.EffectDialogueTargets.Contains(options[0].Target))
        {
            var healing = medic.Program.ExecuteWithActions(
                options[0].Target,
                new ClassicScriptState(),
                new ClassicScriptContext(
                    false,
                    false,
                    _classicScriptGameTime,
                    PlayerCurrentHitPoints: _playerHitPoints,
                    PlayerMaximumHitPoints: _playerProfile.HitPoints,
                    PlayerPoison: _classicPlayerStatus.Poison,
                    PlayerRadiation: _classicPlayerStatus.Radiation,
                    PlayerInjuries: _classicPlayerStatus.Injuries));
            if (!healing.Executed || healing.PlayerHealing < 0 ||
                _playerHitPoints + healing.PlayerHealing != _playerProfile.HitPoints ||
                healing.NextProcedure is not null &&
                    healing.NextProcedure != medic.RadiationFollowupProcedure ||
                healing.DisplayMessages.Count != 1 ||
                healing.DisplayMessages[0].MessageId != medic.HealingMessageId)
                throw new InvalidOperationException(
                    $"Fallout Medic healing result did not execute: {options[0].Target}");
            _playerHitPoints += healing.PlayerHealing;
            _classicPlayerStatus.Apply(healing);
            _classicScriptGameTime = checked(
                _classicScriptGameTime +
                healing.GameTimeAdvanceMinutes * medic.GameTimeTicksPerMinute);
            ProcessClassicTimedWorldActions();
            _destinationMedicDialogueProcedure = healing.NextProcedure;
            _status = medic.HealingMessageText;
            RefreshHud();
            Save();
            return true;
        }
        if (!medic.DialogueNodes.TryGetValue(options[0].Target, out var target))
        {
            if (!medic.UnsupportedDialogueTargets.Contains(options[0].Target))
                throw new InvalidOperationException(
                    $"Fallout Medic dialogue target is unclassified: {options[0].Target}");
            return false;
        }
        var targetExecution = medic.Program.ExecuteWithActions(
            target.Procedure,
            new ClassicScriptState(),
            new ClassicScriptContext(false, false, _classicScriptGameTime));
        if (!targetExecution.Executed || targetExecution.DialogueReply.Count != 1 ||
            targetExecution.DialogueOptions.Count != 1 ||
            targetExecution.DialogueReply[0].Message!.Value.MessageId != target.ReplyMessageId ||
            targetExecution.DialogueOptions[0].Message.MessageId != target.OptionMessageId ||
            targetExecution.DialogueOptions[0].Target != target.OptionTarget ||
            targetExecution.DialogueOptions[0].Reaction != target.OptionReaction)
            throw new InvalidOperationException(
                $"Fallout Medic dialogue target did not execute: {target.Procedure}");
        _destinationMedicDialogueProcedure = target.Procedure;
        _status = target.ReplyText;
        RefreshHud();
        Save();
        return true;
    }

    internal bool TryActivateDestinationReturnExitGrid()
    {
        var transition = _destinationReturnExitGrid ?? throw new InvalidOperationException(
            "Fallout destination has no explicit return exit-grid contract.");
        if (!transition.IsTrigger(_playerTile))
            return false;
        _activatedDestinationReturnExitGridTile = _playerTile;
        _status = "VAULT13 source exit grid committed; V13ENT MAP return is ready from its explicit contract.";
        RefreshHud();
        Save();
        return true;
    }

    internal void EnterCommittedSourceReturn()
    {
        var forward = _exitGridTransition ?? throw new InvalidOperationException(
            "Fallout source return requires the original explicit exit-grid contract.");
        var reverse = _destinationReturnExitGrid ?? throw new InvalidOperationException(
            "Fallout source return requires an explicit reciprocal exit-grid contract.");
        if (_activatedDestinationReturnExitGridTile is not { } activatedTile || !reverse.IsTrigger(activatedTile))
            throw new InvalidOperationException(
                "Fallout source return requires a committed source-authored VAULT13 trigger.");
        if (reverse.SourceMapIndex != forward.DestinationMapIndex ||
            reverse.SourceMapName != forward.DestinationMapName ||
            !string.Equals(reverse.SourceMapSha256, forward.DestinationMapSha256, StringComparison.OrdinalIgnoreCase) ||
            reverse.DestinationMapIndex != forward.SourceMapIndex ||
            reverse.DestinationMapName != forward.SourceMapName ||
            !string.Equals(reverse.DestinationMapSha256, forward.SourceMapSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(reverse.DestinationMapSha256, _sourceMapSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Fallout reciprocal exit-grid contract does not return to this loaded V13ENT MAP.");
        RestoreSourceTacticalState(reverse.DestinationTile);
        _returnedToSource = true;
        _status = "V13ENT MAP restored at the reciprocal owned exit-grid destination.";
        RefreshHud();
        Save();
    }

    internal Fo1DestinationPresentationContract LoadCommittedDestinationPresentation()
    {
        if (_activatedExitGridTile is null || _exitGridTransition is null ||
            string.IsNullOrWhiteSpace(_destinationPresentationPath))
            throw new InvalidOperationException("Fallout destination presentation requires a committed exit-grid and explicit cache path.");
        _loadedDestinationPresentation ??= Fo1DestinationPresentationContract.Load(
            _destinationPresentationPath,
            _exitGridTransition);
        return _loadedDestinationPresentation;
    }

    internal void EnterCommittedDestination(Fo1DestinationPresentationContract destination)
    {
        if (_exitGridTransition is null || _activatedExitGridTile is null)
            throw new InvalidOperationException("Fallout cannot enter a destination before its exit grid is committed.");
        ApplyDestinationTacticalState(destination, _exitGridTransition.DestinationTile);
        Save();
    }

    private void ApplyDestinationTacticalState(
        Fo1DestinationPresentationContract destination,
        int playerTile)
    {
        _returnedToSource = false;
        var transition = _exitGridTransition ?? throw new InvalidOperationException(
            "Fallout destination state has no exit-grid transition contract.");
        destination.Validate(transition);
        var elevation = destination.Map.Elevations.Single(row => row.Elevation == transition.DestinationElevation);
        _floorIds = elevation.FloorIds.ToArray();
        _floorNames = destination.Catalog.TileArtifacts.ToDictionary(row => row.Key, row => row.Value.Filename);
        var blockers = elevation.Blockers.Select(blocker => blocker.Tile).ToHashSet();
        _walkable = Enumerable.Range(0, Fo1HexMath.Width * Fo1HexMath.Height)
            .Select(tile => _floorIds[Fo1HexMath.FloorIndex(tile)] != destination.DefaultTileId && !blockers.Contains(tile))
            .ToArray();
        if (_loadedDestinationPresentation is null)
            _inactiveMapInventoryHostSerials.UnionWith(_lootedMapInventoryHostSerials);
        _mobs = [];
        _mobsByTile.Clear();
        _mapInventoryHosts.Clear();
        _destinationInventoryInteraction = string.IsNullOrWhiteSpace(_destinationInventoryInteractionPath)
            ? null
            : Fo1DestinationInventoryInteractionContract.Load(
                _destinationInventoryInteractionPath,
                destination,
                transition);
        _destinationFlareUse = string.IsNullOrWhiteSpace(_destinationFlareUsePath)
            ? null
            : _destinationInventoryInteraction is null
                ? throw new InvalidOperationException("Fallout flare use requires an explicit MAP inventory interaction.")
                : Fo1DestinationFlareUseContract.Load(_destinationFlareUsePath, _destinationInventoryInteraction);
        _destinationGenericDoor = string.IsNullOrWhiteSpace(_destinationGenericDoorPath)
            ? null
            : Fo1DestinationGenericDoorContract.Load(_destinationGenericDoorPath, destination, transition);
        if (_destinationGenericDoor is not null)
        {
            _destinationGenericDoorSession ??= new ClassicDoorSession(
                _destinationGenericDoor.Presentation,
                _destinationGenericDoorOpen
                    ? ClassicDoorSession.OpenTerminal(_destinationGenericDoor.Presentation)
                    : null);
            if (_walkable[_destinationGenericDoor.Door.Tile])
                throw new InvalidOperationException("Fallout destination generic door is not an authored presentation blocker.");
            if (_destinationGenericDoorOpen)
                _walkable[_destinationGenericDoor.Door.Tile] = true;
        }
        _destinationMedicLook = string.IsNullOrWhiteSpace(_destinationMedicLookPath)
            ? null
            : _destinationGenericDoor is null
                ? throw new InvalidOperationException("Fallout Medic look requires an explicit generic-door prerequisite.")
                : Fo1DestinationMedicLookContract.Load(
                    _destinationMedicLookPath, destination, transition, _destinationGenericDoor);
        _destinationReturnExitGrid = string.IsNullOrWhiteSpace(_destinationReturnExitGridPath)
            ? null
            : Fo1ExitGridTransitionContract.Load(_destinationReturnExitGridPath);
        if (_destinationReturnExitGrid is not null)
            _destinationReturnExitGrid.ValidateAgainstScene(destination.SourceMapSha256);
        if (playerTile is < 0 or >= Fo1HexMath.Width * Fo1HexMath.Height || !_walkable[playerTile])
            throw new InvalidOperationException("Fallout destination save tile is not walkable in its source MAP.");
        if (_destinationInventoryInteraction is not null &&
            !_mapInventoryHosts.TryAdd(
                _destinationInventoryInteraction.Host.Serial,
                _destinationInventoryInteraction.Host))
            throw new InvalidOperationException("Fallout destination inventory host serial is duplicated.");
        _playerTile = playerTile;
        _entryTile = transition.DestinationTile;
        if (_playerToken is not null)
            _playerToken.Position = Fo1HexMath.Center(_playerTile) + Vector3.Up * _runtimeProfile.Scene.SourceSprites.GroundAnchorMeters;
        _movement.Clear();
        _status = "VAULT13 source MAP loaded from the committed exit-grid destination.";
        RefreshHud();
    }

    private void RestoreSourceTacticalState(int playerTile)
    {
        if (playerTile is < 0 or >= Fo1HexMath.Width * Fo1HexMath.Height ||
            !_sourceWalkable[playerTile])
            throw new InvalidOperationException(
                "Fallout reciprocal exit-grid destination is not walkable in the owned V13ENT MAP.");
        _walkable = _sourceWalkable.ToArray();
        _floorIds = _sourceFloorIds.ToArray();
        _floorNames = _sourceFloorNames;
        if (_sourceDoorOpen)
        {
            var door = _sourceDoor ?? throw new InvalidOperationException(
                "Fallout reciprocal return has no source door contract.");
            _walkable[door.Tile] = true;
        }
        _mobs = _sourceMobs;
        _mobsByTile.Clear();
        foreach (var mob in _mobs.Where(mob => mob.Alive))
            _mobsByTile.Add(mob.Tile, mob);
        _mapInventoryHosts.Clear();
        foreach (var host in _sourceMapInventoryHosts)
        {
            host.Validate();
            if (!_mapInventoryHosts.TryAdd(host.Serial, host))
                throw new InvalidOperationException(
                    $"Fallout source MAP inventory contract has duplicate host serial: {host.Serial}");
        }
        _inactiveMapInventoryHostSerials.UnionWith(_lootedMapInventoryHostSerials);
        _lootedMapInventoryHostSerials.Clear();
        foreach (var serial in _sourceMapInventoryHosts.Select(host => host.Serial)
                     .Where(_inactiveMapInventoryHostSerials.Contains).ToArray())
        {
            _inactiveMapInventoryHostSerials.Remove(serial);
            _lootedMapInventoryHostSerials.Add(serial);
        }
        _loadedDestinationPresentation = null;
        _playerTile = playerTile;
        _entryTile = playerTile;
        if (_playerToken is not null)
            _playerToken.Position = Fo1HexMath.Center(_playerTile) +
                Vector3.Up * _runtimeProfile.Scene.SourceSprites.GroundAnchorMeters;
        _movement.Clear();
    }

    public override void _Ready()
    {
        BuildHud();
        RefreshHud();
    }

    public override void _Process(double delta)
    {
        _fpsShotCooldownSeconds = Math.Max(0.0, _fpsShotCooldownSeconds - delta);
        _fpsMeleeCooldownSeconds = Math.Max(0.0, _fpsMeleeCooldownSeconds - delta);
        RefreshMobReadability();
        RefreshTargetReticle();
        if (_firstPersonModeActive)
            return;
        if (_movement.Count == 0)
            return;
        PlayPlayerAnimation(_playerMoveAnimation);
        var targetTile = _movement.Peek();
        var target = Fo1HexMath.Center(targetTile) +
            Vector3.Up * _runtimeProfile.Scene.SourceSprites.GroundAnchorMeters;
        if (_playerToken.Position.DistanceSquaredTo(target) > Fo1TacticalSessionNumericContracts.PresentationFloat0Point0001f)
            _playerToken.LookAt(target, Vector3.Up);
        _playerToken.Position = _playerToken.Position.MoveToward(
            target,
            (float)delta * _runtimeProfile.Gameplay.TacticalMoveSpeedMetersPerSecond);
        if (_playerToken.Position.DistanceTo(target) >
            _runtimeProfile.Gameplay.TacticalArrivalToleranceMeters)
            return;
        _playerToken.Position = target;
        CommitQueuedTacticalMovementStep(targetTile);
    }

    internal void CompleteQueuedTacticalMovementForHeadlessProof()
    {
        while (_movement.Count > 0)
        {
            var targetTile = _movement.Peek();
            _playerToken.Position = Fo1HexMath.Center(targetTile) +
                Vector3.Up * _runtimeProfile.Scene.SourceSprites.GroundAnchorMeters;
            CommitQueuedTacticalMovementStep(targetTile);
        }
    }

    private void CommitQueuedTacticalMovementStep(int targetTile)
    {
        if (_movement.Count == 0 || _movement.Peek() != targetTile)
            throw new InvalidOperationException(
                "Fallout tactical movement commit diverged from its selected source path.");
        _movement.Dequeue();
        _playerTile = targetTile;
        if (_exitGridTransition?.IsTrigger(_playerTile) == true)
        {
            _movement.Clear();
            _activatedExitGridTile = _playerTile;
            _status = "Source exit grid activated; destination map is hash-bound and not synthesized.";
        }
        if (_movement.Count == 0)
            PlayPlayerAnimation(_playerIdleAnimation);
        _actionPoints = Math.Max(
            0,
            _actionPoints - _runtimeProfile.Gameplay.TacticalMoveActionPointCost);
        _status = _activatedExitGridTile is null && _movement.Count == 0
            ? $"Arrived at hex {_playerTile}"
            : _activatedExitGridTile is null ? $"Moving: {_movement.Count} step(s) queued" : _status;
        RefreshPathMarkers();
        RefreshHud();
        Save();
    }

    internal void SetHoveredTile(int tile)
    {
        if (_hoveredTile == tile)
            return;
        _hoveredTile = tile;
        _hoverMarker.Visible = _worldGuidesVisible && tile >= 0;
        if (tile >= 0)
        {
            _hoverMarker.Position = Fo1HexMath.Center(tile) + Vector3.Up * Fo1TacticalSessionNumericContracts.PresentationFloat0Point055f;
            var material = _hoverMarker.MaterialOverride as StandardMaterial3D;
            if (material is not null)
                material.AlbedoColor = _walkable[tile]
                    ? new Color(Fo1TacticalSessionNumericContracts.PresentationFloat0Point35f, 1.0f, Fo1TacticalSessionNumericContracts.PresentationFloat0Point28f, Fo1TacticalSessionNumericContracts.PresentationFloat0Point85f)
                    : new Color(1.0f, Fo1TacticalSessionNumericContracts.PresentationFloat0Point25f, Fo1TacticalSessionNumericContracts.PresentationFloat0Point18f, Fo1TacticalSessionNumericContracts.PresentationFloat0Point85f);
        }
        RefreshHud();
    }

    internal void SelectTile(int tile)
    {
        // The committed exit closes V13ENT movement, but the explicitly loaded
        // destination remains a separate source-bound tactical map.
        if (_activatedExitGridTile is not null && _loadedDestinationPresentation is null &&
            !_returnedToSource)
        {
            _status = "Exit-grid transition already activated; source movement is closed.";
            RefreshHud();
            return;
        }
        if (tile < 0 || tile >= _walkable.Length)
            return;
        _selectedTile = tile;
        _movement.Clear();
        if (!_walkable[tile])
        {
            _status = $"Hex {tile} has no non-default floor art; blocked in this proof";
            RefreshPathMarkers();
            RefreshHud();
            return;
        }
        var path = FindPath(_playerTile, tile);
        if (path.Count == 0 && tile != _playerTile)
        {
            _status = $"No provisional floor path to hex {tile}";
            RefreshPathMarkers();
            RefreshHud();
            return;
        }
        var allowed = Math.Min(_actionPoints, path.Count);
        if (path.Count == 0 && tile == _playerTile)
            SnapPlayerToHexCenter();
        foreach (var step in path.Take(allowed))
            _movement.Enqueue(step);
        _status = path.Count == 0
            ? $"Already at hex {tile}"
            : allowed == 0
                ? "No AP remaining; press Space to end turn"
                : allowed < path.Count
                    ? $"Path is {path.Count} hexes; moving {allowed} with remaining AP"
                    : $"Moving {allowed} hex(es) at 1 AP each";
        RefreshPathMarkers();
        RefreshHud();
    }

    internal void ActivateTile(int tile, bool attackRequested)
    {
        if (_mobsByTile.TryGetValue(tile, out var mob) && mob.Alive)
        {
            SelectMob(mob);
            if (attackRequested)
                AttackSelected();
            return;
        }
        SelectTile(tile);
    }

    internal CombatResult AttackSelected() => _meleeWeaponEquipped
        ? AttackSelectedMelee()
        : AttackSelectedRanged();

    internal CombatResult AttackSelectedRanged()
    {
        SetEquippedWeapon(melee: false);
        var target = _selectedMob;
        if (target is null || !target.Alive)
            return RejectCombat("ranged", "tactical", "Select a living target first");
        var weapon = _playerProfile.RangedWeapon;
        var distance = Fo1HexMath.Distance(_playerTile, target.Tile);
        if (distance > weapon.RangeHexes)
            return RejectCombat(
                "ranged",
                "tactical",
                $"{target.DisplayName} is {distance} hexes away; range is {weapon.RangeHexes}");
        if (_actionPoints < _playerProfile.WeaponActionPointCost)
            return RejectCombat(
                "ranged",
                "tactical",
                $"Need {_playerProfile.WeaponActionPointCost} AP to fire {weapon.Name}");
        if (_magazineRounds < weapon.RoundsPerAttack)
        {
            _combatPresentation?.PresentDryFire(_playerToken.GlobalPosition);
            return RejectCombat(
                "ranged",
                "tactical",
                $"{weapon.Name} is empty • press R to reload ({_reserveRounds} reserve)");
        }

        _playerToken.LookAt(Fo1HexMath.Center(target.Tile), Vector3.Up);
        target.Alert();
        _actionPoints -= _playerProfile.WeaponActionPointCost;
        _magazineRounds -= weapon.RoundsPerAttack;
        PlayPlayerCombatAnimation(_playerRangedAttackAnimation);
        _attacks++;
        _rangedAttacks++;
        _combatSequence++;
        var chance = TacticalHitChance(weapon, target, distance);
        var roll = DeterministicPercent("tactical-ranged", target.Serial);
        if (roll > chance)
        {
            PresentTacticalRanged(target, hit: false);
            _status = $"{weapon.Name} missed {target.DisplayName} • {roll}% roll / {chance}% chance";
            RefreshHud();
            Save();
            return new CombatResult(true, "ranged", "tactical", false, 0, false, chance, roll);
        }

        var damage = RollDamage(weapon, target.Serial, melee: false);
        var applied = ApplyDamage(target, damage, firstPerson: false);
        _rangedHits++;
        PresentTacticalRanged(target, hit: true);
        _status = target.Alive
            ? $"{weapon.Name} hit {target.DisplayName} for {applied} • " +
                $"{target.HitPoints}/{target.MaximumHitPoints} HP"
            : $"{weapon.Name} hit {target.DisplayName} for {applied} • killed";
        RefreshHud();
        Save();
        return new CombatResult(true, "ranged", "tactical", true, applied, !target.Alive, chance, roll);
    }

    internal CombatResult AttackSelectedMelee()
    {
        SetEquippedWeapon(melee: true);
        var target = _selectedMob;
        if (target is null || !target.Alive)
            return RejectCombat("melee", "tactical", "Select a living target first");
        var weapon = _playerProfile.MeleeWeapon;
        var distance = Fo1HexMath.Distance(_playerTile, target.Tile);
        if (distance > weapon.RangeHexes)
            return RejectCombat(
                "melee",
                "tactical",
                $"Move next to {target.DisplayName}; {weapon.Name} range is {weapon.RangeHexes} hex");
        if (_actionPoints < weapon.ActionPointCost)
            return RejectCombat(
                "melee",
                "tactical",
                $"Need {weapon.ActionPointCost} AP to use {weapon.Name}");

        _playerToken.LookAt(Fo1HexMath.Center(target.Tile), Vector3.Up);
        target.Alert();
        _actionPoints -= weapon.ActionPointCost;
        PlayPlayerCombatAnimation(_playerMeleeAttackAnimation);
        _attacks++;
        _meleeAttacks++;
        _combatSequence++;
        var chance = TacticalHitChance(weapon, target, distance);
        var roll = DeterministicPercent("tactical-melee", target.Serial);
        if (roll > chance)
        {
            PresentTacticalMelee(target, hit: false);
            _status = $"{weapon.Name} missed {target.DisplayName} • {roll}% roll / {chance}% chance";
            RefreshHud();
            Save();
            return new CombatResult(true, "melee", "tactical", false, 0, false, chance, roll);
        }

        var damage = RollDamage(weapon, target.Serial, melee: true);
        var applied = ApplyDamage(target, damage, firstPerson: false);
        _meleeHits++;
        PresentTacticalMelee(target, hit: true);
        _status = target.Alive
            ? $"{weapon.Name} struck {target.DisplayName} for {applied} • " +
                $"{target.HitPoints}/{target.MaximumHitPoints} HP"
            : $"{weapon.Name} struck {target.DisplayName} for {applied} • killed";
        RefreshHud();
        Save();
        return new CombatResult(true, "melee", "tactical", true, applied, !target.Alive, chance, roll);
    }

    internal bool Reload()
    {
        var weapon = _playerProfile.RangedWeapon;
        if (_magazineRounds >= weapon.AmmunitionCapacity)
        {
            _status = $"{weapon.Name} magazine already full • {_magazineRounds}/{weapon.AmmunitionCapacity}";
            RefreshHud();
            return false;
        }
        if (_reserveRounds <= 0)
        {
            _status = $"No {weapon.Name} ammunition remains";
            RefreshHud();
            return false;
        }
        if (!_firstPersonModeActive && _actionPoints < _runtimeProfile.Gameplay.ReloadActionPointCost)
        {
            _status = $"Need {_runtimeProfile.Gameplay.ReloadActionPointCost} AP to reload {weapon.Name}";
            RefreshHud();
            return false;
        }
        var loaded = Math.Min(weapon.AmmunitionCapacity - _magazineRounds, _reserveRounds);
        _magazineRounds += loaded;
        _reserveRounds -= loaded;
        if (!_firstPersonModeActive)
            _actionPoints -= _runtimeProfile.Gameplay.ReloadActionPointCost;
        SetEquippedWeapon(melee: false);
        PlayPlayerCombatAnimation(_playerReloadAnimation);
        _reloads++;
        _combatPresentation?.PresentReload(_playerToken.GlobalPosition);
        _status = $"Reloaded {weapon.Name} • {_magazineRounds}/{weapon.AmmunitionCapacity} + {_reserveRounds}";
        RefreshHud();
        Save();
        return true;
    }

    internal Fo1Mob? CycleTarget()
    {
        var living = _mobs.Where(mob => mob.Alive)
            .OrderBy(mob => Fo1HexMath.Distance(_playerTile, mob.Tile))
            .ThenBy(mob => mob.Serial)
            .ToArray();
        if (living.Length == 0)
        {
            _status = "No living hostile targets remain";
            RefreshHud();
            return null;
        }
        var current = _selectedMob is null ? -1 : Array.IndexOf(living, _selectedMob);
        var target = living[(current + 1) % living.Length];
        SelectMob(target);
        return target;
    }

    internal void ToggleGrid()
    {
        var grid = GetTree().CurrentScene.FindChild("V13ENT_200X200_HEX_GRID", true, false) as GeometryInstance3D;
        if (grid is null)
            throw new InvalidOperationException("Fallout tactical hex overlay is missing.");
        _gridVisible = !_gridVisible;
        grid.Visible = _gridVisible;
        _status = $"Hex grid {(_gridVisible ? "shown" : "hidden")} (G toggles)";
        RefreshHud();
    }

    internal void ToggleSourceOverlay()
    {
        if (_firstPersonModeActive)
        {
            ApplySourceOverlayVisibility(false);
            _status = "Source 2.5D reference is tactical-only; FPS remains fully 3D";
            RefreshHud();
            return;
        }
        _sourceOverlayVisible = !_sourceOverlayVisible;
        ApplySourceOverlayVisibility(_sourceOverlayVisible);
        _status = $"Source floor/scenery reference {(_sourceOverlayVisible ? "shown" : "hidden")} (V toggles)";
        RefreshHud();
    }

    private void ApplySourceOverlayVisibility(bool visible)
    {
        var overlay = GetTree().CurrentScene.FindChild(
            "FO1_SOURCE_STATIC_SPRITE_OVERLAY",
            true,
            false) as Node3D;
        if (overlay is null)
            throw new InvalidOperationException("Fallout source-sprite reference overlay is missing.");
        overlay.Visible = visible;
        if (GetTree().CurrentScene.FindChild(
                "FO1_OWNED_CONTINUOUS_CAVE_FLOOR",
                true,
                false) is GeometryInstance3D ownedFloor)
            ownedFloor.Visible = !visible;
        _playerSourceSprite.Visible = visible || _ownedPlayer is null;
        foreach (var name in new[] { "ExactV13Secr3Frame", "VaultDoorHexIdentity" })
        {
            if (GetTree().CurrentScene.FindChild(name, true, false) is GeometryInstance3D geometry)
                geometry.Visible = visible;
        }
    }

    internal void Toggle3DBlockout()
    {
        _blockout3dVisible = !_blockout3dVisible;
        foreach (var name in new[]
                 {
                     "V13ENT_FIXED_3D_CAVE_GEOMETRY",
                     "V13ENT_3D_WALL_BLOCKERS",
                     "V13ENT_3D_ROCK_BLOCKERS",
                 })
        {
            if (GetTree().CurrentScene.FindChild(name, true, false) is GeometryInstance3D geometry)
                geometry.Visible = _blockout3dVisible;
        }
        _status = $"Experimental 3D topology blockout {(_blockout3dVisible ? "shown" : "hidden")} (B toggles)";
        RefreshHud();
    }

    internal void EndTurn()
    {
        _movement.Clear();
        PlayPlayerAnimation(_playerIdleAnimation);
        RunRatTurn();
        _turn++;
        _actionPoints = _maximumActionPoints;
        _status = _playerHitPoints <= 0
            ? "Vault Dweller is down — combat proof failed"
            : $"Turn {_turn}: {_lastRatActors} locally active rat(s) acted, player AP restored";
        RefreshPathMarkers();
        RefreshHud();
        Save();
    }

    internal void SaveAndNotify()
    {
        Save();
        _status = $"Saved at hex {_playerTile}";
        RefreshHud();
    }

    internal void SetCameraStatus(string status)
    {
        _status = status;
        RefreshHud();
    }

    internal void ApplyCharacter(Fo1CharacterProfile profile)
    {
        profile.Validate();
        if (_characterProfile is not null)
        {
            if (!SameCharacter(_characterProfile, profile))
                throw new InvalidOperationException(
                    "Fallout save already belongs to a different created character.");
            _status = $"Resumed {_characterProfile.Name} with the saved combat inventory";
            BindCharacterPresentation(profile);
            RefreshHud();
            Save();
            return;
        }
        if (_turn != 1 || _attacks != 0 || _kills != 0 || _playerTile != _entryTile)
            throw new InvalidOperationException(
                "Fallout character profile can only be applied to a fresh V13ENT session.");
        ApplyCharacterStats(profile);
        ApplyTagInventory(profile);
        _actionPoints = profile.ActionPoints;
        _playerHitPoints = profile.HitPoints;
        _status = $"{profile.Name} left Vault 13 • selected SPECIAL now drives live combat";
        BindCharacterPresentation(profile);
        RefreshHud();
        Save();
    }

    internal Fo1CharacterProfile RequireRestoredCharacterForContinue()
    {
        var profile = _characterProfile;
        var binding = _playerPresentationBinding;
        if (!_restoredCharacterFromSave || profile is null || binding is null ||
            _pendingSavedPlayerPresentation is not null)
            throw new InvalidOperationException(
                "Fallout 1 Continue requires a fully restored character and player presentation identity.");
        profile.Validate();
        binding.Identity.Validate(profile);
        if (!binding.ActorRootBound || !binding.AnimationBound)
            throw new InvalidOperationException(
                "Fallout 1 Continue requires a bound gameplay actor and animation presentation.");
        if (binding.UsesOwnedDonor)
        {
            if (_ownedPlayer is null || _ownedPlayerSource is null ||
                _ownedPlayerWeapon is null || _ownedPlayerMeleeWeapon is null ||
                !binding.WeaponAttachmentsBound || binding.WeaponVisualsSuppressed ||
                !_ownedPlayer.Value.Root.IsAncestorOf(_ownedPlayerWeapon.Value.Root) ||
                !_ownedPlayer.Value.Root.IsAncestorOf(_ownedPlayerMeleeWeapon.Value.Root))
                throw new InvalidOperationException(
                    "Fallout 1 Continue donor presentation is missing its actor or weapon attachment chain.");
        }
        else
            throw new InvalidOperationException(
                "Fallout 1 Continue has no compatible owned humanoid donor.");
        return profile;
    }

    internal Fo1CameraSaveState RequireRestoredCameraForContinue()
    {
        _ = RequireRestoredCharacterForContinue();
        if (_restoredCameraState is null || _cameraRig is null)
            throw new InvalidOperationException(
                "Fallout 1 Continue requires a complete saved camera state.");
        _cameraRig.ValidateSaveState(_restoredCameraState.Value);
        return _restoredCameraState.Value;
    }

    private void ApplyCharacterStats(Fo1CharacterProfile profile)
    {
        _characterProfile = profile;
        _playerProfile = _playerProfile with
        {
            Name = profile.Name,
            HitPoints = profile.HitPoints,
            ArmorClass = profile.ArmorClass,
            Sequence = profile.Sequence,
            Strength = profile.EffectiveStrength,
            Perception = profile.EffectivePerception,
            MeleeDamage = profile.MeleeDamage,
            Skills = profile.Skills(),
            RangedWeapon = _playerProfile.RangedWeapon with
            {
                ActionPointCost = Math.Max(
                    1,
                    _playerProfile.RangedWeapon.ActionPointCost +
                    profile.WeaponActionPointAdjustment),
            },
        };
        _maximumActionPoints = profile.ActionPoints;
    }

    private void BindCharacterPresentation(Fo1CharacterProfile profile) => BindCharacterPresentation(
        profile,
        Fo1PlayerPresentationIdentity.FromProfile(profile));

    private void BindCharacterPresentation(
        Fo1CharacterProfile profile,
        Fo1PlayerPresentationIdentity identity)
    {
        identity.Validate(profile);
        if (_ownedPlayerDonorsBySex.Count > 0)
            SelectOwnedPlayerDonor(identity.CharacterId, profile.Sex);
        var source = _ownedPlayerSource;
        var actor = _ownedPlayer;
        if (source is not null && actor is null || source is null && actor is not null)
            throw new InvalidOperationException(
                "Fallout 1 owned actor and its source contract must be present together.");
        var useOwnedDonor = source is not null && actor is not null &&
            source.Value.SourceActorFemale == (profile.Sex == "Female");
        if (!useOwnedDonor)
            throw new InvalidOperationException(
                "Fallout 1 selected identity has no compatible hash-bound owned humanoid donor.");
        if (actor is not null)
            actor.Value.Root.Visible = useOwnedDonor;

        var animationBound = useOwnedDonor && actor is not null &&
            actor.Value.LoadedAnimations.Count > 0 &&
            actor.Value.Root.IsAncestorOf(actor.Value.AnimationPlayer);
        var weaponAttachmentsBound = useOwnedDonor && actor is not null &&
            _ownedPlayerWeapon is not null && _ownedPlayerMeleeWeapon is not null &&
            actor.Value.Root.IsAncestorOf(_ownedPlayerWeapon.Value.Root) &&
            actor.Value.Root.IsAncestorOf(_ownedPlayerMeleeWeapon.Value.Root);
        if (useOwnedDonor && (!animationBound || !weaponAttachmentsBound))
            throw new InvalidOperationException(
                "Fallout 1 compatible owned donor is missing its animation or weapon attachment chain.");
        var binding = new Fo1PlayerPresentationBinding(
            identity.CharacterId,
            profile.Name,
            profile.Sex,
            identity.IdentityMode,
            "owned-fnv-full-body-presentation-donor-non-parity",
            identity.OwnedGcdSha256,
            identity.OwnedBiographySha256,
            identity.OwnedPortraitFrmSha256,
            source?.SourceActorBaseFormId ?? "none",
            useOwnedDonor,
            useOwnedDonor,
            animationBound,
            weaponAttachmentsBound,
            false,
            "owned FNV actor is presentation-only and does not claim Fallout 1 character-model parity");
        _playerPresentationBinding = binding;
        foreach (var node in new Node?[]
                 {
                     _playerToken,
                     actor?.Root,
                     actor?.AnimationPlayer,
                     _ownedPlayerWeapon?.Root,
                     _ownedPlayerMeleeWeapon?.Root,
                 }.Where(node => node is not null).Cast<Node>())
        {
            node.SetMeta("fo1_character_id", identity.CharacterId);
            node.SetMeta("fo1_character_name", profile.Name);
            node.SetMeta("fo1_character_sex", profile.Sex);
            node.SetMeta("fo1_identity_mode", identity.IdentityMode);
            node.SetMeta("fo1_presentation_mode", binding.PresentationMode);
            node.SetMeta("fo1_visual_parity", false);
        }
        _playerSourceSprite.Visible = false;
        ApplyEquippedWeaponVisibility();
    }

    private void ApplyTagInventory(Fo1CharacterProfile profile)
    {
        if (!_tagInventoryApplied)
        {
            foreach (var bonus in _playerProfile.Inventory.TagBonuses.Where(
                         bonus => profile.TaggedSkills.Contains(bonus.Skill, StringComparer.Ordinal)))
            {
                foreach (var row in bonus.Items)
                {
                    AddInventoryObjects(row.Symbol, row.Objects);
                    if (row.Symbol == _playerProfile.Inventory.AmmunitionSymbol)
                        _reserveRounds += row.Objects *
                            _playerProfile.Inventory.AmmunitionRoundsPerObject;
                }
            }
            _tagInventoryApplied = true;
        }
    }

    internal static bool SameCharacter(Fo1CharacterProfile first, Fo1CharacterProfile second) =>
        first.Name == second.Name && first.Age == second.Age && first.Sex == second.Sex &&
        first.Strength == second.Strength && first.Perception == second.Perception &&
        first.Endurance == second.Endurance && first.Charisma == second.Charisma &&
        first.Intelligence == second.Intelligence && first.Agility == second.Agility &&
        first.Luck == second.Luck &&
        first.TaggedSkills.SequenceEqual(second.TaggedSkills, StringComparer.Ordinal) &&
        first.Traits.SequenceEqual(second.Traits, StringComparer.Ordinal) &&
        Equals(first.Identity, second.Identity) &&
        Equals(first.Appearance, second.Appearance);

    internal void SetWorldGuidesVisible(bool visible)
    {
        _worldGuidesVisible = visible;
        _hoverMarker.Visible = visible && _hoveredTile >= 0;
        _pathMarkers.Visible = visible;
    }

    internal void SnapPlayerToHexCenter()
    {
        _movement.Clear();
        _playerToken.Position = Fo1HexMath.Center(_playerTile) +
            Vector3.Up * _runtimeProfile.Scene.SourceSprites.GroundAnchorMeters;
        PlayPlayerAnimation(_playerIdleAnimation);
        RefreshPathMarkers();
    }

    internal void RestoreSaveForProof()
    {
        Load();
        SnapPlayerToHexCenter();
        RestoreSavedPlayerPresentationIfReady();
        RefreshHud();
    }

    internal void SetFirstPersonModeActive(bool active)
    {
        if (_firstPersonModeActive == active)
            return;
        _movement.Clear();
        _firstPersonModeActive = active;
        _firstPersonMoving = false;
        ApplySourceOverlayVisibility(active ? false : _sourceOverlayVisible);
        if (!active)
        {
            var nearest = Fo1HexMath.NearestTile(_playerToken.Position);
            if (CanWalk(nearest))
                _playerTile = nearest;
            SnapPlayerToHexCenter();
            Save();
        }
        else
        {
            SetHoveredTile(-1);
            PlayPlayerAnimation(_playerIdleAnimation);
        }
        if (_fpsCrosshair is not null)
            _fpsCrosshair.Visible = active;
        if (_controlsLabel is not null)
            _controlsLabel.Text = ControlsText();
        _status = active
            ? "FPS MODE • continuous WASD + mouse look • LMB ranged • RMB melee • R reload"
            : $"TACTICAL MODE • snapped to authoritative hex center {_playerTile}";
        RefreshHud();
    }

    internal bool TryMoveFirstPerson(Vector3 direction, float distanceMeters)
    {
        if (!_firstPersonModeActive || distanceMeters <= 0.0f)
            return false;
        direction.Y = 0.0f;
        if (direction.LengthSquared() <= Fo1TacticalSessionNumericContracts.PresentationFloat0Point0001f)
        {
            SetFirstPersonMoving(false);
            return false;
        }
        direction = direction.Normalized();
        var delta = direction * MathF.Min(
            distanceMeters,
            _runtimeProfile.Gameplay.FirstPersonMaximumSubstepMeters);
        var current = _playerToken.Position;
        var candidates = new[]
        {
            current + delta,
            current + new Vector3(delta.X, 0.0f, 0.0f),
            current + new Vector3(0.0f, 0.0f, delta.Z),
        };
        foreach (var candidateSource in candidates)
        {
            var candidate = new Vector3(
                candidateSource.X,
                _runtimeProfile.Scene.SourceSprites.GroundAnchorMeters,
                candidateSource.Z);
            var tile = Fo1HexMath.NearestTile(candidate);
            if (!CanWalk(tile))
                continue;
            _playerToken.LookAt(candidate + direction, Vector3.Up);
            _playerToken.Position = candidate;
            _playerTile = tile;
            SetFirstPersonMoving(true);
            return true;
        }
        SetFirstPersonMoving(false);
        return false;
    }

    internal void SetFirstPersonMoving(bool moving)
    {
        if (_firstPersonMoving == moving)
            return;
        _firstPersonMoving = moving;
        PlayPlayerAnimation(moving ? _playerMoveAnimation : _playerIdleAnimation);
    }

    internal void SetCinematicPlayerAnimation(bool active, bool moving)
    {
        if (_playerAnimationPlayer is not null)
            _playerAnimationPlayer.ProcessMode = active
                ? Node.ProcessModeEnum.Always
                : Node.ProcessModeEnum.Inherit;
        PlayPlayerAnimation(moving ? _playerMoveAnimation : _playerIdleAnimation);
    }

    internal void AttachCamera(Fo1TacticalCamera camera)
    {
        _cameraRig = camera;
        _camera = camera.Camera;
        RefreshTargetReticle();
    }

    internal void PersistCameraState() => Save();

    private List<int> FindPath(int start, int target)
    {
        if (start == target)
            return [];
        var parents = Enumerable.Repeat(-2, _walkable.Length).ToArray();
        var queue = new Queue<int>();
        parents[start] = -1;
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var neighbor in Fo1HexMath.Neighbors(current))
            {
                if (!_walkable[neighbor] || parents[neighbor] != -2)
                    continue;
                parents[neighbor] = current;
                if (neighbor == target)
                {
                    queue.Clear();
                    break;
                }
                queue.Enqueue(neighbor);
            }
        }
        if (parents[target] == -2)
            return [];
        var path = new List<int>();
        for (var current = target; current != start; current = parents[current])
            path.Add(current);
        path.Reverse();
        return path;
    }

    private void SelectMob(Fo1Mob mob)
    {
        _selectedMob?.SetSelected(false);
        _selectedMob = mob;
        mob.SetSelected(true);
        _selectedTile = mob.Tile;
        _status = $"TARGET {mob.DisplayName} • HP {mob.HitPoints}/{mob.MaximumHitPoints} • " +
            $"AC {mob.ArmorClass} • AP {mob.ActionPoints}/{mob.MaximumActionPoints} • " +
            $"double-click or X to attack";
        RefreshHud();
        RefreshTargetReticle();
    }

    private void RunRatTurn()
    {
        foreach (var mob in _mobs.Where(mob => mob.Alive &&
                     Fo1HexMath.Distance(mob.Tile, _playerTile) <= _ratActivationDistanceHexes))
            mob.Alert();
        var actors = _mobs.Where(mob => mob.Alive && mob.Alerted)
                     .OrderByDescending(mob => mob.Sequence)
                     .ThenBy(mob => mob.Serial)
                     .ToArray();
        _lastRatActors = actors.Length;
        foreach (var mob in actors)
        {
            mob.ResetActionPoints();
            var turn = ClassicCombatTurnOwner.BeginTargetTurn(
                mob.ActionPoints,
                mob.MaximumActionPoints,
                mob.AiPacket,
                mob.Team,
                mob.Alerted,
                mob.Tile,
                _playerTile,
                Fo1HexMath.Neighbors(mob.Tile).ToHashSet());
            if (turn.Action == ClassicTargetTurnAction.AdjacentAttackRequired)
            {
                RatAttack(mob);
                continue;
            }
            if (turn.Action != ClassicTargetTurnAction.MovementRequired)
                continue;
            var sourceWalkable = Enumerable.Range(0, _walkable.Length)
                .Where(tile => _walkable[tile] || tile == mob.Tile)
                .ToHashSet();
            var path = ClassicTargetPathOwner.Plan(
                mob.Tile,
                _playerTile,
                mob.ActionPoints,
                sourceWalkable,
                new ClassicTargetPathContract(
                    _sourceMapSha256,
                    true,
                    _sourceMultihexCoverageComplete,
                    null,
                    null));
            if (path.Boundary != ClassicTargetPathBoundary.MoveAnimationRequired)
                throw new InvalidOperationException(
                    "FO1 rat path did not preserve its source move-animation boundary.");
        }
    }

    private void RatAttack(Fo1Mob mob)
    {
        var intent = ClassicAttackOwner.Prepare(
            $"{mob.Serial}:{mob.Pid}",
            "player",
            Fo1HexMath.Distance(mob.Tile, _playerTile),
            mob.ActionPoints,
            new ClassicAttackSource(
                mob.Pid,
                mob.MeleeDamage,
                mob.MeleeDamage,
                null,
                null,
                null,
                null,
                ClassicAttackOwner.EngineRollRequired));
        if (intent.Boundary != ClassicAttackBoundary.ActionPointCostRequired)
            throw new InvalidOperationException(
                "FO1 rat attack did not preserve its source-engine combat boundary.");
    }

    private void BuildWorldMarkers()
    {
        _playerToken = new Node3D
        {
            Name = "VaultDwellerPresentation",
            Position = Fo1HexMath.Center(_playerTile) +
                Vector3.Up * _runtimeProfile.Scene.SourceSprites.GroundAnchorMeters,
        };
        AddChild(_playerToken);
        _playerSourceSprite = new Sprite3D
        {
            Name = "VaultDwellerSourceSprite",
            Texture = _playerProfile.Texture,
            PixelSize = _playerProfile.PixelSize,
            Offset = new Vector2(
                _playerProfile.FrameOffset.X,
                -_playerProfile.FrameOffset.Y + _playerProfile.Height / 2.0f),
            Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
            Shaded = false,
            DoubleSided = true,
            AlphaCut = SpriteBase3D.AlphaCutMode.OpaquePrepass,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            Scale = Vector3.One * Fo1TacticalSessionNumericContracts.PresentationFloat1Point28f,
        };
        _playerToken.AddChild(_playerSourceSprite);
        _hoverMarker = new MeshInstance3D
        {
            Name = "HoveredFalloutHex",
            Mesh = Fo1HexVisuals.BuildRingMesh(Fo1TacticalSessionNumericContracts.PresentationFloat0Point78f, Fo1TacticalSessionNumericContracts.PresentationFloat0Point98f),
            MaterialOverride = Fo1HexVisuals.Material(new Color(Fo1TacticalSessionNumericContracts.PresentationFloat0Point35f, 1.0f, Fo1TacticalSessionNumericContracts.PresentationFloat0Point28f, Fo1TacticalSessionNumericContracts.PresentationFloat0Point85f), true),
            Visible = false,
        };
        AddChild(_hoverMarker);
        _pathMarkers = new MultiMeshInstance3D
        {
            Name = "QueuedFalloutHexPath",
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = Fo1HexVisuals.BuildRingMesh(Fo1TacticalSessionNumericContracts.PresentationFloat0Point86f, Fo1TacticalSessionNumericContracts.PresentationFloat0Point94f),
            },
            MaterialOverride = Fo1HexVisuals.Material(new Color(Fo1TacticalSessionNumericContracts.PresentationFloat0Point95f, Fo1TacticalSessionNumericContracts.PresentationFloat0Point76f, Fo1TacticalSessionNumericContracts.PresentationFloat0Point18f, Fo1TacticalSessionNumericContracts.PresentationFloat0Point72f), true),
        };
        AddChild(_pathMarkers);
    }

    private void RefreshPathMarkers()
    {
        var steps = _movement.ToArray();
        _pathMarkers.Multimesh.InstanceCount = steps.Length;
        for (var index = 0; index < steps.Length; index++)
            _pathMarkers.Multimesh.SetInstanceTransform(
                index,
                new Transform3D(Basis.Identity, Fo1HexMath.Center(steps[index]) + Vector3.Up * Fo1TacticalSessionNumericContracts.PresentationFloat0Point04f));
    }

    private void BuildHud()
    {
        Hud = new CanvasLayer { Name = "Fo1HexHud", Layer = Fo1TacticalSessionNumericContracts.PresentationInt50 };
        AddChild(Hud);
        _debugHudRoot = new Control
        {
            Name = "DevelopmentStatusHud",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        Hud.AddChild(_debugHudRoot);
        var panel = new ColorRect
        {
            Position = new Vector2(Fo1TacticalSessionNumericContracts.PresentationFloat18Point0f, Fo1TacticalSessionNumericContracts.PresentationFloat532Point0f),
            Size = new Vector2(Fo1TacticalSessionNumericContracts.PresentationFloat910Point0f, Fo1TacticalSessionNumericContracts.PresentationFloat170Point0f),
            Color = new Color(Fo1TacticalSessionNumericContracts.PresentationFloat0Point012f, Fo1TacticalSessionNumericContracts.PresentationFloat0Point022f, Fo1TacticalSessionNumericContracts.PresentationFloat0Point018f, Fo1TacticalSessionNumericContracts.PresentationFloat0Point91f),
        };
        _debugHudRoot.AddChild(panel);
        var labels = new VBoxContainer
        {
            Position = new Vector2(Fo1TacticalSessionNumericContracts.PresentationFloat32Point0f, Fo1TacticalSessionNumericContracts.PresentationFloat542Point0f),
            Size = new Vector2(Fo1TacticalSessionNumericContracts.PresentationFloat875Point0f, Fo1TacticalSessionNumericContracts.PresentationFloat145Point0f),
        };
        _debugHudRoot.AddChild(labels);
        var title = new Label { Text = "FALLOUT 1  •  V13ENT  •  200×200 HEX TACTICAL SLICE" };
        title.AddThemeColorOverride("font_color", new Color(Fo1TacticalSessionNumericContracts.PresentationFloat0Point96f, Fo1TacticalSessionNumericContracts.PresentationFloat0Point77f, Fo1TacticalSessionNumericContracts.PresentationFloat0Point28f));
        title.AddThemeFontSizeOverride("font_size", Fo1TacticalSessionNumericContracts.PresentationInt18);
        labels.AddChild(title);
        _turnLabel = HudLabel(labels);
        _hexLabel = HudLabel(labels);
        _statusLabel = HudLabel(labels);
        _controlsLabel = HudLabel(labels);
        _controlsLabel.Text = ControlsText();
        _controlsLabel.AddThemeFontSizeOverride("font_size", Fo1TacticalSessionNumericContracts.PresentationInt14);
        BuildTargetReticle();
        BuildFpsCrosshair();
    }

    private string ControlsText() => _firstPersonModeActive
        ? "FPS • WASD move • Mouse look • LMB 10mm • RMB knife • R reload • C tactical • I inventory • P Pip-Boy • Esc mouse"
        : "TACTICAL • LMB move/select • Tab target • X ranged • Z melee • R reload • C shoulder/FPS • MMB orbit • RMB pan • Wheel zoom • G grid • I inventory • P Pip-Boy • Space turn • F5 save";

    private void BuildFpsCrosshair()
    {
        _fpsCrosshair = new Control
        {
            Name = "Fo1FpsCrosshair",
            AnchorLeft = Fo1TacticalSessionNumericContracts.PresentationFloat0Point5f,
            AnchorTop = Fo1TacticalSessionNumericContracts.PresentationFloat0Point5f,
            AnchorRight = Fo1TacticalSessionNumericContracts.PresentationFloat0Point5f,
            AnchorBottom = Fo1TacticalSessionNumericContracts.PresentationFloat0Point5f,
            OffsetLeft = Fo1TacticalSessionNumericContracts.PresentationFloatNEgativE18Point0f,
            OffsetTop = Fo1TacticalSessionNumericContracts.PresentationFloatNEgativE18Point0f,
            OffsetRight = Fo1TacticalSessionNumericContracts.PresentationFloat18Point0f,
            OffsetBottom = Fo1TacticalSessionNumericContracts.PresentationFloat18Point0f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        Hud.AddChild(_fpsCrosshair);
        var color = new Color(Fo1TacticalSessionNumericContracts.PresentationFloat0Point72f, 1.0f, Fo1TacticalSessionNumericContracts.PresentationFloat0Point46f, Fo1TacticalSessionNumericContracts.PresentationFloat0Point92f);
        foreach (var (position, size) in new[]
                 {
                     (new Vector2(Fo1TacticalSessionNumericContracts.PresentationFloat16Point0f, 4.0f), new Vector2(2.0f, Fo1TacticalSessionNumericContracts.PresentationFloat9Point0f)),
                     (new Vector2(Fo1TacticalSessionNumericContracts.PresentationFloat16Point0f, Fo1TacticalSessionNumericContracts.PresentationFloat23Point0f), new Vector2(2.0f, Fo1TacticalSessionNumericContracts.PresentationFloat9Point0f)),
                     (new Vector2(4.0f, Fo1TacticalSessionNumericContracts.PresentationFloat16Point0f), new Vector2(Fo1TacticalSessionNumericContracts.PresentationFloat9Point0f, 2.0f)),
                     (new Vector2(Fo1TacticalSessionNumericContracts.PresentationFloat23Point0f, Fo1TacticalSessionNumericContracts.PresentationFloat16Point0f), new Vector2(Fo1TacticalSessionNumericContracts.PresentationFloat9Point0f, 2.0f)),
                     (new Vector2(Fo1TacticalSessionNumericContracts.PresentationFloat16Point0f, Fo1TacticalSessionNumericContracts.PresentationFloat16Point0f), new Vector2(2.0f, 2.0f)),
                 })
            _fpsCrosshair.AddChild(new ColorRect
            {
                Position = position,
                Size = size,
                Color = color,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
    }

    private void BuildTargetReticle()
    {
        _targetReticle = new Control
        {
            Name = "SelectedTargetReticle",
            Size = new Vector2(Fo1TacticalSessionNumericContracts.PresentationFloat180Point0f, Fo1TacticalSessionNumericContracts.PresentationFloat104Point0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        Hud.AddChild(_targetReticle);
        var color = new Color(1.0f, Fo1TacticalSessionNumericContracts.PresentationFloat0Point82f, Fo1TacticalSessionNumericContracts.PresentationFloat0Point10f, Fo1TacticalSessionNumericContracts.PresentationFloat0Point98f);
        foreach (var (position, size) in new[]
                 {
                     (new Vector2(Fo1TacticalSessionNumericContracts.PresentationFloat48Point0f, Fo1TacticalSessionNumericContracts.PresentationFloat20Point0f), new Vector2(Fo1TacticalSessionNumericContracts.PresentationFloat30Point0f, 4.0f)),
                     (new Vector2(Fo1TacticalSessionNumericContracts.PresentationFloat102Point0f, Fo1TacticalSessionNumericContracts.PresentationFloat20Point0f), new Vector2(Fo1TacticalSessionNumericContracts.PresentationFloat30Point0f, 4.0f)),
                     (new Vector2(Fo1TacticalSessionNumericContracts.PresentationFloat48Point0f, Fo1TacticalSessionNumericContracts.PresentationFloat20Point0f), new Vector2(4.0f, Fo1TacticalSessionNumericContracts.PresentationFloat28Point0f)),
                     (new Vector2(Fo1TacticalSessionNumericContracts.PresentationFloat128Point0f, Fo1TacticalSessionNumericContracts.PresentationFloat20Point0f), new Vector2(4.0f, Fo1TacticalSessionNumericContracts.PresentationFloat28Point0f)),
                     (new Vector2(Fo1TacticalSessionNumericContracts.PresentationFloat48Point0f, Fo1TacticalSessionNumericContracts.PresentationFloat68Point0f), new Vector2(Fo1TacticalSessionNumericContracts.PresentationFloat30Point0f, 4.0f)),
                     (new Vector2(Fo1TacticalSessionNumericContracts.PresentationFloat102Point0f, Fo1TacticalSessionNumericContracts.PresentationFloat68Point0f), new Vector2(Fo1TacticalSessionNumericContracts.PresentationFloat30Point0f, 4.0f)),
                     (new Vector2(Fo1TacticalSessionNumericContracts.PresentationFloat48Point0f, Fo1TacticalSessionNumericContracts.PresentationFloat44Point0f), new Vector2(4.0f, Fo1TacticalSessionNumericContracts.PresentationFloat28Point0f)),
                     (new Vector2(Fo1TacticalSessionNumericContracts.PresentationFloat128Point0f, Fo1TacticalSessionNumericContracts.PresentationFloat44Point0f), new Vector2(4.0f, Fo1TacticalSessionNumericContracts.PresentationFloat28Point0f)),
                     (new Vector2(Fo1TacticalSessionNumericContracts.PresentationFloat88Point0f, Fo1TacticalSessionNumericContracts.PresentationFloat72Point0f), new Vector2(4.0f, Fo1TacticalSessionNumericContracts.PresentationFloat13Point0f)),
                 })
        {
            _targetReticle.AddChild(new ColorRect
            {
                Position = position,
                Size = size,
                Color = color,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        }
        _targetReticleLabel = new Label
        {
            Position = Vector2.Zero,
            Size = new Vector2(Fo1TacticalSessionNumericContracts.PresentationFloat180Point0f, Fo1TacticalSessionNumericContracts.PresentationFloat22Point0f),
            HorizontalAlignment = HorizontalAlignment.Center,
            Text = "TARGET: GIANT RAT",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _targetReticleLabel.AddThemeColorOverride("font_color", color);
        _targetReticleLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
        _targetReticleLabel.AddThemeConstantOverride("outline_size", Fo1TacticalSessionNumericContracts.PresentationInt6);
        _targetReticleLabel.AddThemeFontSizeOverride("font_size", Fo1TacticalSessionNumericContracts.PresentationInt18);
        _targetReticle.AddChild(_targetReticleLabel);
    }

    private void RefreshTargetReticle()
    {
        if (_targetReticle is null || _camera is null || _firstPersonModeActive ||
            _selectedMob is null || !_selectedMob.Alive)
        {
            if (_targetReticle is not null)
                _targetReticle.Visible = false;
            return;
        }
        var tactical = _camera.Projection == Camera3D.ProjectionType.Orthogonal;
        var maximumDistance = tactical ? Fo1TacticalSessionNumericContracts.PresentationInt8 : 4;
        if (Fo1HexMath.Distance(_playerTile, _selectedMob.Tile) > maximumDistance)
        {
            _targetReticle.Visible = false;
            return;
        }
        var target = _selectedMob.GlobalPosition + Vector3.Up * Fo1TacticalSessionNumericContracts.PresentationFloat0Point55f;
        if (_camera.IsPositionBehind(target))
        {
            _targetReticle.Visible = false;
            return;
        }
        var screen = _camera.UnprojectPosition(target);
        var viewport = GetViewport().GetVisibleRect().Size;
        var position = screen - new Vector2(Fo1TacticalSessionNumericContracts.PresentationFloat90Point0f, Fo1TacticalSessionNumericContracts.PresentationFloat72Point0f);
        position.X = Math.Clamp(position.X, Fo1TacticalSessionNumericContracts.PresentationFloat8Point0f, MathF.Max(Fo1TacticalSessionNumericContracts.PresentationFloat8Point0f, viewport.X - Fo1TacticalSessionNumericContracts.PresentationFloat188Point0f));
        position.Y = Math.Clamp(position.Y, Fo1TacticalSessionNumericContracts.PresentationFloat8Point0f, MathF.Min(Fo1TacticalSessionNumericContracts.PresentationFloat440Point0f, viewport.Y - Fo1TacticalSessionNumericContracts.PresentationFloat112Point0f));
        _targetReticle.Position = position;
        _targetReticleLabel.Text =
            $"TARGET: GIANT RAT  HP {_selectedMob.HitPoints}/{_selectedMob.MaximumHitPoints}";
        _targetReticle.Visible = true;
    }

    private void RefreshMobReadability()
    {
        var tactical = _camera is null ||
            _camera.Projection == Camera3D.ProjectionType.Orthogonal;
        foreach (var mob in _mobs)
            mob.UpdateReadability(_playerTile, tactical);
    }

    private static Label HudLabel(Container parent)
    {
        var label = new Label();
        label.AddThemeColorOverride("font_color", new Color(Fo1TacticalSessionNumericContracts.PresentationFloat0Point68f, Fo1TacticalSessionNumericContracts.PresentationFloat0Point96f, Fo1TacticalSessionNumericContracts.PresentationFloat0Point48f));
        label.AddThemeFontSizeOverride("font_size", Fo1TacticalSessionNumericContracts.PresentationInt16);
        parent.AddChild(label);
        return label;
    }

    private void RefreshHud()
    {
        var inspected = _hoveredTile >= 0 ? _hoveredTile : _selectedTile >= 0 ? _selectedTile : _playerTile;
        var floorId = _floorIds[Fo1HexMath.FloorIndex(inspected)];
        var floorName = _floorNames.GetValueOrDefault(floorId, "unknown.frm");
        var target = _selectedMob is null
            ? "TARGET —"
            : $"TARGET {_selectedMob.DisplayName} {_selectedMob.HitPoints}/{_selectedMob.MaximumHitPoints} HP";
        if (_turnLabel is not null)
        {
            var pips = new string('●', _actionPoints) +
                new string('○', _maximumActionPoints - _actionPoints);
            _turnLabel.Text = _firstPersonModeActive
                ? $"FPS EXPLORATION   HP {_playerHitPoints}/{_playerProfile.HitPoints}   " +
                    $"AC {_playerProfile.ArmorClass}   {EquippedWeaponName}   " +
                    $"AMMO {_magazineRounds}/{_playerProfile.RangedWeapon.AmmunitionCapacity} +{_reserveRounds}   " +
                    $"SHOTS {_fpsShots}  HITS {_fpsHits}"
                : $"COMBAT TURN {_turn}   HP {_playerHitPoints}/{_playerProfile.HitPoints}   " +
                    $"AC {_playerProfile.ArmorClass}   AP {pips} {_actionPoints}/{_maximumActionPoints}   " +
                    $"EQUIPPED {EquippedWeaponName.ToUpperInvariant()} " +
                    $"[{EquippedWeaponActionPointCost} AP]   " +
                    $"10MM {_magazineRounds}/{_playerProfile.RangedWeapon.AmmunitionCapacity} +{_reserveRounds}";
            _hexLabel.Text =
                $"CURSOR HEX {inspected} ({inspected % 200},{inspected / 200})   " +
                $"FLOOR {floorId} {floorName}   " +
                $"{(_walkable[inspected] ? "PROVISIONAL FLOOR" : "NO FLOOR")}";
            _statusLabel.Text = $"{target}   {_status}";
        }
        _classicHud?.Refresh(
            _playerHitPoints,
            _playerProfile.HitPoints,
            _playerProfile.ArmorClass,
            _actionPoints,
            _maximumActionPoints,
            EquippedWeaponSymbol,
            EquippedWeaponActionPointCost,
            _firstPersonModeActive,
            _status);
    }


}
