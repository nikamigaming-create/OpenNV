from __future__ import annotations

from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


def test_native_v13_exit_loads_exact_owned_destination_and_arrival() -> None:
    runtime = (
        ROOT
        / "runtime/src/Campaigns/Fallout1/Native/Fallout1NativeV13InteractionRuntime.cs"
    ).read_text(encoding="utf-8")

    assert "Vault13MapIndex = 6" in runtime
    assert 'Vault13MapLogicalPath = "maps\\\\vault13.map"' in runtime
    assert 'Vault13MapName = "VAULT13.MAP"' in runtime
    assert "_ownedSource.Read(Vault13MapLogicalPath)" in runtime
    assert "Fallout1NativeMapReader.Read(resource.Bytes)" in runtime
    assert "AuthoritativePlayerArrival = new Fallout1NativePlayerArrival" in runtime
    assert 'SetMeta("destination_bytes_written", 0)' in runtime


def test_native_v13_exit_keeps_saves_isolated_and_scripts_fail_closed() -> None:
    runtime = (
        ROOT
        / "runtime/src/Campaigns/Fallout1/Native/Fallout1NativeV13InteractionRuntime.cs"
    ).read_text(encoding="utf-8")
    audit = (
        ROOT / "runtime/tools/NativeFo1OwnedAudit/NativeFo1OwnedAudit.cs"
    ).read_text(encoding="utf-8")

    assert 'SaveCompatibilityId = $"fallout1:{source.ProfileId}"' in runtime
    assert "Fallout 1 native saves may not reside in the owned install." in runtime
    assert "internal void ExecuteDestinationScript" in runtime
    assert "throw new NotSupportedException" in runtime
    assert "interactions.CommitResolvedExitGrid(exitSourceTile)" in audit
    assert "destinationScripts={arrival.LiveMapScripts}:fail-closed" in audit
    assert "VerifyNoWrites(profilePath, profileBefore, source, installBefore)" in audit
