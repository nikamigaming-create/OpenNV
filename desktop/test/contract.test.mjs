import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import {
  CAMPAIGNS,
  createOfflineState,
  createRuntimeArguments,
  mergeRuntimeState,
  validateLaunchRequest
} from "../src/contract.mjs";

test("the launcher has four top-level games while TTW remains an edition", () => {
  const state = createOfflineState({ platform: "linux" });
  assert.deepEqual(state.campaigns.map((campaign) => campaign.id), ["fallout1", "fallout2", "newvegas", "fallout3", "ttw"]);
  assert.deepEqual(
    state.campaigns.filter((campaign) => !campaign.ttw).map((campaign) => campaign.id),
    ["fallout1", "fallout2", "newvegas", "fallout3"]
  );
  assert.deepEqual(
    state.campaigns.find((campaign) => campaign.id === "fallout1").presentations,
    ["hex-tactical", "first-person"]
  );
  assert.deepEqual(
    state.campaigns.find((campaign) => campaign.id === "fallout2").pendingPresentations,
    ["hex-tactical", "first-person", "openxr"]
  );
  assert.deepEqual(state.campaigns.find((campaign) => campaign.id === "fallout2").presentations, []);
  assert.equal(state.campaigns.find((campaign) => campaign.id === "ttw").ttw, true);
  assert.match(state.campaignRule, /before creating a character/i);
});

