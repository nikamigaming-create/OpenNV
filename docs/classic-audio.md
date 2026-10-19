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

Gameplay integration remains required: general scenery/actor script ownership,
source sound-name resolution, voice admission, volume and spatial semantics,
presentation and retirement. The opening computer also requests floating text
and persistent local changes. None may be dropped to make an interaction appear
successful. No in-game audio, music, speech or campaign acceptance is claimed by
the component decoder.
