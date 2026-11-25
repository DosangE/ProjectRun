<#
  Claude 가 Antigravity(agy) 에 작업 하나를 맡기고 결과를 받는다 — ProtoHarness

  실행:
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/agy-run.ps1 -Role ask -PromptFile <파일> -RunId a1
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/agy-run.ps1 -Role fix -PromptFile <파일> -RunId f1 -Allow "tools/x.ps1,docs/Y.md"
    이어 가기(같은 대화의 두 번째 왕복): ... -RunId f2 -Resume f1 (f1 의 폴더에서 대화 ID 를 읽는다)

  지시서에는 "승인: <언제, 무엇이>" 와 "브랜치: <현재 브랜치>" 줄이 있어야 한다 (CLAUDE.md §3-4, §9-2).
  규칙은 docs/RULES/SUBAGENTS.md §7-6 "Claude 가 agy 를 부를 때".

  미리 막는 것 (agy --settings 의 세션 거부 규칙. 저장되지 않고, 훅의 allow 보다 앞서고, 하위 에이전트에도 적용된다):
    둘 다 : 셸 명령 전부(command(*), 샌드박스 안팎 모두)와 MCP. 시험과 git 은 부른 쪽(Claude)이 한다.
    ask   : 파일 쓰기 전부
    fix   : -Allow 밖에 지금 있는 파일·폴더의 쓰기
    (2026-11-25 실측: auto 모드에서는 unsandboxed(*)·command(claude) 거부가 듣지 않아 git tag 와 claude 가 돌았다.
     그래서 일부만 막지 않고 command(*) 로 닫고 accept-edits 모드로 돈다.)
  실행 뒤 대조하는 것: 모든 ref·HEAD·브랜치·색인(플래그 포함), .git/config·훅, 하네스 파일, agy 사용자 설정,
    추적 파일의 크기·수정 시각, 바뀐 파일과 미추적 파일의 내용, 저장소 루트와 -Allow 파일이 있는 폴더의 항목(무시 대상 포함).
    fix 의 -Allow 파일 말고 하나라도 다르면 실패다. 되돌리지 않고 알린다.
  agy 와 그 자식은 Windows Job 하나에 넣고, 끝나면 남은 것까지 끝낸 뒤에 대조한다.
  보지 못하는 것: 그 밖의 폴더에 있는 무시 대상 파일(Library 등. fix 에서는 거부 규칙이 막는다), 저장소 밖의 파일, 원격 저장소.

  산출물은 %TEMP%\protoharness-agy-run\<RunId>\ 에만 쓴다: sent.md, settings.json, raw.json, out.md, stderr.log, result.json
  종료 코드:
    0  끝남 (AGY-STATUS: done)
    2  인자·가드·잠금 거부 (agy 를 부르지 않음)
    10 agy 가 막혔거나 물었다 (AGY-STATUS: blocked | question)
    12 계약 위반 (범위 밖 변경, JSON 해석 실패, AGY-STATUS 줄 없음)
    13 실행 실패 (agy 오류, git 오류, 타임아웃, 대조 실패)

  주의: 이 파일은 UTF-8 with BOM 으로 저장한다 (Windows PowerShell 5.1 한글 깨짐 방지).
#>

param(
    [ValidateSet('ask', 'fix')]
    [string]$Role = 'ask',
    [string]$PromptFile = '',
    [string]$RunId = '',
    # fix 가 고쳐도 되는 파일. 저장소 기준 상대 경로를 쉼표로 잇는다.
    [string]$Allow = '',
    # 앞 실행의 RunId. 그 대화를 잇는다. 한 작업의 왕복은 최대 2번이다.
    [string]$Resume = '',
    [string]$Model = 'gemini-4-neon-high',
    [int]$TimeoutSec = 1500
)

$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd([char]92)
$maxRounds = 2
$maxPromptChars = 20000   # 명령줄 한도(32767자) 안에 든다

function Exit-Guard([string]$msg) {
    [Console]::Error.WriteLine("agy-run 거부(exit 2): $msg")
    exit 2
}

function Quote-Arg([string]$arg) {
    if ($arg -eq '') { return '""' }
    if ($arg -notmatch '[\s"]') { return $arg }
    $escaped = [regex]::Replace($arg, '(\\*)"', '$1$1\"')
    $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
    return '"' + $escaped + '"'
}

