// ── Общие утилиты для статического сайта ──────────────────────────────────────
const IMG = ["jpg","jpeg","png","gif","webp","bmp","svg","avif"];
const VID = ["mp4","webm","mov","avi"];
const AUD = ["mp3","wav","flac","ogg","m4a"];
const TXT = ["txt","md","js","ts","html","css","cs","py","json","cpp","c","java"];

function ext(n) { return n.split(".").pop().toLowerCase(); }
function isImg(n) { return IMG.includes(ext(n)); }
function isText(n) { return TXT.includes(ext(n)); }
function isVid(n) { return VID.includes(ext(n)); }
function isAud(n) { return AUD.includes(ext(n)); }

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
  const resp = await fetch(API_BASE + path, opts);
  if (resp.status === 401 || resp.status === 403) {
    clearAuth();
    location.href = 'login.html';
    throw new Error('auth');
  }
  return resp;
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

// Аватарка оператора в шапке (с поддержкой кастомных настроек)
function operatorAvatar(user) {
  const custom = localStorage.getItem('ft_avatar');
  if (custom) return custom;
  return user === 'Shonll' ? '🦊' : '🐉';
}

// Отображаемое имя (с поддержкой кастомных настроек)
function operatorDisplayName(user) {
  const custom = localStorage.getItem('ft_displayName');
  if (custom) return custom;
  return user;
}

// Заполняет шапку (header) общим HTML для дашборда/токенов/настроек
function renderHeader(activePage) {
  const user = getUser();
  const avatar = operatorAvatar(user);
  const name = operatorDisplayName(user);
  const navFiles = activePage === 'files'
    ? '<a href="index.html" class="nav-link active">📁 Файлы</a>'
    : '<a href="index.html" class="nav-link">📁 Файлы</a>';
  const navTokens = activePage === 'tokens'
    ? '<a href="tokens.html" class="nav-link active">🎫 Токены</a>'
    : '<a href="tokens.html" class="nav-link">🎫 Токены</a>';
  const navSettings = activePage === 'settings'
    ? '<a href="settings.html" class="nav-link active">⚙️ Настройки</a>'
    : '<a href="settings.html" class="nav-link">⚙️ Настройки</a>';
  return `
  <header>
    <div class="logo">⚡ СИСТЕМА ПЕРЕДАЧИ ФАЙЛОВ</div>
    <div class="nav-links" style="margin-left:auto; margin-right:1rem;">
      ${navFiles}
      ${navTokens}
      ${navSettings}
    </div>
    <div class="user-badge">
      <span class="user-avatar">${avatar}</span>
      <span class="user-name">${escapeHtml(name)}</span>
      <a href="#" class="btn-logout" id="btnLogout">Выйти</a>
    </div>
  </header>`;
}

async function bindLogout() {
  const btn = document.getElementById('btnLogout');
  if (!btn) return;
  btn.addEventListener('click', async function(e) {
    e.preventDefault();
    try { await apiFetch('/api/logout', { method: 'POST' }); } catch (_) {}
    clearAuth();
    location.href = 'login.html';
  });
}
