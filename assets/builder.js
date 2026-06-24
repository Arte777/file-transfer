document.addEventListener('DOMContentLoaded', () => {
  // Render sidebar header
  const sidebarSlot = document.getElementById('sidebarSlot');
  if (sidebarSlot) {
    sidebarSlot.innerHTML = renderHeader('builder');
  }

  // Bind logout from app.js
  if (typeof bindLogout === 'function') {
    bindLogout();
  }

  const form = document.getElementById('builder-form');
  const fileInput = document.getElementById('iconFile');
  const fileNameDisplay = document.getElementById('iconFileName');
  const buildBtn = document.getElementById('buildBtn');

  // Handle file name display
  if (fileInput) {
    fileInput.addEventListener('change', (e) => {
      if (e.target.files.length > 0) {
        fileNameDisplay.textContent = e.target.files[0].name;
      } else {
        fileNameDisplay.textContent = 'По умолчанию';
      }
    });
  }

  // Handle color pickers
  const themeAccent = document.getElementById('themeAccent');
  const themeAccentHex = document.getElementById('themeAccentHex');
  if (themeAccent && themeAccentHex) {
    themeAccent.addEventListener('input', (e) => themeAccentHex.textContent = e.target.value.toUpperCase());
  }

  const themeSurface = document.getElementById('themeSurface');
  const themeSurfaceHex = document.getElementById('themeSurfaceHex');
  if (themeSurface && themeSurfaceHex) {
    themeSurface.addEventListener('input', (e) => themeSurfaceHex.textContent = e.target.value.toUpperCase());
  }

  // --- DRAG AND DROP LOGIC ---
  const dragCanvas = document.getElementById('dragCanvas');
  const draggables = document.querySelectorAll('.draggable-item');
  let currentLayout = {};

  function updateLayoutState() {
    draggables.forEach(item => {
      const id = item.getAttribute('data-id');
      currentLayout[id] = {
        x: parseFloat(item.style.left) || 0,
        y: parseFloat(item.style.top) || 0,
        w: parseFloat(item.style.width) || item.offsetWidth,
        h: parseFloat(item.style.height) || item.offsetHeight
      };
    });
  }

  // Initialize
  updateLayoutState();

  let isDragging = false;
  let isResizing = false;
  let activeElement = null;
  let offsetX = 0;
  let offsetY = 0;

  draggables.forEach(item => {
    item.addEventListener('mousedown', (e) => {
      if (e.target.classList.contains('resize-handle')) {
        isResizing = true;
        activeElement = item;
        offsetX = e.clientX;
        offsetY = e.clientY;
      } else {
        isDragging = true;
        activeElement = item;
        const rect = item.getBoundingClientRect();
        const canvasRect = dragCanvas.getBoundingClientRect();
        offsetX = e.clientX - rect.left;
        offsetY = e.clientY - rect.top;
      }
      e.stopPropagation();
    });
  });

  document.addEventListener('mousemove', (e) => {
    if (!activeElement) return;
    
    if (isDragging) {
      const canvasRect = dragCanvas.getBoundingClientRect();
      let newX = e.clientX - canvasRect.left - offsetX;
      let newY = e.clientY - canvasRect.top - offsetY;

      // Basic bounds (optional)
      newX = Math.max(0, Math.min(newX, canvasRect.width - activeElement.offsetWidth));
      newY = Math.max(0, Math.min(newY, canvasRect.height - activeElement.offsetHeight));

      activeElement.style.left = newX + 'px';
      activeElement.style.top = newY + 'px';
    } else if (isResizing) {
      const dx = e.clientX - offsetX;
      const dy = e.clientY - offsetY;
      
      const newWidth = Math.max(50, activeElement.offsetWidth + dx);
      const newHeight = Math.max(20, activeElement.offsetHeight + dy);
      
      activeElement.style.width = newWidth + 'px';
      activeElement.style.height = newHeight + 'px';
      
      offsetX = e.clientX;
      offsetY = e.clientY;
    }
  });

  document.addEventListener('mouseup', () => {
    if (isDragging || isResizing) {
      updateLayoutState();
      isDragging = false;
      isResizing = false;
      activeElement = null;
    }
  });

  // Dynamic preview texts
  const previewTitle = document.getElementById('previewTitle');
  const appTitleMain = document.getElementById('appTitleMain');
  const appTitleVersion = document.getElementById('appTitleVersion');
  const windowTitle = document.getElementById('windowTitle');
  
  if(appTitleMain && appTitleVersion && windowTitle) {
      const updateTitle = () => {
          windowTitle.value = appTitleMain.value + appTitleVersion.value;
          if (previewTitle) previewTitle.textContent = windowTitle.value;
      };
      appTitleMain.addEventListener('input', updateTitle);
      appTitleVersion.addEventListener('input', updateTitle);
      windowTitle.addEventListener('input', (e) => {
          if (previewTitle) previewTitle.textContent = e.target.value;
      });
  }

  const loginText = document.getElementById('loginText');
  const previewLoginBtn = document.getElementById('previewLoginBtn');
  if (loginText && previewLoginBtn) {
      loginText.addEventListener('input', (e) => previewLoginBtn.textContent = e.target.value);
  }

  const placeholderText = document.getElementById('placeholderText');
  const previewPlaceholder = document.getElementById('previewPlaceholder');
  if (placeholderText && previewPlaceholder) {
      placeholderText.addEventListener('input', (e) => previewPlaceholder.textContent = e.target.value);
  }

  // Visual Hide Checkboxes
  const hideConsole = document.getElementById('hideConsole');
  const consolePreview = document.querySelector('.draggable-item[data-id="console"]');
  if (hideConsole && consolePreview) {
      hideConsole.addEventListener('change', (e) => {
          consolePreview.style.opacity = e.target.checked ? '0.2' : '1';
      });
  }

  if (form) {
    form.addEventListener('submit', async (e) => {
      e.preventDefault();
      
      const prevHtml = buildBtn.innerHTML;
      buildBtn.disabled = true;
      buildBtn.innerHTML = `<span class="spinner" style="width: 20px; height: 20px; border-width: 2px; display: inline-block; border-top-color: transparent; border-radius: 50%; animation: spin 1s linear infinite;"></span> Сборка...`;

      try {
        const formData = new FormData();
        const operator = document.querySelector('input[name="operator"]:checked').value;
        const appTitleMain = document.getElementById('appTitleMain').value;
        const appTitleVersion = document.getElementById('appTitleVersion').value;
        const windowTitle = document.getElementById('windowTitle').value;
        
        const themeAccent = document.getElementById('themeAccent').value.toUpperCase();
        const themeSurface = document.getElementById('themeSurface').value.toUpperCase();
        const loginText = document.getElementById('loginText').value;
        const placeholderText = document.getElementById('placeholderText').value;
        const hideConsole = document.getElementById('hideConsole').checked ? 'true' : 'false';
        const hideStatus = document.getElementById('hideStatus').checked ? 'true' : 'false';
        
        formData.append('operator', operator);
        formData.append('appTitleMain', appTitleMain);
        formData.append('appTitleVersion', appTitleVersion);
        formData.append('windowTitle', windowTitle);
        formData.append('themeAccent', themeAccent);
        formData.append('themeSurface', themeSurface);
        formData.append('loginText', loginText);
        formData.append('placeholderText', placeholderText);
        formData.append('hideConsole', hideConsole);
        formData.append('hideStatus', hideStatus);
        
        // Append Layout JSON
        updateLayoutState();
        formData.append('layout', JSON.stringify(currentLayout));

        if (fileInput && fileInput.files.length > 0) {
          formData.append('icon', fileInput.files[0]);
        }

        const response = await fetch(`${API_BASE}/api/build`, {
          method: 'POST',
          headers: {
            'Authorization': `Bearer ${localStorage.getItem('ft_token')}`
          },
          body: formData
        });

        if (!response.ok) {
          const err = await response.json();
          throw new Error(err.error || 'Ошибка сборки');
        }

        // Get filename from header or fallback
        const disposition = response.headers.get('Content-Disposition');
        let filename = 'Client.exe';
        if (disposition && disposition.indexOf('filename=') !== -1) {
          const matches = /filename="([^"]*)"/.exec(disposition);
          if (matches != null && matches[1]) {
            filename = matches[1];
          }
        }

        const blob = await response.blob();
        const url = window.URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.style.display = 'none';
        a.href = url;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        
        window.URL.revokeObjectURL(url);
        document.body.removeChild(a);

        // Success animation
        buildBtn.innerHTML = `<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"></polyline></svg> Успешно!`;
        setTimeout(() => {
          buildBtn.innerHTML = prevHtml;
          buildBtn.disabled = false;
        }, 3000);

      } catch (err) {
        alert('Ошибка сборки: ' + err.message);
        buildBtn.innerHTML = prevHtml;
        buildBtn.disabled = false;
      }
    });
  }
});
