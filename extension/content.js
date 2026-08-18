function safeSendMessage(msg, callback) {
  try {
    chrome.runtime.sendMessage(msg, (resp) => {
      if (chrome.runtime.lastError) {
        console.warn('Extension error:', chrome.runtime.lastError.message);
        if (callback) callback({ success: false, ok: false, error: 'Расширение было обновлено. Обновите страницу (F5)!' });
        return;
      }
      if (callback) callback(resp);
    });
  } catch (e) {
    console.warn('Extension context invalidated:', e.message);
    if (callback) callback({ success: false, ok: false, error: 'Расширение было обновлено. Обновите страницу (F5)!' });
  }
}

window.addEventListener('message', (e) => {
  if (e.data?.type === 'nexus-login') {
    safeSendMessage({ action: 'login_roblox', token: e.data.token }, (resp) => {
      window.postMessage({ type: 'nexus-login-response', ok: resp?.ok === true, error: resp?.error }, '*');
    });
  } else if (e.data?.type === 'nexus-ping-request') {
    safeSendMessage({ action: 'ping' }, (resp) => {
      window.postMessage({ type: 'nexus-ping-response', success: resp?.success === true }, '*');
    });
  }
});
