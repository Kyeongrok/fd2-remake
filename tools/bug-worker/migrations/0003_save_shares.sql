-- 세이브 코드(w-171) — 계정 없이 기록 하나를 올리고 짧은 코드로 다른 기기(PC · 폰)에서 받는다. 3일 뒤 지운다.
--   npx wrangler d1 execute fd2-remake --remote --file=migrations/0003_save_shares.sql
CREATE TABLE IF NOT EXISTS save_shares (
  code        TEXT PRIMARY KEY,           -- 「K7Q29M」(보일 때는 K7Q-29M) — 헷갈리는 0 O 1 I 는 안 쓴다
  created_at  TEXT NOT NULL,              -- 올린 때(ISO, UTC)
  expires_at  TEXT NOT NULL,              -- 이때 지난 것은 못 받고, 다음 올리기 때 지운다
  install     TEXT NOT NULL,              -- 올린 설치의 무작위 번호(한 시간 올리기 수 세기에만)
  version     TEXT NOT NULL,              -- 게임 버전(「0.14.59+abc1234」)
  game        TEXT NOT NULL,              -- 원작 버전 열쇠(FD2.EXE 의 SHA-256 앞 16 자리) · 이름
  label       TEXT NOT NULL,              -- 불러오기 카드 글(「3장 — 전황 2 · 5턴」)
  kind        TEXT NOT NULL,              -- battle(싸움 중 기록) · slot(마을 기록)
  save        TEXT NOT NULL,              -- 원작 FD2.SAV 꼴 22987 바이트(걸어 둔 채) base64 — 그 기록 하나만 든 파일
  shared      TEXT                        -- 공통 가방 옆 파일(.공통가방.json) 내용
);

CREATE INDEX IF NOT EXISTS save_shares_expires ON save_shares (expires_at);
CREATE INDEX IF NOT EXISTS save_shares_install ON save_shares (install, created_at);
