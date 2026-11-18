# MP3 여러 레이서 결정성 — §3-2 합의 요청서 초안

## 0. 2026-11-18 합의: MP3 는 "재생만", 롤백은 M7 (구현 승인됨)

> **승인**: 사용자 2026-11-18 "다 추천대로. 다만 작업은 새 세션에서 하겟다" — 아래 요청서와 정할 것 ①②③ 모두 추천안. 범위를 줄인 이유는 사용자 질문 "mp3 작업 얼마나걸릴것같나" 에 초안 그대로 3~5일 / 재생만 1~2일로 답하고, v1.0 게임에는 롤백을 쓰는 곳이 없다(롤백은 네트워크 보정용, M7)고 추천한 것.
> **이 절이 MP3 의 구현 범위다.** 아래 1~4절(2026-11-15 초안)은 롤백 부분을 M7 에서 다시 열 때의 자료로 남긴다. 그 초안의 "사용자가 정할 것" 6항목은 1·2·4·5 가 이 절로 정해졌고(A 회귀 검출, B 전원 입력 기록, B 논리 순서 고정, 게이트 포함), 3·6(롤백 범위·봇 상태 계약)은 M7 로 넘어갔다.
> 근거 줄 번호는 2026-11-18 `dev` `c6d8c42` 기준으로 다시 확인했다.

**런타임 코드를 고치지 않고 테스트만으로 한다.** 근거:
- 봇을 직접 관측에 연결하고(`bot.Observe(field, i)`) 필드를 한 틱씩 돌리는 방식이 이미 있다 — `Tests/PlayMode/RaceBotTests.cs:182-190`, 필드 한 틱 `:210`(`FieldTick` = `RaceField.FixedUpdate` 리플렉션, `:26`), 봇전 로드 `LoadRace` `:216`.
- 입력원을 `InputRecorder` 로 감싸면 `RaceField.Initialize` 의 자동 `Observe`(`Runtime/ChainRush/Race/RaceField.cs:149,151`, `is RaceBot` 검사)가 걸리지 않는다 → 위처럼 테스트가 `Observe` 를 직접 부른다.
- 기록·재생은 `InputRecorder`(`Control/InputRecorder.cs:21`)·`InputReplay`(`Control/InputReplay.cs:23`). `SetInputSource`·`StartRun` 이 `Clear` 를 부르므로 입력원을 건 뒤 `StartRun` 한다.
- 러너 상태 전체의 비트 비교는 `Runtime/Net/SimSnapshotCodec.cs:15` `Encode(ChainRushGame.CaptureSnapshot())`. 코덱의 `WriteRacer` 는 `RacerState.Snapshot` 의 저장 필드 29개를 모두 쓴다(2026-11-18 대조; `IsDown` 은 대조에서 코덱에 없었다 — 계산 속성인지 구현 때 확인). `CaptureSnapshot` 은 `Running` 에서만 된다(`ChainRushGame.cs:281`) — 결승한 러너는 그 뒤 필드 결과로 비교한다. 같은 방식의 선례 `Tests/PlayMode/NetReconcileTests.cs:166`.

```
목표:      봇전(플레이어 + 봇 3, 접촉 켬)의 전원 입력을 기록해 다시 돌리면 매 틱 전원 상태와
           필드 결과(Place·Progress·FinishTick·ContactSwingBreaks·ContactSequence)가 비트 단위로 같다.
           v1.0 봇전의 결정성 회귀 검출. 롤백은 M7.
브랜치:    feature/mp3-field-replay (dev 에서 분기)
건드릴 것:
  Tests/PlayMode/ChainRushReplayTests.cs   새 테스트 2개 추가 (기존 2개는 그대로)
  tools/select-tests.ps1                   Race/·Bots/ 변경 때 ChainRushReplayTests 의 새 테스트가 골라지는지 확인, 빠지면 배정
  docs/DESIGN.md:107                       MP3 행에서 롤백을 M7 으로 옮김 (정할 것 ①)
새로 만들 것: 없음 (기존 리플레이 테스트 파일, 정할 것 "위치")
테스트:
  Replay_BotRaceAllRunners_RepeatsEveryTickBitForBit — STADIUM·SUMMIT DROP ([Values], 정할 것 ③), Hard
    전원 입력을 기록하며 결승까지 (상한 6,000틱), 같은 세션 / 씬 재로드 / 프레임당 틱 묶음을 바꿔 재생,
    매 틱 4명의 Encode 바이트 + 필드 결과 비교. 전제 단언: 접촉이 실제로 일어남(ContactSequence > 0).
    봇전 해시는 로그로 남기고 DECISIONS 에 기록 (고정 상수 단언 없음, 정할 것 ②).
  Replay_BotRaceOneInputChanged_DivergesFromThatTickOnAndNotBefore
    상대 하나의 입력 인덱스 300 을 바꾸고, 300틱까지 같고 301틱에 그 러너부터 갈라지는지 (대조군).
검증:      컴파일 에러 0, 새 테스트 + 기존 ChainRushReplayTests·RaceBotTests 를 이름으로 골라 1회.
           실행 시간을 재 게이트 포함 여부 판단(초안 5번 B: 게이트 포함이 기본).
안 하는 것: 필드 스냅샷·복원, IRewindableInputSource, 롤백 테스트 9건(M7), 런타임 코드 수정,
           봇전 리플레이 기능, 8인, 다른 PC 의 비트 일치, 혼자 판 지문 고정 상수 단언(정할 것 ②)
멈추는 조건: 재생이 어긋나면 결정성 버그 — 고치지 않고 멈춰 첫 불일치 틱·러너·필드를 보고(§3-3).
```

