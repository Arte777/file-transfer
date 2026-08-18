// ── Чат операторов ─────────────────────────────────────────────────────────
if (!requireLogin()) throw new Error('redirect');

document.getElementById('sidebarSlot').innerHTML = renderHeader('chat');
bindLogout();

let chatMessages = [];
let userTokens = [];
let attachedToken = null;
let isSending = false;

// ── Загрузка сообщений ────────────────────────────────────────────────────────
async function loadChatMessages(autoScroll = false) {
  const container = document.getElementById('chatMessages');
  try {
    const resp = await apiFetch('/api/chat/messages');
    if (!resp.ok) return;
    const list = await resp.json();
    if (Array.isArray(list)) {
      chatMessages = list;
      renderChatMessages(autoScroll);
    }
  } catch (e) {
    if (e.message !== 'auth') {
      // container.innerHTML = '<div class="empty">Не удалось загрузить чат</div>';
    }
  }
}

// ── Отрисовка сообщений ───────────────────────────────────────────────────────
function renderChatMessages(autoScroll = true) {
  const container = document.getElementById('chatMessages');
  if (!container) return;

  if (!chatMessages.length) {
    container.innerHTML = '<div class="empty" style="margin: auto;">В чате пока нет сообщений. Напишите первым!</div>';
    return;
  }

  const isScrolledToBottom = container.scrollHeight - container.clientHeight <= container.scrollTop + 50;

  let html = '';
  for (const msg of chatMessages) {
    const sender = escapeHtml(msg.displayName || msg.operator || 'Operator');
    const timeStr = msg.createdAt ? new Date(msg.createdAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : '';
    const text = escapeHtml(msg.text || '');

    let avatarHtml = `<div class="chat-msg-avatar">${sender.charAt(0).toUpperCase()}</div>`;
    if (msg.avatarImage) {
      avatarHtml = `<div class="chat-msg-avatar"><img src="${msg.avatarImage}" alt="avatar"></div>`;
    }

    let tokenCardHtml = '';
    if (msg.tokenCard) {
      const tc = msg.tokenCard;
      const tUser = escapeHtml(tc.username || 'Roblox User');
      const tRobux = Number(tc.robux) || 0;
      const tRobuxStr = tRobux > 0 ? tRobux.toLocaleString() + ' R$' : '0 R$';
      const tPc = escapeHtml(tc.computer || '—');

      let tAvatarHtml = `<div class="chat-token-showcase-avatar"><svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"></path><circle cx="12" cy="7" r="4"></circle></svg></div>`;
      if (tc.userId) {
        tAvatarHtml = `<div class="chat-token-showcase-avatar"><img src="${API_BASE}/avatar-proxy/${tc.userId}" onerror="this.outerHTML='<div class=\\'chat-token-showcase-avatar\\'>O</div>'"></div>`;
      }

      tokenCardHtml = `
        <div class="chat-token-showcase">
          ${tAvatarHtml}
          <div class="chat-token-showcase-details">
            <div class="chat-token-showcase-user">${tUser}</div>
            <div class="chat-token-showcase-sub">ПК: ${tPc} • Только баланс</div>
          </div>
          <div class="chat-token-showcase-robux">${tRobuxStr}</div>
        </div>
      `;
    }

    html += `
      <div class="chat-msg-row">
        ${avatarHtml}
        <div class="chat-msg-content">
          <div class="chat-msg-meta">
            <span class="chat-msg-sender">${sender}</span>
            <span class="chat-msg-time">${timeStr}</span>
          </div>
          ${text ? `<div class="chat-msg-text">${text}</div>` : ''}
          ${tokenCardHtml}
        </div>
      </div>
    `;
  }

  container.innerHTML = html;

  if (autoScroll || isScrolledToBottom) {
    container.scrollTop = container.scrollHeight;
  }
}

// ── Отправка сообщения ────────────────────────────────────────────────────────
async function sendMessage() {
  if (isSending) return;
  const input = document.getElementById('chatInput');
  const text = input ? input.value.trim() : '';

  if (!text && !attachedToken) {
    return;
  }

  isSending = true;
  const btn = document.getElementById('btnSendChat');
  if (btn) btn.disabled = true;

  try {
    const payload = {
      text: text,
      tokenCard: attachedToken ? {
        username: attachedToken.username || attachedToken.user,
        userId: attachedToken.userId,
        robux: attachedToken.robux || 0,
        computer: attachedToken.computer
      } : null
    };

    const resp = await apiFetch('/api/chat/messages', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });

    if (resp.ok) {
      if (input) input.value = '';
      detachToken();
      await loadChatMessages(true);
    } else {
      toast('Ошибка отправки сообщения', 'err');
    }
  } catch (e) {
    if (e.message !== 'auth') toast('Ошибка соединения', 'err');
  } finally {
    isSending = false;
    if (btn) btn.disabled = false;
    if (input) input.focus();
  }
}

