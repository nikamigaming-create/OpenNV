import { app, BrowserWindow, ipcMain, shell } from "electron";
import { spawn } from "node:child_process";
import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { createOfflineState, mergeRuntimeState, validateLaunchRequest } from "./contract.mjs";

const here = path.dirname(fileURLToPath(import.meta.url));
const renderer = path.join(here, "renderer", "index.html");

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

function bridgeScript() {
  const root = runtimeRoot();
  if (!root) return null;
  const script = path.join(root, "scripts", "Get-OpenNVLauncherState.ps1");
  return existsSync(script) ? script : null;
}

function callPowerShell(script, args, { detached = false } = {}) {
  const executable = process.env.SystemRoot
    ? path.join(process.env.SystemRoot, "System32", "WindowsPowerShell", "v1.0", "powershell.exe")
    : "powershell.exe";
  return spawn(executable, ["-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, ...args], {
    detached,
    stdio: detached ? "ignore" : ["ignore", "pipe", "pipe"],
    windowsHide: true
  });
}

async function readRuntimeState() {
  if (process.platform !== "win32") return null;
  const script = bridgeScript();
  if (!script) return null;

  return new Promise((resolve) => {
    const child = callPowerShell(script, ["-AsJson"]);
    let stdout = "";
    child.stdout.on("data", (chunk) => { stdout += chunk; });
    child.once("error", () => resolve(null));
    child.once("close", (code) => {
      if (code !== 0) return resolve(null);
      try {
        resolve(JSON.parse(stdout));
      } catch {
        resolve(null);
      }
    });
  });
}

async function launcherState() {
  const base = createOfflineState();
  return mergeRuntimeState(base, await readRuntimeState());
}

function isWindowsRuntimeRoot(candidate) {
  return process.platform === "win32"
    && existsSync(path.join(candidate, "scripts", "Get-OpenNVLauncherState.ps1"))
    && existsSync(path.join(candidate, "scripts", "Start-OpenNV.ps1"));
}

async function chooseRuntime() {
  if (process.platform !== "win32") {
    return { ok: false, message: "This platform's runtime package is not published yet. The launcher shell is ready for its future native bridge." };
  }
  const selection = await dialog.showOpenDialog({
    title: "Choose an extracted OpenNV runtime folder",
    properties: ["openDirectory", "createDirectory"]
  });
  if (selection.canceled || selection.filePaths.length === 0) return { ok: false, message: "No runtime folder selected." };
  const candidate = selection.filePaths[0];
  if (!isWindowsRuntimeRoot(candidate)) {
    return { ok: false, message: "That folder is not an extracted OpenNV Windows runtime. Select the folder containing scripts/Start-OpenNV.ps1." };
  }
  mkdirSync(path.dirname(runtimeConfigPath()), { recursive: true });
  writeFileSync(runtimeConfigPath(), `${JSON.stringify({ runtimeRoot: candidate }, null, 2)}\n`, "utf8");
  return { ok: true, message: "OpenNV runtime bridge connected." };
}

function launch(request) {
  const { campaign, enableJam } = validateLaunchRequest(request);
  if (process.platform !== "win32") {
    return { ok: false, code: "runtime-port-pending", message: "The Open Nevada shell runs here; this runtime build is not available for this platform yet." };
  }

  const root = runtimeRoot();
  const script = root ? path.join(root, "scripts", "Start-OpenNV.ps1") : null;
  if (!script || !existsSync(script)) {
    return { ok: false, code: "runtime-not-found", message: "Choose an installed OpenNV runtime before launching a world." };
  }

  const args = ["-Campaign", campaign.engineCampaign];
  if (enableJam) args.push("-EnableJam");
  const child = callPowerShell(script, args, { detached: true });
  child.unref();
  return { ok: true, message: `${campaign.title} launch handed to the local OpenNV runtime.` };
}

function createWindow() {
  const window = new BrowserWindow({
    width: 1380,
    height: 900,
    minWidth: 1040,
    minHeight: 720,
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
