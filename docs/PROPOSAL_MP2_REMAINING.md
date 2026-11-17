# MP2 남은 것 구현 요청서 (§3-2) — D8 복귀 겹침 · 8인 격자 · 러너 드로 배치

> 2026-11-17 작성. **이 문서는 승인 전 요청서다.** 0절의 "정할 것" 3항목에 답을 받고, 승인을 받은 뒤에 구현한다.
> 남은 것의 출처: `docs/DECISIONS.md` 2026-11-14 "MP2 단계 B" 의 "남은 것"(D8 복귀 겹침, 8인 격자 검증), 2026-11-17 "러너 캐릭터 Unity 반영 3·4단계" 의 "남은 것"(드로 배치 측정 — 사용자 2026-11-17 "MP2 남은것 하고 하자").
> 결정 원본: `docs/PROPOSAL_M5_AI_OPPONENTS.md` 의 D1("구조 8인·첫 판정 봇 3명")·D8("출발은 격자, 복귀 겹침은 플레이 뒤"), `docs/PROPOSAL_MP2_BOT_RACE.md:27`(D8 "따로 처리하지 않는다 — 겹치면 접촉 규칙대로 밀린다").
> 접두사: `R/` = `Assets/_Project/Scripts/Runtime/ChainRush/`, `T/` = `Assets/_Project/Scripts/Tests/PlayMode/`, `E/` = `Assets/_Project/Scripts/Tests/EditMode/`. 줄 번호는 `dev` `7ad0af9` 기준이다.

## 0. 사용자가 정할 것

| # | 질문 | 선택지 | 추천 |
|---|---|---|---|
| Q1 | 8인을 어디까지 여나 | **(a)** 구조만 검증한다 — 테스트에서 상대 7명으로 돌리고, 로비는 봇 3명 그대로 둔다 · **(b)** 로비에서도 8인 봇전을 고를 수 있게 한다 | **(a)**. D1 이 "첫 판정 봇 3명" 이고, (b) 는 Q1-b 의 결정이 먼저 필요하다(아래 1-2) |
| Q2 | D8 테스트에서 문제가 보이면 | **(a)** 멈추고 보고한 뒤, 고칠 방법을 그때 정한다 · **(b)** 미리 하나를 고른다(1-1 의 후보 ①~③) | **(a)**. D8 은 "플레이 뒤에 본다" 로 정해 두었고, 문제가 실제로 있는지 아직 모른다 |
| Q3 | 드로 배치를 어떻게 재나 | **(a)** `[Explicit]` PlayMode 테스트로 잰다(1·4·8명 × STADIUM·PALM × 닫힌·열린 얼굴, `UnityStats` 값을 Console 에) · **(b)** 사용자가 Game 뷰 Stats 를 보고 알려 준다 | **(a)**. 2026-11-12 스파이크가 같은 방법으로 옛 모형 값을 남겨 두었다 — 그 값과 바로 비교된다(1-3) |

## 1. 지금 코드가 하는 일 (근거)

### 1-1. D8 복귀 겹침

- 복귀 자리: `RespawnOnTrack` 은 `LastGroundedS − respawnBackoff(4 m)` 의 **도로 중앙선**(`D = 0`, 높이 1.05)에 러너를 세운다(`R/RunnerMotor.cs:501-518`, `:130`). 그래서 같은 곳에서 떨어진 두 러너는 **같은 점**에 복귀한다.
- 복귀 뒤 `respawnHoldTime`(1 s) 동안 `Step` 이 바로 돌아온다 — 움직이지 않고 입력도 받지 않는다(`R/RunnerMotor.cs:278-283`, `:131`).
- 접촉은 복귀 대기 중인 러너도 민다. `ApplyContact` 는 대기를 보지 않고 `Velocity` 에 임펄스를 더하고 `Move` 한다(`R/RunnerMotor.cs:688-700`, 부르는 곳 `R/Race/RaceField.cs:252`).
- 두 중심이 같으면 법선은 늘 월드 `+X` 다(`R/Race/RunnerContact.cs:54`, 결정적). 트랙 방향과 상관없는 축이다.

**확인 못 한 것 (테스트로 볼 것)**
- ⓐ 같은 점에 복귀한 두 러너가 몇 틱 만에 떨어지나. 월드 `+X` 가 도로 방향과 같은 구간이면 옆이 아니라 앞뒤로 갈라지는데, 그 경우 괜찮은가.
- ⓑ 대기 중에 받은 임펄스 속도가 대기 동안 적분되지 않고 남아 있다가(`Step` 이 일찍 돌아오므로), 대기가 끝나는 순간 튀어 나가나.
- ⓒ 대기 중인 러너에게 뒤에서 달려온 러너가 부딪히면, 밀린 러너가 틈(gap)이나 벽 밖으로 나가나. 밀기는 `CharacterController.Move` 라서 벽에서는 막힌다(DECISIONS "MP2 단계 B").

