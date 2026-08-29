const { contextBridge, ipcRenderer } = require("electron");

contextBridge.exposeInMainWorld("openNevada", {
  getState: () => ipcRenderer.invoke("opennv:get-state"),
  chooseRuntime: () => ipcRenderer.invoke("opennv:choose-runtime"),
  chooseFo1Profile: () => ipcRenderer.invoke("opennv:choose-fo1-profile"),
  chooseFo2Profile: () => ipcRenderer.invoke("opennv:choose-fo2-profile"),
  chooseNewVegasCache: () => ipcRenderer.invoke("opennv:choose-newvegas-cache"),
  chooseTtwProfile: () => ipcRenderer.invoke("opennv:choose-ttw-profile"),
  chooseJamProfile: () => ipcRenderer.invoke("opennv:choose-jam-profile"),
  launch: (request) => ipcRenderer.invoke("opennv:launch", request),
  openExternal: (url) => ipcRenderer.invoke("opennv:open-external", url)
});
