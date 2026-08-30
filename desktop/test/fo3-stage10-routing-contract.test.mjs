import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";

import {
  FO3_STAGE10_ROUTE_CONTRACT,
  preflightFo3Stage10Launch,
  probeFo3Stage10Launch
} from "../src/fo3-stage10-routing-contract.mjs";

const masterSha256 = "d9fb0a33af495ddb43992b96ea74f2741b123fefdb1fcdcea28096f7649b0d06";

function standaloneInput() {
  return {
    fallout3Profile: {
      ready: true,
      schema: "opennv-owned-game-profile/v1",
      campaign: "Fallout3",
      sourceEdition: "standalone-fo3",
      masterFile: "Fallout3.esm",
      masterSha256,
      path: "D:\\profiles\\fallout3\\vanilla\\fallout3-profile.json",
      savePath: "D:\\saves\\fallout3\\cg00-character-v1.json"
    },
    birthPresentation: {
      schema: "opennv-fo3-vault101-birth-presentation/v9",
      sourceEdition: "standalone-fo3",
      masterFile: "Fallout3.esm",
      masterSha256,
      path: "D:\\profiles\\fallout3\\vanilla\\fo3-vault101-birth-presentation.json"
    },
    retailStage10Contract: {
      schema: "opennv.fo3-retail-cg00-stage10-camera-contract/v1",
      classification:
        "private-exact-live-stage10-camera-and-participant-contract-not-pixel-parity",
      targetVersion: "1.7.0.4",
      targetExecutableSha256:
        "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e",
      synthetic: false,
      path: "D:\\evidence\\fo3-cg00-stage10-camera-contract.json"
    },
    reportPath: "D:\\proofs\\fo3-stage10-r1\\report.json",
    captureRoot: "D:\\proofs\\fo3-stage10-r1\\frames"
  };
}

test("standalone FO3 stage10 uses only its standalone identity envelope", () => {
  const input = standaloneInput();
  const result = preflightFo3Stage10Launch("fallout3", input);
  assert.equal(result.ready, true);
  assert.equal(result.sourceEdition, "standalone-fo3");
  assert.deepEqual(result.runtimeArguments, [
    "--xr-mode", "off", "--",
    "--fo3-profile", input.fallout3Profile.path,
    "--fo3-birth-presentation", input.birthPresentation.path,
    "--fo3-retail-cg00-stage10-contract", input.retailStage10Contract.path,
    "--fo3-appearance-proof", "stage10-presentation",
    "--save-path", input.fallout3Profile.savePath,
    "--report", input.reportPath,
    "--fo3-appearance-capture-root", input.captureRoot
  ]);
  assert.equal(result.runtimeArguments.some((value) => value.includes("ttw")), false);
});

test("standalone FO3 does not require TTW and rejects silent TTW mixing", () => {
  const withoutTtw = preflightFo3Stage10Launch("fallout3", standaloneInput());
  assert.equal(withoutTtw.ready, true);

  const mixed = standaloneInput();
  mixed.ttwOpeningProfile = {
    path: "D:\\profiles\\ttw-fo3-opening-profile.json"
  };
  const result = preflightFo3Stage10Launch("fallout3", mixed);
  assert.equal(result.ready, false);
  assert.match(result.blocker, /cannot consume TTW/u);
  assert.deepEqual(result.runtimeArguments, []);
});

test("synthetic camera contracts are not standalone launch authority", () => {
  const input = standaloneInput();
  input.retailStage10Contract = {
    ...input.retailStage10Contract,
    classification: "synthetic-parser-test-only-not-retail-evidence",
    synthetic: true
  };
  const result = preflightFo3Stage10Launch("fallout3", input);
  assert.equal(result.ready, false);
  assert.match(result.blocker, /real standalone Fallout3\.exe/u);
  assert.deepEqual(result.runtimeArguments, []);
});

