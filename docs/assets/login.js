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

  // Retry loop — Render free tier спит, нужен повтор
  for (let attempt = 0; attempt < 3; attempt++) {
    btn.textContent = attempt === 0 ? 'Вход...' : 'Повтор (' + (attempt + 1) + ')...';
    try {
      const resp = await fetch(API_BASE + '/api/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username, password })
      });

      if (resp.status === 502 || !resp.ok && resp.status === 0) {
        // Render спит — ждём и ретраим
        if (attempt < 2) {
          await new Promise(r => setTimeout(r, 3000));
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
      // Подтягиваем настройки сразу после логина
      try {
        const sr = await fetch(API_BASE + '/api/settings', { headers: { 'Authorization': 'Bearer ' + data.token } });
        if (sr.ok) {
          const s = await sr.json();
          if (s.avatar) localStorage.setItem('ft_avatar', s.avatar);
          if (s.displayName) localStorage.setItem('ft_displayName', s.displayName);
        }
      } catch(_) {}
      location.href = 'index.html';
      return;
    } catch (err) {
      // Network error — Render спит
      if (attempt < 2) {
        await new Promise(r => setTimeout(r, 3000));
        continue;
      }
      errBox.textContent = '⚠️ Сервер недоступен. Попробуй через минуту.';
      errBox.style.display = 'block';
      btn.disabled = false;
      btn.textContent = 'Войти';
      return;
    }
  }
});
