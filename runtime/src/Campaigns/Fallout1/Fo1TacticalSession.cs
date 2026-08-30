using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;

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
    string OwnedPortraitFrmSha256)
{
    internal static Fo1PlayerPresentationIdentity FromSelection(
        Fo1CharacterProfile profile,
        Fo1PremadeCharacter? premade)
    {
        if (premade is not null && !Fo1TacticalSession.SameCharacter(profile, premade.Profile))
            throw new InvalidOperationException(
                "Fallout 1 selected premade/profile presentation identity differs.");
        var result = new Fo1PlayerPresentationIdentity(
            premade?.Id ?? "custom",
            profile.Name,
            profile.Sex,
            premade is null ? "custom-profile" : "owned-premade-gcd-frm",
            premade?.GcdSha256 ?? "none",
            premade?.Portrait.SourceFrmSha256 ?? "none");
        result.Validate(profile);
        return result;
    }

    internal static Fo1PlayerPresentationIdentity Load(
        JsonElement source,
        Fo1CharacterProfile profile)
    {
        var result = new Fo1PlayerPresentationIdentity(
            Required(source, "characterId"),
            Required(source, "characterName"),
            Required(source, "sex"),
            Required(source, "identityMode"),
            Required(source, "ownedGcdSha256"),
            Required(source, "ownedPortraitFrmSha256"));
        result.Validate(profile);
        return result;
    }

    internal object SaveState() => new
    {
        schema = "opennv-fo1-player-presentation-identity/v1",
        characterId = CharacterId,
        characterName = CharacterName,
        sex = Sex,
        identityMode = IdentityMode,
        ownedGcdSha256 = OwnedGcdSha256,
        ownedPortraitFrmSha256 = OwnedPortraitFrmSha256,
    };

    internal void Validate(Fo1CharacterProfile profile)
    {
        if (CharacterName != profile.Name || Sex != profile.Sex ||
            CharacterId is not ("max-stone" or "natalia" or "albert" or "custom"))
            throw new InvalidOperationException(
                "Fallout 1 saved player presentation identity differs from its character.");
        var premade = CharacterId != "custom";
        if (premade != (IdentityMode == "owned-premade-gcd-frm") ||
            !premade && IdentityMode != "custom-profile" ||
            premade && (!Hash(OwnedGcdSha256) || !Hash(OwnedPortraitFrmSha256)) ||
            !premade && (OwnedGcdSha256 != "none" || OwnedPortraitFrmSha256 != "none") ||
            CharacterId == "max-stone" && CharacterName != "Max Stone" ||
            CharacterId == "natalia" && Sex != "Female" ||
            CharacterId == "natalia" && CharacterName != "Natalia" ||
            CharacterId == "albert" && CharacterName != "Albert" ||
            (CharacterId is "max-stone" or "albert") && Sex != "Male")
            throw new InvalidOperationException(
                "Fallout 1 saved player presentation provenance is invalid.");
    }

    private static string Required(JsonElement source, string property)
    {
        if (source.GetProperty("schema").GetString() !=
                "opennv-fo1-player-presentation-identity/v1")
            throw new InvalidOperationException(
                "Fallout 1 saved player presentation schema is unknown.");
        var value = source.GetProperty(property).GetString();
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(
                $"Fallout 1 saved player presentation field is empty: {property}")
            : value;
    }

    private static bool Hash(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);
}

