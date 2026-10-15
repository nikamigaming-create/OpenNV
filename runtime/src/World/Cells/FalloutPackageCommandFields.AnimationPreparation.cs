using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorPackageCommandFields
{
    // A pointer getter does not execute its nonnull payload. The actual
    // preparation caller gets the controller before dereferencing AnimData.
    // This bounded read uses the existing fields and availability lane, and
    // does not append a command operation for every ordinary frame.
    internal FalloutPlayerSelectedAnimationRead ReadAnimationForPreparation(FalloutPlayerPostAnimationSource source,
        FalloutPlayerLateFrameInvocation frame, Func<FalloutPlayerViewRead> readView,
        Func<FalloutProcessSceneQuery> readCommon, Action<FalloutPlayerLateFrameInvocation> requireActual)
    {
        ArgumentNullException.ThrowIfNull(readView); ArgumentNullException.ThrowIfNull(readCommon);
        ArgumentNullException.ThrowIfNull(requireActual); var fault = _fault; var sequence = _sequence; Check();
        if (!_availability[FalloutPackageCommandFieldLane.Animation].Available)
            throw new NotSupportedException(_availability[FalloutPackageCommandFieldLane.Animation].Boundary);
        var overridden = _values.AnimationOverride; Guid? first = null; var firstRead = false;
        FalloutPlayerViewRead? view = null; FalloutProcessSceneQuery? common = null;
        var selection = FalloutPackageAnimationSelection.Override; Guid? context = overridden;
        if (overridden is null)
        {
            view = readView(); Check();
            if (view.Source != source.Tail.Scene.Identity || view.Stack != Stack || view.Process != frame.PlayerProcess ||
                view.Constructor != Constructor || view.Frame != frame || view.Load is not null)
                throw new InvalidDataException("Animation selection changed the actual current Scene view owner.");
            if (view.Value == 0)
            {
                if (_lateAnimationWriter is { } writer) throw new NotSupportedException(writer);
                first = _values.FirstPersonAnimation; firstRead = true;
            }
            selection = SelectAnimation(view.Value, overridden, first);
            context = first;
            if (selection == FalloutPackageAnimationSelection.Process)
            {
                common = readCommon(); Check();
                if (common.Process != Process || common.Actor != Actor || common.Epoch != Epoch || common.Stack != Stack ||
                    common.ExecutableSha256 != Source.Controls.Main.EngineSha256 ||
                    common.GameplayOwnership != frame.Common.GameplayOwnership || common.Level != frame.Common.Level)
                    throw new InvalidDataException("Animation selector changed its actual current Common process/lease.");
                context = common.AnimationContext;
            }
        }
        Check();
        return new(source.Identity, Stack, Process, Factory, Constructor, Epoch, _sequence,
            frame, overridden, first, firstRead, view, selection, common, context);
        void Check()
        {
            if (_busy) { ++_fault; throw new InvalidOperationException("Animation selection reentered its real reference fields."); }
            requireActual(frame);
            if (_retired || _fault != fault || _sequence != sequence || !Actor.EnginePlayer || frame.Common.Actor != Actor || frame.Common.Process != Process ||
                frame.Common.Epoch != Epoch || frame.PlayerConstructor != Constructor ||
                frame.ThreadId != Environment.CurrentManagedThreadId || _thread is { } thread && thread != frame.ThreadId ||
                source.Tail.Movement.Mover.Controller.Fields != Source)
                throw new InvalidOperationException("Animation selector lost its actual Actor/source/fields/thread.");
        }
    }
}
