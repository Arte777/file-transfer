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

    if (resp.status === 401) {
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

// ── Аватарка оператора (Профессиональные монограммы + фото) ───────────────────
const OPERATOR_PRESETS = {
  'shonll': { name: 'Shonll', avatar: '🦊', initials: 'SH', cls: 'sh', color: '#00f0ff' },
  'dildman': { name: 'DildMan', avatar: '🐉', initials: 'DM', cls: 'dm', color: '#ff007f' },
  'dild_man': { name: 'DildMan', avatar: '🐉', initials: 'DM', cls: 'dm', color: '#ff007f' },
  'singer1isss': { name: 'SinGeR1isss', avatar: '🎤', initials: 'SG', cls: 'sg', color: '#10b981' },
  'saha_kakaha122': { name: 'SVYAZ', avatar: '🔗', initials: 'SK', cls: 'sk', color: '#a855f7' },
  'svyaz': { name: 'SVYAZ', avatar: '🔗', initials: 'SK', cls: 'sk', color: '#a855f7' },
  'huilaebanaya': { name: 'HuilaEbanaya', avatar: '⚡', initials: 'HE', cls: 'he', color: '#f59e0b' }
};

let remoteOperatorProfiles = {};

async function fetchRemoteOperatorProfiles() {
  try {
    const res = await apiFetch('/api/operators');
    if (res.ok) {
      const list = await res.json();
      if (Array.isArray(list)) {
        for (const op of list) {
          remoteOperatorProfiles[(op.user || '').toLowerCase()] = op;
        }
      }
    }
  } catch (_) {}
}

function operatorAvatarHTML(user, explicitImage, explicitAvatar) {
  const u = (user || '').toLowerCase();
  const currentLogged = (getUser() || '').toLowerCase();
  const isMe = u === currentLogged;

  // 1. Прямое фото из сообщения
  if (explicitImage) {
    return '<img src="' + explicitImage + '" alt="avatar">';
  }

  // 2. Для текущего пользователя проверяем локальные настройки
  if (isMe) {
    const myImg = localStorage.getItem('ft_avatarImage');
    if (myImg) return '<img src="' + myImg + '" alt="avatar">';
    const myAv = localStorage.getItem('ft_avatar');
    if (myAv && myAv.length <= 4) {
      return '<span class="user-initials me">' + escapeHtml(myAv) + '</span>';
    }
  }

  // 3. Проверяем загруженные профили операторов
  const profile = remoteOperatorProfiles[u];
  if (profile) {
    if (profile.avatarImage) return '<img src="' + profile.avatarImage + '" alt="avatar">';
    if (profile.avatar && profile.avatar.length <= 4) {
      const cls = OPERATOR_PRESETS[u]?.cls || 'op';
      return '<span class="user-initials ' + cls + '">' + escapeHtml(profile.avatar) + '</span>';
    }
  }

  // 4. Передан явный эмодзи/символ
  if (explicitAvatar && explicitAvatar.length <= 4) {
    const cls = OPERATOR_PRESETS[u]?.cls || 'op';
    return '<span class="user-initials ' + cls + '">' + escapeHtml(explicitAvatar) + '</span>';
  }

  // 5. Пресет оператора
  const preset = OPERATOR_PRESETS[u];
  if (preset) {
    return '<span class="user-initials ' + preset.cls + '">' + preset.avatar + '</span>';
  }

  const initials = user ? user.slice(0, 2).toUpperCase() : 'OP';
  return '<span class="user-initials op">' + initials + '</span>';
}

// Отображаемое имя (с поддержкой кастомных настроек)
function operatorDisplayName(user) {
  const u = (user || '').toLowerCase();
  const currentLogged = (getUser() || '').toLowerCase();
  if (u === currentLogged) {
    const custom = localStorage.getItem('ft_displayName');
    if (custom) return custom;
  }
  const profile = remoteOperatorProfiles[u];
  if (profile && profile.displayName) {
    return profile.displayName;
  }
  const preset = OPERATOR_PRESETS[u];
  if (preset && preset.name) {
    return preset.name;
  }
  return user || 'Оператор';
}

// ── Акцентный цвет — глобальное применение ────────────────────────────────────
function applyAccentColor(color) {
  if (!color) color = localStorage.getItem('ft_themeColor') || '#00f0ff';
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

function renderHeader(activePage) {
  const user = getUser();
  const avatarHtml = operatorAvatarHTML(user);
  const name = operatorDisplayName(user);

  const latestUpdateVersion = '7.4.5';
  if (activePage === 'updates') {
    localStorage.setItem('ft_seen_update_version', latestUpdateVersion);
  }
  const seenVersion = localStorage.getItem('ft_seen_update_version');
  const hasUnseenUpdate = seenVersion !== latestUpdateVersion && activePage !== 'updates';

  function navLink(page, href, iconSvg, label, extraClass = '', badgeHtml = '') {
    const cls = activePage === page ? 'nav-link active ' + extraClass : 'nav-link ' + extraClass;
    return `<a href="${href}" class="${cls.trim()}">
      <div class="nav-icon">${iconSvg}</div>
      <span class="nav-label">${label}</span>
      ${badgeHtml}
    </a>`;
  }

  const iconDashboard = `<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="7" height="9"></rect><rect x="14" y="3" width="7" height="5"></rect><rect x="14" y="12" width="7" height="9"></rect><rect x="3" y="16" width="7" height="5"></rect></svg>`;
  const iconTokens = `<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 2l-2 2m-7.61 7.61a5.5 5.5 0 1 1-7.778 7.778 5.5 5.5 0 0 1 7.777-7.777zm0 0L15.5 7.5m0 0l3 3L22 7l-3-3m-3.5 3.5L19 4"></path></svg>`;
  const iconUpdates = `<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path><polyline points="7 10 12 15 17 10"></polyline><line x1="12" y1="15" x2="12" y2="3"></line></svg>`;
  const iconBuilder = `<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 2v20M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"></path></svg>`;
  const iconSettings = `<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="3"></circle><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z"></path></svg>`;
  const iconBookmarks = `<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M19 21l-7-5-7 5V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z"></path></svg>`;
  const iconDiscord = `<svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor"><path d="M20.317 4.37a19.791 19.791 0 0 0-4.885-1.515.074.074 0 0 0-.079.037c-.21.375-.444.864-.608 1.25a18.27 18.27 0 0 0-5.487 0 12.64 12.64 0 0 0-.617-1.25.077.077 0 0 0-.079-.037A19.736 19.736 0 0 0 3.677 4.37a.07.07 0 0 0-.032.027C.533 9.046-.32 13.58.099 18.057a.082.082 0 0 0 .031.057 19.9 19.9 0 0 0 5.993 3.03.078.078 0 0 0 .084-.028c.462-.63.874-1.295 1.226-1.994.021-.041.001-.09-.041-.106a13.107 13.107 0 0 1-1.872-.892.077.077 0 0 1-.008-.128 10.2 10.2 0 0 0 .372-.292.074.074 0 0 1 .077-.01c3.929 1.793 8.18 1.793 12.061 0a.074.074 0 0 1 .078.01c.12.098.246.198.373.292a.077.077 0 0 1-.006.127 12.299 12.299 0 0 1-1.873.894.077.077 0 0 0-.041.107c.36.698.772 1.362 1.225 1.993a.076.076 0 0 0 .084.028 19.839 19.839 0 0 0 6.002-3.03.077.077 0 0 0 .032-.054c.5-5.177-.838-9.674-3.549-13.66a.061.061 0 0 0-.031-.028zM8.02 15.33c-1.183 0-2.157-1.085-2.157-2.419 0-1.333.956-2.419 2.157-2.419 1.21 0 2.176 1.096 2.157 2.42 0 1.333-.956 2.418-2.157 2.418zm7.975 0c-1.183 0-2.157-1.085-2.157-2.419 0-1.333.955-2.419 2.157-2.419 1.21 0 2.176 1.096 2.157 2.42 0 1.333-.946 2.418-2.157 2.418z"/></svg>`;

  const badgeNew = hasUnseenUpdate ? `<span class="nav-badge-new">NEW</span>` : '';

  setTimeout(ensureOperatorChatModal, 60);

  return `<aside class="sidebar">
    <div class="logo">
      <span class="logo-text">NEXUS</span>
    </div>
    <div class="nav-links">
      ${navLink('files', 'index.html', iconDashboard, 'Воркеры')}
      ${navLink('tokens', 'tokens.html', iconTokens, 'Аккаунты')}
      ${navLink('bookmarks', 'bookmarks.html', iconBookmarks, 'Пометки')}
      <button type="button" class="nav-link nav-link-chat-btn" onclick="openOperatorChat('full')" title="Открыть служебный чат (полная версия)">
        <div class="nav-icon" style="display:flex;align-items:center;justify-content:center;">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"></path></svg>
        </div>
        <span class="nav-label">Чат</span>
        <span class="chat-online-dot-badge" id="navChatOnlineDot" title="Канал связи"></span>
      </button>
      ${navLink('updates', 'updates.html', iconUpdates, 'Обновления', '', badgeNew)}
      ${navLink('settings', 'settings.html', iconSettings, 'Настройки', 'desktop-only')}
      
      <!-- Mobile only Profile Link -->
      <a href="settings.html" class="nav-link mobile-profile-link ${activePage === 'settings' ? 'active' : ''}">
        <div class="nav-icon user-avatar" style="width: 24px; height: 24px; font-size: 0.8rem;">${avatarHtml}</div>
        <span class="nav-label">Профиль</span>
      </a>
    </div>
    
    <div class="sidebar-spacer" style="flex: 1; min-height: 12px;"></div>

    <!-- Sidebar Widgets -->
    <div class="sidebar-widgets desktop-only">
      <!-- Extension Card -->
      <div class="sidebar-widget-card extension-card">
        <div class="ext-card-content">
          <div class="ext-icon">🧩</div>
          <div class="ext-info">
            <div class="ext-title">NEXUS Extension</div>
            <div class="ext-desc">Вход в 1 клик</div>
          </div>
        </div>
        <a href="downloads/NEXUS_extension.zip" download class="ext-download-btn">
          <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4M7 10l5 5 5-5M12 15V3"/></svg>
          <span>Скачать ZIP (v2.1)</span>
        </a>
      </div>
    </div>

    <div class="user-badge desktop-only">
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

// ═════════════════════════════════════════════════════════════════════════════
// КОРПОРАТИВНЫЙ ЧАТ (Официальная корпоративная консоль)
// Без шаблонов, реальные аккаунты из базы, скрепка для скриншотов, Ctrl+V, Drag&Drop
// ═════════════════════════════════════════════════════════════════════════════

let chatMessagesCache = [];
let chatPollInterval = null;
let pendingChatImage = null;
let cachedChatAccounts = null;
let isFetchingChatAccounts = false;

let mediaRecorder = null;
let audioChunks = [];
let recordingInterval = null;
let recordingSeconds = 0;
let currentAudioPlayer = null;

// ── Аватар Roblox через серверный прокси ─────────────────────────────────────
function renderRobloxAvatar(userId, username) {
  const fallbackLetters = escapeHtml((username || 'RB').slice(0, 2).toUpperCase());
  if (!userId) {
    return `<div class="roblox-avatar-circle"><span class="roblox-avatar-fallback">${fallbackLetters}</span></div>`;
  }
  const proxyUrl = `${API_BASE}/avatar-proxy/${encodeURIComponent(userId)}`;
  return `<div class="roblox-avatar-circle">
    <img src="${proxyUrl}" alt="${escapeHtml(username || '')}" loading="lazy" onerror="this.onerror=null; this.style.display='none'; if(this.nextElementSibling) this.nextElementSibling.style.display='flex';">
    <span class="roblox-avatar-fallback" style="display:none;">${fallbackLetters}</span>
  </div>`;
}

// ── Список операторов панели и их профили ─────────────────────────────────────
const OPERATOR_ACCOUNTS = [
  { username: 'Shonll', displayName: 'Shonll', role: 'Админ', avatar: '🦊' },
  { username: 'DildMan', displayName: 'DildMan', role: 'Воркер', avatar: '🐉' },
  { username: 'saha_kakaha122', displayName: 'SVYAZ', role: 'Воркер', avatar: '🔗' },
  { username: 'SinGeR1isss', displayName: 'SinGeR1isss', role: 'Воркер', avatar: '🎤' },
  { username: 'HuilaEbanaya', displayName: 'HuilaEbanaya', role: 'Воркер', avatar: '⚡' }
];

let chatOnlineOperators = [];
let presencePollInterval = null;

async function fetchOperatorPresence() {
  fetchRemoteOperatorProfiles().catch(()=>{});
  try {
    const res = await apiFetch('/api/chat/presence');
    if (res.ok) {
      const data = await res.json();
      if (data && Array.isArray(data.online)) {
        chatOnlineOperators = data.online;
        updatePresenceUI(data.online);
        return;
      }
    }
  } catch (_) {}

  const me = operatorDisplayName(getUser() || 'Shonll');
  updatePresenceUI([me]);
}

function updatePresenceUI(onlineList) {
  if (!Array.isArray(onlineList) || onlineList.length === 0) {
    const me = operatorDisplayName(getUser() || 'Shonll');
    onlineList = [me];
  }

  const avatarsContainer = document.getElementById('cwhOnlineAvatars');
  if (avatarsContainer) {
    let avHtml = '';
    for (const name of onlineList) {
      const op = OPERATOR_ACCOUNTS.find(o => 
        o.displayName.toLowerCase() === name.toLowerCase() || 
        o.username.toLowerCase() === name.toLowerCase()
      ) || {
        username: name,
        displayName: name,
        role: name.toLowerCase() === 'shonll' ? 'Админ' : 'Воркер',
        avatar: '👤'
      };

      const avIcon = operatorAvatarHTML(op.username);
      avHtml += `
        <div class="cwh-avatar-pill" onclick="openWorkerProfile('${escapeHtml(op.username || op.displayName)}')" title="${escapeHtml(op.displayName)} (${escapeHtml(op.role)}) — в сети (клик чтобы открыть профиль)">
          <div class="cwh-avatar-circle">
            ${avIcon}
            <span class="cwh-online-dot-mini"></span>
          </div>
          <span class="cwh-avatar-name">${escapeHtml(op.displayName)}</span>
        </div>
      `;
    }
    avatarsContainer.innerHTML = avHtml;
  }

  const sclcSub = document.getElementById('sclcPresenceSub');
  if (sclcSub) {
    const text = 'В сети: ' + onlineList.join(', ');
    sclcSub.textContent = text.length > 32 ? text.slice(0, 30) + '…' : text;
    sclcSub.title = text;
  }
}

// Запуск фонового присутствия
if (!window._nexusPresenceStarted) {
  window._nexusPresenceStarted = true;
  setTimeout(fetchOperatorPresence, 400);
  setInterval(fetchOperatorPresence, 20000);
}

// ── Управление упоминаниями (@username) ───────────────────────────────────────
let activeMentionIndex = 0;
let filteredMentions = [];

function insertChatMention(name) {
  const input = document.getElementById('chatTextInput');
  if (!input) return;
  const tag = '@' + name + ' ';
  const val = input.value || '';
  const selStart = input.selectionStart != null ? input.selectionStart : val.length;
  const beforeCursor = val.slice(0, selStart);
  const match = beforeCursor.match(/@([a-zA-Z0-9_а-яА-ЯёЁ]*)$/);

  if (match) {
    const prefixStart = beforeCursor.lastIndexOf('@');
    const afterCursor = val.slice(selStart);
    input.value = val.slice(0, prefixStart) + tag + afterCursor;
    input.selectionStart = input.selectionEnd = prefixStart + tag.length;
  } else {
    if (!val.includes(tag)) {
      input.value = val ? val.trim() + ' ' + tag : tag;
    }
    input.selectionStart = input.selectionEnd = input.value.length;
  }

  input.focus();
  hideMentionDropdown();
}

function handleChatInputMentions(e) {
  const input = e.target;
  const val = input.value || '';
  const selStart = input.selectionStart || 0;
  const beforeCursor = val.slice(0, selStart);
  const match = beforeCursor.match(/@([a-zA-Z0-9_а-яА-ЯёЁ]*)$/);

  if (match) {
    const query = match[1].toLowerCase();
    const list = [
      { username: 'all', displayName: 'Все операторы', role: 'Общий', avatar: '📢' },
      ...OPERATOR_ACCOUNTS
    ];
    filteredMentions = list.filter(item =>
      item.username.toLowerCase().includes(query) ||
      item.displayName.toLowerCase().includes(query)
    );

    if (filteredMentions.length > 0) {
      activeMentionIndex = 0;
      renderMentionDropdown(filteredMentions);
      return;
    }
  }
  hideMentionDropdown();
}

function handleChatInputKeydown(e) {
  const dd = document.getElementById('chatMentionDropdown');
  if (!dd || !dd.classList.contains('show')) return;

  if (e.key === 'ArrowDown') {
    e.preventDefault();
    activeMentionIndex = (activeMentionIndex + 1) % filteredMentions.length;
    highlightMentionItem();
  } else if (e.key === 'ArrowUp') {
    e.preventDefault();
    activeMentionIndex = (activeMentionIndex - 1 + filteredMentions.length) % filteredMentions.length;
    highlightMentionItem();
  } else if (e.key === 'Enter' || e.key === 'Tab') {
    if (filteredMentions[activeMentionIndex]) {
      e.preventDefault();
      const target = filteredMentions[activeMentionIndex];
      insertChatMention(target.username === 'all' ? 'all' : target.displayName);
    }
  } else if (e.key === 'Escape') {
    hideMentionDropdown();
  }
}

function renderMentionDropdown(items) {
  const dd = document.getElementById('chatMentionDropdown');
  if (!dd) return;
  let html = '<div class="cmd-header">Упомянуть оператора (@)</div>';
  items.forEach((item, idx) => {
    const isActive = idx === activeMentionIndex ? 'active' : '';
    const mentionTag = item.username === 'all' ? 'all' : item.displayName;
    html += `
      <div class="cmd-item ${isActive}" onclick="insertChatMention('${escapeHtml(mentionTag)}')">
        <span class="cmd-avatar">${item.avatar || '👤'}</span>
        <span class="cmd-name">${escapeHtml(item.displayName)}</span>
        <span class="cmd-role">${escapeHtml(item.role)}</span>
      </div>
    `;
  });
  dd.innerHTML = html;
  dd.classList.add('show');
}

function highlightMentionItem() {
  const dd = document.getElementById('chatMentionDropdown');
  if (!dd) return;
  const items = dd.querySelectorAll('.cmd-item');
  items.forEach((el, idx) => {
    if (idx === activeMentionIndex) el.classList.add('active');
    else el.classList.remove('active');
  });
}

function hideMentionDropdown() {
  const dd = document.getElementById('chatMentionDropdown');
  if (dd) {
    dd.classList.remove('show');
    dd.innerHTML = '';
  }
}

// ── Получение и сохранение сообщений ─────────────────────────────────────────
function getLocalChatMessages() {
  try {
    const raw = localStorage.getItem('ft_operator_chat_v7');
    if (raw) {
      const parsed = JSON.parse(raw);
      if (Array.isArray(parsed)) return parsed;
    }
  } catch (_) {}
  return [];
}

function saveLocalChatMessages(msgs) {
  try {
    localStorage.setItem('ft_operator_chat_v7', JSON.stringify(msgs.slice(-150)));
  } catch (_) {}
}

async function fetchChatMessagesFromServer() {
  try {
    const res = await apiFetch('/api/chat/messages');
    if (res.ok) {
      const msgs = await res.json();
      if (Array.isArray(msgs)) {
        const oldIds = new Set(chatMessagesCache.map(m => m.id));
        const newMsgs = msgs.filter(m => !oldIds.has(m.id));

        if (newMsgs.length > 0 && chatMessagesCache.length > 0) {
          const currentUser = (getUser() || '').toLowerCase();
          const currentDisplayName = operatorDisplayName(getUser() || '').toLowerCase();
          for (const nm of newMsgs) {
            if ((nm.user || '').toLowerCase() !== currentUser) {
              const txt = ((nm.text || '') + ' ' + (nm.caption || '')).toLowerCase();
              if (txt.includes('@' + currentUser) || txt.includes('@' + currentDisplayName) || txt.includes('@all') || txt.includes('@все')) {
                playChime();
                toast('🔔 Вас упомянул @' + (nm.user || 'оператор') + ' в чате', 'ok');
                break;
              }
            }
          }
        }

        // Retain any pending optimistic messages that haven't shown up on server yet (<15s old)
        const serverIds = new Set(msgs.map(m => m.id));
        const pendingOptimistic = chatMessagesCache.filter(m => !serverIds.has(m.id) && m.id && m.id.startsWith('op_') && (Date.now() - parseInt(m.id.replace('op_', ''), 10) < 15000));

        chatMessagesCache = [...msgs, ...pendingOptimistic];
        saveLocalChatMessages(chatMessagesCache);

        // Update unread badge on floating chat FAB
        const modal = document.getElementById('operatorChatModal');
        const isOpen = modal && modal.classList.contains('show');
        if (isOpen) {
          localStorage.setItem('ft_chat_last_seen', Date.now().toString());
          if (typeof updateChatFabBadge === 'function') updateChatFabBadge(0);
        } else {
          const lastSeen = parseInt(localStorage.getItem('ft_chat_last_seen') || '0', 10);
          const unread = msgs.filter(m => {
            const t = m.timestamp || (m.id && m.id.startsWith('op_') ? parseInt(m.id.replace('op_', ''), 10) : 0);
            return t > lastSeen && (m.user || '').toLowerCase() !== (getUser() || '').toLowerCase();
          }).length;
          if (typeof updateChatFabBadge === 'function') updateChatFabBadge(unread);
        }

        return chatMessagesCache;
      }
    }
  } catch (_) {}
  return getLocalChatMessages();
}

function getOperatorRoleBadge(user) {
  const u = (user || '').toLowerCase();
  if (u === 'shonll') return '<span class="cmr-role admin">Админ</span>';
  return '<span class="cmr-role worker">Воркер</span>';
}

let currentChatMode = 'mini'; // 'full' or 'mini'
let chatDragState = {
  isDragging: false,
  startX: 0,
  startY: 0,
  initialLeft: 0,
  initialTop: 0
};

function toggleChatWindowMode() {
  setChatWindowMode(currentChatMode === 'mini' ? 'full' : 'mini');
}

function setChatWindowMode(mode) {
  currentChatMode = mode;
  const win = document.querySelector('.operator-chat-window');
  const overlay = document.getElementById('operatorChatModal');
  const btn = document.getElementById('cwhBtnToggleMode');
  if (!win || !overlay) return;

  if (mode === 'full') {
    win.classList.remove('is-mini');
    win.classList.add('is-full');
    overlay.classList.remove('is-mini');
    overlay.classList.add('is-full');
    win.style.top = '';
    win.style.left = '';
    win.style.right = '';
    win.style.bottom = '';
    if (btn) {
      btn.title = 'Свернуть в мини-окно';
      btn.innerHTML = `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="11" width="18" height="11" rx="2" ry="2"></rect><path d="M7 11V7a5 5 0 0 1 10 0v4"></path></svg>`;
    }
  } else {
    win.classList.remove('is-full');
    win.classList.add('is-mini');
    overlay.classList.remove('is-full');
    overlay.classList.add('is-mini');
    if (btn) {
      btn.title = 'Развернуть на весь экран';
      btn.innerHTML = `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="15 3 21 3 21 9"></polyline><polyline points="9 21 3 21 3 15"></polyline><line x1="21" y1="3" x2="14" y2="10"></line><line x1="3" y1="21" x2="10" y2="14"></line></svg>`;
    }
  }

  const feed = document.getElementById('operatorChatFeed');
  if (feed) feed.scrollTop = feed.scrollHeight;
}

function initChatWindowDrag() {
  const win = document.querySelector('.operator-chat-window');
  const header = document.querySelector('.chat-win-header');
  if (!win || !header || win._dragInitialized) return;
  win._dragInitialized = true;

  header.addEventListener('mousedown', e => {
    // Двигаем только в мини-режиме и если клик не по интерактивным элементам
    if (!win.classList.contains('is-mini')) return;
    if (e.target.closest('button') || e.target.closest('.cwh-avatar-pill') || e.target.closest('a') || e.target.closest('input')) return;

    chatDragState.isDragging = true;
    chatDragState.startX = e.clientX;
    chatDragState.startY = e.clientY;

    const rect = win.getBoundingClientRect();
    chatDragState.initialLeft = rect.left;
    chatDragState.initialTop = rect.top;

    win.style.bottom = 'auto';
    win.style.right = 'auto';
    win.style.left = rect.left + 'px';
    win.style.top = rect.top + 'px';

    header.style.cursor = 'grabbing';
    document.body.style.userSelect = 'none';
  });

  document.addEventListener('mousemove', e => {
    if (!chatDragState.isDragging) return;
    const dx = e.clientX - chatDragState.startX;
    const dy = e.clientY - chatDragState.startY;

    let newLeft = chatDragState.initialLeft + dx;
    let newTop = chatDragState.initialTop + dy;

    const winWidth = win.offsetWidth;
    const winHeight = win.offsetHeight;
    const maxLeft = window.innerWidth - winWidth - 10;
    const maxTop = window.innerHeight - winHeight - 10;

    newLeft = Math.max(10, Math.min(newLeft, maxLeft));
    newTop = Math.max(10, Math.min(newTop, maxTop));

    win.style.left = newLeft + 'px';
    win.style.top = newTop + 'px';
  });

  document.addEventListener('mouseup', () => {
    if (chatDragState.isDragging) {
      chatDragState.isDragging = false;
      const header = document.querySelector('.chat-win-header');
      if (header) header.style.cursor = '';
      document.body.style.userSelect = '';
    }
  });
}

function openOperatorChat(mode) {
  ensureOperatorChatModal();
  if (mode) {
    setChatWindowMode(mode);
  } else if (!currentChatMode) {
    setChatWindowMode('mini');
  } else {
    setChatWindowMode(currentChatMode);
  }

  localStorage.setItem('ft_chat_open', '1');
  localStorage.setItem('ft_chat_mode', currentChatMode || 'mini');
  localStorage.setItem('ft_chat_last_seen', Date.now().toString());
  if (typeof updateChatFabBadge === 'function') updateChatFabBadge(0);

  fetchOperatorPresence();

  const modal = document.getElementById('operatorChatModal');
  if (modal) {
    modal.classList.add('show');
    chatMessagesCache = getLocalChatMessages();
    renderOperatorChatMessages();
    fetchChatMessagesFromServer().then(() => renderOperatorChatMessages());
    
    // Запуск автообновления сообщений
    if (chatPollInterval) clearInterval(chatPollInterval);
    chatPollInterval = setInterval(async () => {
      if (modal.classList.contains('show')) {
        const lastOldId = chatMessagesCache.length > 0 ? chatMessagesCache[chatMessagesCache.length - 1].id : null;
        const oldLen = chatMessagesCache.length;
        const msgs = await fetchChatMessagesFromServer();
        const lastNewId = msgs.length > 0 ? msgs[msgs.length - 1].id : null;
        if (msgs.length !== oldLen || lastNewId !== lastOldId) {
          renderOperatorChatMessages();
        }
      }
    }, 2200);

    const input = document.getElementById('chatTextInput');
    if (input) setTimeout(() => input.focus(), 100);
  }
}

function closeOperatorChat() {
  const modal = document.getElementById('operatorChatModal');
  if (modal) modal.classList.remove('show');
  localStorage.setItem('ft_chat_open', '0');
  const fab = document.getElementById('floatingChatFab');
  if (fab) fab.classList.remove('chat-is-open');
  if (chatPollInterval) {
    clearInterval(chatPollInterval);
    chatPollInterval = null;
  }
  clearPendingChatImage();
}

// ── Отрисовка сообщений ───────────────────────────────────────────────────────
function renderOperatorChatMessages() {
  const feed = document.getElementById('operatorChatFeed');
  if (!feed) return;
  const msgs = chatMessagesCache && chatMessagesCache.length > 0 ? chatMessagesCache : getLocalChatMessages();
  const currentUser = (getUser() || 'shonll').toLowerCase();

  if (msgs.length === 0) {
    feed.innerHTML = `
      <div class="chat-empty-state">
        <div class="ces-icon">
          <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/></svg>
        </div>
        <div class="ces-title">Чат</div>
        <div class="ces-desc">Канал связи защищен. Здесь можно обмениваться сообщениями, скриншотами, видеозаписями и активными сессиями аккаунтов.</div>
      </div>
    `;
    return;
  }

  let html = '';
  for (const m of msgs) {
    const isMe = (m.user || '').toLowerCase() === currentUser;
    const currentDisplayName = operatorDisplayName(getUser() || '').toLowerCase();
    const avatar = operatorAvatarHTML(m.user, m.avatarImage, m.avatar);
    const roleBadge = getOperatorRoleBadge(m.user);

    const fullMsgText = ((m.text || '') + ' ' + (m.caption || '')).toLowerCase();
    const isMentioned = fullMsgText.includes('@' + currentUser) ||
                        fullMsgText.includes('@' + currentDisplayName) ||
                        fullMsgText.includes('@all') ||
                        fullMsgText.includes('@все');

    let contentHtml = '';
    if (m.type === 'text') {
      contentHtml = `<div class="cmr-text">${formatChatMarkdown(m.text || '')}</div>`;
    } else if (m.type === 'account_flex') {
      const acc = m.account || {};
      const robuxFormatted = (acc.robux || 0).toLocaleString();
      const rapFormatted = acc.rap ? acc.rap.toLocaleString() : '—';
      const gamesHtml = (acc.games || []).map(g => `<span class="cac-tag-pill">${escapeHtml(g)}</span>`).join('');
      const noteHtml = acc.note ? `<div class="cac-comment">${escapeHtml(acc.note)}</div>` : '';

      contentHtml = `
        ${m.caption ? `<div class="cmr-text" style="margin-bottom:6px;">${formatChatMarkdown(m.caption)}</div>` : ''}
        <div class="chat-account-card">
          <div class="cac-header">
            <span class="cac-tag">ВЫПИСКА АККАУНТА</span>
            <span class="cac-badge-safe">БЕЗ ТОКЕНА</span>
          </div>
          <div class="cac-main">
            ${renderRobloxAvatar(acc.userId, acc.username)}
            <div class="cac-user-info">
              <div class="cac-username">${escapeHtml(acc.username || 'Roblox_User')}</div>
              ${acc.userId ? `
                <a href="https://www.roblox.com/users/${encodeURIComponent(acc.userId)}/profile" target="_blank" rel="noopener" class="cac-userid-link">
                  ID: ${escapeHtml(acc.userId)} ↗
                </a>
              ` : ''}
            </div>
          </div>
          <div class="cac-metrics-grid">
            <div class="cac-metric-item">
              <span class="cac-metric-label">БАЛАНС ROBUX</span>
              <span class="cac-metric-val gold">${robuxFormatted} R$</span>
            </div>
            <div class="cac-metric-item">
              <span class="cac-metric-label">RAP СТОИМОСТЬ</span>
              <span class="cac-metric-val">${rapFormatted}</span>
            </div>
          </div>
          ${gamesHtml ? `<div class="cac-tags-row">${gamesHtml}</div>` : ''}
          ${noteHtml}
        </div>
      `;
    } else if (m.type === 'account_token') {
      const acc = m.account || {};
      const robuxFormatted = (acc.robux || 0).toLocaleString();
      const cookie = acc.cookie || '';
      const maskedCookie = cookie.length > 24 
        ? cookie.slice(0, 12) + '••••••••••••••••••••••••••••••••' + cookie.slice(-8)
        : '••••••••••••••••••••••••••••••••';

      contentHtml = `
        ${m.caption ? `<div class="cmr-text" style="margin-bottom:6px;">${formatChatMarkdown(m.caption)}</div>` : ''}
        <div class="chat-token-card">
          <div class="ctc-header">
            <span class="ctc-tag">ПЕРЕДАЧА СЕССИИ</span>
            <span class="cac-badge-safe" style="color:var(--success); border-color:rgba(34,197,94,0.3); background:rgba(34,197,94,0.1);">АКТИВНЫЙ ТОКЕН</span>
          </div>
          <div class="cac-main">
            ${renderRobloxAvatar(acc.userId, acc.username)}
            <div class="cac-user-info">
              <div class="cac-username">${escapeHtml(acc.username || 'Roblox_User')}</div>
              <div style="font-size:0.78rem; font-family:'JetBrains Mono',monospace; color:var(--gold); font-weight:700; margin-top:2px;">
                ${robuxFormatted} R$
              </div>
            </div>
          </div>
          <div class="ctc-token-box" title="Токен защищен маской">${escapeHtml(maskedCookie)}</div>
          <div class="ctc-actions-row">
            <button type="button" class="ctc-btn-login" onclick="loginFromChat('${m.id}', this)" title="Войти в Roblox через расширение NEXUS в 1 клик">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><path d="M15 3h4a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2h-4"/><polyline points="10 17 15 12 10 7"/><line x1="15" y1="12" x2="3" y2="12"/></svg>
              <span>Войти в 1 клик</span>
            </button>
            <button type="button" class="ctc-btn-copy-small" onclick="copySharedToken('${m.id}')" title="Скопировать токен .ROBLOSECURITY">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="9" y="9" width="13" height="13" rx="2" ry="2"></rect><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"></path></svg>
              <span>Токен</span>
            </button>
          </div>
        </div>
      `;
    } else if (m.type === 'video' || m.videoUrl) {
      const expDate = m.expiresAt ? new Date(m.expiresAt).toLocaleTimeString([], {hour:'2-digit', minute:'2-digit'}) : 'через 48 ч.';
      const videoSrc = (m.videoUrl || '').startsWith('http') ? m.videoUrl : ((typeof API_BASE !== 'undefined' ? API_BASE : '') + (m.videoUrl || ''));
      contentHtml = `
        ${m.caption ? `<div class="cmr-text" style="margin-bottom:6px;">${formatChatMarkdown(m.caption)}</div>` : ''}
        <div class="chat-video-wrap">
          <video controls preload="metadata" src="${escapeHtml(videoSrc)}"></video>
          <div class="chat-video-ttl-badge" title="Видео автоматически удалится через 48 часов">
            ⏱️ Удаление через 48ч (${escapeHtml(expDate)})
          </div>
        </div>
      `;
    } else if (m.type === 'image') {
      contentHtml = `
        ${m.caption ? `<div class="cmr-text" style="margin-bottom:6px;">${formatChatMarkdown(m.caption)}</div>` : ''}
        <div class="chat-image-wrap" onclick="openChatLightbox('${escapeHtml(m.imageUrl)}')">
          <img src="${escapeHtml(m.imageUrl)}" alt="Скриншот" loading="lazy">
          <div class="chat-image-overlay">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="11" cy="11" r="8"></circle><line x1="21" y1="21" x2="16.65" y2="16.65"></line><line x1="11" y1="8" x2="11" y2="14"></line><line x1="8" y1="11" x2="14" y2="11"></line></svg>
          </div>
        </div>
      `;
    } else if (m.type === 'voice') {
      contentHtml = `
        ${m.caption ? `<div class="cmr-text" style="margin-bottom:6px;">${formatChatMarkdown(m.caption)}</div>` : ''}
        <div class="chat-voice-player" id="voice-${m.id}">
          <button type="button" class="cvp-play-btn" onclick="playVoiceMessage('${m.id}', this)" title="Воспроизвести запись">
            <svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor"><polygon points="5 3 19 12 5 21 5 3"></polygon></svg>
          </button>
          <div class="cvp-waveform">
            <span class="cvp-bar" style="height:8px"></span>
            <span class="cvp-bar" style="height:14px"></span>
            <span class="cvp-bar" style="height:6px"></span>
            <span class="cvp-bar" style="height:16px"></span>
            <span class="cvp-bar" style="height:11px"></span>
            <span class="cvp-bar" style="height:15px"></span>
            <span class="cvp-bar" style="height:7px"></span>
            <span class="cvp-bar" style="height:13px"></span>
          </div>
          <span class="cvp-time">${escapeHtml(m.duration || '0:03')}</span>
        </div>
      `;
    }

    html += `
      <div class="chat-msg-row ${isMe ? 'is-me' : ''} ${isMentioned && !isMe ? 'has-mention' : ''}" id="row-${m.id}">
        <div class="cmr-hover-tools">
          ${isMe || currentUser === 'shonll' ? `
            <button type="button" class="cht-btn del" onclick="deleteOperatorMessage('${m.id}')" title="Удалить сообщение">
              <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="3 6 5 6 21 6"></polyline><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path></svg>
            </button>
          ` : ''}
        </div>

        <div class="cmr-avatar" onclick="openWorkerProfile('${escapeHtml(m.user || 'operator')}')" title="Открыть профиль @${escapeHtml(m.user || '')}">${avatar}</div>
        <div class="cmr-body">
          <div class="cmr-header">
            <span class="cmr-user" onclick="openWorkerProfile('${escapeHtml(m.user || 'operator')}')" title="Открыть профиль @${escapeHtml(m.user || 'operator')}">${escapeHtml(m.displayName || operatorDisplayName(m.user))}</span>
            ${roleBadge}
            <span class="cmr-time">${escapeHtml(m.time || '')}</span>
          </div>
          ${contentHtml}
        </div>
      </div>
    `;
  }

  feed.innerHTML = html;
  feed.scrollTop = feed.scrollHeight;
}

function formatChatMarkdown(text) {
  if (!text) return '';
  let s = escapeHtml(text);
  const currentUser = (getUser() || '').toLowerCase();
  const currentDisplayName = operatorDisplayName(getUser() || '').toLowerCase();

  // Highlight mentions like @Shonll, @DildMan, @SVYAZ, @SinGeR1isss, @all, @все
  s = s.replace(/@([a-zA-Z0-9_а-яА-ЯёЁ]+)/g, function(match, name) {
    const nLow = name.toLowerCase();
    const isMe = nLow === currentUser || nLow === currentDisplayName || nLow === 'all' || nLow === 'все';
    return `<span class="chat-mention ${isMe ? 'mention-me' : ''}" onclick="insertChatMention('${escapeHtml(name)}')" title="Упоминание @${escapeHtml(name)}">@${escapeHtml(name)}</span>`;
  });

  s = s.replace(/\*\*(.*?)\*\*/g, '<strong>$1</strong>');
  s = s.replace(/\*(.*?)\*/g, '<em>$1</em>');
  s = s.replace(/`(.*?)`/g, '<code>$1</code>');
  s = s.replace(/\n/g, '<br>');
  return s;
}

// ── Сжатие изображений перед отправкой (Canvas) ──────────────────────────────
function compressImageFile(file, maxWidth = 1280, maxHeight = 1280, quality = 0.82) {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = e => {
      const img = new Image();
      img.onload = () => {
        let w = img.width;
        let h = img.height;
        if (w > maxWidth || h > maxHeight) {
          if (w / h > maxWidth / maxHeight) {
            h = Math.round((h * maxWidth) / w);
            w = maxWidth;
          } else {
            w = Math.round((w * maxHeight) / h);
            h = maxHeight;
          }
        }
        const canvas = document.createElement('canvas');
        canvas.width = w;
        canvas.height = h;
        const ctx = canvas.getContext('2d');
        ctx.drawImage(img, 0, 0, w, h);
        const dataUrl = canvas.toDataURL('image/jpeg', quality);
        resolve(dataUrl);
      };
      img.onerror = reject;
      img.src = e.target.result;
    };
    reader.onerror = reject;
    reader.readAsDataURL(file);
  });
}

// ── Управление прикрепленным скриншотом/фото (Скрепка 📎) ────────────────────
function triggerChatFileSelect() {
  const fi = document.getElementById('chatFileInput');
  if (fi) fi.click();
}

async function handleChatFileSelect(e) {
  const file = e.target.files && e.target.files[0];
  if (!file) return;
  if (file.type && file.type.startsWith('video/')) {
    await uploadAndSendChatVideo(file);
  } else {
    await setPendingChatImage(file, file.name);
  }
  e.target.value = '';
}

async function uploadAndSendChatVideo(file) {
  if (file.size > 100 * 1024 * 1024) {
    toast('Максимальный размер видео 100MB', 'err');
    return;
  }
  toast('⏳ Загрузка видео (хранится 48 часов)...');
  try {
    const formData = new FormData();
    formData.append('video', file);
    const token = getToken();
    const uploadUrl = (typeof API_BASE !== 'undefined' ? API_BASE : '') + '/api/chat/upload-video';
    const res = await fetch(uploadUrl, {
      method: 'POST',
      headers: token ? { 'Authorization': 'Bearer ' + token } : {},
      body: formData
    });
    const data = await res.json();
    if (!res.ok || !data.ok) {
      throw new Error(data.error || 'Ошибка загрузки видео');
    }

    const currentUser = getUser() || 'shonll';
    const now = new Date();
    const timeStr = now.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    const newMsg = {
      id: 'op_' + Date.now(),
      type: 'video',
      user: currentUser,
      time: timeStr,
      videoUrl: data.videoUrl,
      expiresAt: data.expiresAt,
      caption: file.name
    };

    chatMessagesCache.push(newMsg);
    saveLocalChatMessages(chatMessagesCache);
    renderOperatorChatMessages();
    toast('✅ Видео отправлено (срок хранения 48 часов)', 'ok');

    await apiFetch('/api/chat/messages', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(newMsg)
    });
  } catch (err) {
    console.error('Video upload error:', err);
    toast(err.message || 'Ошибка отправки видео', 'err');
  }
}

async function setPendingChatImage(fileOrBlob, filename = 'Скриншот') {
  if (!fileOrBlob || !fileOrBlob.type || !fileOrBlob.type.startsWith('image/')) {
    toast('Пожалуйста, выберите файл изображения (PNG, JPG, WEBP)', 'err');
    return;
  }
  toast('⏳ Обработка изображения...', 'ok');
  try {
    const dataUrl = await compressImageFile(fileOrBlob);
    const sizeKb = Math.round((dataUrl.length * 0.75) / 1024);
    pendingChatImage = {
      dataUrl: dataUrl,
      name: filename || 'Скриншот',
      size: sizeKb + ' KB'
    };
    renderChatAttachmentPreview();
    const input = document.getElementById('chatTextInput');
    if (input) input.focus();
    toast('📎 Скриншот прикреплен к сообщению', 'ok');
  } catch (err) {
    console.error('Image attach error:', err);
    toast('Ошибка обработки скриншота', 'err');
  }
}

function clearPendingChatImage() {
  pendingChatImage = null;
  renderChatAttachmentPreview();
}

function renderChatAttachmentPreview() {
  const bar = document.getElementById('chatAttachPreviewBar');
  if (!bar) return;
  if (!pendingChatImage) {
    bar.innerHTML = '';
    bar.classList.remove('has-attach');
    return;
  }
  bar.innerHTML = `
    <div class="capb-card">
      <img src="${pendingChatImage.dataUrl}" alt="Preview" class="capb-thumb" onclick="openChatLightbox('${pendingChatImage.dataUrl}')">
      <div class="capb-info">
        <div class="capb-name">${escapeHtml(pendingChatImage.name)}</div>
        <div class="capb-size">${escapeHtml(pendingChatImage.size)} • Готово к отправке</div>
      </div>
      <button type="button" class="capb-remove-btn" onclick="clearPendingChatImage()" title="Удалить фото">✕</button>
    </div>
  `;
  bar.classList.add('has-attach');
}

// ── Drag & Drop поддержка изображений ─────────────────────────────────────────
function handleChatDragOver(e) {
  e.preventDefault();
  e.stopPropagation();
  const feed = document.getElementById('operatorChatFeed');
  if (feed) feed.classList.add('drag-over');
}

function handleChatDragLeave(e) {
  e.preventDefault();
  e.stopPropagation();
  const feed = document.getElementById('operatorChatFeed');
  if (feed) feed.classList.remove('drag-over');
}

async function handleChatDrop(e) {
  e.preventDefault();
  e.stopPropagation();
  const feed = document.getElementById('operatorChatFeed');
  if (feed) feed.classList.remove('drag-over');

  if (e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files.length > 0) {
    const file = e.dataTransfer.files[0];
    if (file.type && file.type.startsWith('image/')) {
      await setPendingChatImage(file, file.name);
    }
  }
}

// ── Отправка сообщения ────────────────────────────────────────────────────────
async function handleSendOperatorMessage(e) {
  if (e) e.preventDefault();
  const input = document.getElementById('chatTextInput');
  if (!input) return;
  const text = input.value.trim();

  if (!text && !pendingChatImage) return;

  const currentUser = getUser() || 'shonll';
  const now = new Date();
  const timeStr = now.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });

  let newMsg;
  if (pendingChatImage) {
    newMsg = {
      id: 'op_' + Date.now(),
      type: 'image',
      user: currentUser,
      time: timeStr,
      imageUrl: pendingChatImage.dataUrl,
      caption: text || ''
    };
    clearPendingChatImage();
  } else {
    newMsg = {
      id: 'op_' + Date.now(),
      type: 'text',
      user: currentUser,
      time: timeStr,
      text: text
    };
  }

  input.value = '';

  // Оптимистичное обновление UI
  chatMessagesCache.push(newMsg);
  saveLocalChatMessages(chatMessagesCache);
  renderOperatorChatMessages();
  playChime();

  // Отправка на сервер
  try {
    const res = await apiFetch('/api/chat/messages', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(newMsg)
    });
    if (res.ok) {
      const data = await res.json();
      if (data && data.message) {
        const idx = chatMessagesCache.findIndex(m => m.id === newMsg.id);
        if (idx !== -1) chatMessagesCache[idx] = data.message;
        saveLocalChatMessages(chatMessagesCache);
      }
    }
  } catch (_) {}
}

async function deleteOperatorMessage(msgId) {
  chatMessagesCache = chatMessagesCache.filter(x => x.id !== msgId);
  saveLocalChatMessages(chatMessagesCache);
  renderOperatorChatMessages();
  toast('Сообщение удалено', 'ok');

  try {
    await apiFetch('/api/chat/messages/' + encodeURIComponent(msgId), {
      method: 'DELETE'
    });
  } catch (_) {}
}

function copySharedToken(msgId) {
  const m = chatMessagesCache.find(x => x.id === msgId);
  if (!m || !m.account || !m.account.cookie) {
    toast('Токен не найден', 'err');
    return;
  }
  navigator.clipboard.writeText(m.account.cookie).then(() => {
    toast('Токен .ROBLOSECURITY скопирован в буфер!', 'ok');
  }).catch(() => {
    toast('Ошибка копирования', 'err');
  });
}

function loginFromChat(msgId, btn) {
  const m = chatMessagesCache.find(x => x.id === msgId);
  if (!m || !m.account || !m.account.cookie) {
    toast('Токен аккаунта не найден', 'err');
    return;
  }
  loginToRoblox(m.account.cookie, btn);
}

function loginToRoblox(token, btn, fileId) {
  if (!token) return;
  const originalHtml = btn ? btn.innerHTML : '';
  if (btn) {
    btn.innerHTML = '⏳ <span>Входим...</span>';
    btn.disabled = true;
  }

  function handler(e) {
    if (e.data && e.data.type === 'nexus-login-response') {
      window.removeEventListener('message', handler);
      if (e.data.ok) {
        if (fileId) {
          try {
            apiFetch('/api/login-mark', {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({ filename: fileId })
            }).catch(()=>{});
            if (window.allTokens) {
              const tokenData = window.allTokens.find(t => t.file === fileId);
              if (tokenData) tokenData.lastLogin = Date.now();
            }
          } catch (_) {}
        }
        const d = new Date();
        if (btn) {
          btn.innerHTML = `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><polyline points="20 6 9 17 4 12"/></svg> <span>Заходили (${d.toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'})})</span>`;
          btn.classList.add('logged-in');
          btn.disabled = false;
        }
        toast('Вход выполнен, открываем Roblox...', 'ok');
      } else {
        restoreBtnText();
        toast('Установите расширение NEXUS для авторизации', 'err');
      }
    }
  }

  function restoreBtnText() {
    if (btn) {
      btn.innerHTML = originalHtml || `<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><path d="M15 3h4a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2h-4"/><polyline points="10 17 15 12 10 7"/><line x1="15" y1="12" x2="3" y2="12"/></svg> <span>Войти в аккаунт</span>`;
      btn.classList.remove('logged-in');
      btn.disabled = false;
    }
  }

  window.addEventListener('message', handler);
  window.postMessage({ type: 'nexus-login', token }, '*');
  setTimeout(() => {
    window.removeEventListener('message', handler);
    restoreBtnText();
    toast('Установите расширение NEXUS для авторизации', 'err');
  }, 1100);
}

