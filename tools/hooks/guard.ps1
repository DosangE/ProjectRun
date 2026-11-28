<#
  PreToolUse 훅 — CLAUDE.md §0 금지선 중 기계로 판정되는 것을 도구 호출 직전에 막는다.

  판정만 한다. 파일·git 상태를 바꾸지 않는다.
    deny : 되돌릴 수 없는 git 명령(reset --hard, checkout . 과 -f, switch -f·--discard-changes,
           restore ., clean, 강제 푸시), main/dev 에서의 git commit        (§0 Git, §9-2)
    deny : 워크트리 우회 (§0 Git, docs/RULES/BRANCHING.md §9-4) — --no-checkout 없는 또는 -f·-B 붙은 worktree add,
           worktree remove -f, checkout/switch --ignore-other-worktrees, sparse-checkout disable·add·set --stdin,
           등록된 linked 트리 루트가 아닌 곳(메인·하위 폴더·--git-dir/--work-tree/-c/GIT_* 지정)의 sparse-checkout init·set·reapply,
           승인 목록과 낱말마다 같지 않은 sparse-checkout init·set, cone 이 아닌 트리의 set
    ask  : 승인 후에만 건드릴 수 있는 파일에 Claude Edit/Write/NotebookEdit
           (§0 파일·에셋, 설정 / 하네스 파일 §3-1, docs/RULES/SUBAGENTS.md §7-6)
           경로는 등록된 모든 워크트리(메인·linked)의 루트를 떼고 본다. 워크트리 목록을 못 읽으면 deny.
    deny : 같은 파일에 Codex apply_patch 및 Antigravity edit_file/write_file
           (Codex 는 ask 미지원. Antigravity 의 ask·force_ask 가 확인 창을 띄우는지는 확인 못 함 —
            2026-11-24 시험은 없는 파일이 대상이라 "파일 없음" 오류가 먼저 났을 수 있다)
    deny : 경로나 명령이 비어 판정할 수 없는 Antigravity 호출, 읽다가 오류가 난 git 명령
  해당 없으면 Claude/Codex 는 아무것도 출력하지 않고 exit 0 (= 판정 없음, 평소 권한 흐름),
  Antigravity 는 {"decision":"allow"} 를 출력하고 exit 0.

  Claude Code(.claude/settings.json), Codex(.codex/hooks.json), Antigravity(.agents/hooks.json)가 같이 부른다.
  스키마:
    - Claude/Codex: stdin(tool_name, tool_input.command|file_path, cwd) /
      stdout(hookSpecificOutput.permissionDecision). Codex 셸은 tool_name Bash,
      파일 편집은 apply_patch 이고 경로는 tool_input.command 의 패치 머리줄에 있다.
    - Antigravity: stdin(toolCall.name, toolCall.args.CommandLine|Path, cwd, workspacePaths) /
      stdout(decision, reason). 셸은 run_command, 파일 편집은 edit_file / write_file.
  한계: 셸 명령(sed -i, 리다이렉트, rm 등)으로 보호 파일을 바꾸는 것은 판정하지 않는다.
    git 은 변수(& $g clean)·별칭(-c alias.x=)·스크립트 파일 안의 것, 절대 경로로 적은 저장소 루트,
    같은 명령에서 먼저 옮긴 폴더(cd)나 브랜치(git switch dev && git commit)를 모른다.
    Claude 의 Bash 는 settings.json 의 if 필터(*git*)를 지난 것만 온다 — g"it" 처럼 끊어 쓴 이름은 오지 않을 수 있다.

  주의: 이 파일은 UTF-8 with BOM 으로 저장한다. Windows PowerShell 5.1 은 BOM 이 없으면
        .ps1 을 ANSI(cp949)로 읽어 한글이 깨진다.
#>

$ErrorActionPreference = 'Stop'

$script:IsAntigravity = $false
$script:MustDeny = $false   # 판정에 필요한 값을 못 읽어 보호 파일로 본 경우. Claude 도 ask 가 아니라 deny 로 막는다.
$script:GitEnvInCommand = $false

function Write-Decision([string]$decision, [string]$reason) {
    if ($script:IsAntigravity) {
        $obj = @{ decision = $decision }
        if (-not [string]::IsNullOrEmpty($reason)) {
            $obj.reason = $reason
        }
    }
    else {
        $obj = @{ hookSpecificOutput = @{
            hookEventName            = 'PreToolUse'
            permissionDecision       = $decision
            permissionDecisionReason = $reason
        } }
    }
    $json  = $obj | ConvertTo-Json -Compress -Depth 5
    $bytes = (New-Object System.Text.UTF8Encoding($false)).GetBytes($json)
    $out   = [Console]::OpenStandardOutput()
    $out.Write($bytes, 0, $bytes.Length)
    $out.Flush()
}

function Write-PassThrough() {
    if ($script:IsAntigravity) {
        Write-Decision 'allow' ''
    }
}

# 보호 파일이면 사유를, 아니면 $null 을 돌려준다. 경로는 저장소 기준 상대 경로로 바꿔 본다.
# . 과 .. 를 먼저 풀어 절대 경로로 만든다. 글자 그대로 비교하면 docs/../Packages/manifest.json 이 빠져나간다.
# 풀 수 없는 경로(잘못된 문자, Foo::$DATA 같은 스트림 표기 등)는 보호 파일일 수 있으므로 막는다.
# 던지게 두면 바깥 catch 의 exit 1(비차단)로 끝나 판정 없이 지나간다 — 예전 글자 비교는 이런 경로도 접두사로 잡았다.
function Get-ProtectedReason([string]$path, [string]$root) {
    $rootFull = [System.IO.Path]::GetFullPath($root).TrimEnd([char]92, [char]47)
    try {
        $full = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($rootFull, $path.Trim()))
    }
    catch {
        return "경로를 판정할 수 없어 보호 파일로 본다 ($($_.Exception.Message)). 대상: $path"
    }
    # \\?\, \\.\, \\서버\공유 로 쓰면 같은 파일인데 아래 접두사 비교가 빗나간다. 판정할 수 없으므로 보호 파일로 본다.
    if ($full.StartsWith('\\')) {
        return "확장·장치·UNC 경로는 판정할 수 없어 보호 파일로 본다. 드라이브 경로로 적는다. 대상: $path (CLAUDE.md §0)"
    }
    $p = $full
    # 등록된 모든 워크트리 루트(메인·linked)와 이 스크립트의 저장소 루트를 먼저 뗀다. 가장 긴 루트부터 본다.
    # 다른 워크트리의 절대 경로(메인 → linked, linked → 메인, linked → linked)도 ^ 로 시작하는 판정이 맞게 한다.
    # 목록을 못 읽으면 다른 트리의 보호 파일을 놓칠 수 있으므로 막는다(fail-closed).
    try { $roots = @(Get-WorktreeRoots) }
    catch {
        $script:MustDeny = $true
        return "워크트리 목록을 읽지 못해 보호 파일로 본다 ($($_.Exception.Message)). 대상: $path (docs/RULES/BRANCHING.md §9-4)"
    }
    $repoFull = [System.IO.Path]::GetFullPath((Split-Path -Parent (Split-Path -Parent $PSScriptRoot))).TrimEnd([char]92, [char]47)
    $bases = @(@($roots) + @($repoFull) | Sort-Object -Property Length -Descending) + @($rootFull)   # cwd 는 마지막(하위 폴더일 수 있다)
    foreach ($base in $bases) {
        if ($full.StartsWith($base + [char]92, [System.StringComparison]::OrdinalIgnoreCase)) {
            $p = $full.Substring($base.Length + 1)
            break
        }
    }
    $p = $p.Replace([char]92, [char]47)

    if     ($p -match '\.meta$')                                    { return ".meta 는 직접 만들거나 고치지 않는다. GUID 는 Unity 만 발급한다. 대상: $p (CLAUDE.md §0)" }
    elseif ($p -match '^(Library|Temp|obj|Build|UserSettings)/')    { return "Library/Temp/obj/Build/UserSettings 는 건드리지 않는다. 대상: $p (CLAUDE.md §0)" }
    elseif ($p -match '^ProjectSettings/')                          { return "ProjectSettings/ 변경은 승인 후에만 한다. 대상: $p (CLAUDE.md §0)" }
    elseif ($p -match '^Packages/manifest\.json$')                  { return "Packages/manifest.json 변경은 승인 후에만 한다. 대상: $p (CLAUDE.md §0)" }
    elseif ($p -match '\.(unity|prefab|asset|inputactions)$')       { return "씬·프리팹·에셋·입력 YAML 은 텍스트로 수정하지 않는다. 에디터에서 한다. 대상: $p (CLAUDE.md §0)" }
    elseif ($p -match '^((CLAUDE|AGENTS)\.md$|\.claude/(settings[^/]*\.json$|agents/)|\.codex/|\.agents/|tools/hooks/|docs/RULES/)') {
        return "하네스 파일은 승인 후에만 고치고, 구현하지 않은 모델이 검토한다. 대상: $p (CLAUDE.md §3-1, SUBAGENTS.md §7-6)"
    }
    return $null
}

