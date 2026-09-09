// ── Страница настроек ──────────────────────────────────────────────────────────
if (!requireLogin()) throw new Error('redirect');

document.getElementById('sidebarSlot').innerHTML = renderHeader('settings');
bindLogout();

let currentSettings = {};
let currentAvatarImageBase64 = null;

// ── Загрузка текущих настроек ─────────────────────────────────────────────────
async function loadSettings() {
  // Загружаем локальные предпочтения интерфейса
  const localViewMode = localStorage.getItem('ft_viewMode') || 'grid';
  const localRefresh = localStorage.getItem('ft_autoRefresh') || '5000';
  const localChime = localStorage.getItem('ft_soundChime') !== '0';

  const selView = document.getElementById('prefDefaultView');
  const selRefresh = document.getElementById('prefAutoRefresh');
  const chkChime = document.getElementById('prefChime');

  if (selView) selView.value = localViewMode;
  if (selRefresh) selRefresh.value = localRefresh;
  if (chkChime) chkChime.checked = localChime;

  try {
    const r = await apiFetch('/api/settings');
    const s = await r.json();
    currentSettings = s;

    document.getElementById('displayName').value = s.displayName || localStorage.getItem('ft_displayName') || '';
    document.getElementById('bio').value = s.bio || localStorage.getItem('ft_bio') || '';
    if (document.getElementById('githubProfile')) document.getElementById('githubProfile').value = s.github || '';
    if (document.getElementById('websiteProfile')) document.getElementById('websiteProfile').value = s.website || '';
    if (document.getElementById('telegramProfile')) document.getElementById('telegramProfile').value = s.telegram || '';

    if (s.avatarImage) {
      currentAvatarImageBase64 = s.avatarImage;
      localStorage.setItem('ft_avatarImage', s.avatarImage);
      localStorage.removeItem('ft_avatar');
      document.getElementById('avatarInput').value = '';
    } else {
      currentAvatarImageBase64 = null;
      localStorage.removeItem('ft_avatarImage');
      const av = s.avatar || localStorage.getItem('ft_avatar') || '🦊';
      document.getElementById('avatarInput').value = av;
      localStorage.setItem('ft_avatar', av);
    }

    // Load banner
    if (s.bannerImage) {
      setBannerValue(s.bannerImage);
    }

    // Load effect
    const eff = s.profileEffect || 'none';
    const effInput = document.getElementById('profileEffectValue');
    if (effInput) effInput.value = eff;
    document.querySelectorAll('.effect-btn').forEach(btn => {
      btn.style.borderColor = btn.dataset.effect === eff ? 'var(--accent)' : 'var(--border)';
      btn.style.color = btn.dataset.effect === eff ? '#fff' : 'var(--text-muted)';
    });

    // Load decoration
    const deco = s.avatarDecoration || 'none';
    const decoInput = document.getElementById('avatarDecorationValue');
    if (decoInput) decoInput.value = deco;
    document.querySelectorAll('.deco-btn').forEach(btn => {
      btn.style.outline = btn.dataset.deco === deco ? '3px solid var(--accent)' : 'none';
    });

    updatePreview();
    highlightSelectedEmoji(s.avatar || '');
  } catch (e) {
    if (e.message !== 'auth') {
      const localAvatar = localStorage.getItem('ft_avatar');
      const localName = localStorage.getItem('ft_displayName');
      const localBio = localStorage.getItem('ft_bio');
      document.getElementById('displayName').value = localName || '';
      document.getElementById('bio').value = localBio || '';
      document.getElementById('avatarInput').value = localAvatar || '🦊';
      updatePreview();
    }
  }
}

// ── Обработка загрузки фото ───────────────────────────────────────────────────
document.getElementById('avatarFileInput')?.addEventListener('change', function(e) {
  const file = e.target.files[0];
  if (!file) return;

  if (file.size > 10 * 1024 * 1024) {
    toast('Файл слишком большой. Максимум 10 МБ.', 'err');
    return;
  }

  const reader = new FileReader();
  reader.onload = (e) => {
    const img = new Image();
    img.onload = () => {
      const canvas = document.createElement('canvas');
      const maxW = 320, maxH = 320;
      let w = img.width, h = img.height;
      if (w > maxW || h > maxH) {
        if (w > h) { h = Math.round((h * maxW) / w); w = maxW; }
        else { w = Math.round((w * maxH) / h); h = maxH; }
      }
      canvas.width = w; canvas.height = h;
      const ctx = canvas.getContext('2d');
      ctx.drawImage(img, 0, 0, w, h);
      currentAvatarImageBase64 = canvas.toDataURL('image/jpeg', 0.85);
      
      document.getElementById('avatarInput').value = '';
      highlightSelectedEmoji('');
      updatePreview();
      toast('📷 Фото выбрано! Нажмите «Сохранить настройки».');
    };
    img.onerror = () => {
      toast('Ошибка загрузки изображения', 'err');
    };
    img.src = e.target.result;
  };
  reader.readAsDataURL(file);
});

