import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { existsSync, mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { CAMPAIGNS, createOfflineState, createRuntimeArguments, mergeRuntimeState, validateLaunchRequest } from "../src/contract.mjs";
import { resolveRuntimeCampaign, validateRuntimeManifest } from "../src/runtime-manifest-contract.mjs";

const runtimeRoot = fileURLToPath(new URL("../../runtime/", import.meta.url));
const shippedManifest = JSON.parse(readFileSync(path.join(runtimeRoot, "runtime-manifest.json"), "utf8"));
const profiles = Object.fromEntries([
  ["fallout1Profile", "fallout1"], ["fallout2Profile", "fallout2"],
  ["fallout3Profile", "fallout3"], ["newVegasProfile", "newvegas"]
].map(([key, id]) => [key, {
  ready: true, validated: true, dataRoot: `D:\\Games\\${id}`, savePath: `D:\\Saves\\${id}\\player.json`
}]));

test("the shipped manifest retains every campaign and launches all four standalone routes", () => {
  validateRuntimeManifest(shippedManifest);
  const merged = mergeRuntimeState(createOfflineState(), shippedManifest, profiles);
  assert.deepEqual(merged.campaigns.map(({ id }) => id), CAMPAIGNS.map(({ id }) => id));
  for (const id of ["fallout1", "fallout2", "fallout3", "newvegas"]) {
    const campaign = merged.campaigns.find((entry) => entry.id === id);
    assert.equal(campaign.ready, true, `${id} must remain reachable from the launcher`);
    assert.ok(campaign.presentations.includes(campaign.defaultPresentation));
    const availability = resolveRuntimeCampaign(shippedManifest, campaign);
    assert.equal(availability.presentations[campaign.defaultPresentation].launchable, true);
  }
  assert.equal(merged.campaigns.find(({ id }) => id === "ttw").ready, false);
});

test("classic previews retain incomplete campaign status and block unsupported FPS/VR", () => {
  const merged = mergeRuntimeState(createOfflineState(), shippedManifest, profiles);
  for (const id of ["fallout1", "fallout2"]) {
    const campaign = merged.campaigns.find((entry) => entry.id === id);
    assert.equal(campaign.previewOnly, true);
    assert.deepEqual(campaign.presentations, ["hex-tactical"]);
    assert.equal(campaign.presentationStatus["first-person"].launchable, false);
    assert.equal(campaign.presentationStatus.openxr.launchable, false);
    assert.match(campaign.readiness, /remain incomplete/);
  }
});

test("an unavailable installation affects only its own game and retains the catalog", () => {
  const merged = mergeRuntimeState(createOfflineState(), shippedManifest, {
    ...profiles, fallout1Profile: { ready: false, message: "Installation moved" }
  });
  assert.equal(merged.campaigns.find(({ id }) => id === "fallout1").ready, false);
  assert.equal(merged.campaigns.find(({ id }) => id === "fallout1").readiness, "Installation moved");
  for (const id of ["fallout2", "fallout3", "newvegas"])
    assert.equal(merged.campaigns.find((entry) => entry.id === id).ready, true);
  assert.equal(merged.campaigns.length, CAMPAIGNS.length);
});

test("a removed runtime route remains visible and cannot inherit another game's readiness", () => {
  const manifest = structuredClone(shippedManifest);
  manifest.campaigns = manifest.campaigns.filter(({ id }) => id !== "Fallout1");
  const merged = mergeRuntimeState(createOfflineState(), manifest, profiles);
  const missing = merged.campaigns.find(({ id }) => id === "fallout1");
  assert.equal(missing.ready, false);
  assert.match(missing.readiness, /missing.*registration is retained/);
  assert.equal(merged.campaigns.find(({ id }) => id === "fallout2").ready, true);
});

test("manifest drift and duplicate campaign identities are rejected", () => {
  assert.throws(() => validateRuntimeManifest({ ...shippedManifest, schema: "unknown" }), /unsupported manifest/);
  const duplicate = structuredClone(shippedManifest);
  duplicate.campaigns.push({ ...duplicate.campaigns[0], id: "FALLOUT1" });
  assert.throws(() => validateRuntimeManifest(duplicate), /duplicated/);
  const noRoutes = structuredClone(shippedManifest);
  delete noRoutes.campaigns[0].presentations;
  assert.throws(() => validateRuntimeManifest(noRoutes), /presentation routes/);
});

test("switching campaigns and requested views retains each game's source and save identity", () => {
  for (const [id, key, expectedCampaign] of [
    ["fallout1", "fallout1Profile", "fallout-1"],
    ["fallout2", "fallout2Profile", "fallout-2"],
    ["fallout3", "fallout3Profile", "fallout-3"],
    ["newvegas", "newVegasProfile", "fallout-new-vegas"],
    ["fallout1", "fallout1Profile", "fallout-1"]
  ]) {
    const request = validateLaunchRequest({ campaign: id });
    const args = createRuntimeArguments(request, profiles);
    assert.equal(args[args.indexOf("--campaign") + 1], expectedCampaign);
    assert.equal(args[args.indexOf("--data-root") + 1], profiles[key].dataRoot);
    assert.equal(args[args.indexOf("--save-path") + 1], profiles[key].savePath);
    assert.equal(args.filter((value) => value === "--save-path").length, 1);
  }
  const hex = createRuntimeArguments(validateLaunchRequest({ campaign: "fallout1", presentation: "hex-tactical" }), profiles);
  const fps = createRuntimeArguments(validateLaunchRequest({ campaign: "fallout1", presentation: "first-person" }), profiles);
  assert.equal(hex[hex.indexOf("--save-path") + 1], fps[fps.indexOf("--save-path") + 1]);
  // Request construction preserves identity; installed availability still blocks FPS.
  assert.equal(resolveRuntimeCampaign(shippedManifest, CAMPAIGNS[0]).presentations["first-person"].launchable, false);
});

// Opt-in owned-data checks exercise the exact arguments emitted by the launcher.
test("classic appearance donors do not replace campaign sources or saves", () => {
  for (const [id, key] of [["fallout1", "fallout1Profile"], ["fallout2", "fallout2Profile"]]) {
    const request = validateLaunchRequest({ campaign: id });
    const args = createRuntimeArguments(request, profiles);
    assert.equal(args[args.indexOf("--appearance-data-root") + 1], profiles.newVegasProfile.dataRoot);
    assert.equal(args[args.indexOf("--data-root") + 1], profiles[key].dataRoot);
    assert.equal(args[args.indexOf("--save-path") + 1], profiles[key].savePath);
    const withoutDonor = createRuntimeArguments(request, { [key]: profiles[key] });
    assert.equal(withoutDonor.includes("--appearance-data-root"), false);
    assert.equal(withoutDonor[withoutDonor.indexOf("--save-path") + 1], profiles[key].savePath);
  }
});

// They prove startup only and must not close HUD, input, combat or campaign acceptance.
for (const [id, profileKey, environmentKey, marker] of [
  ["fallout1", "fallout1Profile", "OPENNV_TEST_FO1_ROOT", "OPENNV_FO1_NATIVE_INSTALL_READY"],
  ["fallout2", "fallout2Profile", "OPENNV_TEST_FO2_ROOT", "OPENNV_FO2_NATIVE_INSTALL_READY"]
]) {
  const godot = process.env.OPENNV_TEST_GODOT;
  const installRoot = process.env[environmentKey];
  test(`${id} starts from ordinary launcher arguments against selected owned data`, {
    skip: !godot || !installRoot
  }, () => {
    const temporary = mkdtempSync(path.join(tmpdir(), "opennv-classic-launch-"));
    try {
      const savePath = path.join(temporary, `${id}.json`);
      const args = createRuntimeArguments(validateLaunchRequest({ campaign: id }), {
        [profileKey]: { ready: true, dataRoot: installRoot, savePath }
      });
      const result = spawnSync(godot, ["--headless", "--path", runtimeRoot, ...args,
        "--settings-path", path.join(temporary, "settings.json")], {
        encoding: "utf8", timeout: 45000, windowsHide: true
      });
      const output = `${result.stdout || ""}\n${result.stderr || ""}`;
      assert.ifError(result.error);
      assert.equal(result.status, 0, output);
      assert.ok(output.includes(marker), output);
      assert.doesNotMatch(output, /(?:^|\n)ERROR:/);
      assert.equal(existsSync(savePath), false, "a scene preview must not manufacture a gameplay save");
    } finally {
      const relative = path.relative(tmpdir(), temporary);
      if (!relative || relative.startsWith("..") || path.isAbsolute(relative))
        throw new Error("Temporary launch directory escaped its cleanup root.");
      rmSync(temporary, { recursive: true, force: true });
    }
  });
}
