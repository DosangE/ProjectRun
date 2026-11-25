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
    - 시작 시점에 base, merge-base, HEAD 전체 해시와 브랜치를 한 번만 확정해 stat·patch 와 result.json 에 사용한다.
    - 기본 diff 는 커밋 간(merge-base..HEAD)만 본다. 미커밋·미추적 포함은 -IncludeWorktree 를 명시할 때만.
      review 이고 -IncludeWorktree 가 아닐 때 merge-base..HEAD 변경 파일 중 하나라도 미커밋 변경(스테이지 포함)이 있으면 CLI 미호출로 거부(exit 2)한다.
    - git 출력은 Process 스트림에서 UTF-8 로 직접 읽어 한글 본문·비ASCII 경로 깨짐을 막는다.
    - 산출물 폴더(%TEMP%\protoharness-agent-relay\<RunId>\)는 잠금을 잡은 뒤에 존재를 검사하고 새로 만든다. 이미 있으면 기존 내용은 손대지 않고 exit 2.
    - 한계: taskkill /PID <부모PID> /T /F 는 부모가 먼저 종료된 뒤 살아남은 손자 프로세스를 잡지 못할 수 있다.

  종료 코드:
    0  성공 (review 는 정상 PASS 와 정상 NEEDS_FIX 모두 exit 0; 판정은 verdict 로 구분)
    2  인자·가드·잠금·대상 거부·기존 RunId 폴더 존재·미커밋 변경 충돌 (CLI 미호출)
    12 계약 위반 (JSON 파싱/필수 필드 누락, -Role review 응답 마지막 줄에 RELAY-VERDICT: 누락, 또는 실행 중 HEAD/브랜치 변경; verdict 는 null)
    13 CLI 실행 실패, git 오류, stdin 쓰기 실패, stdout/stderr 읽기 시간 초과, 프로세스 트리 종료/확인 실패, 또는 타임아웃 (verdict 는 null; 잠금 해제 전 원문을 stderr 와 result.json error 에 기록)
    ※ exitCode 가 0 이 아니면 result.json 과 콘솔 요약의 verdict 는 항상 null(빈 값)이다.

  주의: 이 파일은 UTF-8 with BOM, CRLF 로 저장한다 (Windows PowerShell 5.1 한글 깨짐 방지).
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
    $ok = $true
    $details = New-Object System.Collections.Generic.List[string]

    $pidVal = $null
    try {
        $pidVal = $proc.Id
    }
    catch {
        $ok = $false
        $details.Add("프로세스 PID 조회 실패: $($_.Exception.Message)")
    }

    if ($null -ne $pidVal) {
        $tkPsi = New-Object System.Diagnostics.ProcessStartInfo
        $tkPsi.FileName               = 'taskkill.exe'
        $tkPsi.Arguments              = "/PID $pidVal /T /F"
        $tkPsi.UseShellExecute        = $false
        $tkPsi.RedirectStandardOutput = $true
        $tkPsi.RedirectStandardError  = $true

        $tkProc = New-Object System.Diagnostics.Process
        $tkProc.StartInfo = $tkPsi
        try {
            $null = $tkProc.Start()
            $tkOutTask = $tkProc.StandardOutput.ReadToEndAsync()
            $tkErrTask = $tkProc.StandardError.ReadToEndAsync()
            if (-not $tkProc.WaitForExit(10000)) {
                $ok = $false
                $details.Add("taskkill /PID $pidVal /T /F 10초 타임아웃")
                try {
                    if (-not $tkProc.HasExited) { $tkProc.Kill() }
                }
                catch {
                    $details.Add("taskkill 자체 종료 실패: $($_.Exception.Message)")
                }
            }
            else {
                $tkOut = ''
                $tkErr = ''
                if ($tkOutTask.Wait(3000)) {
                    $tkOut = $tkOutTask.Result.Trim()
                }
                else {
                    $ok = $false
                    $details.Add("taskkill stdout 읽기 3초 초과 (PID=$pidVal)")
                }
                if ($tkErrTask.Wait(3000)) {
                    $tkErr = $tkErrTask.Result.Trim()
                }
                else {
                    $ok = $false
                    $details.Add("taskkill stderr 읽기 3초 초과 (PID=$pidVal)")
                }
                if ($tkProc.ExitCode -ne 0) {
                    $ok = $false
                    $msgParts = @($tkOut, $tkErr) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
                    $details.Add("taskkill 종료 코드 $($tkProc.ExitCode): $($msgParts -join ' | ')")
                }
            }
        }
        catch {
            $ok = $false
            $details.Add("taskkill 실행 예외: $($_.Exception.Message)")
        }
        finally {
            $tkProc.Dispose()
        }
    }

    try {
        if (-not $proc.HasExited) {
            try {
                $proc.Kill()
            }
            catch {
                $ok = $false
                $details.Add("Process.Kill 예외: $($_.Exception.Message)")
            }
        }
        if (-not $proc.WaitForExit(5000)) {
            $ok = $false
            $details.Add("프로세스 종료 확인 5초 타임아웃 (PID=$pidVal)")
        }
        elseif (-not $proc.HasExited) {
            $ok = $false
            $details.Add("프로세스 종료 확인 실패: HasExited=false (PID=$pidVal)")
        }
    }
    catch {
        $ok = $false
        $details.Add("프로세스 종료 확인 예외: $($_.Exception.Message)")
    }

    return [pscustomobject]@{
        Succeeded = $ok
        Detail    = ($details -join '; ')
    }
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
    $started      = $false
    $resultStdout = $null
    $tryError     = $null
    $cleanupError = $null
    try {
        $started = $proc.Start()
        $outTask = $proc.StandardOutput.ReadToEndAsync()
        $errTask = $proc.StandardError.ReadToEndAsync()
        if (-not $proc.WaitForExit($timeoutMs)) {
            $killRes = Stop-ProcessTree $proc
            $killMsg = if (-not [string]::IsNullOrWhiteSpace($killRes.Detail)) { " | $($killRes.Detail)" } else { '' }
            throw "git $($gitArgs -join ' ') 타임아웃 (${timeoutMs}ms)$killMsg"
        }

        $ioErrors = New-Object System.Collections.Generic.List[string]
        $stdout = $null
        $stderr = $null
        if ($outTask.Wait(5000)) {
            $stdout = $outTask.Result
        }
        else {
            $ioErrors.Add('git stdout 읽기 5초 시간 초과')
        }
        if ($errTask.Wait(5000)) {
            $stderr = $errTask.Result
        }
        else {
            $ioErrors.Add('git stderr 읽기 5초 시간 초과')
        }
        if ($ioErrors.Count -gt 0) {
            $killRes = Stop-ProcessTree $proc
            if (-not $killRes.Succeeded -and -not [string]::IsNullOrWhiteSpace($killRes.Detail)) {
                $ioErrors.Add("프로세스 트리 종료/확인 실패: $($killRes.Detail)")
            }
            throw "git $($gitArgs -join ' ') $($ioErrors -join ' | ')"
        }
        if ($proc.ExitCode -ne 0) {
            throw "git $($gitArgs -join ' ') 실패 (exit $($proc.ExitCode)): $($stderr.Trim())"
        }
        $resultStdout = $stdout
    }
    catch {
        $tryError = $_.Exception.Message
    }
    finally {
        if ($started) {
            try {
                if (-not $proc.HasExited) {
                    $killRes = Stop-ProcessTree $proc
                    if (-not $killRes.Succeeded) {
                        $cleanupError = "git 프로세스 트리 종료/확인 실패: $($killRes.Detail)"
                    }
                }
            }
            catch {
                $cleanupError = "git 프로세스 정리 예외: $($_.Exception.Message)"
            }
        }
        $proc.Dispose()
    }

    if ($null -ne $tryError -or $null -ne $cleanupError) {
        $combined = @($tryError, $cleanupError) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        throw ($combined -join ' | ')
    }
    return $resultStdout
}