document.getElementById('btnResetAvatar')?.addEventListener('click', function() {
  currentAvatarImageBase64 = null;
  localStorage.removeItem('ft_avatarImage');
  document.getElementById('avatarFileInput').value = '';
  document.getElementById('avatarInput').value = '🦊';
  highlightSelectedEmoji('🦊');
  updatePreview();
  toast('Фото сброшено на эмодзи. Нажмите «Сохранить настройки».');
});

// ── Эмодзи пикер ─────────────────────────────────────────────────────────────
document.querySelectorAll('.emoji-btn').forEach(btn => {
  btn.addEventListener('click', function() {
    currentAvatarImageBase64 = null;
    document.getElementById('avatarFileInput').value = '';
    const emoji = this.dataset.emoji;
    document.getElementById('avatarInput').value = emoji;
    highlightSelectedEmoji(emoji);
    updatePreview();
  });
});

function highlightSelectedEmoji(emoji) {
  document.querySelectorAll('.emoji-btn').forEach(btn => {
    if (btn.dataset.emoji === emoji && !currentAvatarImageBase64) {
      btn.classList.add('active');
    } else {
      btn.classList.remove('active');
    }
  });
}

// ── Live Preview ─────────────────────────────────────────────────────────────
function updatePreview() {
  const preview = document.getElementById('previewAvatar');
  if (!preview) return;

  if (currentAvatarImageBase64) {
    preview.innerHTML = `<img src="${currentAvatarImageBase64}" alt="Avatar">`;
  } else {
    const emoji = document.getElementById('avatarInput')?.value.trim() || '👤';
    preview.innerHTML = escapeHtml(emoji);
  }
}

// ── Live preview на ввод ──────────────────────────────────────────────────────
['displayName', 'avatarInput', 'bio'].forEach(id => {
  const el = document.getElementById(id);
  if (!el) return;
  el.addEventListener('input', function() {
    if (id === 'avatarInput' && this.value.trim() !== '') {
      currentAvatarImageBase64 = null;
    }
    updatePreview();
  });
});

// ── Banner helpers ─────────────────────────────────────────────────────────────
const BANNER_PRESETS = {
  aurora: 'linear-gradient(135deg,#0b3954,#20dfb0)',
  fire:   'linear-gradient(135deg,#7a1200,#ffb347)',
  neon:   'linear-gradient(135deg,#2d0066,#cc00ff)',
  forest: 'linear-gradient(135deg,#143314,#55c355)',
  ocean:  'linear-gradient(135deg,#001a4f,#0098ff)',
  sunset: 'linear-gradient(135deg,#4b0066,#ff9800)',
  cyber:  'linear-gradient(135deg,#001a30,#00f0ff)',
  rose:   'linear-gradient(135deg,#4a003a,#ff6b9e)',
};

function setBannerValue(val) {
  const hidden = document.getElementById('bannerValue');
  const inner = document.getElementById('bannerPreviewInner');
  if (!hidden || !inner) return;
  hidden.value = val;
  if (val.startsWith('preset:')) {
    const key = val.replace('preset:', '');
    inner.style.background = BANNER_PRESETS[key] || 'linear-gradient(135deg,rgba(0,240,255,0.18),rgba(168,85,247,0.18))';
    inner.innerHTML = '';
  } else if (val.startsWith('data:image/') || val.startsWith('http')) {
    inner.style.background = 'none';
    inner.innerHTML = `<img src="${val}" style="width:100%;height:100%;object-fit:cover;">`;
  } else {
    inner.style.background = 'linear-gradient(135deg,rgba(0,240,255,0.18),rgba(168,85,247,0.18))';
    inner.innerHTML = '';
    hidden.value = '';
  }
  // highlight active swatch
  document.querySelectorAll('.banner-swatch').forEach(sw => {
    sw.style.borderColor = ('preset:' + sw.dataset.preset === val) ? 'var(--accent)' : 'transparent';
    sw.style.transform = ('preset:' + sw.dataset.preset === val) ? 'scale(1.06)' : '';
  });
}

