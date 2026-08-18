// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
//  NEXUS WEBRTC VOICE, VIDEO & SCREEN SHARE SYSTEM
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

const RTC_CONFIG = {
  iceServers: [
    { urls: 'stun:stun.l.google.com:19302' },
    { urls: 'stun:stun1.l.google.com:19302' },
    { urls: 'stun:stun2.l.google.com:19302' }
  ]
};

let inCall = false;
let localStream = null;
let localAudioTrack = null;
let localVideoTrack = null;
let screenStream = null;

let isMicMuted = false;
let isCamEnabled = false;
let isScreenSharing = false;
let isNoiseSuppressionOn = true;

// Peer Connections: Map<operator, { pc: RTCPeerConnection, remoteStream: MediaStream, audioElement: HTMLAudioElement, videoElement: HTMLVideoElement, isTalking: boolean }>
const peerConnections = new Map();
let currentRoomParticipants = [];

// Web Audio API context for Volume Analysis (Voice Activity Detection)
let localAudioContext = null;
let localAnalyser = null;
let isLocalTalking = false;

// ── Инициализация событий звонка ──────────────────────────────────────────────
document.addEventListener('DOMContentLoaded', () => {
  setupCallListeners();
  fetchRoomStatus();
});

function setupCallListeners() {
  const btnToggleCall = document.getElementById('btnToggleCall');
  if (btnToggleCall) {
    btnToggleCall.addEventListener('click', toggleJoinLeaveCall);
  }

  document.getElementById('btnCallMic')?.addEventListener('click', toggleMic);
  document.getElementById('btnCallCam')?.addEventListener('click', toggleCam);
  document.getElementById('btnCallScreen')?.addEventListener('click', toggleScreenShare);
  document.getElementById('btnCallNoise')?.addEventListener('click', toggleNoiseSuppression);
  document.getElementById('btnCallLeave')?.addEventListener('click', leaveCall);
}

// ── Получение статуса комнаты ────────────────────────────────────────────────
async function fetchRoomStatus() {
  try {
    const res = await apiFetch('/api/call/room');
    if (!res.ok) return;
    const data = await res.json();
    if (data && Array.isArray(data.participants)) {
      updateRoomBadge(data.participants);
    }
  } catch (e) {}
}

function updateRoomBadge(participants) {
  currentRoomParticipants = participants || [];
  const badge = document.getElementById('callParticipantsBadge');
  const count = currentRoomParticipants.length;
  if (badge) {
    if (count > 0) {
      badge.textContent = count;
      badge.style.display = 'inline-flex';
    } else {
      badge.style.display = 'none';
    }
  }

  if (inCall) {
    renderCallStage();
  }
}

// ── Подключение / Выход из звонка ─────────────────────────────────────────────
async function toggleJoinLeaveCall() {
  if (inCall) {
    leaveCall();
  } else {
    joinCall();
  }
}

