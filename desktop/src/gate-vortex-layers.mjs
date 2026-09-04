import { createHash } from "node:crypto";
import { existsSync, readFileSync, readdirSync, statSync } from "node:fs";
import path from "node:path";

export const GATE_VORTEX_LAYERS_SCHEMA = "opennv-gate-vortex-layers/v1";
const SUPPORTED_GAMES = new Set(["fallout-new-vegas", "fallout-3"]);

function identity(document) {
  return createHash("sha256")
    .update("opennv-gate-vortex-layers-v1\0", "utf8")
    .update(JSON.stringify({ game: document.game, layers: document.layers }), "utf8")
    .digest("hex");
}

function topLevelPlugins(root) {
  return readdirSync(root, { withFileTypes: true })
    .filter((entry) => entry.isFile() && [".esm", ".esp"].includes(path.extname(entry.name).toLowerCase()))
    .map((entry) => entry.name)
    .sort((left, right) => left.localeCompare(right, "en", { sensitivity: "base" }));
}

function gateMetadata(root) {
  const metadataPath = path.join(path.dirname(root), "install.json");
  if (!existsSync(metadataPath)) return null;
  const metadata = JSON.parse(readFileSync(metadataPath, "utf8"));
  if (metadata?.schema !== "opennv-local-mod-install/v1" ||
      path.resolve(metadata.contentRoot || "") !== path.resolve(root) ||
      typeof metadata.installId !== "string" || !metadata.installId) {
    throw new Error(`Gate Vortex install metadata does not own its content root: ${metadataPath}`);
  }
  return { metadataPath, installId: metadata.installId, displayName: metadata.displayName };
}

export function validateManagedLayers(document) {
  if (document?.schema !== GATE_VORTEX_LAYERS_SCHEMA || !SUPPORTED_GAMES.has(document?.game) ||
      !Array.isArray(document?.layers)) {
    throw new Error("The Gate Vortex layer catalog is invalid.");
  }
  const ids = new Set();
  const roots = new Set();
  for (const [order, layer] of document.layers.entries()) {
    const resolved = path.resolve(String(layer?.root || ""));
    if (layer?.order !== order || typeof layer?.id !== "string" || !layer.id || ids.has(layer.id) ||
        !path.isAbsolute(String(layer?.root || "")) || roots.has(resolved.toLowerCase()) ||
        typeof layer?.provider !== "string" || typeof layer?.displayName !== "string" ||
        typeof layer?.enabled !== "boolean" || !Array.isArray(layer?.plugins) ||
        layer.plugins.some((file) => typeof file !== "string" || path.basename(file) !== file ||
          ![".esm", ".esp"].includes(path.extname(file).toLowerCase()))) {
      throw new Error("The Gate Vortex layer catalog contains an invalid layer.");
    }
    ids.add(layer.id);
    roots.add(resolved.toLowerCase());
  }
  if (document.catalogId !== identity(document)) {
    throw new Error("The Gate Vortex layer catalog identity changed.");
  }
  return document;
}

export function synchronizeManagedLayers(stack, previous = null) {
  const validatedPrevious = previous === null ? null : validateManagedLayers(previous);
  if (validatedPrevious !== null && validatedPrevious.game !== stack.game) {
    throw new Error("Gate Vortex refuses to apply a layer catalog from another game profile.");
  }
  const prior = validatedPrevious?.layers || [];
  const byId = new Map(prior.map((layer) => [layer.id, layer]));
  const activeIds = new Set(stack.roots.slice(1).map((root) => root.id));
  const layers = [...prior];
  for (const root of stack.roots.slice(1)) {
    const resolved = path.resolve(root.root);
    if (!existsSync(resolved) || !statSync(resolved).isDirectory()) {
      throw new Error(`Managed mod layer is missing: ${resolved}`);
    }
    const metadata = root.provider === "gate-vortex" ? gateMetadata(resolved) : null;
    const next = {
      id: root.id,
      provider: root.provider,
      root: resolved,
      displayName: metadata?.displayName || path.basename(resolved),
      enabled: true,
      order: 0,
      plugins: topLevelPlugins(resolved),
      removable: root.provider === "gate-vortex",
      installId: metadata?.installId || null,
      metadataPath: metadata?.metadataPath || null
    };
    if (byId.has(root.id)) layers[layers.findIndex((layer) => layer.id === root.id)] = next;
    else layers.push(next);
  }
  const normalized = layers
    .filter((layer) => activeIds.has(layer.id) || !layer.enabled)
    .map((layer, order) => ({ ...layer, order }));
  const document = { schema: GATE_VORTEX_LAYERS_SCHEMA, game: stack.game, layers: normalized };
  document.catalogId = identity(document);
  return validateManagedLayers(document);
}

export function updateManagedLayer(document, layerId, action) {
  validateManagedLayers(document);
  const index = document.layers.findIndex((layer) => layer.id === layerId);
  if (index < 0) throw new Error(`Managed mod layer is unknown: ${layerId}`);
  const layers = document.layers.map((layer) => ({ ...layer, plugins: [...layer.plugins] }));
  if (action === "enable" || action === "disable") {
    layers[index].enabled = action === "enable";
  } else if (action === "move-up" || action === "move-down") {
    const target = action === "move-up" ? index - 1 : index + 1;
    if (target < 0 || target >= layers.length) throw new Error(`Layer ${layerId} cannot move ${action.slice(5)}.`);
    [layers[index], layers[target]] = [layers[target], layers[index]];
  } else if (action === "uninstall") {
    if (!layers[index].removable || layers[index].provider !== "gate-vortex") {
      throw new Error("Only launcher-owned Gate Vortex installs can be uninstalled; external MO2, Vortex, Wabbajack, TTW, JAM, and manual folders remain read-only.");
    }
    layers.splice(index, 1);
  } else {
    throw new Error(`Unsupported Gate Vortex layer action: ${action}`);
  }
  const normalized = layers.map((layer, order) => ({ ...layer, order }));
  const result = { schema: GATE_VORTEX_LAYERS_SCHEMA, game: document.game, layers: normalized };
  result.catalogId = identity(result);
  return validateManagedLayers(result);
}
