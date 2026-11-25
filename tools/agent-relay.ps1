<#
  Claude Code 비대화형 교차 검토/질의 중계 스크립트 (review / ask 전용) — ProtoHarness

  실행:
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/agent-relay.ps1 -Target claude -Role review -Base dev -RunId r1 [-PromptFile <파일>] [-IncludeWorktree] [-Resume <세션ID>] [-Model <모델>] [-Effort medium]
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/agent-relay.ps1 -Target claude -Role ask -RunId q1 -Prompt "<질문>" [-Resume <세션ID>]

  안전 제약 (CLAUDE.md §0, §4, §5, §9-2 / docs/RULES/SUBAGENTS.md §7-6):
    - 읽기 전용 검토·질의(review / ask)만 지원한다 (-Role fix 및 자동 왕복 루프 없음).
    - -Target claude: --restricted --strict-mcp-config --tools "Read,Grep,Glob" --permission-mode dontAsk --permission-prompts none
      (2026-11-25 실측: 사용 가능 도구 Glob·Grep·Read 3개뿐, Bash·Edit·Write·MCP 도구 모두 제거됨).
    - -Target agy: 2026-11-25 실측에서 agy --mode plan 이 write_file 을 막지 못해 거부(exit 2)한다.
    - 동시 실행 잠금(Local\protoharness-agent-relay 뮤텍스), --dangerously-skip-permissions / bypassPermissions 금지.
    - 기본 diff 는 커밋 간(Base...HEAD)만 본다. 미커밋·미추적 포함은 -IncludeWorktree 를 명시할 때만.
    - git 출력은 Process 스트림에서 UTF-8 로 직접 읽어 한글 본문·비ASCII 경로 깨짐을 막는다.
    - 산출물은 항상 저장소 밖 새 폴더(%TEMP%\protoharness-agent-relay\<RunId>\)에만 쓴다.

  종료 코드:
    0  성공 (review 는 RELAY-VERDICT: PASS 또는 NEEDS_FIX 확인됨)
    2  인자·가드·잠금·대상 거부 (CLI 미호출)
    12 계약 위반 (JSON 파싱/필수 필드 누락, 또는 -Role review 응답 마지막 줄에 RELAY-VERDICT: 누락)
    13 CLI 실행 실패, git 오류, stdin 쓰기 실패, 또는 타임아웃

  주의: 이 파일은 UTF-8 with BOM 으로 저장한다 (Windows PowerShell 5.1 한글 깨짐 방지).
#>

param(
    [ValidateSet('agy', 'claude')]
    [string]$Target = 'claude',

    [ValidateSet('review', 'ask')]
    [string]$Role = 'review',

    [string]$PromptFile = '',
    [string]$Prompt = '',
    [string]$RunId = '',
    [string]$Base = 'dev',
    [switch]$IncludeWorktree,
    [string]$Resume = '',
    [string]$Model = '',

    [ValidateSet('low', 'medium', 'high', 'xhigh', 'max')]
    [string]$Effort = 'medium',

    [int]$TimeoutSec = 600
)

$ErrorActionPreference = 'Stop'
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$repoRoot  = Split-Path -Parent $PSScriptRoot

function Exit-Guard([string]$msg) {
    [Console]::Error.WriteLine("agent-relay 거부(exit 2): $msg")
    exit 2
}