// Banner preset swatches
document.querySelectorAll('.banner-swatch').forEach(sw => {
  sw.addEventListener('click', () => setBannerValue('preset:' + sw.dataset.preset));
});

// Banner clear
document.getElementById('btnClearBanner')?.addEventListener('click', () => setBannerValue(''));

// Banner file upload
document.getElementById('bannerFileInput')?.addEventListener('change', function(e) {
  const file = e.target.files[0];
  if (!file) return;
  if (file.size > 10 * 1024 * 1024) { toast('Файл слишком большой. Максимум 10 МБ.', 'err'); return; }
  const reader = new FileReader();
  reader.onload = ev => {
    const img = new Image();
    img.onload = () => {
      const canvas = document.createElement('canvas');
      const maxW = 800, maxH = 280;
      let w = img.width, h = img.height;
      if (w > maxW) { h = Math.round(h * maxW / w); w = maxW; }
      if (h > maxH) { w = Math.round(w * maxH / h); h = maxH; }
      canvas.width = w; canvas.height = h;
      canvas.getContext('2d').drawImage(img, 0, 0, w, h);
      setBannerValue(canvas.toDataURL('image/jpeg', 0.82));
      toast('🖼️ Баннер загружен! Нажмите «Сохранить настройки».');
    };
    img.src = ev.target.result;
  };
  reader.readAsDataURL(file);
});

// ── Effect buttons ─────────────────────────────────────────────────────────────
document.querySelectorAll('.effect-btn').forEach(btn => {
  btn.addEventListener('click', () => {
    const eff = btn.dataset.effect;
    const inp = document.getElementById('profileEffectValue');
    if (inp) inp.value = eff;
    document.querySelectorAll('.effect-btn').forEach(b => {
      b.style.borderColor = b.dataset.effect === eff ? 'var(--accent)' : 'var(--border)';
      b.style.color = b.dataset.effect === eff ? '#fff' : 'var(--text-muted)';
    });
  });
});

// ── Decoration buttons ─────────────────────────────────────────────────────────
document.querySelectorAll('.deco-btn').forEach(btn => {
  btn.addEventListener('click', () => {
    const deco = btn.dataset.deco;
    const inp = document.getElementById('avatarDecorationValue');
    if (inp) inp.value = deco;
    document.querySelectorAll('.deco-btn').forEach(b => {
      b.style.outline = b.dataset.deco === deco ? '3px solid var(--accent)' : 'none';
    });
  });
});

