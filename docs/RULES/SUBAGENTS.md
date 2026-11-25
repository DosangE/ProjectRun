# 서브에이전트 운영 — CLAUDE.md §7-1 · §7-2 · §7-4 · §7-5 · §7-6

> **CLAUDE.md 의 일부다. 구속력은 CLAUDE.md 와 같다.** § 번호는 CLAUDE.md 의 번호를 그대로 쓴다.
> **읽는 시점**: 서브에이전트를 파견하거나 Codex·Antigravity 를 부르기 전, 그 둘로 작업하거나 교차 검토하기 전.
> 파견할지 말지(§7-3 파견 기준)와 **병렬 절대 금지**는 CLAUDE.md §7 에 있다. 파견 여부는 그쪽에서 먼저 판단한다.
> 이 내용을 고칠 때는 이 파일만 고친다. CLAUDE.md 에 사본을 두지 않는다.

### 7-1. 7종

| 에이전트 | 작업 | 쓰기 | 모델 | 노력 |
|---|---|---|---|---|
| `unity-explorer` | 탐색 | ❌ | `claude-opus-5-5` | high |
| `unity-architect` | 구조 분석 | `docs/` 만 | `claude-opus-5-5` | high |
| `unity-reviewer` | 리뷰 (§0/§1/§5 체크리스트) | ❌ | `claude-opus-5-5` | high |
| `unity-implementer` | 생성 | `Assets/` | `claude-opus-5-5` | high |
| `unity-verifier` | 검증 (Unity MCP) | ❌ | `claude-opus-5-5` | high |
| `blender-artist` | 모델링 (Blender MCP) | `art-src/` 만 | `claude-opus-5-5` | high |
| `gameplay-designer` | 기획 (피드백 → 가설·변경안·플레이테스트 설계·요청서 초안) | ❌ | `claude-opus-5-5` | high |

- `blender-artist` 는 사용자의 열린 Blender 하나에 붙는다. 같은 Blender 를 두 에이전트(Claude·Codex 포함)가 동시에 쓰지 않는다. 결과를 `Assets/` 로 옮기는 것은 승인받은 `unity-implementer` 작업이다 (`docs/DECISIONS.md` 2026-11-09 "Blender 아트 에이전트").

- `gameplay-designer` 를 쓰는 때: 플레이 피드백이 들어왔거나 구현 전에 선택지가 갈릴 때. 파견 여부는 §7-3(사용자가 지시할 때)을 따른다. 파견문에는 `git rev-parse HEAD` 값을 적는다 — 이 에이전트는 Bash 가 없어 인덱스 신선도를 스스로 확인하지 못한다. 역할 경계와 응답 형식은 정의(`.claude/agents/gameplay-designer.md`)가 원본이다 (`docs/DECISIONS.md` 2026-11-09 "기획 에이전트").

- 읽기 전용 에이전트(`unity-explorer`·`unity-reviewer`·`gameplay-designer`)는 Bash 가 없다. git 값은 메인이 파견문에 넣는다 (`docs/DECISIONS.md` 2026-11-15 "읽기 전용 에이전트의 Bash").
  - `unity-reviewer`: 메인이 `git diff <base>...HEAD` 를 scratchpad 파일로 저장하고, 파견문에 그 **파일 경로와 base·HEAD 해시**, 작업 트리가 HEAD 와 같은지를 적는다. 없으면 리뷰어는 리뷰하지 않고 되묻는다. 병합 전 리뷰(§7-5 "리뷰 규모")는 그 브랜치를 체크아웃하고 `Assets/` 에 커밋 안 한 변경이 없는 상태에서 파견한다 — 작업 트리가 HEAD 와 다르면 리뷰어가 `리뷰 미완료` 로 답하고, 그 결과는 병합 리뷰로 세지 않는다.
  - `unity-explorer`: 인덱스(`index/symbols.tsv`)를 쓰게 할 때만 `git rev-parse HEAD` 값을 적는다. 없으면 코드 `Grep` 으로 찾는다.