| 정할 것 | 답 (2026-11-18 "다 추천대로") |
|---|---|
| ① 롤백을 M7 로, `DESIGN.md` 수정 | 예 |
| ② 혼자 판 지문 `0xEF92FB76BFD1D767` 고정 상수 단언 | 안 넣는다 — 시뮬 튜닝마다 상수 갱신 합의가 필요해진다. 로그 + DECISIONS 기록 유지 |
| ③ 트랙 | STADIUM·SUMMIT DROP |
| 위치 | 기존 `ChainRushReplayTests.cs` 에 추가 (새 파일 없음) |

> 작성·보관: 2026-11-15.
> 구현 승인 상태: 없음. 이 문서는 합의 요청서 초안이며, 문서 보관 지시는 구현 승인이 아니다.
> 보관 승인: 사용자 “해당 요청서 프로젝트 경로에 md파일로 보관하라. 요청서 경로가 따로 있다면 거기에, 없다면 만들어서”.
> 조사 당시 브랜치: `feature/mp2-bot-difficulty`. 다른 작업이 같은 트리를 수정하고 있으므로 근거 줄 번호와 미확인 항목은 조사 시점 기준이다.
> 보관하면서 `건드릴 것` 표제를 한국어로 바로잡고, 이 PC의 절대 파일 링크는 아래 접두사를 사용하는 `경로:줄` 표기로 정리했다. 요청서의 제안·선택지·검증 범위는 유지했다.

**조사 당시 수행 범위: 읽기 전용 조사와 초안 작성만 수행했다. 파일·Git·Unity 상태를 변경하지 않았다. 실행 검증은 하지 않았다.** 이후 사용자 지시에 따라 이 Markdown 문서만 보관했다.

재검토 결과, 진행 중인 작업 트리에서 다음 변경을 확인했다. 아래 초안은 이를 반영한다.

- `RaceField`에 **입력 직전 전원의 위치·속도를 복사하는 관측 기능**이 이미 있다. 이를 새로 만드는 안은 제외한다. `R/Race/RaceField.cs:175`
- `RaceBot` 소스가 생겼으며, 스윙 단계·대기 시간·재시도 상태를 가진다. `R/Bots/RaceBot.cs:22`
- `ShortcutBot`은 현재 `Runtime/ChainRush/Bots/`에 있고, `Clear`도 상태를 초기화한다. 최초 지시의 “Clear가 비어 있다”는 현재 작업 트리에는 해당하지 않는다. **중간 상태 복원 기능은 별개로 필요하다.** `R/Bots/ShortcutBot.cs:58`

아래에서 `R/`은 `Assets/_Project/Scripts/Runtime/ChainRush/`, `T/`는 `Assets/_Project/Scripts/Tests/PlayMode/`를 뜻한다. 줄 번호는 조사 당시 작업 트리 기준이다.

## 1. 요청서 본문

### 목표

**MP3를 v1.0 봇전의 회귀 검출 장치로 구현한다.** 같은 PC·같은 실행 버전·같은 레이스 설정에서, 플레이어와 봇 3명의 **전원 입력 재생** 및 **필드 전체 복원 후 재실행**이 매 틱 비트 단위로 일치함을 검증한다.

게임에서 사용하는 봇전 리플레이 저장·재생 기능은 추가하지 않는다. M7에는 검증 기반을 남기되, 네트워크 결정성이나 멀티 완료를 선언하지 않는다. 근거는 M5의 MP3 항목, v1.0 범위와 D4다. `docs/DESIGN.md:107`, `docs/DESIGN.md:112`, `docs/DECISIONS.md:171`

**기존 검증과 MP3의 차이**

| 기존 검증 | 코드가 실제로 단언하는 것 | MP3에서 추가할 것 |
|---|---|---|
| MP1의 플레이어 런 비교 | 현재 코드는 같은 출발 칸에서 접촉을 끄고, 플레이어의 `StateTrace`를 혼자 런과 비교한다. 상대의 틱 동기화·진행도도 확인하지만 상대 전원의 상태를 비트 비교하지 않는다. | 접촉을 켠 전원 상태·순위·결승 상태 비교 |
| MP2 B의 4명 × 300틱 | 매 실행 `CircuitBot`을 새로 연결하고 위치 세 성분의 누적 해시를 비교한다. 입력 로그 재생이나 스냅샷 복원은 하지 않는다. `Begin()`에서 먼저 1틱을 진행한 뒤 300회 위치를 채집한다. | 정확한 틱 구간의 전원 입력 재생·전체 상태 비교 |
| 혼자 판 지문 | 1,700틱 기록·재생의 상대 비교를 단언한다. `0xEF92FB76BFD1D767` 상수는 현재 로그로 확인하는 기준이며 테스트의 고정 기대값은 아니다. | 기존 실행 순서·표본 형식을 유지하고 고정 지문 단언을 추가 |

