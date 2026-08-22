# Runtime release policy

Automated runtime publishing is disabled during the Godot replacement. The CI
workflow validates the direct content exporter, provenance sidecar, Forward+
Godot load, C# build, and desktop launcher on every supported development
platform; it does not publish a playable archive.

A Godot runtime becomes eligible for a nightly only after all of these pass:

1. asset-free package layout and clean implementation dependency scan;
2. direct retail-data provenance and fail-closed unsupported semantics;
3. campaign preflight using legally owned data;
4. natural playable route with retained telemetry;
5. save/quit/cold-reload persistence;
6. locked retail differential for every parity claim;
7. platform frame-time, memory, streaming, and crash budgets;
8. launcher manifest and exported-binary verification.

Each release remains asset-free. Users supply their lawful game files, saves,
and separately authorized mods. Archived previews retain their historical
licenses and notices but are not evidence for the new Godot runtime.