// ── Выбор аккаунта для отправки (РЕАЛЬНЫЕ АККАУНТЫ, НИКАКИХ ШАБЛОНОВ) ──────
function openAccountPickerForChat() {
  ensureAccountPickerModal();
  const overlay = document.getElementById('chatAccPickerModal');
  if (overlay) {
    overlay.classList.add('show');
    renderAccountPickerList();
    const s = document.getElementById('capSearchInput');
    if (s) {
      s.value = '';
      setTimeout(() => s.focus(), 60);
    }
  }
}

function closeAccountPickerForChat() {
  const overlay = document.getElementById('chatAccPickerModal');
  if (overlay) overlay.classList.remove('show');
}

async function getAvailableAccountsForChat() {
  if (Array.isArray(window.allTokens) && window.allTokens.length > 0) {
    return window.allTokens.map(t => ({
      username: t.username || t.user || 'Roblox User',
      userId: t.userId || null,
      robux: t.robux || 0,
      rap: t.rap || 0,
      cookie: t.security || t.cookie || '',
      games: Array.isArray(t.bookmarks) && t.bookmarks.length > 0 ? t.bookmarks : ['Murder Mystery 2'],
      note: t.computer ? `ПК: ${t.computer}` : ''
    }));
  }

  if (cachedChatAccounts) return cachedChatAccounts;
  if (isFetchingChatAccounts) return [];

  isFetchingChatAccounts = true;
  try {
    const res = await apiFetch('/tokens-data');
    if (res.ok) {
      const data = await res.json();
      if (Array.isArray(data)) {
        cachedChatAccounts = data.map(t => ({
          username: t.username || t.user || 'Roblox User',
          userId: t.userId || null,
          robux: t.robux || 0,
          rap: t.rap || 0,
          cookie: t.security || t.cookie || '',
          games: Array.isArray(t.bookmarks) && t.bookmarks.length > 0 ? t.bookmarks : ['Murder Mystery 2'],
          note: t.computer ? `ПК: ${t.computer}` : ''
        }));
        return cachedChatAccounts;
      }
    }
  } catch (_) {} finally {
    isFetchingChatAccounts = false;
  }
  return [];
}

