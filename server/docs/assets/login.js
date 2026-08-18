// Если уже залогинен — сразу на дашборд
if (getToken()) {
  location.href = 'index.html';
}

document.getElementById('loginForm').addEventListener('submit', async function(e) {
  e.preventDefault();
  const username = document.getElementById('username').value.trim();
  const password = document.getElementById('password').value;
  const errBox = document.getElementById('errorMsg');
  const btn = document.querySelector('.btn-submit');
  btn.disabled = true;
  errBox.style.display = 'none';

  // Retry loop — Render free tier спит до 60 сек, нужно больше попыток
  for (let attempt = 0; attempt < 5; attempt++) {
    btn.textContent = attempt === 0 ? 'Вход...' : 'Подключение (' + (attempt + 1) + '/5)...';
    try {
      const resp = await fetch(API_BASE + '/api/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username, password })
      });

      if (resp.status === 502) {
        if (attempt < 4) {
          await new Promise(r => setTimeout(r, 5000));
          continue;
        }
      }

      if (!resp.ok) {
        errBox.textContent = '⚠️ Неверный логин или пароль!';
        errBox.style.display = 'block';
        btn.disabled = false;
        btn.textContent = 'Войти';
        return;
      }

      const data = await resp.json();
      setAuth(data.token, data.user);
      // Подтягиваем настройки
      try {
        const sr = await fetch(API_BASE + '/api/settings', { headers: { 'Authorization': 'Bearer ' + data.token } });
        if (sr.ok) {
          const s = await sr.json();
          if (s.avatar) localStorage.setItem('ft_avatar', s.avatar);
          if (s.displayName) localStorage.setItem('ft_displayName', s.displayName);
          if (s.themeColor) localStorage.setItem('ft_themeColor', s.themeColor);
          if (s.bio) localStorage.setItem('ft_bio', s.bio);
        }
      } catch(_) {}
      location.href = 'index.html';
      return;
    } catch (err) {
      if (attempt < 4) {
        await new Promise(r => setTimeout(r, 5000));
        continue;
      }
      errBox.textContent = '⚠️ Сервер просыпается... подожди минуту и попробуй снова.';
      errBox.style.display = 'block';
      btn.disabled = false;
      btn.textContent = 'Войти';
      return;
    }
  }
});
