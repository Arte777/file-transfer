// ── Общие утилиты для статического сайта ──────────────────────────────────────
const IMG = ["jpg","jpeg","png","gif","webp","bmp","svg","avif"];
const VID = ["mp4","webm","mov","avi"];
const AUD = ["mp3","wav","flac","ogg","m4a"];
const TXT = ["txt","md","js","ts","html","css","cs","py","json","cpp","c","java"];

// Системные файлы, которые нужно скрывать из списка
const HIDDEN_FILES = ["settings.json","_metadata.json","metadata.json",".gitkeep",".DS_Store","thumbs.db"];

function ext(n) { return n.split(".").pop().toLowerCase(); }
function isImg(n) { return IMG.includes(ext(n)); }
function isText(n) { return TXT.includes(ext(n)); }
function isVid(n) { return VID.includes(ext(n)); }
function isAud(n) { return AUD.includes(ext(n)); }
function isHiddenFile(n) { return HIDDEN_FILES.includes(n.toLowerCase()); }

function icon(n) {
  const e = ext(n);
  if (isImg(n)) return "🖼️";
  if (isVid(n)) return "🎥";
  if (isAud(n)) return "🎵";
  if (e === "pdf") return "📄";
  if (["zip","rar","7z"].includes(e)) return "🗜️";
  if (isText(n)) return "📝";
  return "📁";
}

function fmtSize(b) {
  if (b < 1024) return b + " B";
  if (b < 1048576) return (b / 1024).toFixed(1) + " KB";
  return (b / 1048576).toFixed(1) + " MB";
}

function fmtDate(s) {
  if (!s) return "—";
  return new Date(s).toLocaleString("ru", { day: "2-digit", month: "short", hour: "2-digit", minute: "2-digit", second: "2-digit" });
}

// ── Auth (token в localStorage) ───────────────────────────────────────────────
function getToken() { return localStorage.getItem('ft_token') || ''; }
function getUser()  { return localStorage.getItem('ft_user')  || ''; }
function setAuth(token, user) {
  localStorage.setItem('ft_token', token);
  localStorage.setItem('ft_user', user);
}
function clearAuth() {
  localStorage.removeItem('ft_token');
  localStorage.removeItem('ft_user');
}

// Редирект на логин, если нет токена
function requireLogin() {
  if (!getToken()) {
    location.href = 'login.html';
    return false;
  }
  return true;
}

// fetch с подставленным API_BASE и Authorization-заголовком
async function apiFetch(path, opts = {}) {
  opts.headers = opts.headers || {};
  const token = getToken();
  if (token) opts.headers['Authorization'] = 'Bearer ' + token;

  try {
    const resp = await fetch(API_BASE + path, opts);

    if (resp.status === 401 || resp.status === 403) {
      clearAuth();
      location.href = 'login.html';
      throw new Error('auth');
    }
    return resp;
  } catch (err) {
    if (err.message === 'auth') throw err;
    throw err;
  }
}

// Абсолютный URL для статики сервера (превью /uploads/...)
function assetUrl(path) { return API_BASE + path; }

// ── UI-помощники ──────────────────────────────────────────────────────────────
function toast(msg, type = "ok") {
  const t = document.getElementById("toast");
  if (!t) return;
  t.textContent = msg;
  t.className = "show " + type;
  setTimeout(function() { t.className = ""; }, 3000);
}

function playChime() {
  try {
    const ctx = new (window.AudioContext || window.webkitAudioContext)();
    const now = ctx.currentTime;
    const osc1 = ctx.createOscillator();
    const gain1 = ctx.createGain();
    osc1.type = "sine";
    osc1.frequency.setValueAtTime(783.99, now);
    osc1.frequency.exponentialRampToValueAtTime(1046.5, now + 0.15);
    gain1.gain.setValueAtTime(0.12, now);
    gain1.gain.exponentialRampToValueAtTime(0.001, now + 0.5);
    osc1.connect(gain1); gain1.connect(ctx.destination);
    const osc2 = ctx.createOscillator();
    const gain2 = ctx.createGain();
    osc2.type = "sine";
    osc2.frequency.setValueAtTime(1318.51, now + 0.08);
    gain2.gain.setValueAtTime(0.08, now + 0.08);
    gain2.gain.exponentialRampToValueAtTime(0.001, now + 0.6);
    osc2.connect(gain2); gain2.connect(ctx.destination);
    osc1.start(now); osc1.stop(now + 0.5);
    osc2.start(now + 0.08); osc2.stop(now + 0.6);
  } catch (e) { console.error("Audio error:", e); }
}

