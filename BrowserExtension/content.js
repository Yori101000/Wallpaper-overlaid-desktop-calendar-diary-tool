// 内容脚本 - 处理划线高亮和文本提取

// 已保存的高亮标记颜色
const HIGHLIGHT_COLOR = "#FFEB3B";
const HIGHLIGHT_HOVER_COLOR = "#FFD600";

// 太短的文本不恢复高亮：像"的""the"这种会在整页命中几十处，把页面刷成一片黄。
const MIN_RESTORE_LENGTH = 8;

// 页面加载后恢复已保存的高亮（从 storage 读取）
restoreHighlights();

// 监听来自 background 的消息
chrome.runtime.onMessage.addListener((request, sender, sendResponse) => {
  if (request.action === "saveHighlight") {
    const result = highlightSelection();
    sendResponse(result);
  } else if (request.action === "getSelectionText") {
    sendResponse({ text: window.getSelection()?.toString() || "" });
  }
  return true;
});

// 注意：不要在这里监听 Ctrl+Shift+S。快捷键由 manifest 的 commands 在浏览器层处理，
// 页面侧再监听只会白白 preventDefault 掉所有网站上的这个组合键。

// 核心：划线高亮选中文本
function highlightSelection() {
  const selection = window.getSelection();
  if (!selection || selection.isCollapsed || !selection.toString().trim()) {
    return { success: false, error: "没有选中任何文本" };
  }

  const selectedText = selection.toString().trim();
  const range = selection.getRangeAt(0);

  try {
    // 创建高亮 span
    const highlightSpan = document.createElement("span");
    highlightSpan.style.backgroundColor = HIGHLIGHT_COLOR;
    highlightSpan.style.color = "#000000";
    highlightSpan.style.borderRadius = "2px";
    highlightSpan.style.padding = "0 1px";
    highlightSpan.style.cursor = "pointer";
    highlightSpan.title = "已保存到透明日历";
    highlightSpan.dataset.highlighted = "true";

    // 包裹选中文本
    range.surroundContents(highlightSpan);

    // 点击高亮区域可移除此高亮
    highlightSpan.addEventListener("click", (e) => {
      e.stopPropagation();
      const parent = highlightSpan.parentNode;
      if (parent) {
        const textNode = document.createTextNode(highlightSpan.textContent || "");
        parent.replaceChild(textNode, highlightSpan);
        // 从存储中移除
        removeHighlightFromStorage(getPageKey(), textNode.textContent || "");
      }
    });

    // 保存高亮到 storage
    saveHighlightToStorage(getPageKey(), {
      text: selectedText,
      url: location.href,
      title: document.title,
      timestamp: Date.now()
    });

    selection.removeAllRanges();

    return {
      success: true,
      text: selectedText,
      url: location.href,
      title: document.title
    };
  } catch (e) {
    // surroundContents 可能在某些复杂选区失败，回退到仅提取文本
    return {
      success: true,
      text: selectedText,
      url: location.href,
      title: document.title
    };
  }
}

// 生成页面唯一标识
function getPageKey() {
  return location.hostname + location.pathname;
}

// 保存高亮到 chrome.storage
function saveHighlightToStorage(pageKey, highlightData) {
  chrome.storage.local.get(["highlights"], (result) => {
    const allHighlights = result.highlights || {};
    if (!allHighlights[pageKey]) {
      allHighlights[pageKey] = [];
    }
    // 避免重复
    const exists = allHighlights[pageKey].some(h => h.text === highlightData.text);
    if (!exists) {
      allHighlights[pageKey].push(highlightData);
      chrome.storage.local.set({ highlights: allHighlights });
    }
  });
}

// 从 storage 移除高亮
function removeHighlightFromStorage(pageKey, text) {
  chrome.storage.local.get(["highlights"], (result) => {
    const allHighlights = result.highlights || {};
    if (allHighlights[pageKey]) {
      allHighlights[pageKey] = allHighlights[pageKey].filter(h => h.text !== text);
      chrome.storage.local.set({ highlights: allHighlights });
    }
  });
}

// 恢复页面高亮（页面重开时）
function restoreHighlights() {
  const pageKey = getPageKey();
  chrome.storage.local.get(["highlights"], (result) => {
    const allHighlights = result.highlights || {};
    const pageHighlights = allHighlights[pageKey] || [];

    // 本页没有记录就直接返回，不做任何 DOM 遍历 —— 内容脚本注入在 <all_urls>，
    // 绝大多数页面都会走到这一行。
    if (pageHighlights.length === 0) return;

    const targets = pageHighlights
      .map(h => h.text)
      .filter(text => text && text.length >= MIN_RESTORE_LENGTH);

    if (targets.length === 0) return;

    // 一次遍历处理全部待恢复文本，而不是每条各遍历一遍整页
    highlightTextsInBody(targets);
  });
}

// 在页面 body 中查找文本并高亮（每条只高亮首个匹配）
function highlightTextsInBody(texts) {
  const remaining = new Set(texts);
  const treeWalker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT, null, false);

  const pending = [];
  while (treeWalker.nextNode() && remaining.size > 0) {
    const node = treeWalker.currentNode;
    const content = node.textContent;
    if (!content) continue;

    for (const text of remaining) {
      const idx = content.indexOf(text);
      if (idx === -1) continue;

      pending.push({ node, idx, text });
      // 每条摘录只高亮第一处命中，避免同一句话在页面里出现多次时满屏泛黄
      remaining.delete(text);
      break;
    }
  }

  // 遍历结束后再改 DOM —— 边遍历边 surroundContents 会让 TreeWalker 的游标失效
  pending.forEach(({ node, idx, text }) => {
    try {
      const range = document.createRange();
      range.setStart(node, idx);
      range.setEnd(node, idx + text.length);

      const span = document.createElement("span");
      span.style.backgroundColor = HIGHLIGHT_COLOR;
      span.style.color = "#000000";
      span.style.borderRadius = "2px";
      span.style.padding = "0 1px";
      span.style.cursor = "pointer";
      span.dataset.highlighted = "true";
      span.title = "已保存到透明日历";

      range.surroundContents(span);
    } catch (e) {
      // 跨元素边界的选区无法 surroundContents，跳过
    }
  });
}
