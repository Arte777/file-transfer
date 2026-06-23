const AI_API_URL = "https://corsproxy.io/?url=https://api.freemodel.dev/v1/chat/completions";
const AI_API_KEY = "fe_oa_e3e70c5030421d4c0cf0bcacac538b2af0cc54840007e9c2";
const AI_MODEL = "gpt-5-4";

let chatHistory = JSON.parse(localStorage.getItem('nexus_ai_chat')) || [];

document.addEventListener('DOMContentLoaded', () => {
  // Init sidebar
  const sidebarSlot = document.getElementById('sidebarSlot');
  if (sidebarSlot) sidebarSlot.innerHTML = renderHeader('ai');
  if (typeof bindLogout === 'function') bindLogout();

  const chatInput = document.getElementById('chatInput');
  const btnSend = document.getElementById('btnSendChat');
  
  if (chatInput && btnSend) {
    // Auto-resize textarea
    chatInput.addEventListener('input', function() {
      this.style.height = 'auto';
      this.style.height = (this.scrollHeight) + 'px';
      if (this.value === '') this.style.height = 'auto';
    });

    chatInput.addEventListener('keydown', (e) => {
      if (e.key === 'Enter' && !e.shiftKey) {
        e.preventDefault();
        sendMessage();
      }
    });

    btnSend.addEventListener('click', sendMessage);
  }

  // Set up marked.js options for code highlighting
  if (typeof marked !== 'undefined' && typeof hljs !== 'undefined') {
    marked.setOptions({
      highlight: function(code, lang) {
        const language = hljs.getLanguage(lang) ? lang : 'plaintext';
        return hljs.highlight(code, { language }).value;
      },
      langPrefix: 'hljs language-'
    });
  }

  renderChatHistory();
});

function renderChatHistory() {
  const container = document.getElementById('chatMessages');
  if (!container) return;
  
  // Clear container but keep welcome message if empty
  const welcome = container.querySelector('.chat-welcome');
  container.innerHTML = '';
  if (chatHistory.length === 0 && welcome) {
    container.appendChild(welcome);
    return;
  }

  chatHistory.forEach(msg => {
    appendMessage(msg.role, msg.content, false);
  });
  scrollToBottom();
}

function appendMessage(role, content, save = true) {
  const container = document.getElementById('chatMessages');
  const welcome = container.querySelector('.chat-welcome');
  if (welcome) welcome.style.display = 'none';

  if (save) {
    chatHistory.push({ role, content });
    localStorage.setItem('nexus_ai_chat', JSON.stringify(chatHistory));
  }

  const msgDiv = document.createElement('div');
  msgDiv.className = `chat-msg ${role === 'user' ? 'msg-user' : 'msg-ai'}`;
  
  const avatar = document.createElement('div');
  avatar.className = 'msg-avatar';
  avatar.textContent = role === 'user' ? (localStorage.getItem('operatorAvatar') || '👤') : '🤖';

  const contentDiv = document.createElement('div');
  contentDiv.className = 'msg-content';
  
  if (role === 'ai' && typeof marked !== 'undefined') {
    contentDiv.innerHTML = marked.parse(content);
  } else {
    contentDiv.textContent = content;
  }

  msgDiv.appendChild(avatar);
  msgDiv.appendChild(contentDiv);
  container.appendChild(msgDiv);
  scrollToBottom();
}

function appendLoading() {
  const container = document.getElementById('chatMessages');
  const msgDiv = document.createElement('div');
  msgDiv.className = 'chat-msg msg-ai loading-msg';
  msgDiv.id = 'aiLoadingIndicator';
  
  const avatar = document.createElement('div');
  avatar.className = 'msg-avatar';
  avatar.textContent = '🤖';

  const contentDiv = document.createElement('div');
  contentDiv.className = 'msg-content';
  contentDiv.innerHTML = '<span class="typing-dot"></span><span class="typing-dot"></span><span class="typing-dot"></span>';

  msgDiv.appendChild(avatar);
  msgDiv.appendChild(contentDiv);
  container.appendChild(msgDiv);
  scrollToBottom();
}

function removeLoading() {
  const loader = document.getElementById('aiLoadingIndicator');
  if (loader) loader.remove();
}

function scrollToBottom() {
  const container = document.getElementById('chatMessages');
  if (container) {
    container.scrollTop = container.scrollHeight;
  }
}

async function sendMessage() {
  const input = document.getElementById('chatInput');
  const text = input.value.trim();
  if (!text) return;

  input.value = '';
  input.style.height = 'auto';
  input.disabled = true;
  document.getElementById('btnSendChat').disabled = true;

  appendMessage('user', text);
  appendLoading();

  // Prepare messages for API
  const messages = [
    { 
      role: "system", 
      content: "You are NEXUS AI, a coding assistant. Follow the Ponytail rules: Before writing code, stop at the first rung that holds: 1. Does this need to exist? -> no: skip it (YAGNI). 2. Already in this codebase? -> reuse it. 3. Stdlib does it? -> use it. 4. Native platform feature? -> use it. 5. Installed dependency? -> use it. 6. One line? -> one line. 7. Only then: the minimum that works. Never cut validation, error handling, security, or accessibility. Use markdown. Be extremely concise." 
    }
  ];
  
  // Only send the last 10 messages to save context limit
  const recentHistory = chatHistory.slice(-10);
  messages.push(...recentHistory);

  try {
    const response = await fetch(AI_API_URL, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${AI_API_KEY}`
      },
      body: JSON.stringify({
        model: AI_MODEL,
        messages: messages,
        temperature: 0.7
      })
    });

    removeLoading();

    if (!response.ok) {
      const err = await response.json().catch(()=>({}));
      throw new Error(err.error?.message || `HTTP error! status: ${response.status}`);
    }

    const data = await response.json();
    const reply = data.choices[0].message.content;
    
    appendMessage('assistant', reply); // The API returns "assistant", we store it as such, but render it as "ai" styling
    
    // Fix the role name in our local storage to match what we expect in render
    chatHistory[chatHistory.length - 1].role = 'ai';
    localStorage.setItem('nexus_ai_chat', JSON.stringify(chatHistory));

  } catch (error) {
    removeLoading();
    appendMessage('ai', `**Ошибка API:** ${error.message}\nПроверьте соединение или API ключ.`);
  } finally {
    input.disabled = false;
    document.getElementById('btnSendChat').disabled = false;
    input.focus();
  }
}
