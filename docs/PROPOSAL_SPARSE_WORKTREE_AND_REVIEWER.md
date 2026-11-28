# 비-Unity Sparse Worktree 규칙 + `unity-reviewer` 기준 초안 (§3-2 합의 요청서 초안, v4)

> 2026-11-28 갱신 · 1·2·3차 Claude·Codex 교차 검토 반영판(v4, 브랜치 `docs/sparse-worktree-and-reviewer`, 기준 `dev` `4f9ab25`).
> **승인 (2026-11-28, 사용자 "ㅇㅋ 진행해보자")**: Q1~Q5 모두 추천안 `(a)` 및 §3-2 범위 반영 승인.
> **진행 분담**: Antigravity 는 비보호 도구(`tools/sync-agents.ps1`, `tools/agy-run.ps1`) 보강·시험까지 수행하고, `tools/hooks/guard.ps1` 이 `deny` 로 막는 보호 하네스 파일(`CLAUDE.md`, `docs/RULES/`, `.claude/agents/`, `tools/hooks/`) 및 최종 실측·병합은 Claude Code(또는 사용자)가 이어서 수행한다(`docs/RULES/SUBAGENTS.md` §7-6).

---

## 0. 사용자가 정할 것

| # | 질문 | 선택지 | 추천 |
|---|---|---|---|
| Q1 | 비-Unity sparse worktree 의 체크아웃 방식 | **(a)** `--cone` (`docs`, `tools`, `.claude`, `.codex`, `.agents` + 루트 파일; `.vscode/` 미포함, `docs/images/` LFS 12개·21,564,683 B ≈ 20.57 MiB 포함) · **(b)** `--no-cone` 으로 `docs/images/` 까지 제외(`/*` 패턴을 쓰므로 `.vscode/` 도 명시적으로 제외: `/*`, `!/Assets/`, `!/Packages/`, `!/ProjectSettings/`, `!/art-src/`, `!/docs/images/`, `!/.vscode/`). **(b) 채택 시 생성 절차·`guard.ps1` 패턴 정확 일치 검사·필수 실측 ⑤ 를 `--no-cone` 규격(순서 포함 전체 패턴 일치, `docs/images/`·`.vscode/` 미체크아웃 확인, README 이미지는 메인에서 확인)으로 분기한다** | **(a)**. `--cone` 이 단순·고속이고 메인 LFS 캐시(12/12 일치 확인)를 재사용한다. 오프라인 smudge 여부는 첫 실측에서 확인한다(1-5) |
| Q2 | 비-Unity sparse worktree 생성 시점과 `extensions.worktreeConfig` 유지 | **(a)** 사용자가 지시하거나 병렬·격리 작업에 합의했을 때만 생성. 첫 `sparse-checkout init` 이 `.git/config` 에 남기는 `extensions.worktreeConfig=true` 는 유지(모든 워크트리 제거 후 원복을 원하면 `git config --unset extensions.worktreeConfig` 로 수동 복구) · **(b)** 비-Unity 작업에서 에이전트가 지시 없이 생성 | **(a)**. `sparse-checkout init` 이 공유 `.git/config` 를 바꾸므로(`CLAUDE.md` §3-1 환경 기준), 시범 안정화 전에는 사용자 지시·합의 시에만 쓴다 |
| Q3 | 훅·래퍼 보강 시점 (`guard.ps1`·`after-agents-edit.ps1`·`sync-agents.ps1`·`agy-run.ps1`) | **(a)** 규칙 반영과 같은 작업(또는 직전 선행 `fix/`)에서 함께 보강하고, 실제 linked worktree 1회 실측 통과 뒤 규칙을 발효한다 · **(b)** 규칙만 먼저 넣되, 훅 보강과 실측 완료 전까지 sparse worktree 실사용을 보류한다 | **(a)**. 현재 `guard.ps1` 은 다른 트리의 루트 기준 보호 파일(`CLAUDE.md`·`docs/RULES/`·`Packages/manifest.json`·`ProjectSettings/ProjectVersion.txt`)과 일반 `worktree add` 를 막지 못하고, `after-agents-edit.ps1` 은 엉뚱한 트리를 검사하며, `agy-run.ps1` 은 `.git` 감시를 조용히 건너뛴다(1-3) |
| Q4 | `unity-reviewer` 자동 파견 기준 (`Assets/` 변경 대상) | **(a)** **위험 우선 4단 분기(Tier 1~4)**: 어셈블리·코어 시뮬/결정성/프로토콜·대형 에디터 빌더(>100줄/신규)·테스트 단언/선택 변경·라이프사이클/시각 큰 변경(>100줄/새 `MonoBehaviour`)·타 계열 비소규모 구현만 파견. 그 밖은 대조 + 독립 교차 검토 · **(b)** 구현자에 `claude` 가 포함되면(단독·혼합 모두) `unity-reviewer` 자동 파견을 전부 끄고, `claude` 미포함 타 계열 구현만 Q5 대로 적용(사용자 지시 시에만 파견) | **(a)**. 28건 실측(부록 A)에서 시뮬·레이스·봇·트랙 11건 중 6건(단독 3건 포함), 아트 11건 중 3건(K4·STADIUM·FINGER 신규 빌더 594줄), UI·오디오 6건 중 3건에서 중대·실제 결함·주요 규칙 이슈가 나왔다. 최초 킷 등록으로 `select-tests.ps1` 을 건드린 브랜치(K1 1차·K3 1차·K4)는 계속 Tier 1 ⑤ 로 파견되지만, 후속 트랙 킷 반영(K1 2차·K3 2~4차·K11 등)과 프리팹 diff 위주·단순 테스트 추가 브랜치의 반복 비용을 줄인다 |
| Q5 | 타 계열(`codex`·`antigravity`) 구현의 비소규모 코드 변경 시 독립 Claude 검토 방식 (`SUBAGENTS.md` §7-6 정합) | **(a)** 2-2② 축 1 표의 **Q5 (a) 열** 채택(`CODE` > 30줄·신규/API 변경은 Tier 3 으로 자동 파견/relay, Tier 4 도 Antigravity/Codex 메인 세션은 실제 Claude 대조 또는 relay) · **(b)** 2-2② 축 1 표의 **Q5 (b) 열** 채택(현행 §7-6:128 유지 — 타 계열 구현은 Tier 무관 Claude 메인 대조, 파견은 사용자 지시 시에만) | **(a)**. 체크리스트 담당자(축 1)와 독립 교차 검토자(축 2)를 분리해 §7-5·§7-6·`CLAUDE.md:316` 의 충돌을 없앤다 |

---

## 1. 비-Unity Sparse Worktree 규칙 초안

### 1-1. 배경과 현재 규칙의 근거

- 현행 규칙 `CLAUDE.md:324` (§9-2):
  > `병렬 작업을 위한 워크트리는 쓰지 않는다. 직렬로 진행한다. (Unity 프로젝트가 둘이 되면 Library/ 를 따로 만든다.)`
- 기존 금지 이유 원본 (3가지):
  1. `docs/archive/DECISIONS-2026-10.md:2276` ("브랜치 전략"): Unity 프로젝트가 둘이 되면 `Library/` 를 따로 만들어야 한다.
  2. `docs/archive/DECISIONS-2026-10.md:1004` ("아트 AI 병행"): Unity MCP 가 어느 에디터에 붙는지 확인 못 한 문제.
  3. `docs/DECISIONS.md:150` (`feature/agy-run`): 저장소가 LFS 를 쓰고 `Library/` 가 따로 필요해 격리 복사본 비용이 크다.
- 비-Unity sparse worktree 가 위 세 근거를 우회하지 않고 해소하는 이유:
  - `CLAUDE.md:305` (§9-2 "병합 조건의 적용 범위")는 이미 `Assets/`·`Packages/`·`ProjectSettings/` 에 닿지 않는 브랜치를 **문서 검증** 대상으로 분리하고 있다.
  - sparse worktree 는 허용 목록 경로만 체크아웃하고 Unity 에디터를 열지 않으므로 ① `Library/` 가 생기지 않고 ② Unity 에디터는 메인 1개만 유지되어 MCP 대상 혼선이 없으며 ③ `Assets/`·`art-src/` LFS 대용량 파일이 복제되지 않는다.
- 직렬 강제의 실제 마찰:
  - 에디터가 열린 상태에서 문서·도구·하네스 브랜치로 전환하면 재임포트(`docs/RULES/BRANCHING.md:19`)와 타 세션의 브랜치 충돌(`docs/DECISIONS.md:1377`, 리뷰 도중 다른 세션이 `docs/blender-art-agent` 체크아웃)이 일어난다.
  - `docs/HANDOFF.md:58` 이 격리 작업 공간 도입 뒤로 보류한 `agent-relay.ps1` 자동 수정 검토에도 메인 Unity 트리를 공유하지 않을 기반이 필요하다.

### 1-2. 실측 사실 (2026-11-27, Git 2.49.0.windows.1 + Git LFS 3.6.1)