- 모델·노력은 `.claude/agents/<이름>.md` frontmatter 의 `model`·`effort` 가 원본이고 이 표는 그 요약이다. 별칭(`opus`) 대신 전체 ID 로 고정해 별칭이 가리키는 모델이 바뀌어도 따라가지 않는다 (`docs/DECISIONS.md` 2026-10-10 "서브에이전트 모델·노력 고정").

### 7-2. 병렬 / 직렬

**병렬 OK** — 읽기 전용이고 영역이 겹치지 않을 때만.
- 탐색 여러 갈래, 파일별 리뷰, 서로 다른 시스템 조사

**직렬 필수** — 앞의 결과가 뒤의 입력일 때.
- `구조 분석 → 생성 → 검증`. 뒤집으면 근거 없는 코드가 나온다.
- `리뷰 → 수정 → 재검증`

### 7-4. 파견할 때 지키는 것

- 에이전트는 이 대화를 모른다. 파견 프롬프트에 **경로·범위·판정 기준·안 할 것**을 전부 적는다. "아까 그거" 같은 지시는 통하지 않는다.
- 돌아온 결과를 그대로 믿지 않는다. **`경로:줄` 근거가 없는 주장은 되묻는다.** → §4-1
- 에이전트가 규칙 위반을 저질렀으면 결과를 버리고 보고한다. 조용히 고쳐 쓰지 않는다.

### 7-5. Codex

`codex` 플러그인(`/codex:*`)은 다른 모델 계열의 에이전트를 부른다. 구현자(Claude)와 다른 눈이 필요한 곳에만 쓴다 (`docs/DECISIONS.md` 2026-10-26 "Codex 역할"). Codex 쪽 설정·훅 사실은 `AGENTS.md` 에 있다.

**쓰는 곳** — 넷 다 읽기 전용이다.

| 용도 | 명령 (`<cc>` = 플러그인의 `scripts/codex-companion.mjs`) | 언제 | 에이전트가 스스로 부르나 |
|---|---|---|---|
| 계획 반박 | `node <cc> adversarial-review --wait <초점>` | §3-2 합의 요청서를 `docs/` 파일로 둔 뒤, 승인 **전에**. 반박과 그에 대한 답을 요청서에 붙여 승인을 받는다 | 사용자가 지시할 때만 |
| 설계·정합성 리뷰 | `node <cc> adversarial-review --wait --base dev <초점>` | `feature → dev` 병합 전. §0/§1/§5 체크리스트는 아래 "리뷰 규모" 대로 맡는다 | **브랜치당 1회는 지시 없이 부른다.** 다시 부르는 것은 지시가 있을 때만 |
| 넓은 탐색 | `node <cc> task --wait "<읽기 전용 …>"` | §7-3 "파견한다" 에 해당할 때(독립 영역 3개 이상 등). 1–2 파일은 직접 읽는다 — Codex 는 호출 한 번의 고정비가 크다(2026-10-26 T1: 파일 2개에 입력 407k) | 사용자가 지시할 때만 (§7-3) |
| 막혔을 때 진단 | `node <cc> task --wait "<읽기 전용 …>"` | §4-5 에서 3번 실패한 뒤, 보고하는 다음 후보 중 하나로 | 사용자가 지시할 때만 |