# 이 스크립트가 Job 에 들어가면 여기서 띄우는 프로세스와 그 자식이 모두 같은 Job 에 들어간다.
# 부모가 먼저 끝난 자식도 이 목록에 남는다 (taskkill /T 는 부모가 없으면 찾지 못한다).
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
public static class AgyRunJob
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr a, string n);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool AssignProcessToJobObject(IntPtr j, IntPtr p);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool QueryInformationJobObject(IntPtr j, int c, IntPtr i, int l, IntPtr r);
    static IntPtr job;
    public static void Join()
    {
        job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) throw new Win32Exception();
        if (!AssignProcessToJobObject(job, Process.GetCurrentProcess().Handle)) throw new Win32Exception();
    }
    // Process ids in the job other than this script.
    public static int[] Others()
    {
        int size = 8 + 4096 * IntPtr.Size;
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!QueryInformationJobObject(job, 3, buffer, size, IntPtr.Zero)) throw new Win32Exception();
            int count = Marshal.ReadInt32(buffer, 4), self = Process.GetCurrentProcess().Id;
            var ids = new List<int>();
            for (int k = 0; k < count; k++)
            {
                int id = (int)Marshal.ReadIntPtr(buffer, 8 + k * IntPtr.Size);
                if (id != self) ids.Add(id);
            }
            return ids.ToArray();
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
}
'@

# Job 에 남은 프로세스를 끝낸다. 끝나지 않은 것의 PID 를 돌려준다.
function Stop-JobProcesses() {
    $until = (Get-Date).AddSeconds(10)
    while ($true) {
        $ids = @([AgyRunJob]::Others())
        if ($ids.Count -eq 0 -or (Get-Date) -gt $until) { return $ids }
        foreach ($id in $ids) { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue }   # 그 사이 끝난 것은 다음 목록에서 빠진다
        Start-Sleep -Milliseconds 50
    }
}

# 프로세스를 돌리고 stdout·stderr 를 UTF-8 로 받는다. 돌아오거나 던질 때 Job 에는 살아 있는 프로세스가 없다.
function Invoke-Captured([string]$file, [string[]]$argList, [int]$timeoutMs) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $file
    $psi.Arguments = (($argList | ForEach-Object { Quote-Arg $_ }) -join ' ')
    if ($psi.Arguments.IndexOf([char]0) -ge 0) { throw '인자에 NUL 문자가 있다. 그 뒤의 인자가 사라진다.' }
    if ($file.Length + $psi.Arguments.Length -gt 32000) { throw "명령줄이 $($psi.Arguments.Length)자다 (Windows 한도 32767자)." }
    $psi.WorkingDirectory = $repoRoot
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = $utf8
    $psi.StandardErrorEncoding = $utf8
    $psi.EnvironmentVariables['PROTOHARNESS_AGENT_RUN'] = '1'

    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $psi
    try {
        $null = $proc.Start()
        $proc.StandardInput.Close()
        $outTask = $proc.StandardOutput.ReadToEndAsync()
        $errTask = $proc.StandardError.ReadToEndAsync()
        $inTime = $proc.WaitForExit($timeoutMs)

        # 본체가 끝났어도 남은 자식을 끝낸다. 그래야 출력이 닫히고, 대조 뒤에 늦은 변경이 생기지 않는다.
        $stuck = @(Stop-JobProcesses)
        if ($stuck.Count -gt 0) { throw "$file 의 프로세스가 끝나지 않는다 (PID $($stuck -join ', ')). 직접 확인하고 끝낸 뒤에 다시 부른다." }
        if (-not $inTime) { throw "$file 이 $([int]($timeoutMs / 1000))초 안에 끝나지 않아 끝냈다." }
        if (-not $outTask.Wait(5000) -or -not $errTask.Wait(5000)) { throw "$file 은 끝났지만 출력이 5초 안에 닫히지 않았다." }
        return [pscustomobject]@{ Code = $proc.ExitCode; Out = $outTask.Result; Err = $errTask.Result }
    }
    finally {
        $proc.Dispose()
    }
}

function Invoke-Git([string[]]$gitArgs) {
    $r = Invoke-Captured $gitExe (@('--no-optional-locks', '-C', $repoRoot, '-c', 'core.quotepath=false', '-c', 'core.safecrlf=false') + $gitArgs) 60000
    if ($r.Code -ne 0) { throw "git $($gitArgs -join ' ') 실패 (exit $($r.Code)): $($r.Err.Trim())" }
    return $r.Out
}