근거: `T/ChainRushRaceFieldTests.cs:265`, `T/ChainRushRaceFieldTests.cs:321`, `T/ChainRushContactTests.cs:308`, `T/ChainRushContactTests.cs:330`, `T/ChainRushReplayTests.cs:51`, `T/ChainRushReplayTests.cs:146`

### 건드릴 것

| 기존 파일·근거 위치 | 제안하는 변경 |
|---|---|
| `R/Race/RaceField.cs:157` | 기존 틱 본문을 공통 진입점에서 사용하도록 하고, 필드 전체 캡처·복원과 틱 경계 검사를 추가한다. 기존 관측 배열과 입력→이동→접촉→판정 순서는 재사용한다. |
| `R/ChainRushGame.cs:278` | 필드용 내부 캡처·복원 경로를 추가한다. 월드별 Running·Complete·Failed 상태를 보존한다. 기존 `StepTick` 실행 순서와 기존 `SimSnapshot` 형식은 유지한다. |
| `R/Race/RaceOrder.cs:25` | 순위 계산의 이력 상태를 깊은 사본으로 캡처·복원한다. 순위 계산식은 바꾸지 않는다. |
| `R/Bots/RaceBot.cs:22` | 별도 입력원 복원 인터페이스를 구현한다. 전략·난이도 조정은 포함하지 않는다. 난이도 작업이 확정한 코드에 맞춰 통합한다. |
| `R/Bots/CircuitBot.cs:35` | 현재 입력 생성 동작을 유지하며 복원 지원을 명시한다. 상태 없음도 명시적 계약으로 표현한다. |
| `R/Control/InputReplay.cs:11` | 재생 커서를 캡처·복원하고 범위를 검사한다. 기존 로그 종료 후 입력 동작은 유지한다. |
| `T/ChainRushReplayTests.cs:70` | 기존 지문 테스트에 승인된 상수 단언을 추가한다. 기대값 갱신이나 표본 형식 변경은 하지 않는다. |
| `tools/select-tests.ps1:79` | 새 테스트를 실제 소비 경로에 배정한다. 기존 선택 목록을 축소하지 않는다. |

**공개 API 추가가 있는 안이다.** `RaceField`의 수동 틱·캡처·복원 진입점, 입력원 복원 인터페이스, 필요한 `RaceOrder` 캡처·복원이 합의 대상이다. 기존 `IInputSource`의 세 메서드는 변경하지 않는 것을 권고한다.

기존 `RaceField`, `SimSnapshot`, `InputRecorder`, `InputReplay`, `StateTrace`와 관련 테스트를 참고했다. 새 테스트는 기존 `ProtoHarness.Tests.PlayMode` 형식에 맞춘다.

### 새로 만들 것 — 승인 후에만

목적: 월드 하나의 시뮬 상태와 달림·완주·실패 상태를 함께 보존하기 위해  
경로: `Assets/_Project/Scripts/Runtime/ChainRush/Race/RaceWorldSnapshot.cs`  
형태: `struct`

목적: 여러 월드·순위 이력·필드 상태·입력원 상태를 하나의 틱 경계로 보존하기 위해  
경로: `Assets/_Project/Scripts/Runtime/ChainRush/Race/RaceFieldSnapshot.cs`  
형태: `struct`

목적: 상태가 있는 입력원의 중간 상태를 명시적으로 캡처·검사·복원하기 위해  
경로: `Assets/_Project/Scripts/Runtime/ChainRush/Control/IRewindableInputSource.cs`  
형태: `interface`

목적: 접촉과 상태가 있는 봇을 포함한 전원 재생·롤백의 비트 일치를 검증하기 위해  
경로: `Assets/_Project/Scripts/Tests/PlayMode/ChainRushFieldDeterminismTests.cs`  
형태: 테스트

### 필드 전체 스냅샷 계약

**권고 경계:** 첫 필드 틱을 마친 뒤부터, 전원 판정과 순위 수집까지 끝난 틱 사이만 지원한다. 상대가 먼저 완주·실패한 상태와 플레이어 결과 확정 상태는 포함한다. 준비·카운트다운·일시정지·오류로 중단된 필드의 캡처·복원은 이번 범위에서 명시적으로 거부한다.

플레이어의 `TickCompleted`만으로 필드 경계를 판단하지 않는다. `JudgeTick`은 러너별로 이벤트를 발생시키고, 필드의 순위 수집은 전원 판정 뒤에 실행된다. `R/ChainRushGame.cs:248`, `R/Race/RaceField.cs:181`

