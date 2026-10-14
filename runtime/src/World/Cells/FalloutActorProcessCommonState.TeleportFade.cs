using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutProcessOpacityFields(uint BaseBits, uint EffectiveBits, long Stores)
{
    internal static FalloutProcessOpacityFields Constructed => new(FalloutActorTeleportFade.One, FalloutActorTeleportFade.One, 0);
}
internal sealed record FalloutProcessOpacityQuery(FalloutProcessSceneQuery Process,
    FalloutProcessOpacityFields Opacity, FalloutActorTeleportFadeFields? Teleport);
internal sealed record FalloutProcessTeleportFadeUpdate(FalloutProcessSceneQuery Before, long Changed,
    FalloutActorTeleportFadeFields BeforeFields, FalloutActorTeleportFadeFields AfterFields,
    FalloutActorTeleportFadeTrace Trace);

internal sealed partial class FalloutActorProcessCommonState
{
    internal FalloutProcessTeleportFadeUpdate? ReadTeleportFadeReceipt(FalloutFormKey actor, long epoch)
    {
        RequireNotBusy(); var current = Require(actor);
        if (current.Epoch != epoch) throw new InvalidDataException("Teleport receipt changed its actual process epoch.");
        return current.SceneFields?.TeleportCall;
    }
    internal FalloutProcessOpacityQuery ReadOpacity(FalloutFormKey actor, long epoch)
    {
        var process = ReadSceneQueries(actor, epoch);
        var entry = Require(actor);
        var opacity = entry.SceneFields?.Opacity ?? throw new NotSupportedException("source-current-process-opacity-constructor-unavailable");
        return new(process, opacity, entry.SceneFields?.TeleportFade);
    }

    internal FalloutProcessTeleportFadeUpdate UpdateTeleportFade(FalloutPlayerOpacitySource source,
        FalloutFormKey actor, long epoch, Guid invocation,
        Func<FalloutProcessOpacityQuery, Func<FalloutProcessOpacityQuery>, Action<uint>, IFalloutActorTeleportFadeConsumers> bind)
    {
        source.Validate(); ArgumentNullException.ThrowIfNull(bind);
        var before = ReadOpacity(actor, epoch);
        if (before.Process.ExecutableSha256 != source.Controls.Main.EngineSha256 ||
            before.Process.Level != FalloutDetectionProcessLevel.High || before.Teleport is not { } initial)
            throw new InvalidDataException("Teleport fade changed its actual High process source, epoch or constructor.");
        var fault = _callbackFault; _busy = true;
        try
        {
            var consumers = bind(before, ReadCurrentOpacity, StoreEffective);
            Check();
            FalloutActorTeleportFade.Execute(invocation, before.Process.Actor.EnginePlayer, consumers, ReadFade, StoreFade, Observe);
            return Require(actor).SceneFields!.TeleportCall!;
        }
        catch (Exception error) { HoldMutation(actor, error.Message); throw; }
        finally { _busy = false; }
        FalloutActorTeleportFadeFields ReadFade() { Check(); return Require(actor).SceneFields!.TeleportFade!; }
        FalloutProcessOpacityQuery ReadCurrentOpacity()
        {
            Check(); var current = Require(actor);
            return new(before.Process with { Changed = current.Changed }, current.SceneFields!.Opacity!, current.SceneFields.TeleportFade);
        }
        void StoreFade(FalloutActorTeleportFadeFields value)
        {
            Check(); var current = Require(actor);
            _current[actor] = current with { SceneFields = current.SceneFields! with { TeleportFade = value }, Changed = Next() };
        }
        void StoreEffective(uint alpha)
        {
            Check(); var current = Require(actor); var opacity = current.SceneFields!.Opacity!;
            _current[actor] = current with
            {
                SceneFields = current.SceneFields with
                { Opacity = opacity with { EffectiveBits = alpha, Stores = checked(opacity.Stores + 1) } },
                Changed = Next()
            };
        }
        void Observe(FalloutActorTeleportFadeTrace trace)
        {
            if (trace.Failure is null) Check();
            var current = Require(actor);
            _current[actor] = current with
            {
                SceneFields = current.SceneFields! with
                { TeleportCall = new(before.Process, current.Changed, initial, current.SceneFields!.TeleportFade!, trace) }
            };
        }
        void Check()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_callbackFault != fault || Require(actor).Epoch != epoch || Require(actor).Gameplay?.Ownership != before.Process.GameplayOwnership)
                throw new InvalidOperationException("Teleport child lost its current process lease or swallowed reentry.");
        }
    }

    private static void ValidateOpacityFields(FalloutProcessCommonEntry entry, string executable, string stack)
    {
        var opacity = entry.SceneFields?.Opacity; var fade = entry.SceneFields?.TeleportFade;
        if ((entry.SceneFields is not null) != (opacity is not null) ||
            (entry.SceneFields is not null && entry.Level == FalloutDetectionProcessLevel.High) != (fade is not null) ||
            opacity is not null && (opacity.Stores < 0 || opacity.BaseBits != FalloutActorTeleportFade.One ||
                opacity.Stores == 0 && opacity != FalloutProcessOpacityFields.Constructed))
            throw new InvalidDataException("Process opacity lost independent inherited/High constructors or a retained effective store.");
        if (fade is not null) FalloutActorTeleportFade.Validate(fade);
        if (entry.SceneFields?.TeleportCall is { } call)
        {
            if (call.Before is null)
                throw new InvalidDataException("Cold teleport fade omitted its actual current process query.");
            FalloutProcessTeleportFadeReceipt.Validate(call, stack, executable, entry.Source.EnginePlayer);
            if (fade is null || call.AfterFields != fade ||
                call.Before.Actor != entry.Source || call.Before.Epoch != entry.Epoch || call.Before.Level != entry.Level ||
                entry.Gameplay is { } lease && call.Before.GameplayOwnership != lease.Ownership ||
                call.Changed > entry.Changed)
                throw new InvalidDataException("Cold teleport fade lost actual process order or its returned/failed prefix.");
        }
    }
}
