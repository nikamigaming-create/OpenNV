using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorPackageCommandFields
{
    internal FalloutPackageCommandSavedPositionCall SavePosition(FalloutPackageCommandFieldInvocation invocation,
        IFalloutPackageReferenceFieldConsumers? consumers)
    {
        Execute(invocation, "saved-position-Extra-and-reference-change", scope =>
        {
            var rotation = _values.RotationZ;
            if (consumers is not null)
            {
                RequireReferenceConsumer(scope, consumers);
                var read = consumers.ReadRawRotationZ(invocation); RequireReferenceConsumer(scope, consumers);
                if (read is not null)
                {
                    if (read.Source != Source.Controls.Identity || read.Stack != Stack || read.Process == Guid.Empty ||
                        read.Factory == Guid.Empty || read.Constructor == Guid.Empty || read.Reference != Actor.Reference ||
                        read.Base != Actor.Base || read.Sequence < 0)
                        throw new InvalidDataException("Saved position lost its actual borrowed raw-reference rotation owner.");
                    rotation = read.ZBits;
                    scope.SavedPositionCall = scope.SavedPositionCall! with { RotationRead = read };
                }
                else RequireLane(FalloutPackageCommandFieldLane.RotationZ);
            }
            else RequireLane(FalloutPackageCommandFieldLane.RotationZ);
            scope.SavedPositionCall = scope.SavedPositionCall! with { RotationZ = rotation };
            var position = ReadPosition(scope);
            scope.SavedPositionCall = scope.SavedPositionCall with { Position = position };
            RequireLane(FalloutPackageCommandFieldLane.ParentCell);
            var cell = _values.ParentCell;
            scope.SavedPositionCall = scope.SavedPositionCall with { ParentCell = cell, ParentCellReturned = true };
            if (consumers is null) throw new NotSupportedException(WorldspaceBoundary);
            RequireReferenceConsumer(scope, consumers);
            scope.SavedPositionCall = scope.SavedPositionCall with { Consumer = consumers.Factory };
            var worldspace = consumers.ReadWorldspace(invocation, cell); RequireReferenceConsumer(scope, consumers); ValidateForm(worldspace);
            scope.SavedPositionCall = scope.SavedPositionCall with { Worldspace = worldspace, WorldspaceReturned = true };
            RequireLane(FalloutPackageCommandFieldLane.SavedPosition);
            if (_values.SavedPosition is null)
            {
                var identity = Guid.NewGuid();
                Store(scope, FalloutPackageCommandField.SavedConstruction, @object: identity);
                Store(scope, FalloutPackageCommandField.SavedSpace, form: worldspace ?? cell);
                Store(scope, FalloutPackageCommandField.SavedX, bits: position.X);
                Store(scope, FalloutPackageCommandField.SavedY, bits: position.Y);
                Store(scope, FalloutPackageCommandField.SavedZ, bits: position.Z);
                Store(scope, FalloutPackageCommandField.SavedRotationZ, bits: rotation);
                Store(scope, FalloutPackageCommandField.SavedPresence, bits: 1, @object: identity);
            }
            else
            {
                Store(scope, FalloutPackageCommandField.SavedSpace, form: worldspace ?? cell);
                Store(scope, FalloutPackageCommandField.SavedX, bits: position.X);
                Store(scope, FalloutPackageCommandField.SavedY, bits: position.Y);
                Store(scope, FalloutPackageCommandField.SavedZ, bits: position.Z);
            }
            scope.SavedPositionCall = scope.SavedPositionCall with { After = _values.SavedPosition, ChangeEntered = true };
            consumers.ReferenceChanged(invocation, FalloutPackageCommandFieldsSource.ReferenceChangedMask);
            RequireReferenceConsumer(scope, consumers); return true;
        }, scope => scope.SavedPositionCall = new(invocation, scope.Entered, scope.Entered, null, null, null,
            false, null, false, null, _values.SavedPosition, _values.SavedPosition, [], false, false, null));
        return LastSavedPositionCall!;
    }
    private void RequireReferenceConsumer(FieldScope scope, IFalloutPackageReferenceFieldConsumers consumers)
    {
        Guard(scope);
        if (consumers.Source.Identity != Source.Identity || consumers.Stack != Stack || consumers.Actor != Actor ||
            consumers.Process != Process || consumers.Epoch != Epoch || consumers.Factory == Guid.Empty)
            throw new InvalidDataException("Saved-position consumer changed its actual source Actor/world lifetime.");
        consumers.RequireCurrent(scope.Invocation); Guard(scope);
    }

    internal FalloutPackageCommandAnimationRead ReadAnimationContext(FalloutPackageCommandFieldInvocation invocation,
        Func<FalloutProcessSceneQuery> readActualCommon,
        Func<FalloutPackageCommandAnimationViewRead>? readActualView = null)
    {
        Execute(invocation, "selected-animation-context-getter", scope =>
        {
            ArgumentNullException.ThrowIfNull(readActualCommon); RequireLane(FalloutPackageCommandFieldLane.Animation);
            var selection = FalloutPackageAnimationSelection.Process;
            if (Actor.EnginePlayer)
            {
                if (_values.AnimationOverride is not null) selection = FalloutPackageAnimationSelection.Override;
                else
                {
                    var view = (readActualView ?? throw new NotSupportedException(AnimationViewBoundary))(); Guard(scope);
                    RequireAnimationView(view);
                    scope.AnimationRead = scope.AnimationRead! with { ViewRead = view };
                    selection = SelectAnimation(view.View, _values.AnimationOverride, _values.FirstPersonAnimation);
                }
            }
            scope.AnimationRead = scope.AnimationRead! with { Selection = selection };
            Guid? context;
            if (selection == FalloutPackageAnimationSelection.Process)
            {
                var common = readActualCommon(); Guard(scope);
                if (common.Process != Process || common.Actor != Actor || common.Epoch != Epoch || common.Stack != Stack ||
                    common.ExecutableSha256 != Source.Controls.Main.EngineSha256 || common.GameplayOwnership == Guid.Empty)
                    throw new InvalidDataException("Package animation getter changed the actual current Common process lease.");
                context = common.AnimationContext;
                scope.AnimationRead = scope.AnimationRead with { Common = common };
            }
            else context = selection == FalloutPackageAnimationSelection.Override ? _values.AnimationOverride : _values.FirstPersonAnimation;
            scope.AnimationRead = scope.AnimationRead with { Context = context };
            if (context is not null) throw new NotSupportedException(AnimationBoundary);
            return true;
        }, scope => scope.AnimationRead = new(invocation, scope.Entered, scope.Entered, null, null, null, false, null));
        return LastAnimationRead!;
    }
    internal static FalloutPackageAnimationSelection SelectAnimation(byte view, Guid? overrideContext, Guid? firstPersonContext) =>
        overrideContext is not null ? FalloutPackageAnimationSelection.Override :
        view == FalloutPackageCommandFieldsSource.FirstPersonView && firstPersonContext is not null ?
            FalloutPackageAnimationSelection.FirstPerson : FalloutPackageAnimationSelection.Process;
}
