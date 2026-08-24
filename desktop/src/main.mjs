import { app, BrowserWindow, dialog, ipcMain, shell } from "electron";
import { spawn } from "node:child_process";
import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { createOfflineState, createRuntimeArguments, mergeRuntimeState, validateLaunchRequest } from "./contract.mjs";

const here = path.dirname(fileURLToPath(import.meta.url));
const renderer = path.join(here, "renderer", "index.html");
const RUNTIME_CONFIG_JSON_INDENT = 2;

function productConfigurationPath() {
  return app.isPackaged
    ? path.join(process.resourcesPath, "config", "open-nv-runtime-v1.json")
    : path.join(here, "..", "..", "runtime", "config", "open-nv-runtime-v1.json");
}

function desktopLauncherPolicy() {
  const configuration = JSON.parse(readFileSync(productConfigurationPath(), "utf8"));
  const policy = configuration?.desktopLauncher;
  if (!policy) throw new Error("OpenNV desktop launcher policy is missing.");
  return policy;
}

function runtimeConfigPath() {
  return path.join(app.getPath("userData"), "runtime.json");
}

function configuredRuntimeRoot() {
  try {
    const configured = JSON.parse(readFileSync(runtimeConfigPath(), "utf8"));
    if (configured?.runtimeRoot && existsSync(configured.runtimeRoot)) return configured.runtimeRoot;
  } catch {
    // The launcher remains usable before a user chooses a runtime.
  }
  return null;
}

function runtimeRoot() {
  const explicit = process.env.OPENNV_RUNTIME_ROOT || process.env.OPENNV_HOME;
  if (explicit && existsSync(explicit)) return explicit;
  const configured = configuredRuntimeRoot();
  if (configured) return configured;
  return null;
}

function runtimeManifest() {
  const root = runtimeRoot();
  if (!root) return null;
  const manifestPath = path.join(root, "runtime-manifest.json");
  if (!existsSync(manifestPath)) return null;
  try {
    const manifest = JSON.parse(readFileSync(manifestPath, "utf8"));
    return manifest?.schema === "opennv-runtime-manifest/v1" ? { root, manifest } : null;
  } catch {
    return null;
  }
}

async function readRuntimeState() {
  return runtimeManifest()?.manifest ?? null;
}

async function launcherState() {
  const base = createOfflineState();
  return {
    ...mergeRuntimeState(base, await readRuntimeState()),
    desktopLauncher: desktopLauncherPolicy()
  };
}

function isRuntimeRoot(candidate) {
  const manifestPath = path.join(candidate, "runtime-manifest.json");
  if (!existsSync(manifestPath)) return false;
  try {
    return JSON.parse(readFileSync(manifestPath, "utf8"))?.schema === "opennv-runtime-manifest/v1";
  } catch {
    return false;
  }
}

async function chooseRuntime() {
  const selection = await dialog.showOpenDialog({
    title: "Choose an extracted OpenNV runtime folder",
    properties: ["openDirectory", "createDirectory"]
  });
  if (selection.canceled || selection.filePaths.length === 0) return { ok: false, message: "No runtime folder selected." };
  const candidate = selection.filePaths[0];
  if (!isRuntimeRoot(candidate)) {
    return { ok: false, message: "That folder has no valid OpenNV Godot runtime-manifest.json." };
  }
  mkdirSync(path.dirname(runtimeConfigPath()), { recursive: true });
  writeFileSync(
    runtimeConfigPath(),
    `${JSON.stringify({ runtimeRoot: candidate }, null, RUNTIME_CONFIG_JSON_INDENT)}\n`,
    "utf8"
  );
  return { ok: true, message: "OpenNV runtime bridge connected." };
}

function launch(request) {
  const { campaign, enableJam, enableVr } = validateLaunchRequest(request);
  const installed = runtimeManifest();
  if (!installed) {
    return { ok: false, code: "runtime-not-found", message: "Choose an installed OpenNV runtime before launching a world." };
  }
  if (!installed.manifest.runtime?.canLaunch) {
    return { ok: false, code: "runtime-slice-not-playable", message: installed.manifest.runtime?.label || "This runtime slice is not playable yet." };
  }
  const runtimeCampaign = installed.manifest.campaigns?.find((entry) =>
    String(entry?.id ?? "").toLowerCase() === campaign.engineCampaign.toLowerCase());
  if (!runtimeCampaign?.variants?.vanilla?.ready) {
    return { ok: false, code: "campaign-not-ready", message: runtimeCampaign?.variants?.vanilla?.message || `${campaign.title} is not ready in this runtime.` };
  }
  if (enableJam && !runtimeCampaign?.variants?.jam?.ready) {
    return { ok: false, code: "jam-not-ready", message: runtimeCampaign?.variants?.jam?.message || "JAM is not ready in this runtime." };
  }
  const openXr = installed.manifest.runtime?.presentationModes?.openxr;
  if (enableVr && !openXr?.launchable) {
    return { ok: false, code: "openxr-not-ready", message: "OpenXR is not launchable in this runtime." };
  }
  const relativeExecutable = installed.manifest.runtime?.executables?.[process.platform];
  const executable = relativeExecutable ? path.join(installed.root, relativeExecutable) : null;
  if (!executable || !existsSync(executable)) {
    return { ok: false, code: "runtime-executable-missing", message: `The runtime has no ${process.platform} executable.` };
  }

  const args = createRuntimeArguments({ campaign, enableJam, enableVr });
  const child = spawn(executable, args, { detached: true, stdio: "ignore", windowsHide: true });
  child.unref();
  return { ok: true, message: `${campaign.title} ${enableVr ? "OpenXR" : "flat"} launch handed to the local OpenNV runtime.` };
}

function createWindow() {
  const policy = desktopLauncherPolicy();
  const window = new BrowserWindow({
    width: policy.mainWindowWidthPixels,
    height: policy.mainWindowHeightPixels,
    minWidth: policy.mainWindowMinimumWidthPixels,
    minHeight: policy.mainWindowMinimumHeightPixels,
    backgroundColor: "#050b12",
    title: "Open Nevada",
    webPreferences: {
      contextIsolation: true,
      nodeIntegration: false,
      preload: path.join(here, "preload.mjs")
    }
  });
  window.removeMenu();
  window.loadFile(renderer);
}

app.whenReady().then(() => {
  ipcMain.handle("opennv:get-state", launcherState);
  ipcMain.handle("opennv:choose-runtime", chooseRuntime);
  ipcMain.handle("opennv:launch", (_, request) => launch(request));
  ipcMain.handle("opennv:open-external", async (_, value) => {
    const url = new URL(value);
    if (url.protocol !== "https:") throw new Error("Open Nevada only opens secure external links.");
    await shell.openExternal(url.toString());
  });
  createWindow();
  app.on("activate", () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

app.on("window-all-closed", () => {
  if (process.platform !== "darwin") app.quit();
});
