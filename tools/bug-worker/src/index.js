// 용의기사2 리메이크 버그 리포트를 받는 곳 (Cloudflare Worker + D1) — 창세기전3(the-war-of-genesis-clicker/tools/stats-worker)의 /v1/bug 와 같은 꼴.
//
//   POST /v1/bug                               버그 리포트 한 건(게임 메뉴의 「버그 리포트」, src/Fd2/GameWindow.BugReport.cs) — {id} 를 돌려준다. 설치마다 한 시간에 10 건까지.
//   GET  /v1/bugs?since=<id>&status=new        받은 버그 리포트(관리자만 — Authorization: Bearer <BUG_ADMIN_KEY>)
//   POST /v1/bugs/<id>/status {status, note}   버그 리포트의 상태를 바꾼다(관리자만)
//
// 누가 보냈는지는 설치할 때 만든 무작위 번호(install)뿐이다. IP 는 적지 않는다.

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

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    const { pathname } = url;
    try {
      if (request.method === 'POST' && pathname === '/v1/bug') return await postBug(request, env);
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
