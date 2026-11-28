# 브랜치 이름과 주의점 — CLAUDE.md §9-1 · §9-3 · §9-4

> **CLAUDE.md 의 일부다. 구속력은 CLAUDE.md 와 같다.** § 번호는 CLAUDE.md 의 번호를 그대로 쓴다.
> **읽는 시점**: 브랜치를 만들거나 병합하기 전, 워크트리를 만들거나 지우기 전.
> 브랜치 구조와 **규칙(§9-2: 직접 커밋 금지, `--no-ff`, 병합 조건)** 은 CLAUDE.md §9 에 있다.
> 이 내용을 고칠 때는 이 파일만 고친다. CLAUDE.md 에 사본을 두지 않는다.

### 9-1. 이름

| 접두사 | 용도 | 예 |
|---|---|---|
| `feature/` | 기능 | `feature/p1-fixed-tick` |
| `fix/` | 버그 수정 | `fix/grapple-release-boost` |
| `docs/` | 문서·규칙 | `docs/branch-strategy` |
| `spike/` | 버려도 되는 실험 | `spike/netcode-ngo-vs-fusion` |

### 9-3. 알아둘 것

- 에디터가 열린 채로 브랜치를 전환하면 재임포트가 일어난다. 전환은 에디터가 한가할 때 한다.
- Unity 가 자동으로 바꾸는 파일(`ProjectSettings/`, `ProtoHarness.slnx`, `.codex` 줄바꿈)은 브랜치 전환 때 같이 따라온다.
- `.gitattributes` 는 `.unity/.prefab/.asset/.mat` 에 `merge=unityyamlmerge` 를 지정하지만 git config 에 병합 드라이버가 **등록되어 있지 않다.** 임시 저장소 실험(2026-10-02)에서 미등록 드라이버는 기본 텍스트 병합으로 되돌아가 **일반 충돌 마커를 남겼다.** 그래서 씬·프리팹이 충돌하면 마커를 손으로 고치지 않고 병합을 중단(`git merge --abort`)한 뒤 에디터에서 다시 작업한다. 드라이버 등록은 검증 없이 하지 않는다.
- GitHub 브랜치 보호는 설정하지 않았다 (2026-10-02 결정, 단독 개발).

### 9-4. 비-Unity sparse worktree

`docs/DECISIONS.md` 2026-11-28 "비-Unity sparse worktree". Unity 에디터를 여는 메인 작업 트리 하나는 그대로 두고, Unity 가 읽지 않는 작업만 별도 작업 트리에서 한다. 금지 명령은 CLAUDE.md §0 Git, 실행 제한은 §0 실행에 있다. `tools/hooks/guard.ps1` 이 아래 2·3 의 git 명령을 판정한다.

1. **허용 범위** — 아래 경로만 바꾸는 브랜치.
   - `docs/`, `tools/`, `.claude/`, `.codex/`, `.agents/`, 루트 문서(`CLAUDE.md`, `AGENTS.md`, `README.md`).
   - 단 `Assets/` 를 읽는 `tools/select-tests.ps1`·`tools/reindex.ps1`·`tools/test-runs.ps1` 의 수정·실행과 `index/` 작업은 메인 전용이다. sparse 트리에는 `Assets/` 가 없어 조용히 틀린 답을 내고, CLAUDE.md §9-2 "도구를 실제로 실행한 결과" 를 낼 수 없다.
   - 함께 체크아웃되는 루트의 `.gitignore`·`.gitattributes` 는 CLAUDE.md §3-1 환경 기준 합의 없이 건드리지 않는다. `ProtoHarness.slnx` 는 열지 않는다(가리키는 `*.csproj` 가 `.gitignore` 대상이라 트리에 없다).
   - `Assets/`, `Packages/`, `ProjectSettings/`, `art-src/`, `.vscode/` 에 닿는 작업은 메인 작업 트리에서 직렬로 한다.
