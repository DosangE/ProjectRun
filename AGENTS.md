# ProtoHarness — 에이전트 지침 (Codex·Antigravity 진입점)

## 규칙 본문은 여기 없다

**작업 전에 `CLAUDE.md` 를 읽어라.** 금지선(§0), 목적·경로·형태(§1), 기존 자산 우선(§2),
구현 전 합의(§3), 추측 금지·실증 의무(§4), 실패는 시끄럽게(§5), 조회 모드(§6),
서브에이전트(§7), 응답 형식(§8) 이 전부 거기 있다.
작업별 절차(형태 규칙·검증·인덱스 조회·서브에이전트 운영·브랜치 이름)는 `docs/RULES/` 에 있다.
이것도 사본이 아니라 CLAUDE.md 에서 **옮겨 간 일부**이며, 언제 무엇을 읽는지는 `CLAUDE.md` 맨 위 표가 정한다.

이 파일은 **일부러 사본이 아니다.** 같은 규칙의 사본을 둘로 두면 반드시 어긋난다.
실제로 2026-08-25에 사본을 만든 지 3분 만에 §3-1 개정이 한쪽에만 반영되어 갈라졌다.
→ `docs/DECISIONS.md`

**규칙을 고칠 때는 그 규칙이 있는 한 곳(`CLAUDE.md` 또는 `docs/RULES/`)만 고친다.** 이 파일에 규칙을 옮겨 적지 마라.

---

## Codex 전용 사항

### 에이전트 정의

`.codex/agents/*.toml` 은 **생성물이다. 직접 수정하지 마라.**

- 원본: `.claude/agents/*.md`
- 재생성: `powershell -ExecutionPolicy Bypass -File tools/sync-agents.ps1`
- 어긋남 검사: 같은 명령에 `-Check` (일치 0 / 어긋남 1 로 종료)

에이전트를 고치려면 `.claude/agents/<이름>.md` 를 고치고 위 명령을 돌린다.

### 확인한 것 (2026-10-26, 공식 문서 `learn.chatgpt.com/docs/extend/mcp`·`/docs/hooks` 와 이 머신의 설정)

| 항목 | 내용 |
|---|---|
| MCP 설정 위치 | 사용자 `~/.codex/config.toml` 의 `[mcp_servers.<이름>]`. Codex 앱·CLI·IDE 확장이 같이 쓴다. 프로젝트 `.codex/config.toml` 은 신뢰된 프로젝트에서만 읽힌다. 지금 무엇이 붙어 있는지는 `codex mcp list` 로 구한다(머신마다 다르다, CLAUDE.md 맨 위) |
| MCP 도구 승인 | `[mcp_servers.<이름>.tools.<도구>] approval_mode` — 값은 `auto`·`prompt`·`writes`·`approve`. `Unity_RunCommand` 는 설정에 승인을 두지 않는다. 플러그인의 `task` 는 `approvalPolicy: never` 라 승인이 필요한 도구가 `MCP tool call requires approval, but approval policy is never` 로 거부된다(2026-11-06 확인). 이것이 평소 상태다. 사용자가 허락한 실행만 `tools/codex-unity.ps1 -Unity` 가 그 동안 `approve` 로 두고 되돌린다(`docs/RULES/SUBAGENTS.md` §7-5). `approve` 는 `never` 에서 `Unity_RunCommand`·`Unity_GetConsoleLogs` 를 부르게 해 줬다(2026-11-06 읽기 전용 호출 확인). CLAUDE.md §0 "상태를 바꾸는 코드는 승인 후에만" |
| 훅 | Codex 에도 `PreToolUse` 훅이 있다(`.codex/hooks.json`, 신뢰된 프로젝트에서 `/hooks` 로 신뢰한 훅만 돈다). 셸은 `tool_name: Bash`, 파일 편집은 전부 `apply_patch` 하나로 오고 경로는 `tool_input.command` 의 패치 본문에 있다. `permissionDecision: "ask"` 는 지원하지 않는다(오류를 내고 호출을 계속한다). 이 레포는 `.codex/hooks.json` 으로 `tools/hooks/guard.ps1` 을 `Bash`·`apply_patch` 에 건다 — 되돌릴 수 없는 git 명령·`main`/`dev` 커밋·보호 파일 편집을 **deny** 한다(Claude 쪽은 보호 파일이 ask). 처음 한 번 `/hooks` 로 신뢰해야 돈다. 2026-10-26 `guard.ps1` 만 고쳤을 때는 다시 신뢰하지 않아도 돌았다 — **신뢰는 스크립트 내용을 다시 검사하지 않는다.** 그러니 `tools/hooks/` 의 변경은 리뷰로 본다 |

### 확인 못 한 것 — 단정하지 마라 (§4-1)

