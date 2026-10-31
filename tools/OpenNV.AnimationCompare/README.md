# Animation comparison

This development tool inventories owned animation resources, records bounded
OpenNV poses, compares private retail/OpenNV captures, and runs explicit controlled
rig plans. A catalog entry or completed command is not a parity pass.

Build with `dotnet build tools/OpenNV.AnimationCompare -c Release`.
Invoke the resulting `OpenNV.AnimationCompare.dll` with:

```text
catalog <owned-installation> <fresh-output-directory> [--mod-stack <file>]
observe <absolute-command-directory> <actual-process-id> <plugin:reference> <seconds> <fresh-absolute-output> [actor|object]
compare <native.pose.jsonl> <opennv.pose.json> <fresh-output-directory> --plugins <comma-separated-load-order> [--stage post-draw|post-physics]
run <private-rig-configuration.json> <fresh-report.json>
```

Keep catalogs, native adapters, recordings and source bindings in ignored private
storage. They must not become packaged assets or runtime launch inputs.

## Evidence

Observations retain incomplete captures as failures. Comparisons require source
identity and animation-phase matches; they report missing or unqualified lanes
instead of filling them with assumed poses. Advancing native sequence clocks do
not prove that off-camera bones were updated. Bone comparisons do not certify
audio, rendered pixels, performance, or gameplay.

## Controlled runs

`ParityRigProtocol.cs` defines the plan and request records. The run configuration
contains `plan`, `openNv`, and `retail` sections. OpenNV bindings must identify real
source forms/references and the active process/channel. The external retail adapter
receives one correlated JSON request on standard input and returns a JSON receipt;
it must verify actual engine results and retain evidence. Console acceptance alone
does not establish that an animation ran.

The default `preserveInitialState: true` requires checkpoints and restoration.
Explicit `false` permits a disposable world and reports
`disposable-world-no-restore`; it does not skip quiescence or turn save failures into
successful saves. Cases use actual source entities and shared runtime operations.
Unsupported actions remain unbound. Ordinary campaign play remains a separate
acceptance requirement.

After a failed case, both engines must settle before another case runs. Completed
peer receipts are retained, and cases that were never reached are listed as
`not-run`. The process exits unsuccessfully when cases fail or remain unbound.
