// Background Service Worker
const LOCAL_API = "http://localhost:51999/save";

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

// 键盘快捷键处理
chrome.commands.onCommand.addListener((command) => {
  if (command === "save-highlight") {
    chrome.tabs.query({ active: true, currentWindow: true }, (tabs) => {
      if (tabs[0]) {
        handleSaveHighlight(tabs[0], true);
      }
    });
  }
});

// 工具栏按钮点击处理
chrome.action.onClicked.addListener((tab) => {
  handleSaveHighlight(tab, true);
});

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
      // 通知用户没有选中文本
      await showNotification(tab.id, "请先选中文字再保存");
      return;
    }

    // 如果有划线要求，先划线
    if (withHighlight) {
      await chrome.scripting.executeScript({
        target: { tabId: tab.id },
        func: () => {
          // 调用 content.js 中的 highlightSelection
          // 直接发消息让 content 处理
        }
      });

      // 通过 content script 做高亮
      await chrome.tabs.sendMessage(tab.id, { action: "saveHighlight" });
    }

    // 发送数据到本地应用
    const data = {
      url: tab.url,
      title: tab.title || "",
      text: text
    };

    const response = await fetch(LOCAL_API, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(data)
    });

    if (response.ok) {
      await showNotification(tab.id, "✅ 已保存到透明日历");
      // 更新 popup 显示
      chrome.storage.local.set({ lastSaved: { text, url: tab.url, title: tab.title, time: Date.now() } });
    } else {
      await showNotification(tab.id, "❌ 保存失败，桌面应用是否在运行？");
    }
  } catch (e) {
    // 可能 fetch 失败（应用没在运行）
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