| 구분 | 포함할 것·처리 방침 | 현재 근거 |
|---|---|---|
| 월드별 상태 | 틱, 위치, `RacerState.Snapshot` 전체, `CircuitRace.Snapshot` 전체, 달림·완주·실패 상태 | 현재 `SimSnapshot`은 틱·러너·위치·서킷만 포함한다. 기존 복원은 항상 Running으로 되돌린다. `R/SimSnapshot.cs:12`, `R/ChainRushGame.cs:286` |
| 러너 세부 상태 | 속도·방향·조향, 게이지, 그래플·로프, 스윙·부스터 관련 값, 접지·복귀 기준·대기, 공격·피격 타이머 등 기존 스냅샷의 모든 필드 | `R/RacerState.cs:48` |
| 서킷 상태 | `LastS`, 진행도, 랩·체크포인트 진행과 시간 배열. 복원 시 트랙 초점도 복원 | `R/Race/CircuitRace.cs:154`, `R/Track/LapCounter.cs:121` |
| 필드 결과 | `places`, `finishTicks`, `progress`, `ended`, `HasResult`, `staged`, `ContactEnabled`. 오류 중단 상태는 정상 스냅샷으로 되살리지 않음 | `R/Race/RaceField.cs:19`, `:246`, `:259` |
| 순위 이력 | `RaceOrder`의 phases, 틱별 progress 이력, ahead, paired, places, ComparedTick, Version. 배열·목록은 깊은 사본 | `R/Race/RaceOrder.cs:25`, `:93` |
| 접촉의 지속 상태 | **`contactSwingBreaks` 전원 값**. 봇의 `observedBreaks`와 같은 시점으로 복원 | `R/Race/RaceField.cs:238`, `R/Bots/RaceBot.cs:109` |
| 관측 상태 | `observedPositions`, `observedVelocities`, `observationReady`. 현재 공개 조회 결과까지 정확히 되돌리기 위해 포함 | `R/Race/RaceField.cs:37`, `:49` |
| 봇 내부 상태 | RaceBot의 stage, active, turnFrom, pressed, swingLeft, cooldown, retryLeft, observedBreaks, retryShortcut | `R/Bots/RaceBot.cs:22` |
| 입력 재생 상태 | 실제 연결된 전원 입력원의 종류·상태 형식 버전·상태 값. `InputReplay`의 커서 포함 | `R/ChainRushGame.cs:146`, `R/Control/InputReplay.cs:11` |
| 일시 작업 배열 | `stepping`과 접촉 풀이용 위치·속도·이동량·임펄스·벽 법선은 다음 틱에서 초기화·덮어쓰는 대상으로 분류. 캡처 대상에서 제외하되 재사용 값이 새지 않는 테스트를 둠 | `R/Race/RaceField.cs:181`, `:214`, `R/Race/RunnerContact.cs:40` |
| 고정 설정 | 논리적 레이서 번호·프로필·트랙·속도·출발 칸·봇 튜닝·입력원 종류를 호환성 검사 정보로 보존. Unity 씬 핸들·오브젝트 참조를 이식 가능한 상태로 취급하지 않음 | `R/Race/StartGrid.cs:53`, `R/Bots/RaceBot.cs:29` |

현재 **단일 월드 스냅샷만 배열로 모으는 것으로는 부족하다.** 순위 이력·완주한 상대의 phase·접촉 누적값·봇 내부 상태·재생 커서가 빠진다.

복원 전에 전원 구성과 상태 형식을 검사한다. 지원하지 않는 입력원, 다른 구성, 잘못된 틱 경계는 예외로 즉시 거부하고 일부 월드만 복원하지 않는다. 복원 도중 예상하지 못한 실패가 발생하면 원문을 남기고 필드를 중단한다.

### 입력원 복원 계약

권고 인터페이스는 `IInputSource`와 별도로 둔다. 계약은 다음과 같다.

- 입력원 종류와 상태 형식 버전을 식별할 수 있어야 한다.
- 캡처 결과는 이후 입력 소비나 호출자의 변경에 영향받지 않는 사본이어야 한다.
- 복원 전 검사와 복원을 구분한다. `Clear`는 새 런 초기화이며 중간 상태 복원의 대용으로 사용하지 않는다.
- `Consume` 결과가 달라지는 모든 내부 상태를 포함한다. 난수를 사용하는 구현이라면 현재 난수 상태도 포함한다.
- 상태가 없는 구현도 이를 명시한다. 인터페이스가 없다는 이유로 상태가 없다고 간주하지 않는다.
- 시험용 계측 래퍼는 안쪽 입력원의 복원 계약을 전달한다. 전달하지 않는 래퍼의 캡처는 거부한다.

현재 `IInputSource`에는 `Poll`, `Consume`, `Clear`만 있다. `InputRecorder`는 소비한 입력을 기록하고 `Clear`에서 안쪽 입력원과 로그를 비운다. 따라서 **기록기를 Clear해서 롤백하는 설계는 채택하지 않는다.** `R/Control/IInputSource.cs:5`, `R/Control/InputRecorder.cs:21`