# 셸 명령을 명령 단위로 나눈다. 명령마다 낱말 배열 하나를 $out 에 넣는다 (따옴표를 풀고, 리다이렉트와 주석은 뺀다).
# 명령 치환 $(...)·백틱, 괄호·블록 안의 명령도 따로 넣고, 그 자리에는 '$()' 를 남긴다.
# $close 는 이 호출을 끝내는 닫는 글자(')' · '}' · ''), 돌려주는 값은 그 다음 위치다. $ps 는 PowerShell 문법인지다.
$script:rxPlain   = New-Object regex '\G[^\s''"\\`$;&|<>(){}#@]+'
$script:rxDqPlain = New-Object regex '\G[^"\\`$]+'
$script:shells    = '^(bash|sh|dash|zsh|ksh|pwsh|powershell|cmd)$'
$script:clock     = [System.Diagnostics.Stopwatch]::StartNew()

# 훅 제한(15초)을 넘기면 판정 없이 지나간다. 그 전에 던져서 막는다.
function Assert-Time() {
    if ($script:clock.ElapsedMilliseconds -gt 8000) { throw '명령이 너무 길거나 복잡해 8초 안에 읽지 못했다. 나눠서 실행한다.' }
}

# 큰따옴표 안(또는 따옴표 없는 heredoc 본문, $q 가 빈 문자열)을 읽는다. 글자는 $sb 에, 안의 명령 치환은 $out 에 넣는다.
function Read-Quoted([string]$t, [int]$i, [string]$q, [bool]$ps, $out, $sb) {
    $n = $t.Length
    while ($i -lt $n) {
        Assert-Time
        $m = $script:rxDqPlain.Match($t, $i)
        if ($m.Success) { [void]$sb.Append($m.Value); $i += $m.Length; continue }
        $c = [string]$t[$i]
        $next = if ($i + 1 -lt $n) { [string]$t[$i + 1] } else { '' }
        if ($c -eq '"') {
            if ($q -eq '') { [void]$sb.Append($c); $i++; continue }
            if ($ps -and $next -eq '"') { [void]$sb.Append($c); $i += 2; continue }
            return $i + 1
        }
        if ($c -eq '$' -and $next -eq '(') { $i = Read-Commands $t ($i + 2) ')' $ps $out; [void]$sb.Append('$()'); continue }
        if ($c -eq '\' -and -not $ps) {
            if ($next -eq "`n") { $i += 2; continue }
            if ($next -ne '' -and '$`"\'.Contains($next)) { [void]$sb.Append($next); $i += 2; continue }
        }
        if ($c -eq '`') {
            if ($ps) { [void]$sb.Append($next); $i += 2; continue }
            $i = Read-Backtick $t $i $out; [void]$sb.Append('$()'); continue
        }
        [void]$sb.Append($c); $i++
    }
    return $n
}

# bash 의 `명령` 을 읽는다.
function Read-Backtick([string]$t, [int]$i, $out) {
    $m = [regex]::new('\G`((?:[^`\\]|\\.)*)`?', 'Singleline').Match($t, $i)
    [void](Read-Commands ($m.Groups[1].Value -replace '\\([`$\\])', '$1') 0 '' $false $out)
    return $i + $m.Length
}

function Read-Commands([string]$t, [int]$i, [string]$close, [bool]$ps, $out) {
    $n = $t.Length
    $words = New-Object System.Collections.Generic.List[string]
    $sb = New-Object System.Text.StringBuilder
    $has = $false; $skip = $false          # 낱말을 읽는 중인가 / 다음 낱말은 리다이렉트 대상인가
    $heredocs = New-Object System.Collections.Generic.List[object]
    $lineStart = $out.Count
    $endWord = {
        if ($has) {
            if ($skip) { $skip = $false } else { $words.Add($sb.ToString()) }
            [void]$sb.Clear(); $has = $false
        }
    }
    $endCmd = {
        . $endWord
        $skip = $false
        if ($words.Count -gt 0) { $out.Add($words.ToArray()); $words.Clear() }
    }

    while ($i -lt $n) {
        Assert-Time
        $m = $script:rxPlain.Match($t, $i)
        if ($m.Success) { [void]$sb.Append($m.Value); $has = $true; $i += $m.Length; continue }
        $c = [string]$t[$i]
        $next = if ($i + 1 -lt $n) { [string]$t[$i + 1] } else { '' }

        if ($c -eq "`n") {
            . $endCmd; $i++
            foreach ($h in $heredocs) {
                $end = [regex]::new('^' + $h.Indent + [regex]::Escape($h.Delim) + '$', 'Multiline').Match($t, $i)
                if (-not $end.Success) { break }   # 끝을 못 찾으면 본문으로 보지 않고 명령으로 읽는다
                $body = $t.Substring($i, $end.Index - $i)
                $toShell = $false
                for ($k = $lineStart; $k -lt $out.Count; $k++) {
                    foreach ($w in $out[$k]) { if ((Get-CommandName $w) -match $script:shells) { $toShell = $true } }
                }
                if ($toShell) { [void](Read-Commands $body 0 '' $ps $out) }
                elseif (-not $h.Quoted) { [void](Read-Quoted $body 0 '' $ps $out (New-Object System.Text.StringBuilder)) }
                $i = [Math]::Min($n, $end.Index + $end.Length + 1)
            }
            $heredocs.Clear(); $lineStart = $out.Count
            continue
        }
        if ([char]::IsWhiteSpace($c, 0)) { . $endWord; $i++; continue }
        if ($c -eq '#' -and -not $has) {
            $j = $t.IndexOf("`n", $i); $i = if ($j -lt 0) { $n } else { $j }
            continue
        }
        if ($c -eq "'") {
            if ($ps) {
                $m = [regex]::new('\G''((?:[^'']|'''')*)''?').Match($t, $i)
                [void]$sb.Append($m.Groups[1].Value.Replace("''", "'")); $i += $m.Length
            }
            else {
                $j = $t.IndexOf("'", $i + 1); if ($j -lt 0) { $j = $n }
                [void]$sb.Append($t, $i + 1, $j - $i - 1); $i = $j + 1
            }
            $has = $true; continue
        }
        if ($c -eq '"') { $i = Read-Quoted $t ($i + 1) '"' $ps $out $sb; $has = $true; continue }
        if ($c -eq '\' -and -not $ps) {
            if ($next -ne "`n") { [void]$sb.Append($next); $has = $true }
            $i += 2; continue
        }
        if ($c -eq '`') {
            if (-not $ps) { $i = Read-Backtick $t $i $out; [void]$sb.Append('$()'); $has = $true; continue }
            if ($next -ne "`n") { [void]$sb.Append($next); $has = $true }
            $i += 2; continue
        }
        if ($c -eq '@' -and $ps) {
            $m = [regex]::new('\G@([''"])[ \t]*\n(.*?)(?<=\n)\1@', 'Singleline').Match($t, $i)
            if ($m.Success) {
                if ($m.Groups[1].Value -eq '"') { [void](Read-Quoted $m.Groups[2].Value 0 '' $ps $out $sb) }
                else { [void]$sb.Append($m.Groups[2].Value) }
                $has = $true; $i += $m.Length; continue
            }
        }
        if ($c -eq '$') {
            $m = [regex]::new('\G\$\{([^}]*)\}').Match($t, $i)
            if ($m.Success) {
                [void](Read-Quoted $m.Groups[1].Value 0 '' $ps $out (New-Object System.Text.StringBuilder))
                [void]$sb.Append('${}'); $has = $true; $i += $m.Length; continue
            }
            if (-not $ps) {
                $m = [regex]::new('\G\$\(\((?:[^()]|\([^()]*\))*\)\)').Match($t, $i)   # 산술식
                if ($m.Success) { [void]$sb.Append('$()'); $has = $true; $i += $m.Length; continue }
                $m = [regex]::new('\G\$''((?:[^''\\]|\\.)*)''?', 'Singleline').Match($t, $i)
                if ($m.Success) { [void]$sb.Append($m.Groups[1].Value); $has = $true; $i += $m.Length; continue }
                if ($next -eq '"') { $i++; continue }
            }
            if ($next -eq '(') { $i = Read-Commands $t ($i + 2) ')' $ps $out; [void]$sb.Append('$()'); $has = $true; continue }
            [void]$sb.Append($c); $has = $true; $i++; continue
        }
        if ($c -eq '(') {
            if ($ps) {
                # git @('commit', '-m') 처럼 글자 그대로인 목록은 그 낱말이 인자가 된다. 그 밖의 괄호는 값을 모르는 낱말 하나다.
                $m = [regex]::new('\G\((?>\s*)(?>(?<v>''[^'']*''|"[^"$`]*")(?>\s*,?\s*))+\)').Match($t, $i)
                if ($m.Success -and $words.Count -gt 0 -and ($sb.ToString() -eq '' -or $sb.ToString() -eq '@')) {
                    [void]$sb.Clear(); $has = $false
                    foreach ($v in $m.Groups['v'].Captures) { $words.Add($v.Value.Substring(1, $v.Length - 2)) }
                    $i += $m.Length; continue
                }
                $i = Read-Commands $t ($i + 1) ')' $ps $out; [void]$sb.Append('$()'); $has = $true; continue
            }
            $m =[regex]::new('\G\(\((?:[^()]|\([^()]*\))*\)\)').Match($t, $i)
            . $endCmd
            if ($m.Success) { $i += $m.Length } else { $i = Read-Commands $t ($i + 1) ')' $ps $out }
            continue
        }
        if ($c -eq '{' -and $ps) {
            $m = [regex]::new('\G\{[^{}]*\}').Match($t, $i)
            if ($has -and $sb.ToString().EndsWith('@') -and $m.Success) { [void]$sb.Append($m.Value); $i += $m.Length; continue }   # HEAD@{1}
            . $endCmd; $i = Read-Commands $t ($i + 1) '}' $ps $out
            continue
        }
        if ($c -eq ')' -or ($c -eq '}' -and $ps)) {
            . $endCmd; $i++
            if ($c -eq $close) { return $i }
            continue
        }
        if ($c -eq ';' -or $c -eq '|') { . $endCmd; $i++; continue }
        if ($c -eq '&') {
            if ($next -eq '>' -and -not $ps) { . $endWord } else { . $endCmd }
            $i++; continue
        }
        if ($c -eq '>' -or ($c -eq '<' -and -not $ps)) {
            if ($has -and $sb.ToString() -match '^(\d+|\*)$') { [void]$sb.Clear(); $has = $false }   # 2> 의 2
            . $endWord
            if ($next -eq '(' -and -not $ps) { $i = Read-Commands $t ($i + 2) ')' $ps $out; continue }
            $m = [regex]::new('\G<<(-?)(?!<)[ \t]*(?:''([^''\n]+)''|"([^"\n]+)"|(\\?)([^\s;&|<>()''"\\]+))').Match($t, $i)
            if ($m.Success) {
                $heredocs.Add(@{
                    Indent = if ($m.Groups[1].Value -eq '-') { '\t*' } else { '' }
                    Delim  = $m.Groups[2].Value + $m.Groups[3].Value + $m.Groups[5].Value
                    Quoted = ($m.Groups[5].Value -eq '') -or ($m.Groups[4].Value -ne '')
                })
                $i += $m.Length; continue
            }
            $m = [regex]::new('\G(>{1,2}\|?|<>|<{1,3})(&(\d+-?|-))?').Match($t, $i)
            $i += $m.Length
            if (-not $m.Groups[2].Success) {
                $skip = $true
                if ($i -lt $n -and $t[$i] -eq '&') { $i++ }   # >&파일
            }
            continue
        }
        if ($c -eq '<' -and $next -eq '#') {
            $j = $t.IndexOf('#>', $i); $i = if ($j -lt 0) { $n } else { $j + 2 }
            continue
        }
        [void]$sb.Append($c); $has = $true; $i++
    }
    . $endCmd
    return $n
}

# 실행 파일 이름. 경로와 .exe 를 뗀다.
function Get-CommandName([string]$word) {
    return ($word -replace '^.*[/\\]', '' -replace '\.exe$', '').ToLowerInvariant()
}

# 묶음 단축 옵션(-fu)에 $flag 가 있는가. $valued 의 글자 뒤는 그 옵션의 값이다 (-ofoo).
function Test-ShortFlag([string]$arg, [char]$flag, [string]$valued) {
    if ($arg -notmatch '^-[^-]') { return $false }
    foreach ($ch in $arg.Substring(1).ToCharArray()) {
        if ($ch -ceq $flag) { return $true }
        if ($valued.IndexOf($ch) -ge 0) { return $false }
    }
    return $false
}

# 작업 트리 전체(또는 위 폴더 전체)를 가리키는 pathspec 인가: . ./ .. * **/* :/ :(top,glob)**
function Test-WholeTree([string]$arg) {
    Assert-Time
    $p = $arg.Replace('\', '/')
    $path = $p -replace '^:(\([^)]*\)|/)', ''   # 매직 접두사
    if ($path -eq '') { return $p -ne '' }
    if ($path.StartsWith('/')) { return $false }
    # . 과 .. 를 git 처럼 먼저 푼다: ./Assets/.. 는 . 이다.
    $parts = New-Object System.Collections.Generic.List[string]
    foreach ($part in $path.Split('/')) {
        if ($part -eq '..' -and $parts.Count -gt 0 -and $parts[$parts.Count - 1] -ne '..') { $parts.RemoveAt($parts.Count - 1) }
        elseif ($part -ne '' -and $part -ne '.') { $parts.Add($part) }
    }
    foreach ($part in $parts) { if ($part -notmatch '^(\.\.|\*+)$') { return $false } }
    return $true
}

# 읽기 전용 git 을 돌려 { Code, Out, Err } 를 돌려준다. 같은 인자는 한 번만 돌린다.
# 프로세스 대기와 두 출력 스트림 읽기를 합쳐 3초(훅 전체 마감이 더 가까우면 그만큼) 안에 끝나지 않으면 던진다(호출부가 deny 로 바꾼다).
# 훅 제한(15초)에 걸려 판정 없이 지나가지 않도록 종료 처리에도 한도를 둔다.
$script:gitCache = @{}
function Invoke-GitQuery([string[]]$gitArgs) {
    $key = $gitArgs -join "`n"
    if ($key.Contains('"')) { throw "경로에 따옴표가 있어 git 을 돌릴 수 없다: $key" }
    if (-not $script:gitCache.ContainsKey($key)) {
        $info = New-Object System.Diagnostics.ProcessStartInfo
        $info.FileName = 'git'
        $info.Arguments = (@($gitArgs) | ForEach-Object { '"' + $_ + '"' }) -join ' '
        $info.UseShellExecute = $false; $info.CreateNoWindow = $true
        $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
        $info.StandardOutputEncoding = [System.Text.Encoding]::UTF8; $info.StandardErrorEncoding = [System.Text.Encoding]::UTF8
        # 훅 전체 마감(시작 뒤 11초)에서 종료 처리 2초와 판정 출력 여유를 뺀 만큼만 기다린다. 조회가 여럿 이어져도 15초 제한 안에 deny 를 낸다.
        $limit = [int][Math]::Min(3000, 11000 - 2500 - $script:clock.ElapsedMilliseconds)
        if ($limit -lt 200) { throw "훅 시간 예산이 남지 않아 git 을 돌리지 않았다: $key" }
        $budget = [System.Diagnostics.Stopwatch]::StartNew()
        $proc = [System.Diagnostics.Process]::Start($info)
        $outTask = $proc.StandardOutput.ReadToEndAsync(); $errTask = $proc.StandardError.ReadToEndAsync()
        $exited = $proc.WaitForExit($limit)
        $left = [int][Math]::Max(0, $limit - $budget.ElapsedMilliseconds)
        $read = $exited -and [System.Threading.Tasks.Task]::WaitAll([System.Threading.Tasks.Task[]]@($outTask, $errTask), $left)
        if (-not $read) {
            try {
                $kill = Start-Process -FilePath 'taskkill.exe' -ArgumentList "/PID $($proc.Id) /T /F" -WindowStyle Hidden -PassThru
                [void]$kill.WaitForExit(2000)
            }
            catch { [Console]::Error.WriteLine("guard.ps1: git 프로세스를 끝내지 못했다: $($_.Exception.Message)") }
            throw "git 을 $limit ms 안에 읽지 못했다: $key"
        }
        $script:gitCache[$key] = @{ Code = $proc.ExitCode; Out = $outTask.Result; Err = $errTask.Result.Trim() }
    }
    return $script:gitCache[$key]
}

# git 이 보는 현재 브랜치. 못 읽으면 $script:lookupError 에 원문을 남기고 $null 을 돌려준다. 3초를 넘기면 던진다.
$script:lookupError = $null
function Get-Branch([string[]]$gitArgs) {
    $r = Invoke-GitQuery (@($gitArgs) + @('branch', '--show-current'))
    if ($r.Code -ne 0) { $script:lookupError = "커밋할 곳의 브랜치를 읽지 못했다: $($r.Err)"; return $null }
    return $r.Out.Trim()
}

# 이 저장소에 등록된 모든 워크트리 루트(메인이 첫 항목). git worktree list 가 실패하거나 비면 던진다.
$script:worktreeRoots = $null
function Get-WorktreeRoots() {
    if ($null -eq $script:worktreeRoots) {
        $repo = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)).Replace('\', '/')
        $r = Invoke-GitQuery @('-C', $repo, 'worktree', 'list', '--porcelain', '-z')
        if ($r.Code -ne 0) { throw "git worktree list 실패 (exit $($r.Code)): $($r.Err)" }
        $roots = New-Object System.Collections.Generic.List[string]
        foreach ($field in $r.Out.Split([char]0)) {
            if ($field.StartsWith('worktree ')) {
                $roots.Add([System.IO.Path]::GetFullPath($field.Substring(9)).TrimEnd([char]92, [char]47))
            }
        }
        if ($roots.Count -eq 0) { throw 'git worktree list 에 워크트리가 하나도 없다.' }
        $script:worktreeRoots = $roots.ToArray()
    }
    return $script:worktreeRoots
}

# 비-Unity sparse worktree 의 승인 목록 (docs/RULES/BRANCHING.md §9-4, Q1 (a) --cone). 순서는 보지 않고 집합이 정확히 같아야 한다.
$script:sparseSet = @('.agents', '.claude', '.codex', 'docs', 'tools')

# sparse-checkout 이 바꿀 작업 트리가 등록된 linked 워크트리의 루트인가.
# 메인이면 'main', 판정할 수 없거나 조건에 안 맞으면 사유 문자열, 맞으면 $null 을 돌려준다.
#   $where 는 -C 만 담는다(--git-dir·--work-tree·-c·GIT_* 환경 지정은 호출부가 먼저 막는다 — 메타데이터와 작업 경로를 따로 줄 수 있다).
#   하위 폴더에서 부르면 git 이 인자에 그 접두사를 붙이므로 루트에서만 받는다.
#   set 이 cone 모드로 적용되는지 보도록 그 트리의 core.sparseCheckoutCone 을 $script:sparseCone 에 남긴다.
$script:sparseCone = $null
function Get-SparseTarget([string]$cwd, [string[]]$where) {
    if (($where -join ' ') -match '[$%]') { return '대상 경로에 변수가 있어 어느 워크트리인지 판정할 수 없다' }
    $base = @('-C', $cwd.Replace('\', '/')) + $where
    $r = Invoke-GitQuery ($base + @('rev-parse', '--path-format=absolute', '--git-dir', '--git-common-dir', '--show-toplevel', '--show-prefix'))
    if ($r.Code -ne 0) { return "어느 워크트리인지 읽지 못했다: $($r.Err)" }
    $lines = @($r.Out -split "`r?`n")
    if ($lines.Count -lt 4 -or $lines[0] -eq '' -or $lines[1] -eq '' -or $lines[2] -eq '') { return "rev-parse 출력을 읽지 못했다: $($r.Out.Trim())" }
    $gitDirFull = [System.IO.Path]::GetFullPath($lines[0]).TrimEnd([char]92, [char]47)
    $commonFull = [System.IO.Path]::GetFullPath($lines[1]).TrimEnd([char]92, [char]47)
    $topFull    = [System.IO.Path]::GetFullPath($lines[2]).TrimEnd([char]92, [char]47)
    $roots = @(Get-WorktreeRoots)
    $ic = [System.StringComparison]::OrdinalIgnoreCase
    if ([string]::Equals($gitDirFull, $commonFull, $ic) -or [string]::Equals($topFull, $roots[0], $ic)) { return 'main' }
    $registered = $false
    foreach ($root in ($roots | Select-Object -Skip 1)) { if ([string]::Equals($topFull, $root, $ic)) { $registered = $true } }
    if (-not $registered) { return "대상 작업 트리가 등록된 linked 워크트리가 아니다: $topFull" }
    if ($lines[3] -ne '') { return "작업 트리 루트가 아닌 하위 폴더($($lines[3]))에서 불렀다. 인자에 접두사가 붙는다 — 루트에서 -C <트리> 로 부른다" }
    $c = Invoke-GitQuery ($base + @('config', '--get', 'core.sparseCheckoutCone'))
    $script:sparseCone = if ($c.Code -eq 0) { $c.Out.Trim() } else { '' }
    return $null
}

# Git Bash 경로를 Windows 경로로: /c/x → c:/x, /tmp → TEMP, ~ → 홈.
# \ 를 / 로 바꾼다: Windows PowerShell 5.1 은 빈칸과 끝의 \ 가 같이 있는 인자를 잘못 넘긴다.
function ConvertTo-WinPath([string]$path) {
    $path = $path.Replace('\', '/')
    if ($path -match '^/([A-Za-z])(/.*)?$') { return $Matches[1] + ':' + $(if ($Matches[2]) { $Matches[2] } else { '/' }) }
    if ($path -match '^/tmp(?=$|/)') { return $env:TEMP.Replace('\', '/') + $path.Substring(4) }
    if ($path -match '^~(?=$|/)') { return $HOME.Replace('\', '/') + $path.Substring(1) }
    return $path
}

# git 인자가 금지선이면 사유를, 아니면 $null 을 돌려준다. $gitDir 는 GIT_DIR= 로 준 값이다.
function Get-GitReason([string[]]$a, [string]$cwd, [string]$gitDir, [bool]$plain) {
    $where = New-Object System.Collections.Generic.List[string]   # 커밋 대상을 정하는 -C, --git-dir
    if ($gitDir) { $where.Add("--git-dir=$(ConvertTo-WinPath $gitDir)") }
    $k = 0
    $forceConfig = $false
    # 메타데이터·작업 경로·설정을 따로 주는 전역 옵션. sparse-checkout 변경에서는 대상을 판정할 수 없어 막는다.
    $override = [bool]$gitDir -or $script:GitEnvInCommand
    while ($k -lt $a.Count -and $a[$k].StartsWith('-')) {
        $opt = $a[$k]; $k++
        if ($opt -cmatch '^(-c|--git-dir|--work-tree|--config-env|--bare)(=|$)' -or $opt -cmatch '^-c.') { $override = $true }
        if ($opt -cmatch '^(-C|-c|--git-dir|--work-tree|--namespace|--super-prefix|--config-env|--attr-source)$' -and $k -lt $a.Count) {
            if ($opt -ceq '-C') { if ($a[$k] -ne '') { $where.Add('-C'); $where.Add((ConvertTo-WinPath $a[$k])) } }   # 빈 -C 는 git 도 무시한다
            elseif ($opt -ceq '-c') { if ($a[$k] -match '^remote\..+\.(mirror($|=(?!(false|no|off|0+|0x0+|)$))|push=\+)') { $forceConfig = $true } }
            elseif ($opt -eq '--git-dir') { $where.Add("--git-dir=$(ConvertTo-WinPath $a[$k])") }
            $k++
        }
        elseif ($opt -match '^--git-dir=(.+)$') { $where.Add("--git-dir=$(ConvertTo-WinPath $Matches[1])") }
    }
    if ($k -ge $a.Count) { return $null }
    $sub = $a[$k].ToLowerInvariant()
    $rest = @($a | Select-Object -Skip ($k + 1))
    $dash = [Array]::IndexOf([string[]]$rest, '--')
    $opts = @(if ($dash -lt 0) { $rest } else { $rest | Select-Object -First $dash })   # -- 앞

    if ($sub -eq 'clean') { return 'git clean 은 금지선이다 (CLAUDE.md §0 Git).' }
    if ($sub -eq 'reset') {
        if ($opts -cmatch '^--h(a(rd?)?)?$') { return 'git reset --hard 는 금지선이다 (CLAUDE.md §0 Git).' }
    }
    elseif ($sub -eq 'push') {
        if ($forceConfig) { return '-c remote.*.mirror, push=+ 는 강제 푸시다. 금지선이다 (CLAUDE.md §0 Git).' }
        foreach ($o in $opts) {
            if ($o -cmatch '^--(forc|force|force-w.*|mi.*)$' -or (Test-ShortFlag $o 'f' 'o')) { return '강제 푸시는 금지선이다 (CLAUDE.md §0 Git).' }
        }
        foreach ($o in $rest) {
            if ($o.StartsWith('+')) { return '앞에 + 를 붙인 refspec 은 강제 푸시다. 금지선이다 (CLAUDE.md §0 Git).' }
        }
    }
    elseif ($sub -eq 'checkout' -or $sub -eq 'switch') {
        foreach ($o in $opts) {
            if ($o -cmatch '^--(f(o(r(ce?)?)?)?|di.*)$' -or (Test-ShortFlag $o 'f' 'bBcC')) {
                return "git $sub $o 는 작업 트리 변경을 버린다. 금지선이다 (CLAUDE.md §0 Git)."
            }
            # 다른 워크트리가 쓰는 브랜치를 함께 체크아웃한다. checkout 은 --ignore-skip-worktree-bits 와 겹치지 않는 --ignore-o 부터 본다.
            $minLen = if ($sub -eq 'checkout') { 10 } else { 3 }
            if ($o.Length -ge $minLen -and '--ignore-other-worktrees'.StartsWith($o, [System.StringComparison]::Ordinal)) {
                return "git $sub --ignore-other-worktrees 는 다른 워크트리가 쓰는 브랜치를 겹쳐 쓴다. 금지선이다 (CLAUDE.md §0 Git, docs/RULES/BRANCHING.md §9-4)."
            }
        }
        if ($sub -eq 'checkout') {
            foreach ($o in $rest) {
                if (Test-WholeTree $o) { return 'git checkout . 은 작업 트리 변경을 버린다. 금지선이다 (CLAUDE.md §0 Git).' }
            }
        }
    }
    elseif ($sub -eq 'restore') {
        # --staged 만 준 경우(-W/--worktree 없이)는 인덱스만 되돌리므로 통과시킨다.
        $staged = $false; $worktree = $false
        foreach ($o in $opts) {
            if ($o -cmatch '^--st' -or (Test-ShortFlag $o 'S' 's')) { $staged = $true }
            if ($o -cmatch '^--w' -or (Test-ShortFlag $o 'W' 's')) { $worktree = $true }
        }
        if (-not $staged -or $worktree) {
            foreach ($o in $rest) {
                if (Test-WholeTree $o) { return 'git restore . 는 작업 트리 변경을 버린다. 금지선이다 (CLAUDE.md §0 Git).' }
            }
        }
    }
    elseif ($sub -eq 'worktree') {
        $verb = if ($rest.Count -gt 0) { $rest[0].ToLowerInvariant() } else { '' }
        $wopts = @($rest | Select-Object -Skip 1)
        if ($verb -eq 'add' -or $verb -eq 'remove') {
            # 왼쪽부터 읽는다. 값을 받는 옵션(-b·-B·--reason)의 다음 낱말은 값이다 — git 은 '-' 로 시작해도, '--' 여도 값으로 가져간다
            # (--reason --no-checkout, --reason -- --checkout). 값으로 먹히지 않은 '--' 에서만 옵션 해석을 끝낸다.
            # 마지막에 준 --checkout / --no-checkout 이 이긴다. --no-checkout 은 줄여 쓰지 않은 그대로만 센다.
            $noCheckout = $false
            $ord = [System.StringComparison]::Ordinal
            for ($i = 0; $i -lt $wopts.Count; $i++) {
                $o = $wopts[$i]
                if ($o -eq '--') { break }
                if (-not $o.StartsWith('-') -or $o -eq '-') { continue }   # 경로·커밋
                if ($o.StartsWith('--')) {
                    $name = ($o -split '=', 2)[0]
                    if ($name.Length -ge 3 -and '--force'.StartsWith($name, $ord)) {
                        return "git worktree $verb $o 는 강제 실행이다. 금지선이다 (CLAUDE.md §0 Git, docs/RULES/BRANCHING.md §9-4)."
                    }
                    if ($verb -ne 'add') { continue }
                    if ($name -ceq '--no-checkout') { $noCheckout = $true }
                    elseif ($name.Length -ge 3 -and '--checkout'.StartsWith($name, $ord)) { $noCheckout = $false }
                    elseif ($name.Length -ge 5 -and '--reason'.StartsWith($name, $ord) -and -not $o.Contains('=')) { $i++ }
                    continue
                }
                if (Test-ShortFlag $o 'f' 'bB') {
                    return "git worktree $verb $o 는 강제 실행이다. 금지선이다 (CLAUDE.md §0 Git, docs/RULES/BRANCHING.md §9-4)."
                }
                if ($verb -ne 'add') { continue }
                if (Test-ShortFlag $o 'B' 'b') {
                    return "git worktree add -B 는 있는 브랜치를 강제로 옮긴다(branch --force). -b 로 새 브랜치를 만든다 (CLAUDE.md §0 Git, docs/RULES/BRANCHING.md §9-4)."
                }
                $chars = $o.Substring(1)
                $at = $chars.IndexOfAny([char[]]'bB')
                if ($at -eq $chars.Length - 1) { $i++ }   # -b 다음 낱말이 브랜치 이름
            }
            if ($verb -eq 'add' -and -not $noCheckout) {
                return 'git worktree add 는 --no-checkout 으로만 만든다. 그렇지 않으면 Assets/·LFS 전체를 체크아웃한다 (CLAUDE.md §0 Git, docs/RULES/BRANCHING.md §9-4).'
            }
        }
    }
    elseif ($sub -eq 'sparse-checkout') {
        $verb = if ($rest.Count -gt 0) { $rest[0].ToLowerInvariant() } else { '' }
        $sargs = @($rest | Select-Object -Skip 1)
        # 읽기·도움말만 그냥 지나간다. 그 밖의 '-' 로 시작하는 낱말(PowerShell 의 --% 등)은 하위 명령을 가릴 수 있어 막는다.
        if ($verb -eq '' -or $verb -eq 'list' -or $verb -eq 'check-rules' -or $verb -eq '-h' -or $verb -eq '--help') { return $null }
        if ($verb.StartsWith('-')) {
            return "git sparse-checkout 뒤의 '$($rest[0])' 은 하위 명령 자리라 판정할 수 없어 막는다 (docs/RULES/BRANCHING.md §9-4)."
        }
        if ($verb -eq 'disable' -or $verb -eq 'add') {
            return "git sparse-checkout $verb 은 승인 목록 밖을 체크아웃한다. 금지선이다 (CLAUDE.md §0 Git, docs/RULES/BRANCHING.md §9-4)."
        }
        if ($verb -ne 'init' -and $verb -ne 'set' -and $verb -ne 'reapply') {
            return "git sparse-checkout $verb 은 판정할 수 없는 하위 명령이라 막는다 (docs/RULES/BRANCHING.md §9-4)."
        }
        if (-not $plain) {
            return "git sparse-checkout $verb 은 다른 명령·대입·감싸는 명령 없이 git 하나만 있는 호출로만 받는다. 앞에서 바꾼 셸 상태(환경 변수·현재 폴더)를 훅이 모른다 (docs/RULES/BRANCHING.md §9-4)."
        }
        if ($override) {
            return "git sparse-checkout $verb 에 --git-dir·--work-tree·-c 나 GIT_* 환경 지정이 있어 대상 작업 트리를 판정할 수 없다. 대상 트리 루트에서 -C <트리> 만 준다 (docs/RULES/BRANCHING.md §9-4)."
        }
        try { $target = Get-SparseTarget $cwd $where.ToArray() }
        catch { $target = $_.Exception.Message }
        if ($target -eq 'main') {
            return "메인 워크트리에서 git sparse-checkout $verb 을 하면 Assets/ 가 디스크에서 사라진다. 금지선이다 (CLAUDE.md §0 Git, docs/RULES/BRANCHING.md §9-4)."
        }
        if ($target) { return "git sparse-checkout $verb 을 판정하지 못해 막는다: $target (docs/RULES/BRANCHING.md §9-4)." }
        # 비교는 모두 서수(Ordinal)다. -ceq 는 문화권 비교라 보이지 않는 글자(U+00AD 등)를 무시해 'do­cs' 를 'docs' 로 본다.
        $ord = [System.StringComparison]::Ordinal
        if ($verb -eq 'init') {
            if ($sargs.Count -ne 1 -or -not [string]::Equals($sargs[0], '--cone', $ord)) {
                return "git sparse-checkout init 은 '--cone' 하나만 준다. 받은 인자: $($sargs -join ' ') (docs/RULES/BRANCHING.md §9-4)."
            }
        }
        elseif ($verb -eq 'set') {
            # 낱말 하나하나가 승인 목록의 한 항목과 같아야 한다(개수와 원소를 비교 — 한 낱말 안에 이름 다섯을 넣은 것도 막는다).
            $paths = @($sargs | Where-Object { -not [string]::Equals($_, '--cone', $ord) })
            $extra = $sargs.Count - $paths.Count
            $got  = [string[]]@($paths); [Array]::Sort($got, [System.StringComparer]::Ordinal)
            $want = [string[]]@($script:sparseSet); [Array]::Sort($want, [System.StringComparer]::Ordinal)
            $same = ($got.Count -eq $want.Count)
            for ($j = 0; $same -and $j -lt $want.Count; $j++) { if (-not [string]::Equals($got[$j], $want[$j], $ord)) { $same = $false } }
            if ($extra -gt 1 -or -not $same) {
                return "git sparse-checkout set 의 인자는 승인 목록($($script:sparseSet -join ' '))과 낱말마다 정확히 같아야 한다. 받은 인자: $($sargs -join ' ') (docs/RULES/BRANCHING.md §9-4)."
            }
            if ($extra -eq 0 -and -not [string]::Equals($script:sparseCone, 'true', $ord)) {
                return "그 트리가 cone 모드(core.sparseCheckoutCone=true)가 아니다(값: '$($script:sparseCone)'). init --cone 을 먼저 하거나 set --cone 으로 준다 (docs/RULES/BRANCHING.md §9-4)."
            }
        }
        else {
            if ($sargs.Count -gt 0) {
                return "git sparse-checkout reapply 에는 인자를 주지 않는다. 받은 인자: $($sargs -join ' ') (docs/RULES/BRANCHING.md §9-4)."
            }
            # reapply 는 저장된 패턴을 그대로 다시 적용한다. cone 모드이고 저장된 목록이 승인 목록과 서수로 같을 때만 받는다.
            if (-not [string]::Equals($script:sparseCone, 'true', $ord)) {
                return "그 트리가 cone 모드가 아니라 reapply 를 막는다. 승인 목록으로 set 을 다시 한다 (docs/RULES/BRANCHING.md §9-4)."
            }
            $lr = Invoke-GitQuery (@('-C', $cwd.Replace('\', '/')) + $where.ToArray() + @('sparse-checkout', 'list'))
            if ($lr.Code -ne 0) { return "저장된 sparse 목록을 읽지 못해 reapply 를 막는다: $($lr.Err) (docs/RULES/BRANCHING.md §9-4)." }
            $stored = [string[]]@($lr.Out -split "`r?`n" | Where-Object { $_ -ne '' }); [Array]::Sort($stored, [System.StringComparer]::Ordinal)
            $want = [string[]]@($script:sparseSet); [Array]::Sort($want, [System.StringComparer]::Ordinal)
            $same = ($stored.Count -eq $want.Count)
            for ($j = 0; $same -and $j -lt $want.Count; $j++) { if (-not [string]::Equals($stored[$j], $want[$j], $ord)) { $same = $false } }
            if (-not $same) {
                return "저장된 sparse 목록($($stored -join ' '))이 승인 목록과 달라 reapply 를 막는다. 승인 목록으로 set 을 다시 한다 (docs/RULES/BRANCHING.md §9-4)."
            }
        }
    }
    elseif ($sub -eq 'commit') {
        # -C 나 --git-dir 이 가리키는 곳의 브랜치를 본다. 변수가 들어 있으면 훅이 풀 수 없어 cwd 로 판정한다.
        if (($where -join ' ') -match '[$%]') { $where.Clear() }
        $branch = Get-Branch (@('-C', $cwd.Replace('\', '/')) + $where.ToArray())
        if ($branch -eq 'main' -or $branch -eq 'dev') {
            return "커밋할 브랜치가 $branch 이다. main/dev 에 직접 커밋하지 않는다. 브랜치부터 만든다 (CLAUDE.md §0 Git, §9-2)."
        }
    }
    return $null
}

# 명령 문자열에서 금지선에 걸리는 git 을 찾아 사유를 돌려준다. 없으면 $null.
function Get-ShellReason([string]$cmd, [bool]$ps, [string]$cwd, [int]$depth) {
    if ($depth -gt 8) { throw '명령을 감싼 깊이가 8을 넘었다.' }
    $cmds = New-Object System.Collections.Generic.List[object]
    [void](Read-Commands ($cmd -replace "`r`n?", "`n") 0 '' $ps $cmds)
    # 다른 명령·대입·감싸는 명령 없이 git 하나만 있는 호출인가. sparse-checkout 변경은 이런 호출만 받는다
    # (Set-Item Env:GIT_WORK_TREE, cd 처럼 앞에서 바꾼 셸 상태를 훅은 모른다).
    $solo = ($depth -eq 0 -and $cmds.Count -eq 1)
    foreach ($words in $cmds) {
        Assert-Time
        $w =New-Object System.Collections.Generic.List[string]
        $w.AddRange([string[]]$words)
        $gitDir = ''
        $plain = $solo
        # 명령 앞의 예약어, A=b 대입, PowerShell 의 $x = 을 건너뛴다. 하나라도 있으면 단독 호출이 아니다.
        if ($w.Count -gt 0 -and ($w[0] -match '^(if|then|else|elif|do|while|until|!|\{|time|return|coproc)$' -or $w[0] -match '^[A-Za-z_]\w*\+?=' -or $w[0] -match '^\$')) { $plain = $false }
        while ($w.Count -gt 0) {
            if ($w[0] -match '^(if|then|else|elif|do|while|until|!|\{|time|return|coproc)$') { $w.RemoveAt(0) }
            elseif ($w[0] -match '^[A-Za-z_]\w*\+?=') {
                if ($w[0] -cmatch '^GIT_DIR=(.+)$') { $gitDir = $Matches[1] }
                $w.RemoveAt(0)
            }
            elseif ($w[0] -match '^\$[\w:{}]+$' -and $w.Count -gt 1 -and $w[1] -match '^\+?=$') { $w.RemoveRange(0, 2) }
            elseif ($w[0] -match '^\$[\w:]+\+?=(.*)$') { $w[0] = $Matches[1]; if ($w[0] -eq '') { $w.RemoveAt(0) } }
            else { break }
        }
        if ($w.Count -eq 0) { continue }
        $name = Get-CommandName $w[0]
        # 다른 명령을 실행해 주는 명령이면 그 뒤에서 처음 나오는 git 이나 셸부터 본다.
        if ($name -match '^(env|sudo|command|exec|builtin|nohup|nice|timeout|xargs|winpty|stdbuf)$') {
            $at = -1
            for ($k = 1; $k -lt $w.Count -and $at -lt 0; $k++) {
                if ($w[$k] -cmatch '^GIT_DIR=(.+)$') { $gitDir = $Matches[1] }
                $inName = Get-CommandName $w[$k]
                if ($inName -eq 'git' -or $inName -match $script:shells) { $at = $k }
            }
            if ($at -lt 0) { continue }
            $plain = $false
            $w.RemoveRange(0, $at)
            $name = Get-CommandName $w[0]
        }
        $reason = $null
        if ($name -eq 'git') {
            $reason = Get-GitReason ([string[]]@($w | Select-Object -Skip 1)) $cwd $gitDir $plain
        }
        else {
            # 문자열을 명령으로 실행하는 것: 예전 판정은 이 안의 "; git ..." 도 잡았다.
            $inner = ''; $innerPs = $ps
            for ($k = 1; $k -lt $w.Count; $k++) {
                if ($name -match '^(bash|sh|dash|zsh|ksh)$' -and $w[$k] -cmatch '^-[A-Za-z]*c[A-Za-z]*$') {
                    # -c 뒤에도 옵션(-e, --, -o 이름)이 올 수 있다. 옵션을 지나 처음 나오는 낱말이 명령이고, 그 뒤는 $0 $1 이다.
                    $j = if ($w[$k] -cmatch '[oO]') { $k + 2 } else { $k + 1 }
                    while ($j -lt $w.Count -and $w[$j] -match '^[-+]') {
                        if ($w[$j] -eq '--' -or $w[$j] -eq '-') { $j++; break }
                        if ($w[$j] -cmatch '^([-+][A-Za-z]*[oO]|--rcfile|--init-file)$') { $j++ }
                        $j++
                    }
                    if ($j -lt $w.Count) { $inner = $w[$j]; $innerPs = $false }
                    break
                }
                if (($name -match '^(pwsh|powershell)$' -and $w[$k] -match '^[-/]c(o[a-z]*)?$') -or ($name -eq 'cmd' -and $w[$k] -match '^/{1,2}[ck]$')) {
                    $inner = @($w | Select-Object -Skip ($k + 1)) -join ' '; $innerPs = $true
                    break
                }
            }
            if ($name -match '^(eval|iex|invoke-expression)$') { $inner = @($w | Select-Object -Skip 1) -join ' ' }
            if ($inner) { $reason = Get-ShellReason $inner $innerPs $cwd ($depth + 1) }
        }
        if ($reason) { return $reason }
    }
    return $null
}

try {
    # 표준입력을 UTF-8 로 읽는다. 기본 코드페이지로 읽으면 한글 경로가 깨진다.
    $stdin = New-Object System.IO.StreamReader([Console]::OpenStandardInput(), (New-Object System.Text.UTF8Encoding($false)))
    $raw = $stdin.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { throw '표준입력이 비었다.' }
    $in = $raw | ConvertFrom-Json

    $script:IsAntigravity = ($null -ne $in.PSObject.Properties['toolCall'])
    $tool = if ($script:IsAntigravity) { [string]$in.toolCall.name } else { [string]$in.tool_name }
    $cwd  = [string]$in.cwd
    if ([string]::IsNullOrWhiteSpace($cwd) -and $script:IsAntigravity -and $in.workspacePaths -and $in.workspacePaths.Count -gt 0) {
        $cwd = [string]$in.workspacePaths[0]
    }
    if ([string]::IsNullOrWhiteSpace($cwd)) { $cwd = (Get-Location).Path }

    if ($tool -eq 'Bash' -or $tool -eq 'PowerShell' -or ($script:IsAntigravity -and $tool -eq 'run_command')) {
        $cmd = if ($script:IsAntigravity) { [string]$in.toolCall.args.CommandLine } else { [string]$in.tool_input.command }

        if ($script:IsAntigravity -and [string]::IsNullOrWhiteSpace($cmd)) {
            Write-Decision 'deny' 'run_command 인데 CommandLine 이 비어 판정할 수 없다. 입력 형식을 확인하라 (tools/hooks/guard.ps1).'; exit 0
        }

        # 글자 모양이 아니라 명령 단위로 본다: 명령을 나누고 낱말로 풀어 git 인자를 판정한다.
        # Claude 의 Bash 는 bash 로, PowerShell 은 PowerShell 로 읽는다. Codex·Antigravity 는 어느 셸인지 입력에 없어
        # 둘 다로 읽고 하나라도 걸리면 막는다.
        if (($cmd -replace '["''`\\^]', '') -match 'git') {
            # 같은 명령 안에서 git 대상·설정을 바꾸는 환경 변수(GIT_WORK_TREE=…, $env:GIT_DIR=…, export GIT_…)를 쓰는가.
            $script:GitEnvInCommand = ($cmd -match '(?i)\$env:GIT_|\bGIT_[A-Z_]+\s*=')
            $isClaude = -not [string]::IsNullOrEmpty($env:CLAUDE_PROJECT_DIR) -and $null -eq $in.PSObject.Properties['turn_id']
            $modes = if ($tool -eq 'PowerShell') { @($true) } elseif ($tool -eq 'Bash' -and $isClaude) { @($false) } else { @($false, $true) }
            # 커밋할 곳을 못 읽은 것은 모든 읽기에서 그럴 때만 막는다. 다른 셸로 읽어서 생긴 잘못된 경로(.\Assets → .Assets)일 수 있다.
            $failed = @()
            foreach ($ps in $modes) {
                $script:lookupError = $null
                try { $reason = Get-ShellReason $cmd $ps $cwd 0 }
                catch { $reason = "git 명령을 판정하지 못해 막는다: $($_.Exception.Message) (tools/hooks/guard.ps1)." }
                if ($reason) { Write-Decision 'deny' $reason; exit 0 }
                if ($script:lookupError) { $failed += $script:lookupError }
            }
            if ($failed.Count -eq $modes.Count) {
                Write-Decision 'deny' "git 명령을 판정하지 못해 막는다: $($failed[0]) (tools/hooks/guard.ps1)."; exit 0
            }
        }
        Write-PassThrough
        exit 0
    }

    if ($script:IsAntigravity -and ($tool -eq 'edit_file' -or $tool -eq 'write_file')) {
        $path = [string]$in.toolCall.args.Path
        if ([string]::IsNullOrWhiteSpace($path)) {
            Write-Decision 'deny' "$tool 인데 Path 가 비어 판정할 수 없다. 입력 형식을 확인하라 (tools/hooks/guard.ps1)."; exit 0
        }

        $reason = Get-ProtectedReason $path $cwd
        if ($reason) {
            Write-Decision 'deny' "$reason. 승인이 있으면 사용자에게 에디터에서 하거나 직접 고치도록 청한다."
            exit 0
        }
        Write-PassThrough
        exit 0
    }

    if ($tool -eq 'Edit' -or $tool -eq 'Write' -or $tool -eq 'NotebookEdit') {
        $path = [string]$in.tool_input.file_path
        if ([string]::IsNullOrWhiteSpace($path)) { $path = [string]$in.tool_input.notebook_path }
        if ([string]::IsNullOrWhiteSpace($path)) { exit 0 }

        $root = $env:CLAUDE_PROJECT_DIR
        if ([string]::IsNullOrWhiteSpace($root)) { $root = $cwd }

        $reason = Get-ProtectedReason $path $root
        if ($reason) {
            $decision = if ($script:MustDeny) { 'deny' } else { 'ask' }
            Write-Decision $decision "$reason."
        }
        exit 0
    }

    # Codex 파일 편집. 패치 한 개가 여러 파일을 고칠 수 있어 머리줄의 경로를 모두 본다.
    if ($tool -eq 'apply_patch') {
        $patch = [string]$in.tool_input.command
        if ([string]::IsNullOrWhiteSpace($patch)) { throw 'apply_patch 인데 tool_input.command 가 비었다.' }

        $paths = @([regex]::Matches($patch, '(?m)^\*\*\* (?:Add File|Update File|Delete File|Move to): (?<p>.+?)\s*$') |
                   ForEach-Object { $_.Groups['p'].Value })
        # 머리줄을 못 찾으면 무엇을 고치는지 판정할 수 없다. 그냥 두면 보호 파일도 지나가므로 막는다.
        if ($paths.Count -eq 0) {
            Write-Decision 'deny' 'apply_patch 의 패치 머리줄(*** Add/Update/Delete File:, *** Move to:)에서 경로를 찾지 못해 판정할 수 없다. 머리줄 형식을 확인하라 (tools/hooks/guard.ps1).'; exit 0
        }

        foreach ($path in $paths) {
            $reason = Get-ProtectedReason $path $cwd
            if ($reason) {
                Write-Decision 'deny' "$reason. 승인이 있으면 사용자에게 에디터에서 하거나 직접 고치도록 청한다."; exit 0
            }
        }
        exit 0
    }

    Write-PassThrough
    exit 0
}
catch {
    # 훅이 조용히 죽으면 보호가 없는 줄 모르고 진행한다. 원문을 남기고 비차단 오류(exit 1)로 알린다.
    [Console]::Error.WriteLine("guard.ps1 오류: $($_.Exception.Message)")
    exit 1
}