async function renderAccountPickerList(query = '') {
  const listEl = document.getElementById('capAccountsList');
  if (!listEl) return;
  
  listEl.innerHTML = '<div style="text-align:center; padding:28px; color:var(--text-muted); font-size:0.85rem;">⏳ Загрузка реальных аккаунтов...</div>';

  const accounts = await getAvailableAccountsForChat();
  const q = query.toLowerCase().trim();

  const filtered = accounts.filter(a => {
    if (!q) return true;
    const u = (a.username || '').toLowerCase();
    const id = String(a.userId || '');
    return u.includes(q) || id.includes(q);
  });

  window._chatFilteredAccounts = filtered;

  if (filtered.length === 0) {
    listEl.innerHTML = `
      <div class="cap-empty-box">
        <div class="cap-empty-icon">📂</div>
        <div class="cap-empty-text">Нет доступных аккаунтов</div>
        <div class="cap-empty-sub">Когда в базу поступят реальные логи, вы сможете делиться ими в 1 клик.</div>
      </div>
    `;
    return;
  }

  listEl.innerHTML = filtered.map((acc, idx) => {
    const robuxStr = (acc.robux || 0).toLocaleString();
    return `
      <div class="cap-item">
        <div class="cap-item-left">
          ${renderRobloxAvatar(acc.userId, acc.username)}
          <div>
            <div class="cap-item-name">${escapeHtml(acc.username || 'Roblox_User')}</div>
            <div class="cap-item-sub">ID: ${escapeHtml(acc.userId || '—')}</div>
          </div>
        </div>
        <div class="cap-item-robux">${robuxStr} R$</div>
        <div class="cap-item-btns">
          <button type="button" class="cap-btn-flex" onclick="sendAccountShareFromPicker(${idx}, 'flex')" title="Показать баланс и статистику без токена">
            Показать баланс
          </button>
          <button type="button" class="cap-btn-token" onclick="sendAccountShareFromPicker(${idx}, 'token')" title="Отправить токен для быстрого входа операторам">
            Передать для входа
          </button>
        </div>
      </div>
    `;
  }).join('');
}

