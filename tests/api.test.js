'use strict';

const { test } = require('node:test');
const assert = require('node:assert/strict');

// Stub OpenAI before requiring the app
const { OpenAI } = require('openai');
const originalCreate = OpenAI.prototype.chat;

// We'll monkey-patch after require
const app = require('../index.js');

// Use supertest-like raw http for simplicity
const http = require('node:http');

function request(app, method, path, body) {
  return new Promise((resolve, reject) => {
    const server = http.createServer(app);
    server.listen(0, () => {
      const port = server.address().port;
      const data = body ? JSON.stringify(body) : null;
      const options = {
        hostname: '127.0.0.1',
        port,
        path,
        method,
        headers: {
          'Content-Type': 'application/json',
          ...(data ? { 'Content-Length': Buffer.byteLength(data) } : {}),
        },
      };
      const req = http.request(options, (res) => {
        let raw = '';
        res.on('data', (chunk) => { raw += chunk; });
        res.on('end', () => {
          server.close();
          resolve({ status: res.statusCode, body: JSON.parse(raw) });
        });
      });
      req.on('error', (err) => { server.close(); reject(err); });
      if (data) req.write(data);
      req.end();
    });
  });
}

test('GET /api/modes returns list of modes', async () => {
  const res = await request(app, 'GET', '/api/modes', null);
  assert.equal(res.status, 200);
  assert.ok(Array.isArray(res.body.modes));
  assert.ok(res.body.modes.includes('simplify'));
  assert.ok(res.body.modes.includes('formal'));
  assert.ok(res.body.modes.includes('summarize'));
});

test('POST /api/convert returns 400 for missing text', async () => {
  const res = await request(app, 'POST', '/api/convert', { mode: 'simplify' });
  assert.equal(res.status, 400);
  assert.ok(res.body.error);
});

test('POST /api/convert returns 400 for empty text', async () => {
  const res = await request(app, 'POST', '/api/convert', { text: '   ', mode: 'simplify' });
  assert.equal(res.status, 400);
  assert.ok(res.body.error);
});

test('POST /api/convert returns 400 for invalid mode', async () => {
  const res = await request(app, 'POST', '/api/convert', { text: 'Hello', mode: 'invalid-mode' });
  assert.equal(res.status, 400);
  assert.ok(res.body.error);
});

test('POST /api/convert returns 400 for text over 5000 chars', async () => {
  const res = await request(app, 'POST', '/api/convert', {
    text: 'a'.repeat(5001),
    mode: 'simplify',
  });
  assert.equal(res.status, 400);
  assert.ok(res.body.error);
});
