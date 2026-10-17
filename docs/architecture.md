# OpenNV architecture

This document describes the product boundary and the source paths inspected on
October 17, 2026. It does not certify that every owner completes at runtime.

## Product boundary

OpenNV reads user-owned Fallout records and resources in place. C# owns formats,
record resolution, scripts, gameplay state, telemetry and persistence. Godot owns
presentation, input adaptation and OpenXR. Flat and VR must use the same
authoritative game/save state. Retail files and saves remain read-only inputs;
retail assets and derivatives are not distributed.

The implementation is independent of OpenMW and other engine source. Private
retail observation yields neutral behavioral contracts, not copied implementation.
Unknown required behavior remains an explicit failure until implemented.

## Source entry and content selection

RuntimeCoordinator.Launcher.cs selects the game, profile, mod stack and presentation,
then configures RuntimeLiveContentSource. Native extension selection accepts
installed or none; the current default is installed.
RuntimeLiveContentSource.NativePlugins.cs excludes native plugin discovery when
that selection is disabled. Base ESM/ESP selection and native DLL selection are
distinct and must both be explicit for a vanilla test.

RuntimeCoordinator.NativeOwned.cs constructs the world and campaign adapters.
RuntimeCoordinator.NativePlugins.cs binds original native-module compatibility,
input and inventory-reference services. Native module compatibility is an optional
content capability; its failures must not become an undeclared vanilla dependency.

## Gameplay delivery and the current integration problem

Campaigns/NewVegas/Opening/RuntimeNativeOpeningStageDriver.SourceMainScriptCaller.cs
requires the selected Main runtime at attachment and starts native frame delivery.
World/Cells/RuntimeNativeSourceMainScriptCaller.cs adapts the Godot process callback
to the shared C# Main owner; its Async partial retains entered calls and failures.

World/Cells/FalloutCampaignMainScriptConsumers.cs supplies the production callbacks.
The FNV paths for MainHold, TimedContexts and renderer/registered clocks currently
throw. RuntimeCoordinator.MainPlayer.cs also retains incomplete interface, scene
and CELL-loading callbacks. These are production gaps, not merely diagnostic
warnings.

World/Cells/FalloutReferenceWorld.FunctionalFailure.cs retains a shared gameplay
failure. RuntimeNativePlayer.cs and RuntimeNativePlayer.OpenXR.cs stop mutations
after that failure. This preserves a consistent stopped state but makes existing
presentation/input unusable when a mandatory upstream consumer is unfinished.
Recovery must repair the integration and real behavior; bypassing the fault would
leave inconsistent simulation and saves.

## Existing presentation and interaction owners

- RuntimeNativePlayer and RuntimeNativePlayerActor adapt movement, equipment,
  player models and their shared gameplay state.
- Presentation/OpenXR/NativeXrRig and related contact, pointer and support-grip
  owners provide tracked head/controller and first-person interaction.
- Presentation/Ui/NativeOwnedPipBoy presents the owned device/menu and routes
  interaction to the gameplay owners.
- Content/FalloutWeaponShot and Gameplay/State weapon owners resolve source
  ammunition, actions and damage; flat and XR input enter the same action path.
- Campaign, conversation, reference, companion and inventory owners remain the
  existing gameplay implementation. Their presence is not current end-to-end proof.

## Persistence and acceptance

Complete saves must retain authoritative state, source identity and required live
continuations across normal Quit and a new process. Presentation reconstruction
must bind fresh resources without manufacturing historical gameplay. A save-file
write alone is insufficient; ordinary cold Continue after travel/combat is required.

Use one candidate and one publication owner. Consolidate checked source onto main,
verify ordinary FNV gameplay in the simulator, then start the release branch.
The delivery order, concrete route, cleanup policy and checks are defined in
[implementation-plan.md](implementation-plan.md). Add architectural detail here
only when it describes an implemented, source-located owner or a clearly labelled
remaining gap.