2. **만드는 때와 자리** — 사용자가 지시하거나 병렬·격리 작업에 합의했을 때만 만든다. 첫 `sparse-checkout init` 이 공유 `.git/config` 에 `extensions.worktreeConfig=true` 를 쓰고, 워크트리를 다 지워도 남는다(그대로 둔다. 되돌리려면 모든 워크트리를 지운 뒤 사용자가 `git config --unset extensions.worktreeConfig`). 자리는 메인 리포 폴더 **바깥**의 형제 경로(예: `../ProtoHarness-wt/<짧은이름>`)다.
3. **만드는 절차** — `--no-checkout` 으로 만들고 cone 목록을 정확히 맞춘 뒤 체크아웃한다. 단계마다 종료 코드를 본다.
   ```powershell
   function Invoke-CheckedGit([string[]]$a) { & git @a; if ($LASTEXITCODE -ne 0) { throw "git $($a -join ' ') 실패 (exit $LASTEXITCODE)" } }
   Invoke-CheckedGit @('worktree', 'add', '--no-checkout', '-b', '<브랜치>', '<경로>', 'dev')
   Invoke-CheckedGit @('-C', '<경로>', 'sparse-checkout', 'init', '--cone')
   Invoke-CheckedGit @('-C', '<경로>', 'sparse-checkout', 'set', 'docs', 'tools', '.claude', '.codex', '.agents')
   $sc = (@(& git -C '<경로>' sparse-checkout list) | Where-Object { $_ -ne '' } | Sort-Object) -join ','
   if ($LASTEXITCODE -ne 0 -or $sc -ne '.agents,.claude,.codex,docs,tools') { throw "sparse-checkout 목록 불일치: $sc" }
   Invoke-CheckedGit @('-C', '<경로>', 'checkout')
   foreach ($forbid in @('Assets','Packages','ProjectSettings','art-src')) {
       if (Test-Path (Join-Path '<경로>' $forbid)) { throw "금지 폴더가 체크아웃됨: $forbid" }
   }
   ```
   - `<경로>` 는 글자 그대로 적는다. `guard.ps1` 은 `-C $wt` 처럼 변수인 경로를 어느 트리인지 판정하지 못해 `sparse-checkout` 을 막는다. 위 함수처럼 스크립트 안에서 부르면 훅이 보지 못하므로, Claude·Codex·Antigravity 는 명령을 하나씩 셸 도구로 실행한다.
   - `sparse-checkout init`·`set`·`reapply` 는 **셸 호출 하나에 그 git 명령 하나만** 둔다. `; echo …`·대입·`Set-Item Env:…`·`cd`·`env`/`xargs` 같은 감싸는 명령이 같이 있으면 훅이 막는다 — 앞에서 바꾼 셸 상태를 훅이 모른다. `worktree add` 와 그 트리의 `sparse-checkout` 도 따로 부른다(훅은 실행 전에 판정하므로 아직 없는 트리를 막는다).
   - 목록(`docs`, `tools`, `.claude`, `.codex`, `.agents`)을 줄이거나 늘리지 않는다. 훅·규칙·에이전트 정의가 이 다섯에 있다. `sparse-checkout set` 의 인자가 낱말마다 이 다섯과 같지 않으면(부분집합·추가·중복·한 낱말에 묶음·`--no-cone`·`--stdin`), 또는 cone 모드가 아닌 트리면 훅이 막는다. `init` 은 `--cone` 하나만 준다.
   - `sparse-checkout init`·`set`·`reapply` 는 등록된 linked 트리의 **루트**에서 `-C <경로>` 만 주고 부른다. 메인 트리, 하위 폴더(git 이 인자에 접두사를 붙인다), `--git-dir`·`--work-tree`·`-c`·`GIT_*` 환경 지정이 있으면 훅이 막는다 — 메타데이터와 작업 경로를 따로 주면 어느 트리가 바뀌는지 판정할 수 없다.
   - `worktree add` 의 `-B`(있는 브랜치를 강제로 옮긴다)도 막는다. 새 브랜치는 `-b` 로 만든다. `--reason` 처럼 값을 받는 옵션 뒤의 낱말은 값으로 보고 `--no-checkout` 으로 세지 않는다.
   - `docs/images/` 의 LFS 파일 12개(21,564,683 B)는 메인 `.git/lfs/objects/` 캐시에서 네트워크 없이 채워진다(2026-11-28 실측). sparse 트리의 `git lfs ls-files` 는 cone 밖 파일까지 175개를 다 나열하니, 확인할 때는 `-I 'docs/images/**'` 로 좁힌다.
   - **중간에 실패하면** 그 자리에서 멈추고 `git worktree list`·`git branch --list <브랜치>` 로 이 실행이 만든 등록·브랜치인지, 트리 안에 미추적·무시 파일이 있는지 확인한다. 이 실행이 만든 것일 때만 일반 `git worktree remove <경로>` + `git branch -d <브랜치>` 로 정리한다. `--no-checkout` 뒤 `checkout` 전에 멈춘 트리는 색인이 비어 일반 `remove` 가 `contains modified or untracked files` 로 거부한다 — `init --cone` → 승인 목록 `set` → `checkout` 을 마저 한 뒤 일반 `remove` 한다. 그래도 일반 `remove` 가 실패하면 `-f` 를 쓰지 않고 그 상태로 멈춰 사용자에게 보고한다(CLAUDE.md §0 Git).