async function sendAccountShareFromPicker(index, mode) {
  const accounts = (window._chatFilteredAccounts && window._chatFilteredAccounts.length > 0)
    ? window._chatFilteredAccounts
    : await getAvailableAccountsForChat();
  const acc = accounts[index];
  if (!acc) return;
  closeAccountPickerForChat();

  const now = new Date();
  const timeStr = now.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  const currentUser = getUser() || 'shonll';

  let msg;
  if (mode === 'flex') {
    msg = {
      id: 'op_' + Date.now(),
      type: 'account_flex',
      user: currentUser,
      time: timeStr,
      caption: 'Выписка по аккаунту: баланс и статистика без передачи токена.',
      account: {
        username: acc.username,
        userId: acc.userId,
        robux: acc.robux,
        rap: acc.rap,
        games: acc.games || ['Murder Mystery 2'],
        note: acc.note || ''
      }
    };
    toast('Выписка по аккаунту отправлена в чат!', 'ok');
  } else {
    msg = {
      id: 'op_' + Date.now(),
      type: 'account_token',
      user: currentUser,
      time: timeStr,
      caption: 'Передана сессия аккаунта. Вход в один клик по кнопке «Войти в 1 клик»:',
      account: {
        username: acc.username,
        userId: acc.userId,
        robux: acc.robux,
        cookie: acc.cookie,
        games: acc.games || ['Murder Mystery 2']
      }
    };
    toast('Сессия аккаунта передана операторам!', 'ok');
  }

  chatMessagesCache.push(msg);
  saveLocalChatMessages(chatMessagesCache);
  renderOperatorChatMessages();

  try {
    await apiFetch('/api/chat/messages', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(msg)
    });
  } catch (_) {}
}