**문제가 보일 때의 후보** (Q2 에서 (b) 를 고르면 이 중 하나를 미리 고른다)
- ① 대기 중인 러너는 접촉에서 속도 변화를 받지 않는다(위치 분리만 받는다).
- ② 복귀 자리를 겹치지 않게 옆으로 비킨다(출발 격자처럼 칸을 준다).
- ③ 대기 중인 러너끼리는 접촉하지 않는다.

### 1-2. 8인 격자

- 격자: `StartGrid.Assign` 은 1~8명을 받는다(`R/Race/StartGrid.cs:71`). 한 줄에 4칸이고, 8명이면 4 m 뒤에 둘째 줄을 둔다(DECISIONS "MP2 단계 B" 의 "만든 것").
- 테스트: `StartGridTests` 는 **4명만** 다섯 트랙에서 본다(`E/StartGridTests.cs:30-31`). 8명은 테스트가 없다.
- 상대 수: `RaceSetup.MaxOpponents = 7`(`R/Flow/RaceSetup.cs:29`). `WithOpponents` 는 같은 프로필이 겹쳐도 거부하지 않는다(`:45-56`).
- 로비: `BotLineup.Pick(..., count = 3)` 은 **플레이어와 다른 프로필**에서만 뽑는다(`R/Bots/BotLineup.cs:9,26`). 프로필은 5종이다(`Assets/_Project/Data/RunnerProfile_*.asset`). 그래서 로비 경로로는 상대가 최대 4명이다. **Q1-b 로 가려면 같은 러너가 둘 이상 나와도 되는지(같은 겉모습 구별 방법 포함)를 먼저 정해야 한다.**
- 비용(2026-11-12 스파이크, 옛 모형): 복사본 8개의 틱은 294.7~343.8 µs(틱 20 ms 의 1.47~1.72%)였다. 메모리는 네이티브 +68.3~103.2 MB 였다. 로드는 복사본 하나에 746~1,357 ms 였다(DECISIONS 2026-11-12 "MP1 구조" 표).

### 1-3. 드로 배치

- **"전" 값이 이미 있다.** 2026-11-12 스파이크에서 도로·조명·HUD 를 끈 복사본 8개는 STADIUM 1,305 배치, PALM 1,539 배치였다. 1개는 266~286 · 404~542 였다. 그래서 러너 모형 하나 ≈ **150 배치**((1,305 − 266) ÷ 7, 옛 모형, DECISIONS "MP1 구조").
- 지금 상대 월드는 러너만 보이게 한다(`R/Flow/RaceAssembler.cs:222-227` `HideAllButTheRunner`, 카메라·리스너는 `:112-117` 에서 끈다). 스파이크의 "끈 8개" 와 같은 조건인지(조명·HUD 가 꺼지는지)는 구현할 때 `HideAllButTheRunner` 를 읽어 확인하고, 다르면 보고에 밝힌다.
- 요청서가 기대한 값은 "러너 1명 = 렌더러 1개·슬롯 3개" 였다. 실제 배치 수로 바꿔 말하지 않고 잰 값을 보고하기로 했다(`docs/PROPOSAL_RUNNER_UNITY.md:313`).
- 열린 얼굴은 `RaceAssembler.openFace`(직렬화 private, `R/Flow/RaceAssembler.cs:32`)로만 바뀐다. 테스트는 `SerializedObject` 로 값을 넣는다(`E/RunnerLookTests.cs:175` 의 `Set` 과 같은 방식).
- 스파이크 테스트 `ChainRushWorldCostTests`(`spike/mp1-isolated-worlds` `57c54f4`, 이 PC 로컬에만 있음)는 MP1 전 구조라 그대로 쓸 수 없다. `UnityStats` 를 읽는 방식만 가져온다(그 파일 `:168-170`).

## 2. 합의 요청