`%TEMP%` 임시 저장소 실측 및 본 리포 조회로 확인한 사실:

1. **생성 절차와 격리 (임시 저장소 실측)**:
   - `git worktree add --no-checkout` → `sparse-checkout init --cone` → `sparse-checkout set docs tools .claude .codex .agents` → `checkout` 순서로 만들면 루트 파일(`CLAUDE.md`, `AGENTS.md`, `README.md`, `.gitignore`, `.gitattributes`, `ProtoHarness.slnx` 등)과 5개 디렉터리만 체크아웃된다.
   - `Assets/`, `Packages/`, `ProjectSettings/`, `art-src/`, `.vscode/` 는 작업 트리에 생성되지 않는다.
   - `git sparse-checkout list` 는 cone 경로를 사전순(`.agents`, `.claude`, `.codex`, `docs`, `tools`)으로 정렬해 출력한다.
   - 메인 워크트리의 `core.sparseCheckout` 는 비어 그대로 유지되고, `Assets/` 에 영향이 없다.
   - 루트의 추적 파일 `ProtoHarness.slnx` 는 `--cone` 에서 함께 체크아웃되지만, 그것이 가리키는 `*.csproj` 는 `.gitignore:51` 대상이라 워크트리에 없다 → **워크트리에서 IDE 로 솔루션을 열거나 빌드하지 않는다.**
2. **공유 `.git/config` 변화 (임시 저장소 실측)**:
   - `git worktree add` 가 아니라 **`git sparse-checkout init`** 이 메인 `.git/config` 에 `extensions.worktreeConfig=true` 를 쓴다(`core.repositoryformatversion=0` 유지, 워크트리별 설정은 `.git/worktrees/<이름>/config.worktree` 에 저장).
   - `git worktree remove` 로 워크트리를 지워도 `extensions.worktreeConfig=true` 는 `.git/config` 에 남는다(Q2).
3. **LFS 범위 실측 (본 리포 `dev` `4f9ab25`)**:
   - `git lfs ls-files` 전체 175개 중 `Assets/`·`art-src/` 밖은 `docs/images/` 12개뿐이다(합계 **21,564,683 B ≈ 20.57 MiB**, 메인 LFS 캐시에 12/12 존재·크기 일치).
   - Git LFS 3.6.1 은 저장소 경로를 `commondir` 기준으로 찾지만, 본 리포 linked worktree 에서의 실제 오프라인 smudge 는 아직 실측하지 않았다(1-5).
4. **브랜치 점유 제약 (임시 저장소 실측)**:
   - 한 브랜치가 이미 다른 워크트리에 체크아웃돼 있으면 `git switch <브랜치>` 는 `fatal: '<브랜치>' is already used by worktree at ...` 로 실패한다.

### 1-3. 기존 훅·래퍼 점검 결과 (코드 실증 — 보강 대상)

1. **`tools/hooks/guard.ps1:80-98, :394-439` (보강 필수, Q3)**:
   - **양방향 절대 경로 보호 틈**: `Get-ProtectedReason` 은 `$repoFull` 과 `$rootFull` 만 접두사로 떼고 `^CLAUDE\.md$`, `^docs/RULES/`, `^Packages/manifest\.json$`, `^ProjectSettings/` 같은 `^` 앵커로 판정한다. 따라서 **linked → 메인**·**메인 → linked**·**linked → linked** 3방향으로 다른 워크트리의 절대 경로를 넘기면 루트 기준 보호 파일(`CLAUDE.md`, `AGENTS.md`, `docs/RULES/BRANCHING.md`, `Packages/manifest.json`, `ProjectSettings/ProjectVersion.txt` 등)이 통과한다. *(참고: `.meta`·`.unity`·`.prefab`·`.asset`·`.inputactions` 는 확장자 매치라 이미 걸린다.)*
   - **필요한 보강 ① (fail-closed)**: `git worktree list --porcelain -z` 로 등록된 모든 워크트리 루트(메인 + 전체 linked)를 구해 접두사 제거 후보에 넣는다. **`git worktree list` 조회가 실패하면 비차단 `exit 1`(`guard.ps1:609-612`)로 넘기지 않고 `deny`(fail-closed)로 막는다.**
   - **필요한 보강 ② (git 명령 차단 — 승인 패턴 정확 일치)**: `Get-GitReason` 에 아래 차단(`deny`)을 추가한다:
     - `git worktree add` 에 `-n`/`--no-checkout` 이 빠진 호출(전체 `Assets/`·LFS 체크아웃 방지) 및 `-f`/`--force`
     - `git worktree remove -f` / `--force`
     - `git checkout` / `git switch` 의 `--ignore-other-worktrees`
     - **메인 워크트리**(`git worktree list` 첫 항목)에서의 `git sparse-checkout init|set|add|reapply` 실행(메인의 `Assets/` 가 디스크에서 사라져 Unity 재임포트 유발 방지)
     - `git sparse-checkout disable`, `git sparse-checkout add`, `git sparse-checkout set --stdin`(정적 판정 불가·점진 확대 방지)
     - `git sparse-checkout set` **승인 패턴 정확 일치 검증**:
       - **Q1 (a) `--cone` 채택 시**: `--no-cone` 금지, 인자 집합이 `{.agents, .claude, .codex, docs, tools}` 와 **정확히 일치**할 때만 `pass` (부분집합 `set docs` 등 훅 누락도 `deny`, 금지 경로 포함도 `deny`).
       - **Q1 (b) `--no-cone` 채택 시**: 인자 목록이 승인된 **순서 포함 전체 패턴**(`/*`, `!/Assets/`, `!/Packages/`, `!/ProjectSettings/`, `!/art-src/`, `!/docs/images/`, `!/.vscode/`)과 **정확히 일치**할 때만 `pass` (`/*` 단독 등 부정 패턴 누락 `deny`).
   - **한계 명시**: 훅은 셸 명령으로 보호 파일을 직접 쓰는 것은 판정하지 못하며(`SUBAGENTS.md:127`), 판정 외 예외 시 비차단 오류(exit 1, `guard.ps1:609-612`)로 끝난다.
2. **`tools/hooks/after-agents-edit.ps1:19-30` · `tools/sync-agents.ps1:22-31` (보강 필수, Q3)**:
   - `after-agents-edit.ps1:22` 는 어느 트리의 `/.claude/agents/*.md` 에든 반응하지만 `:24-26` 은 늘 `CLAUDE_PROJECT_DIR` 의 `sync-agents.ps1 -Check` 를 돌린다 → 편집 대상 `$path` 에서 워크트리 루트(`/.claude/agents/...` 앞부분)를 추출해 그 트리의 `sync-agents.ps1 -Check` 를 실행하도록 고친다.
   - `sync-agents.ps1:31` 은 `-Check` 모드에서도 `.codex/agents` 폴더가 없으면 `New-Item` 으로 생성한다 → `-Check` 에서는 디렉터리를 만들지 않고 어긋남(exit 1)으로 보고하게 고친다.
3. **래퍼 3종별 실제 동작과 워크트리 지원 범위**:
   - **`tools/agent-relay.ps1`**: `$repoRoot` 를 스크립트 위치(`:54`)에서 구하므로 워크트리 안의 스크립트를 호출하면 그 워크트리 대상으로 동작한다. 잠금 `Local\protoharness-agent-relay` 는 **Windows 로그온 세션 범위**로 직렬이며, 산출물 폴더 `%TEMP%\protoharness-agent-relay\<RunId>` 충돌을 피하려면 워크트리 간 서로 다른 `-RunId` 를 쓴다(`SUBAGENTS.md:148`).
   - **`tools/agy-run.ps1` (Q3 (a) 필수 보강 대상; 보강·시험 전 linked worktree 사용 금지)**:
     - `:204-207` 이 `Join-Path $repoRoot '.git/config'`, `'.git/info/exclude'`, `'.git/hooks'` 를 직접 읽는다. linked worktree 에서 `.git` 은 폴더가 아니라 **파일**이므로 config·exclude 는 앞뒤 모두 `missing` 으로 같아지고 hooks 는 `Test-Path` 실패로 건너뛰어 실제 공유 Git 설정·훅 변경을 조용히 놓친다(§5). 또한 `extensions.worktreeConfig=true` 뒤에는 메인·linked 모두 `config.worktree` 도 감시 대상이 된다. → `git rev-parse --git-common-dir` 로 공유 `config`·`info/exclude`·`hooks` 를 감시하고, `git rev-parse --git-path config.worktree` 로 워크트리별 `config.worktree` 까지 함께 감시하도록 보강한다.
     - `:192-194` 는 공유 ref 전체(`for-each-ref`)를 스냅샷하므로, `agy-run.ps1` 실행 중 외부 세션이 어느 워크트리에서든 커밋·브랜치 변경 시 거짓 계약 위반(exit 12)이 난다.
     - `:291` 은 `Local\protoharness-agy-run` 과 함께 **`Local\protoharness-agent-relay` 도 동시에 잡는다**(Windows 로그온 세션 범위). 따라서 워크트리가 달라도 `agy-run` 과 `agent-relay` 는 동시에 돌지 않는다.
   - **`tools/codex-unity.ps1`**: `:259-261` 이 스크립트 위치가 아니라 **호출 디렉터리(cwd)** 에서 companion 을 실행한다. `:69-82, :275` 뮤텍스(`Local\protoharness-codex-unity`, Windows 로그온 세션 범위)는 `~/.codex/config.toml` 을 다루는 `-Unity`/`-Blender`·`-Close`·감시 프로세스 설정 복원에만 걸리며 일반 읽기·쓰기 실행을 직렬화하지 않는다. 비-Unity sparse worktree 에서는 `-Unity`/`-Blender` 를 쓰지 않으며, 검증 완료 전까지 시범 범위에서 제외한다.
