import { createOfflineState } from "../contract.mjs";

const api = window.openNevada ?? {
  getState: async () => createOfflineState(),
  chooseRuntime: async () => ({ ok: false, message: "The launcher preview cannot choose a local runtime." }),
  launch: async () => ({ ok: false, message: "The launcher preview is not connected to a local runtime." }),
  openExternal: async () => undefined
};

let state = await api.getState();
let selectedId = "newvegas";

const campaignContainer = document.querySelector("#campaigns");
const statusElement = document.querySelector("#runtime-status");
const selectionTitle = document.querySelector("#selection-title");
const selectionDetail = document.querySelector("#selection-detail");
const jamRow = document.querySelector("#jam-toggle-row");
const jamToggle = document.querySelector("#jam-toggle");
const vrRow = document.querySelector("#vr-toggle-row");
const vrToggle = document.querySelector("#vr-toggle");
const launchButton = document.querySelector("#launch");
const toast = document.querySelector("#toast");

function selectedCampaign() {
  return state.campaigns.find((campaign) => campaign.id === selectedId) ?? state.campaigns[0];
}

function showToast(message, kind = "info") {
  toast.textContent = message;
  toast.dataset.kind = kind;
  toast.classList.add("show");
  window.clearTimeout(showToast.timer);
  showToast.timer = window.setTimeout(() => toast.classList.remove("show"), 5200);
}

function statusLabel(runtime) {
  if (runtime.status === "connected") return "Runtime connected";
  if (runtime.status === "ready") return "Godot runtime ready";
  if (runtime.status === "experimental") return "Experimental Godot runtime";
  if (runtime.status === "development-slice") return "Godot development slice";
  if (runtime.status === "portable-shell") return "Cross-platform shell";
  return "Godot runtime not selected";
}

function renderCampaigns() {
  campaignContainer.innerHTML = state.campaigns.map((campaign, index) => `
    <button class="campaign-card route-${campaign.id} ${campaign.id === selectedId ? "selected" : ""}" type="button" data-campaign="${campaign.id}">
      <span class="card-top"><span class="card-number">0${index + 1}</span><span class="card-eyebrow">${campaign.eyebrow}</span></span>
      <strong class="card-title">${campaign.title}</strong>
      <span class="card-character">${campaign.character}</span>
      <span class="card-detail">${campaign.detail}</span>
      <span class="route-rule">${campaign.ttw ? "Character path / choose at creation" : campaign.jam ? "Base route / JAM can join later" : "Standalone vanilla route"}</span>
      <span class="card-footer"><i class="ready-dot ${campaign.ready ? "ready" : ""}"></i>${campaign.ready ? "Runtime ready" : campaign.readiness}</span>
    </button>
  `).join("");
  campaignContainer.querySelectorAll("[data-campaign]").forEach((element) => {
    element.addEventListener("click", () => {
      selectedId = element.dataset.campaign;
      jamToggle.checked = false;
      vrToggle.checked = false;
      render();
    });
  });
}

function renderLayers() {
  document.querySelector("#extender-layers").innerHTML = state.extenderLayers.map((layer) => `
    <div class="layer-row"><span class="status-pill ${layer.status}">${layer.status.replace("-", " ")}</span><div><strong>${layer.title}</strong><p>${layer.detail}</p></div></div>
  `).join("");
  document.querySelector("#mods").innerHTML = state.mods.map((mod) => `
    <div class="module-row"><div><strong>${mod.title}</strong><p>${mod.detail}</p></div><span class="module-status">${mod.status.replaceAll("-", " ")}</span></div>
  `).join("");
}

function render() {
  const campaign = selectedCampaign();
  const jamAvailable = Boolean(campaign.jam && campaign.jamReady);
  const vrAvailable = Boolean(campaign.ready && state.runtime.openXrLaunchable);
  document.querySelector("#campaign-rule").textContent = state.campaignRule;
  document.querySelector("#jam-rule").textContent = state.jamRule;
  statusElement.textContent = statusLabel(state.runtime);
  statusElement.dataset.status = state.runtime.status;
  selectionTitle.textContent = campaign.title;
  selectionDetail.textContent = campaign.detail;
  jamRow.classList.toggle("disabled", !jamAvailable);
  jamToggle.disabled = !jamAvailable;
  if (!jamAvailable) jamToggle.checked = false;
  vrRow.classList.toggle("disabled", !vrAvailable);
  vrToggle.disabled = !vrAvailable;
  if (!vrAvailable) vrToggle.checked = false;
  const routeLaunchable = Boolean(state.runtime.canLaunch && campaign.ready);
  launchButton.disabled = !routeLaunchable;
  launchButton.title = routeLaunchable ? "Launch this path" : (campaign.readiness || state.runtime.label);
  document.querySelector("#platform-label").textContent = `${state.runtime.platform.toUpperCase()} / ${state.runtime.source.toUpperCase()}`;
  renderCampaigns();
  renderLayers();
}

launchButton.addEventListener("click", async () => {
  const result = await api.launch({
    campaign: selectedCampaign().id,
    enableJam: jamToggle.checked,
    enableVr: vrToggle.checked
  });
  showToast(result.message, result.ok ? "success" : "warning");
});

document.querySelector("#mod-guide").addEventListener("click", () => {
  document.querySelector("#mod-support").scrollIntoView({ behavior: "smooth", block: "start" });
});

document.querySelector("#choose-runtime").addEventListener("click", async () => {
  const result = await api.chooseRuntime();
  showToast(result.message, result.ok ? "success" : "warning");
  if (result.ok) {
    state = await api.getState();
    render();
  }
});

document.querySelector("#open-mod-docs").addEventListener("click", async () => {
  try {
    await api.openExternal("https://github.com/nikamigaming-create/OpenNV/blob/main/docs/mods.md");
  } catch {
    showToast("The support policy is available in docs/mods.md in the OpenNV release.", "info");
  }
});

render();
