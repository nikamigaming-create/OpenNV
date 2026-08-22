import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { CAMPAIGNS, createOfflineState, mergeRuntimeState, validateLaunchRequest } from "../src/contract.mjs";

test("the launcher has distinct standalone and TTW character paths", () => {
  const state = createOfflineState({ platform: "linux" });
  assert.deepEqual(state.campaigns.map((campaign) => campaign.id), ["newvegas", "fallout3", "ttw"]);
  assert.equal(state.campaigns.find((campaign) => campaign.id === "ttw").ttw, true);
  assert.match(state.campaignRule, /before creating a character/i);
});

test("JAM is modular only for the supported character paths", () => {
  assert.equal(validateLaunchRequest({ campaign: "newvegas", enableJam: true }).enableJam, true);
  assert.throws(() => validateLaunchRequest({ campaign: "fallout3", enableJam: true }), /New Vegas and TTW/);
});

test("a Godot runtime manifest augments rather than replaces product campaign rules", () => {
  const base = createOfflineState({ platform: "win32" });
  const merged = mergeRuntimeState(base, {
    runtime: { status: "ready", label: "Godot runtime ready", canLaunch: true },
    campaigns: [{ id: "NewVegas", variants: { vanilla: { ready: true, unavailableDlc: [] } } }]
  });
  assert.equal(merged.runtime.status, "ready");
  assert.equal(merged.runtime.canLaunch, true);
  assert.equal(merged.campaigns.find((campaign) => campaign.id === "newvegas").ready, true);
  assert.equal(merged.campaigns.find((campaign) => campaign.id === "newvegas").jamReady, false);
  assert.equal(merged.campaigns.find((campaign) => campaign.id === "ttw").ready, false);
  assert.equal(CAMPAIGNS.length, 3);
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

test("the checked-in runtime exposes only the playable New Vegas sandbox", () => {
  const manifest = JSON.parse(readFileSync(new URL("../../runtime/runtime-manifest.json", import.meta.url), "utf8"));
  const merged = mergeRuntimeState(createOfflineState({ platform: "win32" }), manifest);
  assert.equal(merged.runtime.canLaunch, true);
  assert.equal(merged.campaigns.find((campaign) => campaign.id === "newvegas").ready, true);
  assert.equal(merged.campaigns.find((campaign) => campaign.id === "newvegas").jamReady, false);
  assert.equal(merged.campaigns.find((campaign) => campaign.id === "fallout3").ready, false);
  assert.equal(merged.campaigns.find((campaign) => campaign.id === "ttw").ready, false);
});