- **에이전트는 스크립트를 Bash 로 직접 부른다.** 플러그인 에이전트 `codex:codex-rescue` 와 `/codex:rescue` 는 쓰지 않는다. 전달만 하는 데 Claude 토큰이 든다(2026-10-26 T1 약 29k). 게다가 실패하면 "return nothing"(`agents/codex-rescue.md`)이라 빈 결과가 돌아온다. `<cc>` 위치는 플러그인 설치 경로라 머신마다 다르다. 실행할 때 구한다 (CLAUDE.md 맨 위).
- **`task` 에 `--write` 를 붙이지 않는다.** 없으면 `read-only` sandbox 로 돈다(`codex-companion.mjs` `sandbox: request.write ? "workspace-write" : "read-only"`). 리뷰는 늘 `read-only` 다.
- 사용자는 같은 일을 슬래시 명령(`/codex:adversarial-review`·`/codex:review`·`/codex:rescue`)으로 직접 할 수 있다.
- Codex 결과도 §7-4 와 같이 다룬다. `경로:줄` 근거가 없는 주장은 되묻는다.
- **Codex 가 실패하면 시끄럽게** (§5): 호출이 0 이 아닌 코드로 끝나거나, 오류를 내거나(사용량 소진 포함), 결과가 비면 그 원문을 보고에 `Codex 리뷰 못 함: <원문>` 으로 적는다. 리뷰 없이 병합할지는 사용자에게 묻는다. 조용히 건너뛰거나 Claude 리뷰만으로 대신했다고 쓰지 않는다.
- **리뷰 규모**: §0/§1/§5 체크리스트를 누가 맡을지는 `dev...HEAD` 의 `Assets/` 변경(`.meta` 제외)으로 정한다 (`docs/DECISIONS.md` 2026-10-26 "리뷰 규모").
  - **작은 변경** — `git diff --name-status` 가 `M` 뿐이고(새 파일·삭제·이동 없음), 공개 시그니처가 바뀌지 않았고, `git diff --numstat` 의 추가+삭제 합이 30줄 이하: `unity-reviewer` 를 파견하지 않는다. 메인이 `.claude/agents/unity-reviewer.md` 체크리스트를 바뀐 줄에 대조하고 병합 보고에 항목별 결과(`위반 없음` 또는 `경로:줄`)를 적는다. Codex 설계 리뷰 1회는 그대로 한다.
  - **그 밖** — `unity-reviewer` 파견 + Codex 설계 리뷰.
- **시험 중 (리뷰 분담 비교)**: `Assets/` 를 바꾼 병합 보고에 두 리뷰의 지적(실제 결함 / 규칙 위반 / 오탐)과 비용을 나란히 적는다. 비용은 `unity-reviewer` 의 `subagent_tokens`(작은 변경이면 "메인 대조, 별도 수치 없음")와 Codex 리뷰 전후 `/status` 의 5시간 `used_percent`(사용자가 읽어 준다. 움직이지 않으면 "변화 없음")다. 비교는 "그 밖" 브랜치만 센다. 두 브랜치를 비교한 뒤 분담을 다시 정하고 이 줄을 지운다.

