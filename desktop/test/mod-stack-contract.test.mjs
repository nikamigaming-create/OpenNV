import test from "node:test";
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import {
  MOD_STACK_EDITION_PROFILES,
  MOD_STACK_PROVIDERS,
  appendSourceRoot,
  createOwnedFallout3Stack,
  createOwnedNewVegasStack,
  createModStack,
  inspectSourceRoot,
  importMo2Profile,
  importTtwInstallerProfile,
  resolveLooseFile,
  validateInstalledModStack,
  validateModStack
} from "../src/mod-stack-contract.mjs";

function withRoots(run) {
  const workspace = mkdtempSync(path.join(tmpdir(), "opennv-mod-stack-"));
  try {
    const vanilla = path.join(workspace, "Data");
    const mod = path.join(workspace, "JAM");
    mkdirSync(path.join(vanilla, "textures", "interface"), { recursive: true });
    mkdirSync(path.join(mod, "Textures", "Interface"), { recursive: true });
    writeFileSync(path.join(vanilla, "FalloutNV.esm"), "TES4master");
    writeFileSync(path.join(vanilla, "Fallout - Textures.bsa"), "archive");
    writeFileSync(path.join(workspace, "Fallout_default.ini"),
      "[Archive]\nSArchiveList=Fallout - Textures.bsa\n");
    writeFileSync(path.join(vanilla, "textures", "interface", "hud.dds"), "vanilla");
    writeFileSync(path.join(mod, "JustAssortedMods.esp"), "plugin");
    writeFileSync(path.join(mod, "Textures", "Interface", "HUD.DDS"), "mod");
    run({ vanilla, mod });
  } finally {
    rmSync(workspace, { recursive: true, force: true });
  }
}

test("all supported managers feed one explicit low-to-high source contract", () => {
  assert.deepEqual(MOD_STACK_PROVIDERS, [
    "manual", "gate-vortex", "mo2", "wabbajack", "vortex", "nexus-mods-app", "thunderstore",
    "ttw-installer"
  ]);
  withRoots(({ vanilla, mod }) => {
    const vanillaFiles = inspectSourceRoot(vanilla);
    const modFiles = inspectSourceRoot(mod);
    const stack = createModStack({
      name: "JAM test",
      roots: [
        { id: "vanilla", provider: "manual", root: vanilla },
        { id: "jam", provider: "vortex", root: mod }
      ],
      plugins: [
        { index: 0, rootId: "vanilla", ...vanillaFiles.plugins[0] },
        { index: 1, rootId: "jam", ...modFiles.plugins[0] }
      ],
      archives: [
        { index: 0, rootId: "vanilla", ...vanillaFiles.archives[0] }
      ]
    });
    assert.equal(validateModStack(stack), stack);
    assert.equal(stack.stackId.length, 64);
  });
});

test("v2 source stacks bind edition metadata and save compatibility", () => {
  withRoots(({ vanilla }) => {
    for (const edition of ["fallout-new-vegas", "fallout-3", "ttw"]) {
      const stack = createModStack({
        name: `${edition} metadata`,
        edition,
        roots: [{ id: "owned", provider: "manual", root: vanilla }]
      });
      const profile = MOD_STACK_EDITION_PROFILES[edition];
      assert.equal(stack.schema, "opennv-mod-stack/v2");
      assert.equal(stack.edition, edition);
      assert.equal(stack.engineBuild, profile.engineBuild);
      assert.equal(stack.contentVersion, profile.contentVersion);
      assert.deepEqual(stack.supportedCampaigns, profile.supportedCampaigns);
      assert.deepEqual(stack.semanticExtensions, profile.semanticExtensions);
      assert.equal(stack.saveCompatibilityId, `${edition}:${stack.stackId}`);
      assert.equal(validateModStack(stack), stack);
    }

    const stack = createModStack({
      name: "metadata tamper",
      roots: [{ id: "owned", provider: "manual", root: vanilla }]
    });
    stack.contentVersion = "unknown";
    assert.throws(() => validateModStack(stack), /edition metadata/u);
    stack.contentVersion = MOD_STACK_EDITION_PROFILES["fallout-new-vegas"].contentVersion;
    stack.saveCompatibilityId = "fallout-new-vegas:" + "0".repeat(64);
    assert.throws(() => validateModStack(stack), /save namespace/u);
  });
});