function escapeHtml(s) {
  return String(s == null ? '' : s)
    .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;").replace(/'/g, "&#39;");
}

// ── Аватарка оператора (поддержка фото + emoji) ──────────────────────────────
function operatorAvatarHTML(user) {
  const avatarImage = localStorage.getItem('ft_avatarImage');
  if (avatarImage) {
    return '<img src="' + avatarImage + '" alt="avatar">';
  }
  const emoji = localStorage.getItem('ft_avatar');
  if (emoji) return escapeHtml(emoji);
  return user === 'Shonll' ? '🦊' : '🐉';
}

// Отображаемое имя (с поддержкой кастомных настроек)
function operatorDisplayName(user) {
  const custom = localStorage.getItem('ft_displayName');
  if (custom) return custom;
  return user;
}

// ── Акцентный цвет — глобальное применение ────────────────────────────────────
function applyAccentColor(color) {
  if (!color) color = localStorage.getItem('ft_themeColor') || '#6366f1';
  const root = document.documentElement;
  root.style.setProperty('--accent', color);

  // Вычисляем производные цвета
  const r = parseInt(color.slice(1, 3), 16);
  const g = parseInt(color.slice(3, 5), 16);
  const b = parseInt(color.slice(5, 7), 16);
  root.style.setProperty('--accent-soft', 'rgba(' + r + ',' + g + ',' + b + ', 0.12)');
  root.style.setProperty('--accent-glow', 'rgba(' + r + ',' + g + ',' + b + ', 0.2)');

  // Светлая версия акцента для текста
  const lr = Math.min(255, r + 50);
  const lg = Math.min(255, g + 50);
  const lb = Math.min(255, b + 50);
  root.style.setProperty('--accent-text', 'rgb(' + lr + ',' + lg + ',' + lb + ')');

  localStorage.setItem('ft_themeColor', color);
}

// Применяем акцент при загрузке каждой страницы
applyAccentColor();

// ── UI Components ──────────────────────────────────────────────────────────────
function renderHeader(activePage) {
  const user = getUser();
  const avatarHtml = operatorAvatarHTML(user);
  const name = operatorDisplayName(user);

  function navLink(page, href, icon, label) {
    const cls = activePage === page ? 'nav-link active' : 'nav-link';
    return '<a href="' + href + '" class="' + cls + '">' + icon + ' ' + label + '</a>';
  }

  // Generate unique ID for chat initialization
  const chatHtmlId = `nexus_chat_${Math.floor(Math.random()*10000)}`;

  // Attach init logic to window to call after DOM is ready
  window.initSidebarChat = function() {
    const container = document.getElementById(chatHtmlId);
    if(!container || container.dataset.initialized) return;
    container.dataset.initialized = 'true';
    initNexusAI(container);
  };
  setTimeout(() => window.initSidebarChat(), 100);

  return `<aside class="sidebar">
    <div class="logo">
      <span class="logo-text">NEXUS</span>
    </div>
    <div class="nav-links">
      ${navLink('files', 'index.html', '', 'Дашборд')}
      ${navLink('tokens', 'tokens.html', '', 'Токены')}
      ${navLink('settings', 'settings.html', '', 'Настройки')}
    </div>
    
    <!-- AI Assistant Widget -->
    <div class="sidebar-chat-wrapper" id="${chatHtmlId}">
      <div class="sidebar-chat-header">
        <span class="chat-status-dot"></span>
        <span>NEXUS AI</span>
      </div>
      <div class="sidebar-chat-messages">
        <div class="chat-msg ai-msg">Система готова. Чем могу помочь, Админ?</div>
      </div>
      <div class="sidebar-chat-input-wrapper">
        <input type="text" class="sidebar-chat-input" placeholder="Спросить ИИ...">
        <button class="sidebar-chat-send">></button>
      </div>
    </div>

    <div class="user-badge">
      <span class="user-avatar">${avatarHtml}</span>
      <div class="user-info">
        <span class="user-name">${escapeHtml(name)}</span>
        <button class="btn-logout" id="btnLogout">Выйти</button>
      </div>
  </div>
</aside>`;
}

