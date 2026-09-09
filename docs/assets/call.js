// ── NEXUS WebRTC Call & Discord-Style Video/Screen Stage Module ─────────────────
// Pure SVG UI, Multi-User WebRTC Mesh, DSP Noise Suppression & Theatre Mode

(function() {
  let callState = {
    active: false,
    localStream: null,
    screenStream: null,
    audioContext: null,
    audioAnalyser: null,
    processedStream: null,
    isMuted: false,
    isVideoOn: false,
    isScreenSharing: false,
    noiseSuppressionEnabled: true,
    peers: new Map(), // username -> { pc, remoteStream, isCam, isScreen, isMuted, analyser }
    focusedUser: null, // username or 'local'
    lastSignalTime: 0,
    pollInterval: null,
    speakingInterval: null
  };

  const RTC_CONFIG = {
    iceServers: [
      { urls: 'stun:stun.l.google.com:19302' },
      { urls: 'stun:stun1.l.google.com:19302' },
      { urls: 'stun:stun2.l.google.com:19302' }
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

      // 1. High-pass filter at 85Hz (eliminates desk rumble, low AC hum)
      const highpass = ctx.createBiquadFilter();
      highpass.type = 'highpass';
      highpass.frequency.value = 85;
      highpass.Q.value = 0.7;

      // 2. Peaking filter at 3000Hz (speech clarity)
      const speechClarity = ctx.createBiquadFilter();
      speechClarity.type = 'peaking';
      speechClarity.frequency.value = 3000;
      speechClarity.gain.value = 2.5;

      // 3. Dynamics Compressor (levels out whispering/shouting)
      const compressor = ctx.createDynamicsCompressor();
      compressor.threshold.setValueAtTime(-28, ctx.currentTime);
      compressor.knee.setValueAtTime(14, ctx.currentTime);
      compressor.ratio.setValueAtTime(4.5, ctx.currentTime);
      compressor.attack.setValueAtTime(0.003, ctx.currentTime);
      compressor.release.setValueAtTime(0.25, ctx.currentTime);

      // 4. Analyser for Discord-style speaking ring
      callState.audioAnalyser = ctx.createAnalyser();
      callState.audioAnalyser.fftSize = 256;

      // 5. Destination stream
      const dest = ctx.createMediaStreamDestination();
      source.connect(highpass);
      highpass.connect(speechClarity);
      speechClarity.connect(compressor);
      compressor.connect(callState.audioAnalyser);
      compressor.connect(dest);

      const processedTracks = [...dest.stream.getAudioTracks(), ...rawStream.getVideoTracks()];
      callState.processedStream = new MediaStream(processedTracks);
      return callState.processedStream;
    } catch (e) {
      console.warn('Audio DSP init fallback:', e);
      return rawStream;
    }
  }

  // ── Real User Avatar Helper (No ugly emojis!) ────────────────────────────────
  function getCallAvatarHTML(username) {
    if (typeof operatorAvatarHTML === 'function') {
      return operatorAvatarHTML(username);
    }
    const initials = (username || 'U').slice(0, 2).toUpperCase();
    return `<span class="user-initials op">${escapeHtml(initials)}</span>`;
  }

  // ── SVG Icons ─────────────────────────────────────────────────────────────────
  const SVG_ICONS = {
    micOn: `<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 1a3 3 0 0 0-3 3v8a3 3 0 0 0 6 0V4a3 3 0 0 0-3-3z"/><path d="M19 10v2a7 7 0 0 1-14 0v-2"/><line x1="12" y1="19" x2="12" y2="23"/><line x1="8" y1="23" x2="16" y2="23"/></svg>`,
    micOff: `<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="1" y1="1" x2="23" y2="23"/><path d="M9 9v3a3 3 0 0 0 5.12 2.12M15 9.34V4a3 3 0 0 0-5.94-.6"/><path d="M17 16.95A7 7 0 0 1 5 12v-2m14 0v2a7 7 0 0 1-.11 1.23"/><line x1="12" y1="19" x2="12" y2="23"/><line x1="8" y1="23" x2="16" y2="23"/></svg>`,
    camOn: `<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polygon points="23 7 16 12 23 17 23 7"/><rect x="1" y="5" width="15" height="14" rx="2" ry="2"/></svg>`,
    screenOn: `<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="2" y="3" width="20" height="14" rx="2" ry="2"/><line x1="8" y1="21" x2="16" y2="21"/><line x1="12" y1="17" x2="12" y2="21"/></svg>`,
    dsp: `<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="m12 3-1.9 5.8a2 2 0 0 1-1.3 1.3L3 12l5.8 1.9a2 2 0 0 1 1.3 1.3L12 21l1.9-5.8a2 2 0 0 1 1.3-1.3L21 12l-5.8-1.9a2 2 0 0 1-1.3-1.3z"/></svg>`,
    leave: `<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2"><path d="M10.68 13.31a16 16 0 0 0 3.41 2.6l1.27-1.27a2 2 0 0 1 2.11-.45 12.84 12.84 0 0 0 2.81.7 2 2 0 0 1 1.72 2v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.42 19.42 0 0 1-3.33-2.67m-2.67-3.34a19.79 19.79 0 0 1-3.07-8.63A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72 12.84 12.84 0 0 0 .7 2.81 2 2 0 0 1-.45 2.11L8.09 9.91"/><line x1="23" y1="1" x2="1" y2="23"/></svg>`,
    expand: `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="15 3 21 3 21 9"/><polyline points="9 21 3 21 3 15"/><line x1="21" y1="3" x2="14" y2="10"/><line x1="3" y1="21" x2="10" y2="14"/></svg>`,
    collapse: `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="4 14 10 14 10 20"/><polyline points="20 10 14 10 14 4"/><line x1="14" y1="10" x2="21" y2="3"/><line x1="3" y1="21" x2="10" y2="14"/></svg>`,
    theater: `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="3" width="18" height="18" rx="2" ry="2"/><line x1="3" y1="9" x2="21" y2="9"/><line x1="9" y1="21" x2="9" y2="9"/></svg>`,
    fullscreen: `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M8 3H5a2 2 0 0 0-2 2v3m18 0V5a2 2 0 0 0-2-2h-3m0 18h3a2 2 0 0 0 2-2v-3M3 16v3a2 2 0 0 0 2 2h3"/></svg>`,
    focusPin: `<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"/></svg>`
  };

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
          <button type="button" class="ncw-hbtn" id="ncwBtnTheater" onclick="window.nexusCall.toggleTheaterMode()" title="Режим сцены Discord">
            ${SVG_ICONS.theater}
          </button>
          <button type="button" class="ncw-hbtn" id="ncwBtnToggleExpand" onclick="window.nexusCall.toggleExpand()" title="Развернуть окно">
            ${SVG_ICONS.expand}
          </button>
          <button type="button" class="ncw-hbtn" onclick="window.nexusCall.minimize()" title="Свернуть">_</button>
          <button type="button" class="ncw-hbtn close" onclick="window.nexusCall.leaveCall()" title="Отключиться">✕</button>
        </div>
      </div>

      <div class="ncw-body" id="ncwVideoGrid">
        <!-- Local Participant -->
        <div class="ncw-participant local" id="ncwParticipant_local" ondblclick="window.nexusCall.focusUser('local')">
          <video id="ncwLocalVideo" autoplay playsinline muted></video>
          <div class="ncw-avatar-container" id="ncwLocalAvatarContainer">
            <div class="ncw-avatar-wrap" id="ncwLocalAvatarWrap"></div>
          </div>
          <div class="ncw-user-label">
            <span id="ncwLocalName">Вы</span>
            <span id="ncwLocalMicIcon">${SVG_ICONS.micOn}</span>
          </div>
          <div class="ncw-tile-tools">
            <button type="button" class="ncw-tile-btn" onclick="window.nexusCall.focusUser('local'); event.stopPropagation();" title="Закрепить на сцене (Discord Focus)">
              ${SVG_ICONS.focusPin}
            </button>
            <button type="button" class="ncw-tile-btn" onclick="window.nexusCall.fullscreenTile('ncwParticipant_local'); event.stopPropagation();" title="На весь экран">
              ${SVG_ICONS.fullscreen}
            </button>
          </div>
        </div>
      </div>

      <div class="ncw-controls">
        <button type="button" class="ncw-ctrl-btn" id="ncwBtnMic" onclick="window.nexusCall.toggleMic()" title="Вкл/Выкл микрофон (M)">
          ${SVG_ICONS.micOn}
        </button>
        <button type="button" class="ncw-ctrl-btn" id="ncwBtnCam" onclick="window.nexusCall.toggleCam()" title="Вкл/Выкл камеру">
          ${SVG_ICONS.camOn}
        </button>
        <button type="button" class="ncw-ctrl-btn" id="ncwBtnScreen" onclick="window.nexusCall.toggleScreen()" title="Демонстрация экрана (как в Discord)">
          ${SVG_ICONS.screenOn}
        </button>
        <button type="button" class="ncw-ctrl-btn dsp active" id="ncwBtnDsp" onclick="window.nexusCall.toggleDSP()" title="Шумоподавление (DSP 85Hz)">
          ${SVG_ICONS.dsp}
        </button>
        <button type="button" class="ncw-ctrl-btn end-call" onclick="window.nexusCall.leaveCall()" title="Покинуть звонок">
          ${SVG_ICONS.leave}
        </button>
      </div>
    `;

    document.body.appendChild(div);
    initCallWindowDrag();
    renderLocalAvatar();
  }

  function renderLocalAvatar() {
    const wrap = document.getElementById('ncwLocalAvatarWrap');
    if (!wrap) return;
    const currentUser = (typeof getUser === 'function' ? getUser() : 'Shonll');
    wrap.innerHTML = getCallAvatarHTML(currentUser);
    const localName = document.getElementById('ncwLocalName');
    if (localName) localName.textContent = currentUser + ' (Вы)';
  }

  function initCallWindowDrag() {
    const win = document.getElementById('nexusCallWindow');
    const header = document.getElementById('ncwHeader');
    if (!win || !header || win._dragInitialized) return;
    win._dragInitialized = true;

    let isDragging = false;
    let startX = 0, startY = 0, initialLeft = 0, initialTop = 0;

    header.addEventListener('mousedown', e => {
      if (e.target.closest('button')) return;
      if (win.classList.contains('is-fullscreen')) return;

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

  // ── Voice Activity Detection (Speaking Green Ring) ───────────────────────────
  function startVoiceDetection() {
    if (callState.speakingInterval) clearInterval(callState.speakingInterval);

    const localTile = document.getElementById('ncwParticipant_local');
    const buf = new Uint8Array(64);

    callState.speakingInterval = setInterval(() => {
      if (!callState.active) return;

      // Local speaking check
      if (callState.audioAnalyser && !callState.isMuted) {
        callState.audioAnalyser.getByteFrequencyData(buf);
        let sum = 0;
        for (let i = 0; i < buf.length; i++) sum += buf[i];
        const avg = sum / buf.length;
        if (localTile) {
          localTile.classList.toggle('speaking', avg > 18);
        }
      } else if (localTile) {
        localTile.classList.remove('speaking');
      }

      // Remote peers speaking check
      callState.peers.forEach((peerObj, user) => {
        const tile = document.getElementById('ncwParticipant_' + user);
        if (!tile) return;
        if (peerObj.analyser && !peerObj.isMuted) {
          peerObj.analyser.getByteFrequencyData(buf);
          let sum = 0;
          for (let i = 0; i < buf.length; i++) sum += buf[i];
          const avg = sum / buf.length;
          tile.classList.toggle('speaking', avg > 18);
        } else {
          tile.classList.remove('speaking');
        }
      });
    }, 120);
  }

  // ── Multi-User WebRTC Peer Connection Factory ─────────────────────────────────
  function createPeerConnection(remoteUser, isInitiator = false) {
    const uKey = remoteUser.toLowerCase();
    if (callState.peers.has(uKey)) {
      return callState.peers.get(uKey).pc;
    }

    const pc = new RTCPeerConnection(RTC_CONFIG);
    const peerObj = {
      user: remoteUser,
      pc,
      remoteStream: new MediaStream(),
      isCam: false,
      isScreen: false,
      isMuted: false,
      analyser: null
    };

    callState.peers.set(uKey, peerObj);

    // Add local tracks to peer
    const activeStream = callState.processedStream || callState.localStream;
    if (activeStream) {
      activeStream.getTracks().forEach(track => {
        pc.addTrack(track, activeStream);
      });
    }

    // ICE Candidates
    pc.onicecandidate = e => {
      if (e.candidate) {
        broadcastSignal('candidate', e.candidate, remoteUser);
      }
    };

    // Remote Track Arrived
    pc.ontrack = e => {
      if (e.streams && e.streams[0]) {
        peerObj.remoteStream = e.streams[0];
      } else {
        peerObj.remoteStream.addTrack(e.track);
      }
      updateRemoteParticipantUI(remoteUser);
      setupRemoteAudioAnalyser(peerObj);
    };

    pc.onconnectionstatechange = () => {
      if (pc.connectionState === 'disconnected' || pc.connectionState === 'failed' || pc.connectionState === 'closed') {
        removeRemoteParticipantUI(remoteUser);
        callState.peers.delete(uKey);
      }
    };

    if (isInitiator) {
      pc.onnegotiationneeded = async () => {
        try {
          const offer = await pc.createOffer();
          await pc.setLocalDescription(offer);
          broadcastSignal('offer', offer, remoteUser);
        } catch (err) {
          console.warn('Negotiation error:', err);
        }
      };
    }

    ensureRemoteParticipantTile(remoteUser);
    return pc;
  }

  function setupRemoteAudioAnalyser(peerObj) {
    try {
      if (!callState.audioContext) {
        callState.audioContext = new (window.AudioContext || window.webkitAudioContext)();
      }
      if (peerObj.remoteStream && peerObj.remoteStream.getAudioTracks().length > 0) {
        const src = callState.audioContext.createMediaStreamSource(peerObj.remoteStream);
        const an = callState.audioContext.createAnalyser();
        an.fftSize = 256;
        src.connect(an);
        peerObj.analyser = an;
      }
    } catch (_) {}
  }

  // ── Remote Participant DOM Elements ──────────────────────────────────────────
  function ensureRemoteParticipantTile(user) {
    const grid = document.getElementById('ncwVideoGrid');
    if (!grid) return;

    const tileId = 'ncwParticipant_' + user.toLowerCase();
    let tile = document.getElementById(tileId);
    if (!tile) {
      tile = document.createElement('div');
      tile.id = tileId;
      tile.className = 'ncw-participant remote';
      tile.ondblclick = () => window.nexusCall.focusUser(user);

      tile.innerHTML = `
        <video id="ncwVideo_${user.toLowerCase()}" autoplay playsinline></video>
        <audio id="ncwAudio_${user.toLowerCase()}" autoplay></audio>
        <div class="ncw-avatar-container" id="ncwAvatarContainer_${user.toLowerCase()}">
          <div class="ncw-avatar-wrap">
            ${getCallAvatarHTML(user)}
          </div>
        </div>
        <div class="ncw-user-label">
          <span>${escapeHtml(user)}</span>
          <span id="ncwMicIcon_${user.toLowerCase()}">${SVG_ICONS.micOn}</span>
        </div>
        <div class="ncw-tile-tools">
          <button type="button" class="ncw-tile-btn" onclick="window.nexusCall.focusUser('${escapeHtml(user)}'); event.stopPropagation();" title="Закрепить на сцене (Discord Focus)">
            ${SVG_ICONS.focusPin}
          </button>
          <button type="button" class="ncw-tile-btn" onclick="window.nexusCall.fullscreenTile('${tileId}'); event.stopPropagation();" title="На весь экран">
            ${SVG_ICONS.fullscreen}
          </button>
        </div>
      `;
      grid.appendChild(tile);
    }
    return tile;
  }

  function updateRemoteParticipantUI(user) {
    const uKey = user.toLowerCase();
    const peerObj = callState.peers.get(uKey);
    const tile = ensureRemoteParticipantTile(user);
    if (!peerObj || !tile) return;

    const videoEl = document.getElementById('ncwVideo_' + uKey);
    const audioEl = document.getElementById('ncwAudio_' + uKey);

    if (videoEl && peerObj.remoteStream) {
      videoEl.srcObject = peerObj.remoteStream;
    }
    if (audioEl && peerObj.remoteStream) {
      audioEl.srcObject = peerObj.remoteStream;
    }

    const hasVideo = peerObj.remoteStream && peerObj.remoteStream.getVideoTracks().some(t => t.enabled && t.readyState === 'live');
    tile.classList.toggle('has-video', hasVideo || peerObj.isCam || peerObj.isScreen);
    tile.classList.toggle('has-screen', !!peerObj.isScreen);

    const micIcon = document.getElementById('ncwMicIcon_' + uKey);
    if (micIcon) {
      micIcon.innerHTML = peerObj.isMuted ? SVG_ICONS.micOff : SVG_ICONS.micOn;
    }
  }

  function removeRemoteParticipantUI(user) {
    const tile = document.getElementById('ncwParticipant_' + user.toLowerCase());
    if (tile) {
      tile.style.opacity = '0';
      tile.style.transform = 'scale(0.8)';
      setTimeout(() => tile.remove(), 250);
    }
    if (callState.focusedUser === user) {
      resetTheaterMode();
    }
  }

  // ── Discord Theatre / Stage Mode ─────────────────────────────────────────────
  function focusUser(username) {
    const grid = document.getElementById('ncwVideoGrid');
    if (!grid) return;

    if (callState.focusedUser === username) {
      // Toggle off
      resetTheaterMode();
      return;
    }

    callState.focusedUser = username;
    grid.classList.add('has-focused');

    // Remove is-focused from all tiles
    grid.querySelectorAll('.ncw-participant').forEach(p => p.classList.remove('is-focused'));

    // Create or find strip
    let strip = grid.querySelector('.ncw-strip');
    if (!strip) {
      strip = document.createElement('div');
      strip.className = 'ncw-strip';
      grid.appendChild(strip);
    }

    // Move tiles: focused on top stage, others in strip
    const targetTileId = username === 'local' ? 'ncwParticipant_local' : 'ncwParticipant_' + username.toLowerCase();
    const targetTile = document.getElementById(targetTileId);

    if (targetTile) {
      targetTile.classList.add('is-focused');
      grid.insertBefore(targetTile, strip);
    }

    grid.querySelectorAll('.ncw-participant:not(.is-focused)').forEach(p => {
      strip.appendChild(p);
    });

    const btn = document.getElementById('ncwBtnTheater');
    if (btn) btn.classList.add('active');
  }

  function resetTheaterMode() {
    const grid = document.getElementById('ncwVideoGrid');
    if (!grid) return;

    callState.focusedUser = null;
    grid.classList.remove('has-focused');

    const strip = grid.querySelector('.ncw-strip');
    if (strip) {
      while (strip.firstChild) {
        grid.insertBefore(strip.firstChild, strip);
      }
      strip.remove();
    }

    grid.querySelectorAll('.ncw-participant').forEach(p => p.classList.remove('is-focused'));
    const btn = document.getElementById('ncwBtnTheater');
    if (btn) btn.classList.remove('active');
  }

  function toggleTheaterMode() {
    if (callState.focusedUser) {
      resetTheaterMode();
    } else {
      focusUser('local');
    }
  }

  function fullscreenTile(tileId) {
    const tile = document.getElementById(tileId);
    if (!tile) return;
    if (document.fullscreenElement) {
      document.exitFullscreen().catch(()=>{});
    } else {
      tile.requestFullscreen().catch(()=>{});
    }
  }

  // ── Public Call API ──────────────────────────────────────────────────────────
  async function startOrJoinCall() {
    ensureCallWindowUI();
    const win = document.getElementById('nexusCallWindow');
    win.style.display = 'flex';
    win.classList.remove('minimized');
    renderLocalAvatar();

    if (callState.active) {
      toast('Звонок уже активен', 'ok');
      return;
    }

    try {
      toast('Подключение микрофона с шумоподавлением DSP...', 'ok');
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
      setupAudioDSP(stream);
      callState.active = true;
      callState.lastSignalTime = Date.now();

      const localVideo = document.getElementById('ncwLocalVideo');
      const localTile = document.getElementById('ncwParticipant_local');
      if (localVideo) localVideo.style.display = 'none';
      if (localTile) localTile.classList.remove('has-video');

      // Start signaling poll & voice activity detection
      startSignalingPoll();
      startVoiceDetection();

      // Announce arrival to everyone
      broadcastSignal('join', {
        isCam: callState.isVideoOn,
        isScreen: callState.isScreenSharing,
        isMuted: callState.isMuted
      });

      // Discover existing users in call room
      fetchExistingCallUsers();
      toast('✅ Вы вошли в голосовой канал связи', 'ok');
    } catch (err) {
      console.error('Call mic error:', err);
      toast('Не удалось получить доступ к микрофону: ' + (err.message || 'отклонено'), 'err');
    }
  }

  async function fetchExistingCallUsers() {
    try {
      const res = await apiFetch('/api/call/room?room=main');
      if (res.ok) {
        const data = await res.json();
        const me = (typeof getUser === 'function' ? getUser() : '').toLowerCase();
        if (Array.isArray(data.participants)) {
          for (const p of data.participants) {
            if (p.user && p.user.toLowerCase() !== me) {
              createPeerConnection(p.user, true);
            }
          }
        }
      }
    } catch (_) {}
  }

  function toggleMic() {
    if (!callState.localStream) return;
    callState.isMuted = !callState.isMuted;
    callState.localStream.getAudioTracks().forEach(t => t.enabled = !callState.isMuted);

    const btn = document.getElementById('ncwBtnMic');
    const ind = document.getElementById('ncwLocalMicIcon');
    if (btn) {
      btn.classList.toggle('muted', callState.isMuted);
      btn.innerHTML = callState.isMuted ? SVG_ICONS.micOff : SVG_ICONS.micOn;
    }
    if (ind) {
      ind.innerHTML = callState.isMuted ? SVG_ICONS.micOff : SVG_ICONS.micOn;
    }

    broadcastSignal('state_update', { isMuted: callState.isMuted });
    toast(callState.isMuted ? 'Микрофон выключен' : 'Микрофон включен');
  }

  async function toggleCam() {
    if (!callState.active) return;
    const localVideo = document.getElementById('ncwLocalVideo');
    const localTile = document.getElementById('ncwParticipant_local');
    const btn = document.getElementById('ncwBtnCam');

    if (callState.isVideoOn) {
      // Turn off cam
      if (callState.localStream) {
        callState.localStream.getVideoTracks().forEach(t => {
          t.stop();
          callState.localStream.removeTrack(t);
        });
      }
      callState.isVideoOn = false;
      if (localVideo) { localVideo.srcObject = null; localVideo.style.display = 'none'; }
      if (localTile) localTile.classList.remove('has-video');
      if (btn) btn.classList.remove('active');

      replaceVideoTrackOnPeers(null);
      broadcastSignal('state_update', { isCam: false, isScreen: false });
      toast('Камера выключена');
    } else {
      // Turn on cam
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
        if (localTile) {
          localTile.classList.add('has-video');
          localTile.classList.remove('has-screen');
        }
        if (btn) btn.classList.add('active');

        replaceVideoTrackOnPeers(vTrack);
        broadcastSignal('state_update', { isCam: true, isScreen: false });
        toast('Камера включена', 'ok');
      } catch (e) {
        toast('Ошибка доступа к камере: ' + e.message, 'err');
      }
    }
  }

  async function toggleScreen() {
    if (!callState.active) return;
    const btn = document.getElementById('ncwBtnScreen');
    const localVideo = document.getElementById('ncwLocalVideo');
    const localTile = document.getElementById('ncwParticipant_local');

    if (callState.isScreenSharing) {
      // Stop screen share
      if (callState.screenStream) {
        callState.screenStream.getTracks().forEach(t => t.stop());
        callState.screenStream = null;
      }
      callState.isScreenSharing = false;
      if (btn) btn.classList.remove('active');

      if (callState.isVideoOn && callState.localStream.getVideoTracks().length > 0) {
        const vTrack = callState.localStream.getVideoTracks()[0];
        if (localVideo) { localVideo.srcObject = new MediaStream([vTrack]); localVideo.style.display = 'block'; }
        if (localTile) { localTile.classList.add('has-video'); localTile.classList.remove('has-screen'); }
        replaceVideoTrackOnPeers(vTrack);
      } else {
        if (localVideo) localVideo.style.display = 'none';
        if (localTile) { localTile.classList.remove('has-video'); localTile.classList.remove('has-screen'); }
        replaceVideoTrackOnPeers(null);
      }

      broadcastSignal('state_update', { isCam: callState.isVideoOn, isScreen: false });
      toast('Демонстрация экрана остановлена');
    } else {
      // Start screen share
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
        if (localTile) {
          localTile.classList.add('has-video');
          localTile.classList.add('has-screen');
        }
        if (btn) btn.classList.add('active');

        replaceVideoTrackOnPeers(sTrack);
        broadcastSignal('state_update', { isCam: false, isScreen: true });
        toast('Демонстрация экрана запущена (как в Discord)', 'ok');
      } catch (e) {
        toast('Демонстрация экрана отменена');
      }
    }
  }

  function replaceVideoTrackOnPeers(newTrack) {
    callState.peers.forEach(({ pc }) => {
      const sender = pc.getSenders().find(s => s.track && s.track.kind === 'video');
      if (sender) {
        sender.replaceTrack(newTrack);
      } else if (newTrack) {
        const activeStream = callState.processedStream || callState.localStream;
        pc.addTrack(newTrack, activeStream);
      }
    });
  }

  function toggleDSP() {
    callState.noiseSuppressionEnabled = !callState.noiseSuppressionEnabled;
    const badge = document.getElementById('ncwDspBadge');
    const btn = document.getElementById('ncwBtnDsp');
    if (badge) badge.style.display = callState.noiseSuppressionEnabled ? 'inline-block' : 'none';
    if (btn) btn.classList.toggle('active', callState.noiseSuppressionEnabled);
    toast(callState.noiseSuppressionEnabled ? 'Шумоподавление включено (DSP 85Hz)' : 'Шумоподавление выключено');
  }

  function toggleExpand() {
    const win = document.getElementById('nexusCallWindow');
    if (!win) return;
    const isFull = win.classList.contains('is-fullscreen');
    const btn = document.getElementById('ncwBtnToggleExpand');

    if (isFull) {
      win.classList.remove('is-fullscreen');
      win.classList.add('expanded');
      if (btn) btn.innerHTML = SVG_ICONS.expand;
    } else if (win.classList.contains('expanded')) {
      win.classList.add('is-fullscreen');
      if (btn) btn.innerHTML = SVG_ICONS.collapse;
    } else {
      win.classList.add('expanded');
      if (btn) btn.innerHTML = SVG_ICONS.expand;
    }
  }

  function minimize() {
    const win = document.getElementById('nexusCallWindow');
    if (win) win.classList.toggle('minimized');
  }

  function leaveCall() {
    if (callState.pollInterval) {
      clearInterval(callState.pollInterval);
      callState.pollInterval = null;
    }
    if (callState.speakingInterval) {
      clearInterval(callState.speakingInterval);
      callState.speakingInterval = null;
    }

    // Close all peers
    callState.peers.forEach(({ pc }) => pc.close());
    callState.peers.clear();

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
    if (win) {
      win.style.display = 'none';
      win.classList.remove('is-fullscreen', 'expanded', 'minimized');
    }

    resetTheaterMode();

    // Clean remote participant DOM elements
    document.querySelectorAll('.ncw-participant.remote').forEach(el => el.remove());

    broadcastSignal('leave', {});
    if (typeof apiFetch === 'function') {
      apiFetch('/api/call/leave', { method: 'POST' }).catch(()=>{});
    }

    toast('Вы покинули голосовой канал');
  }

  // ── Robust Signaling & Polling ────────────────────────────────────────────────
  function broadcastSignal(type, payload, targetUser = null) {
    if (typeof apiFetch !== 'function') return;
    apiFetch('/api/call/signal', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        type,
        signal: payload,
        to: targetUser,
        isCam: callState.isVideoOn,
        isScreen: callState.isScreenSharing,
        isMuted: callState.isMuted
      })
    }).catch(()=>{});
  }

  function startSignalingPoll() {
    if (callState.pollInterval) clearInterval(callState.pollInterval);
    callState.pollInterval = setInterval(async () => {
      if (!callState.active) return;
      try {
        const res = await apiFetch(`/api/call/signals?since=${callState.lastSignalTime}&room=main`);
        if (res.ok) {
          const data = await res.json();
          if (data.now) callState.lastSignalTime = data.now;

          if (Array.isArray(data.signals)) {
            for (const sig of data.signals) {
              handleIncomingSignal(sig);
            }
          }
        }
      } catch (_) {}
    }, 1100);
  }

  async function handleIncomingSignal(data) {
    if (!data || !data.from) return;
    const currentUser = (typeof getUser === 'function' ? getUser() : '').toLowerCase();
    if (data.from.toLowerCase() === currentUser) return;

    const sender = data.from;
    const uKey = sender.toLowerCase();

    if (data.type === 'join') {
      toast(`📞 @${sender} зашел в голосовой канал`, 'ok');
      createPeerConnection(sender, true);
    } else if (data.type === 'offer' && data.signal) {
      const pc = createPeerConnection(sender, false);
      try {
        await pc.setRemoteDescription(new RTCSessionDescription(data.signal));
        const answer = await pc.createAnswer();
        await pc.setLocalDescription(answer);
        broadcastSignal('answer', answer, sender);
      } catch (err) {
        console.warn('Offer handle error:', err);
      }
    } else if (data.type === 'answer' && data.signal) {
      if (callState.peers.has(uKey)) {
        const pc = callState.peers.get(uKey).pc;
        try {
          await pc.setRemoteDescription(new RTCSessionDescription(data.signal));
        } catch (err) {
          console.warn('Answer handle error:', err);
        }
      }
    } else if (data.type === 'candidate' && data.signal) {
      if (callState.peers.has(uKey)) {
        const pc = callState.peers.get(uKey).pc;
        try {
          await pc.addIceCandidate(new RTCIceCandidate(data.signal));
        } catch (_) {}
      }
    } else if (data.type === 'state_update') {
      if (callState.peers.has(uKey)) {
        const pObj = callState.peers.get(uKey);
        if (data.signal?.isMuted !== undefined) pObj.isMuted = data.signal.isMuted;
        if (data.signal?.isCam !== undefined) pObj.isCam = data.signal.isCam;
        if (data.signal?.isScreen !== undefined) pObj.isScreen = data.signal.isScreen;
        updateRemoteParticipantUI(sender);
      }
    } else if (data.type === 'leave') {
      toast(`@${sender} вышел из звонка`);
      removeRemoteParticipantUI(sender);
      if (callState.peers.has(uKey)) {
        callState.peers.get(uKey).pc.close();
        callState.peers.delete(uKey);
      }
    }
  }

  // ── Global Exports ────────────────────────────────────────────────────────────
  window.handleIncomingCallSignal = handleIncomingSignal;

  window.nexusCall = {
    startOrJoinCall,
    toggleMic,
    toggleCam,
    toggleScreen,
    toggleDSP,
    toggleExpand,
    toggleTheaterMode,
    focusUser,
    fullscreenTile,
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