test("source inspection inventories plugins and archives without extracting them", () => {
  withRoots(({ vanilla, mod }) => {
    assert.deepEqual(inspectSourceRoot(vanilla).plugins.map((row) => row.file), ["FalloutNV.esm"]);
    assert.deepEqual(inspectSourceRoot(vanilla).archives.map((row) => row.file), ["Fallout - Textures.bsa"]);
    assert.deepEqual(inspectSourceRoot(mod).plugins.map((row) => row.file), ["JustAssortedMods.esp"]);
  });
});

test("owned Data registration validates TES4 and records fast archive metadata", () => {
  withRoots(({ vanilla }) => {
    const stack = createOwnedNewVegasStack(vanilla);
    assert.equal(stack.roots[0].id, "owned-data");
    assert.equal(stack.plugins[0].file, "FalloutNV.esm");
    assert.equal(stack.archives[0].file, "Fallout - Textures.bsa");
    assert.equal(stack.archives[0].bytes, 7);
    assert.deepEqual(stack.archives[0].activation,
      { kind: "fallout-default-ini", key: "SArchiveList" });
    assert.equal(stack.archiveOrderSource.files.length, 1);
    assert.equal(validateInstalledModStack(stack), stack);
    writeFileSync(path.join(vanilla, "Fallout - Textures.bsa"), "archive-changed");
    assert.throws(() => validateInstalledModStack(stack), /changed/u);
  });
});

test("standalone Fallout 3 registers an isolated official native source stack", () => {
  const workspace = mkdtempSync(path.join(tmpdir(), "opennv-fo3-stack-"));
  try {
    const data = path.join(workspace, "Data");
    mkdirSync(data, { recursive: true });
    writeFileSync(path.join(data, "Fallout3.esm"), "TES4master");
    writeFileSync(path.join(data, "Anchorage.esm"), "TES4dlc");
    writeFileSync(path.join(data, "Fallout - Meshes.bsa"), "meshes");
    writeFileSync(path.join(data, "Anchorage - Main.bsa"), "anchorage");
    writeFileSync(path.join(workspace, "Fallout_default.ini"),
      "[Archive]\nSArchiveList=Fallout - Meshes.bsa\n");
    const stack = createOwnedFallout3Stack(data);
    assert.equal(stack.game, "fallout-3");
    assert.equal(stack.roots[0].id, "owned-fallout3-data");
    assert.deepEqual(stack.plugins.map((row) => row.file), ["Fallout3.esm", "Anchorage.esm"]);
    assert.deepEqual(stack.archives.map((row) => row.file),
      ["Fallout - Meshes.bsa", "Anchorage - Main.bsa"]);
    assert.equal(stack.archives[1].activation.plugin, "Anchorage.esm");
    assert.equal(validateInstalledModStack(stack), stack);
  } finally {
    rmSync(workspace, { recursive: true, force: true });
  }
});

