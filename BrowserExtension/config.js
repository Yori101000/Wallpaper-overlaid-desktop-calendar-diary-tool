// 端口配置与探测 —— background.js 与 popup.js 共用（ES module）。
//
// 交叉引用：桌面端的起始端口定义在 Services/NoteListenerService.cs 的 DefaultPort，
// 回退次数为 PortAttempts。C# 与 JS 属于两个运行时，无法共享同一份常量，
// 改动时**必须两边同步**。
export const PORT_START = 51999;
export const PORT_COUNT = 10;

/** 探测桌面端是否在某端口监听。 */
export async function pingPort(port) {
  try {
    const response = await fetch(`http://localhost:${port}/save`, {
      method: "OPTIONS",
      signal: AbortSignal.timeout(600)
    });
    return response.status === 204 || response.ok;
  } catch {
    return false;
  }
}

/** 依次探测整个端口区间，命中后写入 storage 供下次优先复用。返回 null 表示桌面端未运行。 */
export async function discoverPort() {
  const stored = (await chrome.storage.local.get(["port"])).port;
  if (stored && (await pingPort(stored))) {
    return stored;
  }

  for (let offset = 0; offset < PORT_COUNT; offset++) {
    const port = PORT_START + offset;
    if (await pingPort(port)) {
      await chrome.storage.local.set({ port });
      return port;
    }
  }

  return null;
}
