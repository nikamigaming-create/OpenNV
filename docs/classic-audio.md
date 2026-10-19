# Classic audio

ClassicAcmDecoder reads owned ACM bytes into signed 16-bit PCM in memory. It
retains synthesis history and partial-block position per decoder, so callers
can request bounded buffers without a persistent conversion cache. It does not
use a subprocess, emulator, retail executable or third-party decoder at runtime.

The source contract includes least-significant-bit-first fields, the declared
sample/channel/rate layout, all admitted column selectors, grouped symbols,
signed amplitude wrapping, staged synthesis and history across blocks. Output
shifts and narrows with the source integer semantics. Invalid headers, reserved
selectors and out-of-table symbols fail visibly; a decoding failure remains
attached to that decoder instead of allowing a later read to skip the failure.
Sample counts are scalar values, not an inferred whole number of channel
frames: original speech resources can declare odd counts with two-channel
metadata. The decoder preserves both fields; playback interpretation is separate.

The original byte source supplies zeros at EOF while finishing declared samples.
Valid owned files use that extension, so it is implemented explicitly and exposed
as ZeroPaddingBits. This format behavior cannot by itself distinguish an authored
zero tail from damage to a compressed payload. Content provenance and source
identity remain separate checks; successful decoding is not an integrity claim.

Synthetic checks cover selectors, grouped column tails, two synthesis levels,
history across blocks, chunked reads, retained failures and zero extension.
The development command `classic-audio <installation> <fallout-1|fallout-2>`
audits owned resources, PCM hashes, exact declared counts, chunk independence,
unchanged source bytes and zero-extension counts. It writes no decoded assets.
Private native-oracle comparisons are additional evidence; their binaries,
addresses, input bytes and PCM are not repository inputs.

ClassicScriptSounds now resolves source script effect names against owned SFX,
prepares PCM before event commit and owns a four-voice admission limit. Disabled
or saturated requests do not load resources. The owned configuration supplies
enablement and master/effect volumes; the original integer/logarithmic volume
curve is retained. FO1 script effects supply no spatial object and play as mono
16-bit PCM at the source system rate of 22050 Hz, independently of ACM metadata.
Godot consumes memory through ClassicScriptSoundPlayback and acknowledges finite
completion or shutdown once. Its adapter belongs to the player session rather
than replaceable map controllers, preserving pending/active voices during a map
presentation handoff. Current door events use this owner; unverified FO2
voice semantics remain rejected. No persistent decoded assets are written.

The mixer audit covers real stream start, four-voice capacity, completion and
shutdown, with synthetic or selected owned input. Mixer stop retirement is
asynchronous; its audit waits for the audio thread before checking process exit.
It does not prove audible device output, retail mixing/resampling/timing, or
ordinary gameplay. Live voice save continuation also remains unverified.

General scenery/actor script ownership and floating text remain required for the
opening computer, together with persistent local changes and ordinary input.
Those effects may not be dropped to make an interaction appear successful. No
terminal, music, speech, campaign or complete audio acceptance is claimed here.