test("active BSA order comes from Fallout_default.ini then enabled plugin order", () => {
  withRoots(({ vanilla }) => {
    const gameRoot = path.dirname(vanilla);
    const profile = path.join(gameRoot, "FalloutNV");
    mkdirSync(profile);
    writeFileSync(path.join(vanilla, "Base First.bsa"), "base-first");
    writeFileSync(path.join(vanilla, "Fallout - Textures.bsa"), "base-textures");
    writeFileSync(path.join(vanilla, "Update.bsa"), "base-update");
    writeFileSync(path.join(vanilla, "Active.esm"), "active-plugin");
    writeFileSync(path.join(vanilla, "Active - Main.bsa"), "active-main");
    writeFileSync(path.join(vanilla, "Active - Sounds.bsa"), "active-sounds");
    writeFileSync(path.join(vanilla, "Disabled.esp"), "disabled-plugin");
    writeFileSync(path.join(vanilla, "Disabled - Main.bsa"), "inactive-archive");
    writeFileSync(path.join(vanilla, "Unrelated.bsa"), "unrelated-archive");
    writeFileSync(path.join(gameRoot, "Fallout_default.ini"),
      "[Archive]\nSArchiveList=Base First.bsa, Fallout - Textures.bsa\n");
    writeFileSync(path.join(profile, "plugins.txt"), "*FalloutNV.esm\n*Active.esm\nDisabled.esp\n");

    const stack = createOwnedNewVegasStack(vanilla, { configRoot: profile });
    assert.deepEqual(stack.archives.map((row) => row.file), [
      "Base First.bsa",
      "Fallout - Textures.bsa",
      "Update.bsa",
      "Active - Main.bsa",
      "Active - Sounds.bsa"
    ]);
    assert.deepEqual(stack.archives.slice(2).map((row) => row.activation.plugin), [
      "FalloutNV.esm", "Active.esm", "Active.esm"
    ]);
    assert.equal(stack.archives.some((row) => row.file === "Disabled - Main.bsa"), false);
    assert.equal(stack.archives.some((row) => row.file === "Unrelated.bsa"), false);
  });
});

test("archive list provenance is hash-bound and missing declared archives fail closed", () => {
  withRoots(({ vanilla }) => {
    const ini = path.join(path.dirname(vanilla), "Fallout_default.ini");
    const stack = createOwnedNewVegasStack(vanilla);
    writeFileSync(ini, "[Archive]\nSArchiveList=Missing.bsa\n");
    assert.throws(() => validateInstalledModStack(stack), /Archive-order source changed/u);
    assert.throws(() => createOwnedNewVegasStack(vanilla), /activates a missing BSA/u);
  });
});

test("canonical FNV plugins.txt and loadorder.txt control enabled plugin order", () => {
  withRoots(({ vanilla }) => {
    const profile = path.join(path.dirname(vanilla), "FalloutNV");
    mkdirSync(profile);
    writeFileSync(path.join(vanilla, "Earlier.esp"), "plugin-a");
    writeFileSync(path.join(vanilla, "Later.esp"), "plugin-b");
    writeFileSync(path.join(vanilla, "Disabled.esp"), "plugin-off");
    writeFileSync(path.join(profile, "plugins.txt"), "*Later.esp\n*Earlier.esp\nDisabled.esp\n");
    writeFileSync(path.join(profile, "loadorder.txt"), "FalloutNV.esm\nEarlier.esp\nLater.esp\nDisabled.esp\n");
    const stack = createOwnedNewVegasStack(vanilla, { configRoot: profile });
    assert.deepEqual(stack.plugins.map((row) => row.file), [
      "FalloutNV.esm", "Earlier.esp", "Later.esp"
    ]);
    assert.equal(stack.orderSource.kind, "fnv-profile");
    assert.equal(validateInstalledModStack(stack), stack);
    writeFileSync(path.join(profile, "plugins.txt"), "*Earlier.esp\n");
    assert.throws(() => validateInstalledModStack(stack), /Load-order source changed/u);
  });
});

test("non-official deployed plugins require an explicit load order", () => {
  withRoots(({ vanilla }) => {
    writeFileSync(path.join(vanilla, "Mystery.esp"), "plugin");
    assert.throws(() => createOwnedNewVegasStack(vanilla), /explicit plugins.txt/u);
  });
});

