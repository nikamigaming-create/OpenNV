export const LAUNCHER_STATE_SCHEMA = "opennv-launcher-state/v2";

export const CAMPAIGNS = Object.freeze([
  Object.freeze({
    id: "newvegas",
    engineCampaign: "NewVegas",
    title: "New Vegas",
    eyebrow: "Standalone route",
    character: "A separate Mojave character",
    detail: "Use the base profile, then add JAM whenever you are ready.",
    jam: true,
    ttw: false,
    assetRequirement: "Licensed New Vegas data"
  }),
  Object.freeze({
    id: "fallout3",
    engineCampaign: "Fallout3",
    title: "Fallout 3",
    eyebrow: "Standalone route",
    character: "A separate Capital Wasteland character",
    detail: "Use the vanilla standalone profile, or choose TTW now for a shared world path.",
    jam: false,
    ttw: false,
    assetRequirement: "Licensed Fallout 3 data"
  }),
  Object.freeze({
    id: "ttw",
    engineCampaign: "TTW",
    title: "TTW",
    eyebrow: "Combined route",
    character: "One Capital Wasteland-to-Mojave character",
    detail: "This is the TTW choice at character creation. Start base or add JAM later.",
    jam: true,
    ttw: true,
    assetRequirement: "Licensed Fallout 3, New Vegas, DLC, and official TTW output"
  })
]);

export const EXTENDER_LAYERS = Object.freeze([
  Object.freeze({
    id: "content",
    title: "Content records",
    status: "validated",
    detail: "Data plugins and assets are mounted through isolated profiles."
  }),
  Object.freeze({
    id: "semantic-bridge",
    title: "Script-extender contract",
    status: "building",
    detail: "Open Nevada implements portable behavior contracts instead of loading a Windows DLL into another runtime."
  }),
  Object.freeze({
    id: "native-bridge",
    title: "Native Windows bridge",
    status: "measured",
    detail: "Windows-only plugins are profiled command-by-command, then promoted only after a portable equivalent is verified."
  })
]);

function defaultRuntime(platform) {
  const windows = platform === "win32";
  return {
    status: windows ? "runtime-searching" : "portable-shell",
    platform,
    label: windows ? "Choose a local OpenNV Godot runtime folder" : "Launcher shell ready; runtime export is not published for this platform",
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
    product: {
      name: "Open Nevada",
      codeName: "OpenNV",
      tagline: "Choose a world. Keep the save path clean."
    },
    campaignRule: "Choose New Vegas, standalone Fallout 3, or TTW before creating a character. TTW is a separate combined-world choice and never merges existing standalone saves.",
    jamRule: "JAM is modular for New Vegas and TTW. You can add it later, but saves created with JAM must keep it enabled.",
    campaigns: CAMPAIGNS.map((campaign) => ({ ...campaign, ready: false, readiness: "Register licensed data to enable this route." })),
    extenderLayers: EXTENDER_LAYERS,
    runtime: defaultRuntime(platform),
    mods: [
      {
        id: "jam",
        title: "Just Assorted Mods",
        status: "available-when-registered",
        detail: "Profile module for New Vegas and TTW; it can be added after character creation."
      },
      {
        id: "benny-humbles",
        title: "Benny Humbles You and Steals Your Stuff",
        status: "bridge-validation",
        detail: "Queued for the OpenNV semantic extender bridge; not represented as portable support until the required behaviors pass."
      }
    ]
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
      readiness: ready ? "Ready in the installed runtime." : (vanilla.message || "Register licensed data to enable this route."),
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
      label: String(declaredRuntime.label || "Connected to the local OpenNV Godot runtime"),
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
  if (!campaign) throw new Error("Choose a valid world path before launching.");
  if (request?.enableJam && !campaign.jam) {
    throw new Error("JAM is available for New Vegas and TTW, not standalone Fallout 3.");
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
