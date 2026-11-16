# K1 RoadsideKit 3·4차 — PALM COAST·SUMMIT DROP 코너 화살표판 (§3-2 합의 요청서 초안)

> 작성: 2026-11-16. 승인 상태: **사용자 2026-11-16 "정할 것 1·2 둘다 그대로", "추천대로" 로 승인. 밝기 후속과 "FINGER 방식 — SUMMIT 전용 재질 (추천)" 선택까지 반영·재빌드 끝. 최종 플레이 확인·리뷰 대기, 커밋·병합 안 함.**
> 브랜치: `feature/art-roadside-palm-summit` (`dev` `0ce9854` 에서 분기, 커밋 없음).
> 원본 규칙: `docs/ART_REQUEST_PROPS.md` K1 (판은 한 크기 3.8 로 만들고 Unity 에서 균일 스케일 A5 — PALM 0.947배, SUMMIT 1.053배). 앞의 두 트랙: `docs/DECISIONS.md` 2026-11-11 "K1 RoadsideKit 1차(FINGER)", "2차(SKYWAY)".
> 줄 번호는 2026-11-16 `dev` `0ce9854` 기준이다. 구현 직전에 다시 대조한다.
> Codex 지시서: `docs/CODEX_K1_PALM_SUMMIT.md`.

유사 자산: 기존 `FingerLanesEnvironmentBuilder.cs:425-447`, `SkywayEnvironmentBuilder.cs:590-607` 을 참고했고 같은 형식으로 맞춘다. 새 형식은 없다.

## 목표

PALM COAST·SUMMIT DROP 의 코너 화살표판을 Blender 킷 메시(`art-src/RoadsideKit` → `Assets/_Project/Art/Models/RoadsideKit.fbx`, 이미 임포트됨)로 바꿔, 막대 상자 조합을 한 덩어리 꺾쇠로 만들고 렌더러를 줄인다. 위치·방향·개수는 그대로다.

## 건드릴 것

| 경로 | 변경 |
|---|---|
| `Assets/_Project/Scripts/Editor/ChainRush/PalmCoastEnvironmentBuilder.cs` — 필드 `:98` 부근, `Build()` `:147` 부근, `CornerBoards` `:588-617` | `chevronPanel`·`chevronArrow` 필드 + `KitMesh("RoadsideKit", …)` 로드. 판 `Box`(`:607`) → `ChevronPanel`. 막대 `Beam` 둘 × 3(`:613-614`) → `ChevronArrow` 3. |
| `Assets/_Project/Scripts/Editor/ChainRush/SummitDropEnvironmentBuilder.cs` — 필드 `:100`, `Build()` `:144`, 지그재그 코너판 `:585-610` | 같은 변경. 판 `Box`(`:603`) → `ChevronPanel`, `Beam` 둘 × 3(`:607-608`) → `ChevronArrow` 3. |
| `Assets/_Project/Prefabs/PalmCoastExterior.prefab`, `SummitDropExterior.prefab` | **재빌드 메뉴로만** 바뀐다(`Rebuild Palm Coast Exterior`, `Rebuild Summit Drop Exterior`). YAML 을 텍스트로 고치지 않는다. 프리팹 로컬 fileID 가 새로 나와 diff 가 크다(HANDOFF "장식 재빌드"). |
| `Assets/_Project/Art/Models/SummitDropExterior_05_M_SummitInk.asset`·`Assets/_Project/Art/Models/SummitDropExterior_05_M_SummitAmber.asset` | 최초 킷 교체는 이 두 병합 메시(`MergeByMaterial`)에 기록됐고 그때는 `SummitDropExterior.prefab` 내용이 같았다. 최종 판은 `SummitInk`, 화살표는 아래 `SummitChevron` 을 참조한다. `SummitAmber` 메시는 참조가 없어져 삭제했다(사용자 2026-11-16 "추천대로 네가 돌려봐", DECISIONS 같은 날 항목). |
| `Assets/_Project/Art/Models/SummitDropExterior_05_M_PalmYellow.asset`·`Assets/_Project/Art/Models/SummitDropExterior_05_M_SummitChevron.asset` | 밝기 조정의 PALM 재질 공유 단계와 최종 SUMMIT 전용 재질 단계에서 각각 생성된 병합 메시. 최종 프리팹은 `SummitChevron` 메시를 참조하며, `PalmYellow` 메시는 참조가 없어 삭제했다(추적된 적 없는 파일). 이 후속에서는 프리팹의 메시·재질 참조도 바뀐다. |
| `Assets/_Project/Art/Materials/M_SummitChevron.mat` | 최종 SUMMIT 전용 재질. `Palette("Chevron", new Color(0.92f, 0.78f, 0.30f))` 로 생성하며 PALM 재질을 참조하지 않는다. 새 에셋의 `.meta` 는 Unity 가 생성한다. |