test("MO2 and Wabbajack profiles import enabled folder priority and plugin order", () => {
  withRoots(({ vanilla }) => {
    const instance = path.join(path.dirname(vanilla), "Portable Instance");
    const profile = path.join(instance, "profiles", "Courier");
    const mods = path.join(instance, "mods");
    mkdirSync(profile, { recursive: true });
    mkdirSync(path.join(mods, "Low Assets"), { recursive: true });
    mkdirSync(path.join(mods, "High Patch"), { recursive: true });
    writeFileSync(path.join(mods, "Low Assets", "Low.esm"), "low");
    writeFileSync(path.join(mods, "Low Assets", "Low - Main.bsa"), "low-bsa");
    writeFileSync(path.join(mods, "High Patch", "Patch.esp"), "patch");
    writeFileSync(path.join(mods, "High Patch", "Patch - Main.bsa"), "patch-bsa");
    writeFileSync(path.join(mods, "High Patch", "Fallout - Textures.bsa"), "texture-override-bsa");
    writeFileSync(path.join(mods, "High Patch", "Inactive - Main.bsa"), "inactive-bsa");
    writeFileSync(path.join(profile, "modlist.txt"), "+High Patch\n-Low Disabled\n+Low Assets\n");
    writeFileSync(path.join(profile, "plugins.txt"), "*FalloutNV.esm\n*Low.esm\n*Patch.esp\n");
    const base = createOwnedNewVegasStack(vanilla);
    const stack = importMo2Profile(base, profile, { provider: "wabbajack" });
    assert.deepEqual(stack.roots.map((row) => path.basename(row.root)), [
      "Data", "Low Assets", "High Patch"
    ]);
    assert.deepEqual(stack.plugins.map((row) => row.file), [
      "FalloutNV.esm", "Low.esm", "Patch.esp"
    ]);
    assert.equal(stack.roots[2].provider, "wabbajack");
    assert.equal(stack.orderSource.files.length, 2);
    assert.deepEqual(stack.archives.map((row) => [row.file, row.rootId]), [
      ["Fallout - Textures.bsa", stack.roots[2].id],
      ["Low - Main.bsa", stack.roots[1].id],
      ["Patch - Main.bsa", stack.roots[2].id]
    ]);
  });
});

test("a registered flattened TTW profile becomes one native layered source stack", () => {
  withRoots(({ vanilla }) => {
    const workspace = path.dirname(vanilla);
    const flattened = path.join(workspace, "TTW Installed");
    mkdirSync(flattened);
    const pluginNames = ["FalloutNV.esm", "Fallout3.esm", "TaleOfTwoWastelands.esm", "YUPTTW.esm"];
    const pluginRows = pluginNames.map((file, index) => {
      const bytes = Buffer.from(index === 0 ? "TES4ttw-master" : `TES4ttw-${index}`);
      writeFileSync(path.join(flattened, file), bytes);
      return {
        file,
        loadOrderIndex: index,
        sourceRootIndex: 1,
        bytes: bytes.length,
        sha256: createHash("sha256").update(bytes).digest("hex"),
        masters: index === 0 ? [] : ["FalloutNV.esm"]
      };
    });
    const archiveNames = [
      "Fallout3 - Main.bsa", "TaleOfTwoWastelands - Main.bsa", "YUPTTW - Main.bsa"
    ];
    for (const file of archiveNames) writeFileSync(path.join(flattened, file), `archive-${file}`);
    const loadOrder = path.join(workspace, "ttw.loadorder.txt");
    writeFileSync(loadOrder, `${pluginNames.join("\n")}\n`);
    const loadOrderBytes = Buffer.from(`${pluginNames.join("\n")}\n`);
    const profilePath = path.join(workspace, "ttw-profile.json");
    writeFileSync(profilePath, JSON.stringify({
      schema: "opennv-ttw-profile/v1",
      status: "validated-generated-plugin-profile",
      kind: "ttw",
      pluginStackId: "a".repeat(64),
      saveCompatibilityId: `ttw:${"a".repeat(64)}`,
      sourceRoots: [vanilla, flattened],
      plugins: pluginRows,
      archives: [
        { file: "Fallout - Textures.bsa", sourceRootIndex: 0, bytes: 7 },
        ...archiveNames.map((file) => ({
          file,
          sourceRootIndex: 1,
          bytes: Buffer.byteLength(`archive-${file}`)
        }))
      ],
      loadOrderSource: {
        file: loadOrder,
        sha256: createHash("sha256").update(loadOrderBytes).digest("hex")
      }
    }));

    const stack = importTtwInstallerProfile(profilePath);
    assert.deepEqual(stack.roots.map((row) => row.provider), ["manual", "ttw-installer"]);
    assert.deepEqual(stack.plugins.map((row) => row.file), pluginNames);
    assert.deepEqual(stack.archives.map((row) => row.file), [
      "Fallout - Textures.bsa", ...archiveNames
    ]);
    assert.equal(stack.orderSource.kind, "ttw-profile");
    assert.equal(validateInstalledModStack(stack), stack);
    writeFileSync(path.join(flattened, "YUPTTW.esm"), "same-size-bad");
    assert.throws(() => validateInstalledModStack(stack), /changed/u);
  });
});

