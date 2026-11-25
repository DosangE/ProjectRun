<#
  Claude Code ↔ Antigravity(agy) 비대화형 중계 스크립트 — ProtoHarness

  단발/이어가기 호출:
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/agent-relay.ps1 -Target claude -Role review -Base dev -RunId r1 [-PromptFile <파일>] [-Resume <세션ID>] [-Model <모델>] [-Effort medium]
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/agent-relay.ps1 -Target agy -Role fix -Approve -PromptFile <파일> -RunId f1

  제한된 교차 검토 왕복 루프 (최대 MaxRounds 회, 자동 커밋·병합 금지):
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/agent-relay.ps1 -RoundTrip -Impl agy -Reviewer claude -Base dev -RunId rt1 -MaxRounds 2 -Approve

  안전 제약 (CLAUDE.md §0, §3-4, §5 / docs/RULES/SUBAGENTS.md §7-6):
    - --dangerously-skip-permissions / bypassPermissions 는 절대 쓰지 않는다.
    - -Role review/ask 는 읽기 전용(claude: --permission-mode plan + Edit/Write/NotebookEdit 차단, agy: --mode plan).
    - -Role fix 와 -RoundTrip 은 -Approve 스위치 필수. 자동 커밋·병합·푸시는 계약으로 금지한다.
    - -Target agy 에 Claude 계열 모델 금지(Gemini 모델만). -RoundTrip 에서 -Impl 과 -Reviewer 는 달라야 한다.

  출력 파일 (-PromptFile 옆, 없으면 %TEMP%\protoharness-agent-relay\):
    <prefix>.<RunId>.sent.md / .out.md / .stderr.log / .diff.patch(리뷰 시) / .result.json

  종료 코드:
    0  성공 (RoundTrip 은 PASS)
    2  인자·가드 거부 (CLI 미호출)
    10 RoundTrip 이 MaxRounds 뒤에도 NEEDS_FIX 잔존
    12 JSON 파싱 실패 또는 RoundTrip 판정 줄(RELAY-VERDICT:) 누락
    13 CLI 실행 실패 또는 타임아웃

  주의: 이 파일은 UTF-8 with BOM 으로 저장한다 (Windows PowerShell 5.1 한글 깨짐 방지).
#>

param(
    [ValidateSet('', 'agy', 'claude')]
    [string]$Target = '',

    [ValidateSet('review', 'fix', 'ask')]
    [string]$Role = 'review',

    [switch]$RoundTrip,

    [ValidateSet('', 'agy', 'claude')]
    [string]$Impl = '',

    [ValidateSet('', 'agy', 'claude')]
    [string]$Reviewer = '',

    [string]$PromptFile = '',
    [string]$Prompt = '',
    [string]$RunId = '',
    [string]$Base = 'dev',
    [string]$Resume = '',
    [string]$Model = '',
    [string]$ImplModel = '',
    [string]$ReviewerModel = '',

    [ValidateSet('low', 'medium', 'high', 'xhigh', 'max')]
    [string]$Effort = 'medium',

    [int]$MaxRounds = 2,
    [int]$TimeoutSec = 600,
    [switch]$Approve
)

$ErrorActionPreference = 'Stop'
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$repoRoot  = Split-Path -Parent $PSScriptRoot

function Exit-Guard([string]$msg) {
    [Console]::Error.WriteLine("agent-relay 거부(exit 2): $msg")
    exit 2
}

function Assert-ModelFamily([string]$agent, [string]$modelName) {
    if ([string]::IsNullOrWhiteSpace($modelName)) { return }
    if ($agent -eq 'agy' -and $modelName -match '(?i)(claude|opus|sonnet|haiku)') {
        Exit-Guard "agy 에는 Gemini 모델만 지정할 수 있다 (SUBAGENTS.md §7-6). 지정값: $modelName"
    }
    if ($agent -eq 'claude' -and $modelName -match '(?i)gemini') {
        Exit-Guard "claude 에는 Claude 모델만 지정할 수 있다. 지정값: $modelName"
    }
}

function Invoke-GitRead([string[]]$gitArgs) {
    $out = & git -C $repoRoot -c core.safecrlf=false @gitArgs
    if ($LASTEXITCODE -ne 0) {
        throw "git $($gitArgs -join ' ') 실패 (exit $LASTEXITCODE)"
    }
    return @($out)
}

