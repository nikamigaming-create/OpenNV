import assert from "node:assert/strict";
import path from "node:path";
import test from "node:test";
import { registeredOwnedDataRoot } from "../src/owned-install-registration.mjs";

test("existing and current installation registrations select the same owned root", () => {
  const dataRoot = path.resolve("owned-registration-fixture/Data");
  for (const schema of ["opennv-launcher-owned-data-registration/v1", "opennv-live-install-registration/v1"])
    for (const campaign of ["NewVegas", "Fallout3"]) {
      const registration = { schema, campaign, dataRoot };
      assert.equal(registeredOwnedDataRoot(registration, campaign), dataRoot);
      assert.equal(registeredOwnedDataRoot(registration, campaign === "NewVegas" ? "Fallout3" : "NewVegas"), null);
    }
});

test("unknown registrations and relative or empty roots cannot redirect a campaign", () => {
  for (const registration of [null, {}, { schema: "unrecognized", campaign: "NewVegas", dataRoot: path.resolve("Data") },
    ...["", " ", "Data", null].map((dataRoot) => ({ schema: "opennv-live-install-registration/v1", campaign: "NewVegas", dataRoot }))])
    assert.equal(registeredOwnedDataRoot(registration, "NewVegas"), null);
});
