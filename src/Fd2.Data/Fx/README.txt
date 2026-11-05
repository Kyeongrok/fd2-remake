holy-heal.png / holy-heal.json
  pixelfx (https://github.com/patch-gg/pixelfx, MIT - LICENSE-pixelfx.txt) 의 holy/heal 을
  node tools/render.mjs holy/heal --sheet --no-actor --bg none --opts {"noBody":true} 로 뽑은 것.
  게임 지도 사람 위에 회색 사람 모양이 겹치지 않게 heal.js 의 몸 덧칠(recolor)을 noBody 일 때 건너뛰도록 한 줄 고쳤다(fb-86).
