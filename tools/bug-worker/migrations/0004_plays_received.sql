-- 플레이 기록을 받을 때 한 시간 횟수 제한을 세는 줄(SELECT COUNT(*) … FROM plays WHERE received_at > ?)이 색인이 없어 표 전체를 훑었다 —
-- 한 판 받을 때마다 수천 줄, 하루 70만 줄 넘게 읽어 계정의 D1 하루 읽기 한도(500만 줄)를 함께 갉았다(2026-11-25). 이제 지난 한 시간 줄만 읽는다.
CREATE INDEX IF NOT EXISTS plays_received ON plays (received_at);