### 검증 방법 — 영향 평가

**비교를 두 종류로 구분한다.**

1. **전원 입력 재생:** 봇을 입력 로그로 대체하므로 전원 월드 상태와 필드 상태를 비교한다. 원래 봇과 재생 입력원의 내부 상태를 서로 같다고 요구하지 않는다.
2. **봇까지 복원한 재실행:** 같은 봇 구현을 복원하므로 재생성한 `TickInput`, 봇 내부 상태, 전원 월드 상태와 필드 상태를 모두 비교한다.

float·double은 비트 표현으로 비교한다. 해시는 요약값으로 남기고, 실패에는 첫 불일치 틱·레이서·필드·양쪽 비트를 출력한다. 마지막 결승 틱도 채집할 수 있도록 새 필드 추적은 필드 틱 종료 지점에서 수행한다. 기존 `StateTrace`는 `Consume` 시점에 일부 상태를 채집하므로 지문 형식을 유지하고 별도로 사용한다. `T/StateTrace.cs:43`, `T/StateTrace.cs:58`

**새 테스트 계획**

모두 `ChainRushFieldDeterminismTests`에 배치하고, 제외 카테고리나 `[Explicit]`를 붙이지 않아 마일스톤 게이트에 포함하는 것을 권고한다. 틱 수는 제안하는 실행·탐색 상한이다.

| 이름 — 대상_조건_기대결과 | 틱 수·핵심 단언 |
|---|---|
| `FieldReplay_AllRacerInputs_RepeatsEveryTickBitForBit` | 4명 × 1,700틱. 전원 입력을 기록한 뒤 같은 판 재시작·씬 재로드·프레임당 틱 묶음 변경으로 재생. 접촉 켬, 마지막 틱까지 전체 상태 비교 |
| `FieldReplay_OneRacerInputChanged_DivergesAtTheChangedTick` | 700틱. 입력 인덱스 300을 바꾸고 301틱부터 차이가 검출되는지 확인. 이전 틱 일치도 단언 |
| `FieldInput_SourcesReadOtherRacers_UsesOnePreInputObservation` | 10틱. 기존 관측 API가 전원에게 동일한 틱 시작 위치·속도를 제공하는지 확인 |
| `FieldContact_SameLogicalRosterWithDifferentLoadOrder_RepeatsEveryTickBitForBit` | 300틱씩 2회. 논리적 레이서 번호·접촉 순서는 유지하고 복사본 로드 순서를 바꿔 비교 |
| `FieldRollback_BeforeAndAfterWallContact_RepeatsWholeFieldBitForBit` | 300틱. 벽 옆 다인 접촉 전·후 스냅샷에서 각각 60틱 및 남은 구간 재실행 |
| `FieldRollback_AfterSwingContact_RestoresBreakCountersAndBotRetry` | 300틱의 통제된 시험 상황. 스윙 접촉 중단 전·후에서 120틱 재실행. 필드 누적값과 봇의 재시도 상태 포함 |
| `FieldRollback_NearTheOrderMargin_RestoresPairHistoryAndPlaces` | 120틱. 1m 이내 선후 이력이 있는 상태를 복원하고 다음 순위까지 비교 |
| `FieldRollback_AcrossOpponentAndPlayerFinish_RestoresPhasesAndResults` | 결승을 배치한 상황에서 최대 300틱. 상대 선완주·동시 결승·플레이어 결승을 지나 복원하고 결과·정지 틱 비교 |
| `RaceBot_FromStatefulTechniqueStages_RegeneratesInputsAndFieldState` | 단계별 상황 탐색 최대 6,000틱, 각 지점에서 120틱 재실행. 지름길·코너 스윙·대기·재시도 상태 방문을 단언. 방문하지 못하면 실패 |
| `FieldRollback_TwentyRestoresInOneFrame_RepeatsEveryTickBitForBit` | 기준 런 1,700틱 후 한 프레임에서 20회 × 40틱 |
| `FieldSnapshot_MidTickOrIncompatibleSource_ThrowsBeforeChangingAnyWorld` | 1~2틱. 러너별 완료 콜백을 포함한 필드 중간·구성 불일치·미지원 입력원 거부 |
| `FieldSnapshot_RunContinuesOrCopiesChange_KeepsCapturedStateIndependent` | 120틱. 캡처 이후 상태 변경과 사본 변경이 저장 상태에 전파되지 않음을 확인 |
| `RaceBot_RestartAfterStatefulActions_MatchesAFreshBot` | 통제된 300틱. 재시작한 봇과 새 봇의 입력·상태 비교 |

`RaceBot` 상태 검증은 Normal·Hard 이름만으로 충족시키지 않는다. 조사 당시 두 프리셋은 같은 생성식을 사용하고 기술 플래그 기본값도 꺼져 있다. 기술을 켠 시험 설정과 실제 상태 방문 단언을 사용한다. `R/Bots/BotTuning.cs:37`

**변경 → 읽는 곳 → 선택할 검증**

