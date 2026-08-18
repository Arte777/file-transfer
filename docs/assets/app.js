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

  function navLink(page, href, iconSvg, label, extraClass = '') {
    const cls = activePage === page ? 'nav-link active ' + extraClass : 'nav-link ' + extraClass;
    return `<a href="${href}" class="${cls.trim()}">
      <div class="nav-icon">${iconSvg}</div>
      <span class="nav-label">${label}</span>
    </a>`;
  }

  const iconDashboard = `<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="7" height="9"></rect><rect x="14" y="3" width="7" height="5"></rect><rect x="14" y="12" width="7" height="9"></rect><rect x="3" y="16" width="7" height="5"></rect></svg>`;
  const iconTokens = `<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 2l-2 2m-7.61 7.61a5.5 5.5 0 1 1-7.778 7.778 5.5 5.5 0 0 1 7.777-7.777zm0 0L15.5 7.5m0 0l3 3L22 7l-3-3m-3.5 3.5L19 4"></path></svg>`;
  const iconUpdates = `<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path><polyline points="7 10 12 15 17 10"></polyline><line x1="12" y1="15" x2="12" y2="3"></line></svg>`;
  const iconBuilder = `<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 2v20M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"></path></svg>`;
  const iconSettings = `<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="3"></circle><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z"></path></svg>`;

  return `<aside class="sidebar">
    <div class="logo">
      <span class="logo-text">NEXUS</span>
    </div>
    <div class="nav-links">
      ${navLink('files', 'index.html', iconDashboard, 'Файлы')}
      ${navLink('tokens', 'tokens.html', iconTokens, 'Токены')}
      ${navLink('updates', 'updates.html', iconUpdates, 'Обновления')}
      ${navLink('settings', 'settings.html', iconSettings, 'Настройки', 'desktop-only')}
      
      <!-- Mobile only Profile Link -->
      <a href="settings.html" class="nav-link mobile-profile-link ${activePage === 'settings' ? 'active' : ''}">
        <div class="nav-icon user-avatar" style="width: 24px; height: 24px; font-size: 1rem;">${avatarHtml}</div>
        <span class="nav-label">Профиль</span>
      </a>
    </div>
    
    <div class="sidebar-spacer" style="flex: 1;"></div>

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


