import { contextBridge, ipcRenderer } from "electron";

contextBridge.exposeInMainWorld("openNevada", {
  getState: () => ipcRenderer.invoke("opennv:get-state"),
  chooseRuntime: () => ipcRenderer.invoke("opennv:choose-runtime"),
  launch: (request) => ipcRenderer.invoke("opennv:launch", request),
  openExternal: (url) => ipcRenderer.invoke("opennv:open-external", url)
});