test("the compact renderer starts with one readable four-card row and two columns when narrow", () => {
  const html = readFileSync(new URL("../src/renderer/index.html", import.meta.url), "utf8");
  const styles = readFileSync(new URL("../src/renderer/styles.css", import.meta.url), "utf8");
  assert.equal((html.match(/class="campaign-card /gu) || []).length, 4);
  for (const title of ["Fallout 1", "Fallout 2", "New Vegas", "Fallout 3"]) {
    assert.match(html, new RegExp(`card-title">${title.replace(" ", "\\s")}`));
  }
  assert.doesNotMatch(html, /card-title">TTW/);
  assert.match(styles, /grid-template-columns:\s*repeat\(4,/u);
  assert.match(styles, /@media \(max-width: 760px\)[\s\S]*grid-template-columns:\s*repeat\(2,/u);
});

test("Fallout 2 stays disabled with a registered owned profile and no runtime variant", () => {
  const base = createOfflineState({ platform: "win32" });
  const profile = {
    ready: false,
    runtimeReady: false,
    validated: true,
    message: "Fallout 2 owned DAT2 install registered; runtime pending."
  };
  const merged = mergeRuntimeState(base, {
    runtime: { status: "ready", label: "Godot runtime ready", canLaunch: true },
    campaigns: []
  }, { fallout2Profile: profile });
  const fallout2 = merged.campaigns.find((campaign) => campaign.id === "fallout2");
  assert.equal(fallout2.ready, false);
  assert.match(fallout2.readiness, /registered/i);
  assert.throws(
    () => validateLaunchRequest({ campaign: "fallout2", presentation: "hex-tactical" }),
    /not available/i
  );
  assert.throws(
    () => createRuntimeArguments({ campaign: fallout2, presentation: "hex-tactical" }, {
      fallout2Profile: profile
    }),
    /no Hex, FPS, or VR runtime/i
  );
});

test("flat and OpenXR launches separate engine and game arguments", () => {
  const profile = { savePath: "D:\\profiles\\courier-v1.json" };
  const flat = validateLaunchRequest({ campaign: "newvegas" });
  assert.deepEqual(createRuntimeArguments(flat, { newVegasProfile: profile }), [
    "--xr-mode", "off", "--", "--reuse-cache", "--opening-menu", "--save-path", profile.savePath
  ]);
  const xr = validateLaunchRequest({ campaign: "newvegas", enableVr: true });
  assert.deepEqual(
    createRuntimeArguments(xr, { newVegasProfile: profile }),
    ["--xr-mode", "on", "--", "--reuse-cache", "--opening-menu", "--save-path", profile.savePath, "--vr"]
  );
});

test("Fallout 1 launches the registered local contracts into the selected presentation", () => {
  const profile = {
    ready: true,
    hexScene: "D:\\cache\\hex-scene.json",
    characterStart: "D:\\cache\\character-start.json",
    characterStartSha256: "a".repeat(64),
    savePath: "D:\\profiles\\vault-dweller-v1.json"
  };
  const request = validateLaunchRequest({ campaign: "fallout1", presentation: "hex-tactical" });
  assert.deepEqual(createRuntimeArguments(request, { fallout1Profile: profile }), [
    "--xr-mode", "off", "--",
    "--fo1-hex-scene", profile.hexScene,
    "--fo1-new-game",
    "--fo1-character-start", profile.characterStart,
    "--fo1-character-start-sha256", profile.characterStartSha256,
    "--fo1-start-presentation", "hex-tactical",
    "--save-path", profile.savePath
  ]);
  assert.throws(
    () => validateLaunchRequest({ campaign: "fallout1", enableVr: true }),
    /OpenXR is not available/
  );
});

test("JAM is modular only for the supported character paths", () => {
  assert.equal(validateLaunchRequest({ campaign: "newvegas", enableJam: true }).enableJam, true);
  assert.throws(() => validateLaunchRequest({ campaign: "fallout3", enableJam: true }), /New Vegas and TTW/);
  assert.equal(validateLaunchRequest({ campaign: "newvegas", enableVr: true }).enableVr, true);
});

test("TTW and JAM remain disabled until both registered profiles report portable runtime readiness", () => {
  const runtime = {
    runtime: { status: "ready", label: "Godot runtime ready", canLaunch: true },
    campaigns: [{
      id: "TTW",
      variants: { vanilla: { ready: true }, jam: { ready: true } }
    }]
  };
  const withoutProfiles = mergeRuntimeState(createOfflineState({ platform: "win32" }), runtime);
  assert.equal(withoutProfiles.campaigns.find((campaign) => campaign.id === "ttw").ready, false);
  assert.equal(withoutProfiles.campaigns.find((campaign) => campaign.id === "ttw").jamReady, false);

  const withProfiles = mergeRuntimeState(createOfflineState({ platform: "win32" }), runtime, {
    ttwProfile: { ready: true, message: "Registered" },
    jamProfile: { ready: true, message: "Registered" }
  });
  assert.equal(withProfiles.campaigns.find((campaign) => campaign.id === "ttw").ready, true);
  assert.equal(withProfiles.campaigns.find((campaign) => campaign.id === "ttw").jamReady, true);
});

test("TTW and JAM launch arguments pass only manifest identities to the portable runtime", () => {
  const ttwProfile = {
    ready: true,
    path: "D:\\profiles\\ttw-profile.json",
    savePath: "D:\\profiles\\ttw\\courier-v1.json"
  };
  const jamProfile = { ready: true, path: "D:\\profiles\\jam-profile.json" };
  const request = validateLaunchRequest({ campaign: "ttw", enableJam: true });
  assert.deepEqual(createRuntimeArguments(request, { ttwProfile, jamProfile }), [
    "--xr-mode", "off", "--",
    "--campaign", "TTW",
    "--ttw-profile", ttwProfile.path,
    "--save-path", ttwProfile.savePath,
    "--enable-jam", "--jam-profile", jamProfile.path
  ]);
  assert.throws(
    () => createRuntimeArguments(request, { ttwProfile }),
    /JAM profile/
  );
});

test("a Godot runtime manifest augments rather than replaces product campaign rules", () => {
  const base = createOfflineState({ platform: "win32" });
  const merged = mergeRuntimeState(base, {
    runtime: { status: "ready", label: "Godot runtime ready", canLaunch: true },
    campaigns: [{ id: "NewVegas", variants: { vanilla: { ready: true, unavailableDlc: [] } } }]
  }, {
    newVegasProfile: { ready: true, message: "Registered" }
  });
  assert.equal(merged.runtime.status, "ready");
  assert.equal(merged.runtime.canLaunch, true);
  assert.equal(merged.runtime.openXrLaunchable, false);
  assert.equal(merged.campaigns.find((campaign) => campaign.id === "newvegas").ready, true);
  assert.equal(merged.campaigns.find((campaign) => campaign.id === "newvegas").jamReady, false);
  assert.equal(merged.campaigns.find((campaign) => campaign.id === "ttw").ready, false);
  assert.equal(CAMPAIGNS.length, 5);
});

test("an experimental runtime connects without claiming campaigns are playable", () => {
  const merged = mergeRuntimeState(createOfflineState({ platform: "linux" }), {
    runtime: { status: "experimental", label: "Static geometry only", canLaunch: false },
    campaigns: []
  });
  assert.equal(merged.runtime.status, "experimental");
  assert.equal(merged.runtime.canLaunch, false);
  assert.match(merged.runtime.label, /Static geometry/);
});

test("Fallout 1 becomes launcher-ready only when runtime capability and local profile are both present", () => {
  const runtime = {
    runtime: { status: "ready", label: "Godot runtime ready", canLaunch: true },
    campaigns: [{ id: "Fallout1EtTu", variants: { vault13Concept: { ready: true } } }]
  };
  const withoutProfile = mergeRuntimeState(createOfflineState({ platform: "win32" }), runtime);
  assert.equal(withoutProfile.campaigns.find((campaign) => campaign.id === "fallout1").ready, false);
  const withProfile = mergeRuntimeState(createOfflineState({ platform: "win32" }), runtime, {
    fallout1Profile: { ready: true, message: "Registered" }
  });
  assert.equal(withProfile.campaigns.find((campaign) => campaign.id === "fallout1").ready, true);
});

test("the checked-in runtime keeps owned-data routes profile-gated", () => {
  const manifest = JSON.parse(readFileSync(new URL("../../runtime/runtime-manifest.json", import.meta.url), "utf8"));
  const merged = mergeRuntimeState(createOfflineState({ platform: "win32" }), manifest, {
    newVegasProfile: { ready: true, message: "Registered" }
  });
  assert.equal(merged.runtime.canLaunch, true);
  assert.equal(merged.runtime.openXrLaunchable, true);
  assert.equal(merged.runtime.openXrHardwareValidated, false);
  assert.equal(merged.campaigns.find((campaign) => campaign.id === "newvegas").ready, true);
  assert.equal(merged.campaigns.find((campaign) => campaign.id === "newvegas").jamReady, false);
  assert.equal(merged.campaigns.find((campaign) => campaign.id === "fallout1").ready, false);
  assert.equal(merged.campaigns.find((campaign) => campaign.id === "fallout2").ready, false);
  assert.equal(merged.campaigns.find((campaign) => campaign.id === "fallout3").ready, false);
  assert.equal(merged.campaigns.find((campaign) => campaign.id === "ttw").ready, false);
});
