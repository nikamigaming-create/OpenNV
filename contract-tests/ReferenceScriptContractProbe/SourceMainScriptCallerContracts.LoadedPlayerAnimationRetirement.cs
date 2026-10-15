using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunLoadedPlayerAnimationRetirementContracts()
    {
        using var fixture = new PlayerReturnTailFixture();
        var source = AuthoredLoadedAnimation(fixture.State.Source);
        fixture.EnterLoad((invocation, guard) =>
        {
            IndependentBindings(); SharedBinding(); FailedSourceRelease(); CallbackReentry();

            void IndependentBindings()
            {
                var firstRelease = 0; var secondRelease = 0; var firstAttempts = 0; var secondAttempts = 0;
                using var firstBinding = new MemoryStream(); using var secondBinding = new MemoryStream();
                var failure = new IOException("authored-first-final-binding-retirement-failed");
                var firstNode = NewAnimationRetirementNode(invocation, guard, () => ++firstRelease, () =>
                {
                    if (++firstAttempts == 1) throw failure;
                    firstBinding.Dispose();
                });
                var secondNode = NewAnimationRetirementNode(invocation, guard, () => ++secondRelease,
                    () => { ++secondAttempts; secondBinding.Dispose(); });
                var first = BindAnimationRetirementContext(source.Tail, invocation, guard, firstNode);
                var second = BindAnimationRetirementContext(source.Tail, invocation, guard, secondNode, FalloutLoadedAnimationRole.Common);
                RequireAnimationRetirementFailure(() => FalloutPlayerLoadedAnimationState.RetireContexts([first, second]), failure);
                Require(firstRelease == 1 && firstAttempts == 1 && firstBinding.CanWrite && !first.Retired &&
                    first.Capture().RootFactory is null && first.RetainedModelFactory == firstNode.Node.Factory &&
                    firstNode.RetirementState == FalloutReferenceModelBindingRetirementState.BindingPending && firstNode.SaveBlocker is not null,
                    "Failed final binding retirement replayed source release or lost its pending binding.");
                Require(secondRelease == 1 && secondAttempts == 1 && !secondBinding.CanWrite && second.Retired &&
                    second.RetainedModelFactory is null && secondNode.RetirementState == FalloutReferenceModelBindingRetirementState.Retired,
                    "The facade's actual drain stopped before its independent second context retired.");
                RequireAnimationRetirementSnapshot(first, null, firstNode.Node.Factory, false, failure.Message);
                Reject(firstNode.RequireCurrent);
                Reject(() => first.ObserveUnownedWriter("authored-retirement-is-not-gameplay", () => guard(invocation)));
                var secondSequence = second.Capture().Sequence;
                FalloutPlayerLoadedAnimationState.RetireContexts([first, second]);
                Require(firstRelease == 1 && firstAttempts == 2 && !firstBinding.CanWrite && first.Retired &&
                    first.RetainedModelFactory is null && secondRelease == 1 && secondAttempts == 1 &&
                    second.Capture().Sequence == secondSequence,
                    "Retry did not retire only the remaining binding, or repeated a committed source decrement.");
                RequireAnimationRetirementSnapshot(first, null, null, true, failure.Message);
                var firstSequence = first.Capture().Sequence;
                FalloutPlayerLoadedAnimationState.RetireContexts([first, second]);
                Require(first.Capture().Sequence == firstSequence && second.Capture().Sequence == secondSequence &&
                    firstRelease == 1 && secondRelease == 1 && firstAttempts == 2 && secondAttempts == 1,
                    "Returned context retirement replayed release or native-binding effects.");
            }

            void SharedBinding()
            {
                var sourceReleases = 0; var attempts = 0; using var binding = new MemoryStream();
                var failure = new IOException("authored-shared-final-binding-retirement-failed");
                var node = NewAnimationRetirementNode(invocation, guard, () => ++sourceReleases, () =>
                {
                    if (++attempts == 1) throw failure;
                    binding.Dispose();
                });
                var first = BindAnimationRetirementContext(source.Tail, invocation, guard, node);
                var second = BindAnimationRetirementContext(source.Tail, invocation, guard, node, FalloutLoadedAnimationRole.Common);
                FalloutPlayerLoadedAnimationState.RetireContexts([first]);
                Require(first.Retired && first.RetainedModelFactory is null && !second.Retired &&
                    second.Capture().RootFactory == node.Node.Factory && binding.CanWrite && sourceReleases == 0 && attempts == 0 &&
                    node.RetirementState == FalloutReferenceModelBindingRetirementState.Retained,
                    "A shared context decrement retired a graph still retained by the other actual context.");
                var firstSequence = first.Capture().Sequence;
                RequireAnimationRetirementFailure(() => FalloutPlayerLoadedAnimationState.RetireContexts([first, second]), failure);
                Require(sourceReleases == 1 && attempts == 1 && !second.Retired && binding.CanWrite &&
                    node.RetirementState == FalloutReferenceModelBindingRetirementState.BindingPending &&
                    second.Capture().RootFactory is null && second.RetainedModelFactory == node.Node.Factory,
                    "The last shared source release lost its separate pending binding failure.");
                RequireAnimationRetirementSnapshot(second, null, node.Node.Factory, false, failure.Message);
                FalloutPlayerLoadedAnimationState.RetireContexts([first, second]);
                Require(sourceReleases == 1 && attempts == 2 && !binding.CanWrite && second.Retired &&
                    first.Capture().Sequence == firstSequence && node.RetirementState == FalloutReferenceModelBindingRetirementState.Retired,
                    "Shared binding retry repeated SourceRelease or revisited an already retired context.");
            }

            void FailedSourceRelease()
            {
                var releases = 0; var bindingAttempts = 0; var independentReleases = 0; var independentBindings = 0;
                using var binding = new MemoryStream(); using var independentBinding = new MemoryStream();
                var failure = new IOException("authored-source-release-retained-partial-prefix");
                var node = NewAnimationRetirementNode(invocation, guard, () => { ++releases; throw failure; },
                    () => { ++bindingAttempts; binding.Dispose(); });
                var context = BindAnimationRetirementContext(source.Tail, invocation, guard, node);
                var independentNode = NewAnimationRetirementNode(invocation, guard, () => ++independentReleases,
                    () => { ++independentBindings; independentBinding.Dispose(); });
                var independent = BindAnimationRetirementContext(source.Tail, invocation, guard, independentNode, FalloutLoadedAnimationRole.Common);
                RequireAnimationRetirementFailure(() => FalloutPlayerLoadedAnimationState.RetireContexts([context, independent]), failure);
                Require(releases == 1 && bindingAttempts == 0 && binding.CanWrite && !context.Retired &&
                    context.Capture().RootFactory == node.Node.Factory && context.Capture().RetiringBindingFactory is null &&
                    node.RetirementState == FalloutReferenceModelBindingRetirementState.SourceReleaseFailed && node.SaveBlocker is not null &&
                    independent.Retired && independentReleases == 1 && independentBindings == 1 && !independentBinding.CanWrite,
                    "A failed source callback discarded its held lease, attempted final teardown, or blocked independent cleanup.");
                RequireAnimationRetirementSnapshot(context, node.Node.Factory, null, false, failure.Message);
                Reject(node.RequireCurrent); Reject(node.RetireBinding);
                var independentSequence = independent.Capture().Sequence;
                RequireAnimationRetirementFailure(() => FalloutPlayerLoadedAnimationState.RetireContexts([context, independent]), failure);
                Require(releases == 1 && bindingAttempts == 0 && !context.Retired && binding.CanWrite &&
                    independent.Capture().Sequence == independentSequence,
                    "Retry replayed an unknown source-release prefix or hid its incomplete lifetime.");
                // The intentionally failed source callback remains held and
                // visible. Disposing this authored stream below is fixture
                // cleanup, not a returned model/source retirement.
            }

            void CallbackReentry()
            {
                var releases = 0; var retirements = 0; var illegalCommits = 0; var rejected = 0;
                using var binding = new MemoryStream(); FalloutReferenceModelDataNode? node = null;
                node = NewAnimationRetirementNode(invocation, guard, () =>
                {
                    ++releases; var entered = node ?? throw new InvalidOperationException("No entered authored node.");
                    Require(entered.RetirementState == FalloutReferenceModelBindingRetirementState.SourceReleaseEntered,
                        "An entered source callback exposed a safe zero-reference binding retry.");
                    RejectReentry(entered.RetireBinding);
                    RejectReentry(() => entered.Release(() => ++illegalCommits));
                    Require(entered.RetirementState == FalloutReferenceModelBindingRetirementState.SourceReleaseEntered,
                        "Rejected release reentry changed the active source callback state.");
                }, () =>
                {
                    ++retirements; var entered = node ?? throw new InvalidOperationException("No entered authored node.");
                    Require(entered.RetirementState == FalloutReferenceModelBindingRetirementState.BindingRetirementEntered,
                        "An entered binding callback exposed a safe final retry.");
                    RejectReentry(entered.RetireBinding);
                    RejectReentry(() => entered.Release(() => ++illegalCommits));
                    binding.Dispose();
                });
                var context = BindAnimationRetirementContext(source.Tail, invocation, guard, node);
                FalloutPlayerLoadedAnimationState.RetireContexts([context]);
                Require(releases == 1 && retirements == 1 && rejected == 4 && illegalCommits == 0 && context.Retired &&
                    !binding.CanWrite && node.RetirementState == FalloutReferenceModelBindingRetirementState.Retired,
                    "Callback reentry repeated source/native release or changed committed reference ownership.");
                void RejectReentry(Action reenter)
                {
                    try { reenter(); }
                    catch (InvalidOperationException) { ++rejected; return; }
                    throw new InvalidOperationException("Actual same-node lifecycle callback reentry was admitted.");
                }
            }
        });
        Console.WriteLine("OPENNV_LOADED_ANIMATION_RETIREMENT_CONTRACT_PASS independentDrain=true bindingRetryOnly=true sharedLease=true failedSourceSticky=true callbackReentryRefused=true json3=true nativeSDK=UNEXECUTED gameplay=UNEXECUTED");
    }

    private static FalloutReferenceModelDataNode NewAnimationRetirementNode(FalloutReferenceModelDataInvocation invocation,
        Action<FalloutReferenceModelDataInvocation> guard, Action sourceRelease, Action retireBinding)
    {
        var logical = PlayerOpacityContracts.CreateReferenceColdModel().ConstructLogicalScene();
        return new(new(invocation.ReferenceProcess, logical.Factory, 123U), logical,
            () => guard(invocation), sourceRelease, retireBinding, () => guard(invocation));
    }
    private static FalloutSourceAnimationContextState BindAnimationRetirementContext(FalloutPlayerReturnTailSource source,
        FalloutReferenceModelDataInvocation invocation, Action<FalloutReferenceModelDataInvocation> guard,
        FalloutReferenceModelDataNode node, FalloutLoadedAnimationRole role = FalloutLoadedAnimationRole.FirstPerson)
    {
        var context = new FalloutSourceAnimationContextState(source, invocation, guard, () => guard(invocation), role);
        context.EnterModelBinding(invocation, node, guard);
        return context;
    }
    private static void RequireAnimationRetirementFailure(Action retire, Exception expected)
    {
        try { retire(); }
        catch (AggregateException error)
        {
            Require(error.InnerExceptions.Count == 1 && ReferenceEquals(error.InnerExceptions[0], expected),
                "Independent retirement lost its original binding/source exception prefix.");
            return;
        }
        throw new InvalidOperationException("Expected the exact authored independent retirement failure.");
    }
    private static void RequireAnimationRetirementSnapshot(FalloutSourceAnimationContextState context,
        Guid? root, Guid? pendingBinding, bool retired, string failure)
    {
        var snapshot = context.Capture();
        for (var cycle = 0; cycle < 3; cycle++)
        {
            snapshot = JsonSerializer.Deserialize<FalloutSourceAnimationContextSnapshot>(JsonSerializer.Serialize(snapshot))!;
            Require(snapshot.RootFactory == root && snapshot.RetiringBindingFactory == pendingBinding &&
                snapshot.Retired == retired && snapshot.RetirementFailure == failure,
                "Diagnostic JSON lost held source, committed binding retry or sticky retirement failure.");
        }
    }
}