// ── Просмотрщик картинок (Lightbox) ──────────────────────────────────────────
function openChatLightbox(url) {
  ensureLightboxModal();
  const lb = document.getElementById('chatLightboxOverlay');
  const img = document.getElementById('chatLightboxImg');
  if (lb && img) {
    img.src = url;
    lb.classList.add('show');
  }
}

function closeChatLightbox() {
  const lb = document.getElementById('chatLightboxOverlay');
  if (lb) lb.classList.remove('show');
}

// ── Голосовые сообщения (ГС) ────────────────────────────────────────────────
async function startVoiceRecording() {
  const panel = document.getElementById('chatRecordingPanel');
  const form = document.getElementById('chatInputForm');
  if (panel && form) {
    panel.classList.add('active');
    form.style.display = 'none';
  }

  recordingSeconds = 0;
  updateRecordingTimer();
  audioChunks = [];

  try {
    const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
    mediaRecorder = new MediaRecorder(stream);
    mediaRecorder.ondataavailable = e => {
      if (e.data && e.data.size > 0) audioChunks.push(e.data);
    };
    mediaRecorder.onstop = () => {
      stream.getTracks().forEach(t => t.stop());
      if (audioChunks.length > 0) {
        const blob = new Blob(audioChunks, { type: 'audio/webm' });
        const reader = new FileReader();
        reader.onload = async evt => {
          const audioUrl = evt.target.result;
          const durStr = formatSeconds(recordingSeconds || 3);
          const now = new Date();
          const timeStr = now.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });

          const msg = {
            id: 'op_' + Date.now(),
            type: 'voice',
            user: getUser() || 'shonll',
            time: timeStr,
            duration: durStr,
            caption: 'Голосовое сообщение',
            audioUrl: audioUrl
          };

          chatMessagesCache.push(msg);
          saveLocalChatMessages(chatMessagesCache);
          renderOperatorChatMessages();

          try {
            await apiFetch('/api/chat/messages', {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify(msg)
            });
          } catch (_) {}
        };
        reader.readAsDataURL(blob);
      }
    };
    mediaRecorder.start();
    recordingInterval = setInterval(() => {
      recordingSeconds++;
      updateRecordingTimer();
    }, 1000);
  } catch (err) {
    console.warn('Microphone access unavailable, running simulation:', err);
    recordingInterval = setInterval(() => {
      recordingSeconds++;
      updateRecordingTimer();
    }, 1000);
  }
}