// ── Сохранение ────────────────────────────────────────────────────────────────
const saveBtn = document.getElementById('btnSaveSettings') || document.getElementById('btnSave');
if (saveBtn) {
  saveBtn.addEventListener('click', async function() {
    const btn = this;
    
    const name = document.getElementById('displayName')?.value.trim() || '';
    const bio = document.getElementById('bio')?.value.trim() || '';
    const github = document.getElementById('githubProfile')?.value.trim() || '';
    const website = document.getElementById('websiteProfile')?.value.trim() || '';
    const telegram = document.getElementById('telegramProfile')?.value.trim() || '';
    const currPwd = document.getElementById('currentPassword')?.value || '';
    const newPwd = document.getElementById('newPassword')?.value || '';
    
    // Сохраняем локальные параметры интерфейса
    const viewMode = document.getElementById('prefDefaultView')?.value || 'grid';
    const autoRefresh = document.getElementById('prefAutoRefresh')?.value || '5000';
    const soundChime = document.getElementById('prefChime')?.checked ? '1' : '0';

    localStorage.setItem('ft_viewMode', viewMode);
    localStorage.setItem('ft_autoRefresh', autoRefresh);
    localStorage.setItem('ft_soundChime', soundChime);

    const avatarVal = document.getElementById('avatarInput')?.value.trim() || '🦊';
    const bannerVal = document.getElementById('bannerValue')?.value || null;
    const effectVal = document.getElementById('profileEffectValue')?.value || 'none';
    const decoVal = document.getElementById('avatarDecorationValue')?.value || 'none';

    const data = {
      displayName: name,
      themeColor: '#3b82f6',
      bio: bio,
      github: github,
      website: website,
      telegram: telegram,
      avatar: avatarVal,
      avatarImage: currentAvatarImageBase64 || null,
      bannerImage: bannerVal || null,
      profileEffect: effectVal || null,
      avatarDecoration: decoVal || null,
    };

    if (newPwd) {
      data.newPassword = newPwd;
      data.currentPassword = currPwd;
    }

    btn.disabled = true;
    const originalText = btn.textContent;
    btn.textContent = 'Сохранение...';

    try {
      const r = await apiFetch('/api/settings', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(data)
      });
      const resp = await r.json();
      if (resp.success) {
        const passwordWasChanged = !!data.newPassword;
        toast('✅ Настройки сохранены');
        if (document.getElementById('newPassword')) document.getElementById('newPassword').value = '';
        if (document.getElementById('currentPassword')) document.getElementById('currentPassword').value = '';
        currentSettings = resp.settings || {};

        if (data.displayName) localStorage.setItem('ft_displayName', data.displayName);
        if (data.bio) localStorage.setItem('ft_bio', data.bio);

        if (currentSettings.avatarImage) {
          localStorage.setItem('ft_avatarImage', currentSettings.avatarImage);
          localStorage.removeItem('ft_avatar');
        } else {
          localStorage.removeItem('ft_avatarImage');
          localStorage.setItem('ft_avatar', currentSettings.avatar || avatarVal);
        }
        
        if (typeof remoteOperatorProfiles !== 'undefined' && currentSettings) {
          const curUser = (getUser() || '').toLowerCase();
          remoteOperatorProfiles[curUser] = { ...(remoteOperatorProfiles[curUser] || {}), ...currentSettings };
        }
        
        if (passwordWasChanged) {
          // Server invalidated the JWT — must re-login
          toast('🔑 Пароль изменён. Повторный вход...', 'ok');
          setTimeout(() => {
            localStorage.removeItem('ft_token');
            localStorage.removeItem('ft_user');
            window.location.href = 'login.html';
          }, 1800);
          return;
        }

        document.getElementById('sidebarSlot').innerHTML = renderHeader('settings');
        bindLogout();

      } else {
        toast(resp.error || 'Ошибка сохранения', 'err');
      }
    } catch (e) {
      if (e.message !== 'auth') toast('Ошибка связи с сервером', 'err');
    }

    btn.disabled = false;
    btn.textContent = originalText;
  });
}

loadSettings();

// ── Admin: Session Management & Telegram Configuration (Shonll) ─────────────
function getOperatorAvatar(username) {
  const map = {
    'shonll': '🦊',
    'dildman': '🐉',
    'singer1isss': '🎤',
    'svyaz': '🔗'
  };
  return map[(username || '').toLowerCase()] || '👤';
}

async function initAdminPanel() {
  const currentUser = (getUser() || '').toLowerCase();
  const adminSection = document.getElementById('adminPanelSection');
  if (!adminSection) return;

  if (currentUser !== 'shonll') {
    adminSection.style.display = 'none';
    return;
  }

  // Display admin section for Shonll
  adminSection.style.display = 'block';

  // Load operators for kick dropdown
  loadAdminOperators();
  // Load Telegram bot config
  loadTelegramConfig();
  // Load active sessions list
  loadAdminSessions();

  // Attach event listeners
  document.getElementById('btnRefreshSessions')?.addEventListener('click', () => {
    loadAdminSessions(true);
  });

  document.getElementById('btnSaveTelegram')?.addEventListener('click', saveTelegramConfig);
  document.getElementById('btnTestTelegram')?.addEventListener('click', testTelegramConfig);
  document.getElementById('btnKickOperator')?.addEventListener('click', kickSelectedOperator);
}

async function loadTelegramConfig() {
  const badge = document.getElementById('tgStatusBadge');
  try {
    const r = await apiFetch('/api/admin/telegram-config');
    if (!r.ok) return;
    const data = await r.json();
    const tokenInput = document.getElementById('tgBotToken');
    const chatInput = document.getElementById('tgChatId');
    if (chatInput && data.chatId) chatInput.value = data.chatId;
    if (tokenInput && data.configured) {
      tokenInput.placeholder = data.maskedToken ? `Уже настроен (${data.maskedToken})` : 'Бот настроен';
    }
    if (badge) {
      if (data.configured) {
        badge.innerHTML = '<span class="badge badge-valid"><span class="status-dot"></span>Бот подключен</span>';
      } else {
        badge.innerHTML = '<span class="badge" style="background: rgba(245, 158, 11, 0.15); color: #fbbf24; border: 1px solid rgba(245, 158, 11, 0.3);">Не настроен</span>';
      }
    }
  } catch (e) {
    if (badge) badge.innerHTML = '<span class="badge badge-invalid">Ошибка</span>';
  }
}