4. **`tools/harness-cost.py:8, :81-83`**:
   - 기본 기록 폴더가 현재 리포 경로 1개(`~/.claude/projects/<encoded-path>`)만 읽는다. 워크트리에서 연 Claude 세션 기록은 별도 폴더에 쌓이고, 워크트리를 `git worktree move` 로 옮기면 이 기록 경로와 `tools/agy-run.ps1:299-310` 의 `-Resume` 저장소 경로 대조도 달라진다.

### 1-4. 규칙 문구 초안 (단일 진실 원천 배치)

금지선은 `CLAUDE.md` §0 에 한 번, 병렬 절대 금지 정합은 `CLAUDE.md` §7 에, 요약·참조는 `CLAUDE.md` §9-2 에, 허용 목록·절차·정리는 `docs/RULES/BRANCHING.md` §9-4 에, 도구 쓰기·`agy-run` 제약은 `docs/RULES/SUBAGENTS.md` §7-6 에만 둔다(`CLAUDE.md:19`).

#### ① `CLAUDE.md` 수정안 (`:16`, `:41`, `:59`, `:242`, `:287`, `:305`, `:324`)
- **`:16` (작업별 규칙 표)** 및 **`:287` (§9 안내)**: `§9-1 이름, §9-3 알아둘 것` → `§9-1 이름, §9-3 알아둘 것, §9-4 비-Unity sparse worktree`
- **`:41` (§0 Git)**: 금지 명령에 `git worktree add`(단 `--no-checkout` 없음 또는 `-f`), `git worktree remove --force`(`-f`), `git checkout|switch --ignore-other-worktrees`, `git sparse-checkout disable|add`, 메인 워크트리에서의 `git sparse-checkout` 추가.
- **`:59` (§0 실행)**: 한 줄 추가 — `비-Unity sparse worktree 경로를 Unity Hub·Editor·batch mode·IDE 솔루션으로 열지 않는다.`
- **`:242` (§7 병렬 절대 금지 1번)**: `같은 파일에 쓰는 작업 2개 — 충돌한다.` → `같은 작업 트리에서 같은 파일에 쓰는 작업 2개 — 충돌한다. (서로 다른 워크트리에서 HANDOFF.md·DECISIONS.md 를 고친 경우의 병합 절차는 docs/RULES/BRANCHING.md §9-4)`
- **`:305` (§9-2 병합 조건의 적용 범위)**: 비-Unity 예시에 `.agents/`, `AGENTS.md` 추가 및 `art-src/` 는 문서 검증 대상이나 sparse worktree 허용 범위는 `docs/RULES/BRANCHING.md` §9-4 를 따른다고 연결.
- **`:324` (§9-2 워크트리 줄 교체안)**:
  > - Unity·Blender 작업(`Assets/`·`Packages/`·`ProjectSettings/`·`art-src/`)과 `Assets/` 를 읽는 도구(`tools/select-tests.ps1`·`reindex.ps1`·`test-runs.ps1`)는 워크트리를 쓰지 않고 메인 작업 트리에서 직렬로 진행한다. (Unity 프로젝트가 둘이 되면 `Library/` 를 따로 만들고, 에디터가 둘이면 MCP 대상이 갈린다.)
  > - 그 밖의 비-Unity 작업만 `docs/RULES/BRANCHING.md` §9-4 조건을 지키는 sparse worktree 를 쓸 수 있다. 금지 명령·실행 제한은 §0 을 따른다.

#### ② `docs/RULES/BRANCHING.md` 수정안 (`:1` 제목 + §9-4 신설)
- **`:1` 제목**: `# 브랜치 이름과 주의점 — CLAUDE.md §9-1 · §9-3 · §9-4`
- **§9-4. 비-Unity sparse worktree (신설안)**:
  1. **허용 범위 (단일 정의 — allowlist)**: 아래 경로만 다루는 비-Unity 브랜치에만 허용한다.
     - `docs/`, `tools/`(**단 `Assets/` 를 읽는 `tools/select-tests.ps1`·`tools/reindex.ps1`·`tools/test-runs.ps1` 수정·실행 및 `index/` 작업은 메인 전용으로 제외** — sparse 트리에서 `Assets/` 부재로 조용히 오판하고 `CLAUDE.md:308` 실실행 증거를 낼 수 없음), `.claude/`, `.codex/`, `.agents/`, 루트 문서(`CLAUDE.md`, `AGENTS.md`, `README.md`)
     - 루트에 함께 체크아웃되는 `.gitignore`·`.gitattributes` 는 §3-1 환경 기준 합의 없이 건드리지 않으며, `ProtoHarness.slnx` 는 IDE 로 열지 않는다(§0 실행).
     - `Assets/`, `Packages/`, `ProjectSettings/`, `art-src/`, `.vscode/` 등 허용 목록 밖 경로에 닿는 작업은 메인 작업 트리에서 직렬로 진행한다.
  2. **생성 조건과 위치**: (Q2 (a) 채택 시) 사용자가 지시하거나 병렬·격리에 합의했을 때만 만든다. 위치는 메인 리포 폴더 **바깥**의 형제 경로(예: `../ProtoHarness-wt/<짧은이름>`). 메인 리포 안에 두지 않는다.
  3. **생성 절차 (fail-fast 필수, `--no-checkout` — 아래는 Q1 (a) `--cone` 기준; Q1 (b) 채택 시 `--no-cone` 전체 패턴 일치 분기 적용)**:
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
     - 최소 필수 세트(`docs`, `tools`, `.claude`, `.codex`, `.agents`)를 줄이지 않는다(훅·규칙 유지).
     - **중간 실패 시 정리**: 어느 단계(`worktree add`·`sparse-checkout`·`checkout`)에서든 실패하면 즉시 중단하고 `git worktree list`·`git branch --list <브랜치>` 로 등록·브랜치 소유를 확인하며, 워크트리 안 미추적·무시 파일 유무도 확인한다. 이 실행이 새로 만든 등록·브랜치임이 확인된 경우에만 일반 `git worktree remove <경로>` + `git branch -d <브랜치>` 로 정리하고, **일반 `remove` 가 부분 체크아웃 등으로 실패하면 강제 삭제(`-f`)하지 않고 그 상태에서 중단·사용자 보고**한다(§0 Git).
  4. **병합·충돌 해결·정리**:
     - `git worktree list` 로 `dev` 의 체크아웃 위치를 먼저 확인하고, 병합 전 `$baseDev = git rev-parse dev` 와 `$featHead = git rev-parse <기능브랜치>` 를 고정해 둔다.
     - **경우 A (`dev` 가 메인에 체크아웃됨)**: 메인 작업 트리가 깨끗하고 Unity 에디터가 한가할 때(`§9-3`) 메인에서 `git merge --no-ff` 로 병합한다(`CLAUDE.md` §9-2: 사용자 지시, `HANDOFF:`·`구현:`·`검토:` 줄 필수).
     - **경우 B (메인이 다른 기능 브랜치에 있고 `dev` 가 어느 트리에도 미점유)**: 메인 브랜치를 전환해 에디터 재임포트를 유발하지 않고, sparse worktree 안에서 `git switch dev` → `git merge --no-ff <브랜치>` → 병합 직후 `dev` 점유를 풀기 위해 워크트리를 삭제하거나 다른 브랜치로 돌린다.
     - **경우 C (`dev` 가 제3의 linked worktree 에 점유됨)**: 즉시 중단하고 `git worktree list` 를 첨부해 사용자에게 보고한다.
     - **허용 목록 밖 변경·금지 폴더 이중 검증**:
       - **커밋 전**: `git status --porcelain=v1 -uall` (미추적 + 스테이지/작업 트리 상태) + `git diff --name-only --cached dev` (스테이지) + `git diff --name-only dev` (작업 트리) 세 가지를 모두 확인한다.
       - **병합 전 (기능 브랜치에서)**: `git diff --name-only "$baseDev...$featHead"` 로 허용 목록 밖 경로 0건 확인.
       - **병합 직후 (`dev` 병합 커밋에서)**: `git diff --name-only "$baseDev" HEAD` (또는 `git diff --name-only HEAD^1 HEAD`)로 실제 병합 결과에 허용 목록 밖 경로가 섞이지 않았는지 확인한다.
       - **금지 4폴더 부재 검사 (sparse worktree 한정)**: `git merge`·`rebase` 는 충돌 표시를 위해 sparse 밖 경로를 꺼낼 수 있으므로, **sparse worktree 에서 수행한 `merge`/`rebase` 전후**에는 반드시 `Assets/`·`Packages/`·`ProjectSettings/`·`art-src/` 부재를 확인한다(메인 작업 트리인 경우 A 에는 해당하지 않음).
     - **동시 편집 충돌 (`HANDOFF.md`·`DECISIONS.md`)**: 기능 브랜치에 최신 `dev` 를 먼저 병합해 충돌을 푼 뒤(`docs/HANDOFF.md:122`), 충돌 해결 내용·최종 `git diff "$baseDev...$featHead"`·리뷰 대상 SHA·병합 후 `git diff "$baseDev" HEAD` 결과를 대조한다.
     - **정리**: 병합(또는 사용자 폐기 승인) 뒤 미추적·무시(`.gitignore`) 파일, 열린 세션, 미병합 커밋이 없는지 확인하고 일반 `git worktree remove <경로>` 로 삭제한 뒤 `git worktree list` 로 확인한다(일반 `remove` 도 디렉터리 안 무시 파일을 함께 지운다). 사라진 경로 정리는 `prune`, 경로 보호는 `lock`, 이동·복구는 `move`·`repair`(이동 시 `tools/agy-run.ps1:299-310` 의 `-Resume` 경로와 `tools/harness-cost.py:8` 기록 폴더가 달라짐).
  5. **워크트리 간 제약·래퍼 지원**:
     - 다른 워크트리의 파일을 절대 경로로 직접 편집하지 않는다.
     - `tools/agy-run.ps1` 은 `.git` 공용 디렉터리 + `config.worktree` 감시 보강·시험 전까지 linked worktree 에서 실행하지 않으며, 어느 트리에서든 `agy-run` 실행 중에는 실행 중인 `agy-run -Role fix` 의 승인된 `-Allow` 쓰기를 제외한 **외부 세션**의 작업 트리 수정·커밋·ref 변경을 금지한다(`SUBAGENTS.md` §7-6). 또한 `agy-run` 이 `Local\protoharness-agent-relay` 잠금도 함께 잡으므로 워크트리가 달라도 `agy-run` 과 `agent-relay` 는 동시에 돌지 않는다.
     - `tools/agent-relay.ps1` 은 워크트리 간 서로 다른 `-RunId` 를 쓴다. `tools/codex-unity.ps1` 은 `-Unity`/`-Blender` 없이 대상 워크트리 cwd 에서만 쓰되 검증 전 시범 제외.
     - 첫 `sparse-checkout init` 이 `.git/config` 에 `extensions.worktreeConfig=true` 를 남긴다. `tools/harness-cost.py` 기본 실행은 메인 경로 세션만 집계한다.