**지키는 것**
- **Codex 호출은 서브에이전트 파견이다.** §7-3(사용자가 지시할 때만 — 위 표의 병합 전 리뷰 1회만 예외)과 §3-4(`승인:`·`브랜치:` 줄을 요청 첫머리에)를 그대로 적용한다. 플러그인 에이전트 `codex:codex-rescue` 의 설명("Proactively use")은 이 프로젝트에서 따르지 않는다.
- **쓰기 작업은 맡기지 않는다.** Codex 에 쓰기를 맡기는 것은 그 자체가 §3 합의 대상이다. Codex 기본 지침은 "되돌릴 수 있는 작업과 수정은 허락을 묻지 말고 진행" 쪽이라(2026-10-26 세션 기록 `base_instructions`) §3 과 방향이 반대다.
- **합의해서 쓰기를 맡길 때는 `tools/codex-unity.ps1 -PromptFile <파일> -Write` 로만 부른다** (Unity MCP 허락이 없어도. `docs/DECISIONS.md` 2026-11-14 "Codex 샌드박스 계약"). companion 에 `--write` 를 직접 붙이지 않는다. `--write` 샌드박스는 `.git`·`.codex`·`.agents` 가 읽기 전용이고 승인 정책이 `never` 라 권한을 올릴 수 없기 때문이다.
  - **지시서**: git 쓰기 명령이나 `.git/`·`.codex/`·`.agents/` 경로를 적지 않는다. 적으면 스크립트가 Codex 를 띄우지 않고 종료 코드 2 로 거부한다(`codex-unity.ps1:131`). 파일 이동·이름 바꾸기는 Claude 가 먼저 `git mv` 로 하고(`.meta` 포함), 지시서에는 `CLAUDE-GIT-DONE: <실행한 명령>` 줄로 알린다.
  - **계약**: 스크립트가 `-Write` 지시서 앞에 계약 블록을 붙인다(`codex-unity.ps1:114`). git 쓰기는 실행하지 말고 `CLAUDE-GIT:` 줄로 보고한다. 질문으로 턴을 끝내지 않는다(범위 밖일 때만 `CLAUDE-QUESTION:`). 막히면 `CLAUDE-BLOCKED:` 와 에러 원문을 적는다. 마지막 줄은 `CLAUDE-STATUS: done|git|question|blocked` 하나다.
  - **기다리기**: 실행마다 새 `-RunId <이름>` 을 준다(이어 가기 포함). 스크립트를 `run_in_background` 로 띄우고, 기다리는 쪽은 `tools/codex-wait.ps1 -Result <지시서>.<RunId>.result.json` 을 따로 `run_in_background` 로 둔다(기본 무활동 10분·상한 90분). 백그라운드 껍데기의 완료 알림만 믿지 않는다(2026-11-13 Codex 가 끝난 뒤 36분 "돌고 있음").
    - `codex-wait.ps1` 은 결과 파일이 생기면 0, 활동 신호(Codex 세션 로그 크기·`Editor.log` 크기·`git status`·`-Watch` 파일 크기)가 10분 동안 하나도 바뀌지 않으면 3, 상한이면 4 로 끝난다(`docs/DECISIONS.md` 2026-11-14 "Codex 무활동 알림"). 3 은 알리기만 한다 — Codex 를 멈추지 않는다. 세션 로그는 이 실행 것만 센다: 새 실행은 스크립트를 띄우기 2분 전부터 생긴 로그(그래서 `codex-unity.ps1` 과 같은 턴에 띄운다), `-ResumeLast` 이어 가기는 앞 결과의 `threadId` 를 `-Thread` 로 준다. `Editor.log`·`git status` 는 다른 세션도 바꿀 수 있어, 같은 트리에서 다른 세션이 일하면 무활동을 늦게 잡을 수 있다.
    - 3·4 를 받거나 코드 11·12·13 결과가 오면 보고 **첫 줄**에 `Codex 멈춤: <이유> / <한 조치>` 를 쓴다. 3 은 출력의 마지막 Codex 이벤트를 붙이고, 사용자가 멈추라고 하지 않으면 같은 명령으로 다시 기다린다.
    - companion 리뷰(`adversarial-review`)는 세션 로그를 남기지 않는다. stderr 를 파일로 받아(`> <out> 2> <err>; echo done > <done>`) `-Result <done> -Watch <err>` 로 기다린다 — stderr 에 단계마다 `[codex] Running command` 줄이 쌓인다.
    - 결과 파일은 맨 마지막에 한 번에 생긴다. 스크립트가 강제 종료되면 감시 프로세스가 `13 killed` 로 남긴다.
    - 같은 자리에 `.sent.md`(보낸 지시서)·`.out.md`(Codex 마지막 메시지)·`.stderr.log` 가 남는다.
    - 같은 `RunId` 의 파일이 이미 있으면 스크립트는 그 파일을 지우지 않고 곧바로 멈춘다. 지시서가 없을 때와 `RunId` 가 잘못됐을 때도 결과 파일 없이 곧바로 멈춘다. 그래서 `codex-wait.ps1` 에 상한이 있고(4), 껍데기가 결과 파일 없이 끝났으면 그 출력 원문을 13 으로 보고한다.
  - **판정은 `-Write` 실행에만**: 계약을 붙이지 않은 읽기 실행은 답이 오면 0(`answer`), 아니면 13 이다.
  - **종료 코드별 복구** (`codex-unity.ps1:144` 의 판정. 마지막 줄의 `CLAUDE-STATUS` 를 먼저 보고, 그 줄과 표지 줄이 맞지 않으면 12 다)

    | 코드 | 뜻 | Claude 가 하는 것 |
    |---|---|---|
    | 0 | 끝남 | 결과를 §7-4 대로 검토한다 |
    | 2 | 지시서 거부, Codex 안 띄움 | git 쓰기는 직접 하고, 지시서를 고쳐 다시 부른다 |
    | 10 | `CLAUDE-STATUS: git` 과 `CLAUDE-GIT:` 줄 | 승인 범위 안의 명령만 실행하고, 실행한 것을 `CLAUDE-GIT-DONE:` 줄로 적은 지시서를 `-ResumeLast` 로 보낸다. 범위 밖이면 멈추고 보고한다 |
    | 11 | `CLAUDE-STATUS: question` 과 `CLAUDE-QUESTION:` 줄 | 지시서 범위 안의 세부면 답하고 `-ResumeLast` 로 이어 간다. 한 작업에 최대 2회. 범위 밖이거나 3번째면 사용자에게 묻는다 |
    | 12 | 막힘, 또는 계약 위반(마지막 줄이 `CLAUDE-STATUS` 가 아님 — 물음표로 끝난 맨 질문 포함, 상태와 표지 줄이 맞지 않음, `.git` 쓰기 거부가 보이는데 done) | 이어 가지 않고 `.out.md` 원문을 보고한다 |
    | 13 | 오류·사용량 소진·강제 종료 | 이어 가지 않고 `.stderr.log` 와 `reason` 원문을 보고한다(위 "Codex 가 실패하면 시끄럽게") |