async function joinCall() {
  const user = getUser();
  if (!user) {
    toast('Требуется авторизация', 'err');
    return;
  }

  try {
    toast('Подключение к голосовому каналу...');
    
    // Получаем аудиопоток с шумоподавлением (с безопасным фолбэком)
    try {
      localStream = await navigator.mediaDevices.getUserMedia({
        audio: {
          echoCancellation: true,
          noiseSuppression: isNoiseSuppressionOn,
          autoGainControl: true
        },
        video: false
      });
    } catch(errMic) {
      console.warn('Fallback to basic audio constraints:', errMic);
      localStream = await navigator.mediaDevices.getUserMedia({ audio: true, video: false });
    }

    localAudioTrack = localStream.getAudioTracks()[0];
    if (localAudioTrack) {
      localAudioTrack.enabled = !isMicMuted;
    }

    // Инициализируем анализатор речи
    try {
      setupAudioActivityAnalyzer(localStream);
    } catch(errAudio) {
      console.warn('Audio analyzer init error:', errAudio);
    }

    inCall = true;

    // Обновляем UI сразу при получении доступа к микрофону
    const callStage = document.getElementById('callStage');
    if (callStage) callStage.style.display = 'block';
    
    const btnToggle = document.getElementById('btnToggleCall');
    if (btnToggle) {
      btnToggle.classList.add('in-call');
      btnToggle.innerHTML = `
        <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2"><path d="M10.68 13.31a16 16 0 0 0 3.41 2.6l1.27-1.27a2 2 0 0 1 2.11-.45 12.84 12.84 0 0 0 2.81.7 2 2 0 0 1 1.72 2v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.42 19.42 0 0 1-6-6 19.8 19.8 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72 12.84 12.84 0 0 0 .7 2.81 2 2 0 0 1-.45 2.11L8.09 9.91"></path><line x1="23" y1="1" x2="1" y2="23"></line></svg>
        <span>Отключиться</span>
      `;
    }

    renderCallStage();
    toast('Вы подключились к каналу связи');

    // Уведомляем сервер о подключении и связываемся с пирами
    try {
      const joinRes = await apiFetch('/api/call/join', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          audioMuted: isMicMuted,
          videoEnabled: isCamEnabled,
          isScreenSharing: isScreenSharing
        })
      });

      if (joinRes.ok) {
        const joinData = await joinRes.json();
        currentRoomParticipants = joinData.participants || [];
        renderCallStage();

        // Инициируем P2P соединения со всеми уже присутствующими операторами
        currentRoomParticipants.forEach(p => {
          if (p.operator !== user) {
            initiatePeerConnection(p.operator, true);
          }
        });
      }
    } catch(errJoin) {
      console.warn('Call join signaling error:', errJoin);
    }

  } catch (e) {
    console.error('Ошибка доступа к микрофону:', e);
    toast(e.message || 'Ошибка доступа к микрофону', 'err');
    leaveCall();
  }
}

async function leaveCall() {
  inCall = false;
  
  // Останавливаем все локальные потоки
  if (localStream) {
    localStream.getTracks().forEach(t => t.stop());
    localStream = null;
    localAudioTrack = null;
    localVideoTrack = null;
  }
  if (screenStream) {
    screenStream.getTracks().forEach(t => t.stop());
    screenStream = null;
  }

  // Закрываем все пир-соединения
  peerConnections.forEach(({ pc, audioElement }) => {
    try { pc.close(); } catch (e) {}
    try { audioElement?.remove(); } catch (e) {}
  });
  peerConnections.clear();

  if (localAudioContext) {
    try { localAudioContext.close(); } catch(e) {}
    localAudioContext = null;
    localAnalyser = null;
  }

  isCamEnabled = false;
  isScreenSharing = false;

  // Серверное уведомление
  try {
    await apiFetch('/api/call/leave', { method: 'POST' });
  } catch (e) {}

  // Обновляем UI
  const callStage = document.getElementById('callStage');
  if (callStage) callStage.style.display = 'none';

  const btnToggle = document.getElementById('btnToggleCall');
  if (btnToggle) {
    btnToggle.classList.remove('in-call');
    btnToggle.innerHTML = `
      <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2"><path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72 12.84 12.84 0 0 0 .7 2.81 2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45 12.84 12.84 0 0 0 2.81.7A2 2 0 0 1 22 16.92z"></path></svg>
      <span>Голосовой канал</span>
    `;
  }

  fetchRoomStatus();
  toast('Вы вышли из голосового канала');
}

// ── WebRTC Peer Connection (Mesh) ────────────────────────────────────────────
function initiatePeerConnection(targetOperator, isInitiator = false) {
  if (peerConnections.has(targetOperator)) {
    return peerConnections.get(targetOperator).pc;
  }

  const pc = new RTCPeerConnection(RTC_CONFIG);
  const remoteStream = new MediaStream();
  const audioElement = new Audio();
  audioElement.autoplay = true;
  audioElement.srcObject = remoteStream;

  const peerData = {
    pc,
    remoteStream,
    audioElement,
    isTalking: false
  };

  peerConnections.set(targetOperator, peerData);

  // Добавляем локальные треки
  if (localStream) {
    localStream.getTracks().forEach(track => {
      pc.addTrack(track, localStream);
    });
  }

  // Обработка ICE кандидатов
  pc.onicecandidate = (event) => {
    if (event.candidate) {
      sendSignal(targetOperator, { candidate: event.candidate }, 'candidate');
    }
  };

  // Получение удалённого трека
  pc.ontrack = (event) => {
    event.streams[0].getTracks().forEach(track => {
      if (!remoteStream.getTracks().includes(track)) {
        remoteStream.addTrack(track);
      }
    });

    // Настраиваем отслеживание голоса для собеседника
    setupRemoteAudioAnalyzer(targetOperator, remoteStream);
    renderCallStage();
  };

  pc.onconnectionstatechange = () => {
    if (pc.connectionState === 'disconnected' || pc.connectionState === 'failed' || pc.connectionState === 'closed') {
      peerConnections.delete(targetOperator);
      renderCallStage();
    }
  };

  // Если мы инициатор — создаём Offer
  if (isInitiator) {
    pc.onnegotiationneeded = async () => {
      try {
        const offer = await pc.createOffer();
        await pc.setLocalDescription(offer);
        sendSignal(targetOperator, { sdp: pc.localDescription }, 'offer');
      } catch (err) {
        console.error('Offer error:', err);
      }
    };
  }

  return pc;
}