| 변경 | 소비 경로·선택 |
|---|---|
| 필드 틱·복원 | `RaceField`의 입력·접촉·판정·순위 수집 → 새 테스트, 기존 RaceField·Contact·BotRace·RaceBot 테스트 |
| 게임의 필드용 상태 접근 | `ChainRushGame`의 상태·복원 경로 → 기존 지문, Rollback·World·Tutorial 및 관련 Flow 테스트, `NetReconcileTests` |
| 순위 캡처·복원 | `RaceField`가 사용하는 `RaceOrder` → 새 순위 롤백, 기존 RaceOrder·순위 표시·Net 관련 테스트 |
| 재생 커서·봇 상태 | 실제 입력 소비 → 새 전원 재생·봇 재생성 테스트, 기존 Replay·Rollback·RaceBot 테스트 |
| 혼자 판 불변 조건 | 기존 지문 테스트 → `0xEF92FB76BFD1D767` 고정 단언과 기존 비트 비교 |

`tools/select-tests.ps1`에는 다음을 배정한다.

- 기존 `Race/` 일반 행과 그보다 먼저 일치하는 `RaceOrder` 행에 새 클래스를 추가한다.
- `Bots/(BotDifficulty|BotTuning|RaceBot)` 행과 `InputReplay` 해당 행에 새 클래스를 추가한다.
- 새 `IRewindableInputSource` 경로를 명시적으로 배정한다.
- `CircuitRace` 변경 시에도 필드 검증이 선택되도록 기존 전용 행을 보완한다.
- `CircuitBot` 등의 기존 `All` 판정과 기존 테스트 목록은 이 작업에서 축소하지 않는다.

현재 도구는 **처음 일치한 행만 사용**한다. 일반 행에만 추가하면 앞선 전용 행에서 빠질 수 있다. `tools/select-tests.ps1:48`, `tools/select-tests.ps1:79`

**테스트 3단 적용**

- ① 작업 중·② 기능 병합: 마지막 통과 이후 변경에 대한 영향 평가로 이름을 선택해 실행한다. 도구가 “전체”로 판단해도 게이트 전체를 실행하지 않는다.
- 혼자 판의 실제 입력·이동에 변경이 들어가면 규칙대로 공용 주행 영향과 해당 Lap 범위를 평가한다. 캡처·조회 추가만으로 끝났는지 diff로 확인한다.
- ③ M5 태그 전: 같은 코드에서 EditMode 전체(`Enemy` 제외) 1회, PlayMode 게이트 전체 2회, Device 1회, 사용자 플레이 확인을 충족한다.
- 기존 `Net` 테스트는 유지한다. 카테고리로 선택할 때는 `^Net$`를 사용한다. `NetRace` 실행은 M7까지 미룬다.
- 각 실행의 XML 값·시각과 실패 원문을 남긴다. 설명되지 않은 실패를 재실행 통과로 덮지 않는다.

근거: `CLAUDE.md:293`, `CLAUDE.md:303`, `docs/RULES/VERIFICATION.md:95`, `docs/RULES/VERIFICATION.md:105`

### 안 하는 것

- 봇전 리플레이 UI·파일 저장·고스트·기록 기능과 D4 변경.
- M7 네트워크 구현, `Runtime/Net/` 수정, 네트워크 프로토콜 변경.
- 씬·프리팹·`.asset`·ProjectSettings·패키지·asmdef 변경.
- 혼자 판의 입력·이동·판정 순서 및 기존 `StateTrace` 지문 형식 변경.
- 봇 난이도·전략·튜닝 개선, 8인 완료 판정, 복사본 로드 최적화.
- 이번 조사 중 파일 생성·수정·이동·삭제, Git 상태 변경, Unity MCP·배치 실행. 단, 조사 뒤 별도 사용자 지시로 이 요청서 Markdown 보관만 수행한다.
- 지정된 에디터 버전 차이 파일 세 개의 수정·정리: `Packages/packages-lock.json`, `ProjectSettings/ProjectVersion.txt`, `ProtoHarness.slnx`.

새 필드 복원 API의 지원 범위·오류 조건은 **새 계약**이다. 기존 테스트의 기대를 통과 목적으로 바꾸지 않는다. 기존 실행 계약을 바꿔야 하는 문제가 발견되면 변경 내용을 밝힌 수정 요청서를 먼저 작성한다.

## 2. 사용자가 정할 것

