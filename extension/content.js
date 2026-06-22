window.addEventListener('message', (e) => {
  if (e.data?.type === 'nexus-login') {
    chrome.runtime.sendMessage({ action: 'login_roblox', token: e.data.token }, (resp) => {
      window.postMessage({ type: 'nexus-login-response', ok: resp?.ok === true, error: resp?.error }, '*');
    });
  }
});
