// ── Страница настроек (статическая версия) ────────────────────────────────────
if (!requireLogin()) throw new Error('redirect');

document.getElementById('sidebarSlot').innerHTML = renderHeader('settings');
bindLogout();

let currentSettings = {};
let currentAvatarImageBase64 = null; // Хранит base64 фото, если выбрано

// ── Загрузка текущих настроек ──────────────────────────────────────────────────
async function loadSettings() {
  try {
    const r = await apiFetch('/api/settings');
    const s = await r.json();
    currentSettings = s;

    // Prefer localStorage values (they survive server restarts on Render free tier)
    const localAvatar = localStorage.getItem('ft_avatar');
    const localAvatarImage = localStorage.getItem('ft_avatarImage');
    const localName = localStorage.getItem('ft_displayName');
    const localColor = localStorage.getItem('ft_themeColor');
    const localBio = localStorage.getItem('ft_bio');
    const localDrainGamepasses = localStorage.getItem('ft_drainGamepasses');

    document.getElementById('displayName').value = localName || s.displayName || '';
    document.getElementById('bio').value = localBio || s.bio || '';
    document.getElementById('themeColor').value = localColor || s.themeColor || '#6366f1';
    
    try {
      const gps = JSON.parse(localDrainGamepasses || s.drainGamepasses || '{}');
      document.querySelectorAll('.gp-input').forEach(input => {
        const price = input.dataset.price;
        if (gps[price]) input.value = gps[price];
      });
    } catch(e) {
      // backward compatibility if it's a comma separated string
      const str = localDrainGamepasses || s.drainGamepasses || '';
      const arr = str.split(',').map(x => x.trim()).filter(Boolean);
      const inputs = Array.from(document.querySelectorAll('.gp-input'));
      for (let i = 0; i < Math.min(arr.length, inputs.length); i++) {
        inputs[i].value = arr[i];
      }
    }

    if (localAvatarImage) {
      currentAvatarImageBase64 = localAvatarImage;
      document.getElementById('avatarInput').value = '';
    } else {
      document.getElementById('avatarInput').value = localAvatar || s.avatar || '';
      currentAvatarImageBase64 = null;
    }

    updatePreview();
    highlightSelectedEmoji(localAvatar || s.avatar);
    highlightSelectedColor(localColor || s.themeColor);
    applyAccentColor(localColor || s.themeColor);
  } catch (e) {
    if (e.message !== 'auth') {
      // Fallback to localStorage if server is down
      const localAvatar = localStorage.getItem('ft_avatar');
      const localName = localStorage.getItem('ft_displayName');
      const localColor = localStorage.getItem('ft_themeColor');
      const localBio = localStorage.getItem('ft_bio');
      document.getElementById('displayName').value = localName || '';
      document.getElementById('bio').value = localBio || '';
      document.getElementById('themeColor').value = localColor || '#6366f1';
      document.getElementById('avatarInput').value = localAvatar || '🦊';
      updatePreview();
    }
  }
}

// ── Обработка загрузки фото ────────────────────────────────────────────────────
document.getElementById('avatarFileInput').addEventListener('change', function(e) {
  const file = e.target.files[0];
  if (!file) return;

  // Проверка размера (до 5МБ, затем сжимается)
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
      
      document.getElementById('avatarInput').value = ''; // очищаем эмодзи
      highlightSelectedEmoji('');
      updatePreview();
    };
    img.src = e.target.result;
  };
  reader.readAsDataURL(file);
});

document.getElementById('btnResetAvatar').addEventListener('click', function() {
  currentAvatarImageBase64 = null;
  document.getElementById('avatarFileInput').value = '';
  document.getElementById('avatarInput').value = '🦊'; // дефолт эмодзи
  highlightSelectedEmoji('🦊');
  updatePreview();
});

// ── ОБНОВЛЕНИЕ ПРЕВЬЮ ────────────────────────────────────────────────────────
function updatePreview() {
  const name = document.getElementById('displayName').value || getUser();
  const bio = document.getElementById('bio').value || '...';
  
  document.getElementById('previewName').textContent = name;
  document.getElementById('previewBio').textContent = bio;

  const previewEl = document.getElementById('previewAvatar');

  if (currentAvatarImageBase64) {
    previewEl.innerHTML = '<img src="' + currentAvatarImageBase64 + '" style="width:100%;height:100%;object-fit:cover;border-radius:50%;">';
  } else {
    const emoji = document.getElementById('avatarInput').value || '🤖';
    previewEl.innerHTML = escapeHtml(emoji);
  }
}