- **Unity 에디터는 한 번에 한쪽만 쓴다.** Claude 와 Codex 는 같은 relay 로 같은 에디터에 붙는다. 한쪽이 Unity MCP 를 쓰는 동안 다른 쪽은 쓰지 않는다 (CLAUDE.md §7 병렬 절대 금지 2번).
- **Codex 의 Unity MCP 는 사용자가 그 작업에서 허락할 때만 연다** (사용자 2026-11-06 "쓸 순 있지만 내가 너에게 명령할때 codex로 구현하고 mcp 활용하라고 사용 허락할때만"). "codex로 구현" 만으로는 허락이 아니다. 허락이 있으면 `tools/codex-unity.ps1 -PromptFile <파일> [-Write] -Unity` 로 부르고, 지시서에 `Unity MCP: 이 실행에서 허용` 줄과 쓸 수 있는 도구를 적는다. 허락이 없으면 `-Unity` 를 빼고(쓰기면 위 스크립트, 읽기 전용이면 companion 을 그대로) 지시서에 `Unity MCP: 허용 안 됨` 을 적는다. 스크립트는 `-Unity` 를 준 실행 동안만 `~/.codex/config.toml` 의 도구 승인을 `approve` 로 두고 끝나면 되돌린다. 서버 이름은 머신마다 달라(`unity_mcp`, `unity-mcp`) relay 를 쓰는 서버를 찾아 쓴다. 스크립트가 강제 종료돼도 따로 띄운 감시 프로세스가 지운다. 전원이 끊긴 뒤에는 `-Status` 로 보고 `-Close` 로 닫는다. 승인을 설정에 남겨 두지 않는다.
- **Codex 의 Blender MCP 는 평소 꺼 두고, 사용자가 그 작업에서 허락할 때만 한 실행 동안 연다** (사용자 2026-11-14 "codex로 구현하라" + 방식 "가"). `~/.codex/config.toml` 에 `[mcp_servers.blender]`(`uvx mcp-for-blender@2.2.7`, env `BLENDER_MCP_SAFE_MODE=1`·`DISABLE_TELEMETRY=true`)를 `enabled = false` 로 등록해 둔다(`codex mcp list` 에 `disabled`). 사용자 설정이라 PC 마다 등록한다 — `D:/PCUBE` PC 는 2026-11-14. 허락이 있으면 `tools/codex-unity.ps1 -PromptFile <파일> [-Write] -Blender` 로 부르고, 지시서에 `Blender MCP: 이 실행에서 허용` 줄을 적는다. 스크립트는 그 실행 동안만 서버를 `enabled = true` 로 바꾸고 `blender-artist` 와 같은 도구 4개(`get_addon_status`·`get_scene_info`·`execute_blender_code`·`look`)에 `approve` 를 준 뒤 되돌린다. 외부 에셋·생성형 도구(`search_assets`·`import_asset`·`generate_3d`)는 승인하지 않는다. 닫는 방식(finally·감시 프로세스·`-Status`/`-Close`)과 표지는 `-Unity` 와 같아서, 한쪽이 열려 있으면 다른 실행은 열지 못하고 오류로 끝난다. 설정 파일의 검사·쓰기·복원은 잠금 하나(`Local\protoharness-codex-unity` 뮤텍스)로 묶고 임시 파일을 옮겨 쓴다. 실행의 finally·감시 프로세스는 자기가 소유한 문만 닫고, `-Close` 는 소유와 상관없이 닫는다. Blender 는 하나다 — Codex 가 쓰는 동안 Claude(메인·`blender-artist`)는 Blender 를 부르지 않는다. 지시서에는 `blender-artist` 정의의 쓰기 범위·세이프 모드·저장 규칙을 옮겨 적는다(Codex 는 그 정의를 읽지 않는다).
  - **사용자가 Codex 를 직접 쓸 때**(Claude 한도·토큰 불균형): 설정은 `enabled = false` 그대로 두고 그 세션만 `codex -c mcp_servers.blender.enabled=true` 로 켠다(`-c` 는 그 실행에만 `config.toml` 값을 덮어쓴다). 자주 쓰면 같은 값을 `~/.codex/<이름>.config.toml` 에 두고 `codex -p <이름>` 으로 얹어도 된다. 설정 파일의 `enabled` 를 손으로 `true` 로 바꿔 두지 않는다 — 닫힌 동안에도 `search_assets`·`generate_3d` 가 목록에 남는다. 대화형이라 도구 승인은 사용자가 그때마다 한다. 이때도 Blender 는 하나다 — Claude 는 그동안 Blender 를 부르지 않는다 (`docs/DECISIONS.md` 2026-11-14 "Codex 직접 사용 때 Blender MCP").
  - 실측 (2026-11-14, `D:/PCUBE`, codex-cli 0.165.0, 읽기 실행 `-Effort low`): ① 닫힘 — `blender` 서버가 도구 목록에 없음 ② `-Blender` — `get_addon_status` 성공(`5.2.3 LTS`, 텔레메트리 false, 라이브러리·생성기 off), 승인 안 한 `search_assets` 는 `MCP tool call requires approval, but approval policy is never` ③ 다시 닫힘 — 서버 없음. 실행 뒤 설정 파일은 열기 전과 같았다(`-Status` `closed`).