function Get-TextHash([string]$text) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($utf8.GetBytes($text))) } finally { $sha.Dispose() }
}

function Get-PathHash([string]$full) {
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { return 'missing' }
    return (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash
}

# 앞뒤로 견줄 상태. 키가 "file <경로>" 인 것만 -Allow 로 바뀔 수 있다.
function Get-Snapshot([string[]]$watchDirs) {
    $s = @{}
    $s['HEAD'] = (Invoke-Git @('rev-parse', 'HEAD')).Trim()
    $s['branch'] = (Invoke-Git @('branch', '--show-current')).Trim()
    $s['index'] = Get-TextHash ((Invoke-Git @('ls-files', '-s', '-z')) + (Invoke-Git @('ls-files', '-v', '-z')))
    # git status 는 줄바꿈만 다른 변경과 assume-unchanged 파일을 보이지 않는다. 크기와 수정 시각을 따로 본다.
    foreach ($path in ((Invoke-Git @('ls-files', '-z')) -split "`0")) {
        if ($path -eq '') { continue }
        $fi = New-Object System.IO.FileInfo((Join-Path $repoRoot $path))
        $s['stat ' + $path] = if ($fi.Exists) { "$($fi.Length) $($fi.LastWriteTimeUtc.Ticks)" } else { 'missing' }
    }
    # 이 폴더들에는 거부 규칙이 닿지 않는 새 파일이 생길 수 있다. 무시 대상까지 항목을 모두 본다.
    foreach ($dir in $watchDirs) {
        foreach ($child in (Get-ChildItem -LiteralPath $dir -Force)) {
            $rel = $child.FullName.Substring($repoRoot.Length + 1).Replace([char]92, [char]47)
            $s['entry ' + $rel] = if ($child.PSIsContainer) { 'dir' } else { "$($child.Length) $($child.LastWriteTimeUtc.Ticks)" }
        }
    }
    foreach ($line in ((Invoke-Git @('for-each-ref', '--format=%(refname) %(objectname)')) -split "\r?\n")) {
        if ($line -ne '') { $s['ref ' + ($line -split ' ')[0]] = ($line -split ' ')[1] }
    }

    $records = (Invoke-Git @('status', '--porcelain=v1', '-z', '-uall', '--no-renames')) -split "`0"
    foreach ($rec in $records) {
        if ($rec.Length -lt 4) { continue }
        $path = $rec.Substring(3)
        $s['file ' + $path] = $rec.Substring(0, 2) + ' ' + (Get-PathHash (Join-Path $repoRoot $path))
    }

    $guarded = New-Object System.Collections.Generic.List[string]
    foreach ($rel in @('.git/config', '.git/info/exclude', 'CLAUDE.md', 'AGENTS.md', 'GEMINI.md')) { $guarded.Add((Join-Path $repoRoot $rel)) }
    foreach ($dir in @('.git/hooks', '.claude', '.codex', '.agents', 'tools/hooks')) {
        $full = Join-Path $repoRoot $dir
        if (Test-Path -LiteralPath $full) {
            foreach ($f in (Get-ChildItem -LiteralPath $full -Recurse -File -Force)) { $guarded.Add($f.FullName) }
        }
    }
    foreach ($rel in @('config/config.json', 'config/mcp_config.json', 'config/hooks.json', 'antigravity-cli/settings.json')) {
        $guarded.Add((Join-Path $HOME ('.gemini/' + $rel)))
    }
    foreach ($full in $guarded) { $s['guard ' + $full] = Get-PathHash $full }
    return $s
}

# -Allow 파일로 가는 길에 있지 않은 것을 모두 거부한다. 폴더 하나의 거부는 그 아래 전부에 걸린다.
function Add-DeniedSiblings([string]$dir, [string[]]$allowFull, $denied) {
    foreach ($child in (Get-ChildItem -LiteralPath $dir -Force)) {
        $full = $child.FullName
        if ($allowFull -contains $full) { continue }
        $isAncestor = @($allowFull | Where-Object { $_.StartsWith($full + [char]92, [System.StringComparison]::OrdinalIgnoreCase) }).Count -gt 0
        if ($child.PSIsContainer -and $isAncestor) { Add-DeniedSiblings $full $allowFull $denied }
        else { $denied.Add('write_file(' + $full.Replace([char]92, [char]47) + ')') }
    }
}

# --- 1. 인자와 가드 ---
if (-not [string]::IsNullOrEmpty($env:PROTOHARNESS_AGENT_RUN)) { Exit-Guard '다른 에이전트 실행 안에서 부르지 않는다 (연쇄 호출 금지, SUBAGENTS.md §7-6).' }
if ($RunId -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$') { Exit-Guard "-RunId 는 영문·숫자로 시작하고 영문·숫자·._- 만 쓴다. 입력값: '$RunId'" }
if ($Model -notmatch '^gemini-') { Exit-Guard "agy 에는 Gemini 모델(^gemini-)만 지정한다 (SUBAGENTS.md §7-6). 입력값: $Model" }
if ($TimeoutSec -lt 30) { Exit-Guard "-TimeoutSec 는 30 이상이다. 입력값: $TimeoutSec" }
if ([string]::IsNullOrWhiteSpace($PromptFile) -or -not (Test-Path -LiteralPath $PromptFile -PathType Leaf)) { Exit-Guard "-PromptFile 이 없다: '$PromptFile'" }
$prompt = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $PromptFile).Path, $utf8)
if ($prompt.Length -gt $maxPromptChars) { Exit-Guard "지시서가 $($prompt.Length)자다. $maxPromptChars 자까지 넣는다 (긴 자료는 저장소 파일로 가리킨다)." }

