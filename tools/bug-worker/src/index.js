// 용의기사2 리메이크 버그 리포트를 받는 곳 (Cloudflare Worker + D1) — 창세기전3(the-war-of-genesis-clicker/tools/stats-worker)의 /v1/bug 와 같은 꼴.
//
//   POST /v1/bug                               버그 리포트 한 건(게임 메뉴의 「버그 리포트」, src/Fd2/GameWindow.BugReport.cs) — {id} 를 돌려준다. 설치마다 한 시간에 10 건까지.
//   GET  /v1/bugs?since=<id>&status=new        받은 버그 리포트(관리자만 — Authorization: Bearer <BUG_ADMIN_KEY>)
//   POST /v1/bugs/<id>/status {status, note}   버그 리포트의 상태를 바꾼다(관리자만)
//
//   POST /v1/play                              플레이 통계 — 전투 한 판마다(src/Fd2/GameWindow.PlayStats.cs)
//   GET  /v1/stats?days=30                     플레이 통계 집계(누구나)
//   GET  /dashboard                            플레이 통계 대시보드(누구나 — PC · 폰 · 장별 판 수 · 턴 수 · 시간)
//
// 누가 보냈는지는 설치할 때 만든 무작위 번호(install)뿐이다. IP 는 적지 않는다.

import DASHBOARD from './dashboard.html';

const BUG_MAX_BODY = 48 * 1024, BUG_MAX_TEXT = 8000, BUG_MAX_CONTEXT = 32 * 1024;
const BUG_PER_HOUR = 10, BUG_ALL_PER_HOUR = 300;

