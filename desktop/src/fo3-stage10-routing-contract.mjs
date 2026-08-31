import { createHash } from "node:crypto";
import { existsSync, readFileSync } from "node:fs";
import path from "node:path";

const SHA256_PATTERN = /^[0-9a-f]{64}$/u;
const FO3_EXECUTABLE_SHA256_PREFIX = "c3f97c2255fa041a851c17cf372d69aa";
const FO3_EXECUTABLE_SHA256_SUFFIX = "add8694e2dc4230ba556001bbfbd2f3e";
const FNV_EXECUTABLE_SHA256_PREFIX = "518c87f58a6c4d9826e9ef8fbb7f4213";
const FNV_EXECUTABLE_SHA256_SUFFIX = "882fa70822675610d45aea2464502a57";

export const FO3_STAGE10_ROUTE_CONTRACT = Object.freeze({
  schema: "opennv.fo3-stage10-launch-routing/v1",
  proofMode: "stage10-presentation",
  routes: Object.freeze({
    fallout3: Object.freeze({
      sourceEdition: "standalone-fo3",
      profileSchema: "opennv-owned-game-profile/v1",
      profileCampaign: "Fallout3",
      masterFile: "Fallout3.esm",
      presentationSchema: "opennv-fo3-vault101-birth-presentation/v9",
      stage10ContractSchema: "opennv.fo3-retail-cg00-stage10-camera-contract/v1",
      stage10ContractClassification:
        "private-exact-live-stage10-camera-and-participant-contract-not-pixel-parity",
      targetVersion: "1.7.0.4",
      targetExecutableSha256:
        FO3_EXECUTABLE_SHA256_PREFIX + FO3_EXECUTABLE_SHA256_SUFFIX
    }),
    "ttw-fo3": Object.freeze({
      sourceEdition: "ttw-effective-stack",
      profileSchema: "opennv-ttw-fo3-opening-profile/v1",
      stage10ContractSchema:
        "opennv.ttw-fo3-retail-cg00-stage10-camera-contract/v1",
      stage10ContractClassification:
        "private-exact-live-ttw-stage10-camera-and-participant-contract-not-pixel-parity",
      targetVersion: "1.4.0.525",
      targetExecutableSha256:
        FNV_EXECUTABLE_SHA256_PREFIX + FNV_EXECUTABLE_SHA256_SUFFIX,
      blocker:
        "Fallout 3 via TTW has no live Vault 101 stage-10 world adapter. " +
        "The standalone Fallout3.exe camera contract cannot authorize the FalloutNV.exe TTW stack."
    })
  })
});

function nonEmpty(value) {
  return typeof value === "string" && value.trim().length > 0;
}

function validSha256(value) {
  return typeof value === "string" && SHA256_PATTERN.test(value);
}

function fileSha256(filePath) {
  return createHash("sha256").update(readFileSync(filePath)).digest("hex");
}

function readJson(filePath, label) {
  if (!nonEmpty(filePath)) throw new Error(`${label} path is missing.`);
  const resolved = path.resolve(filePath);
  if (!existsSync(resolved)) throw new Error(`${label} is missing: ${resolved}`);
  return { path: resolved, value: JSON.parse(readFileSync(resolved, "utf8")) };
}

function blocked(routeId, sourceEdition, blocker) {
  return Object.freeze({
    schema: FO3_STAGE10_ROUTE_CONTRACT.schema,
    routeId,
    sourceEdition,
    ready: false,
    blocker,
    runtimeArguments: Object.freeze([])
  });
}

function mixedStandaloneInputs(input) {
  return input.ttwProfile != null || input.ttwOpeningProfile != null ||
    input.ttwRetailStage10Contract != null;
}

function mixedTtwInputs(input) {
  return input.fallout3Profile != null || input.birthPresentation != null ||
    input.retailStage10Contract != null;
}

