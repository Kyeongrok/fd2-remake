holy-heal.png / holy-heal.json
  pixelfx (https://github.com/patch-gg/pixelfx, MIT - LICENSE-pixelfx.txt) 의 holy/heal 을
  node tools/render.mjs holy/heal --sheet --no-actor --bg none --opts {"noBody":true} 로 뽑은 것.
  게임 지도 사람 위에 회색 사람 모양이 겹치지 않게 heal.js 의 몸 덧칠(recolor)을 noBody 일 때 건너뛰도록 한 줄 고쳤다(fb-86).
fire-flame-pillar.png / fire-flame-pillar.json
  pixelfx 의 fire/flame-pillar 를 node tools/render.mjs fire/flame-pillar --sheet --no-actor --bg none --fps 15 --opts {"width":56} 로 뽑은 것.
  원작 불기둥처럼 굵게: flame-pillar.js 의 기둥 폭 12 를 width 옵션으로 열고(꼭대기 불 혀 크기 k 는 3 까지), 그림 크기를 320x256(발 160,232)로 넓혔다(fb-88).
  티노-검성 치열도(필살기 모션 몸짓 · 이펙트 따로)의 땅에서 솟는 불기둥 자리에(w-161).