test("TTW validates its isolated envelope but remains blocked from world stage10", () => {
  const result = preflightFo3Stage10Launch("ttw-fo3", {
    ttwOpeningProfile: {
      schema: "opennv-ttw-fo3-opening-profile/v1",
      validated: true,
      sourceEdition: "ttw-effective-stack",
      path: "D:\\profiles\\ttw-fo3-opening-profile.json",
      sourceNamespacePath: "D:\\profiles\\ttw-effective-source.json",
      sourceNamespaceSha256: "7".repeat(64)
    },
    ttwRetailStage10Contract: {
      schema: "opennv.ttw-fo3-retail-cg00-stage10-camera-contract/v1",
      classification:
        "private-exact-live-ttw-stage10-camera-and-participant-contract-not-pixel-parity",
      targetVersion: "1.4.0.525",
      targetExecutableSha256:
        "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57",
      synthetic: false,
      path: "D:\\evidence\\ttw-fo3-cg00-stage10-camera-contract.json"
    }
  });
  assert.equal(result.ready, false);
  assert.equal(result.sourceEdition, "ttw-effective-stack");
  assert.match(result.blocker, /no live Vault 101 stage-10 world adapter/u);
  assert.match(result.blocker, /standalone Fallout3\.exe camera contract cannot authorize/u);
  assert.deepEqual(result.runtimeArguments, []);
});

test("TTW stage10 stays disabled when no real TTW live contract was emitted", () => {
  const result = preflightFo3Stage10Launch("ttw-fo3", {
    ttwOpeningProfile: {
      schema: "opennv-ttw-fo3-opening-profile/v1",
      validated: true,
      sourceEdition: "ttw-effective-stack",
      path: "D:\\profiles\\ttw-fo3-opening-profile.json",
      sourceNamespacePath: "D:\\profiles\\ttw-effective-source.json",
      sourceNamespaceSha256: "7".repeat(64)
    }
  });
  assert.equal(result.ready, false);
  assert.match(result.blocker, /Supply a real FalloutNV\.exe TTW/u);
  assert.deepEqual(result.runtimeArguments, []);
});

test("TTW rejects standalone stage10 files instead of borrowing them", () => {
  const result = preflightFo3Stage10Launch("ttw-fo3", standaloneInput());
  assert.equal(result.ready, false);
  assert.match(result.blocker, /cannot consume standalone Fallout 3/u);
  assert.deepEqual(result.runtimeArguments, []);
});

test("the routing table names only the two canonical source editions", () => {
  assert.deepEqual(Object.keys(FO3_STAGE10_ROUTE_CONTRACT.routes), ["fallout3", "ttw-fo3"]);
  assert.equal(
    FO3_STAGE10_ROUTE_CONTRACT.routes.fallout3.sourceEdition,
    "standalone-fo3"
  );
  assert.equal(
    FO3_STAGE10_ROUTE_CONTRACT.routes["ttw-fo3"].sourceEdition,
    "ttw-effective-stack"
  );
});

test("the file probe joins the profile and presentation before rejecting synthetic authority", () => {
  const root = mkdtempSync(path.join(os.tmpdir(), "opennv-fo3-stage10-route-"));
  try {
    const dataRoot = path.join(root, "Fallout3", "Data");
    mkdirSync(dataRoot, { recursive: true });
    const profilePath = path.join(root, "fallout3-profile.json");
    writeFileSync(profilePath, JSON.stringify({
      schema: "opennv-owned-game-profile/v1",
      status: "registered-owned-profile",
      campaign: "Fallout3",
      capabilities: { runtimeBootReady: true },
      install: {
        dataRoot,
        master: { file: "Fallout3.esm", sha256: masterSha256 }
      }
    }));
    const birthSlicePath = path.join(root, "fo3-vault101-cg00-birth.json");
    writeFileSync(birthSlicePath, JSON.stringify({
      schema: "opennv-fo3-opening-slice/v1",
      source: { master: { file: "Fallout3.esm", sha256: masterSha256 } }
    }));
    const birthSliceSha256 = createHash("sha256")
      .update(readFileForHash(birthSlicePath))
      .digest("hex");
    const presentationPath = path.join(root, "fo3-vault101-birth-presentation.json");
    writeFileSync(presentationPath, JSON.stringify({
      schema: "opennv-fo3-vault101-birth-presentation/v9",
      source: { birthSlice: birthSlicePath, birthSliceSha256 }
    }));
    const retailFixture = path.resolve(
      "runtime/tests/fixtures/fo3-cg00-stage10-camera-contract.synthetic.json");
    const result = probeFo3Stage10Launch("fallout3", {
      fallout3ProfilePath: profilePath,
      birthPresentationPath: presentationPath,
      retailStage10ContractPath: retailFixture,
      savePath: path.join(root, "save.json"),
      reportPath: path.join(root, "report.json"),
      captureRoot: path.join(root, "frames")
    });
    assert.equal(result.ready, false);
    assert.match(result.blocker, /synthetic fixtures are never launch authority/u);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

function readFileForHash(filePath) {
  return readFileSync(filePath);
}