function updateRecordingTimer() {
  const el = document.getElementById('crpTimer');
  if (el) el.textContent = formatSeconds(recordingSeconds);
}

function stopAndSendVoiceRecording() {
  clearInterval(recordingInterval);
  const panel = document.getElementById('chatRecordingPanel');
  const form = document.getElementById('chatInputForm');
  if (panel && form) {
    panel.classList.remove('active');
    form.style.display = 'flex';
  }

  if (mediaRecorder && mediaRecorder.state !== 'inactive') {
    mediaRecorder.stop();
  } else {
    const now = new Date();
    const timeStr = now.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    const durStr = formatSeconds(recordingSeconds || 3);

    const msg = {
      id: 'op_' + Date.now(),
      type: 'voice',
      user: getUser() || 'shonll',
      time: timeStr,
      duration: durStr,
      caption: 'Голосовое сообщение',
      audioUrl: ''
    };

    chatMessagesCache.push(msg);
    saveLocalChatMessages(chatMessagesCache);
    renderOperatorChatMessages();

    apiFetch('/api/chat/messages', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(msg)
    }).catch(()=>{});
  }
}

function cancelVoiceRecording() {
  clearInterval(recordingInterval);
  const panel = document.getElementById('chatRecordingPanel');
  const form = document.getElementById('chatInputForm');
  if (panel && form) {
    panel.classList.remove('active');
    form.style.display = 'flex';
  }
  if (mediaRecorder && mediaRecorder.state !== 'inactive') {
    mediaRecorder.onstop = null;
    mediaRecorder.stop();
  }
  audioChunks = [];
}

function formatSeconds(sec) {
  const m = Math.floor(sec / 60);
  const s = sec % 60;
  return m + ':' + (s < 10 ? '0' : '') + s;
}

function playVoiceMessage(msgId, btn) {
  const player = document.getElementById('voice-' + msgId);
  const m = chatMessagesCache.find(x => x.id === msgId);

  if (currentAudioPlayer) {
    currentAudioPlayer.pause();
    currentAudioPlayer = null;
    document.querySelectorAll('.chat-voice-player.playing').forEach(p => p.classList.remove('playing'));
    document.querySelectorAll('.cvp-play-btn').forEach(b => {
      b.innerHTML = '<svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor"><polygon points="5 3 19 12 5 21 5 3"></polygon></svg>';
    });
  }

  if (!m || !m.audioUrl) {
    try {
      const ctx = new (window.AudioContext || window.webkitAudioContext)();
      const osc = ctx.createOscillator();
      const gain = ctx.createGain();
      osc.type = 'sine';
      osc.frequency.setValueAtTime(440, ctx.currentTime);
      gain.gain.setValueAtTime(0.08, ctx.currentTime);
      gain.gain.exponentialRampToValueAtTime(0.0001, ctx.currentTime + 1.2);
      osc.connect(gain);
      gain.connect(ctx.destination);
      osc.start();
      osc.stop(ctx.currentTime + 1.2);
    } catch (_) {}

    if (player) {
      player.classList.add('playing');
      btn.innerHTML = '<svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor"><rect x="6" y="4" width="4" height="16"></rect><rect x="14" y="4" width="4" height="16"></rect></svg>';
      setTimeout(() => {
        player.classList.remove('playing');
        btn.innerHTML = '<svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor"><polygon points="5 3 19 12 5 21 5 3"></polygon></svg>';
      }, 1400);
    }
    return;
  }

  const audio = new Audio(m.audioUrl);
  currentAudioPlayer = audio;
  if (player) player.classList.add('playing');
  btn.innerHTML = '<svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor"><rect x="6" y="4" width="4" height="16"></rect><rect x="14" y="4" width="4" height="16"></rect></svg>';

  audio.onended = () => {
    if (player) player.classList.remove('playing');
    btn.innerHTML = '<svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor"><polygon points="5 3 19 12 5 21 5 3"></polygon></svg>';
    currentAudioPlayer = null;
  };
  audio.play();
}