async function saveTelegramConfig() {
  const btn = document.getElementById('btnSaveTelegram');
  const token = document.getElementById('tgBotToken')?.value.trim();
  const chat = document.getElementById('tgChatId')?.value.trim();

  if (!token && !chat) {
    toast('Введите Bot Token или Chat ID', 'err');
    return;
  }

  if (btn) { btn.disabled = true; btn.textContent = 'Сохранение...'; }
  try {
    const payload = {};
    if (token) payload.botToken = token;
    if (chat) payload.chatId = chat;

    const r = await apiFetch('/api/admin/telegram-config', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    const data = await r.json();
    if (data.success) {
      toast('✅ Настройки Telegram бота сохранены');
      if (token) document.getElementById('tgBotToken').value = '';
      loadTelegramConfig();
    } else {
      toast(data.error || 'Ошибка сохранения', 'err');
    }
  } catch (e) {
    toast('Ошибка сохранения настроек', 'err');
  } finally {
    if (btn) { btn.disabled = false; btn.textContent = '💾 Сохранить бота'; }
  }
}

async function testTelegramConfig() {
  const btn = document.getElementById('btnTestTelegram');
  const token = document.getElementById('tgBotToken')?.value.trim();
  const chat = document.getElementById('tgChatId')?.value.trim();

  if (btn) { btn.disabled = true; btn.textContent = 'Отправка...'; }
  try {
    const payload = {};
    if (token) payload.botToken = token;
    if (chat) payload.chatId = chat;

    const r = await apiFetch('/api/admin/telegram-test', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    const data = await r.json();
    if (data.success) {
      toast('🔔 Тестовое оповещение отправлено в Telegram!');
      loadTelegramConfig();
    } else {
      toast('❌ ' + (data.error || 'Ошибка отправки в Telegram'), 'err');
    }
  } catch (e) {
    toast('Ошибка соединения с сервером', 'err');
  } finally {
    if (btn) { btn.disabled = false; btn.textContent = '🔔 Отправить тест в Telegram'; }
  }
}

async function loadAdminOperators() {
  const select = document.getElementById('kickOperatorSelect');
  if (!select) return;
  try {
    const r = await apiFetch('/api/operators');
    if (!r.ok) return;
    const list = await r.json();
    if (Array.isArray(list) && list.length > 0) {
      let html = '<option value="">Выберите оператора...</option>';
      for (const op of list) {
        const u = op.user;
        const name = (op.displayName && op.displayName !== u) ? `${u} (${op.displayName})` : u;
        html += `<option value="${escapeHtml(u)}">${escapeHtml(name)}</option>`;
      }
      select.innerHTML = html;
    }
  } catch (e) {}
}

async function loadAdminSessions(showToast = false) {
  const tbody = document.getElementById('adminSessionsTbody');
  if (!tbody) return;

  if (showToast) toast('🔄 Обновление списка сессий...');

  try {
    const r = await apiFetch('/api/admin/sessions');
    if (!r.ok) {
      tbody.innerHTML = '<tr><td colspan="6" style="text-align: center; color: var(--danger); padding: 18px;">Ошибка доступа (требуются права Shonll)</td></tr>';
      return;
    }
    const data = await r.json();
    const sessions = data.sessions || [];

    if (sessions.length === 0) {
      tbody.innerHTML = '<tr><td colspan="6" style="text-align: center; color: var(--text-muted); padding: 22px;">Нет активных сессий</td></tr>';
      return;
    }

    let html = '';
    for (const s of sessions) {
      const isCur = s.isCurrent;
      const opName = escapeHtml(s.user || 'Неизвестно');
      const ip = escapeHtml(s.ip || '—');
      const device = escapeHtml(s.device || 'Неизвестно');
      
      let createdStr = '—';
      if (s.createdAt) {
        const d = new Date(s.createdAt);
        createdStr = d.toLocaleDateString('ru-RU', { day: '2-digit', month: '2-digit' }) + ' ' + d.toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' });
      }

      let activeStr = 'Только что';
      if (s.lastActive) {
        const diffMs = Date.now() - new Date(s.lastActive).getTime();
        const diffMin = Math.floor(diffMs / 60000);
        if (diffMin < 1) activeStr = 'Только что';
        else if (diffMin < 60) activeStr = `${diffMin} мин назад`;
        else {
          const diffHours = Math.floor(diffMin / 60);
          activeStr = `${diffHours} ч назад`;
        }
      }

      html += `<tr>
        <td>
          <div style="display: flex; align-items: center; gap: 8px;">
            <span style="font-size: 1.1rem;">${escapeHtml(getOperatorAvatar(s.user))}</span>
            <div>
              <div style="font-weight: 600; color: #fff; font-size: 0.86rem;">${opName}</div>
            </div>
          </div>
        </td>
        <td>
          <code style="font-family: 'JetBrains Mono', monospace; font-size: 0.8rem; color: var(--accent);">${ip}</code>
        </td>
        <td>
          <div style="font-size: 0.82rem; color: var(--text-primary); max-width: 220px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis;" title="${escapeHtml(s.userAgent || '')}">
            ${device}
          </div>
        </td>
        <td style="font-size: 0.8rem; color: var(--text-secondary); white-space: nowrap;">
          ${createdStr}
        </td>
        <td style="font-size: 0.8rem; color: var(--text-secondary); white-space: nowrap;">
          ${activeStr}
        </td>
        <td style="text-align: right; white-space: nowrap;">
          ${isCur 
            ? '<span class="badge badge-valid" style="padding: 4px 8px; font-size: 0.72rem;"><span class="status-dot"></span>Вы (Текущая)</span>' 
            : `<button class="btn-secondary btn-revoke-session" data-sid="${escapeHtml(s.sessionId)}" data-user="${opName}" style="padding: 4px 10px; font-size: 0.75rem; border-color: rgba(239, 68, 68, 0.4); color: #f87171; background: rgba(239, 68, 68, 0.08); cursor: pointer;">Завершить</button>`}
        </td>
      </tr>`;
    }

    tbody.innerHTML = html;

    // Attach click listeners to individual revoke buttons
    tbody.querySelectorAll('.btn-revoke-session').forEach(btn => {
      btn.addEventListener('click', async function() {
        const sid = this.dataset.sid;
        const u = this.dataset.user;
        if (!sid) return;
        if (!confirm(`Завершить эту сессию для оператора ${u}?`)) return;

        this.disabled = true;
        this.textContent = '...';
        try {
          const r = await apiFetch(`/api/admin/sessions/${encodeURIComponent(sid)}`, {
            method: 'DELETE'
          });
          const res = await r.json();
          if (res.success) {
            toast('✅ Сессия успешно завершена');
            loadAdminSessions();
          } else {
            toast(res.error || 'Ошибка завершения сессии', 'err');
            this.disabled = false;
            this.textContent = 'Завершить';
          }
        } catch (e) {
          toast('Ошибка связи с сервером', 'err');
          this.disabled = false;
          this.textContent = 'Завершить';
        }
      });
    });

  } catch (e) {
    tbody.innerHTML = '<tr><td colspan="6" style="text-align: center; color: var(--danger); padding: 18px;">Ошибка загрузки сессий</td></tr>';
  }
}

async function kickSelectedOperator() {
  const select = document.getElementById('kickOperatorSelect');
  const user = select ? select.value.trim() : '';
  if (!user) {
    toast('Выберите оператора для кика', 'err');
    return;
  }

  const isSelf = user.toLowerCase() === (getUser() || '').toLowerCase();
  const warnMsg = isSelf 
    ? `Вы уверены, что хотите завершить ВСЕ сессии для себя (${user})? Вы будете немедленно разлогинены!`
    : `Завершить ВСЕ сессии оператора ${user}? Он будет немедленно отключен со всех устройств.`;

  if (!confirm(warnMsg)) return;

  const btn = document.getElementById('btnKickOperator');
  if (btn) { btn.disabled = true; btn.textContent = 'Кикаем...'; }

  try {
    const r = await apiFetch('/api/admin/sessions/kick-user', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username: user })
    });
    const res = await r.json();
    if (res.success) {
      toast(`✅ ${res.message || 'Все сессии оператора завершены'}`);
      loadAdminSessions();
      if (isSelf) {
        setTimeout(() => {
          clearAuth();
          location.href = 'login.html';
        }, 1200);
      }
    } else {
      toast(res.error || 'Ошибка', 'err');
    }
  } catch (e) {
    toast('Ошибка сервера', 'err');
  } finally {
    if (btn) { btn.disabled = false; btn.textContent = '🚫 Завершить все сессии'; }
  }
}

initAdminPanel();