#### ③ `docs/RULES/SUBAGENTS.md` §7-6 (`:112`, `:116`, `:135` 정합 수정안)
- **`:112`**: `무인 실행(/goal·/schedule)과 병렬 쓰기는 하지 않는다.` → `무인 실행(/goal·/schedule)과 같은 작업 트리의 병렬 쓰기는 하지 않는다.`
- **`:116`**: `작업 트리 하나에 쓰는 도구는 한 번에 하나다. 다른 도구로 넘길 때는 쓰던 쪽이 커밋하거나 남은 변경을 보고한 뒤 넘긴다. 비-Unity sparse worktree(docs/RULES/BRANCHING.md §9-4)는 별도 작업 트리로 본다.`
- **`:135`**: `실행하는 동안 Claude 도 작업 트리에 쓰지 않는다.` → `실행하는 동안 실행 중인 agy-run -Role fix 의 승인된 -Allow 쓰기를 제외한 외부 세션(Claude 포함)은 어느 워크트리에서도 작업 트리 수정·커밋·ref 변경을 하지 않는다(공유 for-each-ref 대조).`

### 1-5. 확인 못 한 것 (실효 전 필수 실측 대상)

- 실제 ProtoHarness 의 linked sparse worktree 에서 `docs/images/`(LFS 12개, 21,564,683 B)가 네트워크 없이 메인 `.git/lfs/objects/` 캐시만으로 smudge 되는지(Q1 (a) 기준) — **확인 못 함**.
- 실제 linked worktree 경로에서 Claude·Codex·Antigravity 3도구의 훅 실행, Codex `/hooks` 별도 신뢰 필요 여부, 훅 실패 시 동작 — **확인 못 함**.
- GitHub Desktop·VS Code 가 `extensions.worktreeConfig=true` 상태를 경고 없이 읽는지 — **확인 못 함**.

---

## 2. `unity-reviewer` 기준 초안

### 2-1. 배경과 실측 데이터 (2026-10-26 `리뷰 규모` 도입 이후 28건 전체)

- 현행 규칙 `docs/RULES/SUBAGENTS.md:63-66`:
  - `Assets/` 변경(`.meta` 제외)이 `M` 뿐 + 공개 시그니처 불변 + 추가+삭제 ≤ 30줄이면 "작은 변경"(메인 체크리스트 대조), 그 밖은 `unity-reviewer` 파견 + Codex 설계 리뷰.
  - `:66` `"시험 중 (리뷰 분담 비교) … 두 브랜치를 비교한 뒤 분담을 다시 정하고 이 줄을 지운다"` 가 2026-10-26 이후 미결로 남아 있다.
- **실측 요약** (표본 범위: **2026-10-26 `리뷰 규모` 규칙 도입(`docs/archive/DECISIONS-2026-10.md:557`) 이후** `docs/DECISIONS.md`·`docs/archive/DECISIONS-2026-10.md` 에 토큰 수가 기록된 병합 전 파견 **28건**, 11-15 Bash 제거 시험 파견 `49,016`(`DECISIONS.md:823`) 제외 — 전체 목록은 **부록 A**. *참고: 규칙 도입 직전 같은 날 10-26 R8 `78,161` 토큰(`DECISIONS-2026-10.md:562`)을 포함하면 29건·합계 2,804,462·평균 96,706*):
  - **비용**: 총 **2,726,301 토큰** — 최소 41,530 · 중앙값 **85,584** · 평균 **97,368**(`2,726,301 ÷ 28 = 97,367.89`) · 최대 225,688. 2026-11-27 정식 재측정에서 11-13 이후 전체 Agent 파견 18회 중 10회(55.6%)가 `unity-reviewer`(`docs/DECISIONS.md:1069`).
  - **§0 치명 위반 발견**: **0 / 28건**. 단, `tools/hooks/guard.ps1` 은 파일·git 경로만 막고 **코드 수준 §0 항목**(빈 `catch`, `public` 필드, `Update` 할당, 테스트 단언 약화 등)은 막지 못한다. 따라서 `unity-reviewer` 를 생략하는 Tier 4 에서는 코드 수준 §0 확인이 대조 + 독립 교차 검토에만 의존한다는 위험이 남는다.

| 영역 (부록 A 집계) | 파견 수 | 토큰 합계 | `중대` 또는 실제 결함·핵심 규칙 이슈 발견 | 비고 (반례·실측 대입 포함) |
|---|---:|---:|---:|---|
| **시뮬·레이스·봇·트랙** | 11 | 1,353,723 | **6 / 11** *(단독 3건)* | `race-catchup`(실제 결함 후보 1 — 스윙 2배 가속 불연속, 단독), `m6-open-course`(중대 2 — 빌드·침범, 단독), `m6-wall-boost-heading`(결함 2 중 1 단독), `mp2-contact`(보통 2 = Codex 결함 2 중복), `mp2-bot-race`(보통 1 = `select-tests` 누락), `mp1-race-field`(중대 1 = Codex 중복). *(이 외 `mp2-bot-difficulty` 규칙 1·`bot-draft-pass` 테스트 공백 1·`swing-rope-accel` 단언 약함 제안 1)* |
| **아트·장식·킷 빌더** | 11 | 893,235 | **3 / 11** *(단독 3건)* | **`m6-stadium-detail`**(중대 1 — 0정점 잔디 메시 §5, Codex 구현) + **`art-rockkit` K4**(중대 1 — 예외 배치 무로그 §5, `DECISIONS.md:1243`; 리뷰 전 `5cb1466` 에디터 2파일 90줄 + `select-tests.ps1` 2줄 → **Tier 1 ⑤**) + **`m3-finger-exterior` #27**(중대 1 — `ValidateClearance` 그룹 면제 §5, `DECISIONS-2026-10.md:453`; `e896b5a` 신규 `FingerLanesEnvironmentBuilder.cs` 594줄 → **Tier 1 ④**). 나머지 8건(`K1×2, K3×3, K11, fix/finger-exterior-tool, 맵 디자인`)은 치명·중대 0. 최초 킷 등록으로 `select-tests.ps1` 을 건드린 K1 1차(`:1181`)·K3 1차(`:1222`)는 계속 Tier 1 ⑤ 에 걸리지만, K1 2차(`:1173`)·K3 2~4차(`:1195,:1216`)·K11 등 프리팹 diff 위주 후속 반영은 생략됨 |
| **UI·오디오·설정** | 6 | 479,343 | **3 / 6** *(단독 2건)* | `m4-control-feedback`(실제 결함 1 — `Update`/`LateUpdate` 순서, Codex 구현), `m6-sound-cues`(보통 2 — `Awake` 예외 처리; 사후 대입용 병합 SHA `4929f73`·코드 커밋 `9639fac` 기준 `ChainRushAudio.cs` 340줄 + 시뮬 41줄 + 기존 테스트 기대값 수정 → **Tier 1 ③·⑤ 및 Tier 2 해당**; 당시 리뷰 SHA 는 확인 못 함), `fix/audio-recording-failures`(보통 1 — 무음 구간, Codex 중복). `lobby-bright-theme`·`m2-screen-settings`·`m6-finger-sound`(경미 2)는 실제 결함 0 |

