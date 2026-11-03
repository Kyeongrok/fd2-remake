-- 플레이 통계(w-93) — 전투 한 판에 한 줄. 다시 돌려도 괜찮다(IF NOT EXISTS).
--   npx wrangler d1 execute fd2-bugs --remote --file=migrations/0002_plays.sql
CREATE TABLE IF NOT EXISTS plays (
  id          INTEGER PRIMARY KEY AUTOINCREMENT,
  received_at TEXT NOT NULL,              -- 받은 때(ISO, UTC)
  install     TEXT NOT NULL,              -- 설치할 때 만든 무작위 번호
  version     TEXT NOT NULL,              -- 게임 버전(「0.14.32+abc1234」)
  platform    TEXT NOT NULL,              -- android · windows
  mobile_view INTEGER NOT NULL DEFAULT 0, -- 폰 화면으로 놀았나(윈도우의 「모바일 화면으로 보기」 포함)
  scene       INTEGER NOT NULL,           -- 장면 번호(0 = 1장, 31 · 32 = 프롤로그)
  chapter     INTEGER NOT NULL,           -- 장(0 = 프롤로그)
  how         TEXT NOT NULL,              -- new(새로 시작) · load(전황 · 자동저장 불러오기)
  result      TEXT NOT NULL,              -- win · lose · quit
  turns       INTEGER NOT NULL,           -- 이 판에서 한 턴 수
  end_turn    INTEGER NOT NULL,           -- 끝날 때 차례 수
  seconds     INTEGER NOT NULL,           -- 논 시간(손을 댄 지 3분 안인 동안만)
  started_at  TEXT,                       -- 판 시작(게임 시계, UTC)
  ended_at    TEXT
);

CREATE INDEX IF NOT EXISTS plays_install ON plays (install, received_at);
CREATE INDEX IF NOT EXISTS plays_chapter ON plays (chapter, platform);
CREATE INDEX IF NOT EXISTS plays_started ON plays (started_at);