function Write-Utf8File([string]$path, [string]$text) {
    $dir = Split-Path -Parent $path
    if (-not [string]::IsNullOrWhiteSpace($dir) -and -not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    [System.IO.File]::WriteAllText($path, $text, $utf8NoBom)
}

function Quote-Arg([string]$arg) {
    if ($null -eq $arg) { return '""' }
    if ($arg -eq '') { return '""' }
    if ($arg -notmatch '[\s"]') { return $arg }
    $escaped = [regex]::Replace($arg, '(\\*)"', '$1$1\"')
    $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
    return '"' + $escaped + '"'
}

function Build-DiffArtifact([string]$baseRef, [string]$diffPath) {
    $baseHash  = [string](@(Invoke-GitRead @('rev-parse', '--verify', "$baseRef^{commit}"))[0])
    $headHash  = [string](@(Invoke-GitRead @('rev-parse', '--verify', 'HEAD^{commit}'))[0])
    $branch    = [string](@(Invoke-GitRead @('branch', '--show-current'))[0])
    $mergeBase = [string](@(Invoke-GitRead @('merge-base', $baseHash.Trim(), $headHash.Trim()))[0])
    $baseHash  = $baseHash.Trim()
    $headHash  = $headHash.Trim()
    $branch    = $branch.Trim()
    $mergeBase = $mergeBase.Trim()

    $dirtyFiles = @(Invoke-GitRead @('diff', '--name-only', 'HEAD') | Where-Object { $_ -ne '' })
    $untracked  = @(Invoke-GitRead @('ls-files', '--others', '--exclude-standard') | Where-Object { $_ -ne '' })
    $isDirty    = ($dirtyFiles.Count -gt 0 -or $untracked.Count -gt 0)

    if ($isDirty) {
        $scopeDesc = "$baseRef..working-tree (merge-base $($mergeBase.Substring(0, 7)), HEAD $($headHash.Substring(0, 7)), 커밋 전 변경 포함)"
        $statLines = @(Invoke-GitRead @('diff', '--stat', $mergeBase))
        $diffLines = @(Invoke-GitRead @('diff', $mergeBase))
    }
    else {
        $scopeDesc = "$baseRef...HEAD (merge-base $($mergeBase.Substring(0, 7)), HEAD $($headHash.Substring(0, 7)), 작업 트리 = HEAD)"
        $statLines = @(Invoke-GitRead @('diff', '--stat', "$baseRef...HEAD"))
        $diffLines = @(Invoke-GitRead @('diff', "$baseRef...HEAD"))
    }

    $header = @(
        "# Diff scope: $scopeDesc"
        "# Branch: $branch"
        ""
        "## git diff --stat"
    ) + $statLines
    if ($untracked.Count -gt 0) {
        $header += @("", "## Untracked files") + $untracked
    }
    $header += @("", "## git diff") + $diffLines

    Write-Utf8File $diffPath ($header -join "`n")

    return [pscustomobject]@{
        BaseRef   = $baseRef
        BaseHash  = $baseHash
        HeadHash  = $headHash
        MergeBase = $mergeBase
        Branch    = $branch
        IsDirty   = $isDirty
        ScopeDesc = $scopeDesc
        DiffPath  = $diffPath
        StatText  = ($statLines -join "`n")
    }
}

function Parse-Verdict([string]$text) {
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }
    $lines = @($text -split "\r?\n" | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
    for ($i = $lines.Count - 1; $i -ge 0; $i--) {
        if ($lines[$i] -match '^RELAY-VERDICT:\s*(PASS|NEEDS_FIX)\s*$') {
            return $Matches[1]
        }
    }
    return $null
}

function Invoke-CliTurn(
    [string]$agent,
    [string]$turnRole,
    [string]$promptText,
    [string]$prefixPath,
    [string]$resumeId,
    [string]$modelName,
    [string]$effortLevel,
    [int]$timeoutSeconds
) {
    $sentPath   = "$prefixPath.sent.md"
    $outPath    = "$prefixPath.out.md"
    $errPath    = "$prefixPath.stderr.log"
    $rawPath    = "$prefixPath.raw.json"

    Write-Utf8File $sentPath $promptText

    $cmd = Get-Command $agent -ErrorAction SilentlyContinue
    if (-not $cmd) {
        return [pscustomobject]@{
            ExitCode    = 13
            Agent       = $agent
            Role        = $turnRole
            SessionId   = $null
            Verdict     = $null
            Response    = ''
            Usage       = $null
            CostUsd     = $null
            DurationSec = 0
            Error       = "$agent 실행 파일을 PATH 에서 찾지 못했다."
            SentPath    = $sentPath
            OutPath     = $outPath
            ErrPath     = $errPath
        }
    }

    $argsList = New-Object System.Collections.Generic.List[string]
    $useStdin = $false

    if ($agent -eq 'claude') {
        $argsList.Add('-p')
        $argsList.Add('--output-format')
        $argsList.Add('json')
        $argsList.Add('--permission-prompts')
        $argsList.Add('none')
        $argsList.Add('--effort')
        $argsList.Add($effortLevel)

        if ($turnRole -eq 'fix') {
            $argsList.Add('--permission-mode')
            $argsList.Add('auto')
        }
        else {
            $argsList.Add('--permission-mode')
            $argsList.Add('plan')
            $argsList.Add('--disallowedTools')
            $argsList.Add('Edit,Write,NotebookEdit')
        }

        if (-not [string]::IsNullOrWhiteSpace($modelName)) {
            $argsList.Add('--model')
            $argsList.Add($modelName)
        }
        if (-not [string]::IsNullOrWhiteSpace($resumeId)) {
            $argsList.Add('--resume')
            $argsList.Add($resumeId)
        }
        $useStdin = $true
    }
    else {
        # agy
        $cliPrompt = if ($promptText.Length -le 6000) {
            $promptText
        }
        else {
            "다음 지시서 파일의 내용을 끝까지 읽고 그대로 따르라: $sentPath"
        }

        $argsList.Add('-p')
        $argsList.Add($cliPrompt)
        $argsList.Add('--output-format')
        $argsList.Add('json')
        $argsList.Add('--effort')
        $argsList.Add($effortLevel)
        $argsList.Add('--print-timeout')
        $argsList.Add("${timeoutSeconds}s")

        if ($turnRole -eq 'fix') {
            $argsList.Add('--mode')
            $argsList.Add('auto')
        }
        else {
            $argsList.Add('--mode')
            $argsList.Add('plan')
        }

        if (-not [string]::IsNullOrWhiteSpace($modelName)) {
            $argsList.Add('--model')
            $argsList.Add($modelName)
        }
        if (-not [string]::IsNullOrWhiteSpace($resumeId)) {
            $argsList.Add('--conversation')
            $argsList.Add($resumeId)
        }
    }

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName               = $cmd.Source
    $psi.Arguments              = (($argsList | ForEach-Object { Quote-Arg $_ }) -join ' ')
    $psi.WorkingDirectory       = $repoRoot
    $psi.UseShellExecute        = $false
    $psi.RedirectStandardInput  = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError  = $true
    $psi.StandardOutputEncoding = $utf8NoBom
    $psi.StandardErrorEncoding  = $utf8NoBom

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $psi
    $null = $proc.Start()

    if ($useStdin) {
        $inWriter = New-Object System.IO.StreamWriter($proc.StandardInput.BaseStream, $utf8NoBom)
        $inWriter.Write($promptText)
        $inWriter.Flush()
        $inWriter.Close()
    }
    else {
        $proc.StandardInput.Close()
    }

    $stdoutTask = $proc.StandardOutput.ReadToEndAsync()
    $stderrTask = $proc.StandardError.ReadToEndAsync()

    $exited = $proc.WaitForExit($timeoutSeconds * 1000)
    if (-not $exited) {
        try { $proc.Kill() } catch {}
        $sw.Stop()
        Write-Utf8File $errPath ("Timeout after ${timeoutSeconds}s`n" + $stderrTask.Result)
        return [pscustomobject]@{
            ExitCode    = 13
            Agent       = $agent
            Role        = $turnRole
            SessionId   = $null
            Verdict     = $null
            Response    = ''
            Usage       = $null
            CostUsd     = $null
            DurationSec = [Math]::Round($sw.Elapsed.TotalSeconds, 2)
            Error       = "$agent 실행이 ${timeoutSeconds}초 타임아웃을 초과했다."
            SentPath    = $sentPath
            OutPath     = $outPath
            ErrPath     = $errPath
        }
    }

    $sw.Stop()
    $stdout = $stdoutTask.Result
    $stderr = $stderrTask.Result
    $code   = $proc.ExitCode

    Write-Utf8File $errPath $stderr
    Write-Utf8File $rawPath $stdout

    $parsed = $null
    $jsonLine = $stdout.Trim()
    if (-not [string]::IsNullOrWhiteSpace($jsonLine)) {
        try {
            $parsed = $jsonLine | ConvertFrom-Json
        }
        catch {
            # 앞뒤 비JSON 로그 줄이 섞인 경우 마지막 '{...}' 줄을 시도한다.
            $candidate = @($jsonLine -split "\r?\n" | Where-Object { $_.Trim().StartsWith('{') -and $_.Trim().EndsWith('}') }) | Select-Object -Last 1
            if ($candidate) {
                try { $parsed = $candidate | ConvertFrom-Json } catch { $parsed = $null }
            }
        }
    }

    if ($null -eq $parsed) {
        Write-Utf8File $outPath $stdout
        return [pscustomobject]@{
            ExitCode    = if ($code -ne 0) { 13 } else { 12 }
            Agent       = $agent
            Role        = $turnRole
            SessionId   = $null
            Verdict     = $null
            Response    = $stdout
            Usage       = $null
            CostUsd     = $null
            DurationSec = [Math]::Round($sw.Elapsed.TotalSeconds, 2)
            Error       = if ($code -ne 0) { "$agent 종료 코드 $code (stderr 참고: $errPath)" } else { "$agent JSON 출력을 해석하지 못했다 (원문: $rawPath)" }
            SentPath    = $sentPath
            OutPath     = $outPath
            ErrPath     = $errPath
        }
    }

    $sessionId = $null
    $response  = ''
    $usage     = $null
    $costUsd   = $null
    $isError   = $false
    $errMsg    = $null

    if ($agent -eq 'claude') {
        $sessionId = [string]$parsed.session_id
        $response  = [string]$parsed.result
        $usage     = $parsed.usage
        $costUsd   = $parsed.total_cost_usd
        $isError   = [bool]$parsed.is_error
        if ($isError) { $errMsg = "claude 오류 응답: $response" }
    }
    else {
        $sessionId = [string]$parsed.conversation_id
        $response  = [string]$parsed.response
        $usage     = $parsed.usage
        $statusStr = [string]$parsed.status
        if ($statusStr -ne 'SUCCESS') {
            $isError = $true
            $errMsg  = "agy 상태가 SUCCESS 가 아니다: $statusStr"
        }
    }

    Write-Utf8File $outPath $response

    $finalCode = 0
    if ($code -ne 0 -or $isError) {
        $finalCode = 13
        if ([string]::IsNullOrWhiteSpace($errMsg)) {
            $errMsg = "$agent 종료 코드 $code (stderr 참고: $errPath)"
        }
    }

    return [pscustomobject]@{
        ExitCode    = $finalCode
        Agent       = $agent
        Role        = $turnRole
        SessionId   = $sessionId
        Verdict     = Parse-Verdict $response
        Response    = $response
        Usage       = $usage
        CostUsd     = $costUsd
        DurationSec = [Math]::Round($sw.Elapsed.TotalSeconds, 2)
        Error       = $errMsg
        SentPath    = $sentPath
        OutPath     = $outPath
        ErrPath     = $errPath
    }
}

# --- 1. 인자 및 안전 가드 검사 ---
if ([string]::IsNullOrWhiteSpace($RunId)) {
    Exit-Guard '-RunId <이름> 은 필수다 (실행별 결과 파일 충돌 방지).'
}
if ($RunId -notmatch '^[A-Za-z0-9._-]+$') {
    Exit-Guard "-RunId 는 영문·숫자·._- 만 허용한다. 입력값: $RunId"
}
if ($MaxRounds -lt 1 -or $MaxRounds -gt 5) {
    Exit-Guard "-MaxRounds 는 1~5 사이여야 한다. 입력값: $MaxRounds"
}
if ($TimeoutSec -lt 30) {
    Exit-Guard "-TimeoutSec 는 30 이상이어야 한다. 입력값: $TimeoutSec"
}

$basePrompt = ''
if (-not [string]::IsNullOrWhiteSpace($PromptFile)) {
    if (-not (Test-Path -LiteralPath $PromptFile)) {
        Exit-Guard "PromptFile 이 없다: $PromptFile"
    }
    $PromptFile = (Resolve-Path -LiteralPath $PromptFile).Path
    $basePrompt = [System.IO.File]::ReadAllText($PromptFile, $utf8NoBom)
    $dir  = Split-Path -Parent $PromptFile
    $stem = [System.IO.Path]::GetFileNameWithoutExtension($PromptFile)
    $runPrefix = Join-Path $dir "$stem.$RunId"
}
else {
    $basePrompt = $Prompt
    $relayDir = Join-Path ([System.IO.Path]::GetTempPath()) 'protoharness-agent-relay'
    $runPrefix = Join-Path $relayDir "relay.$RunId"
}

$resultJsonPath = "$runPrefix.result.json"
if (Test-Path -LiteralPath $resultJsonPath) {
    Exit-Guard "이미 같은 RunId 의 결과 파일이 있다: $resultJsonPath (새 -RunId 를 쓴다)"
}

if ($RoundTrip) {
    if ([string]::IsNullOrWhiteSpace($Impl) -or [string]::IsNullOrWhiteSpace($Reviewer)) {
        Exit-Guard '-RoundTrip 에는 -Impl (agy|claude) 과 -Reviewer (agy|claude) 가 모두 필요하다.'
    }
    if ($Impl -eq $Reviewer) {
        Exit-Guard "교차 검토 위반: -Impl($Impl) 과 -Reviewer($Reviewer) 는 달라야 한다 (SUBAGENTS.md §7-6)."
    }
    if (-not $Approve) {
        Exit-Guard '-RoundTrip 은 구현자 수정(-Role fix)을 포함하므로 사용자 승인(-Approve)이 필요하다 (CLAUDE.md §3-4).'
    }
    Assert-ModelFamily $Impl $ImplModel
    Assert-ModelFamily $Reviewer $ReviewerModel
}
else {
    if ([string]::IsNullOrWhiteSpace($Target)) {
        Exit-Guard '단발 호출에는 -Target (agy|claude) 이 필요하다.'
    }
    if ([string]::IsNullOrWhiteSpace($basePrompt) -and $Role -ne 'review') {
        Exit-Guard 'PromptFile 또는 Prompt 가 비어 있다.'
    }
    if ($Role -eq 'fix' -and -not $Approve) {
        Exit-Guard '-Role fix 는 사용자 승인(-Approve)이 필요하다 (CLAUDE.md §3-4).'
    }
    Assert-ModelFamily $Target $Model
}

function Build-ReviewPrompt([string]$userPrompt, $diffInfo) {
    $core = if ([string]::IsNullOrWhiteSpace($userPrompt)) {
        'CLAUDE.md 및 .claude/agents/unity-reviewer.md 기준으로 변경 사항을 교차 검토하라. 실제 결함, 규칙 위반(§0/§1/§3/§4/§5/§9), 문서·검증 기록 불일치만 경로:줄 근거와 함께 지적하라.'
    }
    else {
        $userPrompt
    }

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('[agent-relay: 읽기 전용 교차 검토]')
    [void]$sb.AppendLine('- 파일을 수정·생성·삭제하거나 git 상태를 바꾸지 마라. 지적만 한다 (SUBAGENTS.md §7-6).')
    [void]$sb.AppendLine('- 모든 주장에는 파일경로:줄번호 근거를 붙여라 (CLAUDE.md §4-1).')
    if ($null -ne $diffInfo) {
        [void]$sb.AppendLine("- 검토 범위: $($diffInfo.ScopeDesc)")
        [void]$sb.AppendLine("- Diff 파일: $($diffInfo.DiffPath)")
        [void]$sb.AppendLine('  (먼저 위 Diff 파일을 읽고 필요한 원본 파일만 확인하라.)')
    }
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('## 검토 지시')
    [void]$sb.AppendLine($core)
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('## 필수 응답 형식')
    [void]$sb.AppendLine('- 지적 사항을 간결히 적고, 응답의 맨 마지막 비어 있지 않은 줄은 반드시 아래 둘 중 하나만 적어라:')
    [void]$sb.AppendLine('  RELAY-VERDICT: PASS')
    [void]$sb.AppendLine('  RELAY-VERDICT: NEEDS_FIX')
    return $sb.ToString()
}

function Build-FixPrompt([string]$userPrompt, [string]$reviewText, [int]$roundNum) {
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine("[agent-relay: 검토 지적 수정 — Round $roundNum]")
    [void]$sb.AppendLine('- 승인 범위 안에서 아래 검토 지적만 고쳐라. 범위를 넓히지 마라 (CLAUDE.md §3-3).')
    [void]$sb.AppendLine('- 금지: git commit, git merge, git push, git reset --hard, git clean 실행 금지 (커밋·병합은 사용자 승인 후 메인에서).')
    [void]$sb.AppendLine('- 금지: 하네스 파일(CLAUDE.md, AGENTS.md, .claude/, .codex/, .agents/, tools/hooks/) 수정 금지.')
    if (-not [string]::IsNullOrWhiteSpace($userPrompt)) {
        [void]$sb.AppendLine('')
        [void]$sb.AppendLine('## 원래 작업 맥락')
        [void]$sb.AppendLine($userPrompt)
    }
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('## 검토자 지적 원문')
    [void]$sb.AppendLine($reviewText)
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('## 보고')
    [void]$sb.AppendLine('수정한 파일:줄과 검증 결과를 간결히 보고하라.')
    return $sb.ToString()
}

# --- 2. 실행 ---
if (-not $RoundTrip) {
    $diffInfo = $null
    $finalPrompt = $basePrompt
    if ($Role -eq 'review') {
        if (-not [string]::IsNullOrWhiteSpace($Base)) {
            $diffInfo = Build-DiffArtifact $Base "$runPrefix.diff.patch"
        }
        $finalPrompt = Build-ReviewPrompt $basePrompt $diffInfo
    }
    elseif ($Role -eq 'fix') {
        $finalPrompt = "[agent-relay: 승인된 수정 실행]`n- 금지: git commit, git merge, git push, git reset --hard, git clean 실행 금지.`n`n$basePrompt"
    }

    $turn = Invoke-CliTurn $Target $Role $finalPrompt $runPrefix $Resume $Model $Effort $TimeoutSec

    $summary = [ordered]@{
        mode        = 'single'
        runId       = $RunId
        target      = $Target
        role        = $Role
        exitCode    = $turn.ExitCode
        verdict     = $turn.Verdict
        sessionId   = $turn.SessionId
        durationSec = $turn.DurationSec
        costUsd     = $turn.CostUsd
        usage       = $turn.Usage
        error       = $turn.Error
        files       = [ordered]@{
            sent   = $turn.SentPath
            out    = $turn.OutPath
            stderr = $turn.ErrPath
            diff   = if ($diffInfo) { $diffInfo.DiffPath } else { $null }
            result = $resultJsonPath
        }
    }
    Write-Utf8File $resultJsonPath ($summary | ConvertTo-Json -Depth 10)

    Write-Output "agent-relay 완료: target=$Target role=$Role exit=$($turn.ExitCode) verdict=$($turn.Verdict) session=$($turn.SessionId) duration=$($turn.DurationSec)s"
    Write-Output "결과 파일: $resultJsonPath"
    if ($turn.Error) { Write-Output "오류: $($turn.Error)" }
    exit $turn.ExitCode
}

# --- 3. RoundTrip 교차 검토 루프 ---
$rounds = New-Object System.Collections.Generic.List[object]
$reviewerSession = $null
$implSession     = $null
$finalExit       = 10
$finalVerdict    = $null
$finalError      = $null

for ($r = 1; $r -le $MaxRounds; $r++) {
    $revPrefix = "$runPrefix.r$r.review"
    $diffInfo  = Build-DiffArtifact $Base "$revPrefix.diff.patch"
    $revPrompt = if ($r -eq 1) {
        Build-ReviewPrompt $basePrompt $diffInfo
    }
    else {
        Build-ReviewPrompt "이전 지적에 대한 수정이 반영됐다. 새 Diff($($diffInfo.DiffPath))를 다시 검토하고 미해결 지적이 있는지 판정하라." $diffInfo
    }

    $revTurn = Invoke-CliTurn $Reviewer 'review' $revPrompt $revPrefix $reviewerSession $ReviewerModel $Effort $TimeoutSec
    if (-not [string]::IsNullOrWhiteSpace($revTurn.SessionId)) { $reviewerSession = $revTurn.SessionId }

    $roundRec = [ordered]@{
        round  = $r
        review = [ordered]@{
            agent       = $Reviewer
            exitCode    = $revTurn.ExitCode
            verdict     = $revTurn.Verdict
            sessionId   = $revTurn.SessionId
            durationSec = $revTurn.DurationSec
            costUsd     = $revTurn.CostUsd
            usage       = $revTurn.Usage
            error       = $revTurn.Error
            outPath     = $revTurn.OutPath
            diffPath    = $diffInfo.DiffPath
        }
        fix = $null
    }
    $rounds.Add($roundRec)

    if ($revTurn.ExitCode -ne 0) {
        $finalExit  = $revTurn.ExitCode
        $finalError = "Round $r 검토자($Reviewer) 실패: $($revTurn.Error)"
        break
    }
    if ([string]::IsNullOrWhiteSpace($revTurn.Verdict)) {
        $finalExit  = 12
        $finalError = "Round $r 검토자($Reviewer) 응답에 'RELAY-VERDICT: PASS|NEEDS_FIX' 줄이 없다 ($($revTurn.OutPath))."
        break
    }

    $finalVerdict = $revTurn.Verdict
    if ($revTurn.Verdict -eq 'PASS') {
        $finalExit = 0
        break
    }

    if ($r -ge $MaxRounds) {
        $finalExit  = 10
        $finalError = "MaxRounds($MaxRounds) 후에도 NEEDS_FIX 가 남아 중단했다 ($($revTurn.OutPath))."
        break
    }

    $fixPrefix = "$runPrefix.r$r.fix"
    $fixPrompt = Build-FixPrompt $basePrompt $revTurn.Response $r
    $fixTurn   = Invoke-CliTurn $Impl 'fix' $fixPrompt $fixPrefix $implSession $ImplModel $Effort $TimeoutSec
    if (-not [string]::IsNullOrWhiteSpace($fixTurn.SessionId)) { $implSession = $fixTurn.SessionId }

    $roundRec.fix = [ordered]@{
        agent       = $Impl
        exitCode    = $fixTurn.ExitCode
        sessionId   = $fixTurn.SessionId
        durationSec = $fixTurn.DurationSec
        costUsd     = $fixTurn.CostUsd
        usage       = $fixTurn.Usage
        error       = $fixTurn.Error
        outPath     = $fixTurn.OutPath
    }

    if ($fixTurn.ExitCode -ne 0) {
        $finalExit  = $fixTurn.ExitCode
        $finalError = "Round $r 구현자($Impl) 수정 실패: $($fixTurn.Error)"
        break
    }
}

$rtSummary = [ordered]@{
    mode            = 'roundtrip'
    runId           = $RunId
    impl            = $Impl
    reviewer        = $Reviewer
    base            = $Base
    maxRounds       = $MaxRounds
    exitCode        = $finalExit
    verdict         = $finalVerdict
    reviewerSession = $reviewerSession
    implSession     = $implSession
    error           = $finalError
    rounds          = $rounds
    resultPath      = $resultJsonPath
}
Write-Utf8File $resultJsonPath ($rtSummary | ConvertTo-Json -Depth 12)

Write-Output "agent-relay RoundTrip 종료: impl=$Impl reviewer=$Reviewer rounds=$($rounds.Count)/$MaxRounds exit=$finalExit verdict=$finalVerdict"
Write-Output "결과 파일: $resultJsonPath"
if ($finalError) { Write-Output "사유: $finalError" }
exit $finalExit