// ── Модальное окно чата операторов (Чистый UI) ────────────────────────────────
function ensureOperatorChatModal() {
  if (document.getElementById('operatorChatModal')) return;

  const div = document.createElement('div');
  div.id = 'operatorChatModal';
  div.className = 'chat-modal-overlay';
  div.onclick = e => {
    if (e.target === div) closeOperatorChat();
  };

  div.innerHTML = `
    <div class="operator-chat-window">
      <!-- Шапка с аватарами онлайн и кнопкой режима -->
      <div class="chat-win-header">
        <div class="cwh-left">
          <div class="cwh-title">
            <svg class="cwh-icon" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2"><path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/></svg>
            <span>Чат</span>
          </div>
          <div class="cwh-online-wrap" id="cwhPresenceStatus">
            <span class="cwh-online-label">В сети:</span>
            <div class="cwh-online-avatars" id="cwhOnlineAvatars"></div>
          </div>
        </div>
        <div class="cwh-right">
          <button type="button" class="cwh-btn-call" id="cwhBtnCall" onclick="handleChatCallClick()" title="Аудио/Видео звонок (с шумоподавлением)">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72 12.84 12.84 0 0 0 .7 2.81 2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45 12.84 12.84 0 0 0 2.81.7A2 2 0 0 1 22 16.92z"></path></svg>
            <span>Звонок</span>
          </button>
          <button type="button" class="cwh-btn-toggle-mode" id="cwhBtnToggleMode" onclick="toggleChatWindowMode()" title="Развернуть / Свернуть">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="15 3 21 3 21 9"></polyline><polyline points="9 21 3 21 3 15"></polyline><line x1="21" y1="3" x2="14" y2="10"></line><line x1="3" y1="21" x2="10" y2="14"></line></svg>
          </button>
          <button type="button" class="cwh-btn-close" onclick="closeOperatorChat()" title="Закрыть чат (Esc)">✕</button>
        </div>
      </div>

      <!-- Лента сообщений -->
      <div class="chat-messages-stream" id="operatorChatFeed" ondragover="handleChatDragOver(event)" ondragleave="handleChatDragLeave(event)" ondrop="handleChatDrop(event)"></div>

      <!-- Область ввода -->
      <div class="chat-input-area">
        <!-- Панель предпросмотра прикрепленного фото/скриншота -->
        <div class="chat-attach-preview-bar" id="chatAttachPreviewBar"></div>

        <!-- Скрытый инпут выбора файла -->
        <input type="file" id="chatFileInput" accept="image/*,video/*" style="display:none;" onchange="handleChatFileSelect(event)">

        <!-- Панель записи голосового сообщения -->
        <div class="chat-recording-panel" id="chatRecordingPanel">
          <div class="crp-left">
            <div class="crp-dot"></div>
            <span class="crp-text">Запись аудио...</span>
            <span class="crp-timer" id="crpTimer">0:00</span>
          </div>
          <div class="crp-actions">
            <button type="button" class="crp-btn-cancel" onclick="cancelVoiceRecording()">Отмена</button>
            <button type="button" class="crp-btn-send" onclick="stopAndSendVoiceRecording()">Отправить</button>
          </div>
        </div>

        <!-- Выпадающий список упоминаний (@оператор) -->
        <div class="chat-mention-dropdown" id="chatMentionDropdown"></div>

        <!-- Форма ввода с кнопкой аккаунтов (👤) и скрепкой (📎) внутри -->
        <form class="chat-input-form" id="chatInputForm" onsubmit="handleSendOperatorMessage(event)">
          <button type="button" class="chat-input-action-btn chat-share-btn" onclick="openAccountPickerForChat()" title="Поделиться аккаунтом из базы">
            <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"></path>
              <circle cx="12" cy="7" r="4"></circle>
            </svg>
          </button>
          <button type="button" class="chat-input-action-btn chat-paperclip-btn" onclick="triggerChatFileSelect()" title="Прикрепить скриншот (📎) или выберите файл">
            <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <path d="m21.44 11.05-9.19 9.19a6 6 0 0 1-8.49-8.49l8.57-8.57A4 4 0 1 1 18 8.84l-8.59 8.57a2 2 0 0 1-2.83-2.83l8.49-8.48"></path>
            </svg>
          </button>
          <input type="text" id="chatTextInput" class="chat-text-input" placeholder="Написать сообщение... (@ для упоминания, Ctrl+V для скриншота)" autocomplete="off">
          <button type="button" class="chat-mic-btn" onclick="startVoiceRecording()" title="Записать голосовое сообщение">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 1a3 3 0 0 0-3 3v8a3 3 0 0 0 6 0V4a3 3 0 0 0-3-3z"></path><path d="M19 10v2a7 7 0 0 1-14 0v-2"></path><line x1="12" y1="19" x2="12" y2="23"></line><line x1="8" y1="23" x2="16" y2="23"></line></svg>
          </button>
          <button type="submit" class="chat-send-btn" title="Отправить сообщение">
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="22" y1="2" x2="11" y2="13"></line><polygon points="22 2 15 22 11 13 2 9 22 2"></polygon></svg>
          </button>
        </form>
      </div>
    </div>
  `;

  document.body.appendChild(div);
  initChatWindowDrag();

  // Слушатели поля ввода для упоминаний
  const chatInput = div.querySelector('#chatTextInput');
  if (chatInput) {
    chatInput.addEventListener('input', handleChatInputMentions);
    chatInput.addEventListener('keydown', handleChatInputKeydown);
  }

  // Скрытие выпадающего списка при клике вне него
  document.addEventListener('click', e => {
    if (!e.target.closest('#chatMentionDropdown') && e.target.id !== 'chatTextInput') {
      hideMentionDropdown();
    }
  });

  // Esc key listener
  document.addEventListener('keydown', e => {
    if (e.key === 'Escape') {
      closeOperatorChat();
      closeAccountPickerForChat();
      closeChatLightbox();
    }
  });

  // Вставка скриншота из буфера обмена (Ctrl + V)
  document.addEventListener('paste', async e => {
    const modal = document.getElementById('operatorChatModal');
    if (!modal || !modal.classList.contains('show')) return;
    const items = (e.clipboardData || (e.originalEvent && e.originalEvent.clipboardData) || {}).items || [];
    for (const item of items) {
      if (item.type && item.type.startsWith('image/')) {
        const file = item.getAsFile();
        if (file) {
          e.preventDefault();
          await setPendingChatImage(file, 'Скриншот из буфера');
          break;
        }
      }
    }
  });

  // Синхронизация между вкладками
  window.addEventListener('storage', function(e) {
    if (e.key === 'ft_operator_chat_v7') {
      chatMessagesCache = getLocalChatMessages();
      renderOperatorChatMessages();
    }
  });
}

function ensureAccountPickerModal() {
  if (document.getElementById('chatAccPickerModal')) return;

  const overlay = document.createElement('div');
  overlay.id = 'chatAccPickerModal';
  overlay.className = 'chat-acc-picker-overlay';
  overlay.onclick = e => {
    if (e.target === overlay) closeAccountPickerForChat();
  };

  overlay.innerHTML = `
    <div class="chat-acc-picker-box">
      <div class="cap-header">
        <div class="cap-title">Выбор реального аккаунта для отправки</div>
        <button type="button" class="cwh-btn-close chb-close-btn" title="Закрыть" onclick="closeAccountPickerForChat()">✕</button>
      </div>
      <div class="cap-search">
        <input type="text" id="capSearchInput" placeholder="Поиск по нику или ID Roblox..." oninput="renderAccountPickerList(this.value)">
      </div>
      <div class="cap-list" id="capAccountsList"></div>
    </div>
  `;

  document.body.appendChild(overlay);
}

function ensureLightboxModal() {
  if (document.getElementById('chatLightboxOverlay')) return;
  const lb = document.createElement('div');
  lb.id = 'chatLightboxOverlay';
  lb.className = 'chat-lightbox-overlay';
  lb.onclick = closeChatLightbox;
  lb.innerHTML = `<img id="chatLightboxImg" src="" alt="Zoom" onclick="event.stopPropagation(); closeChatLightbox();">`;
  document.body.appendChild(lb);
}

// ═════════════════════════════════════════════════════════════════════════════
// МОДУЛЬ ЗВОНКОВ (NEXUS Calls Integration)
// ═════════════════════════════════════════════════════════════════════════════
function ensureCallScriptLoaded() {
  if (window.nexusCall) return Promise.resolve();
  return new Promise((resolve) => {
    const existing = document.querySelector('script[src*="assets/call.js"]');
    if (existing) {
      existing.addEventListener('load', () => resolve());
      setTimeout(resolve, 500);
      return;
    }
    const script = document.createElement('script');
    script.src = 'assets/call.js?v=240';
    script.onload = () => resolve();
    script.onerror = () => resolve();
    document.head.appendChild(script);
  });
}

async function handleChatCallClick() {
  await ensureCallScriptLoaded();
  if (typeof window.toggleNexusCall === 'function') {
    window.toggleNexusCall();
  } else {
    toast('Модуль голосовой связи загружается...', 'ok');
  }
}

// ═════════════════════════════════════════════════════════════════════════════
// ПРОФИЛИ ВОРКЕРОВ И МОДЕРАЦИЯ (Worker Profile System)
// ═════════════════════════════════════════════════════════════════════════════
function ensureWorkerProfileModal() {
  if (document.getElementById('workerProfileOverlay')) return;
  const overlay = document.createElement('div');
  overlay.id = 'workerProfileOverlay';
  overlay.className = 'worker-profile-overlay';
  overlay.onclick = (e) => {
    if (e.target === overlay) closeWorkerProfile();
  };
  overlay.innerHTML = `
    <div class="worker-profile-card">
      <div class="wpc-header-banner">
        <button type="button" class="wpc-close-btn" onclick="closeWorkerProfile()" title="Закрыть (Esc)">✕</button>
      </div>
      <div id="wpcDynamicBody"></div>
    </div>
  `;
  document.body.appendChild(overlay);
}

function closeWorkerProfile() {
  const overlay = document.getElementById('workerProfileOverlay');
  if (overlay) overlay.classList.remove('show');
}

async function openWorkerProfile(username) {
  ensureWorkerProfileModal();
  const overlay = document.getElementById('workerProfileOverlay');
  const cardBody = document.getElementById('wpcDynamicBody');
  if (!overlay || !cardBody) return;

  const target = (username || '').trim();
  cardBody.innerHTML = '<div style="padding:48px 24px; text-align:center; color:var(--text-muted); font-size:0.9rem;">⏳ Загрузка профиля воркера...</div>';
  overlay.classList.add('show');

  try {
    const res = await apiFetch('/api/operators/' + encodeURIComponent(target));
    if (!res.ok) throw new Error('Пользователь не найден');
    const data = await res.json();
    renderWorkerProfileCard(data);
  } catch (err) {
    const isTargetAdmin = target.toLowerCase() === 'shonll';
    const fallback = {
      user: target,
      role: isTargetAdmin ? 'Админ' : 'Воркер',
      bio: '',
      isOnline: chatOnlineOperators.some(o => o.toLowerCase() === target.toLowerCase()),
      github: '',
      website: '',
      telegram: '',
      isMuted: false,
      isBanned: false
    };
    renderWorkerProfileCard(fallback);
  }
}

