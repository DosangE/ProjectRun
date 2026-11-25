<#
  SessionStart 훅 — 세션 시작 때 사실 몇 줄을 컨텍스트에 넣는다. 읽기 전용.

    · 현재 브랜치가 main/dev 인가           (CLAUDE.md §9-2)
    · HANDOFF 를 고치지 않은 dev 병합이 있나 (CLAUDE.md §9-2 "병합 메시지와 보고에 HANDOFF: 줄")
      -Ref(기본 dev)의 first-parent 병합을 최신부터 최대 30개 본다. docs/HANDOFF.md 를 바꾼 병합에서
      멈추고, 그 사이에서 HANDOFF 를 바꾸지 않았고 메시지에 "HANDOFF: 안 고침" 줄도 없는 병합을 알린다.
      로컬 ref 만 본다(네트워크 없음). -Ref 가 없으면 보지 못했다고 알린다.
    · 독립 검토가 없는 dev 병합이 있나      (CLAUDE.md §9-2 "병합 메시지와 보고에 구현:·검토: 줄")
      -ReviewCutoff(도입 기준 커밋) 뒤의 first-parent 병합을 최대 30개 본다. 구현:·검토: 줄이 각각 하나가
      아니거나, 모르는 이름이 있거나, 구현 쪽에 없는 검토자가 없으면 알린다. "검토: 없음 — <이유>" 와, 뒤의
      병합에 "사후 검토: <해시> 구현 <이름> 검토 <이름>" 이 있는 병합은 알리지 않는다.
      기록 형식과 독립성만 본다. §7-6 표의 필수 검토자와 사용자 승인은 판정하지 않는다.

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
    [string]$Format = 'plain',
    # 구현:·검토: 줄을 도입하기 직전의 dev 병합 (docs/antigravity-hooks). 그 전 병합에는 이 줄이 없다.
    [string]$ReviewCutoff = 'bc986d6cdcee011ecc93fee1426767201cd0f435'
)

$ErrorActionPreference = 'Stop'

$reviewNames = @('claude', 'codex', 'antigravity', '사용자')

# "claude, codex (설명)" 같은 값의 이름들. 모르는 이름이 하나라도 있거나 비면 $null 이다 —
# 오타를 버리고 세면 구현한 쪽이 독립 검토자로 통과한다.
function ConvertTo-ReviewNames([string]$value) {
    $names = @(($value -split '\s+[—-]\s+|\(')[0] -split '[,+·]' | ForEach-Object { $_.Trim().ToLowerInvariant() })
    foreach ($n in $names) { if ($reviewNames -notcontains $n) { return $null } }
    return ,$names
}

# 병합 메시지에서 "구현: claude, codex" 같은 줄의 이름들. 줄이 정확히 하나가 아니면 $null 이다.
function Get-ReviewNames([string]$body, [string]$label) {
    $hits = [regex]::Matches($body, '(?m)^(?:-\s*)?' + $label + ':[ \t]*(?<v>[^\r\n]*)')
    if ($hits.Count -ne 1) { return $null }
    return ,(ConvertTo-ReviewNames $hits[0].Groups['v'].Value)
}

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

        # 기준 커밋이 -Ref 에 없으면 검사할 수 없다. 조용히 건너뛰지 않고 알린다.
        $hasCutoff = $false
        & git -C $root rev-parse --verify --quiet "$ReviewCutoff^{commit}" *> $null
        if ($LASTEXITCODE -eq 0) {
            & git -C $root merge-base --is-ancestor $ReviewCutoff $Ref
            $hasCutoff = ($LASTEXITCODE -eq 0)
        }
        if (-not $hasCutoff) {
            $lines.Add("[하네스] 교차 검토 기준 커밋 $($ReviewCutoff.Substring(0, [Math]::Min(7, $ReviewCutoff.Length))) 이 $Ref 에 없어 구현:·검토: 줄을 보지 못했다 — git fetch 와 $Ref 갱신을 확인한다.")
        }
        else {
            $checked = @(& git -C $root log --first-parent --merges -n $maxMerges --format=%H "$ReviewCutoff..$Ref" 2>&1)
            if ($LASTEXITCODE -ne 0) { throw "$Ref 병합 목록을 읽지 못했다: $($checked -join ' ')" }

            $unreviewed = New-Object System.Collections.Generic.List[string]
            $later = New-Object System.Collections.Generic.List[object]   # 뒤 병합들의 "사후 검토:" 줄
            foreach ($m in $checked) {
                $m = ([string]$m).Trim()
                if ($m.Length -eq 0) { continue }

                $body = (& git -C $root show -s --format=%B $m) -join "`n"
                if ($LASTEXITCODE -ne 0) { throw "병합 $m 의 메시지를 읽지 못했다" }

                $impl   = Get-ReviewNames $body '구현'
                $review = Get-ReviewNames $body '검토'
                $waived = $body -match '(?m)^(?:-\s*)?검토:[ \t]*없음[ \t]+[—-][ \t]+\S'
                $ok = ($null -ne $impl) -and ($waived -or (($null -ne $review) -and @($review | Where-Object { $impl -notcontains $_ }).Count -gt 0))

                # 사후 검토는 구현자를 함께 적는다. 원래 메시지의 구현자까지 합쳐 독립인지 본다.
                foreach ($r in $later) {
                    if ($ok -or -not $m.StartsWith($r.Hash)) { continue }
                    $all = @($r.Impl) + @($impl | Where-Object { $_ })
                    $ok = @($r.Review | Where-Object { $all -notcontains $_ }).Count -gt 0
                }

                if (-not $ok) {
                    $summary = ([string](& git -C $root show -s --date=format:%m-%d '--format=%h (%cd) %s' $m)).Trim()
                    if ($LASTEXITCODE -ne 0) { throw "병합 $m 의 제목을 읽지 못했다" }
                    if ($summary.Length -gt 100) { $summary = $summary.Substring(0, 100) + '…' }
                    $unreviewed.Add($summary)
                }

                foreach ($hit in [regex]::Matches($body, '(?m)^(?:-\s*)?사후 검토:[ \t]*(?<h>[0-9a-f]{7,40})[ \t]+구현[ \t]+(?<i>[^\r\n]+?)[ \t]+검토[ \t]+(?<r>[^\r\n]+)')) {
                    $li = ConvertTo-ReviewNames $hit.Groups['i'].Value
                    $lr = ConvertTo-ReviewNames $hit.Groups['r'].Value
                    if ($null -ne $li -and $null -ne $lr) { $later.Add([pscustomobject]@{ Hash = $hit.Groups['h'].Value; Impl = $li; Review = $lr }) }
                }
            }

            if ($unreviewed.Count -gt 0) {
                $lines.Add("[하네스] $Ref 병합 $($unreviewed.Count)건에 독립 검토 기록이 없다 (구현:·검토: 줄 없음, 또는 구현한 쪽만 검토):")
                foreach ($s in ($unreviewed | Select-Object -First 5)) { $lines.Add("  $s") }
                if ($unreviewed.Count -gt 5) { $lines.Add("  … 외 $($unreviewed.Count - 5)건") }
                $lines.Add("  사용자에게 알린다. 검토했으면 다음 병합 메시지에 '사후 검토: <해시> 구현 <이름> 검토 <이름>' (CLAUDE.md §9-2).")
            }
        }
    }
    else {
        $lines.Add("[하네스] 로컬에 $Ref 가 없어 HANDOFF·구현:·검토: 줄을 보지 못했다.")
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