| 항목 | 선택지와 장단점 | 추천·근거 |
|---|---|---|
| **1. MP3의 용도** | **A. 회귀 검출:** 전원 재생·롤백 테스트와 필요한 상태 API. 범위가 작고 D4 유지. 사용자용 재생 기능은 생기지 않음.<br>**B. 봇전 리플레이 기능:** 버그 재현 자료를 남기기 쉬움. 저장·호환성·UI 및 D4 재합의 필요.<br>**C. M7 준비 중심:** 네트워크 재개에 직접 연결. v1.0 밖 범위가 커짐. | **A.** M5의 결정성 요구를 충족하면서 v1.0 범위를 지킨다. `docs/DESIGN.md:107`, `:112`, `docs/DECISIONS.md:171` |
| **2. 리플레이에 기록할 것** | **A. 플레이어 입력+설정·시드:** 작지만 봇 코드·초기 상태·관측 계약이 같아야 하며 봇 생성기 회귀와 시뮬 회귀가 섞임.<br>**B. 전원 소비 입력+초기 설정:** 봇 입력 생성과 시뮬을 분리해 재현 가능. 기록량이 늘고 봇 내부 재생성은 별도 검증 필요.<br>**C. 시드만:** 작지만 플레이어 입력과 봇 중간 상태를 담지 못함. | **B.** 전원 `TickInput`, 논리적 명단·프로필·트랙·속도·출발 칸·튜닝·초기 상태를 함께 고정한다. 시드만으로 완전 재현을 선언하지 않는다. `R/Control/InputRecorder.cs:21`, `R/Race/StartGrid.cs:53`, `R/Bots/RaceBot.cs:73` |
| **3. 롤백 범위** | **A. 전원 SimSnapshot만:** 재사용이 쉽지만 phase·순위 이력·접촉 누적·봇·커서가 누락됨.<br>**B. 필드 전체, 틱 경계만:** 재실행에 필요한 상태를 포함하고 기존 단일 월드 계약을 유지. 새 상태 API 필요.<br>**C. 생명주기 전체·틱 중간까지:** 적용 범위가 넓음. 카운트다운·일시정지·부분 실행 상태까지 설계해야 함. | **B.** 위 스냅샷 목록을 계약으로 채택한다. `R/SimSnapshot.cs:12`, `R/ChainRushGame.cs:281`, `R/Race/RaceOrder.cs:29`, `R/Race/RaceField.cs:38` |
| **4. 순서·관측·물리의 결정성 계약** | **A. 현재 실행 순서만 고정:** 변경이 적음. 관측·접촉 순서가 암묵적으로 남음.<br>**B. 논리적 번호·접촉 순서 고정+기존 관측 사본 사용:** 재현 조건이 명확함. 로드 순서와 논리적 번호를 구분해야 함.<br>**C. 임의 순열·다른 PC에서도 같은 결과:** 강한 보장. 현재 순차 접촉 풀이와 float 기반 범위를 넘어섬. | **B.** 접촉 짝 순서를 임의로 바꿔도 같아야 한다고 요구하지 않는다. 같은 논리적 순서의 재현을 검증한다. `R/Race/RunnerContact.cs:42`, `R/Race/RaceField.cs:175`, `R/Bots/RaceBot.cs:169` |
| **5. 테스트와 게이트** | **A. 기존 300틱 위치 해시 확대:** 저렴함. 숨은 상태·순위·결승 누락을 직접 잡지 못함.<br>**B. 위 전원 비교·롤백·대조군을 게이트 포함:** 누락 상태와 잘못된 비교기를 검출. 실행 비용 증가.<br>**C. 긴 테스트를 Explicit로 분리:** 게이트 비용 감소. MP3 핵심 회귀가 마일스톤에서 빠짐. | **B.** 작업 중에는 영향 범위만, 마일스톤에는 전체 게이트. 실행 비용은 측정 후 판단한다. `T/ChainRushContactTests.cs:232`, `CLAUDE.md:293`, `:303` |
| **6. 봇 상태 계약** | **A. IInputSource 확장:** 모든 입력원에 복원 지원을 강제. 기존 구현과 동결된 Net 구현까지 영향.<br>**B. 별도 IRewindableInputSource:** 기존 구현의 시그니처 유지. 지원 여부와 래퍼 전달을 명시적으로 검사해야 함.<br>**C. 봇을 새로 만들거나 Clear 호출:** 단순함. 중간 전략·대기·재시도 상태 복원이 아님. | **B. 공개 API 추가임을 명시한다.** 미지원 입력원은 새 캡처 API에서 즉시 거부한다. `R/Control/IInputSource.cs:5`, `R/Bots/RaceBot.cs:62`, `:109`, `R/Control/InputReplay.cs:11` |

## 3. 위험

