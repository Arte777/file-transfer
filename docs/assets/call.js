// ── NEXUS WebRTC Call & Discord-Style Video/Screen Stage Module ─────────────────
// Pure SVG UI, Multi-User WebRTC Mesh, DSP Noise Suppression, Discord 0%-200% Sound Mixer & Theatre Mode

(function() {
  let callState = {
    active: false,
    localStream: null,     // Raw microphone stream
    videoStream: null,     // Local webcam stream
    screenStream: null,    // Local display media stream
    audioContext: null,
    audioAnalyser: null,
    masterGainNode: null,
    masterVolume: 100,
    isMuted: false,
    isVideoOn: false,
    isScreenSharing: false,
    noiseSuppressionEnabled: true,
    peers: new Map(),      // username.toLowerCase() -> peerObj
    focusedUser: null,     // username or 'local'
    lastSignalTime: 0,
    pollInterval: null,
    speakingInterval: null,
    processedSignalIds: new Set(),
    openVolumeTileUser: null,
    isMixerOpen: false
  };

  const RTC_CONFIG = {
    iceServers: [
      { urls: 'stun:stun.l.google.com:19302' },
      { urls: 'stun:stun1.l.google.com:19302' },
      { urls: 'stun:stun2.l.google.com:19302' }
    ]
  };

  // ── Audio Context & Analyser (For Discord Speaking Ring & Voice DSP) ──────────
  function setupAudioDSP(rawStream) {
    try {
      const AudioCtx = window.AudioContext || window.webkitAudioContext;
      if (!AudioCtx) return;

      if (!callState.audioContext) {
        callState.audioContext = new AudioCtx();
      }
      const ctx = callState.audioContext;
      if (ctx.state === 'suspended') {
        ctx.resume().catch(()=>{});
      }

      const source = ctx.createMediaStreamSource(rawStream);
      callState.audioAnalyser = ctx.createAnalyser();
      callState.audioAnalyser.fftSize = 256;
      source.connect(callState.audioAnalyser);
    } catch (e) {
      console.warn('Audio analyser init fallback:', e);
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

  // ── Volume & Mixer Persistence ───────────────────────────────────────────────
  function getUserVolume(username) {
    const uKey = (username || '').toLowerCase();
    const stored = localStorage.getItem('nexus_user_vol_' + uKey);
    if (stored !== null && stored !== undefined) {
      const v = parseInt(stored, 10);
      if (!isNaN(v)) return Math.max(0, Math.min(200, v));
    }
    return 100;
  }

  function getSavedMasterVolume() {
    const stored = localStorage.getItem('nexus_master_vol');
    if (stored !== null && stored !== undefined) {
      const v = parseInt(stored, 10);
      if (!isNaN(v)) return Math.max(0, Math.min(100, v));
    }
    return 100;
  }

  function setPeerVolume(username, volVal) {
    const uKey = (username || '').toLowerCase();
    const val = Math.max(0, Math.min(200, parseInt(volVal, 10) || 0));
    localStorage.setItem('nexus_user_vol_' + uKey, val.toString());

    const peerObj = callState.peers.get(uKey);
    if (peerObj) {
      peerObj.userVolume = val;
      if (peerObj.gainNode) {
        peerObj.gainNode.gain.value = val / 100;
      }
      const audioEl = document.getElementById('ncwAudio_' + uKey);
      if (audioEl && !peerObj.gainNode) {
        audioEl.volume = Math.max(0, Math.min(1, val / 100));
      }
    }

    const isBoosted = val > 100;
    const isMuted = val === 0;

    // Update tile popover elements if open
    const nvpVal = document.getElementById('nvpVal_' + uKey);
    if (nvpVal) {
      nvpVal.textContent = val + (isBoosted ? '% 🔥' : '%');
      nvpVal.classList.toggle('boost', isBoosted);
    }
    const nvpSlider = document.getElementById('nvpSlider_' + uKey);
    if (nvpSlider && Number(nvpSlider.value) !== val) {
      nvpSlider.value = val;
    }

    // Update mixer modal elements if open
    const nmVal = document.getElementById('nmVal_' + uKey);
    if (nmVal) {
      nmVal.textContent = val + (isBoosted ? '% 🔥' : '%');
      nmVal.classList.toggle('boost', isBoosted);
    }
    const nmSlider = document.getElementById('nmSlider_' + uKey);
    if (nmSlider && Number(nmSlider.value) !== val) {
      nmSlider.value = val;
    }

    const pop = document.getElementById('ncwVolPopover_' + uKey);
    if (pop) {
      pop.querySelectorAll('.nvp-pbtn').forEach(b => b.classList.remove('active'));
      if (val === 0) pop.querySelector('.nvp-pbtn:nth-child(1)')?.classList.add('active');
      if (val === 100) pop.querySelector('.nvp-pbtn:nth-child(2)')?.classList.add('active');
      if (val === 200) pop.querySelector('.nvp-pbtn:nth-child(3)')?.classList.add('active');
    }
  }

  function setMasterVolume(valVal) {
    const val = Math.max(0, Math.min(100, parseInt(valVal, 10) || 0));
    localStorage.setItem('nexus_master_vol', val.toString());
    callState.masterVolume = val;
    if (callState.masterGainNode) {
      callState.masterGainNode.gain.value = val / 100;
    }
    const valEl = document.getElementById('ncwMasterVolVal');
    if (valEl) valEl.textContent = val + '%';
    const sliderEl = document.getElementById('ncwMasterVolSlider');
    if (sliderEl && Number(sliderEl.value) !== val) sliderEl.value = val;
  }

  // ── SVG Icons ─────────────────────────────────────────────────────────────────
  const SVG_ICONS = {
    micOn: `<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 1a3 3 0 0 0-3 3v8a3 3 0 0 0 6 0V4a3 3 0 0 0-3-3z"/><path d="M19 10v2a7 7 0 0 1-14 0v-2"/><line x1="12" y1="19" x2="12" y2="23"/><line x1="8" y1="23" x2="16" y2="23"/></svg>`,
    micOff: `<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="1" y1="1" x2="23" y2="23"/><path d="M9 9v3a3 3 0 0 0 5.12 2.12M15 9.34V4a3 3 0 0 0-5.94-.6"/><path d="M17 16.95A7 7 0 0 1 5 12v-2m14 0v2a7 7 0 0 1-.11 1.23"/><line x1="12" y1="19" x2="12" y2="23"/><line x1="8" y1="23" x2="16" y2="23"/></svg>`,
    camOn: `<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polygon points="23 7 16 12 23 17 23 7"/><rect x="1" y="5" width="15" height="14" rx="2" ry="2"/></svg>`,
    screenOn: `<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="2" y="3" width="20" height="14" rx="2" ry="2"/><line x1="8" y1="21" x2="16" y2="21"/><line x1="12" y1="17" x2="12" y2="21"/></svg>`,
    dsp: `<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="m12 3-1.9 5.8a2 2 0 0 1-1.3 1.3L3 12l5.8 1.9a2 2 0 0 1 1.3 1.3L12 21l1.9-5.8a2 2 0 0 1 1.3-1.3L21 12l-5.8-1.9a2 2 0 0 1-1.3-1.3z"/></svg>`,
    mixer: `<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="4" y1="21" x2="4" y2="14"/><line x1="4" y1="10" x2="4" y2="3"/><line x1="12" y1="21" x2="12" y2="12"/><line x1="12" y1="8" x2="12" y2="3"/><line x1="20" y1="21" x2="20" y2="16"/><line x1="20" y1="12" x2="20" y2="3"/><line x1="1" y1="14" x2="7" y2="14"/><line x1="9" y1="8" x2="15" y2="8"/><line x1="17" y1="16" x2="23" y2="16"/></svg>`,
    volume: `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polygon points="11 5 6 9 2 9 2 15 6 15 11 19 11 5"/><path d="M15.54 8.46a5 5 0 0 1 0 7.07"/><path d="M19.07 4.93a10 10 0 0 1 0 14.14"/></svg>`,
    volumeMute: `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polygon points="11 5 6 9 2 9 2 15 6 15 11 19 11 5"/><line x1="23" y1="9" x2="17" y2="15"/><line x1="17" y1="9" x2="23" y2="15"/></svg>`,
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
        <button type="button" class="ncw-ctrl-btn" id="ncwBtnMixer" onclick="window.nexusCall.toggleMixerModal()" title="Микшер звука (0-200% как в Discord)">
          ${SVG_ICONS.mixer}
        </button>
        <button type="button" class="ncw-ctrl-btn dsp active" id="ncwBtnDsp" onclick="window.nexusCall.toggleDSP()" title="Шумоподавление (DSP 85Hz)">
          ${SVG_ICONS.dsp}
        </button>
        <button type="button" class="ncw-ctrl-btn end-call" onclick="window.nexusCall.leaveCall()" title="Покинуть звонок">
          ${SVG_ICONS.leave}
        </button>
      </div>

      <!-- Discord Sound Mixer Modal Overlay -->
      <div class="ncw-mixer-overlay" id="ncwMixerOverlay" style="display:none;" onclick="if (event.target === this) window.nexusCall.toggleMixerModal()">
        <div class="ncw-mixer-modal">
          <div class="ncw-mixer-header">
            <div class="ncw-mixer-title">
              ${SVG_ICONS.mixer}
              <span>Микшер звука</span>
            </div>
            <button type="button" class="ncw-mixer-close" onclick="window.nexusCall.toggleMixerModal()">✕</button>
          </div>
          <div class="ncw-mixer-desc">Регулировка громкости участников (как в Discord от 0% до 200%)</div>

          <div class="ncw-mixer-list" id="ncwMixerList"></div>

          <div class="ncw-mixer-footer">
            <div class="ncw-mixer-master-row">
              <div class="nmm-label">
                <span>Общая громкость звонка</span>
                <span class="nmm-val" id="ncwMasterVolVal">100%</span>
              </div>
              <input type="range" min="0" max="100" step="1" value="100" class="nvp-slider" id="ncwMasterVolSlider" oninput="window.nexusCall.setMasterVolume(this.value)">
            </div>
          </div>
        </div>
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
      if (e.target.closest('.ncw-vol-popover')) return;
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
          localTile.classList.toggle('speaking', avg > 16);
        }
      } else if (localTile) {
        localTile.classList.remove('speaking');
      }

      // Remote peers speaking check
      callState.peers.forEach((peerObj, userKey) => {
        const tile = document.getElementById('ncwParticipant_' + userKey);
        if (!tile) return;
        if (peerObj.analyser && !peerObj.isMuted && peerObj.userVolume > 0) {
          peerObj.analyser.getByteFrequencyData(buf);
          let sum = 0;
          for (let i = 0; i < buf.length; i++) sum += buf[i];
          const avg = sum / buf.length;
          tile.classList.toggle('speaking', avg > 16);
        } else {
          tile.classList.remove('speaking');
        }
      });
    }, 100);
  }

  // ── Remote Audio Pipeline (Analyser + GainNode for 0%-200% Mixer) ─────────────
  function setupRemoteAudioPipeline(peerObj, audioTrack) {
    try {
      if (!callState.audioContext) {
        callState.audioContext = new (window.AudioContext || window.webkitAudioContext)();
      }
      const ctx = callState.audioContext;
      if (ctx.state === 'suspended') {
        ctx.resume().catch(()=>{});
      }

      if (!callState.masterGainNode) {
        callState.masterGainNode = ctx.createGain();
        const masterVol = getSavedMasterVolume();
        callState.masterGainNode.gain.value = masterVol / 100;
        callState.masterGainNode.connect(ctx.destination);
      }

      if (audioTrack) {
        const stream = new MediaStream([audioTrack]);
        const src = ctx.createMediaStreamSource(stream);

        // 1. Analyser for Discord-style green speaking ring
        const an = ctx.createAnalyser();
        an.fftSize = 256;
        src.connect(an);
        peerObj.analyser = an;

        // 2. GainNode for individual user volume slider (0% to 200%)
        const userGain = ctx.createGain();
        const savedVol = getUserVolume(peerObj.user);
        userGain.gain.value = savedVol / 100;
        peerObj.gainNode = userGain;
        peerObj.userVolume = savedVol;

        src.connect(userGain);
        userGain.connect(callState.masterGainNode);
      }
    } catch (err) {
      console.warn('[WebRTC] Remote audio pipeline fallback:', err);
    }
  }

  // ── Multi-User WebRTC Peer Connection Factory ─────────────────────────────────
  function createPeerConnection(remoteUser, isInitiator = false) {
    const uKey = remoteUser.toLowerCase();
    if (callState.peers.has(uKey)) {
      return callState.peers.get(uKey).pc;
    }

    const pc = new RTCPeerConnection(RTC_CONFIG);

    // 1. Upfront video transceiver ensures video RTCRtpSender exists from the start
    // This allows instant toggleCam() and toggleScreen() without SDP renegotiation!
    let videoTransceiver = null;
    try {
      videoTransceiver = pc.addTransceiver('video', { direction: 'sendrecv' });
    } catch (e) {
      console.warn('addTransceiver error:', e);
    }

    // 2. Add local microphone audio track directly (preserves hardware echo cancellation & AGC)
    if (callState.localStream) {
      const audioTrack = callState.localStream.getAudioTracks()[0];
      if (audioTrack) {
        try {
          pc.addTrack(audioTrack, callState.localStream);
        } catch (_) {}
      }
    }

    // 3. If local video or screen is currently active, attach it to the transceiver sender
    const activeVideoTrack = (callState.screenStream && callState.screenStream.getVideoTracks()[0]) ||
                             (callState.videoStream && callState.videoStream.getVideoTracks()[0]);
    if (activeVideoTrack && videoTransceiver && videoTransceiver.sender) {
      try {
        videoTransceiver.sender.replaceTrack(activeVideoTrack);
      } catch (_) {}
    }

    const peerObj = {
      user: remoteUser,
      pc,
      videoTransceiver,
      remoteStream: new MediaStream(),
      isCam: false,
      isScreen: false,
      isMuted: false,
      analyser: null,
      gainNode: null,
      userVolume: getUserVolume(remoteUser),
      pendingCandidates: []
    };

    callState.peers.set(uKey, peerObj);

    // ICE Candidate Exchange
    pc.onicecandidate = e => {
      if (e.candidate) {
        broadcastSignal('candidate', e.candidate, remoteUser);
      }
    };

    // Remote Track Handler
    pc.ontrack = e => {
      console.log(`[WebRTC] Received remote track (${e.track.kind}) from ${remoteUser}`);
      ensureRemoteParticipantTile(remoteUser);

      if (e.track.kind === 'audio') {
        const audioEl = document.getElementById('ncwAudio_' + uKey);
        setupRemoteAudioPipeline(peerObj, e.track);

        if (audioEl) {
          audioEl.srcObject = new MediaStream([e.track]);
          // Keep audioEl muted so sound isn't doubled by GainNode
          audioEl.muted = true;
          audioEl.play().catch(() => {
            const unlock = () => {
              if (callState.audioContext && callState.audioContext.state === 'suspended') {
                callState.audioContext.resume().catch(()=>{});
              }
              audioEl.play().catch(()=>{});
              document.removeEventListener('click', unlock);
            };
            document.addEventListener('click', unlock);
          });
        }
      } else if (e.track.kind === 'video') {
        const videoEl = document.getElementById('ncwVideo_' + uKey);
        if (videoEl) {
          videoEl.srcObject = new MediaStream([e.track]);
          videoEl.muted = true;
          videoEl.play().catch(()=>{});
        }
        peerObj.remoteStream = new MediaStream([e.track]);
        updateRemoteParticipantUI(remoteUser);
      }

      e.track.onmute = () => updateRemoteParticipantUI(remoteUser);
      e.track.onunmute = () => updateRemoteParticipantUI(remoteUser);
      e.track.onended = () => updateRemoteParticipantUI(remoteUser);
    };

    pc.onconnectionstatechange = () => {
      console.log(`[WebRTC] Connection state with ${remoteUser}: ${pc.connectionState}`);
      if (pc.connectionState === 'disconnected' || pc.connectionState === 'failed' || pc.connectionState === 'closed') {
        removeRemoteParticipantUI(remoteUser);
        callState.peers.delete(uKey);
        if (callState.isMixerOpen) renderMixerList();
      }
    };

    if (isInitiator) {
      pc.createOffer({ offerToReceiveAudio: true, offerToReceiveVideo: true }).then(async offer => {
        await pc.setLocalDescription(offer);
        broadcastSignal('offer', offer, remoteUser);
      }).catch(err => {
        console.warn('Initiate offer error:', err);
      });
    }

    ensureRemoteParticipantTile(remoteUser);
    if (callState.isMixerOpen) renderMixerList();
    return pc;
  }

  // ── Remote Participant DOM Elements ──────────────────────────────────────────
  function ensureRemoteParticipantTile(user) {
    const grid = document.getElementById('ncwVideoGrid');
    if (!grid) return;

    const uKey = user.toLowerCase();
    const tileId = 'ncwParticipant_' + uKey;
    let tile = document.getElementById(tileId);
    if (!tile) {
      tile = document.createElement('div');
      tile.id = tileId;
      tile.className = 'ncw-participant remote';
      tile.ondblclick = () => window.nexusCall.focusUser(user);
      tile.oncontextmenu = e => {
        e.preventDefault();
        e.stopPropagation();
        window.nexusCall.toggleTileVolumeMenu(user);
      };

      tile.innerHTML = `
        <video id="ncwVideo_${uKey}" autoplay playsinline muted></video>
        <audio id="ncwAudio_${uKey}" autoplay playsinline></audio>
        <div class="ncw-avatar-container" id="ncwAvatarContainer_${uKey}">
          <div class="ncw-avatar-wrap">
            ${getCallAvatarHTML(user)}
          </div>
        </div>
        <div class="ncw-user-label">
          <span>${escapeHtml(user)}</span>
          <span id="ncwMicIcon_${uKey}">${SVG_ICONS.micOn}</span>
        </div>
        <div class="ncw-tile-tools">
          <button type="button" class="ncw-tile-btn" onclick="window.nexusCall.toggleTileVolumeMenu('${escapeHtml(user)}'); event.stopPropagation();" title="Громкость пользователя (0-200%)">
            ${SVG_ICONS.volume}
          </button>
          <button type="button" class="ncw-tile-btn" onclick="window.nexusCall.focusUser('${escapeHtml(user)}'); event.stopPropagation();" title="Закрепить на сцене (Discord Focus)">
            ${SVG_ICONS.focusPin}
          </button>
          <button type="button" class="ncw-tile-btn" onclick="window.nexusCall.fullscreenTile('${tileId}'); event.stopPropagation();" title="На весь экран">
            ${SVG_ICONS.fullscreen}
          </button>
        </div>
        <div class="ncw-vol-anchor" id="ncwVolAnchor_${uKey}"></div>
      `;
      grid.appendChild(tile);
    }
    return tile;
  }

  function toggleTileVolumeMenu(username) {
    const uKey = (username || '').toLowerCase();
    const anchor = document.getElementById('ncwVolAnchor_' + uKey);
    if (!anchor) return;

    const existing = document.getElementById('ncwVolPopover_' + uKey);
    if (existing) {
      existing.remove();
      callState.openVolumeTileUser = null;
      return;
    }

    // Remove any other open popovers
    document.querySelectorAll('.ncw-vol-popover').forEach(el => el.remove());

    const vol = getUserVolume(username);
    const isBoosted = vol > 100;

    const popover = document.createElement('div');
    popover.className = 'ncw-vol-popover';
    popover.id = 'ncwVolPopover_' + uKey;
    popover.onclick = e => e.stopPropagation();

    popover.innerHTML = `
      <div class="nvp-header">
        <div class="nvp-user-info">
          <span class="nvp-title">Громкость</span>
          <span class="nvp-name">@${escapeHtml(username)}</span>
        </div>
        <span class="nvp-val ${isBoosted ? 'boost' : ''}" id="nvpVal_${uKey}">${vol}${isBoosted ? '% 🔥' : '%'}</span>
      </div>
      <div class="nvp-slider-wrap">
        <input type="range" min="0" max="200" step="1" value="${vol}" class="nvp-slider" id="nvpSlider_${uKey}" oninput="window.nexusCall.setPeerVolume('${escapeHtml(username)}', this.value)">
      </div>
      <div class="nvp-presets">
        <button type="button" class="nvp-pbtn ${vol === 0 ? 'active' : ''}" onclick="window.nexusCall.setPeerVolume('${escapeHtml(username)}', 0)">0%</button>
        <button type="button" class="nvp-pbtn ${vol === 100 ? 'active' : ''}" onclick="window.nexusCall.setPeerVolume('${escapeHtml(username)}', 100)">100%</button>
        <button type="button" class="nvp-pbtn boost ${vol === 200 ? 'active' : ''}" onclick="window.nexusCall.setPeerVolume('${escapeHtml(username)}', 200)">200% 🔥</button>
      </div>
    `;

    anchor.appendChild(popover);
    callState.openVolumeTileUser = uKey;

    const closeListener = e => {
      if (!popover.contains(e.target)) {
        popover.remove();
        callState.openVolumeTileUser = null;
        document.removeEventListener('click', closeListener);
      }
    };
    setTimeout(() => document.addEventListener('click', closeListener), 20);
  }

  // ── Discord Sound Mixer Modal Panel ──────────────────────────────────────────
  function toggleMixerModal() {
    ensureCallWindowUI();
    const overlay = document.getElementById('ncwMixerOverlay');
    const btn = document.getElementById('ncwBtnMixer');
    if (!overlay) return;

    callState.isMixerOpen = !callState.isMixerOpen;
    overlay.style.display = callState.isMixerOpen ? 'flex' : 'none';
    if (btn) btn.classList.toggle('active', callState.isMixerOpen);

    if (callState.isMixerOpen) {
      renderMixerList();
      const mv = getSavedMasterVolume();
      const mvVal = document.getElementById('ncwMasterVolVal');
      if (mvVal) mvVal.textContent = mv + '%';
      const mvSlider = document.getElementById('ncwMasterVolSlider');
      if (mvSlider) mvSlider.value = mv;
    }
  }

  function renderMixerList() {
    const listEl = document.getElementById('ncwMixerList');
    if (!listEl) return;

    if (callState.peers.size === 0) {
      listEl.innerHTML = `
        <div class="ncw-mixer-empty">
          <div class="nme-icon">🎧</div>
          <div class="nme-text">В звонке пока нет других участников</div>
          <div class="nme-sub">Когда кто-то зайдет в канал, вы сможете индивидуально настроить громкость каждого</div>
        </div>
      `;
      return;
    }

    let html = '';
    callState.peers.forEach((peerObj, uKey) => {
      const user = peerObj.user;
      const vol = getUserVolume(user);
      const isMuted = vol === 0;
      const isBoosted = vol > 100;
      const avatarHtml = getCallAvatarHTML(user);

      html += `
        <div class="ncw-mixer-row" id="nmRow_${uKey}">
          <div class="nmr-user">
            <div class="nmr-avatar">${avatarHtml}</div>
            <div class="nmr-info">
              <div class="nmr-name">@${escapeHtml(user)}</div>
              <div class="nmr-status">${peerObj.isMuted ? '🔇 Заглушен' : '🎙️ Голос активен'}</div>
            </div>
          </div>

          <div class="nmr-slider-wrap">
            <div class="nmr-val-row">
              <span class="nmr-label">Громкость</span>
              <span class="nmr-val ${isBoosted ? 'boost' : ''}" id="nmVal_${uKey}">${vol}${isBoosted ? '% 🔥' : '%'}</span>
            </div>
            <input type="range" min="0" max="200" step="1" value="${vol}" class="nvp-slider" id="nmSlider_${uKey}" oninput="window.nexusCall.setPeerVolume('${escapeHtml(user)}', this.value)">
          </div>

          <div class="nmr-actions">
            <button type="button" class="nmr-btn ${isMuted ? 'active' : ''}" onclick="window.nexusCall.setPeerVolume('${escapeHtml(user)}', ${isMuted ? 100 : 0})" title="${isMuted ? 'Включить звук' : 'Заглушить'}">
              ${isMuted ? SVG_ICONS.volumeMute : SVG_ICONS.volume}
            </button>
            <button type="button" class="nmr-btn boost ${vol === 200 ? 'active' : ''}" onclick="window.nexusCall.setPeerVolume('${escapeHtml(user)}', 200)" title="Выкрутить на 200% (Discord Boost)">
              200% 🔥
            </button>
          </div>
        </div>
      `;
    });

    listEl.innerHTML = html;
  }

  function updateRemoteParticipantUI(user) {
    const uKey = user.toLowerCase();
    const peerObj = callState.peers.get(uKey);
    const tile = ensureRemoteParticipantTile(user);
    if (!peerObj || !tile) return;

    const videoEl = document.getElementById('ncwVideo_' + uKey);
    const hasLiveVideoTrack = peerObj.remoteStream && peerObj.remoteStream.getVideoTracks().some(t => t.enabled && t.readyState === 'live' && !t.muted);
    const showVideo = hasLiveVideoTrack || peerObj.isCam || peerObj.isScreen;

    tile.classList.toggle('has-video', !!showVideo);
    tile.classList.toggle('has-screen', !!peerObj.isScreen);

    if (videoEl && showVideo && peerObj.remoteStream && peerObj.remoteStream.getVideoTracks().length > 0) {
      if (videoEl.srcObject !== peerObj.remoteStream) {
        videoEl.srcObject = peerObj.remoteStream;
        videoEl.play().catch(()=>{});
      }
    }

    const micIcon = document.getElementById('ncwMicIcon_' + uKey);
    if (micIcon) {
      micIcon.innerHTML = peerObj.isMuted ? SVG_ICONS.micOff : SVG_ICONS.micOn;
    }

    if (callState.isMixerOpen) {
      const stEl = document.querySelector(`#nmRow_${uKey} .nmr-status`);
      if (stEl) stEl.textContent = peerObj.isMuted ? '🔇 Заглушен' : '🎙️ Голос активен';
    }
  }

  function removeRemoteParticipantUI(user) {
    const tile = document.getElementById('ncwParticipant_' + user.toLowerCase());
    if (tile) {
      tile.style.opacity = '0';
      tile.style.transform = 'scale(0.8)';
      setTimeout(() => tile.remove(), 250);
    }
    if (callState.focusedUser && callState.focusedUser.toLowerCase() === user.toLowerCase()) {
      resetTheaterMode();
    }
  }

  // ── Discord Theatre / Stage Mode ─────────────────────────────────────────────
  function focusUser(username) {
    const grid = document.getElementById('ncwVideoGrid');
    if (!grid) return;

    if (callState.focusedUser === username) {
      resetTheaterMode();
      return;
    }

    callState.focusedUser = username;
    grid.classList.add('has-focused');

    grid.querySelectorAll('.ncw-participant').forEach(p => p.classList.remove('is-focused'));

    let strip = grid.querySelector('.ncw-strip');
    if (!strip) {
      strip = document.createElement('div');
      strip.className = 'ncw-strip';
      grid.appendChild(strip);
    }

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
      toast('Подключение микрофона...', 'ok');
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
      callState.lastSignalTime = Date.now() - 25000;
      callState.processedSignalIds.clear();

      const localVideo = document.getElementById('ncwLocalVideo');
      const localTile = document.getElementById('ncwParticipant_local');
      if (localVideo) localVideo.style.display = 'none';
      if (localTile) localTile.classList.remove('has-video', 'has-screen');

      // Start signaling poll & voice activity detection
      startSignalingPoll();
      startVoiceDetection();

      // Announce arrival to everyone
      broadcastSignal('join', {
        isCam: callState.isVideoOn,
        isScreen: callState.isScreenSharing,
        isMuted: callState.isMuted
      });

      // Discover existing users in call room and initiate connections
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
      if (callState.videoStream) {
        callState.videoStream.getTracks().forEach(t => t.stop());
        callState.videoStream = null;
      }
      callState.isVideoOn = false;
      if (btn) btn.classList.remove('active');

      if (callState.isScreenSharing && callState.screenStream) {
        const sTrack = callState.screenStream.getVideoTracks()[0];
        if (localVideo) { localVideo.srcObject = callState.screenStream; localVideo.style.display = 'block'; }
        if (localTile) { localTile.classList.add('has-video', 'has-screen'); }
        replaceVideoTrackOnPeers(sTrack);
        broadcastSignal('state_update', { isCam: false, isScreen: true });
      } else {
        if (localVideo) { localVideo.srcObject = null; localVideo.style.display = 'none'; }
        if (localTile) { localTile.classList.remove('has-video', 'has-screen'); }
        replaceVideoTrackOnPeers(null);
        broadcastSignal('state_update', { isCam: false, isScreen: false });
      }
      toast('Камера выключена');
    } else {
      // Turn on cam
      try {
        const vStream = await navigator.mediaDevices.getUserMedia({
          video: { width: { ideal: 1280 }, height: { ideal: 720 }, frameRate: { ideal: 30 } }
        });
        callState.videoStream = vStream;
        callState.isVideoOn = true;

        if (callState.isScreenSharing && callState.screenStream) {
          callState.screenStream.getTracks().forEach(t => t.stop());
          callState.screenStream = null;
          callState.isScreenSharing = false;
          const sBtn = document.getElementById('ncwBtnScreen');
          if (sBtn) sBtn.classList.remove('active');
        }

        const vTrack = vStream.getVideoTracks()[0];
        if (localVideo) {
          localVideo.srcObject = vStream;
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

      if (callState.isVideoOn && callState.videoStream && callState.videoStream.getVideoTracks().length > 0) {
        const vTrack = callState.videoStream.getVideoTracks()[0];
        if (localVideo) { localVideo.srcObject = callState.videoStream; localVideo.style.display = 'block'; }
        if (localTile) { localTile.classList.add('has-video'); localTile.classList.remove('has-screen'); }
        replaceVideoTrackOnPeers(vTrack);
        broadcastSignal('state_update', { isCam: true, isScreen: false });
      } else {
        if (localVideo) { localVideo.srcObject = null; localVideo.style.display = 'none'; }
        if (localTile) { localTile.classList.remove('has-video', 'has-screen'); }
        replaceVideoTrackOnPeers(null);
        broadcastSignal('state_update', { isCam: false, isScreen: false });
      }

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
        sTrack.onended = () => {
          if (callState.isScreenSharing) toggleScreen();
        };

        if (localVideo) {
          localVideo.srcObject = sStream;
          localVideo.style.display = 'block';
        }
        if (localTile) {
          localTile.classList.add('has-video', 'has-screen');
        }
        if (btn) btn.classList.add('active');

        replaceVideoTrackOnPeers(sTrack);
        broadcastSignal('state_update', { isCam: false, isScreen: true });
        toast('Демонстрация экрана запущена (как в Discord)', 'ok');
      } catch (e) {
        console.warn('Screen share cancelled:', e);
        toast('Демонстрация экрана отменена');
      }
    }
  }

  // Instant track switching across all peers via RTCRtpSender.replaceTrack
  function replaceVideoTrackOnPeers(newTrack) {
    callState.peers.forEach(peerObj => {
      let videoSender = peerObj.videoTransceiver ? peerObj.videoTransceiver.sender : null;
      if (!videoSender) {
        videoSender = peerObj.pc.getSenders().find(s => s.track && s.track.kind === 'video') ||
                      peerObj.pc.getSenders().find(s => !s.track);
      }
      if (videoSender && typeof videoSender.replaceTrack === 'function') {
        videoSender.replaceTrack(newTrack).catch(err => {
          console.warn('Sender replaceTrack error:', err);
        });
      }
    });
  }

  async function toggleDSP() {
    callState.noiseSuppressionEnabled = !callState.noiseSuppressionEnabled;
    const badge = document.getElementById('ncwDspBadge');
    const btn = document.getElementById('ncwBtnDsp');
    if (badge) badge.style.display = callState.noiseSuppressionEnabled ? 'inline-block' : 'none';
    if (btn) btn.classList.toggle('active', callState.noiseSuppressionEnabled);

    if (callState.localStream) {
      const track = callState.localStream.getAudioTracks()[0];
      if (track && typeof track.applyConstraints === 'function') {
        try {
          await track.applyConstraints({
            noiseSuppression: callState.noiseSuppressionEnabled,
            echoCancellation: true
          });
        } catch (_) {}
      }
    }
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
    if (callState.videoStream) {
      callState.videoStream.getTracks().forEach(t => t.stop());
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
    callState.videoStream = null;
    callState.screenStream = null;
    callState.processedSignalIds.clear();
    callState.isMixerOpen = false;

    const win = document.getElementById('nexusCallWindow');
    if (win) {
      win.style.display = 'none';
      win.classList.remove('is-fullscreen', 'expanded', 'minimized');
    }

    resetTheaterMode();

    // Clean remote participant DOM elements & popovers
    document.querySelectorAll('.ncw-participant.remote').forEach(el => el.remove());
    document.querySelectorAll('.ncw-vol-popover').forEach(el => el.remove());

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
    }, 750);
  }

  async function handleIncomingSignal(data) {
    if (!data || !data.from) return;
    const currentUser = (typeof getUser === 'function' ? getUser() : '').toLowerCase();
    if (data.from.toLowerCase() === currentUser) return;

    // Deduplicate handled signals
    if (data.id && callState.processedSignalIds.has(data.id)) return;
    if (data.id) {
      callState.processedSignalIds.add(data.id);
      if (callState.processedSignalIds.size > 250) {
        const first = callState.processedSignalIds.values().next().value;
        callState.processedSignalIds.delete(first);
      }
    }

    const sender = data.from;
    const uKey = sender.toLowerCase();

    if (data.type === 'join') {
      toast(`📞 @${sender} зашел в голосовой канал`, 'ok');
      createPeerConnection(sender, true);
    } else if (data.type === 'offer' && data.signal) {
      const pc = createPeerConnection(sender, false);
      const peerObj = callState.peers.get(uKey);
      try {
        await pc.setRemoteDescription(new RTCSessionDescription(data.signal));
        // Flush buffered ICE candidates
        if (peerObj && peerObj.pendingCandidates && peerObj.pendingCandidates.length > 0) {
          for (const cand of peerObj.pendingCandidates) {
            try { await pc.addIceCandidate(new RTCIceCandidate(cand)); } catch (_) {}
          }
          peerObj.pendingCandidates = [];
        }
        const answer = await pc.createAnswer();
        await pc.setLocalDescription(answer);
        broadcastSignal('answer', answer, sender);
      } catch (err) {
        console.warn('Offer handle error:', err);
      }
    } else if (data.type === 'answer' && data.signal) {
      if (callState.peers.has(uKey)) {
        const peerObj = callState.peers.get(uKey);
        const pc = peerObj.pc;
        try {
          if (pc.signalingState !== 'stable') {
            await pc.setRemoteDescription(new RTCSessionDescription(data.signal));
          }
          // Flush buffered candidates
          if (peerObj.pendingCandidates && peerObj.pendingCandidates.length > 0) {
            for (const cand of peerObj.pendingCandidates) {
              try { await pc.addIceCandidate(new RTCIceCandidate(cand)); } catch (_) {}
            }
            peerObj.pendingCandidates = [];
          }
        } catch (err) {
          console.warn('Answer handle error:', err);
        }
      }
    } else if (data.type === 'candidate' && data.signal) {
      if (callState.peers.has(uKey)) {
        const peerObj = callState.peers.get(uKey);
        const pc = peerObj.pc;
        try {
          if (pc.remoteDescription && pc.remoteDescription.type) {
            await pc.addIceCandidate(new RTCIceCandidate(data.signal));
          } else {
            peerObj.pendingCandidates.push(data.signal);
          }
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
        if (callState.isMixerOpen) renderMixerList();
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
    leaveCall,
    toggleTileVolumeMenu,
    toggleMixerModal,
    setPeerVolume,
    setMasterVolume
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
