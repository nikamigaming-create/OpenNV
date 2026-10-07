# Raw audio adapter

This first-party GDExtension implements Godot's public AudioStream and raw
AudioStreamPlayback virtual methods. It carries managed callback handles only;
owned media, interpolation, source clocks, loop regions and persistent state
belong to C#. No retail code or data is included.

`scripts/Build-NativeAudio.ps1` asks the pinned Godot 4.7.2 executable for its
public interface header and builds with the installed MSVC toolchain. Generated
headers, objects and DLLs stay ignored. The Windows export carries one native
DLL beside the executable, which is also the library used by managed callbacks.
End users do not need the compiler or header.

The required runtime gate runs `NativePcmPlaybackAudit` against actual native
playback, fractional cold suffixes, release tails, pending Start and both
explicit and scene-tree pause behavior. Sampler continuation does not certify
Godot output filters, hardware buffering or retail audio parity.