| 항목 | 상태 |
|---|---|
| `tools` / `model` 권한 제한을 Codex 에이전트 TOML 에서 표현하는 키 | **확인 못 함.** `sync-agents.ps1` 은 이 둘을 **주석으로만** 남긴다. 즉 Codex 쪽에서는 도구 권한이 **강제되지 않는다** |
| `approval_mode` 의 `auto` 가 정확히 무엇을 하는지 | **확인 못 함.** 문서가 뜻을 적은 값은 `writes`("읽기 전용 표시가 없는 도구는 묻는다")뿐이다. `approve` 는 뜻이 문서에 없고 위 표의 동작만 실측했다 |
| `/codex:review` 가 이 파일과 `CLAUDE.md` 를 읽고 리뷰하는지 | **확인 못 함.** `adversarial-review` 는 2026-10-26 실행 로그에 `Get-Content CLAUDE…` 가 찍혀 읽은 것을 봤다. 규칙 체크리스트가 필요하면 `adversarial-review` 초점에 적는다 (`docs/RULES/SUBAGENTS.md` §7-5) |

위 표의 첫 항목이 중요하다. `unity-verifier` 는 Claude Code 에서 `tools:` 로 쓰기 도구가 구조적으로 차단되지만,
**Codex 에서는 그 차단이 걸려 있지 않다.** 그러니 Codex 로 검증 에이전트를 돌릴 때는
`developer_instructions` 의 금지 조항이 유일한 방어선이다. 더 강한 보장이 필요하면 Codex 쪽 권한 설정을 먼저 확인하라.

---

## Antigravity 전용 사항

역할·교차 검토·커밋 표시는 `docs/RULES/SUBAGENTS.md` §7-6 이 원본이다. 여기에는 도구 사실만 둔다.

### 확인한 것 (2026-11-25, agy 1.5.15, 이 머신의 세션 기록과 설정)

| 항목 | 내용 |
|---|---|
| 규칙 읽기 | 새 세션 둘 다 첫 단계들에서 `CLAUDE.md` 를 읽었다(`transcript.jsonl`) |
| 권한 | Auto 모드(`permissionPreset: AGENT_PERMISSION_PRESET_AUTO`). 작업 폴더 편집은 자동, 명령은 샌드박스 안에서 자동이다. 샌드박스 안의 git 쓰기는 `.git/index.lock: Permission denied` 로 실패해 샌드박스 밖(`BypassSandbox`)으로 다시 실행했다 |
| 훅 | `.agents/hooks.json` 이 `PreToolUse`(`run_command`·`edit_file`·`write_file`)에 `tools/hooks/guard.ps1`, `SessionStart` 에 `session-start.ps1 -Format antigravity` 를 건다. 훅의 작업 폴더는 `hooks.json` 이 있는 폴더다. `deny` 는 실제 세션에서 막혔고(`git clean --dry-run`, `.meta` 편집), `SessionStart` 알림은 새 세션에 주입됐다 |
| `agy -p` 의 세션 거부 규칙 | `--settings` 의 `permissions.deny` 로 `write_file(...)`·`command(*)`·`mcp(*)` 를 주면 편집 도구, 셸(`BypassSandbox` 포함), 하위 에이전트가 막힌다. 훅이 `allow` 를 돌려줘도 거부가 앞선다. `auto` 모드에서 `unsandboxed(*)`·`command(claude)` 만 준 것은 막지 못했다. 그래서 `tools/agy-run.ps1` 은 `command(*)` 를 쓴다 |
| `--sandbox`, `--mode plan` | 샌드박스 안의 셸은 작업 폴더에 쓰지 못한다. `--mode plan` 은 `write_file` 을 막지 못했다 |

### 확인 못 한 것 — 단정하지 마라 (§4-1)

| 항목 | 상태 |
|---|---|
| 훅의 `allow` 가 권한 확인을 건너뛰는지 | **확인 못 함.** 명시한 거부 규칙을 이기지 못하는 것만 확인했다. 내장 문서(`agy-customizations/docs/hooks.md`)는 "Automatically allow the tool execution", 웹 문서(`antigravity.google/docs/hooks.md`)는 "delegating to the next hook or default policy" 로 서로 다르다 |
| `ask`·`force_ask` 가 `edit_file` 에서 확인 창을 띄우는지 | **확인 못 함.** 2026-11-24 시험은 없는 파일이 대상이라 "파일 없음" 오류가 먼저 났을 수 있다. 그래서 보호 파일은 `deny` 로 둔다 |
| 훅이 0 이 아닌 코드로 끝나거나 빈 출력을 낼 때 | **확인 못 함.** 두 문서 모두 적지 않았다 |
| `run_command`·`edit_file`·`write_file` 이 아닌 쓰기 도구 | **확인 못 함.** 매처에 없는 도구에는 훅이 돌지 않는다 |
| 이미지 생성, 영상 입력 | **확인 못 함.** |