async function sendSignal(to, signal, type) {
  try {
    await apiFetch('/api/call/signal', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ to, signal, type })
    });
  } catch (e) {}
}

// ── Обработка входящих сигналов WebRTC (через SSE) ───────────────────────────
async function handleIncomingCallSignal(msg) {
  if (!inCall) return;
  const { from, signal, type } = msg;
  if (!from || from === getUser()) return;

  let peer = peerConnections.get(from);
  let pc = peer ? peer.pc : initiatePeerConnection(from, false);

  try {
    if (signal.sdp) {
      await pc.setRemoteDescription(new RTCSessionDescription(signal.sdp));
      if (signal.sdp.type === 'offer') {
        const answer = await pc.createAnswer();
        await pc.setLocalDescription(answer);
        sendSignal(from, { sdp: pc.localDescription }, 'answer');
      }
    } else if (signal.candidate) {
      try {
        await pc.addIceCandidate(new RTCIceCandidate(signal.candidate));
      } catch (err) {}
    }
  } catch (e) {
    console.error('Signal handling error:', e);
  }
}

// ── Обновление состояния звонка (SSE) ────────────────────────────────────────
function handleCallRoomUpdate(eventData) {
  const { participants, action, operator } = eventData;
  updateRoomBadge(participants);

  if (inCall) {
    if (action === 'leave' && operator) {
      if (peerConnections.has(operator)) {
        const p = peerConnections.get(operator);
        try { p.pc.close(); } catch(e) {}
        try { p.audioElement?.remove(); } catch(e) {}
        peerConnections.delete(operator);
      }
    } else if (action === 'join' && operator && operator !== getUser()) {
      initiatePeerConnection(operator, true);
    }
    renderCallStage();
  }
}

// ── Индикация речи (Voice Activity Detection) ─────────────────────────────────
function setupAudioActivityAnalyzer(stream) {
  try {
    const AudioContext = window.AudioContext || window.webkitAudioContext;
    if (!AudioContext) return;

    localAudioContext = new AudioContext();
    const source = localAudioContext.createMediaStreamSource(stream);
    localAnalyser = localAudioContext.createAnalyser();
    localAnalyser.fftSize = 256;
    source.connect(localAnalyser);

    const bufferLength = localAnalyser.frequencyBinCount;
    const dataArray = new Uint8Array(bufferLength);

    function checkLevel() {
      if (!inCall || !localAnalyser) return;
      localAnalyser.getByteFrequencyData(dataArray);

      let sum = 0;
      for (let i = 0; i < bufferLength; i++) {
        sum += dataArray[i];
      }
      const avg = sum / bufferLength;
      const talking = avg > 14 && !isMicMuted;

      if (talking !== isLocalTalking) {
        isLocalTalking = talking;
        updateTalkingVisual(getUser(), isLocalTalking);
      }

      requestAnimationFrame(checkLevel);
    }

    checkLevel();
  } catch (e) {}
}