internal sealed record Fo1PlayerPresentationBinding(
    string CharacterId,
    string CharacterName,
    string Sex,
    string IdentityMode,
    string PresentationMode,
    string OwnedGcdSha256,
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
        OwnedPortraitFrmSha256);

    internal object Report() => new
    {
        CharacterId,
        CharacterName,
        Sex,
        IdentityMode,
        PresentationMode,
        OwnedGcdSha256,
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
    private bool _destinationFlareLit;
    private string? _destinationGenericDoorPath;
    private Fo1DestinationGenericDoorContract? _destinationGenericDoor;
    private bool _destinationGenericDoorOpen;
    private string? _destinationMedicLookPath;
    private Fo1DestinationMedicLookContract? _destinationMedicLook;
    private bool _destinationMedicLookViewed;
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
    internal bool DestinationFlareLit => _destinationFlareLit;
    internal Fo1DestinationGenericDoorContract? DestinationGenericDoor => _destinationGenericDoor;
    internal bool DestinationGenericDoorOpen => _destinationGenericDoorOpen;
    internal Fo1DestinationMedicLookContract? DestinationMedicLook => _destinationMedicLook;
    internal bool DestinationMedicLookViewed => _destinationMedicLookViewed;
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
        _destinationGenericDoorOpen = true;
        _walkable[door.Door.Tile] = true;
        _status = "Unscripted MAP door activated; its owned blocked hex is now passable.";
        RefreshHud();
        Save();
        return true;
    }

    internal bool TryLookAtAdjacentDestinationMedic()
    {
        var medic = _destinationMedicLook ?? throw new InvalidOperationException(
            "Fallout destination has no explicit Medic look-at contract.");
        if (!Fo1HexMath.AreNeighbors(_playerTile, medic.Tile))
            return false;
        _destinationMedicLookViewed = true;
        _status = medic.MessageText;
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

    internal void ApplyCharacter(
        Fo1CharacterProfile profile,
        Fo1PremadeCharacter? premade = null)
    {
        profile.Validate();
        if (_characterProfile is not null)
        {
            if (!SameCharacter(_characterProfile, profile))
                throw new InvalidOperationException(
                    "Fallout save already belongs to a different created character.");
            _status = $"Resumed {_characterProfile.Name} with the saved combat inventory";
            BindCharacterPresentation(profile, premade);
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
        BindCharacterPresentation(profile, premade);
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

    private void BindCharacterPresentation(
        Fo1CharacterProfile profile,
        Fo1PremadeCharacter? premade) => BindCharacterPresentation(
        profile,
        Fo1PlayerPresentationIdentity.FromSelection(profile, premade));

    private void BindCharacterPresentation(
        Fo1CharacterProfile profile,
        Fo1PlayerPresentationIdentity identity)
    {
        identity.Validate(profile);
        if (_ownedPlayerDonorsBySex.Count > 0)
            SelectOwnedPlayerDonor(profile.Sex);
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

    internal bool FireFirstPerson(Vector3 origin, Vector3 direction)
    {
        if (!_firstPersonModeActive)
            return false;
        SetEquippedWeapon(melee: false);
        if (_fpsShotCooldownSeconds > 0.0)
            return false;
        var weapon = _playerProfile.RangedWeapon;
        if (_magazineRounds < weapon.RoundsPerAttack)
        {
            _combatPresentation?.PresentDryFire(origin);
            _status = $"{weapon.Name} is empty • press R to reload ({_reserveRounds} reserve)";
            RefreshHud();
            return false;
        }
        _fpsShotCooldownSeconds = _runtimeProfile.Gameplay.FirstPersonShotCooldownSeconds;
        direction = direction.Normalized();
        _fpsShots++;
        _rangedAttacks++;
        _combatSequence++;
        _magazineRounds -= weapon.RoundsPerAttack;
        var maximumRange = FirstPersonMaximumRangeMeters;
        var target = FindFirstPersonTarget(origin, direction, maximumRange);
        if (target is null)
        {
            var endpoint = FirstPersonEnvironmentEndpoint(origin, direction, maximumRange);
            PresentFirstPersonRanged(origin, direction, endpoint, hit: false);
            _status = $"{weapon.Name} fired • MISS • {_magazineRounds}/{weapon.AmmunitionCapacity}";
            RefreshHud();
            Save();
            return false;
        }

        SelectMob(target.Mob);
        target.Mob.Alert();
        var rolled = RollDamage(weapon, target.Mob.Serial, melee: false);
        var applied = ApplyDamage(target.Mob, rolled, firstPerson: true);
        _fpsHits++;
        _rangedHits++;
        PresentFirstPersonRanged(
            origin,
            direction,
            target.Mob.GlobalPosition +
                Vector3.Up * _runtimeProfile.Gameplay.FirstPersonTargetHeightMeters,
            hit: true);
        if (!target.Mob.Alive)
        {
            _status = $"FPS {weapon.Name} hit for {applied} • {target.Mob.DisplayName} down";
        }
        else
            _status = $"FPS {weapon.Name} hit for {applied} • " +
                $"{target.Mob.HitPoints}/{target.Mob.MaximumHitPoints} HP";
        RefreshHud();
        Save();
        return true;
    }

    internal Vector3 FindClearFirstPersonDirection(Vector3 origin)
    {
        Vector3? bestDirection = null;
        var bestDistance = float.PositiveInfinity;
        for (var sample = 0; sample < Fo1HexMath.Width; sample++)
        {
            var angle = Mathf.Tau * sample / Fo1HexMath.Width;
            var direction = new Vector3(MathF.Sin(angle), 0.0f, MathF.Cos(angle));
            if (FindFirstPersonTarget(origin, direction, FirstPersonMaximumRangeMeters) is not null)
                continue;
            var endpoint = FirstPersonEnvironmentEndpoint(
                origin,
                direction,
                FirstPersonMaximumRangeMeters);
            var distance = endpoint.DistanceTo(origin);
            if (distance >= bestDistance)
                continue;
            bestDistance = distance;
            bestDirection = direction;
        }
        return bestDirection ?? throw new InvalidOperationException(
            "Fallout FPS could not find a clear source-walk-mask miss direction.");
    }

    private FirstPersonTarget? FindFirstPersonTarget(
        Vector3 origin,
        Vector3 direction,
        float maximumRange)
    {
        return _mobs
            .Where(mob => mob.Alive)
            .Select(mob =>
            {
                var targetPoint = mob.GlobalPosition +
                    Vector3.Up * _runtimeProfile.Gameplay.FirstPersonTargetHeightMeters;
                var offset = targetPoint - origin;
                var along = offset.Dot(direction);
                var perpendicular = (offset - direction * along).Length();
                return new FirstPersonTarget(mob, along, perpendicular);
            })
            .Where(candidate =>
                candidate.Along > _runtimeProfile.Gameplay.FirstPersonMinimumForwardMeters &&
                candidate.Along <= maximumRange &&
                candidate.Perpendicular <= _runtimeProfile.Gameplay.FirstPersonHitRadiusMeters)
            .OrderBy(candidate => candidate.Along)
            .ThenBy(candidate => candidate.Mob.Serial)
            .FirstOrDefault();
    }

    private Vector3 FirstPersonEnvironmentEndpoint(
        Vector3 origin,
        Vector3 direction,
        float maximumRange)
    {
        var spacing = _runtimeProfile.Gameplay.FirstPersonMaximumSubstepMeters;
        var steps = Math.Max(1, (int)MathF.Ceiling(maximumRange / spacing));
        for (var step = 1; step <= steps; step++)
        {
            var distance = MathF.Min(maximumRange, step * spacing);
            var point = origin + direction * distance;
            if (!CanWalk(Fo1HexMath.NearestTile(point)))
                return point;
        }
        return origin + direction * maximumRange;
    }

    internal bool MeleeFirstPerson(Vector3 origin, Vector3 direction)
    {
        if (!_firstPersonModeActive || _fpsMeleeCooldownSeconds > 0.0)
            return false;
        _fpsMeleeCooldownSeconds = _runtimeProfile.Gameplay.FirstPersonMeleeCooldownSeconds;
        SetEquippedWeapon(melee: true);
        PlayPlayerCombatAnimation(_playerMeleeAttackAnimation);
        direction = direction.Normalized();
        _meleeAttacks++;
        _combatSequence++;
        var target = _mobs
            .Where(mob => mob.Alive)
            .Select(mob =>
            {
                var targetPoint = mob.GlobalPosition +
                    Vector3.Up * _runtimeProfile.Gameplay.FirstPersonTargetHeightMeters;
                var offset = targetPoint - origin;
                var along = offset.Dot(direction);
                var perpendicular = (offset - direction * along).Length();
                return new { Mob = mob, Along = along, Perpendicular = perpendicular };
            })
            .Where(candidate =>
                candidate.Along > 0.0f &&
                candidate.Along <= _runtimeProfile.Gameplay.FirstPersonMeleeReachMeters &&
                candidate.Perpendicular <= _runtimeProfile.Gameplay.FirstPersonMeleeHitRadiusMeters)
            .OrderBy(candidate => candidate.Along)
            .ThenBy(candidate => candidate.Mob.Serial)
            .FirstOrDefault();
        if (target is null)
        {
            _combatPresentation?.PresentMelee(
                origin,
                origin + direction * _runtimeProfile.Gameplay.FirstPersonMeleeReachMeters,
                hit: false);
            _status = $"FPS {_playerProfile.MeleeWeapon.Name} swing • MISS";
            RefreshHud();
            Save();
            return false;
        }

        SelectMob(target.Mob);
        target.Mob.Alert();
        var damage = RollDamage(_playerProfile.MeleeWeapon, target.Mob.Serial, melee: true);
        var applied = ApplyDamage(target.Mob, damage, firstPerson: true);
        _meleeHits++;
        _combatPresentation?.PresentMelee(
            origin,
            target.Mob.GlobalPosition +
                Vector3.Up * _runtimeProfile.Gameplay.FirstPersonTargetHeightMeters,
            hit: true);
        _status = target.Mob.Alive
            ? $"FPS {_playerProfile.MeleeWeapon.Name} struck for {applied} • " +
                $"{target.Mob.HitPoints}/{target.Mob.MaximumHitPoints} HP"
            : $"FPS {_playerProfile.MeleeWeapon.Name} struck for {applied} • " +
                $"{target.Mob.DisplayName} down";
        RefreshHud();
        Save();
        return true;
    }

    private void PresentTacticalRanged(Fo1Mob target, bool hit)
    {
        if (_combatPresentation is null)
            return;
        var origin = _ownedPlayerWeapon is null ||
            _ownedPlayerWeapon.Value.MuzzleMarker is null
            ? _playerToken.GlobalPosition + Vector3.Up
            : _ownedPlayerWeapon.Value.Root.ToGlobal(
                _ownedPlayerWeapon.Value.MuzzlePositionGodotUnits);
        var casingOrigin = _ownedPlayerWeapon is null ||
            _ownedPlayerWeapon.Value.ShellMarker is null
            ? origin
            : _ownedPlayerWeapon.Value.Root.ToGlobal(
                _ownedPlayerWeapon.Value.ShellPositionGodotUnits);
        var right = _ownedPlayerWeapon?.Root.GlobalBasis.X.Normalized() ??
            _playerToken.GlobalBasis.X.Normalized();
        var endpoint = target.GlobalPosition +
            Vector3.Up * _runtimeProfile.Gameplay.FirstPersonTargetHeightMeters;
        if (!hit)
            endpoint += right * _runtimeProfile.CombatPresentation.TacticalMissOffsetMeters;
        _combatPresentation.PresentRanged(origin, endpoint, hit, casingOrigin, right);
    }

    private void PresentTacticalMelee(Fo1Mob target, bool hit)
    {
        if (_combatPresentation is null)
            return;
        var origin = _ownedPlayerMeleeWeapon?.Root.GlobalPosition ??
            _playerToken.GlobalPosition + Vector3.Up;
        var endpoint = target.GlobalPosition +
            Vector3.Up * _runtimeProfile.Gameplay.FirstPersonTargetHeightMeters;
        _combatPresentation.PresentMelee(origin, endpoint, hit);
    }

    private void PresentFirstPersonRanged(
        Vector3 origin,
        Vector3 direction,
        Vector3 endpoint,
        bool hit)
    {
        if (_combatPresentation is null)
            return;
        var right = direction.Cross(Vector3.Up).Normalized();
        var casingOrigin = origin +
            right * _runtimeProfile.CombatPresentation.FpsCasingRightMeters +
            Vector3.Down * _runtimeProfile.CombatPresentation.FpsCasingDownMeters +
            direction * _runtimeProfile.CombatPresentation.FpsCasingForwardMeters;
        _combatPresentation.PresentRanged(origin, endpoint, hit, casingOrigin, right);
    }

    private CombatResult RejectCombat(string kind, string mode, string status)
    {
        _status = status;
        RefreshHud();
        Save();
        return new CombatResult(false, kind, mode, false, 0, false, 0, 0);
    }

    private int TacticalHitChance(WeaponProfile weapon, Fo1Mob target, int distance)
    {
        if (!_playerProfile.Skills.TryGetValue(weapon.Skill, out var skill))
            throw new InvalidOperationException(
                $"Fallout character has no transported combat skill: {weapon.Skill}");
        var strengthPenalty = Math.Max(0, weapon.MinimumStrength - _playerProfile.Strength) *
            _runtimeProfile.Gameplay.StrengthPenaltyPerPointPercent;
        var rangePenalty = weapon.Melee
            ? 0
            : Math.Max(
                0,
                distance - _playerProfile.Perception *
                    _runtimeProfile.Gameplay.RangedPerceptionRangeMultiplier) *
                _runtimeProfile.Gameplay.RangedPenaltyPerExcessHexPercent;
        return Math.Clamp(
            skill - target.ArmorClass - strengthPenalty - rangePenalty,
            _runtimeProfile.Gameplay.TacticalMinimumHitChancePercent,
            _runtimeProfile.Gameplay.TacticalMaximumHitChancePercent);
    }

    private int RollDamage(WeaponProfile weapon, int targetSerial, bool melee)
    {
        var span = weapon.MaximumDamage - weapon.MinimumDamage + 1;
        var rolled = weapon.MinimumDamage +
            (int)(DeterministicUInt($"{(melee ? "melee" : "ranged")}-damage", targetSerial) %
                (uint)span);
        return rolled + (melee ? _playerProfile.MeleeDamage : 0);
    }

    private int DeterministicPercent(string purpose, int targetSerial) =>
        (int)(DeterministicUInt(purpose, targetSerial) % Fo1TacticalSessionNumericContracts.PresentationUint100U) + 1;

    private uint DeterministicUInt(string purpose, int targetSerial)
    {
        var payload = Encoding.UTF8.GetBytes(
            $"{_sceneSha256}|{_turn}|{_combatSequence}|{purpose}|{_playerTile}|{targetSerial}");
        var hash = SHA256.HashData(payload);
        return (uint)(hash[0] << Fo1TacticalSessionNumericContracts.PresentationInt24 | hash[1] << Fo1TacticalSessionNumericContracts.PresentationInt16 | hash[2] << Fo1TacticalSessionNumericContracts.PresentationInt8 | hash[3]);
    }

    private int ApplyDamage(Fo1Mob target, int damage, bool firstPerson)
    {
        var applied = target.TakeDamage(damage);
        if (target.Alive)
            return applied;
        _mobsByTile.Remove(target.Tile);
        _walkable[target.Tile] = true;
        _kills++;
        if (firstPerson)
            _fpsKills++;
        _targetReticle.Visible = false;
        return applied;
    }

    private void AddInventoryObjects(string symbol, int objects)
    {
        if (string.IsNullOrWhiteSpace(symbol) || objects <= 0)
            throw new InvalidOperationException("Fallout inventory stack is invalid.");
        _inventoryObjects[symbol] = InventoryObjects(symbol) + objects;
    }

    private int InventoryObjects(string symbol) => _inventoryObjects.GetValueOrDefault(symbol);

    internal IReadOnlyDictionary<string, int> InventorySnapshot() =>
        _inventoryObjects.OrderBy(row => row.Key, StringComparer.Ordinal)
            .ToDictionary(row => row.Key, row => row.Value, StringComparer.Ordinal);

    internal MapInventoryPickup PickupAdjacentMapInventoryHost(int serial)
    {
        if (!_mapInventoryHosts.TryGetValue(serial, out var host))
            throw new InvalidOperationException(
                $"Fallout MAP inventory host is absent from this source scene: {serial}");
        if (_lootedMapInventoryHostSerials.Contains(serial))
            throw new InvalidOperationException(
                $"Fallout MAP inventory host was already collected: {serial}");
        if (!Fo1HexMath.AreNeighbors(_playerTile, host.Tile))
            throw new InvalidOperationException(
                $"Fallout MAP inventory host requires a source-adjacent player hex: {serial}");
        foreach (var item in host.Items)
            AddInventoryObjects(item.Symbol, item.Objects);
        _lootedMapInventoryHostSerials.Add(serial);
        _status = $"Collected source MAP inventory host {serial}";
        RefreshHud();
        Save();
        return new MapInventoryPickup(host, InventorySnapshot());
    }

    internal bool EquipLootedMapInventoryWeaponForHeadlessProof(int hostSerial, string symbol)
    {
        if (!_mapInventoryHosts.TryGetValue(hostSerial, out var host) ||
            !_lootedMapInventoryHostSerials.Contains(hostSerial) ||
            !host.Items.Any(item => item.Symbol == symbol && item.SubtypeName == "weapon"))
            throw new InvalidOperationException(
                "Fallout headless proof cannot equip a weapon that was not collected from the source MAP host.");
        return EquipInventoryWeaponCore(symbol);
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

    internal Fo1PipBoy2000 AttachPipBoy(
        Fo1CharacterStartContract contract,
        Fo1CharacterProfile profile)
    {
        if (_pipBoy is not null)
            throw new InvalidOperationException("Fallout tactical session already has a Pip-Boy 2000.");
        var pipBoy = new Fo1PipBoy2000();
        pipBoy.Configure(contract, this, profile);
        AddChild(pipBoy);
        _pipBoy = pipBoy;
        return pipBoy;
    }

    internal Fo1CombatPresentation AttachCombatPresentation(JsonElement source)
    {
        if (_combatPresentation is not null)
            throw new InvalidOperationException(
                "Fallout tactical session already has a combat presentation.");
        var presentation = new Fo1CombatPresentation();
        presentation.Configure(source, _runtimeProfile.CombatPresentation);
        AddChild(presentation);
        _combatPresentation = presentation;
        return presentation;
    }

    internal void AttachClassicInterface(Fo1CharacterStartContract contract)
    {
        if (_classicHud is not null)
            throw new InvalidOperationException(
                "Fallout tactical session already has its classic gameplay interface.");
        if (_pipBoy is null)
            throw new InvalidOperationException(
                "Fallout classic interface requires the player's Pip-Boy 2000 to be attached first.");
        if (_characterProfile is null)
            throw new InvalidOperationException(
                "Fallout classic interface requires a selected character.");
        var classicInventory = new Fo1ClassicInventoryScreen();
        classicInventory.Configure(
            contract.ClassicInventory,
            _characterProfile,
            contract.PremadeCharacters,
            _playerProfile.Inventory.DisplayNames,
            InventorySnapshot,
            () => EquippedWeaponSymbol,
            _playerProfile.Inventory.EquippedRangedSymbol,
            _playerProfile.Inventory.EquippedMeleeSymbol,
            EquipInventoryWeapon,
            UseInventoryScriptedItem,
            () => { CloseInventory(); });
        Hud.AddChild(classicInventory);
        _classicInventory = classicInventory;
        var classicHud = new Fo1ClassicHud();
        classicHud.Configure(
            contract.InterfaceHud,
            TogglePipBoy,
            ToggleInventory,
            SwapEquippedWeapon);
        Hud.AddChild(classicHud);
        Hud.MoveChild(classicInventory, Hud.GetChildCount() - 1);
        _classicHud = classicHud;
        _debugHudRoot.Visible = false;
        RefreshHud();
    }

    internal void TogglePipBoy()
    {
        if (_pipBoy is null)
        {
            _status = "Pip-Boy 2000 becomes available after character selection";
            RefreshHud();
            return;
        }
        if (_classicInventory?.IsOpen == true)
            _classicInventory.Close();
        _pipBoy.Toggle();
    }

    internal void ToggleInventory()
    {
        if (_classicInventory is null)
        {
            _status = "Inventory becomes available after character selection";
            RefreshHud();
            return;
        }
        if (_classicInventory.IsOpen)
        {
            CloseInventory();
            return;
        }
        if (_pipBoy?.IsOpen == true)
            _pipBoy.SetOpen(false);
        _classicInventory.Open();
    }

    internal bool CloseInventory() => _classicInventory?.Close() == true;

    internal ActorModelSlice.LoadedActor AttachOwnedPlayer(
        Fo1HexSceneLoader.PlayerPresentationSource source)
    {
        if (_ownedPlayer is not null)
            throw new InvalidOperationException("Fallout tactical player already has an owned 3D presentation.");
        VerifiedGltfLoader.VerifyHash(source.Model, source.ModelSha256);
        VerifiedGltfLoader.VerifyHash(source.Sidecar, source.SidecarSha256);
        var actor = ActorModelSlice.Load(source.Model, source.Sidecar, _playerToken);
        BindOwnedPlayerMaterialTextures(actor, source.Sidecar);
        _ownedPlayerLitMaterials = ApplyOwnedPlayerLighting(
            actor.Root,
            source.UnitsToMeters);
        if (actor.FormId != source.SourceActorBaseFormId ||
            actor.AuthoredSurfaces != source.Surfaces ||
            actor.AuthoredTextures != source.Textures ||
            actor.Animations < source.Animations)
            throw new InvalidOperationException(
                "Fallout owned player runtime coverage differs from its scene contract.");
        actor.Root.Name = "OwnedVaultDweller";
        var groundHeight = _ownedPlayerFloorHeightMeters ??
            _playerToken.GlobalPosition.Y;
        var groundDelta = groundHeight - actor.Bounds.Position.Y;
        actor.Root.Position += Vector3.Up * groundDelta;
        _playerToken.LookAt(Fo1HexMath.Center(_doorTile), Vector3.Up);
        var grounded = actor with
        {
            Bounds = new Aabb(
                actor.Bounds.Position + Vector3.Up * groundDelta,
                actor.Bounds.Size),
        };
        _ownedPlayerGroundErrorMeters = MathF.Abs(
            grounded.Bounds.Position.Y - groundHeight);
        _ownedPlayer = grounded;
        _ownedPlayerSource = source;
        _playerAnimationPlayer = grounded.AnimationPlayer;
        _playerIdleAnimation = grounded.PlayingAnimation;
        _playerMoveAnimation = grounded.AnimationPlayer.GetAnimationList()
            .Select(name => name.ToString())
            .FirstOrDefault(name =>
                name != "RESET" &&
                name != _playerIdleAnimation &&
                name.Contains("forward", StringComparison.OrdinalIgnoreCase))
            ?? grounded.AnimationPlayer.GetAnimationList()
                .Select(name => name.ToString())
                .FirstOrDefault(name => name != "RESET" && name != _playerIdleAnimation)
            ?? "";
        var animationNames = grounded.AnimationPlayer.GetAnimationList()
            .Select(name => name.ToString())
            .ToArray();
        _playerRangedAttackAnimation = animationNames.FirstOrDefault(
            name => name.Equals("AttackRight", StringComparison.OrdinalIgnoreCase)) ?? "";
        _playerMeleeAttackAnimation = animationNames.FirstOrDefault(
            name => name.Contains("AttackRight_A", StringComparison.OrdinalIgnoreCase)) ?? "";
        _playerReloadAnimation = animationNames.FirstOrDefault(
            name => name.Contains("ReloadA", StringComparison.OrdinalIgnoreCase)) ?? "";
        _playerSourceSprite.Visible = false;
        actor.Root.SetMeta("presentation_role", source.Role);
        actor.Root.SetMeta("source_actor_female", source.SourceActorFemale);
        actor.Root.SetMeta("source_model_sha256", source.ModelSha256);
        actor.Root.SetMeta("source_sidecar_sha256", source.SidecarSha256);
        actor.Root.SetMeta("selection_state", "unbound-until-character-selection");
        return grounded;
    }

    internal void RegisterOwnedPlayerDonor(Fo1HexSceneLoader.PlayerPresentationSource source)
    {
        var sex = source.SourceActorFemale ? "Female" : "Male";
        if (!_ownedPlayerDonorsBySex.TryAdd(sex, source))
            throw new InvalidOperationException(
                $"Fallout 1 has duplicate owned humanoid donor identity: {sex}.");
    }

    private void SelectOwnedPlayerDonor(string sex)
    {
        if (!_ownedPlayerDonorsBySex.TryGetValue(sex, out var source))
            throw new InvalidOperationException(
                $"Fallout 1 selected identity has no registered owned donor for {sex}.");
        if (_ownedPlayerSource is { } current && current == source)
            return;
        if (_ownedPlayerWeaponSource is not { } ranged ||
            _ownedPlayerMeleeWeaponSource is not { } melee)
            throw new InvalidOperationException(
                "Fallout 1 donor selection has no source-bound weapon/socket contracts.");
        _ownedPlayerWeapon?.Root.QueueFree();
        _ownedPlayerMeleeWeapon?.Root.QueueFree();
        _ownedPlayer?.Root.QueueFree();
        _ownedPlayerWeapon = null;
        _ownedPlayerMeleeWeapon = null;
        _ownedPlayer = null;
        _ownedPlayerSource = null;
        _playerAnimationPlayer = null;
        _ = AttachOwnedPlayer(source);
        _ = AttachOwnedPlayerWeapon(ranged);
        _ = AttachOwnedPlayerMeleeWeapon(melee);
    }

    internal Fo1ThirdPersonWeapon.LoadedWeapon AttachOwnedPlayerWeapon(JsonElement source)
    {
        if (_ownedPlayer is null)
            throw new InvalidOperationException(
                "Fallout third-person weapon requires the owned 3D player first.");
        if (_ownedPlayerWeapon is not null)
            throw new InvalidOperationException(
                "Fallout tactical player already has an owned third-person weapon.");
        _ownedPlayerWeapon = Fo1ThirdPersonWeapon.Attach(source, _ownedPlayer.Value);
        _ownedPlayerWeaponSource = source.Clone();
        _ownedRangedWeaponLitMaterials = ApplyOwnedWeaponLighting(
            _ownedPlayerWeapon.Value.Root,
            source.GetProperty("unitsToMeters").GetSingle());
        ApplyEquippedWeaponVisibility();
        return _ownedPlayerWeapon.Value;
    }

    internal Fo1ThirdPersonWeapon.LoadedWeapon AttachOwnedPlayerMeleeWeapon(JsonElement source)
    {
        if (_ownedPlayer is null)
            throw new InvalidOperationException(
                "Fallout third-person melee weapon requires the owned 3D player first.");
        if (_ownedPlayerMeleeWeapon is not null)
            throw new InvalidOperationException(
                "Fallout tactical player already has an owned third-person melee weapon.");
        _ownedPlayerMeleeWeapon = Fo1ThirdPersonWeapon.Attach(source, _ownedPlayer.Value);
        _ownedPlayerMeleeWeaponSource = source.Clone();
        _ownedMeleeWeaponLitMaterials = ApplyOwnedWeaponLighting(
            _ownedPlayerMeleeWeapon.Value.Root,
            source.GetProperty("unitsToMeters").GetSingle());
        ApplyEquippedWeaponVisibility();
        RestoreSavedPlayerPresentationIfReady();
        return _ownedPlayerMeleeWeapon.Value;
    }

    private void RestoreSavedPlayerPresentationIfReady()
    {
        if (_pendingSavedPlayerPresentation is null || _characterProfile is null ||
            _ownedPlayer is null || _ownedPlayerWeapon is null ||
            _ownedPlayerMeleeWeapon is null)
            return;
        BindCharacterPresentation(_characterProfile, _pendingSavedPlayerPresentation);
        _pendingSavedPlayerPresentation = null;
    }

    internal void SwapEquippedWeapon()
    {
        SetEquippedWeapon(!_meleeWeaponEquipped);
        _status = $"Equipped {EquippedWeaponName} • {EquippedWeaponActionPointCost} AP";
        RefreshHud();
        Save();
    }

    internal bool EquipInventoryWeapon(string symbol)
    {
        if (_classicInventory?.IsOpen != true)
            throw new InvalidOperationException(
                "Fallout inventory equipment changes require the owned inventory screen.");
        return EquipInventoryWeaponCore(symbol);
    }

    internal bool UseInventoryScriptedItem(string symbol)
    {
        if (_classicInventory?.IsOpen != true)
            throw new InvalidOperationException(
                "Fallout inventory use requires the owned inventory screen.");
        var flare = _destinationFlareUse;
        if (flare is null || symbol != flare.Symbol || InventoryObjects(symbol) <= 0 ||
            _destinationInventoryInteraction is null || !_lootedMapInventoryHostSerials.Contains(flare.HostSerial))
            return false;
        _destinationFlareLit = true;
        _status = $"Used {symbol} through its source script; time-based expiry remains fail-closed.";
        RefreshHud();
        Save();
        return true;
    }

    private bool EquipInventoryWeaponCore(string symbol)
    {
        bool melee;
        if (symbol == _playerProfile.Inventory.EquippedRangedSymbol)
            melee = false;
        else if (symbol == _playerProfile.Inventory.EquippedMeleeSymbol)
            melee = true;
        else
            throw new InvalidOperationException(
                $"Fallout inventory item is not an active-hand weapon: {symbol}.");
        if (_meleeWeaponEquipped == melee)
            return false;
        SetEquippedWeapon(melee);
        _status = $"Equipped {EquippedWeaponName} from inventory • " +
            $"{EquippedWeaponActionPointCost} AP";
        RefreshHud();
        Save();
        return true;
    }

    private void SetEquippedWeapon(bool melee)
    {
        var symbol = melee
            ? _playerProfile.Inventory.EquippedMeleeSymbol
            : _playerProfile.Inventory.EquippedRangedSymbol;
        if (InventoryObjects(symbol) <= 0)
            throw new InvalidOperationException(
                $"Fallout cannot equip an inventory item that is not present: {symbol}.");
        _meleeWeaponEquipped = melee;
        ApplyEquippedWeaponVisibility();
    }

    private void ApplyEquippedWeaponVisibility()
    {
        var actorSupportsWeapons = _playerPresentationBinding is null ||
            _playerPresentationBinding.UsesOwnedDonor;
        if (_ownedPlayerWeapon is not null)
            _ownedPlayerWeapon.Value.Root.Visible =
                actorSupportsWeapons && !_meleeWeaponEquipped;
        if (_ownedPlayerMeleeWeapon is not null)
            _ownedPlayerMeleeWeapon.Value.Root.Visible =
                actorSupportsWeapons && _meleeWeaponEquipped;
    }

    private void PlayPlayerCombatAnimation(string name)
    {
        if (_playerAnimationPlayer is null || string.IsNullOrEmpty(name) ||
            !_playerAnimationPlayer.HasAnimation(name))
            return;
        _playerAnimationPlayer.Play(name);
        _playerAnimationPlayer.Seek(0.0, true);
    }

    private void PlayPlayerAnimation(string name)
    {
        var moving = name == _playerMoveAnimation;
        if (_playerAnimationPlayer is null || string.IsNullOrEmpty(name) ||
            _playerAnimationPlayer.CurrentAnimation.ToString() == name)
            return;
        _playerAnimationPlayer.Play(name);
        if (name == _playerMoveAnimation)
            _playerMoveAnimationPlaybacks++;
    }

    internal object Report() => new
    {
        schema = SaveSchema,
        sceneSha256 = _sceneSha256,
        playerTile = _playerTile,
        playerHex = new[] { _playerTile % Fo1TacticalSessionNumericContracts.PresentationInt200, _playerTile / Fo1TacticalSessionNumericContracts.PresentationInt200 },
        doorTile = _doorTile,
        sourceDoor = _sourceDoor?.Report(_sourceDoorOpen),
        exitGridTransition = _exitGridTransition?.Report(
            _activatedExitGridTile,
            destinationSceneLoaded: _loadedDestinationPresentation is not null),
        destinationPresentation = _loadedDestinationPresentation?.Report(_exitGridTransition!),
        turn = _turn,
        actionPoints = _actionPoints,
        maximumActionPoints = _maximumActionPoints,
        movementCostPerHex = 1,
        playerHexCenterErrorMeters = PlayerHexCenterErrorMeters,
        firstPersonMode = new
        {
            active = _firstPersonModeActive,
            locomotion = "continuous-source-walk-mask-constrained",
            tacticalActionPointsConsumed = false,
            shots = _fpsShots,
            hits = _fpsHits,
            kills = _fpsKills,
        },
        queuedSteps = _movement.Count,
        playerHitPoints = _playerHitPoints,
        playerMaximumHitPoints = _playerProfile.HitPoints,
        playerArmorClass = _playerProfile.ArmorClass,
        playerSequence = _playerProfile.Sequence,
        playerName = _playerProfile.Name,
        combat = new
        {
            equippedWeapon = new
            {
                symbol = EquippedWeaponSymbol,
                name = EquippedWeaponName,
                actionPointCost = EquippedWeaponActionPointCost,
            },
            rangedWeapon = new
            {
                name = _playerProfile.RangedWeapon.Name,
                pid = _playerProfile.RangedWeapon.Pid,
                prototypeSha256 = _playerProfile.RangedWeapon.PrototypeSha256,
                minimumDamage = _playerProfile.RangedWeapon.MinimumDamage,
                maximumDamage = _playerProfile.RangedWeapon.MaximumDamage,
                rangeHexes = _playerProfile.RangedWeapon.RangeHexes,
                actionPointCost = _playerProfile.RangedWeapon.ActionPointCost,
                magazineRounds = _magazineRounds,
                magazineCapacity = _playerProfile.RangedWeapon.AmmunitionCapacity,
                reserveRounds = _reserveRounds,
                attempts = _rangedAttacks,
                hits = _rangedHits,
            },
            meleeWeapon = new
            {
                name = _playerProfile.MeleeWeapon.Name,
                pid = _playerProfile.MeleeWeapon.Pid,
                prototypeSha256 = _playerProfile.MeleeWeapon.PrototypeSha256,
                minimumDamage = _playerProfile.MeleeWeapon.MinimumDamage,
                maximumDamage = _playerProfile.MeleeWeapon.MaximumDamage,
                characterMeleeDamage = _playerProfile.MeleeDamage,
                rangeHexes = _playerProfile.MeleeWeapon.RangeHexes,
                actionPointCost = _playerProfile.MeleeWeapon.ActionPointCost,
                attempts = _meleeAttacks,
                hits = _meleeHits,
            },
            reloads = _reloads,
            inventoryObjects = _inventoryObjects.OrderBy(row => row.Key)
                .ToDictionary(row => row.Key, row => row.Value),
            tagInventoryApplied = _tagInventoryApplied,
        },
        attacks = _attacks,
        kills = _kills,
        mobs = _mobs.Select(mob => mob.Report()).ToArray(),
        livingMobs = _mobs.Count(mob => mob.Alive),
        alertedMobs = AlertedMobs,
        lastRatActors = _lastRatActors,
        ratActivationDistanceHexes = _ratActivationDistanceHexes,
        visibleHostileMarkers = VisibleHostileMarkers,
        visibleHostileBeacons = VisibleHostileBeacons,
        visibleHostileLabels = VisibleHostileLabels,
        provisionalWalkableHexes = _walkable.Count(value => value),
        savePath = _savePath,
        character = _characterProfile?.Report(),
        classicInterface = _classicHud?.Report(),
        classicInventory = _classicInventory?.Report(),
        pipBoy = _pipBoy?.Report(),
        combatPresentation = _combatPresentation?.Report(),
        playerPresentation = new
        {
            owned3d = _ownedPlayer is not null,
            ground = new
            {
                sourceBoundFloorHeightMeters = _ownedPlayerFloorHeightMeters,
                errorMeters = _ownedPlayerGroundErrorMeters,
            },
            selection = _playerPresentationBinding?.Report(),
            formId = _ownedPlayer?.FormId,
            meshes = _ownedPlayer?.Meshes ?? 0,
            importedAnimations = _ownedPlayer?.Animations ?? 0,
            litMaterials = _ownedPlayerLitMaterials,
            idleAnimation = _playerIdleAnimation,
            moveAnimation = _playerMoveAnimation,
            rangedAttackAnimation = _playerRangedAttackAnimation,
            meleeAttackAnimation = _playerMeleeAttackAnimation,
            reloadAnimation = _playerReloadAnimation,
            currentAnimation = _playerAnimationPlayer?.CurrentAnimation.ToString(),
            moveAnimationPlaybacks = _playerMoveAnimationPlaybacks,
            thirdPersonWeapon = _ownedPlayerWeapon is null
                ? null
                : new
                {
                    role = _ownedPlayerWeapon.Value.Role,
                    formId = _ownedPlayerWeapon.Value.FormId,
                    editorId = _ownedPlayerWeapon.Value.EditorId,
                    sourceSha256 = _ownedPlayerWeapon.Value.SourceSha256,
                    bone = _ownedPlayerWeapon.Value.BoneName,
                    muzzleMarker = _ownedPlayerWeapon.Value.MuzzleMarker,
                    shellMarker = _ownedPlayerWeapon.Value.ShellMarker,
                    meshes = _ownedPlayerWeapon.Value.Meshes,
                    surfaces = _ownedPlayerWeapon.Value.Surfaces,
                    materialBindings = _ownedPlayerWeapon.Value.MaterialBindings,
                    materialTextures = _ownedPlayerWeapon.Value.MaterialTextures,
                    litMaterials = _ownedRangedWeaponLitMaterials,
                    tacticalAndThirdPersonOnly = true,
                    visible = _ownedPlayerWeapon.Value.Root.IsVisibleInTree(),
                },
            thirdPersonMeleeWeapon = _ownedPlayerMeleeWeapon is null
                ? null
                : new
                {
                    role = _ownedPlayerMeleeWeapon.Value.Role,
                    formId = _ownedPlayerMeleeWeapon.Value.FormId,
                    gameplayPid = _ownedPlayerMeleeWeapon.Value.GameplayPid,
                    editorId = _ownedPlayerMeleeWeapon.Value.EditorId,
                    sourceSha256 = _ownedPlayerMeleeWeapon.Value.SourceSha256,
                    bone = _ownedPlayerMeleeWeapon.Value.BoneName,
                    meshes = _ownedPlayerMeleeWeapon.Value.Meshes,
                    surfaces = _ownedPlayerMeleeWeapon.Value.Surfaces,
                    materialBindings = _ownedPlayerMeleeWeapon.Value.MaterialBindings,
                    materialTextures = _ownedPlayerMeleeWeapon.Value.MaterialTextures,
                    litMaterials = _ownedMeleeWeaponLitMaterials,
                    tacticalAndThirdPersonOnly = true,
                    visible = _ownedPlayerMeleeWeapon.Value.Root.IsVisibleInTree(),
                },
        },
    };

    private int ApplyOwnedPlayerLighting(
        Node root,
        float unitsToMeters)
    {
        if (!float.IsFinite(unitsToMeters) || unitsToMeters <= 0.0f)
            throw new InvalidOperationException(
                "Fallout owned player lighting requires a finite source unit scale.");
        var atmosphere = _runtimeProfile.Scene.Atmosphere;
        var ambient = new Color(
            atmosphere.AmbientColor.R * atmosphere.AmbientEnergy,
            atmosphere.AmbientColor.G * atmosphere.AmbientEnergy,
            atmosphere.AmbientColor.B * atmosphere.AmbientEnergy,
            atmosphere.AmbientColor.A);
        var fogFar = _runtimeProfile.Camera.Tactical.FarClipMeters / unitsToMeters;
        var configured = RuntimeMaterialLoader.ApplyRetailActorLighting(
            root,
            ambient,
            atmosphere.FogColor,
            0.0f,
            fogFar,
            Fo1TacticalSessionNumericContracts.PresentationFloat1Point0f,
            unitsToMeters);
        if (configured < 1)
            throw new InvalidOperationException(
                "Fallout owned player geometry has no compatible retail-lit material.");
        return configured;
    }

    private static void BindOwnedPlayerMaterialTextures(
        ActorModelSlice.LoadedActor actor,
        string sidecarPath)
    {
        var resolvedSidecar = VerifiedGltfLoader.ResolvePath(sidecarPath);
        using var document = JsonDocument.Parse(File.ReadAllText(resolvedSidecar));
        var root = document.RootElement;
        var textureRows = root.GetProperty("textures").EnumerateArray().ToArray();
        var surfaceRows = root.GetProperty("surfaces").EnumerateArray()
            .ToDictionary(
                row => row.GetProperty("runtimeNodeName").GetString()!,
                StringComparer.Ordinal);
        var rebound = 0;
        var faceGenSurfaces = 0;
        foreach (var surface in actor.Surfaces)
        {
            if (!surfaceRows.TryGetValue(surface.RuntimeNodeName, out var row))
                throw new InvalidOperationException(
                    $"Fallout owned player surface is absent from its sidecar: {surface.RuntimeNodeName}.");
            var material = row.GetProperty("material");
            if (material.TryGetProperty("faceGen", out var faceGen) &&
                faceGen.ValueKind != JsonValueKind.Null)
            {
                faceGenSurfaces++;
                continue;
            }
            if (surface.Mesh.GetSurfaceOverrideMaterial(0) is not ShaderMaterial shader ||
                shader.ResourceName != RuntimeMaterialLoader.RetailActorMaterialResourceName)
                throw new InvalidOperationException(
                    $"Fallout owned player surface has no retail actor material: {surface.RuntimeNodeName}.");
            var generatedDiffuse = material.TryGetProperty(
                    "generatedDiffuseSha256",
                    out var generatedDiffuseProperty) &&
                generatedDiffuseProperty.ValueKind == JsonValueKind.String
                    ? generatedDiffuseProperty.GetString()
                    : null;
            var diffuse = generatedDiffuse is null
                ? SingleTextureRow(
                    textureRows,
                    "identity",
                    material.GetProperty("resolvedDiffuse").GetString()!,
                    resolvedSidecar)
                : SingleTextureRow(
                    textureRows,
                    "identity",
                    $"generated:{surface.Role}:{generatedDiffuse}",
                    resolvedSidecar);
            var normal = SingleTextureRow(
                textureRows,
                "identity",
                material.GetProperty("resolvedNormal").GetString()!,
                resolvedSidecar);
            var diffuseTexture = LoadOwnedPlayerTexture(diffuse, resolvedSidecar);
            var normalTexture = LoadOwnedPlayerTexture(normal, resolvedSidecar);
            shader.SetShaderParameter("base_map", diffuseTexture);
            shader.SetShaderParameter("normal_map", normalTexture);
            shader.SetShaderParameter("use_base_map", true);
            shader.SetShaderParameter("use_normal_map", true);
            VerifyOwnedPlayerTextureReadback(
                shader,
                "base_map",
                diffuseTexture,
                surface.RuntimeNodeName);
            VerifyOwnedPlayerTextureReadback(
                shader,
                "normal_map",
                normalTexture,
                surface.RuntimeNodeName);
            rebound++;
        }
        if (rebound + faceGenSurfaces != actor.AuthoredSurfaces)
            throw new InvalidOperationException(
                $"Fallout owned player texture coverage drifted: rebound={rebound} " +
                $"surfaces={actor.AuthoredSurfaces}.");
        GD.Print(
            $"OPENNV_FO1_OWNED_ACTOR_TEXTURE_BINDING_PASS surfaces={rebound} " +
            $"faceGenSurfaces={faceGenSurfaces} shaderReadback=rid-and-pixel-sha256");
    }

    private static JsonElement SingleTextureRow(
        IReadOnlyList<JsonElement> rows,
        string property,
        string expected,
        string sidecarPath)
    {
        var matches = rows.Where(row =>
                row.GetProperty(property).GetString()?.Equals(
                    expected,
                    StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException(
                $"Fallout owned player texture identity is missing or ambiguous in {sidecarPath}: " +
                $"{property}={expected}.");
        return matches[0];
    }

    private static Texture2D LoadOwnedPlayerTexture(JsonElement row, string sidecarPath)
    {
        var root = Path.GetDirectoryName(sidecarPath)
            ?? throw new InvalidOperationException(
                $"Fallout owned player sidecar has no directory: {sidecarPath}.");
        var path = Path.GetFullPath(Path.Combine(root, row.GetProperty("png").GetString()!));
        var relative = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(relative) ||
            relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Fallout owned player texture escapes its actor artifact: {path}.");
        VerifiedGltfLoader.VerifyHash(path, row.GetProperty("pngSha256").GetString()!);
        var image = Image.LoadFromFile(path);
        if (image is null || image.IsEmpty() ||
            image.GetWidth() != row.GetProperty("width").GetInt32() ||
            image.GetHeight() != row.GetProperty("height").GetInt32())
            throw new InvalidOperationException(
                $"Fallout owned player texture is invalid: {path}.");
        if (!image.HasMipmaps() && image.GetWidth() > 1 && image.GetHeight() > 1)
            image.GenerateMipmaps();
        var pixelSha256 = Convert.ToHexString(SHA256.HashData(image.GetData())).ToLowerInvariant();
        var texture = ImageTexture.CreateFromImage(image);
        texture.SetMeta("opennv_source_pixel_sha256", pixelSha256);
        if (!texture.GetRid().IsValid)
            throw new InvalidOperationException(
                $"Fallout owned player texture has no rendering RID: {path}.");
        return texture;
    }

    private static void VerifyOwnedPlayerTextureReadback(
        ShaderMaterial shader,
        string parameter,
        Texture2D expected,
        string surface)
    {
        if (shader.GetShaderParameter(parameter).AsGodotObject() is not Texture2D readback ||
            !readback.GetRid().IsValid ||
            readback.GetRid() != expected.GetRid())
            throw new InvalidOperationException(
                $"Fallout owned player shader texture RID did not read back: {surface}/{parameter}.");
        var image = readback.GetImage();
        var pixelSha256 = image is null || image.IsEmpty()
            ? ""
            : Convert.ToHexString(SHA256.HashData(image.GetData())).ToLowerInvariant();
        if (!expected.HasMeta("opennv_source_pixel_sha256") ||
            pixelSha256 != expected.GetMeta("opennv_source_pixel_sha256").AsString())
            throw new InvalidOperationException(
                $"Fallout owned player shader pixel source did not read back: {surface}/{parameter}.");
    }

    private int ApplyOwnedWeaponLighting(Node root, float unitsToMeters)
    {
        if (!float.IsFinite(unitsToMeters) || unitsToMeters <= 0.0f)
            throw new InvalidOperationException(
                "Fallout owned weapon lighting requires a finite source unit scale.");
        var atmosphere = _runtimeProfile.Scene.Atmosphere;
        var ambient = new Color(
            atmosphere.AmbientColor.R * atmosphere.AmbientEnergy,
            atmosphere.AmbientColor.G * atmosphere.AmbientEnergy,
            atmosphere.AmbientColor.B * atmosphere.AmbientEnergy,
            atmosphere.AmbientColor.A);
        var fogFar = _runtimeProfile.Camera.Tactical.FarClipMeters / unitsToMeters;
        var configured = RuntimeMaterialLoader.ApplyRetailAmbientDirectionalLighting(
            root,
            ambient,
            atmosphere.FogColor,
            0.0f,
            fogFar,
            Fo1TacticalSessionNumericContracts.PresentationFloat1Point0f,
            unitsToMeters) + CountStandardLitMaterials(root);
        if (configured < 1)
            throw new InvalidOperationException(
                "Fallout held weapon has no compatible light-responsive material.");
        return configured;
    }

    private static int CountStandardLitMaterials(Node root)
    {
        return Descendants(root).OfType<MeshInstance3D>()
            .SelectMany(mesh => Enumerable.Range(0, mesh.Mesh?.GetSurfaceCount() ?? 0)
                .Select(mesh.GetActiveMaterial))
            .OfType<BaseMaterial3D>()
            .Count(material => material.ShadingMode != BaseMaterial3D.ShadingModeEnum.Unshaded);
    }

    private static IEnumerable<Node> Descendants(Node root)
    {
        var pending = new Stack<Node>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            yield return current;
            foreach (var child in current.GetChildren())
                pending.Push(child);
        }
    }

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
            var distance = Fo1HexMath.Distance(mob.Tile, _playerTile);
            if (distance <= _runtimeProfile.Gameplay.RatAttackRangeHexes)
            {
                RatAttack(mob);
                continue;
            }
            var original = mob.Tile;
            _walkable[original] = true;
            _mobsByTile.Remove(original);
            var path = FindPath(original, _playerTile);
            var movement = Math.Min(
                _runtimeProfile.Gameplay.RatMovementLimitHexes,
                Math.Max(0, path.Count - 1));
            movement = Math.Min(movement, mob.ActionPoints);
            var destination = movement > 0 ? path[movement - 1] : original;
            for (var index = 0; index < movement; index++)
                mob.SpendActionPoint();
            mob.MoveTo(destination);
            _walkable[destination] = false;
            _mobsByTile[destination] = mob;
            if (Fo1HexMath.Distance(destination, _playerTile) <=
                    _runtimeProfile.Gameplay.RatAttackRangeHexes &&
                mob.ActionPoints > 0)
                RatAttack(mob);
        }
    }

    private void RatAttack(Fo1Mob mob)
    {
        mob.PlayAttack();
        var damage = Math.Max(_runtimeProfile.Gameplay.MinimumDamage, mob.MeleeDamage);
        _playerHitPoints = Math.Max(0, _playerHitPoints - damage);
        mob.SpendActionPoint();
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

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_savePath)!);
        var cameraState = _cameraRig?.CaptureSaveState();
        if (_characterProfile is not null && cameraState is null)
            throw new InvalidOperationException(
                "Fallout 1 character save requires an attached camera state.");
        var document = new
        {
            schema = SaveSchema,
            sceneSha256 = _sceneSha256,
            playerTile = _playerTile,
            turn = _turn,
            actionPoints = _actionPoints,
            playerHitPoints = _playerHitPoints,
            attacks = _attacks,
            kills = _kills,
            fpsShots = _fpsShots,
            fpsHits = _fpsHits,
            fpsKills = _fpsKills,
            rangedAttacks = _rangedAttacks,
            rangedHits = _rangedHits,
            meleeAttacks = _meleeAttacks,
            meleeHits = _meleeHits,
            reloads = _reloads,
            combatSequence = _combatSequence,
            magazineRounds = _magazineRounds,
            reserveRounds = _reserveRounds,
            equippedWeaponSymbol = EquippedWeaponSymbol,
            tagInventoryApplied = _tagInventoryApplied,
            inventoryObjects = _inventoryObjects,
            destinationFlare = _destinationFlareUse is null ? null : new
            {
                descriptorSha256 = _destinationFlareUse.Sha256,
                lit = _destinationFlareLit,
            },
            destinationGenericDoor = _destinationGenericDoor is null ? null : new
            {
                descriptorSha256 = _destinationGenericDoor.Sha256,
                open = _destinationGenericDoorOpen,
            },
            destinationMedicLook = _destinationMedicLook is null ? null : new
            {
                descriptorSha256 = _destinationMedicLook.Sha256,
                viewed = _destinationMedicLookViewed,
            },
            destinationReturnExitGrid = _destinationReturnExitGrid is null ? null : new
            {
                descriptorSha256 = _destinationReturnExitGrid.Sha256,
                activatedTile = _activatedDestinationReturnExitGridTile,
            },
            lootedMapInventoryHostSerials = _inactiveMapInventoryHostSerials
                .Concat(_lootedMapInventoryHostSerials).Distinct().Order().ToArray(),
            exitGridTransition = _exitGridTransition is null ? null : new
            {
                descriptorSha256 = _exitGridTransition.Sha256,
                activatedTile = _activatedExitGridTile,
            },
            activeMap = SaveActiveMap(),
            sourceDoor = _sourceDoor?.Report(_sourceDoorOpen),
            character = _characterProfile?.Report(),
            playerPresentationIdentity = _playerPresentationBinding?.Identity.SaveState(),
            camera = cameraState?.SaveState(),
            mobs = _mobs.Select(mob => mob.Report()).ToArray(),
        };
        var temporary = _savePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(document, new JsonSerializerOptions
        {
            WriteIndented = true,
        }) + System.Environment.NewLine);
        File.Move(temporary, _savePath, true);
    }

    private void Load()
    {
        if (!File.Exists(_savePath))
            return;
        using var document = JsonDocument.Parse(File.ReadAllText(_savePath));
        var root = document.RootElement;
        if (root.GetProperty("schema").GetString() != SaveSchema ||
            root.GetProperty("sceneSha256").GetString() != _sceneSha256)
            throw new InvalidOperationException($"Fallout hex save does not match this scene: {_savePath}");
        var savedActiveMap = root.TryGetProperty("activeMap", out var activeMap) &&
            activeMap.ValueKind != JsonValueKind.Null
            ? activeMap.Clone()
            : (JsonElement?)null;
        var tile = root.GetProperty("playerTile").GetInt32();
        if (tile is < 0 or >= Fo1HexMath.Width * Fo1HexMath.Height ||
            (savedActiveMap is null && !_walkable[tile]))
            throw new InvalidOperationException($"Fallout hex save contains an invalid player tile: {tile}");
        _playerTile = tile;
        if (root.TryGetProperty("sourceDoor", out var sourceDoor) &&
            sourceDoor.ValueKind != JsonValueKind.Null)
        {
            var configured = _sourceDoor ?? throw new InvalidOperationException(
                "Fallout save has a source door state but no door contract is configured.");
            if (sourceDoor.GetProperty("Serial").GetInt32() != configured.Serial ||
                sourceDoor.GetProperty("Tile").GetInt32() != configured.Tile ||
                sourceDoor.GetProperty("PrototypeSha256").GetString() != configured.PrototypeSha256 ||
                sourceDoor.GetProperty("InitiallyBlocked").GetBoolean() != configured.InitiallyBlocked)
                throw new InvalidOperationException("Fallout save source door differs from its MAP contract.");
            _sourceDoorOpen = sourceDoor.GetProperty("open").GetBoolean();
            if (_sourceDoorOpen)
                _walkable[configured.Tile] = true;
        }
        if (root.TryGetProperty("exitGridTransition", out var exitGridTransition) &&
            exitGridTransition.ValueKind != JsonValueKind.Null)
        {
            if (_exitGridTransition is null ||
                exitGridTransition.GetProperty("descriptorSha256").GetString() != _exitGridTransition.Sha256)
                throw new InvalidOperationException("Fallout save exit-grid transition does not match its descriptor.");
            if (exitGridTransition.TryGetProperty("activatedTile", out var activated) &&
                activated.ValueKind != JsonValueKind.Null)
            {
                var activatedTile = activated.GetInt32();
                if (!_exitGridTransition.IsTrigger(activatedTile) ||
                    (savedActiveMap is null && activatedTile != _playerTile))
                    throw new InvalidOperationException("Fallout save exit-grid activation is not a source trigger.");
                _activatedExitGridTile = activatedTile;
            }
        }
        Fo1DestinationPresentationContract? savedDestination = null;
        if (savedActiveMap is not null)
        {
            var activeKind = savedActiveMap.Value.GetProperty("kind").GetString();
            if (activeKind == "destination")
                savedDestination = LoadSavedDestination(savedActiveMap.Value);
            else if (activeKind == "source-return")
                LoadSavedSourceReturn(savedActiveMap.Value, tile);
            else
                throw new InvalidOperationException("Fallout save has an unknown active MAP kind.");
        }
        if (root.TryGetProperty("character", out var character) &&
            character.ValueKind == JsonValueKind.Object)
        {
            ApplyCharacterStats(ParseSavedCharacter(character));
            if (!root.TryGetProperty(
                    "playerPresentationIdentity",
                    out var presentationIdentity) ||
                presentationIdentity.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException(
                    "Fallout 1 character save has no complete player presentation identity.");
            _pendingSavedPlayerPresentation = Fo1PlayerPresentationIdentity.Load(
                presentationIdentity,
                _characterProfile!);
            _restoredCameraState = root.TryGetProperty("camera", out var camera) &&
                camera.ValueKind == JsonValueKind.Object
                ? Fo1CameraSaveState.Load(camera)
                : null;
            _restoredCharacterFromSave = true;
        }
        else if (root.TryGetProperty(
                     "playerPresentationIdentity",
                     out var orphanedPresentationIdentity) &&
                 orphanedPresentationIdentity.ValueKind == JsonValueKind.Object)
            throw new InvalidOperationException(
                "Fallout 1 save has a player presentation identity without a character.");
        _turn = Math.Max(1, root.GetProperty("turn").GetInt32());
        _actionPoints = Math.Clamp(root.GetProperty("actionPoints").GetInt32(), 0, _maximumActionPoints);
        _playerHitPoints = root.GetProperty("playerHitPoints").GetInt32();
        _attacks = root.GetProperty("attacks").GetInt32();
        _kills = root.GetProperty("kills").GetInt32();
        _fpsShots = root.TryGetProperty("fpsShots", out var fpsShots)
            ? Math.Max(0, fpsShots.GetInt32())
            : 0;
        _fpsHits = root.TryGetProperty("fpsHits", out var fpsHits)
            ? Math.Clamp(fpsHits.GetInt32(), 0, _fpsShots)
            : 0;
        _fpsKills = root.TryGetProperty("fpsKills", out var fpsKills)
            ? Math.Clamp(fpsKills.GetInt32(), 0, _fpsHits)
            : 0;
        _rangedAttacks = root.TryGetProperty("rangedAttacks", out var rangedAttacks)
            ? Math.Max(0, rangedAttacks.GetInt32())
            : _fpsShots + _attacks;
        _rangedHits = root.TryGetProperty("rangedHits", out var rangedHits)
            ? Math.Clamp(rangedHits.GetInt32(), 0, _rangedAttacks)
            : _fpsHits + _attacks;
        _meleeAttacks = root.TryGetProperty("meleeAttacks", out var meleeAttacks)
            ? Math.Max(0, meleeAttacks.GetInt32())
            : 0;
        _meleeHits = root.TryGetProperty("meleeHits", out var meleeHits)
            ? Math.Clamp(meleeHits.GetInt32(), 0, _meleeAttacks)
            : 0;
        _reloads = root.TryGetProperty("reloads", out var reloads)
            ? Math.Max(0, reloads.GetInt32())
            : 0;
        _combatSequence = root.TryGetProperty("combatSequence", out var combatSequence)
            ? Math.Max(0, combatSequence.GetInt32())
            : _rangedAttacks + _meleeAttacks;
        _magazineRounds = root.TryGetProperty("magazineRounds", out var magazineRounds)
            ? Math.Clamp(
                magazineRounds.GetInt32(),
                0,
                _playerProfile.RangedWeapon.AmmunitionCapacity)
            : _magazineRounds;
        _reserveRounds = root.TryGetProperty("reserveRounds", out var reserveRounds)
            ? Math.Max(0, reserveRounds.GetInt32())
            : _reserveRounds;
        if (root.TryGetProperty("equippedWeaponSymbol", out var equippedWeaponSymbol))
        {
            var symbol = equippedWeaponSymbol.GetString();
            if (symbol == _playerProfile.Inventory.EquippedRangedSymbol)
                _meleeWeaponEquipped = false;
            else if (symbol == _playerProfile.Inventory.EquippedMeleeSymbol)
                _meleeWeaponEquipped = true;
            else
                throw new InvalidOperationException(
                    $"Fallout save contains an unknown equipped weapon: {symbol}.");
        }
        _tagInventoryApplied = root.TryGetProperty("tagInventoryApplied", out var tagApplied) &&
            tagApplied.GetBoolean();
        if (root.TryGetProperty("inventoryObjects", out var inventoryObjects))
        {
            _inventoryObjects.Clear();
            foreach (var row in inventoryObjects.EnumerateObject())
            {
                var objects = row.Value.GetInt32();
                if (objects < 0)
                    throw new InvalidOperationException(
                        $"Fallout save contains a negative inventory stack: {row.Name}");
                _inventoryObjects[row.Name] = objects;
            }
        }
        if (root.TryGetProperty("destinationFlare", out var destinationFlare) &&
            destinationFlare.ValueKind != JsonValueKind.Null)
        {
            if (_destinationFlareUse is null ||
                destinationFlare.GetProperty("descriptorSha256").GetString() != _destinationFlareUse.Sha256)
                throw new InvalidOperationException("Fallout save flare state does not match its descriptor.");
            _destinationFlareLit = destinationFlare.GetProperty("lit").GetBoolean();
        }
        if (root.TryGetProperty("destinationGenericDoor", out var destinationGenericDoor) &&
            destinationGenericDoor.ValueKind != JsonValueKind.Null)
        {
            if (_destinationGenericDoor is null ||
                destinationGenericDoor.GetProperty("descriptorSha256").GetString() != _destinationGenericDoor.Sha256)
                throw new InvalidOperationException("Fallout save generic-door state does not match its descriptor.");
            _destinationGenericDoorOpen = destinationGenericDoor.GetProperty("open").GetBoolean();
            if (_destinationGenericDoorOpen)
                _walkable[_destinationGenericDoor.Door.Tile] = true;
        }
        if (root.TryGetProperty("destinationMedicLook", out var destinationMedicLook) &&
            destinationMedicLook.ValueKind != JsonValueKind.Null)
        {
            if (_destinationMedicLook is null ||
                destinationMedicLook.GetProperty("descriptorSha256").GetString() != _destinationMedicLook.Sha256)
                throw new InvalidOperationException("Fallout save Medic look state does not match its descriptor.");
            _destinationMedicLookViewed = destinationMedicLook.GetProperty("viewed").GetBoolean();
        }
        if (root.TryGetProperty("destinationReturnExitGrid", out var destinationReturnExitGrid) &&
            destinationReturnExitGrid.ValueKind != JsonValueKind.Null)
        {
            if (_destinationReturnExitGrid is null ||
                destinationReturnExitGrid.GetProperty("descriptorSha256").GetString() != _destinationReturnExitGrid.Sha256)
                throw new InvalidOperationException("Fallout save return exit-grid state does not match its descriptor.");
            if (destinationReturnExitGrid.TryGetProperty("activatedTile", out var activated) &&
                activated.ValueKind != JsonValueKind.Null)
            {
                var activatedReturnTile = activated.GetInt32();
                if (!_destinationReturnExitGrid.IsTrigger(activatedReturnTile))
                    throw new InvalidOperationException("Fallout save return exit-grid activation is not source-authored.");
                _activatedDestinationReturnExitGridTile = activatedReturnTile;
            }
        }
        var savedLootedHostSerials = root.TryGetProperty("lootedMapInventoryHostSerials", out var lootedHosts)
            ? lootedHosts.EnumerateArray().Select(value => value.GetInt32()).ToArray()
            : Array.Empty<int>();
        var duplicateLootedHostSerials = savedLootedHostSerials
            .GroupBy(serial => serial)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateLootedHostSerials.Any(serial =>
                !_returnedToSource || !_returnInactiveDestinationHostSerials.Contains(serial)))
            throw new InvalidOperationException(
                "Fallout save contains a duplicate MAP inventory host outside the explicit returned-map boundary.");
        savedLootedHostSerials = savedLootedHostSerials.Distinct().ToArray();
        if (_characterProfile is not null && !_tagInventoryApplied)
            ApplyTagInventory(_characterProfile);
        if (savedDestination is null)
        {
            var mobRows = root.GetProperty("mobs").EnumerateArray().ToDictionary(
                row => row.GetProperty("serial").GetInt32());
            _mobsByTile.Clear();
            foreach (var mob in _mobs)
                _walkable[mob.Tile] = true;
            foreach (var mob in _mobs)
            {
                var row = mobRows[mob.Serial];
                mob.SetTile(row.GetProperty("tile").GetInt32());
                if (row.TryGetProperty("alerted", out var alerted) && alerted.GetBoolean())
                    mob.Alert();
                var targetHp = row.GetProperty("hitPoints").GetInt32();
                if (targetHp < mob.HitPoints)
                    mob.TakeDamage(mob.HitPoints - targetHp);
                if (mob.Alive)
                    _mobsByTile[mob.Tile] = mob;
                else
                    _walkable[mob.Tile] = true;
            }
        }
        var sourceHostSerials = _mapInventoryHosts.Keys.ToHashSet();
        if (savedDestination is not null)
            ApplyDestinationTacticalState(savedDestination, tile);
        _lootedMapInventoryHostSerials.Clear();
        _inactiveMapInventoryHostSerials.Clear();
        foreach (var serial in savedLootedHostSerials)
        {
            if (_mapInventoryHosts.ContainsKey(serial))
            {
                if (!_lootedMapInventoryHostSerials.Add(serial))
                    throw new InvalidOperationException(
                        $"Fallout save contains a duplicate MAP inventory host: {serial}");
            }
            else if ((savedDestination is not null && sourceHostSerials.Contains(serial)) ||
                     (_returnedToSource && _returnInactiveDestinationHostSerials.Contains(serial)))
            {
                if (!_inactiveMapInventoryHostSerials.Add(serial))
                    throw new InvalidOperationException(
                        $"Fallout save contains a duplicate inactive MAP inventory host: {serial}");
            }
            else
                throw new InvalidOperationException(
                    $"Fallout save contains an unknown MAP inventory host: {serial}");
        }
        ApplyEquippedWeaponVisibility();
    }

    private object? SaveActiveMap()
    {
        if (_returnedToSource)
            return SaveReturnedSourceMap();
        if (_loadedDestinationPresentation is null)
            return null;
        var transition = _exitGridTransition ?? throw new InvalidOperationException(
            "Fallout active destination has no exit-grid contract.");
        if (string.IsNullOrWhiteSpace(_destinationPresentationPath))
            throw new InvalidOperationException("Fallout active destination has no explicit presentation path.");
        var destination = _loadedDestinationPresentation;
        return new
        {
            schema = ActiveMapSchema,
            kind = "destination",
            mapId = destination.Map.Id,
            sourceFile = destination.Map.SourceFile,
            sourceMapSha256 = destination.SourceMapSha256,
            elevation = transition.DestinationElevation,
            presentation = new
            {
                path = _destinationPresentationPath,
                sha256 = destination.Catalog.CampaignSha256,
            },
            inventoryInteraction = _destinationInventoryInteraction is null ? null : new
            {
                path = _destinationInventoryInteraction.Path,
                sha256 = _destinationInventoryInteraction.Sha256,
            },
            flareUse = _destinationFlareUse is null ? null : new
            {
                path = _destinationFlareUse.Path,
                sha256 = _destinationFlareUse.Sha256,
            },
            genericDoor = _destinationGenericDoor is null ? null : new
            {
                path = _destinationGenericDoor.Path,
                sha256 = _destinationGenericDoor.Sha256,
            },
            medicLook = _destinationMedicLook is null ? null : new
            {
                path = _destinationMedicLook.Path,
                sha256 = _destinationMedicLook.Sha256,
            },
            returnExitGrid = _destinationReturnExitGrid is null ? null : new
            {
                path = _destinationReturnExitGrid.Path,
                sha256 = _destinationReturnExitGrid.Sha256,
            },
        };
    }

    private object SaveReturnedSourceMap()
    {
        var forward = _exitGridTransition ?? throw new InvalidOperationException(
            "Fallout returned source save has no original exit-grid contract.");
        var reverse = _destinationReturnExitGrid ?? throw new InvalidOperationException(
            "Fallout returned source save has no reciprocal exit-grid contract.");
        if (_activatedDestinationReturnExitGridTile is null ||
            reverse.DestinationMapIndex != forward.SourceMapIndex ||
            reverse.DestinationMapName != forward.SourceMapName ||
            !string.Equals(reverse.DestinationMapSha256, _sourceMapSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Fallout returned source save has an incomplete reciprocal MAP join.");
        var inactiveDestinationInventoryHost = _destinationInventoryInteraction is null
            ? null
            : new
            {
                presentation = new
                {
                    path = _destinationPresentationPath ?? throw new InvalidOperationException(
                        "Fallout returned source save has no explicit destination presentation path."),
                    sha256 = Fo1DestinationPresentationContract.Load(
                        _destinationPresentationPath!, forward).Catalog.CampaignSha256,
                },
                interaction = new
                {
                    path = _destinationInventoryInteraction.Path,
                    sha256 = _destinationInventoryInteraction.Sha256,
                    hostSerial = _destinationInventoryInteraction.Host.Serial,
                },
            };
        return new
        {
            schema = ActiveMapSchema,
            kind = "source-return",
            mapIndex = reverse.DestinationMapIndex,
            mapName = reverse.DestinationMapName,
            sourceMapSha256 = reverse.DestinationMapSha256,
            elevation = reverse.DestinationElevation,
            rotation = reverse.DestinationRotation,
            arrivalTile = reverse.DestinationTile,
            sourceSceneSha256 = _sceneSha256,
            returnExitGrid = new { path = reverse.Path, sha256 = reverse.Sha256 },
            inactiveDestinationInventoryHost,
        };
    }

    private void LoadSavedSourceReturn(JsonElement activeMap, int playerTile)
    {
        var forward = _exitGridTransition ?? throw new InvalidOperationException(
            "Fallout returned source save has no original exit-grid contract.");
        if (activeMap.GetProperty("schema").GetString() != ActiveMapSchema ||
            activeMap.GetProperty("kind").GetString() != "source-return" ||
            activeMap.GetProperty("mapIndex").GetInt32() != forward.SourceMapIndex ||
            activeMap.GetProperty("mapName").GetString() != forward.SourceMapName ||
            activeMap.GetProperty("sourceMapSha256").GetString() != _sourceMapSha256 ||
            activeMap.GetProperty("elevation").GetInt32() < 0 ||
            activeMap.GetProperty("rotation").GetInt32() is < 0 or >= Fo1HexMath.DirectionCount ||
            activeMap.GetProperty("arrivalTile").GetInt32() is < 0 or >= Fo1HexMath.Width * Fo1HexMath.Height ||
            activeMap.GetProperty("sourceSceneSha256").GetString() != _sceneSha256)
            throw new InvalidOperationException("Fallout returned source save differs from its V13ENT MAP contract.");
        if (string.IsNullOrWhiteSpace(_destinationReturnExitGridPath))
            throw new InvalidOperationException(
                "Fallout returned source restore requires the explicit reciprocal exit-grid descriptor.");
        var savedReverse = activeMap.GetProperty("returnExitGrid");
        if (savedReverse.GetProperty("path").GetString() != _destinationReturnExitGridPath)
            throw new InvalidOperationException(
                "Fallout returned source reciprocal exit-grid path differs from launch input.");
        var reverse = Fo1ExitGridTransitionContract.Load(_destinationReturnExitGridPath);
        if (savedReverse.GetProperty("sha256").GetString() != reverse.Sha256 ||
            reverse.SourceMapIndex != forward.DestinationMapIndex ||
            reverse.SourceMapName != forward.DestinationMapName ||
            !string.Equals(reverse.SourceMapSha256, forward.DestinationMapSha256, StringComparison.OrdinalIgnoreCase) ||
            reverse.DestinationMapIndex != forward.SourceMapIndex ||
            reverse.DestinationMapName != forward.SourceMapName ||
            !string.Equals(reverse.DestinationMapSha256, _sourceMapSha256, StringComparison.OrdinalIgnoreCase) ||
            reverse.DestinationTile != activeMap.GetProperty("arrivalTile").GetInt32() ||
            reverse.DestinationElevation != activeMap.GetProperty("elevation").GetInt32() ||
            reverse.DestinationRotation != activeMap.GetProperty("rotation").GetInt32() ||
            !_sourceWalkable[playerTile])
            throw new InvalidOperationException("Fallout returned source MAP join drifted or saved an invalid player tile.");
        _destinationReturnExitGrid = reverse;
        _returnInactiveDestinationHostSerials.Clear();
        if (activeMap.TryGetProperty("inactiveDestinationInventoryHost", out var inactiveHost) &&
            inactiveHost.ValueKind != JsonValueKind.Null)
        {
            if (string.IsNullOrWhiteSpace(_destinationPresentationPath) ||
                string.IsNullOrWhiteSpace(_destinationInventoryInteractionPath))
                throw new InvalidOperationException(
                    "Fallout returned source restore requires explicit destination inventory provenance.");
            var presentation = inactiveHost.GetProperty("presentation");
            if (presentation.GetProperty("path").GetString() != _destinationPresentationPath)
                throw new InvalidOperationException(
                    "Fallout returned source destination presentation path differs from launch input.");
            var destination = Fo1DestinationPresentationContract.Load(_destinationPresentationPath, forward);
            if (presentation.GetProperty("sha256").GetString() != destination.Catalog.CampaignSha256)
                throw new InvalidOperationException(
                    "Fallout returned source destination presentation hash drifted.");
            var interaction = inactiveHost.GetProperty("interaction");
            if (interaction.GetProperty("path").GetString() != _destinationInventoryInteractionPath)
                throw new InvalidOperationException(
                    "Fallout returned source destination inventory path differs from launch input.");
            var loadedInteraction = Fo1DestinationInventoryInteractionContract.Load(
                _destinationInventoryInteractionPath, destination, forward);
            if (interaction.GetProperty("sha256").GetString() != loadedInteraction.Sha256 ||
                interaction.GetProperty("hostSerial").GetInt32() != loadedInteraction.Host.Serial)
                throw new InvalidOperationException(
                    "Fallout returned source destination inventory hash drifted.");
            _destinationInventoryInteraction = loadedInteraction;
            _destinationFlareUse = string.IsNullOrWhiteSpace(_destinationFlareUsePath)
                ? null
                : Fo1DestinationFlareUseContract.Load(
                    _destinationFlareUsePath, loadedInteraction);
            _destinationGenericDoor = string.IsNullOrWhiteSpace(_destinationGenericDoorPath)
                ? null
                : Fo1DestinationGenericDoorContract.Load(
                    _destinationGenericDoorPath, destination, forward);
            _destinationMedicLook = string.IsNullOrWhiteSpace(_destinationMedicLookPath)
                ? null
                : _destinationGenericDoor is null
                    ? throw new InvalidOperationException(
                        "Fallout returned source restore requires its explicit generic-door prerequisite.")
                    : Fo1DestinationMedicLookContract.Load(
                        _destinationMedicLookPath, destination, forward, _destinationGenericDoor);
            _returnInactiveDestinationHostSerials.Add(loadedInteraction.Host.Serial);
        }
        _loadedDestinationPresentation = null;
        _returnedToSource = true;
    }

    private Fo1DestinationPresentationContract LoadSavedDestination(JsonElement activeMap)
    {
        var transition = _exitGridTransition ?? throw new InvalidOperationException(
            "Fallout active destination save has no exit-grid contract.");
        if (_activatedExitGridTile is null)
            throw new InvalidOperationException("Fallout active destination save has no committed exit-grid.");
        if (activeMap.GetProperty("schema").GetString() != ActiveMapSchema ||
            activeMap.GetProperty("kind").GetString() != "destination" ||
            activeMap.GetProperty("mapId").GetString() !=
                Path.GetFileNameWithoutExtension(transition.DestinationMapName).ToLowerInvariant() ||
            activeMap.GetProperty("sourceFile").GetString() != transition.DestinationMapName ||
            activeMap.GetProperty("sourceMapSha256").GetString() != transition.DestinationMapSha256 ||
            activeMap.GetProperty("elevation").GetInt32() != transition.DestinationElevation)
            throw new InvalidOperationException("Fallout active destination save differs from its exit-grid contract.");
        if (string.IsNullOrWhiteSpace(_destinationPresentationPath))
            throw new InvalidOperationException(
                "Fallout active destination restore requires an explicit presentation cache path.");
        var presentation = activeMap.GetProperty("presentation");
        if (presentation.GetProperty("path").GetString() != _destinationPresentationPath)
            throw new InvalidOperationException("Fallout active destination save presentation path differs from launch input.");
        var destination = Fo1DestinationPresentationContract.Load(_destinationPresentationPath, transition);
        if (presentation.GetProperty("sha256").GetString() != destination.Catalog.CampaignSha256)
            throw new InvalidOperationException("Fallout active destination save presentation hash drifted.");
        if (activeMap.TryGetProperty("inventoryInteraction", out var interaction) &&
            interaction.ValueKind != JsonValueKind.Null)
        {
            if (string.IsNullOrWhiteSpace(_destinationInventoryInteractionPath) ||
                interaction.GetProperty("path").GetString() != _destinationInventoryInteractionPath)
                throw new InvalidOperationException("Fallout active destination inventory interaction path differs from launch input.");
            var loadedInteraction = Fo1DestinationInventoryInteractionContract.Load(
                _destinationInventoryInteractionPath,
                destination,
                transition);
            if (interaction.GetProperty("sha256").GetString() != loadedInteraction.Sha256)
                throw new InvalidOperationException("Fallout active destination inventory interaction hash drifted.");
            _destinationInventoryInteraction = loadedInteraction;
        }
        if (activeMap.TryGetProperty("flareUse", out var flareUse) &&
            flareUse.ValueKind != JsonValueKind.Null)
        {
            if (string.IsNullOrWhiteSpace(_destinationFlareUsePath) ||
                flareUse.GetProperty("path").GetString() != _destinationFlareUsePath ||
                _destinationInventoryInteraction is null)
                throw new InvalidOperationException("Fallout active destination flare use path differs from launch input.");
            var loadedFlareUse = Fo1DestinationFlareUseContract.Load(
                _destinationFlareUsePath, _destinationInventoryInteraction);
            if (flareUse.GetProperty("sha256").GetString() != loadedFlareUse.Sha256)
                throw new InvalidOperationException("Fallout active destination flare use hash drifted.");
            _destinationFlareUse = loadedFlareUse;
        }
        if (activeMap.TryGetProperty("genericDoor", out var genericDoor) &&
            genericDoor.ValueKind != JsonValueKind.Null)
        {
            if (string.IsNullOrWhiteSpace(_destinationGenericDoorPath) ||
                genericDoor.GetProperty("path").GetString() != _destinationGenericDoorPath)
                throw new InvalidOperationException("Fallout active destination generic-door path differs from launch input.");
            var loadedGenericDoor = Fo1DestinationGenericDoorContract.Load(
                _destinationGenericDoorPath, destination, transition);
            if (genericDoor.GetProperty("sha256").GetString() != loadedGenericDoor.Sha256)
                throw new InvalidOperationException("Fallout active destination generic-door hash drifted.");
            _destinationGenericDoor = loadedGenericDoor;
        }
        if (activeMap.TryGetProperty("medicLook", out var medicLook) &&
            medicLook.ValueKind != JsonValueKind.Null)
        {
            if (string.IsNullOrWhiteSpace(_destinationMedicLookPath) ||
                medicLook.GetProperty("path").GetString() != _destinationMedicLookPath ||
                _destinationGenericDoor is null)
                throw new InvalidOperationException("Fallout active destination Medic look path differs from launch input.");
            var loadedMedicLook = Fo1DestinationMedicLookContract.Load(
                _destinationMedicLookPath, destination, transition, _destinationGenericDoor);
            if (medicLook.GetProperty("sha256").GetString() != loadedMedicLook.Sha256)
                throw new InvalidOperationException("Fallout active destination Medic look hash drifted.");
            _destinationMedicLook = loadedMedicLook;
        }
        if (activeMap.TryGetProperty("returnExitGrid", out var returnExitGrid) &&
            returnExitGrid.ValueKind != JsonValueKind.Null)
        {
            if (string.IsNullOrWhiteSpace(_destinationReturnExitGridPath) ||
                returnExitGrid.GetProperty("path").GetString() != _destinationReturnExitGridPath)
                throw new InvalidOperationException("Fallout active destination return exit-grid path differs from launch input.");
            var loadedReturnExit = Fo1ExitGridTransitionContract.Load(_destinationReturnExitGridPath);
            loadedReturnExit.ValidateAgainstScene(destination.SourceMapSha256);
            if (returnExitGrid.GetProperty("sha256").GetString() != loadedReturnExit.Sha256)
                throw new InvalidOperationException("Fallout active destination return exit-grid hash drifted.");
            _destinationReturnExitGrid = loadedReturnExit;
        }
        _loadedDestinationPresentation = destination;
        return destination;
    }

    private static Fo1CharacterProfile ParseSavedCharacter(JsonElement source)
    {
        var schema = source.GetProperty("schema").GetString();
        if (!string.Equals(
                schema,
                "opennv-fo1-character/v1",
                StringComparison.Ordinal) &&
            !string.Equals(
                schema,
                "opennv-fo1-character/v2",
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Fallout save contains an unknown character schema: {schema}");
        var special = source.GetProperty("allocatedSpecial");
        var profile = new Fo1CharacterProfile(
            source.GetProperty("name").GetString()!,
            source.GetProperty("age").GetInt32(),
            source.GetProperty("sex").GetString()!,
            special.GetProperty("strength").GetInt32(),
            special.GetProperty("perception").GetInt32(),
            special.GetProperty("endurance").GetInt32(),
            special.GetProperty("charisma").GetInt32(),
            special.GetProperty("intelligence").GetInt32(),
            special.GetProperty("agility").GetInt32(),
            special.GetProperty("luck").GetInt32(),
            source.GetProperty("taggedSkills").EnumerateArray()
                .Select(row => row.GetString()!)
                .ToArray(),
            source.GetProperty("traits").EnumerateArray()
                .Select(row => row.GetString()!)
                .ToArray());
        if (schema == "opennv-fo1-character/v2")
        {
            var appearance = source.GetProperty("appearance");
            if (appearance.ValueKind != JsonValueKind.Object ||
                appearance.GetProperty("schema").GetString() !=
                    Fo1CharacterAppearance.ExpectedSchema ||
                appearance.GetProperty("mode").GetString() !=
                    "hex-local-procedural-custom")
                throw new InvalidOperationException(
                    "Fallout save contains an unknown character appearance.");
            profile = profile with
            {
                Appearance = new Fo1CharacterAppearance(
                    appearance.GetProperty("faceShapeId").GetString()!,
                    appearance.GetProperty("hairStyleId").GetString()!,
                    appearance.GetProperty("skinToneId").GetString()!,
                    appearance.GetProperty("hairColorId").GetString()!,
                    appearance.GetProperty("eyeColorId").GetString()!,
                    appearance.GetProperty("recipeId").GetString()!,
                    appearance.GetProperty("recipeSha256").GetString()!,
                    appearance.GetProperty("generatorId").GetString()!,
                    appearance.GetProperty("portraitPath").GetString()!,
                    appearance.GetProperty("portraitSha256").GetString()!,
                    appearance.GetProperty("portraitWidth").GetInt32(),
                    appearance.GetProperty("portraitHeight").GetInt32()),
            };
        }
        profile.Validate();
        return profile;
    }

    private static string ResolvePath(string path) =>
        path.StartsWith("user://", StringComparison.Ordinal)
            ? ProjectSettings.GlobalizePath(path)
            : Path.GetFullPath(path);

    internal readonly record struct PlayerProfile(
        string Name,
        Texture2D Texture,
        int Width,
        int Height,
        float PixelSize,
        Vector2 FrameOffset,
        int HitPoints,
        int ArmorClass,
        int Sequence,
        int Strength,
        int Perception,
        int MeleeDamage,
        IReadOnlyDictionary<string, int> Skills,
        WeaponProfile RangedWeapon,
        WeaponProfile MeleeWeapon,
        InventoryProfile Inventory)
    {
        internal string WeaponName => RangedWeapon.Name;
        internal int WeaponMinimumDamage => RangedWeapon.MinimumDamage;
        internal int WeaponMaximumDamage => RangedWeapon.MaximumDamage;
        internal int WeaponRangeHexes => RangedWeapon.RangeHexes;
        internal int WeaponActionPointCost => RangedWeapon.ActionPointCost;

        internal void Validate()
        {
            RangedWeapon.Validate();
            MeleeWeapon.Validate();
            if (MeleeWeapon.Melee is false || RangedWeapon.Melee ||
                Strength <= 0 || Perception <= 0 || MeleeDamage < 0 ||
                Skills.Count == 0 || Skills.Values.Any(value => value < 0))
                throw new InvalidOperationException("Fallout player combat profile is invalid.");
            Inventory.Validate();
        }
    }

    internal readonly record struct WeaponProfile(
        string Name,
        string Pid,
        string PrototypeSha256,
        string Skill,
        int MinimumDamage,
        int MaximumDamage,
        int RangeHexes,
        int ActionPointCost,
        int MinimumStrength,
        int RoundsPerAttack,
        int AmmunitionCapacity,
        int InitialLoadedRounds,
        bool Melee)
    {
        internal void Validate()
        {
            if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Pid) ||
                PrototypeSha256.Length != Fo1TacticalSessionNumericContracts.PresentationInt64 || string.IsNullOrWhiteSpace(Skill) ||
                MinimumDamage <= 0 || MaximumDamage < MinimumDamage || RangeHexes <= 0 ||
                ActionPointCost <= 0 || MinimumStrength <= 0)
                throw new InvalidOperationException("Fallout weapon profile is invalid.");
            if (Melee && (RoundsPerAttack != 0 || AmmunitionCapacity != 0 || InitialLoadedRounds != 0) ||
                !Melee && (RoundsPerAttack <= 0 || AmmunitionCapacity <= 0 ||
                    InitialLoadedRounds is < 0 || InitialLoadedRounds > AmmunitionCapacity))
                throw new InvalidOperationException("Fallout weapon ammunition contract is invalid.");
        }
    }

    internal sealed record InventoryProfile(
        string EquippedRangedSymbol,
        string EquippedMeleeSymbol,
        string AmmunitionSymbol,
        int AmmunitionRoundsPerObject,
        IReadOnlyDictionary<string, string> DisplayNames,
        IReadOnlyList<InventoryStack> Base,
        IReadOnlyList<InventoryTagBonus> TagBonuses)
    {
        internal void Validate()
        {
            if (string.IsNullOrWhiteSpace(EquippedRangedSymbol) ||
                string.IsNullOrWhiteSpace(EquippedMeleeSymbol) ||
                string.IsNullOrWhiteSpace(AmmunitionSymbol) ||
                AmmunitionRoundsPerObject <= 0 || DisplayNames.Count == 0 ||
                DisplayNames.Any(row => string.IsNullOrWhiteSpace(row.Key) ||
                    string.IsNullOrWhiteSpace(row.Value)) || Base.Count == 0 ||
                Base.Any(row => row.Objects <= 0) ||
                !Base.Any(row => row.Symbol == EquippedRangedSymbol) ||
                !Base.Any(row => row.Symbol == EquippedMeleeSymbol) ||
                !Base.Any(row => row.Symbol == AmmunitionSymbol) ||
                TagBonuses.Select(row => row.Skill).Distinct(StringComparer.Ordinal).Count() !=
                    TagBonuses.Count ||
                TagBonuses.Any(row => string.IsNullOrWhiteSpace(row.Skill) ||
                    row.Items.Count == 0 || row.Items.Any(item => item.Objects <= 0)) ||
                Base.Concat(TagBonuses.SelectMany(row => row.Items))
                    .Any(row => !DisplayNames.ContainsKey(row.Symbol)))
                throw new InvalidOperationException("Fallout starting inventory profile is invalid.");
        }
    }

    internal readonly record struct InventoryStack(string Symbol, string Pid, int Objects);

    internal sealed record MapInventoryHost(
        int Serial,
        int Tile,
        string Pid,
        string PrototypeSha256,
        IReadOnlyList<MapInventoryItem> Items)
    {
        internal void Validate()
        {
            if (Serial < 0 || Tile < 0 || Tile >= Fo1HexMath.Width * Fo1HexMath.Height ||
                string.IsNullOrWhiteSpace(Pid) || PrototypeSha256.Length !=
                Fo1TacticalSessionNumericContracts.PresentationInt64 || Items.Count == 0 ||
                Items.Select(item => item.Index).Distinct().Count() != Items.Count ||
                Items.Any(item => !item.IsValid))
                throw new InvalidOperationException("Fallout MAP inventory-host contract is invalid.");
        }
    }

    internal readonly record struct MapInventoryItem(
        int Index,
        int Serial,
        string Symbol,
        string DisplayName,
        string Pid,
        int Objects,
        string PrototypeSha256,
        string SubtypeName)
    {
        internal bool IsValid => Index >= 0 && Serial >= 0 &&
            !string.IsNullOrWhiteSpace(Symbol) && !string.IsNullOrWhiteSpace(DisplayName) &&
            !string.IsNullOrWhiteSpace(Pid) &&
            Objects > 0 && PrototypeSha256.Length ==
            Fo1TacticalSessionNumericContracts.PresentationInt64 &&
            !string.IsNullOrWhiteSpace(SubtypeName);
    }

    internal readonly record struct MapInventoryPickup(
        MapInventoryHost Host,
        IReadOnlyDictionary<string, int> Inventory);

    internal readonly record struct InventoryTagBonus(
        string Skill,
        IReadOnlyList<InventoryStack> Items);

    private sealed record FirstPersonTarget(
        Fo1Mob Mob,
        float Along,
        float Perpendicular);

    internal readonly record struct CombatResult(
        bool Attempted,
        string Kind,
        string Mode,
        bool Hit,
        int Damage,
        bool Killed,
        int ChancePercent,
        int RollPercent);
}
