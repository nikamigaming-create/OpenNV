import test from "node:test";
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { mkdtemp } from "node:fs/promises";
import {
  readTtwFo3OpeningContract,
  ttwFo3OpeningCacheCompatibilityId,
  validateTtwProfileSourceLayout
} from "../src/ttw-opening-contract.mjs";

function hash(filePath) {
  return createHash("sha256").update(readFileSync(filePath)).digest("hex");
}

test("the TTW profile source layout admits a strict flattened installer stack", () => {
  const plugins = [
    "FalloutNV.esm",
    "Fallout3.esm",
    "TaleOfTwoWastelands.esm",
    "YUPTTW.esm"
  ].map((file, loadOrderIndex) => ({ file, loadOrderIndex, sourceRootIndex: 0 }));
  const profile = {
    sourceRoots: ["D:\\TTW\\Installed"],
    plugins,
    loadOrderSource: {
      derivation: {
        mode: "flattened-installer-output-plugin-mtime",
        allPluginsActive: true,
        strictlyIncreasingPluginModificationTimes: true,
        flattenedSourceRootIndex: 0,
        plugins: plugins.map((row, index) => ({
          file: row.file,
          lastWriteTimeNs: 1000 + index
        }))
      }
    }
  };

  assert.deepEqual(validateTtwProfileSourceLayout(profile), {
    mode: "flattened-installer-output-plugin-mtime",
    sourceRootIndex: 0
  });
  assert.throws(
    () => validateTtwProfileSourceLayout({ ...profile, loadOrderSource: {} }),
    /upper source layer/i
  );
  profile.loadOrderSource.derivation.plugins[2].lastWriteTimeNs = 1000;
  assert.throws(() => validateTtwProfileSourceLayout(profile), /evidence changed/i);
});

test("the TTW FO3 launch boundary binds its source namespace, cache, and save identity", async (context) => {
  const root = await mkdtemp(path.join(tmpdir(), "opennv-ttw-launch-"));
  context.after(() => rmSync(root, { recursive: true, force: true }));
  const lower = path.join(root, "new-vegas-data");
  const upper = path.join(root, "ttw-data");
  const video = path.join(upper, "Video");
  mkdirSync(lower, { recursive: true });
  mkdirSync(video, { recursive: true });
  const intro = path.join(video, "Fallout INTRO Vsk.bik");
  const later = path.join(video, "1 year later.bik");
  writeFileSync(intro, "intro");
  writeFileSync(later, "later");

  const pluginStackId = "a".repeat(64);
  const saveCompatibilityId = `ttw:${pluginStackId}`;
  const plugins = [{
    file: "FalloutNV.esm",
    loadOrderIndex: 0,
    sourceRootIndex: 0,
    bytes: 1,
    sha256: "b".repeat(64),
    masters: []
  }];
  const baseProfile = {
    sourceRoots: [lower, upper],
    plugins,
    pluginStackId,
    saveCompatibilityId
  };
  const baseManifestPath = path.join(root, "ttw-profile.json");
  writeFileSync(baseManifestPath, JSON.stringify(baseProfile));
  const baseHash = hash(baseManifestPath);

  const namespacePath = path.join(root, "ttw-effective-source.json");
  writeFileSync(namespacePath, JSON.stringify({
    schema: "opennv-ttw-effective-source-namespace/v1",
    status: "validated-neutral-effective-source-namespace",
    resolutionPolicy: "top-level-case-insensitive-last-data-root-wins",
    sourceProfile: {
      file: baseManifestPath,
      sha256: baseHash,
      pluginStackId,
      saveCompatibilityId
    },
    sourceRoots: baseProfile.sourceRoots,
    plugins,
    runtimeCompatibility: { ready: false }
  }));

  const opening = {
    schema: "opennv-ttw-fo3-opening-profile/v1",
    status: "transported-bounded-ttw-fo3-opening-command-contract",
    campaign: "Fallout3",
    edition: "TTW",
    saveCompatibilityId,
    sourceProfile: {
      file: baseManifestPath,
      sha256: baseHash,
      pluginStackId,
      saveCompatibilityId
    },
    sourceNamespace: {
      file: namespacePath,
      sha256: hash(namespacePath),
      schema: "opennv-ttw-effective-source-namespace/v1",
      status: "validated-neutral-effective-source-namespace"
    },
    recipe: { file: "bounded.json", sha256: "c".repeat(64) },
    forms: {},
    operands: {},
    stages: {},
    movies: {
      intro: {
        logicalPath: "Video/Fallout INTRO Vsk.bik",
        winner: { sourceRootIndex: 1, bytes: 5, sha256: hash(intro) }
      },
      cg01Stage5: {
        logicalPath: "Video/1 year later.bik",
        winner: { sourceRootIndex: 1, bytes: 5, sha256: hash(later) }
      }
    },
    cacheBoundary: {
      kind: "dedicated-ttw-opening-profile",
      standaloneFallout3ProfileAccepted: false,
      standaloneFallout3CacheReused: false,
      standaloneNewVegasProfileAccepted: false,
      standaloneNewVegasCacheReused: false
    },
    runtimeCompatibility: { ready: false, reason: "Runtime interpreter pending." },
    unsupportedSemantics: [
      "ttw-save-runtime-and-world-transition",
      "xnvse-and-jam-native-plugin-execution"
    ]
  };
  opening.cacheBoundary.compatibilityId = ttwFo3OpeningCacheCompatibilityId(opening);
  const openingManifestPath = path.join(root, "ttw-fo3-opening-profile.json");
  writeFileSync(openingManifestPath, JSON.stringify(opening));

  const result = readTtwFo3OpeningContract({ baseManifestPath, baseProfile, openingManifestPath });
  assert.equal(result.validated, true);
  assert.equal(result.runtimeReady, false);
  assert.equal(result.cacheCompatibilityId, opening.cacheBoundary.compatibilityId);
  assert.equal(result.saveCompatibilityId, saveCompatibilityId);

  opening.cacheBoundary.standaloneFallout3CacheReused = true;
  writeFileSync(openingManifestPath, JSON.stringify(opening));
  const contaminated = readTtwFo3OpeningContract({ baseManifestPath, baseProfile, openingManifestPath });
  assert.equal(contaminated.validated, false);
  assert.match(contaminated.message, /cache boundary changed/i);
});