test("loose resources resolve case-insensitively with the last source root winning", () => {
  withRoots(({ vanilla, mod }) => {
    const stack = createModStack({
      name: "Loose winner",
      roots: [
        { id: "vanilla", provider: "manual", root: vanilla },
        { id: "jam", provider: "wabbajack", root: mod }
      ]
    });
    const resolved = resolveLooseFile(stack, "textures/interface/hud.dds");
    assert.equal(resolved.winner.rootId, "jam");
    assert.equal(resolved.overridden.length, 1);
    assert.equal(resolved.overridden[0].rootId, "vanilla");
    assert.deepEqual(
      stack.looseFiles.filter((row) => row.path.toLowerCase() === "textures/interface/hud.dds")
        .map((row) => row.rootId),
      ["vanilla", "jam"]
    );
    assert.equal(validateInstalledModStack(stack), stack);
    writeFileSync(path.join(mod, "Textures", "Interface", "HUD.DDS"), "changed-loose-bytes");
    assert.throws(() => validateInstalledModStack(stack), /Loose-file inventory changed/u);
    assert.throws(() => resolveLooseFile(stack, "../FalloutNV.esm"), /escapes/u);
  });
});

test("loose-file metadata participates in stack and save identity", () => {
  withRoots(({ vanilla, mod }) => {
    const definition = () => createModStack({
      name: "Loose identity",
      roots: [
        { id: "vanilla", provider: "manual", root: vanilla },
        { id: "mod", provider: "vortex", root: mod }
      ]
    });
    const first = definition();
    writeFileSync(path.join(mod, "new-loose-resource.txt"), "new");
    assert.throws(() => validateInstalledModStack(first), /Loose-file inventory changed/u);
    writeFileSync(path.join(mod, "Textures", "Interface", "HUD.DDS"), "different-size");
    const second = definition();
    assert.notEqual(first.stackId, second.stackId);
  });
});

test("stack identity fails closed when precedence changes", () => {
  withRoots(({ vanilla, mod }) => {
    const stack = createModStack({
      name: "Immutable order",
      roots: [
        { id: "vanilla", provider: "manual", root: vanilla },
        { id: "mod", provider: "thunderstore", root: mod }
      ]
    });
    stack.roots.reverse();
    assert.throws(() => validateModStack(stack), /invalid source root|identity changed/u);
  });
});

test("adding a manager folder preserves order and replaces effective top-level winners", () => {
  withRoots(({ vanilla, mod }) => {
    let stack = appendSourceRoot(null, {
      id: "vanilla",
      provider: "manual",
      root: vanilla,
      name: "Courier profile"
    });
    stack = appendSourceRoot(stack, {
      id: "jam",
      provider: "nexus-mods-app",
      root: mod
    });
    assert.deepEqual(stack.roots.map((root) => root.id), ["vanilla", "jam"]);
    assert.deepEqual(stack.plugins.map((row) => row.file), [
      "FalloutNV.esm", "JustAssortedMods.esp"
    ]);
    assert.throws(() => appendSourceRoot(stack, {
      id: "duplicate",
      provider: "vortex",
      root: mod
    }), /already registered/u);
  });
});