function renderWorkerProfileCard(data) {
  const cardBody = document.getElementById('wpcDynamicBody');
  if (!cardBody) return;

  const currentUser = (getUser() || '').toLowerCase();
  const targetUser = (data.user || '').toLowerCase();
  const isMe = targetUser === currentUser;
  const isAdmin = currentUser === 'shonll';
  const isTargetAdmin = targetUser === 'shonll';
  const isOnline = !!data.isOnline;
  const roleBadgeClass = isTargetAdmin ? 'admin' : 'worker';
  const roleText = isTargetAdmin ? 'Админ' : 'Воркер';
  const avatarHtml = operatorAvatarHTML(data.user, data.avatarImage, data.avatar);

  let linksHtml = '';
  if (data.github) {
    const ghUrl = data.github.startsWith('http') ? data.github : 'https://github.com/' + data.github;
    linksHtml += `
      <a href="${escapeHtml(ghUrl)}" target="_blank" rel="noopener noreferrer" class="wpc-link-item">
        <span class="wpc-link-icon">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M9 19c-5 1.5-5-2.5-7-3m14 6v-3.87a3.37 3.37 0 0 0-.94-2.61c3.14-.35 6.44-1.54 6.44-7A5.44 5.44 0 0 0 20 4.77 5.07 5.07 0 0 0 19.91 1S18.73.65 16 2.48a13.38 13.38 0 0 0-7 0C6.27.65 5.09 1 5.09 1A5.07 5.07 0 0 0 5 4.77a5.44 5.44 0 0 0-1.5 3.78c0 5.42 3.3 6.61 6.44 7A3.37 3.37 0 0 0 9 18.13V22"></path></svg>
        </span>
        <span style="flex:1; text-align:left;">GitHub</span>
        <span style="color:var(--text-muted); font-size:0.75rem;">${escapeHtml(data.github)} ↗</span>
      </a>
    `;
  }
  if (data.telegram) {
    const tgUsername = data.telegram.replace(/^@/, '');
    const tgUrl = data.telegram.startsWith('http') ? data.telegram : 'https://t.me/' + tgUsername;
    linksHtml += `
      <a href="${escapeHtml(tgUrl)}" target="_blank" rel="noopener noreferrer" class="wpc-link-item">
        <span class="wpc-link-icon" style="color:#0088cc;">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor"><path d="m20.665 3.717-17.73 6.837c-1.21.486-1.203 1.161-.222 1.462l4.552 1.42 10.532-6.645c.498-.303.953-.14.579.192l-8.533 7.701h-.002l-.313 4.672c.46 0 .663-.211.921-.46l2.211-2.15 4.599 3.397c.848.467 1.457.227 1.668-.785l3.019-14.228c.309-1.239-.473-1.8-1.282-1.421z"/></svg>
        </span>
        <span style="flex:1; text-align:left;">Telegram</span>
        <span style="color:var(--text-muted); font-size:0.75rem;">@${escapeHtml(tgUsername)} ↗</span>
      </a>
    `;
  }
  if (data.website) {
    const webUrl = data.website.startsWith('http') ? data.website : 'https://' + data.website;
    linksHtml += `
      <a href="${escapeHtml(webUrl)}" target="_blank" rel="noopener noreferrer" class="wpc-link-item">
        <span class="wpc-link-icon" style="color:var(--accent);">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"></circle><line x1="2" y1="12" x2="22" y2="12"></line><path d="M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z"></path></svg>
        </span>
        <span style="flex:1; text-align:left;">Веб-сайт</span>
        <span style="color:var(--text-muted); font-size:0.75rem;">${escapeHtml(data.website.replace(/^https?:\/\//, ''))} ↗</span>
      </a>
    `;
  }

  let modHtml = '';
  if (isAdmin && !isTargetAdmin) {
    const muteBtnText = data.isMuted ? 'Снять заглушку' : 'Заглушить в чате (24ч)';
    const banBtnText = data.isBanned ? 'Разбанить в чате' : 'Забанить в чате';
    modHtml = `
      <div class="wpc-moderation-box">
        <div class="wpc-mod-title">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/></svg>
          <span>Модерация оператора (Админ)</span>
        </div>
        <div class="wpc-mod-actions">
          <button type="button" class="wpc-btn-mute" onclick="toggleMuteOperator('${escapeHtml(data.user)}', ${!!data.isMuted})">
            ${escapeHtml(muteBtnText)}
          </button>
          <button type="button" class="wpc-btn-ban" onclick="toggleBanOperator('${escapeHtml(data.user)}', ${!!data.isBanned})">
            ${escapeHtml(banBtnText)}
          </button>
        </div>
      </div>
    `;
  }

  let editBtnHtml = '';
  if (isMe) {
    editBtnHtml = `
      <a href="settings.html" class="btn-secondary" style="width:100%; margin-top:10px; display:flex; align-items:center; justify-content:center; gap:6px; font-size:0.84rem; text-decoration:none;">
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="3"></circle><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z"></path></svg>
        <span>Редактировать профиль в настройках</span>
      </a>
    `;
  }

  cardBody.innerHTML = `
    <div class="wpc-body">
      <div class="wpc-avatar-wrap">
        <div class="wpc-avatar-img">${avatarHtml}</div>
        <div class="wpc-online-dot ${isOnline ? '' : 'offline'}" title="${isOnline ? 'В сети' : 'Не в сети'}"></div>
      </div>
      <div class="wpc-name-row">
        <span class="wpc-username">${escapeHtml(data.displayName || data.user)}</span>
        <span class="wpc-role-badge ${roleBadgeClass}">${roleText}</span>
      </div>
      <div style="font-size:0.8rem; color:var(--text-muted); margin-bottom:6px;">@${escapeHtml(data.user)}</div>
      
      <div class="wpc-bio">${escapeHtml(data.bio || 'Участник команды NEXUS')}</div>

      ${linksHtml ? `<div class="wpc-links-grid">${linksHtml}</div>` : ''}

      <div class="wpc-meta-row">
        <span>Статус:</span>
        <span style="font-weight:600; color:${isOnline ? '#22c55e' : '#94a3b8'};">${isOnline ? '🟢 В сети' : '⚪ Не в сети'}</span>
      </div>

      ${modHtml}
      ${editBtnHtml}
    </div>
  `;
}

async function toggleMuteOperator(user, currentlyMuted) {
  try {
    const endpoint = currentlyMuted ? '/api/chat/unmute' : '/api/chat/mute';
    const body = currentlyMuted ? { user } : { user, hours: 24 };
    const res = await apiFetch(endpoint, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body)
    });
    if (res.ok) {
      toast(currentlyMuted ? `С пользователя @${user} снята заглушка` : `Пользователь @${user} заглушен на 24 часа`, 'ok');
      openWorkerProfile(user);
    } else {
      toast('Ошибка изменения статуса заглушки', 'err');
    }
  } catch (err) {
    toast('Ошибка модерации', 'err');
  }
}

async function toggleBanOperator(user, currentlyBanned) {
  try {
    const endpoint = currentlyBanned ? '/api/chat/unban' : '/api/chat/ban';
    const res = await apiFetch(endpoint, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ user })
    });
    if (res.ok) {
      toast(currentlyBanned ? `Пользователь @${user} разбанен в чате` : `Пользователь @${user} забанен в чате`, 'ok');
      openWorkerProfile(user);
    } else {
      toast('Ошибка изменения статуса бана', 'err');
    }
  } catch (err) {
    toast('Ошибка модерации', 'err');
  }
}

// ═════════════════════════════════════════════════════════════════════════════
// ПЛАВАЮЩИЙ FAB ЧАТА (Floating Action Button & Unread Counter)
// ═════════════════════════════════════════════════════════════════════════════
function ensureFloatingChatFab() {
  if (document.getElementById('floatingChatFab')) return;
  const fab = document.createElement('button');
  fab.id = 'floatingChatFab';
  fab.type = 'button';
  fab.className = 'floating-chat-fab';
  fab.title = 'Чат';
  fab.innerHTML = `
    <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
      <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"></path>
    </svg>
    <span class="chat-fab-badge" id="chatFabBadge">0</span>
  `;
  fab.onclick = toggleFloatingChatFromFab;
  document.body.appendChild(fab);
}

function toggleFloatingChatFromFab() {
  const modal = document.getElementById('operatorChatModal');
  if (modal && modal.classList.contains('show')) {
    closeOperatorChat();
  } else {
    openOperatorChat('mini');
  }
}

function updateChatFabBadge(unreadCount) {
  const badge = document.getElementById('chatFabBadge');
  const fab = document.getElementById('floatingChatFab');
  if (!badge || !fab) return;
  const modal = document.getElementById('operatorChatModal');
  const isOpen = modal && modal.classList.contains('show');

  if (isOpen) {
    fab.classList.add('chat-is-open');
    badge.style.display = 'none';
    badge.textContent = '0';
  } else {
    fab.classList.remove('chat-is-open');
    if (unreadCount > 0) {
      badge.textContent = unreadCount > 99 ? '99+' : unreadCount;
      badge.style.display = 'flex';
    } else {
      badge.style.display = 'none';
    }
  }
}

// ═════════════════════════════════════════════════════════════════════════════
// SPA ROUTER: Плавные бесшовные переходы без перезагрузки страниц
// Сохраняет виджеты чата, звонки и WebRTC аудио/видео каналы активными
// ═════════════════════════════════════════════════════════════════════════════
let isSpaNavigating = false;

function initSpaRouter() {
  if (window._nexusSpaInitialized) return;
  window._nexusSpaInitialized = true;

  // Перехват кликов по внутренним навигационным ссылкам
  document.addEventListener('click', function(e) {
    const link = e.target.closest('a');
    if (!link) return;

    const href = link.getAttribute('href');
    if (!href || href.startsWith('#') || href.startsWith('http') || href.startsWith('javascript:') || href.hasAttribute('download') || link.getAttribute('target') === '_blank') {
      return;
    }

    const validPages = ['index.html', 'tokens.html', 'bookmarks.html', 'updates.html', 'settings.html'];
    const targetFile = href.split('?')[0].split('#')[0];
    if (validPages.includes(targetFile)) {
      e.preventDefault();
      spaNavigate(href);
    }
  });

  window.addEventListener('popstate', function() {
    const path = location.pathname.split('/').pop() || 'index.html';
    spaNavigate(path, false);
  });
}

async function spaNavigate(url, pushState = true) {
  if (isSpaNavigating) return;
  const targetFile = url.split('?')[0].split('#')[0];
  const currentFile = location.pathname.split('/').pop() || 'index.html';

  if (targetFile === currentFile && pushState) return;

  isSpaNavigating = true;
  const mainContent = document.querySelector('.main-content');
  if (mainContent) {
    mainContent.classList.add('page-fade-out');
  }

  try {
    const res = await fetch(url);
    if (!res.ok) {
      location.href = url;
      return;
    }
    const htmlText = await res.text();
    const parser = new DOMParser();
    const doc = parser.parseFromString(htmlText, 'text/html');

    const newMain = doc.querySelector('.main-content');
    const newTitle = doc.querySelector('title');
    if (newTitle) document.title = newTitle.textContent;

    if (pushState) {
      history.pushState(null, '', url);
    }

    // Обновляем подсветку активного пункта меню в сайдбаре
    document.querySelectorAll('.sidebar .nav-link').forEach(nl => {
      const aHref = nl.getAttribute('href') || '';
      if (aHref === targetFile || (targetFile === 'index.html' && aHref.includes('index.html'))) {
        nl.classList.add('active');
      } else {
        nl.classList.remove('active');
      }
    });

    // Плавная замена основного контента
    if (mainContent && newMain) {
      mainContent.innerHTML = newMain.innerHTML;
      mainContent.classList.remove('page-fade-out');
      mainContent.classList.add('page-fade-in');
      setTimeout(() => mainContent.classList.remove('page-fade-in'), 220);
    }

    // Запуск скрипта страницы
    reinitPageScript(targetFile);
  } catch (err) {
    console.error('SPA nav fallback:', err);
    location.href = url;
  } finally {
    isSpaNavigating = false;
  }
}

function reinitPageScript(pageFile) {
  const scriptMap = {
    'index.html': 'assets/dashboard.js',
    'tokens.html': 'assets/tokens.js',
    'bookmarks.html': 'assets/bookmarks.js',
    'updates.html': 'assets/updates.js',
    'settings.html': 'assets/settings.js'
  };

  const scriptPath = scriptMap[pageFile];
  if (!scriptPath) return;

  const s = document.createElement('script');
  s.src = scriptPath + '?t=' + Date.now();
  s.onload = () => {
    s.remove();
  };
  document.body.appendChild(s);
}

// ── Глобальная автоинициализация виджетов на странице ───────────────────────────
document.addEventListener('DOMContentLoaded', () => {
  ensureOperatorChatModal();
  ensureFloatingChatFab();
  initSpaRouter();
  ensureCallScriptLoaded();
  if (localStorage.getItem('ft_chat_open') === '1') {
    const savedMode = localStorage.getItem('ft_chat_mode') || 'mini';
    openOperatorChat(savedMode);
  }
});

// Если DOMContentLoaded уже наступил
setTimeout(() => {
  ensureOperatorChatModal();
  ensureFloatingChatFab();
  initSpaRouter();
  ensureCallScriptLoaded();
  if (localStorage.getItem('ft_chat_open') === '1') {
    const savedMode = localStorage.getItem('ft_chat_mode') || 'mini';
    openOperatorChat(savedMode);
  }
}, 100);