function setupRemoteAudioAnalyzer(operator, stream) {
  try {
    const AudioContext = window.AudioContext || window.webkitAudioContext;
    if (!AudioContext) return;

    const ctx = new AudioContext();
    const source = ctx.createMediaStreamSource(stream);
    const analyser = ctx.createAnalyser();
    analyser.fftSize = 256;
    source.connect(analyser);

    const bufferLength = analyser.frequencyBinCount;
    const dataArray = new Uint8Array(bufferLength);

    function checkRemoteLevel() {
      const peer = peerConnections.get(operator);
      if (!inCall || !peer) {
        try { ctx.close(); } catch(e) {}
        return;
      }

      analyser.getByteFrequencyData(dataArray);
      let sum = 0;
      for (let i = 0; i < bufferLength; i++) sum += dataArray[i];
      const avg = sum / bufferLength;
      const talking = avg > 14;

      if (talking !== peer.isTalking) {
        peer.isTalking = talking;
        updateTalkingVisual(operator, talking);
      }

      requestAnimationFrame(checkRemoteLevel);
    }

    checkRemoteLevel();
  } catch(e) {}
}

function updateTalkingVisual(operator, isTalking) {
  const card = document.getElementById(`callCard_${operator}`);
  if (card) {
    if (isTalking) {
      card.classList.add('is-talking');
    } else {
      card.classList.remove('is-talking');
    }
  }
}

// ── Управление устройствами (Микрофон, Камера, Экран, Шумоподав) ──────────────
async function toggleMic() {
  if (!localAudioTrack) return;
  isMicMuted = !isMicMuted;
  localAudioTrack.enabled = !isMicMuted;

  const btn = document.getElementById('btnCallMic');
  if (btn) {
    if (isMicMuted) {
      btn.classList.add('disabled');
      btn.innerHTML = `<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2"><line x1="1" y1="1" x2="23" y2="23"></line><path d="M9 9v3a3 3 0 0 0 5.12 2.12M15 9.34V4a3 3 0 0 0-5.94-.6"></path><path d="M17 16.95A7 7 0 0 1 5 12v-2m14 0v2a7 7 0 0 1-.11 1.23"></path><line x1="12" y1="19" x2="12" y2="23"></line><line x1="8" y1="23" x2="16" y2="23"></line></svg><span>Микрофон: Выкл</span>`;
    } else {
      btn.classList.remove('disabled');
      btn.innerHTML = `<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 1a3 3 0 0 0-3 3v8a3 3 0 0 0 6 0V4a3 3 0 0 0-3-3z"></path><path d="M19 10v2a7 7 0 0 1-14 0v-2"></path><line x1="12" y1="19" x2="12" y2="23"></line><line x1="8" y1="23" x2="16" y2="23"></line></svg><span>Микрофон: Вкл</span>`;
    }
  }

  notifyStateChange();
}

async function toggleCam() {
  try {
    if (isCamEnabled) {
      // Отключаем камеру
      if (localVideoTrack) {
        localVideoTrack.stop();
        localStream.removeTrack(localVideoTrack);
        removeTrackFromPeers(localVideoTrack);
        localVideoTrack = null;
      }
      isCamEnabled = false;
    } else {
      // Включаем камеру
      const vStream = await navigator.mediaDevices.getUserMedia({ video: { width: 1280, height: 720 } });
      const newVTrack = vStream.getVideoTracks()[0];
      localVideoTrack = newVTrack;
      localStream.addTrack(newVTrack);
      addOrReplaceVideoTrack(newVTrack);
      isCamEnabled = true;
    }

    updateCamBtnState();
    renderCallStage();
    notifyStateChange();
  } catch (e) {
    toast('Не удалось запустить камеру', 'err');
  }
}

function updateCamBtnState() {
  const btn = document.getElementById('btnCallCam');
  if (!btn) return;
  if (isCamEnabled) {
    btn.classList.add('active');
    btn.innerHTML = `<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2"><polygon points="23 7 16 12 23 17 23 7"></polygon><rect x="1" y="5" width="15" height="14" rx="2" ry="2"></rect></svg><span>Камера: Вкл</span>`;
  } else {
    btn.classList.remove('active');
    btn.innerHTML = `<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2"><path d="M16 16v1a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2h11a2 2 0 0 1 2 2v1"></path><polygon points="23 7 16 12 23 17 23 7"></polygon><line x1="1" y1="1" x2="23" y2="23"></line></svg><span>Камера: Выкл</span>`;
  }
}

