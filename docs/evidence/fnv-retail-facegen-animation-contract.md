# Fallout: New Vegas FaceGen animation contract

## Scope and provenance

This is a behavior-only clean-room contract for an asset-free OpenNV runtime. The
reference executable was the legally owned Fallout: New Vegas 1.4.0.525 binary
with SHA-256
`518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57`.
No retail code or disassembly is copied into OpenNV.

The first Doc Mitchell line used for the live join has owned LIP SHA-256
`1ca83eae25fa3781a9a14e079ebb1a4939a0187a86f3407b913c04c846e0d181`:
127 frames, start frame -4, and preserved metadata word `0x3b74f6f1`.
`meshes\characters\head\headhuman.tri` has owned SHA-256
`6262171ec744cf58ee7cf4e1fbe18482a8d589b948e6c968ff6caaa8712c227c`:
1,211 base vertices, 38 differential morphs, and 8 static morphs.

The contract was cross-checked against the owned BSA corpus and independently
observed retail behavior. Retail derives a sibling `.lip` path from the selected
voice path. OpenNV therefore resolves voice and LIP members as one provenance-
bound pair rather than guessing from actor identity or audio amplitude.

## LIP contract

Fallout 3/New Vegas LIP payloads use a little-endian three-word file header. The
version-one payload may be zero-run encoded. Its decoded stream contains frame
count, signed start frame, one preserved metadata word, and 33 float tracks per
frame. Samples advance at 30 Hz, use linear interpolation, and are zero outside
the authored frame range. Runtime sampling uses the actual audio playback clock.

The ordered tracks are stored in the versioned runtime configuration. They cover
16 phonemes, blinks, brows, gaze, squint, and three head channels. The third
decoded header word is retained byte-for-byte; its behavioral meaning remains
unresolved and OpenNV does not invent timing semantics for it.

The retail LIP table names its long-E channel `Eee`, while the corresponding
owned TRI morph is consistently named `Ee`. That corpus-backed join is declared
once in configuration. The three head channels remain deliberately unbound until
their native controller contract is established.

Owned compressed files may omit exactly four zero bytes from the decoded tail.
Only that exact omission is accepted. Truncation, overflow, non-finite values,
unsupported flags, malformed zero runs, or any other size disagreement fails
closed.

Configured decoded-byte, frame-count, and value-magnitude maxima are explicit
OpenNV input-safety policy, not claims about hidden retail limits.

## TRI contract

FaceGen TRI files use the `FRTRI003` signature and a ten-word little-endian
header. They contain base and added vertices, triangle and quad topology,
optional labelled and UV sections, differential morphs encoded as scaled signed
16-bit deltas, and static morphs encoded as indexed replacement vertices.

The actor compiler joins a TRI only to the exact sibling NIF member in the owned
effective archive namespace. Vertex order and base geometry must agree. Both
differential and static targets are exported with their authored names; duplicate
names, malformed sections, invalid indices, non-finite values, and trailing data
fail closed. Morph normals are deterministically recomputed from authored
topology during compilation.

## Acceptance boundary

The runtime must verify the actor sidecar, imported target names, voice member,
and LIP member before playback. During speech, every exact-name target shared by
the LIP and actor is sampled from the audio clock and applied to every compatible
surface. Unmatched channels are preserved as evidence for later native
controller work and are not redirected through aliases or actor-specific tuning.

Head pitch/roll/yaw controller publication and INFO expression/mood composition
remain separate contracts. Their absence must not be disguised with generic jaw
motion, guessed transforms, or per-character constants.