### 2-2. 새 `unity-reviewer` 기준 초안 (2축 분리: 체크리스트 담당자 × 독립 교차 검토자)

#### ① 판정 대상 파일 (`CODE` 집합 — Git pathspec, 파일군 합산)
- 대상 집합 `CODE`:
  `git diff --name-status dev...HEAD -- ':(glob)Assets/**/*.cs' ':(glob)Assets/**/*.asmdef' ':(glob)Assets/**/*.asmref' ':(glob)Assets/**/*.shader' ':(glob)Assets/**/*.hlsl' ':(glob)Assets/**/*.compute' 'tools/select-tests.ps1'`
- 줄 수는 `git diff --numstat dev...HEAD -- <해당 Tier 대상 파일군 전체>` 의 추가+삭제 **합계**로 센다(파일별 개별이 아니라 **파일군 전체 합산**).
- 비-코드 에셋(`.prefab`·`.unity`·`.asset`·`.mat`·`.fbx`·`.png`·`.meta`)과 `Packages/`·`ProjectSettings/` 는 줄 수에 넣지 않고, 메인이 전체 `git diff --name-status dev...HEAD` 로 §0 금지 파일·`.meta` 짝·`Assets/TutorialInfo/` 접촉을 항상 확인한다.
- **경계별 적용 범위**:
  - **`Assets/` 접촉 브랜치**: 아래 축 1(Tier 1~4) + 축 2 적용.
  - **`Packages/`·`ProjectSettings/` 만 바뀐 브랜치**(`Assets/` 미접촉): Unity 검증 대상(`CLAUDE.md:305`)이지만 `.cs` 코드가 없으므로 **Tier 4**(메인 §0 체크리스트 대조) + 축 2 적용.
  - **비-Unity 브랜치**(`Assets/`·`Packages/`·`ProjectSettings/` 미접촉 — `tools/select-tests.ps1` 단독 수정 포함): `unity-reviewer` 파견 대상이 아니며 `CLAUDE.md` §9-2 문서 검증(도구 수정 시 실실행 증거 `CLAUDE.md:308`) + 축 2 독립 교차 검토만 적용한다.

#### ② 축 1 — 체크리스트 담당자 확정 표 (Tier 1 → 2 → 3 → 4 처음 맞는 행 1개, 공백·충돌 없음)

> **단일 원천**: Q4×Q5 선택에 따른 `unity-reviewer` 파견·relay·메인 대조 여부는 **이 표에서만 정의**한다. 아래 축 2 와 `SUBAGENTS.md` §7-6 교체 문안은 이 표를 참조한다.

| 우선순위 | 조건 (`dev...HEAD` 기준) | Q4 (a) + Q5 (a) *(추천)* | Q4 (a) + Q5 (b) | Q4 (b) + Q5 (a) | Q4 (b) + Q5 (b) |
|---|---|---|---|---|---|
| **Tier 1 (고위험)** | `Assets/` 접촉 브랜치 중 아래 하나라도 해당:<br>① `.asmdef`·`.asmref` 변경, 또는 `Assets/_Project/Scripts/` 바깥 `.cs`(예: `Assets/TutorialInfo/Scripts/`)<br>② **틱·직렬화·프로토콜·입력 핵심 파일**(`Ticks.cs`, `SimSnapshot.cs`, `RacerState.cs`, `RunRules.cs`, `RunnerProfile.cs`, `Runtime/Net/**`, `Runtime/ChainRush/Control/**`) **줄 수 무관** 변경<br>③ **코어 시뮬·레이스·봇·트랙**(`Motion/**`, `Bots/**`, `Track/**`, `Race/**` 중 `TrackLook.cs`·`CircuitScenery.cs` 제외, 루트 `RunnerMotor.cs`·`GrappleController.cs`·`ChainRushGame.cs`) 파일군 합산 **> 30줄** 또는 신규/삭제/이동/공개 시그니처 변경<br>④ **에디터 빌더**(`Editor/ChainRush/**`) 신규 `.cs` 또는 파일군 합산 **> 100줄**<br>⑤ **테스트 무결성**: `Tests/PlayMode/TestLogs.cs`, `tools/select-tests.ps1`(`$rules` 첫 일치 순서 회귀 방지, `DECISIONS.md:88`), 또는 **기존 테스트의 단언·기대값 수정·삭제** *(신규 테스트 메서드 추가만은 제외)* | **파견 1회 자동** *(Claude 세션: `unity-reviewer`, Antigravity·Codex 메인 세션: 아래 relay)* | `claude` 포함 구현 → 파견 1회 자동 / `claude` 미포함 → Claude 대조(파견은 지시 시) | `claude` 포함 구현 → 메인 대조 / `claude` 미포함 → 파견·relay 1회 자동 | 메인/Claude 대조 *(파견은 지시 시)* |
| **Tier 2 (중위험 큰 변경)** | Tier 1 밖, `Flow/**`·`Audio/**`·`Tutorial/**`·`Visuals/**`·`ChainRushHud.cs`·`FollowCamera.cs`·`CourseTarget.cs`·`Race/TrackLook.cs`·`Race/CircuitScenery.cs`·셰이더(`.shader`/`.hlsl`/`.compute`)에서 **새 `MonoBehaviour` 추가** 또는 파일군 합산 **> 100줄** | **파견 1회 자동** *(세션별 방식은 Tier 1 과 동일)* | Tier 1 과 동일 | Tier 1 과 동일 | 메인/Claude 대조 *(파견은 지시 시)* |
| **Tier 3 (타 계열 비소규모)** | Tier 1·2 밖, `구현:` 에 `codex` 또는 `antigravity` 가 포함되고 `CODE` 가 "작은 변경"(`M` 만, 공개 시그니처 불변, ≤ 30줄)을 넘음 | **파견·relay 1회 자동** *(단 `claude` 포함 혼합이면 축 2 독립성 준수)* | *(Tier 4 로 통합)* | `claude` 미포함 타 계열만 파견·relay 1회 자동 / `claude` 포함 혼합은 메인 대조 | *(Tier 4 로 통합)* |
| **Tier 4 (그 밖 모든 Unity 변경)** | Tier 1·2·3 어디에도 해당하지 않는 모든 `Assets/`·`Packages/`·`ProjectSettings/` 변경 *(≤ 30줄 작은 변경, 31~100줄 `Flow/`·`Visuals/`·`Editor/` 일반 수정, 신규 테스트 추가 전용, 비-코드 에셋·`Packages/`·`ProjectSettings/` 전용 등)* | **체크리스트 대조** *(Claude 세션: 메인 대조; Antigravity·Codex 메인 세션: 축 2 대로 실제 Claude 대조 또는 relay)* | **체크리스트 대조** *(동일)* | **체크리스트 대조** *(동일)* | **체크리스트 대조** *(동일)* |

#### ③ 축 2 — 구현자 집합별 독립 교차 검토자 (`검토:` 기록 — `CLAUDE.md:316` & `SUBAGENTS.md` §7-6, 모든 Tier·비-Unity 공통)