if ($prompt.IndexOf([char]0) -ge 0) { Exit-Guard '지시서에 NUL 문자가 있다.' }

[AgyRunJob]::Join()
$gitExe = (Get-Command git -ErrorAction Stop).Source
$agyCmd = Get-Command agy -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $agyCmd) { Exit-Guard 'agy 실행 파일을 PATH 에서 찾지 못했다.' }

$branch = (Invoke-Git @('branch', '--show-current')).Trim()
if ($branch -eq '' -or $branch -eq 'main' -or $branch -eq 'dev') { Exit-Guard "현재 브랜치가 '$branch' 다. main·dev·detached HEAD 에서는 부르지 않는다 (CLAUDE.md §9-2)." }
$approvalLine = [regex]::Match($prompt, '(?m)^승인:[ \t]*(?<a>\S[^\r\n]*)')
if (-not $approvalLine.Success) { Exit-Guard "지시서에 '승인: <언제, 무엇이>' 줄이 없다 (CLAUDE.md §3-4)." }
$branchLine = [regex]::Match($prompt, '(?m)^브랜치:[ \t]*(?<b>[^\s(]+)')
if (-not $branchLine.Success) { Exit-Guard "지시서에 '브랜치: <이름>' 줄이 없다 (CLAUDE.md §9-2)." }
if ($branchLine.Groups['b'].Value -ne $branch) { Exit-Guard "지시서의 브랜치($($branchLine.Groups['b'].Value))가 현재 브랜치($branch)와 다르다." }

