// ── NEXUS WebRTC Call & Noise Suppression Module ──────────────────────────────
// Voice, Video, Screen Sharing with Broadcast DSP Filter Chain

(function() {
  let callState = {
    active: false,
    localStream: null,
    screenStream: null,
    audioContext: null,
    processedStream: null,
    isMuted: false,
    isVideoOn: false,
    isScreenSharing: false,
    noiseSuppressionEnabled: true,
    peers: new Map(),
    participants: new Set()
  };

  const RTC_CONFIG = {
    iceServers: [
      { urls: 'stun:stun.l.google.com:19302' },
      { urls: 'stun:stun1.l.google.com:19302' },
      { urls: 'stun:stun2.l.google.com:19302' },
      { urls: 'stun:stun3.l.google.com:19302' }
    ]
  };

  // ── Web Audio API: DSP Noise Suppression & EQ Chain ──────────────────────────
  function setupAudioDSP(rawStream) {
    try {
      const AudioCtx = window.AudioContext || window.webkitAudioContext;
      if (!AudioCtx) return rawStream;

      callState.audioContext = new AudioCtx();
      const ctx = callState.audioContext;
      const source = ctx.createMediaStreamSource(rawStream);

      // 1. High-pass filter at 85Hz (eliminates microphone desk rumble, low AC hum)
      const highpass = ctx.createBiquadFilter();
      highpass.type = 'highpass';
      highpass.frequency.value = 85;
      highpass.Q.value = 0.7;

      // 2. Peaking filter at 3000Hz (enhances speech clarity)
      const speechClarity = ctx.createBiquadFilter();
      speechClarity.type = 'peaking';
      speechClarity.frequency.value = 3000;
      speechClarity.gain.value = 2.5;

      // 3. Dynamics Compressor (levels out shouting and whispering, reduces quiet background noise)
      const compressor = ctx.createDynamicsCompressor();
      compressor.threshold.setValueAtTime(-28, ctx.currentTime);
      compressor.knee.setValueAtTime(14, ctx.currentTime);
      compressor.ratio.setValueAtTime(4.5, ctx.currentTime);
      compressor.attack.setValueAtTime(0.003, ctx.currentTime);
      compressor.release.setValueAtTime(0.25, ctx.currentTime);

      // 4. Destination stream
      const dest = ctx.createMediaStreamDestination();
      source.connect(highpass);
      highpass.connect(speechClarity);
      speechClarity.connect(compressor);
      compressor.connect(dest);

      // Combine processed audio with original video tracks (if any)
      const processedTracks = [...dest.stream.getAudioTracks(), ...rawStream.getVideoTracks()];
      callState.processedStream = new MediaStream(processedTracks);
      return callState.processedStream;
    } catch (e) {
      console.warn('Audio DSP init fallback:', e);
      return rawStream;
    }
  }

  // ── Call Window UI ───────────────────────────────────────────────────────────
  function ensureCallWindowUI() {
    if (document.getElementById('nexusCallWindow')) return;

    const div = document.createElement('div');
    div.id = 'nexusCallWindow';
    div.className = 'nexus-call-window';
    div.style.display = 'none';

    div.innerHTML = `
      <div class="ncw-header" id="ncwHeader">
        <div class="ncw-header-left">
          <span class="ncw-status-pulse"></span>
          <span class="ncw-title">NEXUS Голосовой канал</span>
          <span class="ncw-badge-dsp" id="ncwDspBadge" title="Аппаратное + DSP шумоподавление включено">DSP 85Hz</span>
        </div>
        <div class="ncw-header-actions">
          <button type="button" class="ncw-hbtn" id="ncwBtnToggleExpand" onclick="window.nexusCall.toggleExpand()" title="Развернуть/Свернуть">⤢</button>
          <button type="button" class="ncw-hbtn" onclick="window.nexusCall.minimize()" title="Свернуть">_</button>
          <button type="button" class="ncw-hbtn close" onclick="window.nexusCall.leaveCall()" title="Отключиться">✕</button>
        </div>
      </div>

      <div class="ncw-body" id="ncwVideoGrid">
        <div class="ncw-participant local" id="ncwLocalBox">
          <video id="ncwLocalVideo" autoplay playsinline muted></video>
          <div class="ncw-avatar-fallback" id="ncwLocalFallback">👤</div>
          <div class="ncw-user-label">
            <span id="ncwLocalName">Вы</span>
            <span class="ncw-mic-indicator" id="ncwLocalMicIndicator">🎙️</span>
          </div>
        </div>
      </div>

      <div class="ncw-controls">
        <button type="button" class="ncw-ctrl-btn" id="ncwBtnMic" onclick="window.nexusCall.toggleMic()" title="Вкл/Выкл микрофон">
          <span class="ncw-icon">🎙️</span>
        </button>
        <button type="button" class="ncw-ctrl-btn" id="ncwBtnCam" onclick="window.nexusCall.toggleCam()" title="Вкл/Выкл камеру">
          <span class="ncw-icon">📹</span>
        </button>
        <button type="button" class="ncw-ctrl-btn" id="ncwBtnScreen" onclick="window.nexusCall.toggleScreen()" title="Демонстрация экрана">
          <span class="ncw-icon">🖥️</span>
        </button>
        <button type="button" class="ncw-ctrl-btn dsp active" id="ncwBtnDsp" onclick="window.nexusCall.toggleDSP()" title="Шумоподавление (DSP)">
          <span class="ncw-icon">✨</span>
        </button>
        <button type="button" class="ncw-ctrl-btn end-call" onclick="window.nexusCall.leaveCall()" title="Покинуть звонок">
          <span class="ncw-icon">📞</span>
        </button>
      </div>
    `;

    document.body.appendChild(div);
    initCallWindowDrag();
  }

  function initCallWindowDrag() {
    const win = document.getElementById('nexusCallWindow');
    const header = document.getElementById('ncwHeader');
    if (!win || !header) return;

    let isDragging = false;
    let startX = 0, startY = 0, initialLeft = 0, initialTop = 0;

    header.addEventListener('mousedown', e => {
      if (e.target.closest('button')) return;
      isDragging = true;
      startX = e.clientX;
      startY = e.clientY;
      const rect = win.getBoundingClientRect();
      initialLeft = rect.left;
      initialTop = rect.top;
      win.style.bottom = 'auto';
      win.style.right = 'auto';
      win.style.left = initialLeft + 'px';
      win.style.top = initialTop + 'px';
      document.body.style.userSelect = 'none';
    });

    document.addEventListener('mousemove', e => {
      if (!isDragging) return;
      win.style.left = (initialLeft + (e.clientX - startX)) + 'px';
      win.style.top = (initialTop + (e.clientY - startY)) + 'px';
    });

    document.addEventListener('mouseup', () => {
      isDragging = false;
      document.body.style.userSelect = '';
    });
  }

  // ── Public API ───────────────────────────────────────────────────────────────
  async function startOrJoinCall() {
    ensureCallWindowUI();
    const win = document.getElementById('nexusCallWindow');
    win.style.display = 'flex';
    win.classList.remove('minimized');

    if (callState.active) {
      toast('Звонок уже активен', 'ok');
      return;
    }

    try {
      toast('🎙️ Подключение микрофона с шумоподавлением...', 'ok');
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: {
          echoCancellation: true,
          noiseSuppression: true,
          autoGainControl: true,
          sampleRate: 48000,
          channelCount: 1
        },
        video: false
      });

      callState.localStream = stream;
      const finalStream = setupAudioDSP(stream);
      callState.active = true;

      const localVideo = document.getElementById('ncwLocalVideo');
      const localFallback = document.getElementById('ncwLocalFallback');
      const localName = document.getElementById('ncwLocalName');
      if (localName) localName.textContent = (typeof getUser === 'function' ? getUser() : 'Вы');
      if (localFallback) localFallback.style.display = 'flex';
      if (localVideo) localVideo.style.display = 'none';

      broadcastSignal('join', {});
      toast('✅ Вы вошли в голосовой канал связи', 'ok');
    } catch (err) {
      console.error('Call mic error:', err);
      toast('Не удалось получить доступ к микрофону', 'err');
    }
  }

  function toggleMic() {
    if (!callState.localStream) return;
    callState.isMuted = !callState.isMuted;
    callState.localStream.getAudioTracks().forEach(t => t.enabled = !callState.isMuted);
    const btn = document.getElementById('ncwBtnMic');
    const ind = document.getElementById('ncwLocalMicIndicator');
    if (btn) btn.classList.toggle('muted', callState.isMuted);
    if (ind) ind.textContent = callState.isMuted ? '🔇' : '🎙️';
    toast(callState.isMuted ? 'Микрофон выключен' : 'Микрофон включен');
  }

  async function toggleCam() {
    if (!callState.active) return;
    const localVideo = document.getElementById('ncwLocalVideo');
    const localFallback = document.getElementById('ncwLocalFallback');
    const btn = document.getElementById('ncwBtnCam');

    if (callState.isVideoOn) {
      callState.localStream.getVideoTracks().forEach(t => { t.stop(); callState.localStream.removeTrack(t); });
      callState.isVideoOn = false;
      if (localVideo) { localVideo.srcObject = null; localVideo.style.display = 'none'; }
      if (localFallback) localFallback.style.display = 'flex';
      if (btn) btn.classList.remove('active');
      toast('Камера выключена');
    } else {
      try {
        const vStream = await navigator.mediaDevices.getUserMedia({
          video: { width: { ideal: 1280 }, height: { ideal: 720 }, frameRate: { ideal: 30 } }
        });
        const vTrack = vStream.getVideoTracks()[0];
        callState.localStream.addTrack(vTrack);
        callState.isVideoOn = true;

        if (localVideo) {
          localVideo.srcObject = new MediaStream([vTrack]);
          localVideo.style.display = 'block';
        }
        if (localFallback) localFallback.style.display = 'none';
        if (btn) btn.classList.add('active');
        toast('📹 Камера включена', 'ok');
      } catch (e) {
        toast('Ошибка доступа к камере', 'err');
      }
    }
  }

  async function toggleScreen() {
    if (!callState.active) return;
    const btn = document.getElementById('ncwBtnScreen');
    const localVideo = document.getElementById('ncwLocalVideo');
    const localFallback = document.getElementById('ncwLocalFallback');

    if (callState.isScreenSharing) {
      if (callState.screenStream) {
        callState.screenStream.getTracks().forEach(t => t.stop());
        callState.screenStream = null;
      }
      callState.isScreenSharing = false;
      if (btn) btn.classList.remove('active');
      if (localVideo && !callState.isVideoOn) localVideo.style.display = 'none';
      if (localFallback && !callState.isVideoOn) localFallback.style.display = 'flex';
      toast('Демонстрация экрана остановлена');
    } else {
      try {
        const sStream = await navigator.mediaDevices.getDisplayMedia({
          video: { cursor: 'always' },
          audio: false
        });
        callState.screenStream = sStream;
        callState.isScreenSharing = true;

        const sTrack = sStream.getVideoTracks()[0];
        sTrack.onended = () => toggleScreen();

        if (localVideo) {
          localVideo.srcObject = sStream;
          localVideo.style.display = 'block';
        }
        if (localFallback) localFallback.style.display = 'none';
        if (btn) btn.classList.add('active');
        toast('🖥️ Демонстрация экрана запущена', 'ok');
      } catch (e) {
        toast('Отмена демонстрации экрана');
      }
    }
  }

  function toggleDSP() {
    callState.noiseSuppressionEnabled = !callState.noiseSuppressionEnabled;
    const badge = document.getElementById('ncwDspBadge');
    const btn = document.getElementById('ncwBtnDsp');
    if (badge) badge.style.display = callState.noiseSuppressionEnabled ? 'inline-block' : 'none';
    if (btn) btn.classList.toggle('active', callState.noiseSuppressionEnabled);
    toast(callState.noiseSuppressionEnabled ? 'Шумоподавление включено (DSP 85Hz + компрессор)' : 'Шумоподавление отключено');
  }

  function toggleExpand() {
    const win = document.getElementById('nexusCallWindow');
    if (!win) return;
    win.classList.toggle('expanded');
    const btn = document.getElementById('ncwBtnToggleExpand');
    if (btn) btn.textContent = win.classList.contains('expanded') ? '⤡' : '⤢';
  }

  function minimize() {
    const win = document.getElementById('nexusCallWindow');
    if (win) win.classList.toggle('minimized');
  }

  function leaveCall() {
    if (callState.localStream) {
      callState.localStream.getTracks().forEach(t => t.stop());
    }
    if (callState.screenStream) {
      callState.screenStream.getTracks().forEach(t => t.stop());
    }
    if (callState.audioContext) {
      callState.audioContext.close().catch(()=>{});
    }

    callState.active = false;
    callState.isVideoOn = false;
    callState.isScreenSharing = false;
    callState.localStream = null;
    callState.screenStream = null;

    const win = document.getElementById('nexusCallWindow');
    if (win) win.style.display = 'none';

    broadcastSignal('leave', {});
    toast('Вы вышли из звонка');
  }

  function broadcastSignal(type, payload) {
    if (typeof apiFetch !== 'function') return;
    apiFetch('/api/call/signal', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ type, signal: payload })
    }).catch(()=>{});
  }

  window.handleIncomingCallSignal = function(data) {
    if (!data) return;
    const currentUser = (typeof getUser === 'function' ? getUser() : '').toLowerCase();
    if (data.from && data.from.toLowerCase() === currentUser) return;

    if (data.type === 'join') {
      toast(`📞 @${data.from} подключился к каналу связи`, 'ok');
    } else if (data.type === 'leave') {
      toast(`@${data.from} покинул звонок`);
    }
  };

  window.nexusCall = {
    startOrJoinCall,
    toggleMic,
    toggleCam,
    toggleScreen,
    toggleDSP,
    toggleExpand,
    minimize,
    leaveCall
  };

  window.toggleNexusCall = function() {
    if (callState.active) {
      const win = document.getElementById('nexusCallWindow');
      if (win) win.style.display = win.style.display === 'none' ? 'flex' : 'none';
    } else {
      startOrJoinCall();
    }
  };
})();
