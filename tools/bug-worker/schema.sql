-- 버그 리포트 표(게임 메뉴의 「버그 리포트」가 보낸 글). 다시 돌려도 괜찮다(IF NOT EXISTS).
--   npx wrangler d1 execute fd2-bugs --remote --file=schema.sql
CREATE TABLE IF NOT EXISTS bug_reports (
  id         INTEGER PRIMARY KEY AUTOINCREMENT,
  created_at TEXT NOT NULL,              -- 받은 때(ISO, UTC)
  version    TEXT NOT NULL,              -- 게임 버전(「0.14.6」)
  install    TEXT NOT NULL,              -- 설치할 때 만든 무작위 번호
  text       TEXT NOT NULL,              -- 사용자가 쓴 글(8000자까지)
  context    TEXT NOT NULL,              -- 게임이 붙인 것(JSON: 버전 · 기기 · 화면 · 장면 · 턴 · 오류 기록 끝)
  status     TEXT NOT NULL DEFAULT 'new',-- new · seen · fixed · wontfix · dup …
  note       TEXT                        -- 고친 사람의 메모
);

CREATE INDEX IF NOT EXISTS bug_reports_install ON bug_reports (install, created_at);
CREATE INDEX IF NOT EXISTS bug_reports_status ON bug_reports (status, id);