$allowFull = @()
$allowRel = @()
if ($Role -eq 'fix') {
    # 경로를 먼저 정규화하고 그 값으로 검사·대조한다. 글자 그대로 보면 tools//hooks/x 가 하네스 검사를 빠져나간다.
    foreach ($raw in ($Allow -split ',')) {
        $raw = $raw.Trim().Replace([char]92, [char]47)
        if ($raw -eq '') { continue }
        if ($raw -match '[:*?()"<>|~]' -or $raw -match '(^|/)\.{1,2}(/|$)' -or $raw -match '^/' -or $raw -match '[/. ]$') { Exit-Guard "-Allow 는 저장소 기준 파일의 상대 경로만 쓴다. 입력값: $raw" }
        $full = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $raw))
        if (-not $full.StartsWith($repoRoot + [char]92, [System.StringComparison]::OrdinalIgnoreCase)) { Exit-Guard "-Allow 가 저장소 밖이다: $raw" }
        $rel = $full.Substring($repoRoot.Length + 1).Replace([char]92, [char]47)
        if ($rel -match '\.(meta|unity|prefab|asset|inputactions)$' -or $rel -match '^(Library|Temp|obj|Build|UserSettings|Logs|ProjectSettings|Packages)/') { Exit-Guard "agy 에 맡기지 않는 파일이다 (CLAUDE.md §0): $rel" }
        if ($rel -match '^((CLAUDE|AGENTS|GEMINI)\.md$|\.git|\.claude/|\.codex/|\.agents/|tools/hooks/)') { Exit-Guard "하네스·git 파일은 agy 에 맡기지 않는다 (SUBAGENTS.md §7-6): $rel" }
        if (Test-Path -LiteralPath $full -PathType Container) { Exit-Guard "-Allow 는 파일만 받는다: $rel" }
        if (-not (Test-Path -LiteralPath (Split-Path -Parent $full) -PathType Container)) { Exit-Guard "-Allow 파일의 폴더가 없다: $rel" }
        # 링크를 따라가면 다른 파일에 쓴다. 저장소 루트에서 대상까지 정션·심볼릭 링크·하드 링크를 받지 않는다.
        $probe = $full
        while ($probe.Length -gt $repoRoot.Length) {
            $item = Get-Item -LiteralPath $probe -Force -ErrorAction SilentlyContinue
            if ($null -ne $item -and (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -or -not [string]::IsNullOrEmpty($item.LinkType))) { Exit-Guard "-Allow 경로에 링크가 있다: $probe" }
            $probe = Split-Path -Parent $probe
        }
        $allowFull += $full
        $allowRel += $rel
    }
    if ($allowRel.Count -eq 0) { Exit-Guard '-Role fix 에는 -Allow "<고쳐도 되는 파일>" 이 필요하다.' }
}
elseif (-not [string]::IsNullOrWhiteSpace($Allow)) { Exit-Guard '-Role ask 에는 -Allow 를 주지 않는다 (아무것도 고치지 않는다).' }

$runsRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'protoharness-agy-run'
$runDir = Join-Path $runsRoot $RunId
$round = 1
$conversation = ''
$approval = $approvalLine.Groups['a'].Value.Trim()

