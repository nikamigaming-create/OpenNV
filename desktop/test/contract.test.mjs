import test from "node:test";
import assert from "node:assert/strict";
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

test("a Windows runtime state augments rather than replaces product campaign rules", () => {
  const base = createOfflineState({ platform: "win32" });
  const merged = mergeRuntimeState(base, {
    campaigns: [{ id: "NewVegas", variants: { vanilla: { ready: true, unavailableDlc: [] } } }]
  });
  assert.equal(merged.runtime.status, "connected");
  assert.equal(merged.campaigns.find((campaign) => campaign.id === "newvegas").ready, true);
  assert.equal(merged.campaigns.find((campaign) => campaign.id === "ttw").ready, false);
  assert.equal(CAMPAIGNS.length, 3);
});