function Split-NonEmptyLines([string]$text) {
    if ([string]::IsNullOrEmpty($text)) { return @() }
    return @($text -split "\r?\n" | Where-Object { $_ -ne '' })
}

function Get-ChangedFilesBetween([string]$fromCommit, [string]$toCommit) {
    $raw = Invoke-GitUtf8 @('diff', '--name-only', '-z', '--no-renames', $fromCommit, $toCommit)
    $files = New-Object System.Collections.Generic.List[string]
    if ([string]::IsNullOrEmpty($raw)) { return $files }
    foreach ($t in $raw.Split([char]0)) {
        if (-not [string]::IsNullOrWhiteSpace($t)) {
            $files.Add($t.Replace('\', '/'))
        }
    }
    return $files
}

function Get-DirtyPaths() {
    $raw = Invoke-GitUtf8 @('status', '--porcelain=v1', '-z', '-uall')
    $dirtySet = @{}
    if ([string]::IsNullOrEmpty($raw)) { return $dirtySet }

    $tokens = $raw.Split([char]0)
    $i = 0
    while ($i -lt $tokens.Length) {
        $entry = $tokens[$i]
        $i++
        if ($entry.Length -lt 4) { continue }
        $xy = $entry.Substring(0, 2)
        $p1 = $entry.Substring(3).Replace('\', '/')
        if (-not [string]::IsNullOrWhiteSpace($p1)) {
            $dirtySet[$p1] = $true
        }
        # porcelain -z 에서 R(이름 변경) 또는 C(복사) 는 다음 NUL 토큰에 원본 경로가 온다.
        if ($xy[0] -eq 'R' -or $xy[0] -eq 'C' -or $xy[1] -eq 'R' -or $xy[1] -eq 'C') {
            if ($i -lt $tokens.Length) {
                $p2 = $tokens[$i].Replace('\', '/')
                $i++
                if (-not [string]::IsNullOrWhiteSpace($p2)) {
                    $dirtySet[$p2] = $true
                }
            }
        }
    }
    return $dirtySet
}

function New-RunDirStrict([string]$rootDir, [string]$targetDir) {
    if (-not (Test-Path -LiteralPath $rootDir)) {
        New-Item -ItemType Directory -Path $rootDir -ErrorAction Stop | Out-Null
    }
    if (Test-Path -LiteralPath $targetDir) {
        Exit-Guard "이미 같은 RunId 의 산출물 폴더가 있다: $targetDir (새 -RunId 를 쓴다)"
    }
    try {
        New-Item -ItemType Directory -Path $targetDir -ErrorAction Stop | Out-Null
    }
    catch {
        if (Test-Path -LiteralPath $targetDir) {
            Exit-Guard "이미 같은 RunId 의 산출물 폴더가 있다: $targetDir (새 -RunId 를 쓴다)"
        }
        throw
    }
}

function Build-DiffArtifact(
    [string]$baseRef,
    [string]$baseHash,
    [string]$mergeBase,
    [string]$headHash,
    [string]$branch,
    [bool]$includeWorktree,
    [string]$diffPath
) {
    $chunks = New-Object System.Collections.Generic.List[string]
    if ($includeWorktree) {
        $scopeDesc = "$baseRef..working-tree (base $($baseHash.Substring(0, 7)), merge-base $($mergeBase.Substring(0, 7)), HEAD $($headHash.Substring(0, 7)), -IncludeWorktree 지정)"
        $statText  = Invoke-GitUtf8 @('diff', '--stat', $mergeBase)
        $diffText  = Invoke-GitUtf8 @('diff', $mergeBase)
        $untracked = @(Split-NonEmptyLines (Invoke-GitUtf8 @('ls-files', '--others', '--exclude-standard')))

        $chunks.Add("# Diff scope: $scopeDesc")
        $chunks.Add("# Branch: $branch")
        $chunks.Add("# Base: $baseRef ($baseHash)")
        $chunks.Add("# Merge-Base: $mergeBase")
        $chunks.Add("# HEAD: $headHash")
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
        $scopeDesc = "$baseRef...HEAD (base $($baseHash.Substring(0, 7)), merge-base $($mergeBase.Substring(0, 7)), HEAD $($headHash.Substring(0, 7)), 커밋 간 diff)"
        $statText  = Invoke-GitUtf8 @('diff', '--stat', $mergeBase, $headHash)
        $diffText  = Invoke-GitUtf8 @('diff', $mergeBase, $headHash)

        $chunks.Add("# Diff scope: $scopeDesc")
        $chunks.Add("# Branch: $branch")
        $chunks.Add("# Base: $baseRef ($baseHash)")
        $chunks.Add("# Merge-Base: $mergeBase")
        $chunks.Add("# HEAD: $headHash")
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
    $started      = $false
    $stdout       = ''
    $stderr       = ''
    $code         = -1
    $execExit     = 0
    $execError    = $null
    $cleanupError = $null

    try {
        $started = $proc.Start()
        $stdoutTask = $proc.StandardOutput.ReadToEndAsync()
        $stderrTask = $proc.StandardError.ReadToEndAsync()

        # stdin 쓰기에도 10초 시간 제한을 둔다.
        $promptBytes = $utf8NoBom.GetBytes($promptText)
        $writeTask = $proc.StandardInput.BaseStream.WriteAsync($promptBytes, 0, $promptBytes.Length)
        if (-not $writeTask.Wait(10000)) {
            $killRes = Stop-ProcessTree $proc
            $killMsg = if (-not [string]::IsNullOrWhiteSpace($killRes.Detail)) { " | $($killRes.Detail)" } else { '' }
            throw "claude stdin 쓰기 10초 타임아웃$killMsg"
        }
        $proc.StandardInput.BaseStream.Flush()
        $proc.StandardInput.Close()

        if (-not $proc.WaitForExit($timeoutSeconds * 1000)) {
            $killRes = Stop-ProcessTree $proc
            $sw.Stop()
            $partialErr = if ($stderrTask.Wait(3000)) { $stderrTask.Result } else { '(stderr 파이프 3초 내 닫히지 않음)' }
            $errDetail = "Timeout after ${timeoutSeconds}s"
            if (-not $killRes.Succeeded -or -not [string]::IsNullOrWhiteSpace($killRes.Detail)) {
                $errDetail += " | kill: $($killRes.Detail)"
            }
            $stderr    = $errDetail + "`n" + $partialErr
            $execExit  = 13
            $execError = "claude 실행이 ${timeoutSeconds}초 타임아웃을 초과했다 ($errDetail)."
        }
        else {
            $sw.Stop()
            $code = $proc.ExitCode
            $ioErrors = New-Object System.Collections.Generic.List[string]
            if ($stdoutTask.Wait(5000)) {
                $stdout = $stdoutTask.Result
            }
            else {
                $ioErrors.Add('claude stdout 읽기 5초 시간 초과')
            }
            if ($stderrTask.Wait(5000)) {
                $stderr = $stderrTask.Result
            }
            else {
                $ioErrors.Add('claude stderr 읽기 5초 시간 초과')
            }
            if ($ioErrors.Count -gt 0) {
                $killRes = Stop-ProcessTree $proc
                if (-not $killRes.Succeeded -and -not [string]::IsNullOrWhiteSpace($killRes.Detail)) {
                    $ioErrors.Add("프로세스 트리 종료/확인 실패: $($killRes.Detail)")
                }
                $execExit  = 13
                $execError = $ioErrors -join ' | '
            }
        }
    }
    catch {
        $sw.Stop()
        $execExit  = 13
        $execError = "claude 프로세스 실행 예외: $($_.Exception.Message)"
    }
    finally {
        if ($started) {
            try {
                if (-not $proc.HasExited) {
                    $killRes = Stop-ProcessTree $proc
                    if (-not $killRes.Succeeded) {
                        $cleanupError = "claude 프로세스 트리 종료/확인 실패: $($killRes.Detail)"
                    }
                }
            }
            catch {
                $cleanupError = "claude 프로세스 정리 예외: $($_.Exception.Message)"
            }
        }
        $proc.Dispose()
    }

    if (-not [string]::IsNullOrWhiteSpace($cleanupError)) {
        $execExit  = 13
        $execError = if ([string]::IsNullOrWhiteSpace($execError)) { $cleanupError } else { "$execError | $cleanupError" }
    }

    if ($execExit -eq 13) {
        $errLogBody = if ([string]::IsNullOrWhiteSpace($stderr)) { $execError } else { $execError + "`n" + $stderr }
        Write-Utf8File $errPath $errLogBody
        if (-not [string]::IsNullOrEmpty($stdout)) {
            Write-Utf8File $rawPath $stdout
        }
        return [pscustomobject]@{
            ExitCode    = 13
            SessionId   = $null
            Verdict     = $null
            Response    = ''
            Usage       = $null
            CostUsd     = $null
            DurationSec = [Math]::Round($sw.Elapsed.TotalSeconds, 2)
            Error       = $execError
            SentPath    = $sentPath
            OutPath     = $outPath
            ErrPath     = $errPath
            RawPath     = $rawPath
        }
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
        Verdict     = if ($finalCode -eq 0) { $verdict } else { $null }
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

    # 산출물 폴더는 잠금을 잡은 뒤에 존재를 검사하고 새로 만든다 (이미 있으면 기존 내용을 손대지 않고 exit 2).
    $relayRoot      = Join-Path ([System.IO.Path]::GetTempPath()) 'protoharness-agent-relay'
    $runDir         = Join-Path $relayRoot $RunId
    if (Test-Path -LiteralPath $runDir) {
        Exit-Guard "이미 같은 RunId 의 산출물 폴더가 있다: $runDir (새 -RunId 를 쓴다)"
    }
    $resultJsonPath = Join-Path $runDir 'result.json'

    $startBranch    = $null
    $startBaseRef   = if ([string]::IsNullOrWhiteSpace($Base)) { $null } else { $Base }
    $startBaseHash  = $null
    $startMergeBase = $null
    $startHeadHash  = $null
    $diffInfo       = $null
    $turn           = $null
    $finalExit      = 13
    $finalError     = $null
    $runDirCreated  = $false

    try {
        # 시작할 때 base, merge-base, HEAD 전체 해시와 브랜치를 한 번만 확정한다.
        $startBranch   = (Invoke-GitUtf8 @('branch', '--show-current')).Trim()
        $startHeadHash = (Invoke-GitUtf8 @('rev-parse', '--verify', 'HEAD^{commit}')).Trim()
        if ($null -ne $startBaseRef) {
            $startBaseHash  = (Invoke-GitUtf8 @('rev-parse', '--verify', "${startBaseRef}^{commit}")).Trim()
            $startMergeBase = (Invoke-GitUtf8 @('merge-base', $startBaseHash, $startHeadHash)).Trim()
        }

        if ($Role -eq 'review' -and -not $IncludeWorktree -and $null -ne $startMergeBase) {
            $changedFiles = @(Get-ChangedFilesBetween $startMergeBase $startHeadHash)
            if ($changedFiles.Count -gt 0) {
                $dirtyPaths = Get-DirtyPaths
                $overlap = New-Object System.Collections.Generic.List[string]
                foreach ($f in $changedFiles) {
                    if ($dirtyPaths.ContainsKey($f)) {
                        $overlap.Add($f)
                    }
                }
                if ($overlap.Count -gt 0) {
                    Exit-Guard "review(커밋 간 diff) 대상 파일에 미커밋 변경(스테이지 포함)이 있다: $($overlap -join ', ') (커밋·정리하거나 -IncludeWorktree 를 지정하라)"
                }
            }
        }

        New-RunDirStrict $relayRoot $runDir
        $runDirCreated = $true

        $finalPrompt = $basePrompt
        if ($Role -eq 'review') {
            if ($null -ne $startMergeBase) {
                $diffInfo = Build-DiffArtifact $startBaseRef $startBaseHash $startMergeBase $startHeadHash $startBranch ([bool]$IncludeWorktree) (Join-Path $runDir 'diff.patch')
            }
            $finalPrompt = Build-ReviewPrompt $basePrompt $diffInfo
        }

        $turn = Invoke-ClaudeReadOnly $Role $finalPrompt $runDir $Resume $Model $Effort $TimeoutSec
        $finalExit  = $turn.ExitCode
        $finalError = $turn.Error

        # claude 호출 종료 뒤 HEAD 또는 브랜치가 시작과 다르면 exit 12, verdict null.
        $endHeadHash = (Invoke-GitUtf8 @('rev-parse', '--verify', 'HEAD^{commit}')).Trim()
        $endBranch   = (Invoke-GitUtf8 @('branch', '--show-current')).Trim()
        if ($endHeadHash -ne $startHeadHash -or $endBranch -ne $startBranch) {
            $driftMsg = "claude 실행 중 git 상태가 바뀌었다: 시작(branch='$startBranch', HEAD=$startHeadHash) -> 종료(branch='$endBranch', HEAD=$endHeadHash)"
            if ($finalExit -eq 0 -or $finalExit -eq 12) {
                $finalExit = 12
            }
            $finalError = if ([string]::IsNullOrWhiteSpace($finalError)) { $driftMsg } else { "$finalError | $driftMsg" }
        }
    }
    catch {
        $finalExit  = 13
        $finalError = "실행 중 예외: $($_.Exception.Message)"
    }

    if (-not $runDirCreated) {
        New-RunDirStrict $relayRoot $runDir
        $runDirCreated = $true
    }

    $finalVerdict = if ($finalExit -eq 0 -and $turn) { $turn.Verdict } else { $null }

    $summary = [ordered]@{
        mode            = 'readonly'
        runId           = $RunId
        branch          = $startBranch
        base            = $startBaseRef
        baseHash        = $startBaseHash
        mergeBase       = $startMergeBase
        headHash        = $startHeadHash
        target          = $Target
        role            = $Role
        includeWorktree = [bool]$IncludeWorktree
        model           = if ([string]::IsNullOrWhiteSpace($Model)) { 'default' } else { $Model }
        exitCode        = $finalExit
        verdict         = $finalVerdict
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

    if ($finalExit -eq 13 -and -not [string]::IsNullOrWhiteSpace($finalError)) {
        [Console]::Error.WriteLine("agent-relay 오류(exit 13): $finalError")
    }

    Write-Output "agent-relay 완료: target=$Target role=$Role exit=$finalExit verdict=$($summary.verdict) session=$($summary.sessionId) duration=$($summary.durationSec)s"
    Write-Output "결과 파일: $resultJsonPath"
    if ($finalError) { Write-Output "오류: $finalError" }
    exit $finalExit
}
finally {
    if ($lockHeld) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
