<#
.SYNOPSIS
  용의기사2 리메이크가 보낸 버그 리포트를 본다 / 상태를 바꾼다 (tools/bug-worker 의 /v1/bugs — 관리자 열쇠가 있어야 한다).

.EXAMPLE
  ./bugs.ps1                              # 새(new) 리포트 목록
  ./bugs.ps1 -Status all -Since 40        # 40 번 뒤의 모든 리포트
  ./bugs.ps1 -Id 42                       # 42 번 하나를 자세히(오류 기록 끝까지)
  ./bugs.ps1 -Mark 42 -Status fixed -Note "v1.0.43 에서 고침"
  ./bugs.ps1 -Url http://127.0.0.1:8833 -KeyFile .\test-key.txt   # 로컬 wrangler dev 에 대고

  관리자 열쇠는 -KeyFile(기본 ~\.fd2-bug-admin-key) 의 첫 줄 — worker 비밀 BUG_ADMIN_KEY 와 같은 값.
  받는 곳 주소는 -Url, 없으면 https://fd2-bugs.oceanfog1.workers.dev.
#>
param(
  [int]$Mark = 0,
  [string]$Status = '',
  [string]$Note,
  [int]$Since = 0,
  [int]$Id = 0,
  [int]$Limit = 100,
  [string]$Url = '',
  [string]$KeyFile = (Join-Path $HOME '.fd2-bug-admin-key'),
  [switch]$Json
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

if (-not (Test-Path $KeyFile)) { throw "관리자 열쇠 파일이 없습니다: $KeyFile (worker 비밀 BUG_ADMIN_KEY 와 같은 값을 한 줄로)" }
$key = (Get-Content -Raw $KeyFile).Trim()

if (-not $Url) { $Url = 'https://fd2-bugs.oceanfog1.workers.dev' }
$Url = $Url.TrimEnd('/')
$headers = @{ Authorization = "Bearer $key" }

function Call([string]$method, [string]$path, $body = $null) {
  $req = @{ Method = $method; Uri = "$Url$path"; Headers = $headers }
  if ($null -ne $body) {
    $req.Body = [System.Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json -Compress))
    $req.ContentType = 'application/json; charset=utf-8'
  }
  Invoke-RestMethod @req
}

# ── 상태 바꾸기 ──────────────────────────────────────────────────────────────
if ($Mark -gt 0) {
  if (-not $Status) { throw "-Status 를 주세요 (new · seen · fixed · wontfix · dup …)" }
  $body = @{ status = $Status }
  if ($PSBoundParameters.ContainsKey('Note')) { $body.note = $Note }
  $r = Call 'POST' "/v1/bugs/$Mark/status" $body
  Write-Output "#$($r.id) → $($r.status)"
  return
}

# ── 목록 ───────────────────────────────────────────────────────────────────
if (-not $Status) { $Status = if ($Id -gt 0) { 'all' } else { 'new' } }
$from = if ($Id -gt 0) { $Id - 1 } else { $Since }
$take = if ($Id -gt 0) { 1 } else { $Limit }
$r = Call 'GET' ("/v1/bugs?since=$from&status=$([uri]::EscapeDataString($Status))&limit=$take")
$bugs = @($r.bugs)
if ($Id -gt 0) { $bugs = @($bugs | Where-Object { $_.id -eq $Id }) }

if ($Json) { $bugs | ConvertTo-Json -Depth 8; return }
if ($bugs.Count -eq 0) { Write-Output "($Status 리포트 없음)"; return }

foreach ($b in $bugs) {
  $c = $b.context
  $when = ([datetime]$b.created_at).ToLocalTime().ToString('yyyy-MM-dd HH:mm')
  Write-Output ("━━ #{0}  {1}  v{2}  [{3}]  설치 {4}" -f $b.id, $when, $b.version, $b.status, $b.install.Substring(0, 8))
  Write-Output ("   자리: {0}{1}{2}" -f $c.screen, $(if ($c.chapter) { " · $($c.chapter)장" } else { '' }), $(if ($c.battle) { " · $($c.turn)턴 · $($c.state)" } else { '' }))
  Write-Output ("   환경: {0} · {1} · {2} · 판 {3}{4} · 자료 {5}" -f $c.game, $c.os, $c.platform, $c.frame, $(if ($c.mobileView) { ' 모바일 화면' } else { '' }), $c.data)
  if ($c.crashLog) {
    $lines = @($c.crashLog -split "`n")
    if ($Id -gt 0) { Write-Output "   오류 기록:"; $lines | ForEach-Object { Write-Output "     $_" } }
    else { Write-Output ("   오류 기록({0}줄): {1}" -f $lines.Count, (($lines | Where-Object { $_.Trim() } | Select-Object -First 2) -join ' / ')) }
  }
  if ($b.note) { Write-Output ("   메모: " + $b.note) }
  Write-Output ""
  ($b.text -split "`n") | ForEach-Object { Write-Output "   $_" }
  Write-Output ""
}