# --- 2. 잠금. agent-relay 와도 동시에 돌지 않는다 ---
$locks = @()
try {
    foreach ($name in @('Local\protoharness-agent-relay', 'Local\protoharness-agy-run')) {
        $m = New-Object System.Threading.Mutex($false, $name)
        $held = $false
        try { $held = $m.WaitOne(0) }
        catch [System.Threading.AbandonedMutexException] { $held = $true }   # 앞 주인이 죽었다. 잠금은 넘어왔다
        if (-not $held) { $m.Dispose(); Exit-Guard "다른 에이전트 실행이 돌고 있다 ($name)." }
        $locks += $m
    }
    if (-not [string]::IsNullOrWhiteSpace($Resume)) {
        if ($Resume -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$') { Exit-Guard "-Resume 는 앞 실행의 RunId 다. 입력값: '$Resume'" }
        $prevDir = Join-Path $runsRoot $Resume
        $prevPath = Join-Path $prevDir 'result.json'
        if (-not (Test-Path -LiteralPath $prevPath)) { Exit-Guard "앞 실행의 결과가 없다: $prevPath" }
        $prev = $null
        try { $prev = [System.IO.File]::ReadAllText($prevPath, $utf8) | ConvertFrom-Json }
        catch { Exit-Guard "앞 실행의 결과를 읽지 못했다: $($_.Exception.Message)" }
        if ($null -eq $prev -or ($prev.exitCode -ne 0 -and $prev.exitCode -ne 10)) { Exit-Guard "앞 실행이 exit $($prev.exitCode) 로 끝났다. 잇지 않고 사용자에게 보고한다." }
        if ($prev.repo -ne $repoRoot -or $prev.role -ne $Role -or $prev.branch -ne $branch) { Exit-Guard "앞 실행(repo=$($prev.repo), role=$($prev.role), branch=$($prev.branch))과 저장소·역할·브랜치가 다르다." }
        if ((@($prev.allow | Sort-Object) -join ',') -ne (@($allowRel | Sort-Object) -join ',')) { Exit-Guard "앞 실행의 -Allow($(@($prev.allow) -join ','))와 다르다. 이어 가기는 범위를 바꾸지 않는다." }
        if ($prev.approval -ne $approval) { Exit-Guard "앞 실행의 승인 줄과 다르다: $($prev.approval)" }
        if ($prev.round -ge $maxRounds) { Exit-Guard "왕복은 최대 $maxRounds 번이다 (앞 실행이 $($prev.round)번째)." }
        if ([string]::IsNullOrWhiteSpace($prev.conversationId)) { Exit-Guard '앞 실행에 대화 ID 가 없다.' }
        if (Test-Path -LiteralPath $runDir) { Exit-Guard "같은 RunId 의 폴더가 있다: $runDir (새 -RunId 를 쓴다)" }
        # 한 실행은 한 번만 잇는다. CreateNew 라서 두 번째는 실패한다.
        try {
            $mark = New-Object System.IO.FileStream((Join-Path $prevDir 'resumed.txt'), [System.IO.FileMode]::CreateNew)
            $mark.Dispose()
        }
        catch [System.IO.IOException] { Exit-Guard "앞 실행은 이미 이어 갔다: $Resume" }
        $round = [int]$prev.round + 1
        $conversation = [string]$prev.conversationId
    }
    if (Test-Path -LiteralPath $runDir) { Exit-Guard "같은 RunId 의 폴더가 있다: $runDir (새 -RunId 를 쓴다)" }
    $null = New-Item -ItemType Directory -Path $runDir

    # --- 3. 이번 실행만의 agy 설정 ---
    $denied = New-Object System.Collections.Generic.List[string]
    $denied.Add('mcp(*)')
    $denied.Add('command(*)')
    if ($Role -eq 'ask') { $denied.Add('write_file(*)') }
    else { Add-DeniedSiblings $repoRoot $allowFull $denied }
    $settings = [ordered]@{ allowNonWorkspaceAccess = $false; permissions = [ordered]@{ deny = $denied.ToArray() } }
    $settingsPath = Join-Path $runDir 'settings.json'
    [System.IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json -Depth 5), $utf8)

    $scope = if ($Role -eq 'ask') { '- 읽기 전용이다. 파일을 만들거나 고치지 마라.' } else { "- 고쳐도 되는 파일: $($allowRel -join ', ') — 그 밖은 새로 만들지도 고치지도 마라." }
    $sent = @"
[Claude Code 가 사용자 승인으로 보낸 지시 — 왕복 $round/$maxRounds]
먼저 CLAUDE.md 와 docs/RULES/SUBAGENTS.md 의 §7-6 을 따른다.
$scope
- 셸 명령과 MCP 는 막혀 있다. 읽기·검색·편집 도구만 쓴다. 시험과 git 은 부른 쪽이 한다. 거부되면 우회하지 말고 원문을 보고하라.
- 승인 범위를 넓히지 마라. 실행해 보지 않은 것은 "검증 안 됨" 으로 적어라.
- 응답의 마지막 줄은 정확히 다음 셋 중 하나다: AGY-STATUS: done / AGY-STATUS: blocked / AGY-STATUS: question

$prompt
"@
    [System.IO.File]::WriteAllText((Join-Path $runDir 'sent.md'), $sent, $utf8)

    # --- 4. 실행과 앞뒤 대조 ---
    $exitCode = 13
    $errors = New-Object System.Collections.Generic.List[string]
    $violations = @()
    $changedAllowed = @()
    $status = $null
    $parsed = $null
    $before = $null
    $started = Get-Date
    $watchDirs = @($repoRoot)
    foreach ($full in $allowFull) {
        $dir = Split-Path -Parent $full
        while ($dir.Length -gt $repoRoot.Length) { $watchDirs += $dir; $dir = Split-Path -Parent $dir }
    }
    $watchDirs = @($watchDirs | Sort-Object -Unique)
    try {
        $before = Get-Snapshot $watchDirs
        # 거부 규칙이 긴 지시문보다 앞에 온다.
        $agyArgs = @('--settings', $settingsPath, '--sandbox', '--mode', 'accept-edits', '--model', $Model, '--output-format', 'json', '--print-timeout', "${TimeoutSec}s")
        if ($conversation -ne '') { $agyArgs += @('--conversation', $conversation) }
        $agyArgs += @('-p', $sent)
        $run = Invoke-Captured $agyCmd.Source $agyArgs (($TimeoutSec + 30) * 1000)
        [System.IO.File]::WriteAllText((Join-Path $runDir 'raw.json'), $run.Out, $utf8)
        [System.IO.File]::WriteAllText((Join-Path $runDir 'stderr.log'), $run.Err, $utf8)

        if ($run.Code -ne 0) { $errors.Add("agy 종료 코드 $($run.Code): $($run.Err.Trim())") }
        else {
            try { $parsed = $run.Out | ConvertFrom-Json }
            catch { $exitCode = 12; $errors.Add("agy 출력을 JSON 으로 해석하지 못했다: $($_.Exception.Message)") }
            # 빈 출력과 "null" 은 예외 없이 $null 이 된다.
            if ($null -eq $parsed -and $errors.Count -eq 0) { $exitCode = 12; $errors.Add("agy 가 빈 출력을 냈다. $($run.Err.Trim())") }
        }
        if ($null -ne $parsed) {
            $response = [string]$parsed.response
            [System.IO.File]::WriteAllText((Join-Path $runDir 'out.md'), $response, $utf8)
            $last = @($response -split "\r?\n" | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' }) | Select-Object -Last 1
            $statusHit = [regex]::Match("$last",'^AGY-STATUS:\s*(done|blocked|question)$')
            if ([string]$parsed.status -ne 'SUCCESS') { $errors.Add("agy 상태가 SUCCESS 가 아니다: $($parsed.status) $($parsed.error)") }
            elseif (-not $statusHit.Success) { $exitCode = 12; $errors.Add("응답의 마지막 줄에 AGY-STATUS 가 없다. $($run.Err.Trim())") }
            else {
                $status = $statusHit.Groups[1].Value
                $exitCode = if ($status -eq 'done') { 0 } else { 10 }
            }
        }
    }
    catch { $exitCode = 13; $errors.Add($_.Exception.Message) }

    # 실행이 어떻게 끝났든 대조한다.
    try {
        if ($null -eq $before) { throw '실행 전 상태를 읽지 못해 agy 를 부르지 않았다.' }
        $after = Get-Snapshot $watchDirs
        $keys = @($before.Keys) + @($after.Keys) | Sort-Object -Unique
        foreach ($k in $keys) {
            if ($before[$k] -eq $after[$k]) { continue }
            $rel = if ($k -match '^(file|stat|entry) ') { $k.Substring($k.IndexOf(' ') + 1) } else { '' }
            if ($rel -ne '' -and $allowRel -contains $rel) { $changedAllowed += $rel }
            else { $violations += $k }
        }
        $changedAllowed = @($changedAllowed | Sort-Object -Unique)
        if ($violations.Count -gt 0) {
            $exitCode = 12
            $errors.Add("허용하지 않은 것이 바뀌었다 (되돌리지 않았다): $($violations -join ' | ')")
        }
    }
    catch { $exitCode = 13; $errors.Add("앞뒤 대조를 하지 못했다: $($_.Exception.Message)") }

    $result = [ordered]@{
        exitCode       = $exitCode
        status         = if ($exitCode -eq 0 -or $exitCode -eq 10) { $status } else { $null }
        role           = $Role
        runId          = $RunId
        round          = $round
        repo           = $repoRoot
        branch         = $branch
        approval       = $approval
        model          = $Model
        allow          = $allowRel
        changed        = $changedAllowed
        violations     = $violations
        conversationId = if ($null -ne $parsed) { [string]$parsed.conversation_id } else { $conversation }
        durationSec    = [int]((Get-Date) - $started).TotalSeconds
        usage          = if ($null -ne $parsed) { $parsed.usage } else { $null }
        error          = ($errors -join ' / ')
        dir            = $runDir
    }
    # 반쯤 쓰인 결과를 읽지 않게 다 쓴 뒤에 이름을 바꾼다.
    $resultTemp = Join-Path $runDir 'result.json.tmp'
    [System.IO.File]::WriteAllText($resultTemp, ($result | ConvertTo-Json -Depth 6), $utf8)
    [System.IO.File]::Move($resultTemp, (Join-Path $runDir 'result.json'))
    Write-Output "agy-run: exit=$exitCode status=$($result.status) role=$Role round=$round/$maxRounds changed=$($changedAllowed -join ',') dir=$runDir"
    foreach ($e in $errors) { [Console]::Error.WriteLine("agy-run: $e") }
    exit $exitCode
}
finally {
    foreach ($m in $locks) { $m.ReleaseMutex(); $m.Dispose() }
}