| `구현:` 값 | 독립 교차 검토자 (`검토:`) | 자동 호출·우선순위 규칙 |
|---|---|---|
| `claude` | `codex` *(시각 작업이면 `antigravity` 도 가능)* | 기본은 Codex `adversarial-review` 1회 자동(`SUBAGENTS.md:54`). 단 **시각 작업에서 사용자가 `antigravity` 시각 검수를 독립 검토로 지정하면 Codex 자동 1회는 중복 생략 가능**(둘 다 돌려도 무방). `unity-reviewer` 는 같은 계열이라 `검토:` 에 세지 않는다 |
| `codex` | `claude` | 체크리스트 방식은 위 축 1 확정 표를 따른다(Claude 세션이면 파견/메인 대조, 사용자가 Codex 를 직접 쓰는 세션(`SUBAGENTS.md:95`)이면 `agent-relay` 또는 Claude 세션 대조). Codex 리뷰는 같은 계열이라 자동 호출·`검토:` 에서 제외 |
| `antigravity` | `claude` *(Tier 1~3 `Assets/` 변경은 `codex` 도 병행 → `검토: claude, codex`)* | **Antigravity(Gemini) 자체 대조는 `검토: claude` 로 세지 않는다.** 체크리스트 방식은 위 축 1 확정 표를 따르며, Tier 4·비-Unity 도 실제 Claude(`agent-relay` 또는 Claude 세션) 검토를 받는다 |
| **혼합** *(예: `claude, codex`)* | **`구현:` 에 없는 모델 계열** 또는 `사용자` *(단 하네스 파일 제외 — 아래 줄)* | `codex` 가 구현에 포함된 혼합에서는 `SUBAGENTS.md:54` Codex 자동 1회를 돌리지 않는다(독립 검토 아님). 혼합에서 `unity-reviewer`(Claude)나 Codex 리뷰는 체크리스트·설계 보조일 뿐 `검토:` 로 세지 않으며, `구현:` 에 없는 계열(`antigravity` 등)이나 사용자 생략 명시(`검토: 없음 — <사용자 말>`)가 필요하다(`CLAUDE.md:316`) |
| **하네스 파일 포함** | **구현하지 않은 모델 계열 1개 이상 + 사용자 승인**(`SUBAGENTS.md:125`) | **혼합 구현이라도 사용자 1명이 모델 검토와 승인을 겸하지 않는다.** 예: `claude, codex` 혼합 하네스 변경은 `antigravity` 검토 + 사용자 승인 필요(3모델 전부 구현 참여 시에만 사용자 명시적 면제 `검토: 없음 — <사용자 말>`) |

- **Antigravity·Codex 세션의 `agent-relay` Claude 체크리스트 검토 계약**:
  - 명령: `powershell -NoProfile -ExecutionPolicy Bypass -File tools/agent-relay.ps1 -Target claude -Role review -Base dev -RunId <고유ID> -PromptFile <지시서>` (`-RunId` 필수, `agy` 세션에서는 샌드박스 밖 실행 `SUBAGENTS.md:151`).
  - `-PromptFile` 필수 내용 (호출 모드에 맞춰 증거 범위를 일치시킨다):
    - ① `.claude/agents/unity-reviewer.md` 체크리스트 수행 지시(relay 는 에이전트를 자동 로드하지 않음).
    - ② **커밋 전용 호출**(`-IncludeWorktree` 없음): 같은 `base`·`HEAD` 기준 전체 `git diff --name-status <base>...<HEAD>` 및(해당 시) `tools/select-tests.ps1 -Range <base>...<HEAD>` 고정 범위 출력(`select-tests.ps1:284` 기본값은 미커밋·미추적을 섞으므로 `-Range` 필수).
    - ③ **`-IncludeWorktree` 호출**(미커밋 초안 등): `git diff --name-status <base>` + `git status --porcelain=v1 -uall`(미추적·스테이지 포함) 및(해당 시) `tools/select-tests.ps1` 출력.
  - 미커밋 변경과 exit 2: `-IncludeWorktree` 없이 호출하면 **`merge-base..HEAD` 변경 파일과 겹치는 미커밋 변경(스테이지 포함)** 이 있을 때만 exit 2 로 거부한다(`SUBAGENTS.md:147`, `agent-relay.ps1:818`).
  - 판정·실패 처리: 정상 `PASS`/`NEEDS_FIX` 모두 exit 0(`verdict` 로 구분). `NEEDS_FIX` 는 수정 후 재확인 또는 사용자 결정. exit 2/12/13 은 원문 보고 후 사용자에게 묻는다(§5).
- **예외 및 분담 비교 시험 종료**:
  - 사용자가 `unity-reviewer` 파견을 명시 지시하면 등급과 무관하게 파견한다.
  - Codex 리뷰 실패 시 Claude 구현의 Tier 4 큰 변경에 `unity-reviewer` 를 보조로 돌릴 수는 있으나, 같은 계열이므로 독립 `검토:` 로 세지 않는다(`검토: 없음 — <사용자 말>` 규칙 유지).
  - `docs/RULES/SUBAGENTS.md:66` 의 `"시험 중 (리뷰 분담 비교)"` 문단을 삭제하고 결정 기록(`docs/DECISIONS.md`)으로 닫는다.

#### ④ `docs/RULES/SUBAGENTS.md` 정합 교체 문안 (`:14`, `:25`, `:54`, `:63-66`, `:123`, `:128`)
- **`:14` 표**: `리뷰 (§0/§1/§5 체크리스트)` → `리뷰 (§0/§1/§5/§4-4 체크리스트)`
- **`:25` (`unity-reviewer` 파견문 입력)**: diff 파일 경로·base·HEAD 해시·작업 트리 일치 여부에 더해 **전체 `git diff --name-status <base>...HEAD` 출력(및 `select-tests.ps1` 변경·새 PlayMode 테스트가 있으면 `-Range <base>...HEAD` 실행 출력)** 을 함께 적도록 추가.
- **`:54` (§7-5 Codex 자동 1회 표)**: `브랜치당 1회는 지시 없이 부른다.` → `구현에 codex 가 포함되지 않고(시각 작업을 antigravity 단독 검토로 지정하지 않은) 브랜치당 1회는 지시 없이 부른다.`
- **`:63-66`**: 제목 `**리뷰 규모**` 는 유지해 `:25`·`:123` 참조가 깨지지 않게 하고, 본문을 위 ② 축 1 확정 표(Q4·Q5 선택 열 1개만 남김)로 교체하며 `:66` `"시험 중"` 문단을 삭제.
- **`:123` (§7-6 교차 검토 표 `antigravity` 행)**: `claude (실제 Claude 검토 — 방식은 §7-5 "리뷰 규모" 확정 표를 따르며, Tier 4·비-Unity 도 agent-relay 또는 Claude 세션 대조). Tier 1~3 `Assets/` 변경은 codex 도`
- **`:128` (§7-6 `codex` 구현 문단)**: `Claude 검토는 §7-5 "리뷰 규모" 확정 표를 따른다(Codex 직접 세션이면 agent-relay 또는 Claude 세션 대조). 혼합 구현·하네스 파일의 독립 검토는 §7-6 표와 CLAUDE.md §9-2 를 따른다.`

#### ⑤ `.claude/agents/unity-reviewer.md` 체크리스트 보강 초안 (조항별 정위치 배치)
- **`:3` description** 및 **`:54` 심각도**: `§0/§1/§5` → `§0/§1/§5/§4-4` (`보통` 에 `§4-4 테스트 선택 누락` 포함).
- **변경 범위 받기(`:15`)**: 메인이 넘기는 필수 입력에 **전체 `git diff --name-status <base>...HEAD`** 추가.
- **`§0 금지선 위반 (치명)` 끝(`:31` 아래)** 1줄 추가:
  - `- [ ] 테스트를 통과시키려고 기존 테스트 단언·기대값을 약화하거나 특수 케이스를 넣었는가 (CLAUDE.md §0)` *(참고: `.meta`·YAML 은 이미 `:23-24` 에 있어 중복 추가하지 않는다.)*
- **`§5 실패는 시끄럽게` 끝(`:45` 아래)** 2줄 추가:
  - `- [ ] 프레임·틱 실행 순서 의존(Awake/OnEnable/Update/LateUpdate/FixedUpdate/sceneLoaded)이 호출 순서와 어긋나지 않는가`
  - `- [ ] 경계값·문턱(0↔양수 전환, 빈 입력/0정점 메시, 초기화 실패 뒤 재호출)에서 동작이 끊기거나 조용히 잘못된 상태가 남지 않는가`
- **새 소절 `§4-4 검증 범위`**(`§2 재사용` 앞) 1줄 추가:
  - `- [ ] 새 PlayMode 테스트 클래스나 파일 이동이 있는데 tools/select-tests.ps1 표 갱신이 빠지지 않았는가 (파견문의 전체 name-status·도구 출력 기준)`

*(정의 수정 시 `tools/sync-agents.ps1` 로 `.codex/agents/unity-reviewer.toml` 재생성, `CLAUDE.md` §7)*

### 2-3. 확인 못 한 것 (`unity-reviewer` 기준)

- **Codex 리뷰의 정확한 토큰·비용**: 28건 대부분에서 `/status` 5시간 `used_percent` 가 기록되지 않아 Claude 대 Codex 비용 비율은 수치화하지 못했다(`docs/DECISIONS.md:1030`).
- **28건 전체의 리뷰 전 커밋별 `.cs` 줄 수 대입**: `#15 K4`(리뷰 전 `5cb1466`, 90줄 + `select-tests.ps1` → Tier 1 ⑤), `#17 m6-sound-cues`(사후 대입용 `4929f73`/`9639fac`, 340줄 + 시뮬 41줄 + 테스트 기대값 → Tier 1 ③·⑤ 및 Tier 2; 당시 리뷰 SHA 확인 못 함), `#27 m3-finger-exterior`(`e896b5a`, 신규 빌더 594줄 → Tier 1 ④)은 확인했으나 나머지 브랜치의 리뷰 전 커밋별 `.cs` 줄 수는 전부 재집계하지 않았다(실제 적용 5~10건 뒤 100줄 임계치 재점검).
- **Tier 4 코드 수준 §0 회귀 위험**: Tier 4(Claude 단독 31~100줄 `Flow/`·`Visuals/`·`Editor/` 등)는 `guard.ps1` 이 코드 수준 §0 을 막지 않으므로 구현자 대조 + Codex 교차 검토에 의존한다.

