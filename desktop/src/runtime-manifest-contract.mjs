export const RUNTIME_MANIFEST_SCHEMA = "opennv-runtime-manifest/v2";

const PRESENTATIONS = new Set(["hex-tactical", "first-person", "openxr"]);

export function validateRuntimeManifest(manifest) {
  if (manifest?.schema !== RUNTIME_MANIFEST_SCHEMA ||
      typeof manifest.runtime?.canLaunch !== "boolean" || !Array.isArray(manifest.campaigns)) {
    throw new Error("The selected runtime has an unsupported manifest.");
  }
  const identities = new Set();
  for (const campaign of manifest.campaigns) {
    const id = typeof campaign?.id === "string" ? campaign.id.toLowerCase() : "";
    if (!id || identities.has(id) || typeof campaign.launchable !== "boolean" ||
        typeof campaign.status !== "string") {
      throw new Error("The runtime campaign declarations are invalid or duplicated.");
    }
    identities.add(id);
    if (!campaign.presentations || typeof campaign.presentations !== "object" ||
        Array.isArray(campaign.presentations)) {
      throw new Error(`The runtime must declare ${campaign.id}'s presentation routes.`);
    }
    for (const [mode, route] of Object.entries(campaign.presentations)) {
      if (!PRESENTATIONS.has(mode) || typeof route?.launchable !== "boolean" ||
          typeof route.previewOnly !== "boolean" || typeof route.status !== "string") {
        throw new Error(`The runtime presentation ${campaign.id}/${mode} is invalid.`);
      }
    }
    if (campaign.launchable && !Object.values(campaign.presentations).some((route) => route.launchable)) {
      throw new Error(`${campaign.id} has no launchable presentation route.`);
    }
  }
  return manifest;
}

// The campaign cards and the launch action resolve the same installed contract.
// Missing campaigns remain in the launcher catalog with an unavailable status.
export function resolveRuntimeCampaign(manifest, campaign) {
  const entry = manifest.campaigns.find((candidate) =>
    candidate.id.toLowerCase() === campaign.engineCampaign.toLowerCase());
  const modes = [...new Set([...(campaign.presentations ?? []), ...(campaign.pendingPresentations ?? [])])];
  const presentations = Object.fromEntries(modes.map((mode) => {
    const route = entry?.presentations[mode];
    return [mode, {
      launchable: manifest.runtime.canLaunch && entry?.launchable === true &&
        route?.launchable === true && (mode !== "openxr" ||
          (campaign.openXr && manifest.runtime.presentationModes?.openxr?.launchable === true)),
      previewOnly: route?.previewOnly === true,
      status: route?.status || `${campaign.title} ${mode} is unavailable in this runtime.`
    }];
  }));
  return {
    launchable: Object.values(presentations).some((route) => route.launchable),
    status: entry?.status || `${campaign.title} is missing from this runtime; its registration is retained.`,
    presentations,
    unavailableDlc: entry?.unavailableDlc ?? []
  };
}
