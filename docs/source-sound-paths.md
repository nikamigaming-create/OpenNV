# Source sound paths

TTW's winning childhood quest changes PHYBabyRattle from its tumbleweed directory
to the baby-rattle directory, then restores it near the quest's end. These are
source commands, independent of the caller's location or quest identity.

The primary [JIP command declaration and handlers](https://github.com/jazzisparis/JIP-LN-NVSE/blob/main/functions_jip/jip_fn_sound.h)
establish a global SOUN/string setter and a global SOUN getter returning the
current raw path. The setter changes the loaded form's path without stopping
playing instances or returning a changed-instance count. OpenNV implements this
behavior independently; plugin code is not included or executed.

FalloutSoundPaths owns current paths and per-form revisions in C#. It resolves
canonical winning SOUN identity, retains the source path/hash and preserves raw
case and trailing separators for the getter. An unchanged assignment retains the
revision. Empty paths remain readable; playback rejects an empty resource path.
Strict owned text encoding rejects unrepresentable input before mutation. Unsafe
resource paths remain visible failures at resolution, preserving the setter's
executed prefix. Getter and setter use typed script arguments/results through
reference, result, stage, startup and fallback programs; the setter returns zero.

Stack-aware sound readers use this owner. Script and animation/NIF/response caches
refresh on the corresponding form revision. Projector/menu and ingestible sound
readers use the same paths. Prepared requests, queued voices and active streams
keep their original source and media identity. Immutable decoded streams may be
reused by media path; source ESM/BSA/loose files remain read-only.

The owner lives with the selected stack, outside campaign snapshots. A fresh
stack reads winning FNAM again. Retail cold continuity, long native argument
buffer behavior and matched command/voice timing remain unmeasured. This bounded
implementation does not establish complete sound-command or save parity.

Synthetic checks cover winning paths, typed string locals, setter result, invalid
arguments, unchanged revisions, retained prepared voices, refreshed cached script
playback, raw/empty paths, unsafe resource failures, representable text, read-only
source bytes and fresh graph scope. The owned native fixture executes both TTW
commands, primes script and animation caches, retains queued old media, plays the
changed owned WAV and restores the original path. It uses an actual source Dad
body for spatial playback, observes nonzero mixer samples and verifies retirement
without recording frames. Environmental reverb and listener submersion remain
explicit audio gaps.

Fresh ordinary flat New/Capital input completes seventeen speeches, accepts the
owned trait screen, stops three birth loops and clears the player camera package.
The source disables Dad, stops CG00 and sets the youth policy. SetSoundSourceFile
changes the winning rattle path; the quest enters CG01 stages 0 and 5. Its next
The subsequent [toddler/scale owner](player-toddler-animation.md) now executes
the source movie and actual playroom transfer; ordinary input reaches CG01 stage
10. Shared scripted actor-value and random-dialogue selection let encouragement
continue, and the source autosave writes after its active continuation settles.
Ordinary Quit drains source readers. Publication overruns, playpen/trigger
progression and existing reference gaps remain visible. The gurney departure,
campaign and retail/XR parity remain unverified.
