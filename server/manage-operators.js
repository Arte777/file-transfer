#!/usr/bin/env node
/**
 * NEXUS Operator Management CLI
 * Управление операторами и паролями напрямую в MongoDB.
 * Пароли хешируются с солью (crypto.scrypt) и НИКОГДА не сохраняются в открытом виде.
 *
 * Использование:
 *   node manage-operators.js list
 *   node manage-operators.js set-password <username> <newPassword>
 *   node manage-operators.js reset-all <password>
 *   node manage-operators.js remove <username>
 */

const crypto = require('crypto');
const { MongoClient } = require('mongodb');

const isRailway = !!process.env.RAILWAY_ENVIRONMENT || !!process.env.RAILWAY_STATIC_URL;
const defaultMongo = isRailway ? '' : 'mongodb://127.0.0.1:27017';
const MONGO_URI = process.env.MONGO_CONNECT || process.env.MONGOCONNECT || process.env.MONGODB_URI || process.env.MONGO_PRIVATE_URL || process.env.MONGO_URL || defaultMongo;
const DB_NAME = process.env.MONGODB_DB || 'file_transfer';

const KNOWN_OPERATORS = ['Shonll', 'DildMan', 'saha_kakaha122', 'SinGeR1isss'];

function escapeRegex(str) {
  if (typeof str !== 'string') return '';
  return str.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function hashPassword(password, salt) {
  if (!password || typeof password !== 'string') return '';
  const s = salt || crypto.randomBytes(16).toString('hex');
  const hash = crypto.scryptSync(password, s, 64).toString('hex');
  return `${s}:${hash}`;
}

async function connectDb() {
  if (!MONGO_URI) {
    console.error('❌ Ошибка: Не задана переменная окружения с MongoDB URI (MONGO_CONNECT / MONGODB_URI).');
    process.exit(1);
  }
  try {
    const client = new MongoClient(MONGO_URI, { serverSelectionTimeoutMS: 5000 });
    await client.connect();
    return { client, db: client.db(DB_NAME) };
  } catch (err) {
    console.error('❌ Ошибка подключения к MongoDB:', err.message);
    process.exit(1);
  }
}

async function listOperators() {
  const { client, db } = await connectDb();
  try {
    const operators = await db.collection('settings').find({}).toArray();
    console.log('\n=== Список операторов в MongoDB ===');
    if (operators.length === 0) {
      console.log('Нет записей в коллекции settings.');
    } else {
      for (const op of operators) {
        const hasPwd = !!op.password;
        const isHashed = hasPwd && op.password.includes(':');
        const pwdStatus = !hasPwd ? '❌ Не установлен' : isHashed ? '🔒 Захеширован (scrypt)' : '⚠️ Plaintext (рекомендуется обновить)';
        console.log(`• Пользователь: ${op.user} | Отображаемое имя: ${op.displayName || '—'} | Пароль: ${pwdStatus}`);
      }
    }
    console.log('====================================\n');
  } finally {
    await client.close();
  }
}

async function setPassword(username, newPassword) {
  if (!username || !newPassword) {
    console.error('Использование: node manage-operators.js set-password <username> <newPassword>');
    process.exit(1);
  }
  if (newPassword.length < 4) {
    console.error('❌ Ошибка: пароль должен содержать минимум 4 символа.');
    process.exit(1);
  }

  const { client, db } = await connectDb();
  try {
    const canonical = KNOWN_OPERATORS.find(k => k.toLowerCase() === username.toLowerCase()) || username;
    const hashed = hashPassword(newPassword);

    await db.collection('settings').updateOne(
      { user: { $regex: new RegExp('^' + escapeRegex(canonical) + '$', 'i') } },
      {
        $set: {
          user: canonical,
          password: hashed,
          kickedAt: new Date(),
          updatedAt: new Date()
        },
        $inc: { tokenVersion: 1 }
      },
      { upsert: true }
    );

    console.log(`✅ Пароль для оператора "${canonical}" успешно установлен в MongoDB (захеширован scrypt).`);
    console.log(`Предыдущие сессии и токены для "${canonical}" аннулированы.`);
  } finally {
    await client.close();
  }
}

async function kickAll() {
  const { client, db } = await connectDb();
  try {
    const now = new Date();
    await db.collection('settings').updateMany(
      {},
      { $inc: { tokenVersion: 1 }, $set: { kickedAt: now, updatedAt: now } }
    );
    await db.collection('system').updateOne(
      { _id: 'auth_epoch' },
      { $set: { epoch: now.getTime() } },
      { upsert: true }
    );
    console.log(`✅ Все операторы успешно кикнуты со всех устройств (${now.toISOString()}).`);
    console.log(`Все ранее выданные токены и сессии аннулированы.`);
  } finally {
    await client.close();
  }
}

async function resetAll(password) {
  if (!password || password.length < 4) {
    console.error('Использование: node manage-operators.js reset-all <password> (мин. 4 символа)');
    process.exit(1);
  }

  const { client, db } = await connectDb();
  try {
    const hashed = hashPassword(password);
    const now = new Date();
    for (const op of KNOWN_OPERATORS) {
      await db.collection('settings').updateOne(
        { user: { $regex: new RegExp('^' + escapeRegex(op) + '$', 'i') } },
        {
          $set: {
            user: op,
            password: hashed,
            kickedAt: now,
            updatedAt: now
          },
          $inc: { tokenVersion: 1 }
        },
        { upsert: true }
      );
      console.log(`✅ Установлен пароль для: ${op}`);
    }
    console.log(`\n🎉 Все операторы успешно обновлены! Все старые сессии аннулированы.`);
  } finally {
    await client.close();
  }
}

async function removeOperator(username) {
  if (!username) {
    console.error('Использование: node manage-operators.js remove <username>');
    process.exit(1);
  }
  const { client, db } = await connectDb();
  try {
    const result = await db.collection('settings').deleteOne({
      user: { $regex: new RegExp('^' + escapeRegex(username) + '$', 'i') }
    });
    console.log(`Удалено документов: ${result.deletedCount}`);
  } finally {
    await client.close();
  }
}

async function main() {
  const args = process.argv.slice(2);
  const command = args[0];

  switch (command) {
    case 'list':
      await listOperators();
      break;
    case 'set-password':
      await setPassword(args[1], args[2]);
      break;
    case 'kick-all':
      await kickAll();
      break;
    case 'reset-all':
      await resetAll(args[1]);
      break;
    case 'remove':
      await removeOperator(args[1]);
      break;
    default:
      console.log(`
NEXUS Operator Manager

Команды:
  node manage-operators.js list
  node manage-operators.js kick-all
  node manage-operators.js set-password <username> <newPassword>
  node manage-operators.js reset-all <password>
  node manage-operators.js remove <username>
      `);
      break;
  }
}

main().catch(err => {
  console.error('Ошибка:', err.message);
  process.exit(1);
});