정정: 원래 요청서는 SUMMIT 코너판 변경이 기록되는 병합 메시 에셋 두 개를 작업 범위에서 놓쳤다.

현재 누적 변경은 빌더 2개 + 프리팹 2개 + 병합 메시 4개 + SUMMIT 전용 재질 1개이며, 새 에셋의 Unity 생성 `.meta` 3개를 포함하면 `Assets/` 변경은 12개다. 병합 메시 4개 중 중간 단계 두 개는 현재 참조가 없다. 3개 이상 수정이라 §3-1 규모 기준, 프리팹 수정이라 환경 기준에 해당한다.

## 새로 만들 것

최초 킷 교체에서는 `Assets/` 아래 새 파일이 없었다. 밝기·재질 분리 후속으로 위 표의 `PalmYellow` 병합 메시·`SummitChevron` 병합 메시·`M_SummitChevron.mat` 과 Unity 생성 `.meta` 가 추가됐다. 이 요청서와 Codex 지시서는 `docs/` 에 있다(§1-1: 목적 = 합의·지시를 기록하려고 / 경로 = `docs/` / 형태 = 문서).

## 바꾸는 값 (추천 — "정할 것" 참고)

킷: `ChevronPanel` 3.8 × 1.4 × 0.15, `ChevronArrow` 0.9 × 1.0 × 0.12 (끝이 로컬 +x, 위아래 대칭). `Shape` 의 `scale` 인자는 **미터 크기**다(`ExteriorKit.cs:65-71`).

| | PALM COAST | SUMMIT DROP |
|---|---|---|
| 균일 배율 | 3.6 / 3.8 = **0.947** | 4.0 / 3.8 = **1.053** |
| 판 크기(미터) | 3.600 × 1.326 × 0.142 (옛 3.6 × 1.3 × 0.15) | 4.000 × 1.474 × 0.158 (옛 4.0 × 1.5 × 0.2) |
| 판 위치 | `(0, 1.9, 0)` 그대로 | `(0, 3.4, 0)` 그대로 |
| 화살표 크기(미터) | 0.852 × 0.947 × 0.114 | 0.948 × 1.053 × 0.126 |
| 화살표 x · y | `c * 1.0` · 1.9 (c = −1, 0, 1) | `arrow * 1.1` · 3.4 |
| 앞 오프셋 `ArrowOut` | 0.071 + 0.057 = **0.128** | 0.079 + 0.063 = **0.142** |
| 판 재질 · 화살표 재질 | `dark` · `yellow` | `ink` · `chevron`(SUMMIT 전용 `M_SummitChevron.mat`, PALM 과 같은 노랑) |

- **방향**: 옛 막대 끝이 `point`(PALM `:610`, = `segment.Degrees > 0 ? -1 : 1`)·`direction`(SUMMIT `:604` 같은 식) 방향이다. 킷 화살표 끝은 +x 라서 `+1` 이면 회전 없음, `-1` 이면 판 법선(z) 둘레 180°. FINGER·SKYWAY 와 같은 규칙이다(`SkywayEnvironmentBuilder.cs:601-607`). 위아래 대칭이라 뒤집혀도 모양은 같다.
- 다리(`Box("Board leg")`)·판 위치·`corridor.Reserve`·`Place` 자리는 그대로다. 판 폭 3.6·4.0 이 그대로라 통로 검사 결과가 바뀌지 않아야 한다.
- SUMMIT 은 `MergeByMaterial`(`:869-882`)로 머티리얼별 한 메시로 합쳐진다. `Shape` 는 `MeshFilter`·`MeshRenderer` 를 만들므로 합쳐지는 방식은 그대로다.

## 정할 것

| # | 항목 | 추천 | 결정 |
|---|---|---|---|
| 1 | 화살표 굵기. 킷은 한 가지 굵기(0.245)라 균일 배율하면 PALM 0.232·SUMMIT 0.258 이 된다. 옛 막대는 PALM 0.16·SUMMIT 0.17 이었다(`DECISIONS.md` 2026-11-11 1차 "남은 것"). 둘 다 옛 것보다 굵다. | **킷 그대로(균일 배율).** FINGER·SKYWAY 가 같은 방식이었고, 한 킷으로 네 트랙을 맞추는 게 K1 의 목적이다. 렌더에서 너무 굵어 보이면 그때 화살표만 0.7배로 줄이는 안을 다시 올린다. | 그대로(사용자 2026-11-16) |
| 2 | SUMMIT 판 두께. 균일 배율이면 0.2 → 0.158 로 얇아진다(요청서 K1 이 이미 "두께 0.158" 로 적음). | **그대로 0.158.** 얇아져도 판 앞에 화살표가 붙는다. | 그대로(사용자 2026-11-16) |
| 3 | 렌더 확인을 누가 하나 | **Claude 가 Unity MCP 로**(전후 같은 카메라). Codex 실행은 `Unity_RunCommand`·`Unity_GetConsoleLogs` 만 열리고 카메라 도구는 없다. | 렌더 확인 생략, 사용자 플레이 확인으로 대신(사용자 2026-11-16 추천대로) |