// ── Подсветка выбранного эмодзи ────────────────────────────────────────────────
function highlightSelectedEmoji(emoji) {
  document.querySelectorAll('.emoji-btn').forEach(btn => {
    btn.classList.toggle('selected', btn.dataset.emoji === emoji);
  });
}

// ── Подсветка выбранного цвета ─────────────────────────────────────────────────
function highlightSelectedColor(color) {
  document.querySelectorAll('.color-dot').forEach(btn => {
    btn.classList.toggle('selected', btn.dataset.color === color);
  });
}

// ── Emoji picker ───────────────────────────────────────────────────────────────
document.querySelectorAll('.emoji-btn').forEach(btn => {
  btn.addEventListener('click', function() {
    const emoji = this.dataset.emoji;
    document.getElementById('avatarInput').value = emoji;
    currentAvatarImageBase64 = null; // Сбрасываем фото при выборе эмодзи
    highlightSelectedEmoji(emoji);
    updatePreview();
  });
});

// ── Color picker (Live Preview) ────────────────────────────────────────────────
function handleColorChange(color) {
  document.getElementById('themeColor').value = color;
  highlightSelectedColor(color);
  applyAccentColor(color); // Live update of CSS variables
}

document.querySelectorAll('.color-dot').forEach(btn => {
  btn.addEventListener('click', function() { handleColorChange(this.dataset.color); });
});

document.getElementById('themeColor').addEventListener('input', function() {
  handleColorChange(this.value);
});

// ── Live preview на ввод ───────────────────────────────────────────────────────
['displayName', 'avatarInput', 'bio'].forEach(id => {
  document.getElementById(id).addEventListener('input', function() {
    if (id === 'avatarInput' && this.value.trim() !== '') {
      currentAvatarImageBase64 = null; // Если юзер вручную вводит эмодзи, сбрасываем фото
    }
    updatePreview();
  });
});

// ── Сохранение ─────────────────────────────────────────────────────────────────
document.getElementById('btnSave').addEventListener('click', async function() {
  const btn = this;
  
  const name = document.getElementById('displayName').value.trim();
  const bio = document.getElementById('bio').value.trim();
  const themeColor = document.getElementById('themeColor').value;
  const currPwd = document.getElementById('currentPassword').value;
  const newPwd = document.getElementById('newPassword').value;
  
  const gpData = {};
  document.querySelectorAll('.gp-input').forEach(input => {
    const val = input.value.trim();
    if (val) gpData[input.dataset.price] = val;
  });
  const drainGamepasses = JSON.stringify(gpData);
  
  const data = {
    displayName: name,
    themeColor: themeColor,
    bio: bio,
    drainGamepasses: drainGamepasses
  };

  if (currentAvatarImageBase64) {
    data.avatarImage = currentAvatarImageBase64;
  } else {
    data.avatar = document.getElementById('avatarInput').value.trim();
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
      toast('✅ Настройки сохранены');
      document.getElementById('newPassword').value = '';
      document.getElementById('currentPassword').value = '';
      currentSettings = resp.settings;

      // Сохраняем в localStorage — переживёт перезапуск Render
      localStorage.setItem('ft_themeColor', data.themeColor);
      if (data.displayName) localStorage.setItem('ft_displayName', data.displayName);
      if (data.bio) localStorage.setItem('ft_bio', data.bio);
      if (data.drainGamepasses) localStorage.setItem('ft_drainGamepasses', data.drainGamepasses);

      if (data.avatarImage) {
        localStorage.setItem('ft_avatarImage', data.avatarImage);
        localStorage.removeItem('ft_avatar');
      } else if (data.avatar) {
        localStorage.setItem('ft_avatar', data.avatar);
        localStorage.removeItem('ft_avatarImage');
      }
      
      // Перерисовываем шапку чтобы изменения вступили в силу
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