---

## 3. §3-2 합의 요청서 (초안 — Q1~Q5 결정 후 확정)

```
목표:        비-Unity 작업에 한해 sparse worktree 를 허용하는 규칙·훅·래퍼 보강을 도입하고,
             28건 실측에 근거해 unity-reviewer 파견 기준을 위험 우선 4단(2축)으로 재정비한다
건드릴 것:
  CLAUDE.md:16, :41, :59, :242, :287, :305, :324 — §0 Git/실행 금지, §7 병렬 금지 1번 정합, §9-4 안내, §9-2 비-Unity 예외·적용 범위
  docs/RULES/BRANCHING.md:1, (끝에 §9-4)          — 비-Unity sparse worktree allowlist·fail-fast 생성·병합 경우 A/B/C·정리·래퍼 제약
  docs/RULES/SUBAGENTS.md:14, :25, :54, :63-66, :112, :116, :123, :128, :135
                                                  — unity-reviewer 4단·2축 분기, "시험 중" 삭제, Codex 자동 조건·워크트리·agy-run·교차 검토 정합
  .claude/agents/unity-reviewer.md:3, :15, :31, :45, :54 — 입력(name-status)·체크리스트(§0/§5/§4-4) 보강
  .codex/agents/unity-reviewer.toml              — tools/sync-agents.ps1 재생성 (생성물)
  tools/hooks/guard.ps1:80-98, :394-439          — (Q3 (a)) worktree list --porcelain -z 양방향 보호(fail-closed) + worktree/sparse 정확 일치 차단
  tools/hooks/after-agents-edit.ps1:19-30        — (Q3 (a)) 편집 대상 파일의 워크트리 루트에서 sync-agents -Check 실행
  tools/sync-agents.ps1:22-31                    — (Q3 (a)) -Check 모드에서 .codex/agents 폴더 자동 생성 제거
  tools/agy-run.ps1:204-214                      — (Q3 (a)) --git-common-dir + config.worktree(메인·linked 공통) 감시 보강
  docs/DECISIONS.md, docs/HANDOFF.md             — 결정 기록, HANDOFF 2절 7번·4절(:116 장식 킷 리뷰 표기) 갱신
  (환경) .git/config                             — 첫 sparse-checkout init 시 extensions.worktreeConfig=true 추가(CLAUDE.md §3-1)
새로 만들 것:
  목적: 비-Unity sparse worktree 규칙과 unity-reviewer 파견 기준 개편을 합의하기 위해
  경로: docs/PROPOSAL_SPARSE_WORKTREE_AND_REVIEWER.md (본 초안)
  형태: 문서 (제안서·합의 요청서)
검증 방법:
  문서·도구 검증 (CLAUDE.md §9-2 적용 범위 — Assets/·Packages/·ProjectSettings/ 미접촉):
    ① 커밋 전: git status --porcelain=v1 -uall + git diff --name-only --cached dev + git diff --name-only dev,
       병합 전(기능 브랜치): git diff --name-only "$baseDev...$featHead",
       병합 직후(dev): git diff --name-only "$baseDev" HEAD 출력 전체로 Unity 미접촉·허용 목록 내 변경 확인
    ② 새로 쓴 § 참조(§0, §3-1, §4-4, §7, §7-5, §7-6, §9-2, §9-4 등)와 "리뷰 규모" 제목 해석 확인
    ③ powershell -ExecutionPolicy Bypass -File tools/sync-agents.ps1 -Check → exit 0,
       git diff -- .codex 에 unity-reviewer.toml 변경만 있는지 확인
    ④ (Q3 (a)) 훅·도구 모의 입력 시험:
       - guard.ps1: 메인↔linked·linked↔linked 3방향 루트 보호 파일(CLAUDE.md, docs/RULES/BRANCHING.md,
         Packages/manifest.json, ProjectSettings/ProjectVersion.txt) → deny/ask; worktree list 실패 → deny(fail-closed)
       - guard.ps1 git 차단: bare worktree add, worktree add/remove -f, checkout/switch --ignore-other-worktrees,
         메인 sparse-checkout, sparse-checkout disable/add/--stdin, Q1 승인 패턴과 다른 set(부분집합 set docs,
         부정 패턴 빠진 set --no-cone '/*', 금지 폴더 포함) → deny; 정상 --no-checkout·승인 패턴 정확 일치 set → pass
       - after-agents-edit.ps1: 다른 트리 .claude/agents/*.md 편집 시 그 트리를 검사하는지 확인
       - sync-agents.ps1 -Check: 대상 .codex/agents 폴더 부재 시 exit 1 이며 폴더를 만들지 않는지 확인
       - agy-run.ps1: linked worktree 에서 공유 config·hooks 및 config.worktree 변조 감시 exit 12 시험
    ⑤ (실효 전 필수 실측) 훅 보강 후 실제 리포에서 비-Unity sparse worktree 1회 생성→검증→정리:
       - Q1 (a) 면 docs/images/ 12개 오프라인 LFS smudge 확인 / Q1 (b) 면 docs/images/·.vscode/ 미체크아웃 확인
       - 금지 4폴더 부재, 3도구 훅 실행·Codex /hooks 신뢰 확인
    ⑥ (분류 경계 점검) 축 1·축 2 표에 경계 사례(K4 리뷰 전 5cb1466, K1 1차·K3 1차 select-tests 추가,
       K1 2차 프리팹 diff, #17 사후 대입 4929f73/9639fac, #27 e896b5a 신규 594줄 빌더, Net/RaceWire 1줄,
       Flow 50줄, TestLogs 1줄, 신규 테스트 추가만, Packages/ProjectSettings 전용, select-tests 단독 비-Unity,
       Antigravity Tier 4, Codex 메인 세션, claude+codex 혼합 일반 및 하네스)를 대입해 공백·충돌 0 확인
안 하는 것:
  Unity·Blender 작업용 워크트리 허용, Assets/·Packages/·ProjectSettings/·art-src/·tools/select-tests.ps1 의 워크트리 작업,
  Assets/·Packages/·ProjectSettings/·art-src/ 변경,
  tools/harness-cost.py 다중 워크트리 자동 합산, tools/agent-relay.ps1 -Role fix 도입,
  파견 0회 에이전트 5종 정리(HANDOFF 2절 7번:65 의 별도 후보)
```

---

## 4. 검토 기록 및 대응 (1·2·3차 Claude · Codex)

### 4-1. 1차 검토 (v1 대상, 2026-11-27)
- **Claude (`sw-rev-c1`, `NEEDS_FIX`, 24건)** · **Codex (14건)**: `Runtime/Net/`·`Control/`·결정성 루트 파일, A/B/D 공백, 테스트 단언 vs D② 모순, `agy-run` linked `.git` 감시·`for-each-ref` 간섭, `dev` 병합 전제, `guard` 양방향(`worktree list --porcelain -z`)·파괴/우회 차단, `after-agents-edit` 대상 트리, `sync-agents -Check` 비쓰기, 래퍼 3종 분리, K4 반례, 부록 A 추가 등 전부 v2 에 반영.

### 4-2. 2차 검토 (v2 대상, 2026-11-27)
- **Claude (`sw-rev-c2`, `NEEDS_FIX`, 높음 1·중간 6·낮음 7)** · **Codex (치명 0·높음 4·중간 4)**: 축 1(Q4×Q5 4조합)·축 2(독립 검토자) 분리, Q1(b) 분기·`.vscode/` 제외, `Assets/` 읽는 `tools/` 3종·`index/` 메인 전용 제외, `guard` bare `worktree add`·메인 sparse·`--stdin`·fail-closed, `dev` 제3 트리 중단·병합 허용 목록 검사, `CLAUDE.md:242` 정합, `agy-run` `config.worktree` 감시·`agent-relay` 잠금 겸유, K4 `5cb1466` 90줄·`m6-sound-cues` 실측·R8 병기·부록 #6/#11/#12 원문 전사 등 전부 v3 에 반영.

### 4-3. 3차 최종 확인 (v3 대상, 2026-11-28)
- **Claude (`sw-rev-c3`, 치명·높음 0, 중간 3·제안 6)** — 전부 v4 에 반영:
  - 중간 3: 병합 직후 diff 명령 `HEAD^1 HEAD`/`$baseDev HEAD` 정정, `guard` 승인 패턴 정확 일치 검증(부분집합·부정 패턴 누락 차단), Tier 1 ⑤(`select-tests.ps1`)와 K1 1차·K3 1차 비용 근거 정합.
  - 제안 6: `select-tests.ps1` 단독 비-Unity 1줄 명시, Q5(b) 문구·표 일치, Q1(b) `.vscode/` 표현 명확화, Codex 메인 세션 relay 명시, `agy-run` 보강 조건부 문구 정리, R8(`:562`)·#15(`:1241,:1245`) 인용 정밀화.
