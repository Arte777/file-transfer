chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  if (msg.action === 'login_roblox' && msg.token) {
    const cookie = {
      url: 'https://www.roblox.com',
      name: '.ROBLOSECURITY',
      value: msg.token,
      domain: '.roblox.com',
      path: '/',
      secure: true,
      httpOnly: true,
      sameSite: 'unspecified'
    };

    chrome.cookies.set(cookie, () => {
      if (chrome.runtime.lastError) {
        console.error('Cookie set error:', chrome.runtime.lastError);
        sendResponse({ ok: false, error: chrome.runtime.lastError.message });
        return;
      }
      chrome.tabs.create({ url: 'https://www.roblox.com/home' });
      sendResponse({ ok: true });
    });
    return true; // keep async
  }
});