- **stop 리뷰 게이트(`/codex:setup --enable-review-gate`)는 켜지 않는다.** 파일을 고친 턴마다 끝에서 Codex 리뷰가 돌고 최대 900초를 기다린다(플러그인 `hooks/hooks.json`). 켜려면 합의한다.

### 7-6. Antigravity·교차 검토

Antigravity(`agy`)는 세 번째 모델 계열(Gemini)이다 (`docs/DECISIONS.md` 2026-11-25 "Antigravity 편입과 교차 검토"). 설정·훅 사실은 `AGENTS.md` 에 있다. Claude·Codex 와 같은 규칙(`CLAUDE.md`, `docs/RULES/`)을 따르고 사본을 만들지 않는다.

**쓰는 곳** — 모두 사용자가 지시할 때만 쓴다.

| 용도 | 쓰기 | 지키는 것 |
|---|---|---|
| 조사 (외부 API·패키지 문서·레퍼런스가 필요한 작업) | ❌ | 출처 URL 을 붙인다. 코드에 대한 말은 `경로:줄` (§7-4) |
| 시각 검수 (렌더·스크린샷) | ❌ | 지적만 한다. 채택은 사용자가 정한다 |
| 막혔을 때 진단 (§4-5 에서 3번 실패한 뒤) | ❌ | 가설과 확인 방법을 나눠 적는다 |
| 작은 구현 (UI·`tools/`·`fix/`) | ✅ | §3 합의와 §9 브랜치. 시뮬·씬·프리팹·에셋은 맡기지 않는다 |

