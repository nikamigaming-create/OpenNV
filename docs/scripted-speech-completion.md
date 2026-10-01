# Scripted speech completion

Say and SayTo select winning INFO records against current actor traits, quest
state and retained SayOnce history. An empty eligible set is an ordinary result
of selection. It does not create a voice, subtitle, INFO result program or
SayOnce entry, and it does not increment completed spoken-command telemetry.

Private read-only observation of the owned New Vegas 1.4.0.525 command handlers
and actor-process submission establishes that an empty request registers the
actor's completion event. Registration marks the requested topic and unfiltered
script blocks; it does not execute them inside the calling command. Repeated
marks coalesce. Private code, addresses and captures are not repository inputs.

FalloutSpeechCompletionEvents owns deferred empty selections in C#. It groups
typed topics per actor and delivers them after the calling script's suffix.
FalloutReferenceScripts admits that topic set as one SayToDone event and runs
matching filtered and unfiltered blocks once in authored order. Delivery uses
the same resident script binding as voiced completion. An unsupported operation
retains its executed prefix, registration and visible failure without retry.
Pending completion is active speech continuation and prevents save creation.
Pausing holds delivery; requests made by a delivered actor's handler wait for
the following dispatch. Exact native script-frame scheduling remains unverified.

RuntimeNativeSpeech selects before changing the bound speaker. A different
actor's empty selection therefore leaves the current voice, subtitle, lip data,
face and response animation owner intact. Missing resident actors, unsupported
conditions and completion bindings still fail visibly. An empty request that
interrupts its own actor's active voice and competing actual voices require
the remaining interruption/arbitration owner.

Synthetic contracts cover deferred suffix order, duplicate and multiple topic
marks, unfiltered block coalescing, pause, next dispatch, invalid typed admission,
non-replay, recursive rejection and retained failure prefixes. The isolated
owned TTW fixture uses the reached SayOnce set: Dad has no eligible INFO at
stage 70 and an actual response at stage 80. It checks native owned actor/voice
binding, another actor's empty request during that voice, retained subtitles,
separate empty/spoken counts and unchanged source/save input. This fixture
changes only its selection query, not a live campaign quest. It records no
frames and does not establish campaign, cold continuation or retail/XR parity.

Ordinary opening continuation is independently checked from fresh New Game.
Previously saved script errors are retained; installing this owner does not
replay a failed source prefix or silently rearm a saved actor script.
