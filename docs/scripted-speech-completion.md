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
conditions and completion bindings still fail visibly.

Actual voices now have separate channels keyed by the resident speaker's typed
reference. Each channel owns its INFO/response cursor, audio player, source lip
data, face/response animation, result programs and completion. Voice selection
and SayOnce history remain shared. The owned actor-process submission retains
these fields on its actor, rather than a single scene-wide speaker slot. A
different actor's actual line can therefore overlap without rebinding the first
actor. Pausing holds all voices. Conversation skip targets only the conversation
channel. Processing snapshots channel generations before callbacks so a newly
started command cannot inherit an outgoing command's frame completion.

Same-actor replacement and empty interruption still fail visibly. A failed result
prefix stops audible playback, retains a failure and blocks saving; another
request does not replay that prefix. Channel identity, source binding, playback,
lip/face state, completion counts and every subtitle candidate remain visible in
telemetry. Competing HUD subtitle selection is unbound: the single-caption
adapter declines an ambiguous candidate set and resumes when one remains. It
does not establish native subtitle queue/priority/hold/fade behavior. Audio is
still non-spatial, and matched actor result/event and output timing are unverified.

Synthetic contracts cover deferred suffix order, duplicate and multiple topic
marks, unfiltered block coalescing, pause, next dispatch, invalid typed admission,
non-replay, recursive rejection and retained failure prefixes. The isolated
owned TTW fixture uses the reached SayOnce set: Dad has no eligible INFO at
stage 70 and an actual response at stage 80. It checks native owned actor/voice
binding, another actor's empty request during that voice, retained subtitles,
separate empty/spoken counts and unchanged source/save input. This fixture
changes only its selection query, not a live campaign quest. It records no
frames and does not establish campaign, cold continuation or retail/XR parity.

The concurrent variant adds Mom's actual owned body and source line. It checks
two simultaneous audio/lip bindings, unchanged Dad binding, independent source
results/completions, pause, isolated skip and a non-replayed failed result prefix.
The reached save and owned inputs remain unchanged. This is component evidence.

Ordinary opening continuation is independently checked from fresh New Game.
Previously saved script errors are retained; installing this owner does not
replay a failed source prefix or silently rearm a saved actor script.
Fresh ordinary TTW input now completes seventeen speech commands through the
Mom/Dad overlap and accepts the owned trait screen. Dad's next source begin
program executes the [shared StopSound owner](source-sound-stopping.md), and the
quest enters stages 90 and 100. Player-package removal then faults during a
pending change animation; the opening and gurney exit remain incomplete.
General quest progress telemetry reports entered source
stages separately from the bootstrap identity and scheduler invocation counts.