- 무인 실행(`/goal`·`/schedule`)과 병렬 쓰기는 하지 않는다. 예외는 아래 "Claude 가 agy 를 부를 때" 하나다. `--dangerously-skip-permissions`·Turbo·`always-proceed` 로 돌리지 않는다.
- 검토·검수는 **Gemini 모델로** 한다. Antigravity 안에서 Claude 모델을 고르면 다른 계열이 아니다.
- Antigravity 가 만든 커밋에는 `Assisted-by: Antigravity (<모델 이름>)` 줄을 붙인다. git 작성자가 사용자와 같아 이 줄이 없으면 누가 썼는지 남지 않는다.
- Unity·Blender MCP 는 사용자가 그 작업에서 허락할 때만 쓴다. 에디터와 Blender 는 한 번에 한쪽만 쓴다 (CLAUDE.md §7 병렬 절대 금지).
- 작업 트리에 쓰는 도구는 한 번에 하나다. 다른 도구로 넘길 때는 쓰던 쪽이 커밋하거나 남은 변경을 보고한 뒤 넘긴다.

**교차 검토** — 구현한 모델 계열은 그 변경의 검토자가 될 수 없다. 병합 메시지에 `구현:`·`검토:` 를 적는다 (CLAUDE.md §9-2).

| 구현 | 검토 |
|---|---|
| claude | codex. 시각 작업이면 antigravity 도 된다 |
| antigravity | claude (`.claude/agents/unity-reviewer.md` 체크리스트). §7-5 "리뷰 규모" 의 "그 밖" 이면 codex 도 |
| codex | claude |
| 하네스 파일 | 구현하지 않은 모델 **+ 사용자 승인** |

- 하네스 파일: `CLAUDE.md`, `AGENTS.md`, `.claude/settings*.json`, `.claude/agents/`, `.codex/`, `.agents/`, `tools/hooks/`. `tools/hooks/guard.ps1` 이 파일 편집 도구로 고치는 것을 Claude 는 ask, Codex·Antigravity 는 deny 로 잡는다. 셸로 쓰는 것은 잡지 못한다 — 셸로 돌아가지 않는다 (CLAUDE.md §3-4).
- codex 가 구현한 브랜치에서는 §7-5 의 병합 전 Codex 리뷰가 같은 계열이라 `검토:` 로 세지 않는다. Claude 검토는 메인이 체크리스트로 직접 한다(파견은 §7-3 대로 사용자가 지시할 때).
- 검토자는 지적만 하고 고치지 않는다. 고치는 것은 구현한 쪽이거나 사용자가 정한다.

**Claude 가 agy 를 부를 때 (`tools/agy-run.ps1`)** — 사용자가 agy 에 맡기라고 지시한 작업만 (`docs/DECISIONS.md` 2026-11-25 "Claude 가 agy 를 부르는 실행"). 사용자의 agy 창이 아니라 새 대화에서 돈다.
- 실행: `powershell -NoProfile -ExecutionPolicy Bypass -File tools/agy-run.ps1 -Role ask|fix -PromptFile <지시서> -RunId <이름> [-Allow "<고쳐도 되는 파일,…>"] [-Resume <앞 RunId>]`. 오래 걸리므로 `run_in_background` 로 띄운다.
- 지시서: `승인:`·`브랜치:` 줄(§3-4, §9-2)과 무엇을·어디까지·판정 기준(§7-4). agy 는 이 대화를 모른다.
- `ask` 는 읽기 전용, `fix` 는 `-Allow` 파일만 고친다. 둘 다 셸과 MCP 가 닫힌다. **시험·컴파일·git 은 Claude 가 한다.** 하네스·git·씬·프리팹·설정 파일은 `-Allow` 로 받지 않는다. 시뮬은 위 표대로 맡기지 않는다(스크립트는 판정하지 못한다).
- 실행하는 동안 Claude 도 작업 트리에 쓰지 않는다. 앞뒤 대조가 누가 바꿨는지 구분하지 못한다.
- 종료 코드: 0 끝남 / 2 거부 / 10 막힘·질문 / 12 계약 위반 / 13 실행 실패. **12 면 되돌리거나 이어 가지 않고** `result.json` 의 `violations` 를 사용자에게 보고한다. 10 은 승인 범위 안의 세부면 `-Resume` 으로 답하고, 범위 밖이면 사용자에게 묻는다. 13 은 원문을 보고한다.
- 왕복은 한 작업에 최대 2번이다(`-Resume` 이 막는다). 새 `-RunId` 로 같은 작업을 다시 시작해 늘리지 않는다(§3-4 쪼개기).
- 결과는 믿지 않고 확인한다(§7-4): diff 를 읽고, 시험을 돌리고, 통과하면 Claude 가 `Assisted-by: Antigravity (<모델 이름>)` 로 커밋한다. 병합은 사용자가 시킬 때만.
- 보지 못하는 것: `.gitignore` 가 무시하는 파일, 저장소 밖의 파일, 원격 저장소. `-Allow` 에 있는 파일의 폴더에 새로 만든 무시 대상 파일도 잡지 못한다.