```
목표:        MP2 의 남은 검증을 끝낸다 — 복귀 겹침이 접촉 규칙으로 풀리는지, 8명이 격자에 서서 결정적으로
             달리는지, 지금 러너 하나가 배치 몇 개를 쓰는지

건드릴 것 (Q1·Q2·Q3 모두 (a) 일 때, 테스트 파일만):
  E/StartGridTests.cs            — 8명 테스트 추가 (아래 ①)
  T/ChainRushContactTests.cs     — D8 테스트 추가 (아래 ②)
  T/ChainRushRaceFieldTests.cs   — 8명 봇전 테스트 + [Explicit] 드로 배치 측정 (아래 ③④)
  런타임 코드: 바꾸지 않는다. 테스트가 결함을 드러내면 멈추고 보고한다(Q2)

새로 만들 것: 없음 (기존 테스트 클래스에 메서드만 더한다 — 새 PlayMode 클래스를 만들면
             select-tests 표를 함께 고쳐야 해서 피한다, VERIFICATION §4-4)

검증 방법:
  컴파일 — Editor.log 의 Tundra build 줄 + Console 에러 원문
  ① EditMode StartGridTests 전체
  ② ③ PlayMode ChainRushContactTests·ChainRushRaceFieldTests·ChainRushBotRaceTests 전체
       + 서킷 지문(ChainRushReplayTests.Replay_CircuitBotRun_RepeatsEveryTickBitForBit)
       — 런타임을 바꾸지 않으면 지문 0xEF92FB76BFD1D767,
         봇전 위치 해시 0xAB407E424EDC7FAE 가 그대로인지 본다
  ④ [Explicit] 측정 1회 — Unity 창을 앞에 둔다(focused 값을 함께 남긴다)
  실행마다 tools/test-runs.ps1 -Record

안 하는 것:  로비 8인(Q1-b), 복귀 처리 변경(Q2-b 가 아니면), 봇 판단(접촉 회피),
            씬·프리팹·트랙 .asset·ProjectSettings·패키지, MP3 결정성
```

**테스트 내용**

- **① `StartGridTests`**
  - 8명이 다섯 트랙 모두에서 들어맞는다. 두 줄이고, 둘째 줄이 출발 창(S −12 ~ +4 m) 안에 있고, 서로 ≥ 0.76 m 떨어진다.
  - `Assign(8, seed)` 가 겹치지 않는 칸을 준다.
- **② `ChainRushContactTests` (D8)**
  - ⓐ 두 러너를 같은 S 에서 떨어뜨려 같은 점에 복귀시킨다. 대기(1 s)가 끝날 때 겹침이 0(허용 2 cm)인지, 둘 다 도로 위인지 본다. 대기가 끝난 다음 틱의 수평 속도도 적는다(ⓑ).
  - ⓒ 대기 중인 러너에게 다른 러너가 달려와 부딪힌다. 밀린 러너가 도로 밖·틈으로 나가지 않는지 본다.
  - 트랙은 월드 `+X` 가 도로 방향과 같은 곳과 수직인 곳을 하나씩 쓴다.
  - 기대값은 "D8 그대로(따로 처리 안 함)" 가 받아들일 만한지 판단할 **관찰 값**이다. 통과 기준은 겹침 해소·도로 이탈 없음 두 가지만 단언하고, 속도·틱 수는 Console 에 남겨 보고한다.
- **③ `ChainRushRaceFieldTests` (8명)**
  - `Setup(RaceMode.Bots).WithOpponents(<7명, 같은 프로필 허용>)` 으로 STADIUM 과 출발이 가장 좁은 트랙(`StartGrid.MinimumHalfWidth` 로 고른다)을 돌린다.
  - 8명이 배정된 칸에 선다(기존 4명 테스트 `:124-141` 과 같은 허용치).
  - 300틱을 두 번 돌려 위치 해시가 같은지 본다.
  - Console 에러가 0인지 본다.
- **④ `[Explicit]` 드로 배치 측정**
  - 조건: 1·4·8명 × STADIUM·PALM × 닫힌·열린 얼굴. 출발 대기 프레임에서 `UnityStats.batches`·`setPassCalls`·`triangles`·`vertices` 를 읽는다.
  - 러너 1명분 = (8명 − 1명) ÷ 7 로 계산해 옛 모형 ≈150 과 비교한다.
  - 단언은 하지 않는다(측정값만 남긴다). 그래서 게이트에서 빠지도록 `[Explicit]` 이다.

## 3. 예상 위험

- 8명 PlayMode 는 복사본 8개를 로드하므로 한 테스트에 10초 이상 걸릴 수 있다(복사본 하나 0.75~1.36 s). `Timeout` 은 기존 4명 테스트보다 넉넉히 둔다.
- `UnityStats` 는 Game 뷰가 렌더된 마지막 프레임의 값이다. 스파이크는 포커스 밖에서도 배치는 읽혔지만 프레임 시간은 판정하지 못했다. 이번에도 **배치만 보고하고 프레임 시간은 판정하지 않는다**(측정 항목 4 "트랙별 프레임" 은 따로).
- ② 의 "월드 `+X` 가 도로 방향과 같은 곳" 을 고르려면 트랙 프레임을 읽어야 한다. 구현할 때 `TrackFrame.Forward` 로 찾고, 고른 S 를 테스트 이름이나 주석에 남긴다.
