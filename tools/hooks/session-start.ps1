<#
  SessionStart 훅 — 세션 시작 때 사실 몇 줄을 컨텍스트에 넣는다. 읽기 전용.

    · 현재 브랜치가 main/dev 인가           (CLAUDE.md §9-2)
    · HANDOFF 를 고치지 않은 dev 병합이 있나 (CLAUDE.md §9-2 "병합 메시지와 보고에 HANDOFF: 줄")
      -Ref(기본 dev)의 first-parent 병합을 최신부터 최대 30개 본다. docs/HANDOFF.md 를 바꾼 병합에서
      멈추고, 그 사이에서 HANDOFF 를 바꾸지 않았고 메시지에 "HANDOFF: 안 고침" 줄도 없는 병합을 알린다.
      로컬 ref 만 본다(네트워크 없음). -Ref 가 없으면 이 검사를 건너뛴다.

  인덱스(index/symbols.tsv) 신선도 알림은 2026-11-13 에 뺐다. 인덱스가 라우터의 첫 조회에서
  내려가 선택 도구가 됐다 (DECISIONS 2026-11-13 "하네스 비용 기준선", LOOKUP.md §6-2).
  출력:
    -Format plain (기본, Claude Code): 평문 stdout (exit 0). 알릴 것이 없으면 아무것도 출력하지 않는다.
    -Format antigravity (Antigravity): JSON stdout (exit 0). 알릴 것이 있으면 injectSteps, 없으면 {}.

  주의: 이 파일은 UTF-8 with BOM 으로 저장한다 (Windows PowerShell 5.1).
#>

param(
    [string]$Ref = 'dev',
    [ValidateSet('plain', 'antigravity')]
    [string]$Format = 'plain'
)

$ErrorActionPreference = 'Stop'

try {
    # git 이 내는 한글 커밋 메시지를 콘솔 코드 페이지가 아니라 UTF-8 로 읽는다.
    [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)

    $root = $env:CLAUDE_PROJECT_DIR
    if ([string]::IsNullOrWhiteSpace($root)) { $root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot) }

    $lines = New-Object System.Collections.Generic.List[string]

    $branch = (& git -C $root branch --show-current 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "현재 브랜치를 읽지 못했다: $branch" }
    $branch = ([string]$branch).Trim()
    if ($branch -eq 'main' -or $branch -eq 'dev') {
        $lines.Add("[하네스] 현재 브랜치: $branch (보호 브랜치). 직접 커밋 금지 — 브랜치부터 만든다 (CLAUDE.md §9-2).")
    }

    $handoff = 'docs/HANDOFF.md'
    $maxMerges = 30
    & git -C $root rev-parse --verify --quiet "$Ref^{commit}" *> $null
    if ($LASTEXITCODE -eq 0) {
        $merges = @(& git -C $root log --first-parent --merges -n $maxMerges --format=%H $Ref 2>&1)
        if ($LASTEXITCODE -ne 0) { throw "$Ref 병합 목록을 읽지 못했다: $($merges -join ' ')" }

        $stale = New-Object System.Collections.Generic.List[string]
        $foundUpdate = $false
        foreach ($m in $merges) {
            $m = ([string]$m).Trim()
            if ($m.Length -eq 0) { continue }

            & git -C $root diff --quiet "$m^1" $m -- $handoff
            $code = $LASTEXITCODE
            if ($code -eq 1) { $foundUpdate = $true; break }   # 이 병합이 HANDOFF 를 바꿨다
            if ($code -ne 0) { throw "병합 $m 의 HANDOFF 변경을 읽지 못했다 (git diff 종료 코드 $code)" }

            $body = (& git -C $root show -s --format=%B $m) -join "`n"
            if ($LASTEXITCODE -ne 0) { throw "병합 $m 의 메시지를 읽지 못했다" }
            if ($body -match '(?m)^HANDOFF:\s*안 고침') { continue }

            $summary = ([string](& git -C $root show -s --date=format:%m-%d '--format=%h (%cd) %s' $m)).Trim()
            if ($LASTEXITCODE -ne 0) { throw "병합 $m 의 제목을 읽지 못했다" }
            if ($summary.Length -gt 100) { $summary = $summary.Substring(0, 100) + '…' }
            $stale.Add($summary)
        }

        if ($stale.Count -gt 0) {
            $more = ''
            if (-not $foundUpdate -and $merges.Count -ge $maxMerges) { $more = " (최근 $maxMerges 개까지만 봤다)" }
            $lines.Add("[하네스] $Ref 병합 $($stale.Count)건이 $handoff 를 고치지 않았다 — 메시지에 'HANDOFF: 안 고침' 줄도 없다$($more):")
            foreach ($s in ($stale | Select-Object -First 5)) { $lines.Add("  $s") }
            if ($stale.Count -gt 5) { $lines.Add("  … 외 $($stale.Count - 5)건") }
            $lines.Add("  작업 전에 HANDOFF 를 갱신하거나 사용자에게 알린다 (CLAUDE.md §9-2 'HANDOFF: 줄').")
        }
    }

    if ($Format -eq 'antigravity') {
        if ($lines.Count -gt 0) {
            $obj = @{
                injectSteps = @(
                    @{
                        systemMessage = @{
                            systemMessage = ($lines -join "`n")
                        }
                    }
                )
            }
            $json = $obj | ConvertTo-Json -Compress -Depth 6
        }
        else {
            $json = '{}'
        }
        $bytes = (New-Object System.Text.UTF8Encoding($false)).GetBytes($json)
        $out = [Console]::OpenStandardOutput()
        $out.Write($bytes, 0, $bytes.Length)
        $out.Flush()
    }
    elseif ($lines.Count -gt 0) {
        $bytes = (New-Object System.Text.UTF8Encoding($false)).GetBytes(($lines -join "`n") + "`n")
        $out = [Console]::OpenStandardOutput()
        $out.Write($bytes, 0, $bytes.Length)
        $out.Flush()
    }
    exit 0
}
catch {
    [Console]::Error.WriteLine("session-start.ps1 오류: $($_.Exception.Message)")
    exit 1
}
