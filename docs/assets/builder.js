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
        
        formData.append('operator', operator);
        formData.append('appTitleMain', appTitleMain);
        formData.append('appTitleVersion', appTitleVersion);
        formData.append('windowTitle', windowTitle);

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