function Write-Utf8File([string]$path, [string]$text) {
    $dir = Split-Path -Parent $path
    if (-not [string]::IsNullOrWhiteSpace($dir) -and -not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    [System.IO.File]::WriteAllText($path, $text, $utf8NoBom)
}

function Quote-Arg([string]$arg) {
    if ($null -eq $arg -or $arg -eq '') { return '""' }
    if ($arg -notmatch '[\s"]') { return $arg }
    $escaped = [regex]::Replace($arg, '(\\*)"', '$1$1\"')
    $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
    return '"' + $escaped + '"'
}

function Stop-ProcessTree([System.Diagnostics.Process]$proc) {
    $notes = New-Object System.Collections.Generic.List[string]
    try {
        $tkOut = & taskkill /PID $proc.Id /T /F 2>&1
        if ($LASTEXITCODE -ne 0) {
            $notes.Add("taskkill 종료 코드 ${LASTEXITCODE}: $($tkOut -join ' ')")
        }
    }
    catch {
        $notes.Add("taskkill 예외: $($_.Exception.Message)")
    }
    try {
        if (-not $proc.HasExited) {
            $proc.Kill()
        }
    }
    catch {
        $notes.Add("Process.Kill 예외: $($_.Exception.Message)")
    }
    return ($notes -join '; ')
}

# git 출력을 PowerShell 5.1 콘솔 코드페이지가 아니라 UTF-8 스트림으로 직접 읽는다.
function Invoke-GitUtf8([string[]]$gitArgs, [int]$timeoutMs = 30000) {
    $gitCmd = Get-Command git -ErrorAction Stop
    $fullArgs = @('-C', $repoRoot, '-c', 'core.quotepath=false', '-c', 'core.safecrlf=false') + $gitArgs

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName               = $gitCmd.Source
    $psi.Arguments              = (($fullArgs | ForEach-Object { Quote-Arg $_ }) -join ' ')
    $psi.WorkingDirectory       = $repoRoot
    $psi.UseShellExecute        = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError  = $true
    $psi.StandardOutputEncoding = $utf8NoBom
    $psi.StandardErrorEncoding  = $utf8NoBom

    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $psi
    $started = $false
    try {
        $started = $proc.Start()
        $outTask = $proc.StandardOutput.ReadToEndAsync()
        $errTask = $proc.StandardError.ReadToEndAsync()
        if (-not $proc.WaitForExit($timeoutMs)) {
            $killNote = Stop-ProcessTree $proc
            throw "git $($gitArgs -join ' ') 타임아웃 (${timeoutMs}ms, $killNote)"
        }
        $stdout = if ($outTask.Wait(5000)) { $outTask.Result } else { throw "git stdout 읽기 5초 초과" }
        $stderr = if ($errTask.Wait(5000)) { $errTask.Result } else { '(git stderr 읽기 5초 초과)' }
        if ($proc.ExitCode -ne 0) {
            throw "git $($gitArgs -join ' ') 실패 (exit $($proc.ExitCode)): $($stderr.Trim())"
        }
        return $stdout
    }
    finally {
        if ($started) {
            try {
                if (-not $proc.HasExited) { $null = Stop-ProcessTree $proc }
            }
            catch {
                [Console]::Error.WriteLine("agent-relay git 정리 예외: $($_.Exception.Message)")
            }
        }
        $proc.Dispose()
    }
}

function Split-NonEmptyLines([string]$text) {
    if ([string]::IsNullOrEmpty($text)) { return @() }
    return @($text -split "\r?\n" | Where-Object { $_ -ne '' })
}

function Build-DiffArtifact([string]$baseRef, [bool]$includeWorktree, [string]$diffPath) {
    $baseHash  = (Invoke-GitUtf8 @('rev-parse', '--verify', "$baseRef^{commit}")).Trim()
    $headHash  = (Invoke-GitUtf8 @('rev-parse', '--verify', 'HEAD^{commit}')).Trim()
    $branch    = (Invoke-GitUtf8 @('branch', '--show-current')).Trim()
    $mergeBase = (Invoke-GitUtf8 @('merge-base', $baseHash, $headHash)).Trim()

    $chunks = New-Object System.Collections.Generic.List[string]
    if ($includeWorktree) {
        $scopeDesc = "$baseRef..working-tree (merge-base $($mergeBase.Substring(0, 7)), HEAD $($headHash.Substring(0, 7)), -IncludeWorktree 지정)"
        $statText  = Invoke-GitUtf8 @('diff', '--stat', $mergeBase)
        $diffText  = Invoke-GitUtf8 @('diff', $mergeBase)
        $untracked = @(Split-NonEmptyLines (Invoke-GitUtf8 @('ls-files', '--others', '--exclude-standard')))

        $chunks.Add("# Diff scope: $scopeDesc")
        $chunks.Add("# Branch: $branch")
        $chunks.Add('')
        $chunks.Add('## git diff --stat')
        $chunks.Add($statText.TrimEnd("`r", "`n"))

        if ($untracked.Count -gt 0) {
            $chunks.Add('')
            $chunks.Add('## Untracked files')
            foreach ($u in $untracked) { $chunks.Add($u) }

            $maxBytes = 102400
            foreach ($u in $untracked) {
                $fullPath = Join-Path $repoRoot $u
                $chunks.Add('')
                $chunks.Add("### Untracked file content: $u")
                if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
                    $chunks.Add('(파일이 존재하지 않거나 디렉터리다)')
                    continue
                }
                $fi = Get-Item -LiteralPath $fullPath
                if ($fi.Length -gt $maxBytes) {
                    $chunks.Add("(크기 $($fi.Length) 바이트 > $maxBytes 바이트 상한 — 본문 생략, 필요 시 직접 읽어라)")
                    continue
                }
                $rawBytes = [System.IO.File]::ReadAllBytes($fullPath)
                if ([Array]::IndexOf($rawBytes, [byte]0) -ge 0) {
                    $chunks.Add('(바이너리 파일 — 본문 생략)')
                    continue
                }
                $chunks.Add([System.IO.File]::ReadAllText($fullPath, $utf8NoBom))
            }
        }

        $chunks.Add('')
        $chunks.Add('## git diff')
        $chunks.Add($diffText.TrimEnd("`r", "`n"))
    }
    else {
        $scopeDesc = "$baseRef...HEAD (merge-base $($mergeBase.Substring(0, 7)), HEAD $($headHash.Substring(0, 7)), 커밋 간 diff)"
        $statText  = Invoke-GitUtf8 @('diff', '--stat', "$baseRef...HEAD")
        $diffText  = Invoke-GitUtf8 @('diff', "$baseRef...HEAD")

        $chunks.Add("# Diff scope: $scopeDesc")
        $chunks.Add("# Branch: $branch")
        $chunks.Add('')
        $chunks.Add('## git diff --stat')
        $chunks.Add($statText.TrimEnd("`r", "`n"))
        $chunks.Add('')
        $chunks.Add('## git diff')
        $chunks.Add($diffText.TrimEnd("`r", "`n"))
    }

    Write-Utf8File $diffPath ($chunks -join "`n")

    return [pscustomobject]@{
        BaseRef   = $baseRef
        BaseHash  = $baseHash
        HeadHash  = $headHash
        MergeBase = $mergeBase
        Branch    = $branch
        ScopeDesc = $scopeDesc
        DiffPath  = $diffPath
    }
}

