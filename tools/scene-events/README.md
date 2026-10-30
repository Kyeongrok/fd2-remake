# 장면 사건 대본 만들기

원작 FD2.EXE 의 사건 함수 표(0x51B91 · 90개), 장 시작 표(0x51D71), 승리 뒤 표(0x51DE9)를 capstone 으로 풀어
`src/Fd2.Data/SceneEvents.json` 을 만든다(분석-장면 §5 의 낱말). 대본은 원작 자료라 저장소에 넣지 않는다 — 가진 FD2.EXE 로 각자 만든다.
있으면 빌드가 Fd2.Data 에 넣고, 없으면 게임은 「등장 차례 k 무리는 k 턴에」로 돈다.

```
pip install capstone
set FD2_EXE=...\용의기사2_original\FD2.EXE
python gen.py                       # out.json
python patch.py ..\..\src\Fd2.Data\SceneEvents.json   # 손으로 푼 조건 다섯을 고쳐 쓴다
```
