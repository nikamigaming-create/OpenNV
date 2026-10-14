# Functional recovery trace

This is the traced product worklist. It is not a completed whole-repository
review or a gameplay acceptance report. Component checks do not close a row.

| Product phase | Actual code path | Current obstruction | Required result |
| --- | --- | --- | --- |
| Record input | `RuntimeCoordinator.NativeOwned.IndexNativeLiveStack` -> `FalloutPluginStack.Load` | Reader return previously had no retained input lifetime for the loading owner. | Same live reader/stack receipt, configured order and source identity. Added; focused and owned checks pass. |
| New Game | `RuntimeCoordinator.NewGame.BootstrapNativeNewGame` -> `PrepareStandalonePlayerOwners` -> compiled starting quest | Cleanup/loading admission is repaired. | The current reader receipt preserves the cleanup byte; actual High reset returns. Loader finalization remains separate. |
| Package suffix | `FalloutActorProcessCommonState.ExecuteActorPackageAssignment` -> `RuntimeNativePlayerPackage.AttachReturnedSourceCommand` | Opening assignment and null-animation attachment return in ordinary FO3. | Same mover and actual type29 membership return; presentation borrows source state without event/idle replay. |
| CELL transfer | `RuntimeCoordinator.TransferMainPlayerPending` -> `FalloutReferenceWorld.CommitSourcePlayerLinkedCell` | Birth transfer and ordinary load now return, retaining 1,610 inputs and ten genuine engine-created plane/portal bases. | Source-qualified provider/primitive full-reader checks and three owned cold component cycles pass. Ordinary later CELL crossings remain required. |
| Gameplay frame | `RuntimeCoordinator.ExecuteStandalonePlayerScene` -> `FalloutStandalonePlayerSceneState.ExecuteInitialVirtualPrefix` | Current controller context/post-action children return; the next nonnull idle-package getter had a blanket refusal. Following/tail/thread/clock lack a complete returned path. | Read the winning package from the same Common slot/registry. Complete every reached child, restore thread/brackets and admit repeated frames. Keep failed-prefix diagnostics independent of gameplay getters. |
| Movement | `RuntimeNativePlayer._PhysicsProcess` and queued Player moves | Native physical pose is observed, while command-time C# XYZ has no ordinary movement publication. Current model reset supports startup only. | One actual current movement/position producer, consumed by physics, gameplay, saved reference fields and presentation. Do not call the startup-only setter as a generic movement shortcut. |
| Save capture | `RuntimeNativeOpeningStageDriver.CaptureCurrentState` -> `FalloutNativeCampaignSave.Capture` -> runtime/common validators | Complete Main is unavailable; successful frames still need return-schema support. | Join real mover parents to retained assignment receipts and compare JSON child values. Independent reference readiness is checked; complete frame still blocks. |
| Pause/Save preparation | source interface/menu transition -> `RuntimeNativeManualSavePreparation` | Source menu lifecycle/held Player child is unfinished; previous pause omitted the Always Main child. | Actual Main admission pause/drain now retains the entered result and resumes after cleanup. Bind the actual menu transitions; prove save commit/cancel and UI lease ownership. |
| Cold Continue | campaign restore -> `PrepareStandalonePlayerOwners` -> reference/controller/Parent binding | World rejected every cold reference/controller; fresh startup would replay Base/XYZ/Script writes. | Reconstruct fresh resources with retained state/history, authenticate Parent against membership and skip startup effects. Integration builds; actual cold product run is unaccepted. |
| Normal Quit | source pause/menu control -> session retirement | Earlier runs quit normally, but current accepted playable save does not exist. Managed factory failure previously blocked retirement without a live resource. | Idle failed factory/Common cleanup is repaired; actual borrowers still gate resource release. Quit an accepted playable state, exit zero, then Continue that exact slot. |
| FNV / TTW | selected plugin graph -> shared native module domain -> bootstrap | Selected native constructors/imports/task dispatch and later mutations still refuse. | Every selected module stays loaded and its actual gameplay/state effects return before the recorded routes. Deferred until the first FO3 save/Continue milestone. |

First acceptance: ordinary Fallout 3 New Game, real gameplay input, complete
save, normal Quit and cold Continue with matching quest/position/inventory state.
Repeat three times and through a CELL transition. Then finish Vault-to-Megaton,
Goodsprings departure and the TTW Megaton quest, with the same persistence check.
Requested video must come from those actual rendered sessions.

Continue reviewing all callees on these paths. Unknown behavior retains its
specific failure; canonical/pixel parity remains a separate evidence lane.
