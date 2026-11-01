# 장면 사건 대본 만들기

원작 FD2.EXE 의 사건 함수 표(0x51B91 · 90개), 장 시작 표(0x51D71), 승리 뒤 표(0x51DE9)를 capstone 으로 풀어
`src/Fd2.Data/SceneEvents.json` 을 만든다(분석-장면 §5 의 낱말). 대본은 저장소에 넣어 두고 빌드가 실행 파일에 넣는다(릴리즈 · 안드로이드 포함) — 다시 만들 때만 아래를 돌린다.
파일이 없으면 게임은 「등장 차례 k 무리는 k 턴에」로 돈다.

```
pip install capstone
set FD2_EXE=...\용의기사2_original\FD2.EXE
python gen.py                       # out.json
python patch.py ..\..\src\Fd2.Data\SceneEvents.json   # 손으로 푼 조건 다섯을 고쳐 쓴다
```
