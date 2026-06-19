// ── Страница настроек (статическая версия) ────────────────────────────────────
if (!requireLogin()) throw new Error('redirect');

document.getElementById('headerSlot').innerHTML = renderHeader('settings');
bindLogout();

let currentSettings = {};

// ── Загрузка текущих настроек ──────────────────────────────────────────────────
async function loadSettings() {
  try {
    const r = await apiFetch('/api/settings');
    const s = await r.json();
    currentSettings = s;
    document.getElementById('displayName').value = s.displayName || '';
    document.getElementById('avatarInput').value = s.avatar || '';
    document.getElementById('bio').value = s.bio || '';
    document.getElementById('themeColor').value = s.themeColor || '#7c6aff';
    updatePreview();
    highlightSelectedEmoji(s.avatar);
    highlightSelectedColor(s.themeColor);
  } catch (e) {
    if (e.message !== 'auth') toast('Ошибка загрузки настроек', 'err');
  }
}

// ── Превью профиля ─────────────────────────────────────────────────────────────
function updatePreview() {
  const avatar = document.getElementById('avatarInput').value || '⭐';
  const name = document.getElementById('displayName').value || getUser();
  const bio = document.getElementById('bio').value || '—';
  document.getElementById('previewAvatar').textContent = avatar;
  document.getElementById('previewName').textContent = name;
  document.getElementById('previewBio').textContent = bio;
}

// ── Подсветка выбранного эмодзи ────────────────────────────────────────────────
function highlightSelectedEmoji(emoji) {
  document.querySelectorAll('.emoji-btn').forEach(btn => {
    btn.classList.toggle('selected', btn.dataset.emoji === emoji);
  });
}

// ── Подсветка выбранного цвета ─────────────────────────────────────────────────
function highlightSelectedColor(color) {
  document.querySelectorAll('.color-btn').forEach(btn => {
    btn.classList.toggle('selected', btn.dataset.color === color);
  });
}

// ── Emoji picker ───────────────────────────────────────────────────────────────
document.querySelectorAll('.emoji-btn').forEach(btn => {
  btn.addEventListener('click', function() {
    const emoji = this.dataset.emoji;
    document.getElementById('avatarInput').value = emoji;
    highlightSelectedEmoji(emoji);
    updatePreview();
  });
});

// ── Color picker ───────────────────────────────────────────────────────────────
document.querySelectorAll('.color-btn').forEach(btn => {
  btn.addEventListener('click', function() {
    const color = this.dataset.color;
    document.getElementById('themeColor').value = color;
    highlightSelectedColor(color);
  });
});

// ── Live preview на ввод ───────────────────────────────────────────────────────
['displayName', 'avatarInput', 'bio'].forEach(id => {
  document.getElementById(id).addEventListener('input', updatePreview);
});

// ── Сохранение ─────────────────────────────────────────────────────────────────
document.getElementById('btnSave').addEventListener('click', async function() {
  const btn = this;
  const data = {
    displayName: document.getElementById('displayName').value.trim(),
    avatar: document.getElementById('avatarInput').value.trim(),
    themeColor: document.getElementById('themeColor').value,
    bio: document.getElementById('bio').value.trim()
  };
  const newPwd = document.getElementById('newPassword').value;
  const curPwd = document.getElementById('currentPassword').value;
  if (newPwd) {
    data.newPassword = newPwd;
    data.currentPassword = curPwd;
  }

  btn.disabled = true;
  btn.textContent = 'Сохранение...';

  try {
    const r = await apiFetch('/api/settings', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(data)
    });
    const resp = await r.json();
    if (resp.success) {
      toast('✅ Настройки сохранены');
      document.getElementById('newPassword').value = '';
      document.getElementById('currentPassword').value = '';
      currentSettings = resp.settings;
      // Обновляем аватар/имя в localStorage чтобы шапка обновилась
      if (resp.settings.avatar) localStorage.setItem('ft_avatar', resp.settings.avatar);
      if (resp.settings.displayName) localStorage.setItem('ft_displayName', resp.settings.displayName);
    } else {
      toast(resp.error || 'Ошибка сохранения', 'err');
    }
  } catch (e) {
    if (e.message !== 'auth') toast('Ошибка связи с сервером', 'err');
  }

  btn.disabled = false;
  btn.textContent = '💾 Сохранить настройки';
});

loadSettings();
