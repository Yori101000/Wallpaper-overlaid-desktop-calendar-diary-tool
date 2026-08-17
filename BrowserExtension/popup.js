// popup.js - 弹窗逻辑

// 端口探测见 config.js（与 background.js 共用）
import { discoverPort } from "./config.js";

// 检测桌面应用是否在线
async function checkAppStatus() {
  const statusEl = document.getElementById("appStatus");
  const port = await discoverPort();

  if (port === null) {
    statusEl.textContent = "🔴 未连接";
    statusEl.className = "status-value offline";
    return;
  }

  statusEl.textContent = `🟢 运行中（端口 ${port}）`;
  statusEl.className = "status-value online";
}

// 显示最近保存记录
function showLastSaved() {
  chrome.storage.local.get(["lastSaved"], (result) => {
    const container = document.getElementById("lastSavedContent");
    const saved = result.lastSaved;
    if (!saved || !saved.text) {
      container.innerHTML = '<div class="empty">暂无保存记录</div>';
      return;
    }
    const time = new Date(saved.time);
    container.innerHTML = `
      <div class="text">${escapeHtml(saved.text.substring(0, 80))}</div>
      <div class="text" style="font-size:12px;color:#888;margin-top:4px">${escapeHtml(saved.title || saved.url)}</div>
      <div class="time">${time.toLocaleString("zh-CN")}</div>
    `;
  });
}

function escapeHtml(str) {
  const div = document.createElement("div");
  div.textContent = str;
  return div.innerHTML;
}

// 初始化
document.addEventListener("DOMContentLoaded", () => {
  checkAppStatus();
  showLastSaved();
});