> 후속 승인 (사용자, 2026-11-16): "수정후는 맘에 드는데 SUmmitDrop은 밝게 해줘 palm coast 처럼" → SUMMIT 화살표만 밝게 바꿨다. 처음에는 `M_PalmYellow.mat` 을 공유했고, 이어 사용자가 "FINGER 방식 — SUMMIT 전용 재질 (추천)" 을 선택해 전용 `M_SummitChevron.mat` 으로 확정했다. FINGER 처럼 자기 `Palette("Chevron")` 로 만들되 색은 PALM 의 RGB 0.92·0.78·0.30 그대로다(FINGER 의 RGB 0.98·0.79·0.10 을 가져온 것은 아니다). 다른 갈색 깃발과 코너판 모양·치수·방향은 유지했다. 확대 미리보기는 공유 재질 단계에서 확인했고 전용 분리 뒤 색·셰이더·표면 값의 동일성과 PALM 재질 참조 0개를 검사했다. 최종 플레이 확인은 대기다. 최신 테스트 결과는 `docs/DECISIONS.md` 같은 날 K1 3·4차 항목의 전용 재질 검증에 있다.

## 검증 방법 (§4-3·§4-4)

1. **Codex (`-Unity`, 지시서에 있음)**: 편집 **전** PALM·SUMMIT 프리팹의 렌더러 수·정점 합·코너판 수·판마다 정점을 잰다 → 편집 → 컴파일(`Editor.log` 의 `Tundra build` 줄 원문, `error CS` 0) → 두 메뉴로 재빌드(`clearance` 로그·`renderers` 로그 원문) → 편집 **후** 같은 값을 다시 잰다 → 프리팹의 RoadsideKit GUID 수 → 영향 평가(바뀐 것 → 읽는 곳 → 고른 테스트)와 선택한 테스트 실행(결과 XML `testcasecount`·`result`·`passed`·`failed`·시각).
2. **Claude**: Codex 보고를 믿지 않고 컴파일·Console 을 다시 보고, 전후 렌더(코너판 방향 `>>>`/`<<<` 확인, 병합 메시 정점에서 자리를 찾는다 — SUMMIT), 코드 diff 를 `unity-reviewer` 체크리스트(§0/§1/§5)에 대조한다.
3. **사용자 플레이 확인**: "코너 들어가기 전에 화살표 방향을 읽을 수 있었나"(요청서 K1). PALM COAST·SUMMIT DROP 을 한 번씩.
4. 병합 전 리뷰: 변경이 프리팹을 포함해 30줄을 넘으므로 "그 밖" 규모 — `unity-reviewer` 파견. **Codex 가 구현하므로 Codex 설계 리뷰는 "다른 눈"이 되지 못한다**(§7-5 "구현자와 다른 눈"). 사용자가 정한다.

## 안 하는 것

- Blender·`art-src/`·`RoadsideKit.fbx` 수정, 새 킷 메시. 광고판(PALM·SUMMIT 에는 없다), SKYWAY 막다른 베이·고속도로 광고판.
- SUMMIT 의 지름길 표지판(`SummitDropEnvironmentBuilder.cs:563-565` 의 `Beam("Arrow")` — `ShortcutSign` 별도 작업), 룩(`Rebuild … Look`)·씬·`ProjectSettings`·패키지.
- 테스트 파일 수정, 다른 트랙(FINGER·SKYWAY·STADIUM), 문서(`HANDOFF.md`·`DECISIONS.md` — 병합 때 Claude 가 쓴다).
- 파일·에셋 삭제, 커밋·푸시.

## 위험·확인 못 한 것

- 두 트랙의 `corridor.Clear`·`Place` 는 폭만 쓰는 것으로 읽었지만 **실행해서 확인하지 않았다.** 재빌드 로그의 `clearance` 줄이 근거다.
- SUMMIT 병합 뒤 화살표 방향을 코드만으로는 확인할 수 없다. 렌더로 본다.
- 이 PC 에서 Codex `-Unity` 가 relay 서버를 하나로 찾는지는 설정을 읽고 추정한 것이다(`unity_mcp` 만 `relay` 를 실행한다). 실행해서 확인하지 않았다.
- Unity 에디터가 이 브랜치 전환 뒤 한가한지, 재임포트가 끝났는지 확인하지 않았다(§9-3).
