<#
  PostToolUse 훅 — .claude/agents/ 를 고친 직후 .codex 사본이 어긋났는지 확인한다.

  고친 파일이 속한 트리(메인 또는 등록된 linked 워크트리)를 이 훅 옆의 tools/sync-agents.ps1 -Check -Root <트리> 로 검사할 뿐 쓰지 않는다.
  다른 파일을 고친 호출은 건드리지 않는다.
  어긋나면 stdout JSON {"decision":"block","reason":...} + exit 0 (Claude 에게 전달). 재생성은 사람이 하거나 시킬 때 한다:
    powershell -ExecutionPolicy Bypass -File tools/sync-agents.ps1

  주의: 이 파일은 UTF-8 with BOM 으로 저장한다 (Windows PowerShell 5.1).
#>

$ErrorActionPreference = 'Stop'

# PostToolUse 는 공식 문서상 JSON decision "block" + reason 이 Claude 에게 전달되는 형식이다
# (exit 2 의 PostToolUse 동작은 문서에서 확인하지 못했다). 도구는 이미 실행됐으므로 되돌리지 않는다.
function Write-Block([string]$reason) {
    $json  = @{ decision = 'block'; reason = $reason } | ConvertTo-Json -Compress
    $bytes = (New-Object System.Text.UTF8Encoding($false)).GetBytes($json)
    $out   = [Console]::OpenStandardOutput()
    $out.Write($bytes, 0, $bytes.Length)
    $out.Flush()
}

$agentEdit = $false
try {
    $stdin = New-Object System.IO.StreamReader([Console]::OpenStandardInput(), (New-Object System.Text.UTF8Encoding($false)))
    $raw = $stdin.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { throw '표준입력이 비었다.' }
    $in = $raw | ConvertFrom-Json

    $path = [string]$in.tool_input.file_path
    if ([string]::IsNullOrWhiteSpace($path)) { exit 0 }
    $p = $path.Replace([char]92, [char]47)
    if ($p -notmatch '^(?:(?<tree>.*)/)?\.claude/agents/[^/]+\.md$') { exit 0 }
    $agentEdit = $true

    # 편집한 파일이 속한 트리를 검사한다. 다른 워크트리(docs/RULES/BRANCHING.md §9-4)의 정의를 고쳤으면 그 트리의 사본을 본다.
    $base = $env:CLAUDE_PROJECT_DIR
    if ([string]::IsNullOrWhiteSpace($base)) { $base = [string]$in.cwd }
    if ([string]::IsNullOrWhiteSpace($base)) { $base = Split-Path -Parent (Split-Path -Parent $PSScriptRoot) }
    $tree = [string]$Matches['tree']
    $root = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($base, $(if ($tree -eq '') { '.' } else { $tree }))).TrimEnd([char]92, [char]47)

    # 그 루트가 이 저장소에 등록된 워크트리일 때만 그 트리의 스크립트를 부른다. 저장소 안 아무 하위 폴더의
    # tools/sync-agents.ps1 을 실행하지 않는다. 등록되지 않은 곳(예: 사용자 폴더 ~/.claude/agents)은 대상이 아니다.
    # 목록은 -z 와 UTF-8 로 직접 읽는다. PowerShell 5.1 이 기본 코드페이지(949)로 받으면 한글 경로가 깨져 등록 트리를 놓친다.
    $repo = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)).Replace('\', '/')
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = 'git'
    $info.Arguments = '-C "' + $repo + '" worktree list --porcelain -z'
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    $info.StandardOutputEncoding = [System.Text.Encoding]::UTF8; $info.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    $proc = [System.Diagnostics.Process]::Start($info)
    $outTask = $proc.StandardOutput.ReadToEndAsync(); $errTask = $proc.StandardError.ReadToEndAsync()
    if (-not $proc.WaitForExit(10000) -or -not [System.Threading.Tasks.Task]::WaitAll([System.Threading.Tasks.Task[]]@($outTask, $errTask), 5000)) {
        try { $proc.Kill() } catch { [Console]::Error.WriteLine("after-agents-edit.ps1: git 을 끝내지 못했다: $($_.Exception.Message)") }
        throw 'git worktree list 가 10초 안에 끝나지 않았다.'
    }
    if ($proc.ExitCode -ne 0) { throw "git worktree list 실패 (exit $($proc.ExitCode)): $($errTask.Result.Trim())" }
    $roots = @($outTask.Result.Split([char]0) | Where-Object { $_.StartsWith('worktree ') } | ForEach-Object { [System.IO.Path]::GetFullPath($_.Substring(9)).TrimEnd([char]92, [char]47) })
    if ($roots.Count -eq 0) { throw 'git worktree list 출력에서 워크트리를 하나도 읽지 못했다.' }
    $isTree = $false
    foreach ($r in $roots) { if ([string]::Equals($r, $root, [System.StringComparison]::OrdinalIgnoreCase)) { $isTree = $true } }
    if (-not $isTree) { exit 0 }

    # 검사기는 이 훅과 같은 트리의 것을 쓰고, 대상 트리는 -Root 로 넘긴다. 대상 트리의 (미커밋일 수 있는) 스크립트는 실행하지 않는다.
    $script = Join-Path (Split-Path -Parent $PSScriptRoot) 'sync-agents.ps1'
    $result = & powershell -NoProfile -ExecutionPolicy Bypass -File $script -Check -Root $root 2>&1 | Out-String
    $code = $LASTEXITCODE
    if ($code -eq 0) { exit 0 }

    Write-Block ("에이전트 정의를 고쳤는데 .codex 사본이 어긋났다 (sync-agents -Check exit $code, $root). CLAUDE.md §7 은 둘을 같이 고치라고 한다.`n" + $result.Trim())
    exit 0
}
catch {
    [Console]::Error.WriteLine("after-agents-edit.ps1 오류: $($_.Exception.Message)")
    # 에이전트 정의를 고친 호출이면 확인을 마치지 못한 것을 Claude 에게도 알린다.
    if ($agentEdit) {
        Write-Block "에이전트 정의를 고쳤는데 .codex 사본을 확인하지 못했다: $($_.Exception.Message) (CLAUDE.md §7, tools/hooks/after-agents-edit.ps1)"
        exit 0
    }
    exit 1
}