async function toggleScreenShare() {
  try {
    if (isScreenSharing) {
      stopScreenShare();
    } else {
      screenStream = await navigator.mediaDevices.getDisplayMedia({
        video: { frameRate: 30, cursor: 'always' },
        audio: true
      });

      const screenTrack = screenStream.getVideoTracks()[0];
      screenTrack.onended = () => stopScreenShare();

      addOrReplaceVideoTrack(screenTrack);
      isScreenSharing = true;
      updateScreenBtnState();
      renderCallStage();
      notifyStateChange();
      toast('Демонстрация экрана запущена');
    }
  } catch (e) {
    if (e.name !== 'NotAllowedError') {
      toast('Ошибка запуска демонстрации экрана', 'err');
    }
  }
}

function stopScreenShare() {
  if (screenStream) {
    screenStream.getTracks().forEach(t => t.stop());
    screenStream = null;
  }
  isScreenSharing = false;

  if (isCamEnabled && localVideoTrack) {
    addOrReplaceVideoTrack(localVideoTrack);
  } else {
    removeVideoTracksFromPeers();
  }

  updateScreenBtnState();
  renderCallStage();
  notifyStateChange();
}

function updateScreenBtnState() {
  const btn = document.getElementById('btnCallScreen');
  if (!btn) return;
  if (isScreenSharing) {
    btn.classList.add('active');
    btn.innerHTML = `<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2"><rect x="2" y="3" width="20" height="14" rx="2" ry="2"></rect><line x1="8" y1="21" x2="16" y2="21"></line><line x1="12" y1="17" x2="12" y2="21"></line></svg><span>Демонстрация: Вкл</span>`;
  } else {
    btn.classList.remove('active');
    btn.innerHTML = `<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2"><rect x="2" y="3" width="20" height="14" rx="2" ry="2"></rect><line x1="8" y1="21" x2="16" y2="21"></line><line x1="12" y1="17" x2="12" y2="21"></line></svg><span>Демонстрация</span>`;
  }
}

async function toggleNoiseSuppression() {
  isNoiseSuppressionOn = !isNoiseSuppressionOn;
  
  if (localAudioTrack) {
    try {
      await localAudioTrack.applyConstraints({
        noiseSuppression: isNoiseSuppressionOn,
        echoCancellation: true,
        autoGainControl: true
      });
    } catch(e) {}
  }

  const btn = document.getElementById('btnCallNoise');
  if (btn) {
    if (isNoiseSuppressionOn) {
      btn.classList.add('active');
      btn.innerHTML = `<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2"><polygon points="11 5 6 9 2 9 2 15 6 15 11 19 11 5"></polygon><path d="M15.54 8.46a5 5 0 0 1 0 7.07"></path></svg><span>Шумоподавление: Вкл</span>`;
      toast('Шумоподавление включено');
    } else {
      btn.classList.remove('active');
      btn.innerHTML = `<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2"><polygon points="11 5 6 9 2 9 2 15 6 15 11 19 11 5"></polygon><line x1="23" y1="9" x2="17" y2="15"></line><line x1="17" y1="9" x2="23" y2="15"></line></svg><span>Шумоподавление: Выкл</span>`;
      toast('Шумоподавление выключено');
    }
  }
}

function addOrReplaceVideoTrack(newTrack) {
  peerConnections.forEach(({ pc }) => {
    const sender = pc.getSenders().find(s => s.track && s.track.kind === 'video');
    if (sender) {
      sender.replaceTrack(newTrack);
    } else {
      pc.addTrack(newTrack, localStream || screenStream);
    }
  });
}

function removeTrackFromPeers(track) {
  peerConnections.forEach(({ pc }) => {
    const sender = pc.getSenders().find(s => s.track === track);
    if (sender) pc.removeTrack(sender);
  });
}

function removeVideoTracksFromPeers() {
  peerConnections.forEach(({ pc }) => {
    const sender = pc.getSenders().find(s => s.track && s.track.kind === 'video');
    if (sender) pc.removeTrack(sender);
  });
}

async function notifyStateChange() {
  try {
    await apiFetch('/api/call/state', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        audioMuted: isMicMuted,
        videoEnabled: isCamEnabled,
        isScreenSharing: isScreenSharing
      })
    });
  } catch (e) {}
}