**자동 중계 (`tools/agent-relay.ps1`, `review`/`ask` 전용)** — 비대화형 읽기 전용 교차 검토·질의와 결과(세션ID·토큰·비용)를 파일로 주고받는다 (`docs/DECISIONS.md` 2026-11-25 "Claude 비대화형 교차 검토 중계 스크립트"). 쓰기(`fix`)·자동 왕복 루프는 두지 않는다.
- 실행: `powershell -NoProfile -ExecutionPolicy Bypass -File tools/agent-relay.ps1 -Target claude -Role review|ask -Base dev -RunId <이름> [-PromptFile <파일>] [-IncludeWorktree] [-Resume <세션ID>]`
- 가드:
  - 동시 실행 잠금(`Local\protoharness-agent-relay` 뮤텍스), `--dangerously-skip-permissions`·`bypassPermissions` 금지.
  - `-Target claude`: `--restricted --strict-mcp-config --tools "Read,Grep,Glob" --permission-mode dontAsk --permission-prompts none --add-dir <산출물폴더>` (2026-11-25 실측: `Glob`·`Grep`·`Read` 3개만 남고 `Bash`·`Edit`·`Write`·MCP 도구 모두 제거됨).
  - `-Target agy`: 2026-11-25 실측에서 `agy --mode plan` 이 `write_file` 을 막지 못해 거부(exit 2)한다.
  - 기본 diff 는 시작 시 한 번만 확정한 `base`·`merge-base`·`HEAD` 전체 해시와 브랜치로 만들고(`merge-base..HEAD`), 그 값을 `result.json` 에 기록한다. 미커밋·미추적 포함은 `-IncludeWorktree` 를 줄 때만. `review` 이고 `-IncludeWorktree` 가 아닐 때 `merge-base..HEAD` 변경 파일 중 하나라도 미커밋 변경(스테이지 포함)이 있으면 `claude` 를 부르지 않고 exit 2. 호출이 끝난 뒤 `HEAD` 나 브랜치가 시작과 다르면 exit 12(`verdict` `null`). git 출력은 UTF-8 스트림으로 직접 읽는다.
  - 산출물 폴더(`%TEMP%\protoharness-agent-relay\<RunId>\`)는 잠금을 잡은 뒤에 존재를 검사하고 새로 만든다(이미 있으면 기존 내용을 손대지 않고 exit 2).
  - 종료 코드와 판정: 정상 `PASS` 와 정상 `NEEDS_FIX` 모두 exit 0(판정은 `verdict` 로 구분). `-Role review` 응답 마지막 줄에 `RELAY-VERDICT: PASS|NEEDS_FIX` 가 없거나 JSON 필수 필드가 빠지거나 실행 중 `HEAD`/브랜치가 바뀌면 exit 12. CLI 실패·git 오류·타임아웃·stdout/stderr 읽기 시간 초과·프로세스 트리 종료/확인 실패는 exit 13(잠금을 풀기 전에 원문을 stderr 와 `result.json` `error` 에 남김). `exitCode` 가 0 이 아니면 `result.json` 과 콘솔 요약의 `verdict` 는 `null`(빈 값)이다. 남는 한계: `taskkill /PID <부모PID> /T /F` 는 부모 프로세스가 먼저 끝난 뒤 남은 손자 프로세스를 잡지 못할 수 있다.
  - §7-6 교차 검토 표와 다른 조합(예: 시각 작업이 아닌 `claude` 구현을 `agy` 가 검토)은 병합의 독립 검토(`검토:`)로 세지 않는다.
  - `agy` 세션 안에서 부를 때는 샌드박스 밖 실행이 필요하다.