| 위험·근거 | 잡을 검증·대응 |
|---|---|
| **접촉 중단 누적값과 봇 상태의 불일치.** 필드의 `contactSwingBreaks`와 봇의 `observedBreaks`가 함께 다음 입력에 영향을 준다. `R/Race/RaceField.cs:238`, `R/Bots/RaceBot.cs:109` | 스윙 중단 직전·직후 전체 복원 테스트. 양쪽 값을 같은 경계에서 복원 |
| **순위 이력 누락.** 1m 이내에서는 현재 거리만으로 순위를 재구성할 수 없다. `R/Race/RaceOrder.cs:108` | 선후 이력이 있는 두 러너를 여유 범위 안에서 복원해 이후 순위 비교 |
| **순차 접촉 풀이와 float 누적.** 앞선 짝의 변위·속도 변화가 다음 짝 계산에 들어간다. `R/Race/RunnerContact.cs:45` | 논리적 순서를 고정하고 다인 겹침·벽 압박·장기 재생을 비트 비교 |
| **관측 시점과 데이터 순서.** 현재는 입력 전에 위치·속도를 고정하며 회피 계산도 필드 순서로 누적한다. `R/Race/RaceField.cs:175`, `R/Bots/RaceBot.cs:158` | 기존 관측 API를 재사용하고 입력 소비 순서·복사본 로드 순서 시험을 구분 |
| **CharacterController 내부 정밀도·접지.** 기존 모터는 매 이동 시작에 컨트롤러를 정규화하고 접지를 러너 상태로 보관한다. 접촉은 이동 뒤 추가 Move를 한다. `R/RunnerMotor.cs:272`, `:594`, `:687` | 벽·공중·그래플·접촉 후 복원을 검증. 모터 수정이 필요하면 혼자 판 불변 조건을 포함해 범위를 다시 합의 |
| **물리 씬 혼용.** 복사본은 별도 물리 씬이며 접지·벽 질의는 해당 씬을 사용한다. `R/Race/RaceWorld.cs:14`, `R/RunnerMotor.cs:587` | 기존 World의 격리·공유 물리 대조군 유지, 새 재로드·로드 순서 시험 추가 |
| **래퍼로 인한 봇 관측 연결 누락.** 현재 Initialize는 입력원이 직접 `RaceBot`일 때 Observe를 호출한다. `R/Race/RaceField.cs:137` | 계측 래퍼 도입 시 관측 연결을 명시적으로 확인. 관측 없이 통과한 결과를 봇전 결정성 증거로 쓰지 않음 |
| **캐시된 입력원과 실제 입력원의 차이.** 필드는 입력원을 보관하지만 게임은 이후 `SetInputSource`로 바꿀 수 있다. `R/Race/RaceField.cs:135`, `R/ChainRushGame.cs:138` | 캡처는 실제 `Game.InputSource`를 기준으로 하며 복원 시 연결 호환성을 검사 |
| **미완주 상태로 잘못 복원.** 현재 기존 복원은 phase를 Running으로 지정한다. `R/ChainRushGame.cs:293` | 상대 선완주·동시 결승·플레이어 결과 이후 복원 시험 |
| **비교기의 사각지대.** 기존 `StateTrace`는 모든 RacerState·순위·봇 필드를 담지 않는다. `T/StateTrace.cs:17` | 새 상태 필드별 비교와 입력 변조 대조군. 지문용 추적 형식은 유지 |
| **기술을 사용하지 않은 봇 테스트의 통과.** 조사 당시 프리셋의 기술 플래그 기본값은 꺼져 있다. `R/Bots/BotTuning.cs:42` | 기술별 설정을 명시하고 실제 단계 방문을 필수 단언 |
| **단일 월드 형식 변경이 동결 Net에 전파.** Net 코덱은 기존 SimSnapshot 필드를 고정 순서로 읽고 쓴다. `Assets/_Project/Scripts/Runtime/Net/SimSnapshotCodec.cs:15` | 기존 형식 유지, 필드용 스냅샷 별도 도입. 기존 Net 검증은 유지 |

## 4. 확인 못 한 것

- **실행 검증 안 됨:** 이번 조사에서는 컴파일·PlayMode·EditMode·Unity Console 확인을 하지 않았다. 문서의 MP1·MP2 통과 기록을 현재 작업 트리의 실행 결과로 간주하지 않는다.
- **현재 작업 트리 전체의 일관성:** 다른 작업이 같은 트리를 수정하고 있어 조사 도중 코드가 바뀌었다. 위 내용은 마지막으로 읽은 소스에 맞췄지만, 확정 커밋에 묶인 검토는 아니다.
- **난이도 작업의 최종 계약:** `RaceBot`·`BotTuning`·관측 API가 최종적으로 어떤 형태로 병합될지 확인하지 못했다. MP3 구현 전에 확정본과 상태 목록·줄 번호를 다시 대조해야 한다.
- **시험 구간의 충분성:** 제안한 1,700틱·6,000틱 상한에서 각 기술 단계와 접촉 상황을 모두 방문하는지 실행으로 확인하지 못했다. 방문 단언이 실패하면 시험 상황을 보완하고 이유를 밝힌다.
- **성능·메모리·게이트 시간:** 순위 이력과 전원 상태를 깊게 복사하는 비용을 측정하지 않았다.
- **다른 PC·플레이어 빌드·8인:** 비트 일치를 확인하지 못했다. 이 초안의 보장 범위에 포함하지 않는다.
- **MP1의 미확인 로드 중단 경로·표현·프레임, MP2의 복귀 겹침·8인 격자:** 이번 조사로 해결됐다고 판단하지 않는다. 기존 남은 항목으로 유지한다. `docs/DECISIONS.md:265`, `:298`, `:102`

HANDOFF는 최초 파일 변경 금지와 이번 요청서 보관 범위를 지키기 위해 갱신하지 않았다. 새 세션 권장: 난이도 작업의 확정본을 기준으로 MP3 구현 범위를 합의할 때.