// ── Отрисовка видовой панели звонка (Call Stage) ──────────────────────────────
function renderCallStage() {
  const container = document.getElementById('callGrid');
  if (!container || !inCall) return;

  const myUser = getUser();
  const allUsers = [...currentRoomParticipants];
  
  // Гарантируем, что текущий оператор в списке
  if (!allUsers.some(u => u.operator === myUser)) {
    allUsers.unshift({
      operator: myUser,
      displayName: operatorDisplayName(myUser),
      avatarImage: localStorage.getItem('ft_avatarImage'),
      audioMuted: isMicMuted,
      videoEnabled: isCamEnabled,
      isScreenSharing: isScreenSharing
    });
  }

  let html = '';
  allUsers.forEach(u => {
    const isMe = u.operator === myUser;
    const displayName = isMe ? `${escapeHtml(operatorDisplayName(myUser))} (Вы)` : escapeHtml(u.displayName || u.operator);
    const isMuted = isMe ? isMicMuted : !!u.audioMuted;
    const hasVideo = isMe ? (isCamEnabled || isScreenSharing) : (u.videoEnabled || u.isScreenSharing);

    let avatarContent = `<div class="call-user-avatar">${escapeHtml((u.operator || 'O').charAt(0).toUpperCase())}</div>`;
    if (u.avatarImage) {
      avatarContent = `<img src="${u.avatarImage}" class="call-user-avatar-img" alt="avatar">`;
    }

    html += `
      <div class="call-participant-card ${isMe && isLocalTalking ? 'is-talking' : ''}" id="callCard_${u.operator}">
        <div class="call-video-wrap" id="callVideoWrap_${u.operator}">
          <div class="call-avatar-placeholder" id="callPlaceholder_${u.operator}" style="${hasVideo ? 'display:none;' : ''}">
            ${avatarContent}
          </div>
          <video id="callVideo_${u.operator}" class="call-video-feed" autoplay playsinline muted="${isMe ? 'true' : 'false'}" style="${hasVideo ? '' : 'display:none;'}"></video>
        </div>
        
        <div class="call-participant-info">
          <span class="call-participant-name">${displayName}</span>
          <div class="call-status-icons">
            ${isMuted ? '<span class="call-muted-badge" title="Микрофон выключен"><svg viewBox="0 0 24 24" width="12" height="12" fill="none" stroke="currentColor" stroke-width="2"><line x1="1" y1="1" x2="23" y2="23"></line><path d="M9 9v3a3 3 0 0 0 5.12 2.12M15 9.34V4a3 3 0 0 0-5.94-.6"></path><path d="M17 16.95A7 7 0 0 1 5 12v-2m14 0v2a7 7 0 0 1-.11 1.23"></path></svg></span>' : ''}
            ${u.isScreenSharing ? '<span class="call-screen-badge" title="Демонстрация экрана"><svg viewBox="0 0 24 24" width="12" height="12" fill="none" stroke="currentColor" stroke-width="2"><rect x="2" y="3" width="20" height="14" rx="2" ry="2"></rect><line x1="8" y1="21" x2="16" y2="21"></line></svg></span>' : ''}
          </div>
        </div>
      </div>
    `;
  });

  container.innerHTML = html;

  // Прикрепляем локальный видеопоток
  if (isCamEnabled || isScreenSharing) {
    const myVideo = document.getElementById(`callVideo_${myUser}`);
    if (myVideo) {
      myVideo.srcObject = isScreenSharing ? screenStream : localStream;
      myVideo.style.display = 'block';
      const pl = document.getElementById(`callPlaceholder_${myUser}`);
      if (pl) pl.style.display = 'none';
    }
  }

  // Прикрепляем удалённые видеопотоки
  peerConnections.forEach(({ remoteStream }, peerUser) => {
    const videoTracks = remoteStream.getVideoTracks();
    if (videoTracks.length > 0 && videoTracks[0].enabled) {
      const vid = document.getElementById(`callVideo_${peerUser}`);
      const pl = document.getElementById(`callPlaceholder_${peerUser}`);
      if (vid) {
        vid.srcObject = remoteStream;
        vid.style.display = 'block';
      }
      if (pl) pl.style.display = 'none';
    }
  });
}

// ── Экспорт в глобальную область для связки с app.js (SSE) ────────────────────
window.handleIncomingCallSignal = handleIncomingCallSignal;
window.handleCallRoomUpdate = handleCallRoomUpdate;