async function bindLogout() {
  const btn = document.getElementById('btnLogout');
  if (!btn) return;
  btn.addEventListener('click', async function(e) {
    e.preventDefault();
    try { await apiFetch('/api/logout', { method: 'POST' }); } catch (_) {}
    clearAuth();
    // Очищаем "Запомнить меня" при ручном выходе
    localStorage.removeItem('ft_remember');
    localStorage.removeItem('ft_savedUser');
    localStorage.removeItem('ft_savedPass');
    location.href = 'login.html';
  });
}

// ── AI Assistant Logic (NEXUS AI) ──────────────────────────────────────────────
function initNexusAI(container) {
  const input = container.querySelector('.sidebar-chat-input');
  const sendBtn = container.querySelector('.sidebar-chat-send');
  const messagesDiv = container.querySelector('.sidebar-chat-messages');

  const API_KEY = 'fe_oa_cbaca536b7607f971ecf244619b38d5684aeba85b26e1ed3';
  
  // Keep chat history in memory
  let chatHistory = [
    {
      role: "system",
      content: "Ты — NEXUS AI, личный встроенный помощник администратора системы NEXUS (панель управления логами, файлами и Robux Drainer). Отвечай кратко, стильно, в киберпанк/хакерском стиле. Твоя задача — помогать по функционалу сайта. ТЫ НЕ УМЕЕШЬ КОДИТЬ. Если тебя просят написать скрипт, код или взломать что-то реальное, жестко отвечай, что твоя специализация — только управление дашбордом NEXUS и ты не программист."
    }
  ];

  function addMessage(text, isAi) {
    const msg = document.createElement('div');
    msg.className = `chat-msg ${isAi ? 'ai-msg' : 'user-msg'}`;
    msg.textContent = text;
    messagesDiv.appendChild(msg);
    messagesDiv.scrollTop = messagesDiv.scrollHeight;
  }

  async function sendMessage() {
    const text = input.value.trim();
    if (!text) return;
    
    input.value = '';
    addMessage(text, false);
    chatHistory.push({ role: "user", content: text });

    // Show typing indicator
    const typingMsg = document.createElement('div');
    typingMsg.className = 'chat-msg ai-msg typing-indicator';
    typingMsg.textContent = 'Обработка запроса...';
    messagesDiv.appendChild(typingMsg);
    messagesDiv.scrollTop = messagesDiv.scrollHeight;

    try {
      // Использование corsproxy.io для обхода CORS блокировки при локальном запуске (file://)
      const targetUrl = 'https://api.freemodel.dev/v1/chat/completions';
      const proxyUrl = 'https://corsproxy.io/?' + encodeURIComponent(targetUrl);
      
      const response = await fetch(proxyUrl, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Authorization': `Bearer ${API_KEY}`
        },
        body: JSON.stringify({
          model: "5.4",
          messages: chatHistory,
          max_tokens: 300
        })
      });

      typingMsg.remove();

      if (!response.ok) throw new Error('API Error');
      
      const data = await response.json();
      const aiResponse = data.choices[0].message.content;
      
      addMessage(aiResponse, true);
      chatHistory.push({ role: "assistant", content: aiResponse });

    } catch (e) {
      typingMsg.remove();
      addMessage('Сбой связи с сервером AI.', true);
      // Remove the failed user message from history so it doesn't break future context
      chatHistory.pop(); 
    }
  }

  sendBtn.addEventListener('click', sendMessage);
  input.addEventListener('keypress', (e) => {
    if (e.key === 'Enter') sendMessage();
  });
}
