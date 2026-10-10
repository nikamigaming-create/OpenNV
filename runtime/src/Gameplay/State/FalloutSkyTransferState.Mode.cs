using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutSkyTransferState
{
    private readonly Dictionary<FalloutSkyModeRole, FalloutSkyModeChild> _modeChildren =
        Enum.GetValues<FalloutSkyModeRole>().ToDictionary(role => role, role => new FalloutSkyModeChild(role, null, null));
    private FalloutSkyModeCall? _modeCall;
    private bool _modeExecuting;
    private string? _modeDeferredUnowned;
    private Guid? _modeHistoricalSky, _modeHistoricalProcess;
    private readonly HashSet<FalloutSkyModeRole> _modeColdChildren = [];
    internal string? ModeSaveBlocker => _modeFailure ?? (_modeExecuting ? "source-Sky-mode-child-entered" :
        _modeColdChildren.Count != 0 ? "source-Sky-mode-cold-child-republication-pending" :
        _modeCall is { Returned: false } call ? "source-Sky-mode-entered-prefix/" + call.EnteredChild : null);
    internal object ModeState => new
    {
        call = _modeCall,
        children = _modeChildren.Values.OrderBy(row => row.Role).ToArray(),
        deferredScenes = _modeDeferredUnowned ?? "actual-source-constructor-empty",
        deferredBy = ModeSaveBlocker
    };

    internal void MarkActualModeChild(FalloutSkyModeRole role, Guid actualOwner, string missingConsumer)
    {
        BindPresentationThread(); RequireWriter(); ArgumentException.ThrowIfNullOrWhiteSpace(missingConsumer);
        if (actualOwner == Guid.Empty || !Enum.IsDefined(role)) throw new InvalidDataException("Sky mode factory omitted its actual child identity.");
        if (_modeChildren[role].Owner is { } previous && previous != actualOwner)
            throw new InvalidOperationException("An original Sky mode child still owns that field.");
        _modeChildren[role] = new(role, actualOwner, missingConsumer); Next();
        _modeColdChildren.Remove(role);
    }
    internal void RetireActualModeChild(FalloutSkyModeRole role, Guid actualOwner)
    {
        if (_modeChildren[role].Owner != actualOwner) throw new InvalidDataException("Foreign Sky mode child attempted field retirement.");
        _modeChildren[role] = new(role, null, null); Next();
        _modeColdChildren.Remove(role);
    }
    internal void LoseActualModeChildProjection(FalloutSkyModeRole role, Guid actualOwner)
    {
        if (_modeChildren[role].Owner != actualOwner)
            throw new InvalidDataException("Foreign Sky mode child attempted projection retirement.");
        // Releasing presentation resources does not free the living source
        // child. Keep its identity and refuse consumption until real binding.
        _modeChildren[role] = new(role, actualOwner, "source-Sky-mode-" + role + "-actual-projection-republication-pending");
        _modeColdChildren.Add(role); Next();
    }
    internal FalloutSkyModeCall? SelectModeForActualCell(FalloutMainPlayerCellInvocation invocation,
        FalloutMainPlayerSourceCell cell, Action requireActualCell)
    {
        if (invocation.Step is not (FalloutMainPlayerCellStep.CellAttach or FalloutMainPlayerCellStep.PendingDestination))
            throw new InvalidOperationException("CELL Sky mode is outside its actual Main load/attachment child.");
        invocation.Require(invocation.Step); BindPresentationThread(); RequireWriter();
        ArgumentNullException.ThrowIfNull(requireActualCell); requireActualCell();
        if (_process != invocation.Main.Process || _modeExecuting || _modeCall is { Returned: false })
            throw new InvalidOperationException("Sky mode changed its actual Main/process or reentered an original child.");
        var source = FalloutSkyModeSource.Read(Source);
        var requested = source.Requested(Mode, cell.CellFlags);
        if (requested is null) return null; // Original source mode-zero caller suppresses the call.
        var record = _records.GetEffective(cell.Source.Cell);
        if (record.Signature != "CELL" || record.Flags != cell.Source.Flags ||
            !Convert.ToHexString(SHA256.HashData(record.ReadData())).Equals(cell.Source.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Sky mode changed its exact winning CELL payload/header identity.");
        var data = record.ReadSubrecords().Where(row => row.Signature == "DATA").ToArray();
        if (data.Length != 1 || data[0].Data.Length is not (1 or 2) || data[0].Data.Span[0] != cell.CellFlags)
            throw new InvalidDataException("Sky mode borrowed a different actual winning CELL DATA byte.");
        var changed = Next();
        _modeHistoricalSky = null; _modeHistoricalProcess = null;
        _modeCall = new(Guid.NewGuid(), invocation.Main.Identity, _process, Identity, source.Contract, cell.Source.Cell,
            cell.Source.Sha256, cell.CellFlags, Mode, requested.Value, changed, changed, [], null, false, null, null);
        var completed = new List<FalloutSkyModeStep>(); _modeExecuting = true;
        try
        {
            foreach (var step in source.Steps(Mode, requested.Value))
            {
                Check(); _modeCall = _modeCall with { Changed = Next(), EnteredChild = step };
                ApplyModeStep(step, requested.Value);
                Check(); completed.Add(step);
                _modeCall = _modeCall with { Changed = Next(), Completed = completed.ToArray(), EnteredChild = null };
            }
            _modeCall = _modeCall with { Changed = Next(), Returned = true }; return _modeCall;
        }
        catch (Exception failure)
        {
            _modeFailure = failure.ToString();
            _modeCall = _modeCall! with
            {
                Changed = Next(),
                FailureType = failure.GetType().FullName,
                Error = string.IsNullOrWhiteSpace(failure.Message) ? failure.GetType().Name : failure.Message
            };
            throw;
        }
        finally { _modeExecuting = false; }
        void Check()
        {
            invocation.Require(invocation.Step); requireActualCell();
            if (_process != invocation.Main.Process) throw new InvalidOperationException("Sky mode lost its original invocation epoch.");
        }
    }
    private void ApplyModeStep(FalloutSkyModeStep step, int requested)
    {
        switch (step)
        {
            case FalloutSkyModeStep.ActiveResourceLoad:
                throw new NotSupportedException("source-Sky-active-resource-Sun-Night-factory-and-weather-load-consumer-unowned");
            case FalloutSkyModeStep.CloudReset:
                var call = _modeCall!;
                ResetChild(_cloudBinding, _clouds, "Clouds", new(Identity, _process, call.Call, call.Main, null,
                    call.Entered, FalloutSkyResetOrigin.ModeSelection, Source.Contract));
                break;
            case FalloutSkyModeStep.CloudRelease:
                if (_cloudBinding.Disposition == FalloutSkyChildDisposition.ConstructorNull) break;
                if (_clouds is not IFalloutSkyCloudModeRelease release || release.Sky != Identity || release.Identity != _clouds.Identity)
                    throw new NotSupportedException("source-Sky-mode-Clouds-nonnull-release-owner-unowned");
                _cloudBinding = _cloudBinding with { Source = release.ReleaseForMode(_modeCall!) }; break;
            case FalloutSkyModeStep.MasserTextures: KnownNull(FalloutSkyModeRole.Masser); break;
            case FalloutSkyModeStep.SecundaTextures: KnownNull(FalloutSkyModeRole.Secunda); break;
            case FalloutSkyModeStep.SunTextures:
            case FalloutSkyModeStep.SunShadowHide:
            case FalloutSkyModeStep.SunShadowShow: KnownNull(FalloutSkyModeRole.Sun); break;
            case FalloutSkyModeStep.NightRelease: KnownNull(FalloutSkyModeRole.Night); break;
            case FalloutSkyModeStep.RootHide:
            case FalloutSkyModeStep.RootShow: KnownNull(FalloutSkyModeRole.Root); break;
            case FalloutSkyModeStep.DeferredScenes:
                if (_modeDeferredUnowned is { } deferred) throw new NotSupportedException(deferred);
                break; // Exact source constructor list is empty; no native task substitutes for it.
            case FalloutSkyModeStep.PrecipitationZero:
                ResetChild(_precipitationBinding, _precipitation, "Precipitation", new(Identity, _process, _modeCall!.Call,
                    _modeCall.Main, null, _modeCall.Entered, FalloutSkyResetOrigin.ModeSelection, Source.Contract)); break;
            case FalloutSkyModeStep.ModeStore:
            case FalloutSkyModeStep.FinalModeStore: Mode = requested; Next(); break;
            case FalloutSkyModeStep.FlashZero: TransitionBits = 0; Next(); break;
            case FalloutSkyModeStep.ImageManager: UpdateResetImages(); break;
            case FalloutSkyModeStep.CloudActive:
                if (_cloudBinding.Disposition != FalloutSkyChildDisposition.ConstructorNull)
                    throw new NotSupportedException("source-Sky-Clouds-active-byte-writer-consumer-unowned");
                break;
            default: throw new InvalidDataException("Sky mode contains an unknown original step.");
        }
    }
    private void KnownNull(FalloutSkyModeRole role)
    {
        var field = _modeChildren[role];
        if (field.Owner is not null || field.Unowned is not null)
            throw new NotSupportedException(field.Unowned ?? "source-Sky-mode-" + role + "-nonnull-consumer-unowned");
    }
}
