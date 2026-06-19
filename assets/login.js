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
  btn.textContent = 'Вход...';
  errBox.style.display = 'none';
  try {
    const resp = await fetch(API_BASE + '/api/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password })
    });
    if (!resp.ok) {
      errBox.style.display = 'block';
      btn.disabled = false;
      btn.textContent = 'Войти';
      return;
    }
    const data = await resp.json();
    setAuth(data.token, data.user);
    location.href = 'index.html';
  } catch (err) {
    errBox.textContent = '⚠️ Нет связи с сервером';
    errBox.style.display = 'block';
    btn.disabled = false;
    btn.textContent = 'Войти';
  }
});
