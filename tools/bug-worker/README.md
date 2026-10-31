# 버그 리포트 받는 곳 (Cloudflare Worker + D1)

게임 메뉴의 「버그 리포트」(PC 햄버거 메뉴 · 폰 「메뉴」)가 글 쓰는 창을 띄우고, 쓴 글을 여기로 보낸다(`src/Fd2/GameWindow.BugReport.cs`).
못 보내면 `%LOCALAPPDATA%\Fd2\bug-queue`(폰은 앱 데이터)에 두었다가 다음에 켤 때 다시 보낸다. 창세기전3(`the-war-of-genesis-clicker/tools/stats-worker`)의 `/v1/bug` 와 같은 꼴이다.

- 주소: `https://fd2-bugs.oceanfog1.workers.dev`
- `POST /v1/bug` — `{ v: 1, install, version, text, context }`. 글 8000자 · context 32KB · 몸 48KB 까지. 설치 하나가 한 시간에 10 건(모두 합쳐 300 건)을 넘으면 429. `{ ok, id }`.
- `GET /v1/bugs?since=<id>&status=new&limit=100` — 관리자만(`Authorization: Bearer <BUG_ADMIN_KEY>`). `status=all` 이면 모두.
- `POST /v1/bugs/<id>/status` `{ status, note }` — 관리자만. status 는 new · seen · fixed · wontfix · dup …

`context` 에 게임이 붙이는 것: 게임 버전 · OS · 플랫폼(windows · android) · 모바일 화면 · 그릴 판 크기 · 자료 폴더 이름 · 화면(타이틀 · 게임 …) · 몇 장 · 몇 턴 · 전투 상태 ·
오류 기록(`crash.txt`)의 끝 100줄. 경로의 사용자 폴더 · 이름은 `%USERPROFILE%` · `<user>` 로 바꿔 보낸다. 이름 · 계정 · IP 는 적지 않는다.

## 보기

```
./bugs.ps1                              # 새(new) 리포트
./bugs.ps1 -Status all                  # 모두
./bugs.ps1 -Id 3                        # 하나를 자세히(오류 기록 끝까지)
./bugs.ps1 -Mark 3 -Status fixed -Note "v0.14.7 에서 고침"
```

관리자 열쇠는 `~\.fd2-bug-admin-key` 의 첫 줄(worker 비밀 `BUG_ADMIN_KEY` 와 같은 값). 저장소에는 넣지 않는다.

## 올리기(처음 한 번 — 이미 했음)

```
cd tools/bug-worker
npx wrangler d1 create fd2-bugs                                   # 찍힌 database_id 를 wrangler.toml 에
npx wrangler d1 execute fd2-bugs --remote --file=schema.sql
npx wrangler secret put BUG_ADMIN_KEY                             # ~\.fd2-bug-admin-key 와 같은 값
npx wrangler deploy
```

고친 뒤에는 `npx wrangler deploy` 만 다시.