4. **병합·충돌·정리**
   - 병합 전에 `git worktree list` 로 `dev` 가 어디에 체크아웃돼 있는지 보고, `$baseDev = git rev-parse dev`, `$featHead = git rev-parse <기능브랜치>` 를 고정해 둔다. 병합은 CLAUDE.md §9-2 대로 사용자가 시킬 때만, `--no-ff` 와 `HANDOFF:`·`구현:`·`검토:` 줄을 붙인다.
     - **경우 A** (`dev` 가 메인에 체크아웃됨): 메인 작업 트리가 깨끗하고 에디터가 한가할 때(§9-3) 메인에서 병합한다.
     - **경우 B** (메인은 다른 기능 브랜치이고 `dev` 는 어느 트리에도 없음): 메인 브랜치를 바꾸지 않는다(재임포트). sparse 트리 안에서 `git switch dev` → `git merge --no-ff <브랜치>` 하고, 끝나면 그 트리를 지우거나 다른 브랜치로 돌려 `dev` 점유를 푼다.
     - **경우 C** (`dev` 가 제3의 linked worktree 에 있음): 멈추고 `git worktree list` 출력을 붙여 사용자에게 보고한다.
   - **허용 범위 밖 변경 확인** — 커밋 전: `git status --porcelain=v1 -uall`, `git diff --name-only --cached dev`, `git diff --name-only dev` 세 가지. 병합 전(기능 브랜치): `git diff --name-only "$baseDev...$featHead"`. 병합 직후(`dev`): `git diff --name-only "$baseDev" HEAD`. 셋 다 1 의 범위 밖 경로가 0건이어야 한다.
   - **sparse 트리에서 `merge`·`rebase` 를 했으면** 그 전후에 `Assets/`·`Packages/`·`ProjectSettings/`·`art-src/` 가 없는지 본다. 충돌을 보여 주려고 cone 밖 경로를 꺼낼 수 있다(경우 A 의 메인에는 해당하지 않는다).
   - **두 트리에서 `docs/HANDOFF.md`·`docs/DECISIONS.md` 를 고쳤으면** 기능 브랜치에 최신 `dev` 를 먼저 병합해 거기서 충돌을 푼다(`docs/HANDOFF.md` 4절 "`dev` 병합"). 푼 내용, `git diff "$baseDev...$featHead"`, 리뷰받은 SHA, 병합 뒤 `git diff "$baseDev" HEAD` 를 대조한다.
   - **지울 때** — 병합(또는 사용자의 폐기 승인) 뒤 미추적·무시 파일, 열린 세션, 병합하지 않은 커밋이 없는지 보고 일반 `git worktree remove <경로>` 로 지운 뒤 `git worktree list` 로 확인한다(일반 `remove` 도 트리 안의 무시 파일은 함께 지운다). 사라진 경로는 `prune`, 보호는 `lock`, 옮기기·복구는 `move`·`repair` 다. 옮기면 `tools/agy-run.ps1` 의 `-Resume` 저장소 대조와 `tools/harness-cost.py` 의 기록 폴더가 달라진다.
5. **트리 사이의 제약과 래퍼**
   - 다른 워크트리의 파일을 절대 경로로 고치지 않는다. `guard.ps1` 은 등록된 모든 워크트리 루트를 떼고 보호 파일을 판정한다(목록을 못 읽으면 막는다). 셸로 쓰는 것은 여전히 판정하지 못한다.
   - `tools/agy-run.ps1` 은 공용 git 디렉터리(`--git-common-dir`)의 `config`·`info/exclude`·`hooks` 와 트리별 `config.worktree`, linked 트리의 `.git` 파일을 대조한다. 어느 트리에서든 `agy-run` 이 도는 동안에는 그 실행의 승인된 `-Allow` 쓰기 밖에서 작업 트리 수정·커밋·ref 변경을 하지 않는다 — ref 는 모든 트리가 공유한다(`docs/RULES/SUBAGENTS.md` §7-6). `agy-run` 은 `Local\protoharness-agent-relay` 잠금도 잡으므로 트리가 달라도 `agent-relay` 와 동시에 돌지 않는다.
   - `tools/agent-relay.ps1` 은 트리마다 다른 `-RunId` 를 쓴다(산출물 폴더가 `%TEMP%` 하나다). `tools/codex-unity.ps1` 은 호출한 폴더에서 Codex 를 띄우므로 대상 트리에서 부르고 `-Unity`·`-Blender` 는 쓰지 않는다 — sparse 트리에서 써 본 적은 없다.
   - `tools/harness-cost.py` 의 기본 실행은 메인 경로의 세션 기록만 집계한다. 트리에서 연 세션은 따로 쌓인다.
   - 훅은 linked 트리에서도 돈다(2026-11-28 실측: Claude `-p`·`codex exec`·`agy -p` 가 그 트리의 `guard.ps1` 로 `git clean -n` 을 막았다. Codex 는 `/hooks` 를 다시 신뢰하지 않아도 돌았다).
