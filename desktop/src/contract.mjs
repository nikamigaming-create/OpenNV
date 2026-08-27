import launcherState from "./launcher-state.json" with { type: "json" };

function deepFreeze(value) {
  if (!value || typeof value !== "object" || Object.isFrozen(value)) return value;
  for (const child of Object.values(value)) deepFreeze(child);
  return Object.freeze(value);
}

const CONTRACT = deepFreeze(launcherState);
export const LAUNCHER_STATE_SCHEMA = CONTRACT.schema;
export const CAMPAIGNS = CONTRACT.campaigns;
export const EXTENDER_LAYERS = CONTRACT.extenderLayers;

function defaultRuntime(platform) {
  const windows = platform === "win32";
  return {
    status: windows ? "runtime-searching" : "portable-shell",
    platform,
    label: windows ? CONTRACT.copy.runtimeSearching : CONTRACT.copy.portableShell,
    canLaunch: false,
    openXrLaunchable: false,
    openXrHardwareValidated: false,
    source: "offline"
  };
}

const hostPlatform = typeof process === "undefined" ? "web" : process.platform;

export function createOfflineState({ platform = hostPlatform } = {}) {
  return {
    schema: LAUNCHER_STATE_SCHEMA,
    product: CONTRACT.product,
    campaignRule: CONTRACT.campaignRule,
    jamRule: CONTRACT.jamRule,
    campaigns: CAMPAIGNS.map((campaign) => ({
      ...campaign,
      ready: false,
      readiness: CONTRACT.copy.readinessUnavailable
    })),
    extenderLayers: EXTENDER_LAYERS,
    runtime: defaultRuntime(platform),
    mods: CONTRACT.mods
  };
}

function findCampaign(candidate) {
  const id = String(candidate?.id ?? "").toLowerCase();
  return CAMPAIGNS.find((campaign) => campaign.id === id || campaign.engineCampaign.toLowerCase() === id);
}

export function mergeRuntimeState(baseState, runtimeState) {
  if (!runtimeState || !Array.isArray(runtimeState.campaigns)) return baseState;

  const campaigns = baseState.campaigns.map((campaign) => {
    const runtimeCampaign = runtimeState.campaigns.find((entry) => findCampaign(entry)?.id === campaign.id);
    const variants = runtimeCampaign?.variants ?? {};
    const vanilla = variants.vanilla ?? {};
    const jam = variants.jam ?? null;
    const ready = Boolean(vanilla.ready);
    return {
      ...campaign,
      ready,
      readiness: ready
        ? "Ready in the installed runtime."
        : (vanilla.message || CONTRACT.copy.readinessUnavailable),
      jamReady: jam ? Boolean(jam.ready) : false,
      unavailableDlc: Array.isArray(vanilla.unavailableDlc) ? vanilla.unavailableDlc : []
    };
  });

  const declaredRuntime = runtimeState.runtime ?? {};
  const openXr = declaredRuntime.presentationModes?.openxr ?? {};
  const canLaunch = Boolean(declaredRuntime.canLaunch) && campaigns.some((campaign) => campaign.ready);
  return {
    ...baseState,
    campaigns,
    runtime: {
      ...baseState.runtime,
      status: String(declaredRuntime.status || "connected"),
      label: String(declaredRuntime.label || CONTRACT.copy.runtimeConnected),
      canLaunch,
      openXrLaunchable: Boolean(openXr.launchable),
      openXrHardwareValidated: Boolean(openXr.hardwareValidated),
      openXrStatus: String(openXr.status || "unavailable"),
      source: "runtime-manifest"
    },
    installer: runtimeState.installer ?? null
  };
}

export function validateLaunchRequest(request) {
  const campaign = CAMPAIGNS.find((entry) => entry.id === request?.campaign);
  if (!campaign) throw new Error(CONTRACT.copy.invalidCampaign);
  if (request?.enableJam && !campaign.jam) {
    throw new Error(CONTRACT.copy.jamUnavailable);
  }
  return {
    campaign,
    enableJam: Boolean(request?.enableJam),
    enableVr: Boolean(request?.enableVr)
  };
}

export function createRuntimeArguments({ campaign, enableJam, enableVr }) {
  const args = ["--xr-mode", enableVr ? "on" : "off", "--", "--campaign", campaign.engineCampaign];
  if (enableJam) args.push("--enable-jam");
  if (enableVr) args.push("--vr");
  return args;
}
