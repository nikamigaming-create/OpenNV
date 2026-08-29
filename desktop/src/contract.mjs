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

export function mergeRuntimeState(
  baseState,
  runtimeState,
  {
    fallout1Profile = null,
    fallout2Profile = null,
    fallout3Profile = null,
    newVegasProfile = null,
    ttwProfile = null,
    jamProfile = null
  } = {}
) {
  if (!runtimeState || !Array.isArray(runtimeState.campaigns)) return baseState;

  const campaigns = baseState.campaigns.map((campaign) => {
    const runtimeCampaign = runtimeState.campaigns.find((entry) => findCampaign(entry)?.id === campaign.id);
    const variants = runtimeCampaign?.variants ?? {};
    const selectedVariant = variants[campaign.runtimeVariant] ?? {};
    const jam = variants.jam ?? null;
    const profileRequired = ["fallout1", "fallout2", "newvegas", "fallout3", "ttw"].includes(campaign.id);
    const requiredProfile = campaign.id === "fallout1"
      ? fallout1Profile
      : campaign.id === "fallout2"
        ? fallout2Profile
      : campaign.id === "newvegas"
        ? newVegasProfile
      : campaign.id === "fallout3"
        ? fallout3Profile
      : campaign.id === "ttw"
        ? ttwProfile
        : null;
    const profileReady = !profileRequired || Boolean(requiredProfile?.ready);
    const runtimePresentations = selectedVariant.presentations ?? {};
    const presentations = (campaign.presentations || []).filter(
      (id) => runtimePresentations[id]?.ready === true);
    const ready = Boolean(selectedVariant.ready) && profileReady && presentations.length > 0;
    return {
      ...campaign,
      presentations,
      ready,
      readiness: ready
        ? "Ready in the installed runtime."
        : (profileRequired && selectedVariant.ready
          ? (requiredProfile?.message || CONTRACT.copy.readinessUnavailable)
          : (requiredProfile?.validated && requiredProfile?.message
            ? requiredProfile.message
            : (selectedVariant.message || CONTRACT.copy.readinessUnavailable))),
      jamReady: Boolean(campaign.jam && jam?.ready && jamProfile?.ready),
      jamReadiness: jamProfile?.message || jam?.message || CONTRACT.copy.jamProfileUnavailable,
      unavailableDlc: Array.isArray(selectedVariant.unavailableDlc) ? selectedVariant.unavailableDlc : []
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
  if (request?.enableVr && !campaign.openXr) {
    throw new Error(CONTRACT.copy.openXrUnavailable);
  }
  const presentation = String(
    request?.presentation ||
    (request?.enableVr ? "openxr" : campaign.defaultPresentation || "flat"));
  if (Array.isArray(campaign.presentations) && !campaign.presentations.includes(presentation)) {
    throw new Error(CONTRACT.copy.invalidPresentation);
  }
  return {
    campaign,
    enableJam: Boolean(request?.enableJam),
    enableVr: presentation === "openxr",
    presentation
  };
}

export function createRuntimeArguments(
  { campaign, enableJam, enableVr, presentation },
  {
    fallout1Profile = null,
    fallout2Profile = null,
    fallout3Profile = null,
    newVegasProfile = null,
    ttwProfile = null,
    jamProfile = null
  } = {}
) {
  if (campaign.id === "fallout1") {
    if (!fallout1Profile?.ready) throw new Error(CONTRACT.copy.fallout1ProfileUnavailable);
    if (!["forward_plus", "mobile", "gl_compatibility"].includes(campaign.desktopRenderingMethod)) {
      throw new Error("Fallout 1 has no valid desktop rendering method.");
    }
    return [
      "--xr-mode", "off",
      "--rendering-method", campaign.desktopRenderingMethod,
      "--",
      "--fo1-hex-scene", fallout1Profile.hexScene,
      "--fo1-new-game",
      "--fo1-character-start", fallout1Profile.characterStart,
      "--fo1-character-start-sha256", fallout1Profile.characterStartSha256,
      "--fo1-start-presentation", presentation,
      "--save-path", fallout1Profile.savePath
    ];
  }
  if (campaign.id === "fallout2") {
    if (!fallout2Profile?.ready) throw new Error(CONTRACT.copy.fallout2ProfileUnavailable);
    if (presentation !== "hex-tactical") throw new Error(CONTRACT.copy.invalidPresentation);
    return [
      "--xr-mode", "off",
      "--windowed",
      "--resolution", "1280x720",
      "res://src/Campaigns/Fallout2/CharacterStart/Fo2CharacterStart.tscn",
      "--",
      "--fo2-temple-cache", fallout2Profile.templeCache,
      "--fo2-temple-transitions", fallout2Profile.templeTransitions,
      "--fo2-arroyo-cache", fallout2Profile.arroyoCache,
      "--fo2-player-cache", fallout2Profile.playerCache,
      "--fo2-character-start-cache", fallout2Profile.characterStartCache,
      "--fo2-save", fallout2Profile.savePath
    ];
  }
  if (campaign.id === "fallout3") {
    if (!fallout3Profile?.ready) throw new Error(CONTRACT.copy.fallout3ProfileUnavailable);
    return [
      "--xr-mode", "off", "--",
      "--fo3-profile", fallout3Profile.path,
      "--save-path", fallout3Profile.savePath
    ];
  }
  if (campaign.id === "newvegas") {
    if (!newVegasProfile?.ready || !newVegasProfile?.cacheRoot || !newVegasProfile?.savePath) {
      throw new Error("The New Vegas owned-data profile is unavailable.");
    }
    const args = [
      "--xr-mode", enableVr ? "on" : "off", "--",
      "--reuse-cache",
      "--cache-root", newVegasProfile.cacheRoot,
      "--opening-menu",
      "--save-path", newVegasProfile.savePath
    ];
    if (enableJam) {
      if (!jamProfile?.ready) throw new Error(CONTRACT.copy.jamProfileUnavailable);
      args.push("--enable-jam", "--jam-profile", jamProfile.path);
    }
    if (enableVr) args.push("--vr");
    return args;
  }
  if (!ttwProfile?.ready) throw new Error(CONTRACT.copy.ttwProfileUnavailable);
  const args = [
    "--xr-mode", enableVr ? "on" : "off", "--",
    "--campaign", campaign.engineCampaign,
    "--ttw-profile", ttwProfile.path,
    "--save-path", ttwProfile.savePath
  ];
  if (enableJam) {
    if (!jamProfile?.ready) throw new Error(CONTRACT.copy.jamProfileUnavailable);
    args.push("--enable-jam", "--jam-profile", jamProfile.path);
  }
  if (enableVr) args.push("--vr");
  return args;
}
