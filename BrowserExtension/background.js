// Background Service Worker
// 桌面端在 51999 被占用时会依次回退到后面的端口，端口探测见 config.js。
import { discoverPort, pingPort } from "./config.js";

let cachedPort = null;

// 创建右键菜单
chrome.runtime.onInstalled.addListener(() => {
  chrome.contextMenus.create({
    id: "saveHighlight",
    title: "划线保存到透明日历",
    contexts: ["selection"]
  });

  chrome.contextMenus.create({
    id: "saveHighlightNoMark",
    title: "保存选中文本到日历（不高亮）",
    contexts: ["selection"]
  });
});

// 右键菜单点击处理
chrome.contextMenus.onClicked.addListener((info, tab) => {
  if (info.menuItemId === "saveHighlight" || info.menuItemId === "saveHighlightNoMark") {
    const withHighlight = info.menuItemId === "saveHighlight";
    handleSaveHighlight(tab, withHighlight);
  }
});

// 键盘快捷键处理（由 chrome.commands 提供，页面侧无需再监听按键）
chrome.commands.onCommand.addListener((command) => {
  if (command === "save-highlight") {
    chrome.tabs.query({ active: true, currentWindow: true }, (tabs) => {
      if (tabs[0]) {
        handleSaveHighlight(tabs[0], true);
      }
    });
  }
});

// 优先复用上次命中的端口，失效时重新探测整个区间
async function resolvePort() {
  if (cachedPort !== null && (await pingPort(cachedPort))) {
    return cachedPort;
  }

  cachedPort = await discoverPort();
  return cachedPort;
}

// 主处理逻辑
async function handleSaveHighlight(tab, withHighlight) {
  try {
    // 先获取选中文本
    const results = await chrome.scripting.executeScript({
      target: { tabId: tab.id },
      func: () => {
        const sel = window.getSelection();
        if (sel && !sel.isCollapsed) {
          return sel.toString().trim();
        }
        return null;
      }
    });

    const text = results?.[0]?.result;
    if (!text) {
      await showNotification(tab.id, "请先选中文字再保存");
      return;
    }

    const port = await resolvePort();
    if (port === null) {
      await showNotification(tab.id, "❌ 连接失败，请确认透明日历已启动");
      return;
    }

    // 通过 content script 做高亮
    if (withHighlight) {
      try {
        await chrome.tabs.sendMessage(tab.id, { action: "saveHighlight" });
      } catch {
        // 内容脚本未注入（如 chrome:// 页面）时跳过高亮，仍然保存文本。
      }
    }

    const response = await fetch(`http://localhost:${port}/save`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        url: tab.url,
        title: tab.title || "",
        text: text
      })
    });

    if (response.ok) {
      await showNotification(tab.id, "✅ 已保存到透明日历");
      chrome.storage.local.set({ lastSaved: { text, url: tab.url, title: tab.title, time: Date.now() } });
    } else {
      await showNotification(tab.id, `❌ 保存失败（HTTP ${response.status}）`);
    }
  } catch (e) {
    try {
      await showNotification(tab.id, "❌ 连接失败，请确认透明日历已启动");
    } catch {}
  }
}

// 显示通知（使用 content script 弹 toast）
async function showNotification(tabId, message) {
  try {
    await chrome.scripting.executeScript({
      target: { tabId },
      func: (msg) => {
        // 移除旧 toast
        const old = document.getElementById("transparent-calendar-toast");
        if (old) old.remove();

        const toast = document.createElement("div");
        toast.id = "transparent-calendar-toast";
        toast.textContent = msg;
        toast.style.cssText = `
          position: fixed; top: 20px; right: 20px; z-index: 2147483647;
          background: #333; color: white; padding: 12px 20px;
          border-radius: 8px; font-size: 14px; font-family: sans-serif;
          box-shadow: 0 4px 12px rgba(0,0,0,0.3); max-width: 300px;
          transition: opacity 0.3s;
        `;
        document.body.appendChild(toast);
        setTimeout(() => { toast.style.opacity = "0"; setTimeout(() => toast.remove(), 300); }, 2000);
      },
      args: [message]
    });
  } catch {}
}
