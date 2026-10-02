# Source toddler animation and scale

The shared C# script session owns SetPCToddler and player SetScale, including
their saved state. Reference/result/stage programs and pre-world New Game use
the same typed effects. A reached command changes the real player presentation;
it does not substitute a flag for an unsupported transfer or animation.

## Contract

SetPCToddler accepts a signed 32-bit integer and treats nonzero as enabled.
The source first-person animation palette selects its Locomotion/Toddler
directory. Grounded movement uses the authored toddler walk/fast names; airborne
movement uses the directory's jump-loop names without an invented suffix.
Direction and speed retain the existing movement selector. Third-person selection
and the separate SetPCYoung appearance policy retain their owners.

Player SetScale accepts a finite source Float32, stores two decimal places and
clamps to the reference range 0.01 through 10. The shared session persists that
value. Native player transforms, body/camera and collision consume the uniform
source scale. Teleport and cold transform restoration preserve it instead of
resetting it to one. Saving extracts an orthonormal rotation separately from
scale. Legacy session snapshots default to normal animation and unit scale;
invalid saved scales reject before mutating retained state.

Looking-only flat controls still advance the real first-person toddler skeleton
and its owned Camera1st path. Clearing toddler mode restores the ordinary camera
even while movement remains disabled. Script camera packages and furniture keep
their existing precedence. Flat and OpenXR read the same C# policy; physical
headset acceptance and matched frame/pose behavior remain unverified.

## Following the source transition

Fresh ordinary New/Capital, gender/name, gene-projector and trait input reaches
the source One Year Later movie, player scale 0.4 and MoveTo to the toddler
playroom. Birth and playroom markers belong to the same CELL: cell identity alone
cannot distinguish their rooms. The reached source marker, player pose and actual
room pixels provide the separate transfer evidence. The private ninety-second
recording includes the movie and Dad/playpen at toddler scale, with audio.
It does not establish a correct gurney departure or Vault exit.

Scripted speech now receives the existing actor-value condition owner. Numeric
user-value slots resolve to the same saved reference values used by SetAV/ModAV;
unsupported actor formulas still reject. Reached encouragement uses Variable05.
Random INFO selection collects eligible source responses through the first
qualifying Random End or nonrandom boundary, then draws from the shared saved
script RNG. Both scripted speech and conversation selection use this owner.
Other flags and missing/invalid RNG owners still fail visibly. The
[GECK dialogue documentation](https://geckwiki.com/index.php?title=Dialogue)
describes the eligible random stack; the implementation's RNG sequence is not
a measured retail sequence. Linked follow-up groups remain outside this check.

A script autosave request remains pending while an unsupported active movie,
furniture, speech or menu continuation owns the player. Its blocker is published
in telemetry. When those owners settle and queued player moves finish, the
existing campaign save path captures and writes the state before clearing the
request. Explicit manual capture still rejects unsupported continuations.
Deferred requests do not establish retail autosave timing or complete checkpoint
restoration; actor clocks and active interaction saves remain open.

## Checks and limits

Synthetic checks cover typed command prefixes, malformed flags/arity,
Float32 precision/clamping, invalid atomic restore, legacy defaults and cold
policy. Random-selection checks cover SayOnce precedence, conditions, source
boundaries, missing/out-of-range RNG, unsupported flags and cold RNG sequence.

The owned native toddler fixture executes the selected quest's actual set,
scale and clear commands. It binds male/female first- and third-person bodies,
samples 32 movement cases across twelve owned paths, checks the actual flat
camera with looking-only controls, clears that camera and retains scale across
movement/restoration. The separate owned speech fixture selects two eligible
encouragement responses, plays their actual actor/voice/lip bindings, executes
begin/end results, observes typed completions and retains changed user values
cold. These fixtures record no frames and do not advance a campaign.

The gurney camera's authored repeat interval, package event handoff, animation
sound text keys and complete body targets remain unresolved. Toddler HP/AP/HUD
visibility, missing resident references, source actor/packages, full cold actor
continuation, every Vault quest, Megaton, TTW train travel, New Vegas/Benny and
JAM/MCM still require ordinary proof. No campaign, audio, renderer, physical XR
or matched retail parity completion is claimed.