const json = (body, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json; charset=utf-8' } });

const int = (v, max = 2_000_000_000) => (Number.isFinite(v) ? Math.max(0, Math.min(max, Math.trunc(v))) : 0);
const text = (v, max) => (typeof v === 'string' ? v.slice(0, max) : '');
const isId = (v) => typeof v === 'string' && /^[0-9a-f-]{32,36}$/i.test(v);

async function postBug(request, env) {
  const raw = await request.text();
  if (raw.length > BUG_MAX_BODY) return json({ error: 'too large' }, 413);
  let b;
  try { b = JSON.parse(raw); } catch { return json({ error: 'bad json' }, 400); }
  if (!b || b.v !== 1 || !isId(b.install) || typeof b.text !== 'string') return json({ error: 'bad report' }, 400);
  const body = b.text.trim();
  if (!body) return json({ error: 'empty' }, 400);
  if (body.length > BUG_MAX_TEXT) return json({ error: 'text too long' }, 413);
  const context = JSON.stringify(b.context && typeof b.context === 'object' && !Array.isArray(b.context) ? b.context : {});
  if (context.length > BUG_MAX_CONTEXT) return json({ error: 'context too large' }, 413);

  const hourAgo = new Date(Date.now() - 3600_000).toISOString();
  const recent = await env.DB.prepare(
    `SELECT COUNT(*) AS n, SUM(install = ?) AS mine FROM bug_reports WHERE created_at > ?`).bind(b.install, hourAgo).first();
  if ((recent?.mine ?? 0) >= BUG_PER_HOUR || (recent?.n ?? 0) >= BUG_ALL_PER_HOUR) return json({ error: 'rate limited' }, 429);

  const row = await env.DB.prepare(
    `INSERT INTO bug_reports (created_at, version, install, text, context) VALUES (?, ?, ?, ?, ?) RETURNING id`)
    .bind(new Date().toISOString(), text(b.version, 48), b.install, body, context).first();
  return json({ ok: true, id: row.id });
}

// 관리자 열쇠 — worker 비밀(wrangler secret put BUG_ADMIN_KEY). 없으면 아무도 못 본다. 견줄 때 걸린 시간으로 새지 않게 끝까지 본다.
function isAdmin(request, env) {
  const key = env.BUG_ADMIN_KEY;
  const got = (request.headers.get('authorization') || '').replace(/^Bearer\s+/i, '');
  if (typeof key !== 'string' || key.length < 16 || got.length !== key.length) return false;
  let diff = 0;
  for (let i = 0; i < key.length; i++) diff |= key.charCodeAt(i) ^ got.charCodeAt(i);
  return diff === 0;
}

const STATUS = /^[a-z][a-z_-]{0,19}$/;

async function listBugs(url, env) {
  const since = int(Number(url.searchParams.get('since') || 0));
  const status = url.searchParams.get('status') || 'new';
  const limit = Math.max(1, Math.min(200, int(Number(url.searchParams.get('limit') || 100))));
  if (status !== 'all' && !STATUS.test(status)) return json({ error: 'bad status' }, 400);
  const sql = `SELECT id, created_at, version, install, text, context, status, note FROM bug_reports WHERE id > ?`
    + (status === 'all' ? '' : ' AND status = ?') + (url.searchParams.get('order') === 'desc' ? ' ORDER BY id DESC LIMIT ?' : ' ORDER BY id LIMIT ?');
  const args = status === 'all' ? [since, limit] : [since, status, limit];
  const { results } = await env.DB.prepare(sql).bind(...args).all();
  const bugs = results.map((r) => {
    let context = {};
    try { context = JSON.parse(r.context); } catch { context = { raw: r.context }; }
    return { ...r, context };
  });
  return json({ bugs });
}

async function setBugStatus(id, request, env) {
  let b;
  try { b = JSON.parse(await request.text()); } catch { return json({ error: 'bad json' }, 400); }
  if (!b || typeof b.status !== 'string' || !STATUS.test(b.status)) return json({ error: 'bad status' }, 400);
  const note = typeof b.note === 'string' ? b.note.slice(0, 2000) : null;
  const done = await env.DB.prepare(`UPDATE bug_reports SET status = ?, note = COALESCE(?, note) WHERE id = ?`).bind(b.status, note, id).run();
  if (!done.meta.changes) return json({ error: 'not found' }, 404);
  return json({ ok: true, id, status: b.status });
}

// ── 플레이 통계(w-93) ──────────────────────────────────────────────────────
// POST /v1/play {v:1, install, plays:[{scene, chapter, how, result, startTurn, endTurn, seconds, started, ended, version, platform, mobileView}]}
// 게임은 src/Fd2/GameWindow.PlayStats.cs. 한 번에 50 판, 설치마다 한 시간에 300 판까지.

const PLAY_MAX_BODY = 64 * 1024, PLAY_PER_POST = 50, PLAY_PER_HOUR = 300, PLAY_ALL_PER_HOUR = 20000;
const RESULTS = new Set(['win', 'lose', 'quit']), HOWS = new Set(['new', 'load']), PLATFORMS = new Set(['android', 'windows']);

async function postPlays(request, env) {
  const raw = await request.text();
  if (raw.length > PLAY_MAX_BODY) return json({ error: 'too large' }, 413);
  let b;
  try { b = JSON.parse(raw); } catch { return json({ error: 'bad json' }, 400); }
  if (!b || b.v !== 1 || !isId(b.install) || !Array.isArray(b.plays)) return json({ error: 'bad plays' }, 400);
  const rows = b.plays.slice(0, PLAY_PER_POST).filter((p) => p && typeof p === 'object'
    && RESULTS.has(p.result) && HOWS.has(p.how) && PLATFORMS.has(p.platform) && Number.isFinite(p.scene));
  if (!rows.length) return json({ ok: true, n: 0 });

  const hourAgo = new Date(Date.now() - 3600_000).toISOString();
  const recent = await env.DB.prepare(
    `SELECT COUNT(*) AS n, SUM(install = ?) AS mine FROM plays WHERE received_at > ?`).bind(b.install, hourAgo).first();
  if ((recent?.mine ?? 0) + rows.length > PLAY_PER_HOUR || (recent?.n ?? 0) >= PLAY_ALL_PER_HOUR) return json({ error: 'rate limited' }, 429);

  const now = new Date().toISOString();
  const stmt = env.DB.prepare(
    `INSERT INTO plays (received_at, install, version, platform, mobile_view, scene, chapter, how, result, turns, end_turn, seconds, started_at, ended_at)
     VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`);
  await env.DB.batch(rows.map((p) => {
    const end = int(p.endTurn, 999), start = int(p.startTurn, 999);
    return stmt.bind(now, b.install, text(p.version, 48), p.platform, p.mobileView === true ? 1 : 0,
      int(p.scene, 99), int(p.chapter, 99), p.how, p.result, Math.max(0, end - start), end, int(p.seconds, 86400),
      text(p.started, 40) || null, text(p.ended, 40) || null);
  }));
  return json({ ok: true, n: rows.length });
}

// GET /v1/stats?days=30 — 대시보드가 읽는 집계(누구나). days 를 안 주면 처음부터.
async function playStats(url, env) {
  const days = int(Number(url.searchParams.get('days') || 0), 3650);
  const since = days ? new Date(Date.now() - days * 86400_000).toISOString() : '';
  const where = `WHERE received_at >= ?`;
  const sum = `COUNT(*) AS plays, SUM(result = 'win') AS wins, SUM(result = 'lose') AS loses, SUM(result = 'quit') AS quits,
               SUM(turns) AS turns, SUM(seconds) AS seconds, COUNT(DISTINCT install) AS installs`;
  const q = (sql) => env.DB.prepare(sql).bind(since).all().then((r) => r.results);
  const [platform, chapter, daily, version, total] = await Promise.all([
    q(`SELECT platform, ${sum} FROM plays ${where} GROUP BY platform`),
    q(`SELECT chapter, platform, ${sum} FROM plays ${where} GROUP BY chapter, platform ORDER BY chapter`),
    q(`SELECT substr(COALESCE(started_at, received_at), 1, 10) AS day, platform, COUNT(*) AS plays, SUM(seconds) AS seconds, COUNT(DISTINCT install) AS installs
       FROM plays ${where} GROUP BY day, platform ORDER BY day`),
    q(`SELECT CASE WHEN instr(version, '+') > 0 THEN substr(version, 1, instr(version, '+') - 1) ELSE version END AS version, ${sum}
       FROM plays ${where} GROUP BY 1 ORDER BY MAX(id) DESC LIMIT 20`),
    q(`SELECT ${sum} FROM plays ${where}`),
  ]);
  return new Response(JSON.stringify({ days, total: total[0], platform, chapter, daily, version }), {
    headers: { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'max-age=60' },
  });
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    const { pathname } = url;
    try {
      if (request.method === 'POST' && pathname === '/v1/bug') return await postBug(request, env);
      if (request.method === 'POST' && pathname === '/v1/play') return await postPlays(request, env);
      if (request.method === 'GET' && pathname === '/v1/stats') return await playStats(url, env);
      if (request.method === 'GET' && (pathname === '/dashboard' || pathname === '/dashboard/'))
        return new Response(DASHBOARD, { headers: { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'max-age=300' } });
      if (pathname === '/v1/bugs' || pathname.startsWith('/v1/bugs/')) {
        if (!isAdmin(request, env)) return json({ error: 'unauthorized' }, 401);
        if (request.method === 'GET' && pathname === '/v1/bugs') return await listBugs(url, env);
        const m = pathname.match(/^\/v1\/bugs\/(\d{1,12})\/status$/);
        if (request.method === 'POST' && m) return await setBugStatus(Number(m[1]), request, env);
        return json({ error: 'not found' }, 404);
      }
      if (request.method === 'GET' && pathname === '/') return json({ ok: true, service: 'fd2-bugs' });
      return json({ error: 'not found' }, 404);
    } catch (e) {
      return json({ error: 'server error' }, 500);
    }
  },
};
