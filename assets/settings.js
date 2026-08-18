// ── Страница настроек ────────────────────────────────────────────────────────
if (!requireLogin()) throw new Error('redirect');

document.getElementById('sidebarSlot').innerHTML = renderHeader('settings');
bindLogout();

let currentSettings = {};
let currentAvatarImageBase64 = null;

// ── Загрузка текущих настроек ────────────────────────────────────────────────
async function loadSettings() {
  try {
    const r = await apiFetch('/api/settings');
    const s = await r.json();
    currentSettings = s;

    const localAvatarImage = localStorage.getItem('ft_avatarImage');
    const localName = localStorage.getItem('ft_displayName');
    const localColor = localStorage.getItem('ft_themeColor');
    const localBio = localStorage.getItem('ft_bio');

    document.getElementById('displayName').value = localName || s.displayName || '';
    document.getElementById('bio').value = localBio || s.bio || '';
    document.getElementById('themeColor').value = localColor || s.themeColor || '#38bdf8';

    const serverAvatarImage = s.avatarImage || null;

    if (localAvatarImage) {
      currentAvatarImageBase64 = localAvatarImage;
    } else if (serverAvatarImage) {
      currentAvatarImageBase64 = serverAvatarImage;
      localStorage.setItem('ft_avatarImage', serverAvatarImage);
    } else {
      currentAvatarImageBase64 = null;
    }

    updatePreview();
    highlightSelectedColor(localColor || s.themeColor || '#38bdf8');
    applyAccentColor(localColor || s.themeColor || '#38bdf8');
  } catch (e) {
    if (e.message !== 'auth') {
      const localName = localStorage.getItem('ft_displayName');
      const localColor = localStorage.getItem('ft_themeColor');
      const localBio = localStorage.getItem('ft_bio');
      document.getElementById('displayName').value = localName || '';
      document.getElementById('bio').value = localBio || '';
      document.getElementById('themeColor').value = localColor || '#38bdf8';
      updatePreview();
    }
  }
}

// ── Загрузка фото аватара ────────────────────────────────────────────────────
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
      currentAvatarImageBase64 = canvas.toDataURL('image/jpeg', 0.85);
      updatePreview();
    };
    img.src = e.target.result;
  };
  reader.readAsDataURL(file);
});

document.getElementById('btnResetAvatar')?.addEventListener('click', function() {
  currentAvatarImageBase64 = null;
  const input = document.getElementById('avatarFileInput');
  if (input) input.value = '';
  localStorage.removeItem('ft_avatarImage');
  localStorage.removeItem('ft_avatar');
  updatePreview();
});

// ── Обновление превью ────────────────────────────────────────────────────────
function updatePreview() {
  const name = document.getElementById('displayName').value || getUser() || 'Operator';
  const bio = document.getElementById('bio').value || '...';
  
  const previewName = document.getElementById('previewName');
  const previewBio = document.getElementById('previewBio');
  if (previewName) previewName.textContent = name;
  if (previewBio) previewBio.textContent = bio;

  const previewEl = document.getElementById('previewAvatar');
  if (!previewEl) return;

  if (currentAvatarImageBase64) {
    previewEl.innerHTML = '<img src="' + currentAvatarImageBase64 + '" style="width:100%;height:100%;object-fit:cover;border-radius:50%;">';
  } else {
    const initial = name.trim().toUpperCase().charAt(0) || 'O';
    previewEl.innerHTML = escapeHtml(initial);
  }
}

// ── Выбор цвета ──────────────────────────────────────────────────────────────
function highlightSelectedColor(color) {
  document.querySelectorAll('.color-dot').forEach(btn => {
    btn.classList.toggle('selected', btn.dataset.color === color);
  });
}

function handleColorChange(color) {
  document.getElementById('themeColor').value = color;
  highlightSelectedColor(color);
  applyAccentColor(color);
}

document.querySelectorAll('.color-dot').forEach(btn => {
  btn.addEventListener('click', function() { handleColorChange(this.dataset.color); });
});

document.getElementById('themeColor')?.addEventListener('input', function() {
  handleColorChange(this.value);
});

// ── Live preview ─────────────────────────────────────────────────────────────
['displayName', 'bio'].forEach(id => {
  document.getElementById(id)?.addEventListener('input', function() {
    updatePreview();
  });
});

// ── Сохранение настроек ──────────────────────────────────────────────────────
document.getElementById('btnSave')?.addEventListener('click', async function() {
  const btn = this;
  
  const name = document.getElementById('displayName').value.trim();
  const bio = document.getElementById('bio').value.trim();
  const themeColor = document.getElementById('themeColor').value;
  const currPwd = document.getElementById('currentPassword').value;
  const newPwd = document.getElementById('newPassword').value;
  
  const data = {
    displayName: name,
    themeColor: themeColor,
    bio: bio
  };

  if (currentAvatarImageBase64) {
    data.avatarImage = currentAvatarImageBase64;
  }

  if (newPwd) {
    data.newPassword = newPwd;
    data.currentPassword = currPwd;
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
      toast('Настройки сохранены');
      document.getElementById('newPassword').value = '';
      document.getElementById('currentPassword').value = '';
      currentSettings = resp.settings;

      localStorage.setItem('ft_themeColor', data.themeColor);
      if (data.displayName) localStorage.setItem('ft_displayName', data.displayName);
      if (data.bio) localStorage.setItem('ft_bio', data.bio);

      if (data.avatarImage) {
        localStorage.setItem('ft_avatarImage', data.avatarImage);
      } else {
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
  btn.textContent = 'Сохранить настройки';
});

loadSettings();