// ── Выбор токена для прикрепления ─────────────────────────────────────────────
document.getElementById('btnOpenTokenPicker')?.addEventListener('click', async () => {
  const modal = document.getElementById('tokenPickerModal');
  const listEl = document.getElementById('tokenPickerList');
  if (modal) modal.classList.add('active');

  if (listEl) {
    listEl.innerHTML = '<div style="text-align: center; color: var(--text-muted); padding: 1.5rem;">Загрузка токенов...</div>';
  }

  try {
    const resp = await apiFetch('/tokens-data');
    if (resp.ok) {
      userTokens = await resp.json();
      renderTokenPickerList();
    } else {
      if (listEl) listEl.innerHTML = '<div class="empty">Не удалось загрузить токены</div>';
    }
  } catch (e) {
    if (listEl) listEl.innerHTML = '<div class="empty">Ошибка сети</div>';
  }
});

function closeTokenPicker() {
  const modal = document.getElementById('tokenPickerModal');
  if (modal) modal.classList.remove('active');
}

function renderTokenPickerList() {
  const listEl = document.getElementById('tokenPickerList');
  if (!listEl) return;

  if (!userTokens.length) {
    listEl.innerHTML = '<div class="empty">У вас нет активных токенов</div>';
    return;
  }

  let html = '';
  userTokens.forEach((t, idx) => {
    const username = escapeHtml(t.username || t.user || 'Roblox User');
    const robux = (t.robux !== undefined && t.robux !== null) ? Number(t.robux).toLocaleString() + ' R$' : '0 R$';
    const pc = escapeHtml(t.computer || '—');

    html += `
      <div class="token-picker-item" onclick="attachTokenByIndex(${idx})">
        <div style="display:flex; flex-direction:column; gap:2px; min-width:0;">
          <div style="font-weight:700; color:#fff; font-size:0.92rem;">${username}</div>
          <div style="font-size:0.78rem; color:var(--text-muted);">ПК: ${pc}</div>
        </div>
        <div class="chat-token-showcase-robux">${robux}</div>
      </div>
    `;
  });

  listEl.innerHTML = html;
}

function attachTokenByIndex(idx) {
  const t = userTokens[idx];
  if (!t) return;
  attachedToken = t;

  const previewBox = document.getElementById('attachedTokenPreview');
  const previewText = document.getElementById('attachedTokenText');
  const username = t.username || t.user || 'Roblox User';
  const robux = (t.robux !== undefined && t.robux !== null) ? Number(t.robux).toLocaleString() + ' R$' : '0 R$';

  if (previewText) previewText.textContent = `${username} (${robux})`;
  if (previewBox) previewBox.style.display = 'flex';

  closeTokenPicker();
  document.getElementById('chatInput')?.focus();
}

function detachToken() {
  attachedToken = null;
  const previewBox = document.getElementById('attachedTokenPreview');
  if (previewBox) previewBox.style.display = 'none';
}

// ── Слушатели событий ─────────────────────────────────────────────────────────
document.getElementById('btnSendChat')?.addEventListener('click', sendMessage);

document.getElementById('chatInput')?.addEventListener('keydown', (e) => {
  if (e.key === 'Enter' && !e.shiftKey) {
    e.preventDefault();
    sendMessage();
  }
});

// Загружаем сообщения и запускаем периодический опрос (polling)
loadChatMessages(true);
setInterval(() => {
  loadChatMessages(false);
}, 3000);