export function preflightFo3Stage10Launch(routeId, input = {}) {
  const route = FO3_STAGE10_ROUTE_CONTRACT.routes[routeId];
  if (!route) {
    return blocked(
      String(routeId ?? ""),
      "unknown",
      "Choose standalone Fallout 3 or Fallout 3 via TTW for the stage-10 route.");
  }

  if (routeId === "ttw-fo3") {
    if (mixedTtwInputs(input)) {
      return blocked(
        routeId,
        route.sourceEdition,
        "The TTW stage-10 route cannot consume standalone Fallout 3 profile, presentation, or camera-contract files.");
    }
    const profile = input.ttwOpeningProfile;
    if (!profile || profile.schema !== route.profileSchema || profile.validated !== true ||
        profile.sourceEdition !== route.sourceEdition || !nonEmpty(profile.path) ||
        !nonEmpty(profile.sourceNamespacePath) || !validSha256(profile.sourceNamespaceSha256)) {
      return blocked(
        routeId,
        route.sourceEdition,
        "Register a validated, isolated TTW Fallout 3 opening profile before preflight.");
    }
    const retail = input.ttwRetailStage10Contract;
    if (!retail || retail.schema !== route.stage10ContractSchema ||
        retail.classification !== route.stage10ContractClassification ||
        retail.targetVersion !== route.targetVersion ||
        retail.targetExecutableSha256 !== route.targetExecutableSha256 ||
        retail.synthetic === true || !nonEmpty(retail.path)) {
      return blocked(
        routeId,
        route.sourceEdition,
        "Supply a real FalloutNV.exe TTW CG00 stage-10 contract bound to the selected effective-source stack; synthetic or standalone evidence is never authority.");
    }
    return blocked(routeId, route.sourceEdition, route.blocker);
  }

  if (mixedStandaloneInputs(input)) {
    return blocked(
      routeId,
      route.sourceEdition,
      "The standalone Fallout 3 stage-10 route cannot consume TTW profile or effective-stack files.");
  }

  const profile = input.fallout3Profile;
  if (!profile || profile.ready !== true || profile.schema !== route.profileSchema ||
      profile.campaign !== route.profileCampaign ||
      profile.sourceEdition !== route.sourceEdition ||
      profile.masterFile !== route.masterFile || !validSha256(profile.masterSha256) ||
      !nonEmpty(profile.path) || !nonEmpty(profile.savePath)) {
    return blocked(
      routeId,
      route.sourceEdition,
      "Register a validated standalone Fallout 3 owned-game profile before stage-10 preflight.");
  }

  const presentation = input.birthPresentation;
  if (!presentation || presentation.schema !== route.presentationSchema ||
      presentation.sourceEdition !== route.sourceEdition ||
      presentation.masterFile !== profile.masterFile ||
      presentation.masterSha256 !== profile.masterSha256 || !nonEmpty(presentation.path)) {
    return blocked(
      routeId,
      route.sourceEdition,
      "Prepare a Vault 101 birth presentation from the same standalone Fallout 3 master.");
  }

  const retail = input.retailStage10Contract;
  if (!retail || retail.schema !== route.stage10ContractSchema ||
      retail.classification !== route.stage10ContractClassification ||
      retail.targetVersion !== route.targetVersion ||
      retail.targetExecutableSha256 !== route.targetExecutableSha256 ||
      retail.synthetic === true || !nonEmpty(retail.path)) {
    return blocked(
      routeId,
      route.sourceEdition,
      "Supply a real standalone Fallout3.exe CG00 stage-10 camera/participant contract; synthetic fixtures are never launch authority.");
  }

  if (!nonEmpty(input.reportPath) || !nonEmpty(input.captureRoot)) {
    return blocked(
      routeId,
      route.sourceEdition,
      "Stage-10 preflight requires unique report and capture output paths.");
  }

  return Object.freeze({
    schema: FO3_STAGE10_ROUTE_CONTRACT.schema,
    routeId,
    sourceEdition: route.sourceEdition,
    ready: true,
    blocker: null,
    runtimeArguments: Object.freeze([
      "--xr-mode", "off", "--",
      "--fo3-profile", profile.path,
      "--fo3-birth-presentation", presentation.path,
      "--fo3-retail-cg00-stage10-contract", retail.path,
      "--fo3-appearance-proof", FO3_STAGE10_ROUTE_CONTRACT.proofMode,
      "--save-path", profile.savePath,
      "--report", input.reportPath,
      "--fo3-appearance-capture-root", input.captureRoot
    ])
  });
}