- **Codex (치명 0, 높음 4·중간 5)** — 전부 v4 에 반영:
  - 높음 4: Q4×Q5 단일 원천화(축 2·§7-6 문안이 축 1 표 참조), Codex 자동 1회·시각 작업 예외·혼합 하네스(사용자 1명 겸임 금지)·`Packages/`/`ProjectSettings/` 전용 규칙 보완, 생성 절차 `sparse-checkout list` 정렬 집합 비교(`.agents,.claude,.codex,docs,tools`) + no-cone 전체 패턴 일치, 병합 전후 `$baseDev`/`$featHead` 고정·스테이지/작업 트리 양쪽 검사·금지 4폴더 검사 sparse 트리 한정.
  - 중간 5: `agy-run` 쓰기 금지 주체(외부 세션) 명시, 생성 실패 시 `worktree add` 소유·무시 파일 확인 및 부분 체크아웃 중단 분기 추가, relay `-PromptFile` 의 `-IncludeWorktree` 상태 및 `select-tests -Range` 고정 범위 일치, `#17 m6-sound-cues` 사후 대입용 SHA(`4929f73`/`9639fac`)·Tier 1 ⑤ 해당 표기, `#27 m3-finger-exterior` 중대 1(`DECISIONS-2026-10.md:453`, 신규 594줄 빌더 → Tier 1 ④) 반영해 아트 3/11 정정.

---

## 부록 A. `unity-reviewer` 실측 표본 28건 전체 (2026-10-26 `리뷰 규모` 도입 이후 ~ 2026-11-27)

> 기준: **2026-10-26 `리뷰 규모` 규칙 도입(`docs/archive/DECISIONS-2026-10.md:557`) 이후** `docs/DECISIONS.md` 와 `docs/archive/DECISIONS-2026-10.md` 에 `unity-reviewer` 토큰 수가 기록된 병합 전 파견 28건(2026-11-15 Bash 제거 시험 파견 `49,016` `DECISIONS.md:823` 제외).
> 합계 `2,726,301` · 최소 `41,530` · 중앙값 `85,584` · 평균 `97,368` · 최대 `225,688`.
> *(참고: 규칙 도입 직전 같은 날 R8 `78,161` 토큰(`DECISIONS-2026-10.md:562`)을 포함하면 29건 · 합계 `2,804,462` · 평균 `96,706`.)*

| # | 날짜 | 브랜치 | 영역 | 구현자 | 토큰 | 리뷰 결과 (치명/중대/보통/제안 및 핵심) | 근거 |
|---:|---|---|---|---|---:|---|---|
| 1 | 11-27 | `feature/race-catchup` | sim | claude | 98,311 | 0/0/0/6 — **결함 후보 1**(스윙 2배 가속 불연속, 단독) | `DECISIONS.md:44` |
| 2 | 11-26 | `feature/bot-draft-pass` | bot | claude | 70,604 | 0/0/0/3 — 테스트 공백 1(고침), Codex 결함 3 | `DECISIONS.md:83` |
| 3 | 11-26 | `feature/race-draft` | sim+hud | claude | 121,743 | 0/0/1/6 — 실제 결함 0, Codex 결함 3 | `DECISIONS.md:120` |
| 4 | 11-23 | `feature/lobby-bright-theme` | ui | codex+claude | 89,090 | 0/0/0/5 — 틈 1(Codex 중복), Codex 결함 3 | `DECISIONS.md:276` |
| 5 | 11-20 | `feature/k11-summit-cabins` | art | claude | 91,163 | 0/0/0/6 — 박공 z-fighting 제안 1, Codex 결함 3 | `DECISIONS.md:477` |
| 6 | 11-16 | `feature/mp2-bot-difficulty` | bot+sim | codex+claude | 148,624 | 0/0/2/9 — `ShortcutBot` 이동 규칙 지적, Codex 결함 1 | `DECISIONS.md:806` |
| 7 | 11-14 | `feature/mp2-contact` | sim | codex+claude | 119,189 | 0/0/2/8 — **결함 2**(Codex 와 독립 중복) | `DECISIONS.md:894` |
| 8 | 11-13 | `feature/mp2-bot-race` | race | codex+claude | 178,403 | 0/0/1/7 — **`select-tests` 누락**(보통 1), Codex 결함 1 | `DECISIONS.md:993` |
| 9 | 11-12 | `feature/mp1-race-field` | race | claude | 80,779 | 0/**1**/0/4 — **상대 조립 실패**(Codex 중복) | `DECISIONS.md:1111` |
| 10 | 11-11 | `feature/art-roadsidekit-skyway` | art | claude | 52,718 | 0/0/1/0 — 주석 수치 오기 *(프리팹 diff >30줄)* | `DECISIONS.md:1173` |
| 11 | 11-11 | `feature/art-roadsidekit` | art | claude | 64,205 | 0/0/1/2 — 문서 수치 오기·제안 2 *(K1 1차, `select-tests` 추가)* | `DECISIONS.md:1184` |
| 12 | 11-11 | `feature/art-treekit-palm` | art | claude | 54,076 | 0/0/0/3 — 문서·검사 제안 3 | `DECISIONS.md:1195` |
| 13 | 11-11 | `feature/art-treekit-finger` | art | claude | 41,530 | 0/0/0/2 — 문서 줄 번호 *(프리팹 diff >30줄)* | `DECISIONS.md:1216` |
| 14 | 11-11 | `feature/art-treekit` | art | claude | 57,766 | 0/0/0/4 — `KitMesh` 검사 제안 *(K3 1차, `select-tests` 추가)* | `DECISIONS.md:1226` |
| 15 | 11-10 | `feature/art-rockkit` (K4) | art | claude | 73,407 | 0/**1**/2/6 — **중대 1**(예외 배치 무로그 §5; 리뷰 전 `5cb1466` 90줄 + `select-tests.ps1`) | `DECISIONS.md:1241, :1245` |
| 16 | 11-09 | `fix/audio-recording-failures` | audio | claude | 68,294 | 0/0/1/8 — **무음 구간 결함**(Codex 중복) | `DECISIONS.md:1376` |
| 17 | 11-07 | `feature/m6-sound-cues` | audio | claude | 77,551 | 0/0/2/6 — **`Awake` 예외 처리**(§5; 사후 대입 `4929f73`/`9639fac` Audio 340+시뮬 41+테스트 기대값) | `DECISIONS.md:1430` |
| 18 | 11-06 | `feature/m6-open-course` | sim+track | codex+claude | 225,688 | 0/**2**/5/4 — **중대 2**(빌드·침범 범위, 단독) | `DECISIONS.md:1520` |
| 19 | 11-05 | `feature/m6-finger-sound` | audio | claude | 105,033 | 0/0/2경미/5 — 실제 결함 0 | `DECISIONS.md:1590` |
| 20 | 11-04 | `feature/m6-wall-boost-heading` | sim | codex+claude | 141,867 | **실제 결함 2 중 1 단독**(2 m 캐스트) + 규칙 1 | `DECISIONS.md:1642` |
| 21 | 11-03 | `feature/m6-stadium-detail` | art | codex+claude | 118,462 | 0/**1**/0/5 — **중대 1**(0정점 잔디 메시 §5, 단독) | `DECISIONS.md:1685` |
| 22 | 11-02 | `feature/m6-map-design` | art+track | claude | 172,276 | 0/0/1/2 — 테스트 로그 형식 | `DECISIONS.md:1793` |
| 23 | 10-29 | `feature/m4-control-feedback` | ui | codex+claude | 86,458 | **실제 결함 1 단독**(`Update`/`LateUpdate` 순서) | `DECISIONS-2026-10.md:241` |
| 24 | 10-28 | `fix/finger-exterior-tool` | art | claude | 82,922 | 0/0/0/3 — 문서·후속 제안 | `DECISIONS-2026-10.md:379` |
| 25 | 10-28 | `feature/m3-finger-sbend` | track | claude | 101,274 | 0/0/0/2 — 계산 검산·후속 제안 | `DECISIONS-2026-10.md:392` |
| 26 | 10-27 | `fix/swing-rope-acceleration` | sim | claude | 67,241 | 0/0/0/4 — 새 테스트 단언 약함 제안 1 | `DECISIONS-2026-10.md:407` |
| 27 | 10-27 | `feature/m3-finger-exterior` | art | claude | 84,710 | 0/**1**/1/3 — **중대 1**(`ValidateClearance` 면제 §5, `e896b5a` 신규 594줄 빌더) + 보통 1 | `DECISIONS-2026-10.md:452-455` |
| 28 | 10-26 | `feature/m2-screen-settings` | ui | claude | 52,917 | 실제 결함 0 · 오탐 1 · 의견 5 | `DECISIONS-2026-10.md:550` |
