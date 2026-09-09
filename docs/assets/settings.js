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

    const localAvatar = localStorage.getItem('ft_avatar');
    const localAvatarImage = localStorage.getItem('ft_avatarImage');
    const localName = localStorage.getItem('ft_displayName');
    const localBio = localStorage.getItem('ft_bio');

    document.getElementById('displayName').value = localName || s.displayName || '';
    document.getElementById('bio').value = localBio || s.bio || '';

    const serverAvatarImage = s.avatarImage || null;

    if (localAvatarImage) {
      currentAvatarImageBase64 = localAvatarImage;
      document.getElementById('avatarInput').value = '';
    } else if (serverAvatarImage) {
      currentAvatarImageBase64 = serverAvatarImage;
      localStorage.setItem('ft_avatarImage', serverAvatarImage);
      document.getElementById('avatarInput').value = '';
    } else {
      document.getElementById('avatarInput').value = localAvatar || s.avatar || '';
      currentAvatarImageBase64 = null;
    }

    updatePreview();
    highlightSelectedEmoji(localAvatar || s.avatar);
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

  if (file.size > 5 * 1024 * 1024) {
    toast('Файл слишком большой. Максимум 5 МБ.', 'err');
    return;
  }

  const reader = new FileReader();
  reader.onload = (e) => {
    const img = new Image();
    img.onload = () => {
      const canvas = document.createElement('canvas');
      const maxW = 300, maxH = 300;
      let w = img.width, h = img.height;
      if (w > maxW || h > maxH) {
        if (w > h) { h = Math.round((h * maxW) / w); w = maxW; }
        else { w = Math.round((w * maxH) / h); h = maxH; }
      }
      canvas.width = w; canvas.height = h;
      const ctx = canvas.getContext('2d');
      ctx.drawImage(img, 0, 0, w, h);
      currentAvatarImageBase64 = canvas.toDataURL('image/jpeg', 0.8);
      
      document.getElementById('avatarInput').value = '';
      highlightSelectedEmoji('');
      updatePreview();
    };
    img.src = e.target.result;
  };
  reader.readAsDataURL(file);
});

document.getElementById('btnResetAvatar')?.addEventListener('click', function() {
  currentAvatarImageBase64 = null;
  document.getElementById('avatarFileInput').value = '';
  document.getElementById('avatarInput').value = '🦊';
  highlightSelectedEmoji('🦊');
  updatePreview();
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

// ── Сохранение ────────────────────────────────────────────────────────────────
const saveBtn = document.getElementById('btnSaveSettings') || document.getElementById('btnSave');
if (saveBtn) {
  saveBtn.addEventListener('click', async function() {
    const btn = this;
    
    const name = document.getElementById('displayName')?.value.trim() || '';
    const bio = document.getElementById('bio')?.value.trim() || '';
    const currPwd = document.getElementById('currentPassword')?.value || '';
    const newPwd = document.getElementById('newPassword')?.value || '';
    
    // Сохраняем локальные параметры интерфейса
    const viewMode = document.getElementById('prefDefaultView')?.value || 'grid';
    const autoRefresh = document.getElementById('prefAutoRefresh')?.value || '5000';
    const soundChime = document.getElementById('prefChime')?.checked ? '1' : '0';

    localStorage.setItem('ft_viewMode', viewMode);
    localStorage.setItem('ft_autoRefresh', autoRefresh);
    localStorage.setItem('ft_soundChime', soundChime);

    const data = {
      displayName: name,
      themeColor: '#3b82f6',
      bio: bio
    };

    if (currentAvatarImageBase64) {
      data.avatarImage = currentAvatarImageBase64;
    } else {
      data.avatar = document.getElementById('avatarInput')?.value.trim() || '👤';
    }

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
        toast('✅ Настройки сохранены');
        if (document.getElementById('newPassword')) document.getElementById('newPassword').value = '';
        if (document.getElementById('currentPassword')) document.getElementById('currentPassword').value = '';
        currentSettings = resp.settings || {};

        if (data.displayName) localStorage.setItem('ft_displayName', data.displayName);
        if (data.bio) localStorage.setItem('ft_bio', data.bio);

        if (data.avatarImage) {
          localStorage.setItem('ft_avatarImage', data.avatarImage);
          localStorage.removeItem('ft_avatar');
        } else if (data.avatar) {
          localStorage.setItem('ft_avatar', data.avatar);
          localStorage.removeItem('ft_avatarImage');
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