export function probeFo3Stage10Launch(routeId, files = {}) {
  const route = FO3_STAGE10_ROUTE_CONTRACT.routes[routeId];
  if (!route) return preflightFo3Stage10Launch(routeId);
  try {
    if (routeId === "ttw-fo3") {
      if (files.fallout3ProfilePath || files.birthPresentationPath ||
          files.retailStage10ContractPath) {
        return preflightFo3Stage10Launch(routeId, {
          fallout3Profile: files.fallout3ProfilePath ? {} : null,
          birthPresentation: files.birthPresentationPath ? {} : null,
          retailStage10Contract: files.retailStage10ContractPath ? {} : null
        });
      }
      const opening = readJson(files.ttwOpeningProfilePath, "TTW opening profile");
      if (opening.value?.schema !== route.profileSchema ||
          opening.value?.status !== "transported-bounded-ttw-fo3-opening-command-contract") {
        throw new Error("TTW Fallout 3 opening profile identity differs.");
      }
      const sourceProfileRow = opening.value?.sourceProfile;
      const sourceProfile = readJson(sourceProfileRow?.file, "TTW source profile");
      if (sourceProfile.value?.schema !== "opennv-ttw-profile/v1" ||
          sourceProfile.value?.status !== "validated-generated-plugin-profile" ||
          fileSha256(sourceProfile.path) !== sourceProfileRow?.sha256) {
        throw new Error("TTW source profile hash or identity differs.");
      }
      const expectedTtwRoot = path.resolve(files.ttwSourceRoot);
      if (!Array.isArray(sourceProfile.value?.sourceRoots) ||
          !sourceProfile.value.sourceRoots.some((root) =>
            path.resolve(root).toLowerCase() === expectedTtwRoot.toLowerCase())) {
        throw new Error("TTW opening profile does not bind the selected TTW source root.");
      }
      const namespaceRow = opening.value?.sourceNamespace;
      const sourceNamespace = readJson(namespaceRow?.file, "TTW effective-source namespace");
      const namespaceSha256 = fileSha256(sourceNamespace.path);
      if (sourceNamespace.value?.schema !== "opennv-ttw-effective-source-namespace/v1" ||
          sourceNamespace.value?.status !== "validated-neutral-effective-source-namespace" ||
          namespaceSha256 !== namespaceRow?.sha256) {
        throw new Error("TTW effective-source namespace hash or identity differs.");
      }
      let retailContract = null;
      if (files.ttwRetailStage10ContractPath) {
        const retail = readJson(
          files.ttwRetailStage10ContractPath,
          "TTW Fallout 3 retail stage-10 contract");
        retailContract = {
          schema: retail.value?.schema,
          classification: retail.value?.classification,
          targetVersion: retail.value?.target?.version,
          targetExecutableSha256: retail.value?.target?.sha256,
          synthetic: retail.value?.classification ===
            "synthetic-ttw-parser-test-only-not-retail-evidence",
          path: retail.path
        };
      }
      return preflightFo3Stage10Launch(routeId, {
        ttwOpeningProfile: {
          schema: opening.value.schema,
          validated: true,
          sourceEdition: route.sourceEdition,
          path: opening.path,
          sourceNamespacePath: sourceNamespace.path,
          sourceNamespaceSha256: namespaceSha256
        },
        ttwRetailStage10Contract: retailContract
      });
    }

    if (files.ttwOpeningProfilePath || files.ttwSourceRoot ||
        files.ttwRetailStage10ContractPath) {
      return preflightFo3Stage10Launch(routeId, { ttwOpeningProfile: {} });
    }
    const profile = readJson(files.fallout3ProfilePath, "standalone Fallout 3 profile");
    const master = profile.value?.install?.master;
    const dataRoot = profile.value?.install?.dataRoot;
    if (!nonEmpty(dataRoot) ||
        existsSync(path.join(dataRoot, "TaleOfTwoWastelands.esm")) ||
        existsSync(path.join(dataRoot, "YUPTTW.esm"))) {
      throw new Error("Standalone Fallout 3 profile points at a TTW source root.");
    }

    const presentation = readJson(
      files.birthPresentationPath,
      "standalone Fallout 3 birth presentation");
    const birthSliceRow = presentation.value?.source;
    const birthSlice = readJson(birthSliceRow?.birthSlice, "Fallout 3 birth slice");
    if (birthSlice.value?.schema !== "opennv-fo3-opening-slice/v1" ||
        fileSha256(birthSlice.path) !== birthSliceRow?.birthSliceSha256) {
      throw new Error("Fallout 3 birth presentation source-slice hash differs.");
    }
    const birthMaster = birthSlice.value?.source?.master;

    const retail = readJson(
      files.retailStage10ContractPath,
      "standalone Fallout 3 retail stage-10 contract");
    return preflightFo3Stage10Launch(routeId, {
      fallout3Profile: {
        ready: profile.value?.status === "registered-owned-profile" &&
          profile.value?.capabilities?.runtimeBootReady === true,
        schema: profile.value?.schema,
        campaign: profile.value?.campaign,
        sourceEdition: route.sourceEdition,
        masterFile: master?.file,
        masterSha256: master?.sha256,
        path: profile.path,
        savePath: files.savePath
      },
      birthPresentation: {
        schema: presentation.value?.schema,
        sourceEdition: route.sourceEdition,
        masterFile: birthMaster?.file,
        masterSha256: birthMaster?.sha256,
        path: presentation.path
      },
      retailStage10Contract: {
        schema: retail.value?.schema,
        classification: retail.value?.classification,
        targetVersion: retail.value?.target?.version,
        targetExecutableSha256: retail.value?.target?.sha256,
        synthetic: retail.value?.classification ===
          "synthetic-parser-test-only-not-retail-evidence",
        path: retail.path
      },
      reportPath: files.reportPath,
      captureRoot: files.captureRoot
    });
  } catch (error) {
    return blocked(
      routeId,
      route.sourceEdition,
      error instanceof Error ? error.message : "FO3 stage-10 file preflight failed.");
  }
}