# 응답의 맨 마지막 비어 있지 않은 줄만 검사한다 (본문 인용 오판 방지).
function Parse-Verdict([string]$text) {
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }
    $lines = @($text -split "\r?\n" | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
    if ($lines.Count -eq 0) { return $null }
    if ($lines[$lines.Count - 1] -match '^RELAY-VERDICT:\s*(PASS|NEEDS_FIX)\s*$') {
        return $Matches[1]
    }
    return $null
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
    [void]$sb.AppendLine('- 모든 주장에는 파일경로:줄번호 근거를 붙여라 (CLAUDE.md §4-1).')
    if ($null -ne $diffInfo) {
        [void]$sb.AppendLine("- 검토 범위: $($diffInfo.ScopeDesc)")
        [void]$sb.AppendLine("- Diff 파일: $($diffInfo.DiffPath)")
        [void]$sb.AppendLine('  (먼저 위 Diff 파일을 Read 도구로 읽고 필요한 원본 파일만 확인하라.)')
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

function Invoke-ClaudeReadOnly(
    [string]$turnRole,
    [string]$promptText,
    [string]$runDir,
    [string]$resumeId,
    [string]$modelName,
    [string]$effortLevel,
    [int]$timeoutSeconds
) {
    $sentPath = Join-Path $runDir 'sent.md'
    $outPath  = Join-Path $runDir 'out.md'
    $errPath  = Join-Path $runDir 'stderr.log'
    $rawPath  = Join-Path $runDir 'raw.json'

    Write-Utf8File $sentPath $promptText

    $cmd = Get-Command claude -ErrorAction SilentlyContinue
    if (-not $cmd) {
        return [pscustomobject]@{
            ExitCode    = 13
            SessionId   = $null
            Verdict     = $null
            Response    = ''
            Usage       = $null
            CostUsd     = $null
            DurationSec = 0
            Error       = 'claude 실행 파일을 PATH 에서 찾지 못했다.'
            SentPath    = $sentPath
            OutPath     = $outPath
            ErrPath     = $errPath
            RawPath     = $rawPath
        }
    }

    $argsList = New-Object System.Collections.Generic.List[string]
    $argsList.Add('-p')
    $argsList.Add('--output-format')
    $argsList.Add('json')
    $argsList.Add('--restricted')
    $argsList.Add('--strict-mcp-config')
    $argsList.Add('--tools')
    $argsList.Add('Read,Grep,Glob')
    $argsList.Add('--permission-mode')
    $argsList.Add('dontAsk')
    $argsList.Add('--permission-prompts')
    $argsList.Add('none')
    $argsList.Add('--add-dir')
    $argsList.Add($runDir)
    $argsList.Add('--effort')
    $argsList.Add($effortLevel)

    if (-not [string]::IsNullOrWhiteSpace($modelName)) {
        $argsList.Add('--model')
        $argsList.Add($modelName)
    }
    if (-not [string]::IsNullOrWhiteSpace($resumeId)) {
        $argsList.Add('--resume')
        $argsList.Add($resumeId)
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
    $started = $false
    $stdout = ''
    $stderr = ''
    $code   = -1

    try {
        $started = $proc.Start()
        $stdoutTask = $proc.StandardOutput.ReadToEndAsync()
        $stderrTask = $proc.StandardError.ReadToEndAsync()

        # stdin 쓰기에도 10초 시간 제한을 둔다.
        $promptBytes = $utf8NoBom.GetBytes($promptText)
        $writeTask = $proc.StandardInput.BaseStream.WriteAsync($promptBytes, 0, $promptBytes.Length)
        if (-not $writeTask.Wait(10000)) {
            $killNote = Stop-ProcessTree $proc
            throw "claude stdin 쓰기 10초 타임아웃 ($killNote)"
        }
        $proc.StandardInput.BaseStream.Flush()
        $proc.StandardInput.Close()

        if (-not $proc.WaitForExit($timeoutSeconds * 1000)) {
            $killNote = Stop-ProcessTree $proc
            $sw.Stop()
            $partialErr = if ($stderrTask.Wait(3000)) { $stderrTask.Result } else { '(stderr 파이프 3초 내 닫히지 않음)' }
            $errDetail = "Timeout after ${timeoutSeconds}s"
            if (-not [string]::IsNullOrWhiteSpace($killNote)) { $errDetail += " | kill: $killNote" }
            Write-Utf8File $errPath ($errDetail + "`n" + $partialErr)
            return [pscustomobject]@{
                ExitCode    = 13
                SessionId   = $null
                Verdict     = $null
                Response    = ''
                Usage       = $null
                CostUsd     = $null
                DurationSec = [Math]::Round($sw.Elapsed.TotalSeconds, 2)
                Error       = "claude 실행이 ${timeoutSeconds}초 타임아웃을 초과했다 ($errDetail)."
                SentPath    = $sentPath
                OutPath     = $outPath
                ErrPath     = $errPath
                RawPath     = $rawPath
            }
        }

        $sw.Stop()
        $stdout = if ($stdoutTask.Wait(5000)) { $stdoutTask.Result } else { '' }
        $stderr = if ($stderrTask.Wait(5000)) { $stderrTask.Result } else { '(stderr 읽기 5초 초과)' }
        $code   = $proc.ExitCode
    }
    catch {
        $sw.Stop()
        $exMsg = "claude 프로세스 실행 예외: $($_.Exception.Message)"
        Write-Utf8File $errPath ($exMsg + "`n" + $stderr)
        return [pscustomobject]@{
            ExitCode    = 13
            SessionId   = $null
            Verdict     = $null
            Response    = ''
            Usage       = $null
            CostUsd     = $null
            DurationSec = [Math]::Round($sw.Elapsed.TotalSeconds, 2)
            Error       = $exMsg
            SentPath    = $sentPath
            OutPath     = $outPath
            ErrPath     = $errPath
            RawPath     = $rawPath
        }
    }
    finally {
        if ($started) {
            try {
                if (-not $proc.HasExited) {
                    $cleanupNote = Stop-ProcessTree $proc
                    if (-not [string]::IsNullOrWhiteSpace($cleanupNote)) {
                        [Console]::Error.WriteLine("agent-relay 정리 경고: $cleanupNote")
                    }
                }
            }
            catch {
                [Console]::Error.WriteLine("agent-relay 프로세스 정리 예외: $($_.Exception.Message)")
            }
        }
        $proc.Dispose()
    }

    Write-Utf8File $errPath $stderr
    Write-Utf8File $rawPath $stdout

    $parsed = $null
    $parseErrors = New-Object System.Collections.Generic.List[string]
    $jsonLine = $stdout.Trim()
    if (-not [string]::IsNullOrWhiteSpace($jsonLine)) {
        try {
            $parsed = $jsonLine | ConvertFrom-Json
        }
        catch {
            $parseErrors.Add("전체 JSON 파싱 실패: $($_.Exception.Message)")
            $candidate = @($jsonLine -split "\r?\n" | Where-Object { $_.Trim().StartsWith('{') -and $_.Trim().EndsWith('}') }) | Select-Object -Last 1
            if ($candidate) {
                try {
                    $parsed = $candidate | ConvertFrom-Json
                }
                catch {
                    $parseErrors.Add("마지막 줄 JSON 파싱 실패: $($_.Exception.Message)")
                    $parsed = $null
                }
            }
        }
    }

    if ($null -eq $parsed) {
        $parseNote = if ($parseErrors.Count -gt 0) { ' | ' + ($parseErrors -join '; ') } else { '' }
        if ($parseErrors.Count -gt 0) {
            Write-Utf8File $errPath ($stderr + "`n[agent-relay JSON parse]$parseNote")
        }
        Write-Utf8File $outPath $stdout
        return [pscustomobject]@{
            ExitCode    = if ($code -ne 0) { 13 } else { 12 }
            SessionId   = $null
            Verdict     = $null
            Response    = $stdout
            Usage       = $null
            CostUsd     = $null
            DurationSec = [Math]::Round($sw.Elapsed.TotalSeconds, 2)
            Error       = if ($code -ne 0) { "claude 종료 코드 $code (stderr 참고: $errPath)$parseNote" } else { "claude JSON 출력을 해석하지 못했다 (원문: $rawPath)$parseNote" }
            SentPath    = $sentPath
            OutPath     = $outPath
            ErrPath     = $errPath
            RawPath     = $rawPath
        }
    }

    # 필수 JSON 필드 검증 (session_id, result, is_error)
    $missingFields = New-Object System.Collections.Generic.List[string]
    if ($null -eq $parsed.PSObject.Properties['session_id'] -or [string]::IsNullOrWhiteSpace([string]$parsed.session_id)) { $missingFields.Add('session_id') }
    if ($null -eq $parsed.PSObject.Properties['result']     -or $null -eq $parsed.result) { $missingFields.Add('result') }
    if ($null -eq $parsed.PSObject.Properties['is_error']   -or ($parsed.is_error -isnot [bool])) { $missingFields.Add('is_error') }
    if ($missingFields.Count -gt 0) {
        Write-Utf8File $outPath $stdout
        return [pscustomobject]@{
            ExitCode    = 12
            SessionId   = $null
            Verdict     = $null
            Response    = $stdout
            Usage       = $null
            CostUsd     = $null
            DurationSec = [Math]::Round($sw.Elapsed.TotalSeconds, 2)
            Error       = "claude JSON 필수 필드 누락: $($missingFields -join ', ') (원문: $rawPath)"
            SentPath    = $sentPath
            OutPath     = $outPath
            ErrPath     = $errPath
            RawPath     = $rawPath
        }
    }

    $sessionId = [string]$parsed.session_id
    $response  = [string]$parsed.result
    $usage     = $parsed.usage
    $costUsd   = $parsed.total_cost_usd
    $isError   = [bool]$parsed.is_error
    $verdict   = Parse-Verdict $response

    Write-Utf8File $outPath $response

    $finalCode = 0
    $errMsg    = $null
    if ($code -ne 0 -or $isError) {
        $finalCode = 13
        $errMsg    = if ($isError) { "claude 오류 응답: $response" } else { "claude 종료 코드 $code (stderr 참고: $errPath)" }
    }
    elseif ($turnRole -eq 'review' -and [string]::IsNullOrWhiteSpace($verdict)) {
        $finalCode = 12
        $errMsg    = "review 응답의 마지막 줄에 'RELAY-VERDICT: PASS|NEEDS_FIX' 가 없다 ($outPath)."
    }

    return [pscustomobject]@{
        ExitCode    = $finalCode
        SessionId   = $sessionId
        Verdict     = $verdict
        Response    = $response
        Usage       = $usage
        CostUsd     = $costUsd
        DurationSec = [Math]::Round($sw.Elapsed.TotalSeconds, 2)
        Error       = $errMsg
        SentPath    = $sentPath
        OutPath     = $outPath
        ErrPath     = $errPath
        RawPath     = $rawPath
    }
}

# --- 1. 인자 및 안전 가드 검사 ---
if ($Target -eq 'agy') {
    Exit-Guard '2026-11-25 실측에서 agy --mode plan 이 write_file 을 막지 못했다. 읽기 전용 쓰기 차단이 검증되기 전까지 -Target agy 는 사용하지 않는다.'
}
if (-not [string]::IsNullOrWhiteSpace($Model) -and $Model -match '(?i)(gemini|gpt)') {
    Exit-Guard "claude 에는 Claude 모델만 지정할 수 있다. 지정값: $Model"
}
if ([string]::IsNullOrWhiteSpace($RunId)) {
    Exit-Guard '-RunId <이름> 은 필수다 (실행별 결과 폴더 충돌 방지).'
}
if ($RunId -notmatch '^[A-Za-z0-9._-]+$') {
    Exit-Guard "-RunId 는 영문·숫자·._- 만 허용한다. 입력값: $RunId"
}
if ($TimeoutSec -lt 30) {
    Exit-Guard "-TimeoutSec 는 30 이상이어야 한다. 입력값: $TimeoutSec"
}

$basePrompt = ''
if (-not [string]::IsNullOrWhiteSpace($PromptFile)) {
    if (-not (Test-Path -LiteralPath $PromptFile -PathType Leaf)) {
        Exit-Guard "PromptFile 이 없다: $PromptFile"
    }
    $PromptFile = (Resolve-Path -LiteralPath $PromptFile).Path
    $basePrompt = [System.IO.File]::ReadAllText($PromptFile, $utf8NoBom)
}
else {
    $basePrompt = $Prompt
}
if ([string]::IsNullOrWhiteSpace($basePrompt) -and $Role -ne 'review') {
    Exit-Guard '-Role ask 에는 PromptFile 또는 Prompt 가 필요하다.'
}

# 산출물은 항상 저장소 밖 새 폴더(%TEMP%\protoharness-agent-relay\<RunId>\)에만 쓴다.
$relayRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'protoharness-agent-relay'
$runDir    = Join-Path $relayRoot $RunId
if (Test-Path -LiteralPath $runDir) {
    Exit-Guard "이미 같은 RunId 의 산출물 폴더가 있다: $runDir (새 -RunId 를 쓴다)"
}

# --- 2. 동시 실행 잠금 ---
$mutex = New-Object System.Threading.Mutex($false, 'Local\protoharness-agent-relay')
$lockHeld = $false
try {
    try {
        $lockHeld = $mutex.WaitOne(0)
    }
    catch [System.Threading.AbandonedMutexException] {
        $lockHeld = $true
    }
    if (-not $lockHeld) {
        Exit-Guard '다른 agent-relay 실행이 이미 진행 중이다. 동시 실행은 금지한다 (SUBAGENTS.md §7-6).'
    }

    New-Item -ItemType Directory -Path $runDir -Force | Out-Null
    $resultJsonPath = Join-Path $runDir 'result.json'

    $diffInfo   = $null
    $turn       = $null
    $finalExit  = 13
    $finalError = $null
    $branchName = $null

    try {
        $branchName = (Invoke-GitUtf8 @('branch', '--show-current')).Trim()
        $finalPrompt = $basePrompt
        if ($Role -eq 'review') {
            if (-not [string]::IsNullOrWhiteSpace($Base)) {
                $diffInfo = Build-DiffArtifact $Base ([bool]$IncludeWorktree) (Join-Path $runDir 'diff.patch')
            }
            $finalPrompt = Build-ReviewPrompt $basePrompt $diffInfo
        }

        $turn = Invoke-ClaudeReadOnly $Role $finalPrompt $runDir $Resume $Model $Effort $TimeoutSec
        $finalExit  = $turn.ExitCode
        $finalError = $turn.Error
    }
    catch {
        $finalExit  = 13
        $finalError = "실행 중 예외: $($_.Exception.Message)"
    }

    $summary = [ordered]@{
        mode            = 'readonly'
        runId           = $RunId
        branch          = $branchName
        target          = $Target
        role            = $Role
        includeWorktree = [bool]$IncludeWorktree
        model           = if ([string]::IsNullOrWhiteSpace($Model)) { 'default' } else { $Model }
        exitCode        = $finalExit
        verdict         = if ($turn) { $turn.Verdict } else { $null }
        sessionId       = if ($turn) { $turn.SessionId } else { $null }
        durationSec     = if ($turn) { $turn.DurationSec } else { 0 }
        costUsd         = if ($turn) { $turn.CostUsd } else { $null }
        usage           = if ($turn) { $turn.Usage } else { $null }
        error           = $finalError
        files           = [ordered]@{
            dir    = $runDir
            sent   = if ($turn) { $turn.SentPath } else { $null }
            out    = if ($turn) { $turn.OutPath } else { $null }
            stderr = if ($turn) { $turn.ErrPath } else { $null }
            raw    = if ($turn) { $turn.RawPath } else { $null }
            diff   = if ($diffInfo) { $diffInfo.DiffPath } else { $null }
            result = $resultJsonPath
        }
    }
    Write-Utf8File $resultJsonPath ($summary | ConvertTo-Json -Depth 10)

    Write-Output "agent-relay 완료: target=$Target role=$Role exit=$finalExit verdict=$($summary.verdict) session=$($summary.sessionId) duration=$($summary.durationSec)s"
    Write-Output "결과 파일: $resultJsonPath"
    if ($finalError) { Write-Output "오류: $finalError" }
    exit $finalExit
}
finally {
    if ($lockHeld) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
