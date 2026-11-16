# 러너 캐릭터 Unity 반영 — §3-2 합의 요청서 초안

> 작성: 2026-11-15. 승인 상태: **구현 승인 없음**.
> 최초 요청은 읽기 전용 조사·응답 본문 초안 작성만 허용했다. 이후 사용자가 이 요청서의 Markdown 보관을 승인했다. 이 파일 저장은 Unity 반영·코드 수정·에셋 삭제 승인이 아니다.
> 최초 조사 시 브랜치: `feature/mp2-bot-difficulty`. 공유 작업 트리이므로 브랜치·Git 상태를 바꾸는 명령, Unity MCP·배치 실행은 하지 않았다.
> 본문의 경로·줄 번호는 최초 조사 시점의 근거다. 구현 직전에 현재 파일과 다시 대조한다. 저장하면서 초안에 남은 일본어를 한국어로 바로잡고, 지오메트리 GUID 참조 수를 각각 **총 25곳**으로 명확히 했다.

## 1. 요청서 본문

### 목표

기존 러너의 물리·입력·주행 결과를 유지하면서, 플레이어와 봇 상대의 겉모습을 5종 스킨 메시와 상태 기반 모션으로 교체한다. 반영 뒤 서킷 지문은 **`0xEF92FB76BFD1D767`**이어야 한다. 기존 기준은 [HANDOFF.md:15](HANDOFF.md#L15), 지문을 출력하는 테스트는 [ChainRushReplayTests.cs:51](../Assets/_Project/Scripts/Tests/PlayMode/ChainRushReplayTests.cs#L51)이다.

이하 변경 범위는 **② 발바닥 수평 판 + Animator + 기존 에셋 삭제 보류**를 추천안으로 작성했다. 다른 선택을 승인하면 해당 범위를 다시 맞춘다.

원본 수량과 최신 사양은 다음과 같이 구분한다.

- FBX는 **파일 2개**이며, 각 파일에 러너 **5종 × 닫힌/열린 얼굴 메시 10개**가 들어 있다. 바이너리를 읽어 뼈 17개, 클립 ① 8개·② 9개를 확인했다. 기록 근거는 [ART_REQUEST_RUNNER.md:211](ART_REQUEST_RUNNER.md#L211)이다.
- 최신 슬롯 사양은 **닫힌 얼굴 3개·열린 얼굴 2개**다. 최초의 “머티리얼 2개 상한”을 닫힌 얼굴에 그대로 적용하지 않는다. 헬멧·OMEGA 바이저 수정 기록을 우선한다. [ART_REQUEST_RUNNER.md:218](ART_REQUEST_RUNNER.md#L218), [같은 문서:219](ART_REQUEST_RUNNER.md#L219)
- “기존 모형 에셋 20개”에 정확히 대응하는 목록은 **러너별 머티리얼 5×4개**다. 프리팹·지오메트리·공용 모형은 별도 목록으로 아래에 적는다.

### 건드릴 것

| 기존 경로·근거 | 제안하는 변경 |
|---|---|
| [Visuals/RunnerLook.cs:17](../Assets/_Project/Scripts/Runtime/ChainRush/Visuals/RunnerLook.cs#L17) | 계층별 `MeshFilter`·`MeshRenderer` 복사를 폐기하고, 아래의 스킨 메시 계약으로 교체한다. 전체 검증 뒤 반영하는 원자성은 유지한다. |
| [RunnerProfile.cs:20](../Assets/_Project/Scripts/Runtime/ChainRush/RunnerProfile.cs#L20) | `look`의 설명을 새 프리팹 계약으로 갱신한다. `GameObject Look` 타입과 이동 배율·무게 값은 유지한다. |
| [Flow/RaceAssembler.cs:141](../Assets/_Project/Scripts/Runtime/ChainRush/Flow/RaceAssembler.cs#L141), [같은 파일:188](../Assets/_Project/Scripts/Runtime/ChainRush/Flow/RaceAssembler.cs#L188) | 대상·원본의 `RunnerVisual`을 조립 시 확보해 새 `Apply`를 호출한다. 잘못된 겉모습은 예외 원문을 남기고 레이스 조립을 중단한다. |
| [Visuals/RunnerAnimation.cs:9](../Assets/_Project/Scripts/Runtime/ChainRush/Visuals/RunnerAnimation.cs#L9), [같은 파일:39](../Assets/_Project/Scripts/Runtime/ChainRush/Visuals/RunnerAnimation.cs#L39) | 관절 각도 직접 계산을 Animator 구동으로 바꾼다. 실패 숙임은 제거하고 아래 상태표를 적용한다. 발소리·착지음, 일시정지·재시작 처리는 보존한다. |
| [Visuals/RunnerTilt.cs:27](../Assets/_Project/Scripts/Runtime/ChainRush/Visuals/RunnerTilt.cs#L27), [같은 파일:52](../Assets/_Project/Scripts/Runtime/ChainRush/Visuals/RunnerTilt.cs#L52) | 회전 대상은 FBX 자체가 아닌 시각용 부모로 연결한다. 겉모습 적용 완료 뒤 선택된 렌더러만 깜빡임 대상으로 캐시한다. |
| [Visuals/GhostRunner.cs:41](../Assets/_Project/Scripts/Runtime/ChainRush/Visuals/GhostRunner.cs#L41) | 복제본 Animator를 정지시키고 선택된 얼굴만 유지한다. 반투명화가 숨긴 얼굴을 켜지 않도록 검증한다. |
| [Flow/RunnerPreview.cs:48](../Assets/_Project/Scripts/Runtime/ChainRush/Flow/RunnerPreview.cs#L48), [같은 파일:138](../Assets/_Project/Scripts/Runtime/ChainRush/Flow/RunnerPreview.cs#L138) | 복제 뒤 Idle 첫 포즈를 적용하고 정지시킨다. 실제 표시되는 얼굴의 렌더러로 크기를 잰다. |
| [Race/RaceField.cs:188](../Assets/_Project/Scripts/Runtime/ChainRush/Race/RaceField.cs#L188), [Flow/RaceAssembler.cs:322](../Assets/_Project/Scripts/Runtime/ChainRush/Flow/RaceAssembler.cs#L322) | 러너 간 충돌 결과를 표시용으로 읽는 관측값과 각 애니메이션의 필드 연결을 추가한다. 해결기 입력·출력·적용 순서와 시뮬 상태는 바꾸지 않는다. |
| [Editor/ChainRushSceneBuilder.cs:138](../Assets/_Project/Scripts/Editor/ChainRush/ChainRushSceneBuilder.cs#L138) | 옛 문자열 경로 `Torso Joint/Right Arm Joint/Chain Origin` 대신 `RunnerVisual.ChainOrigin`을 연결하도록 바꾼다. |
| [GrappleController.cs:18](../Assets/_Project/Scripts/Runtime/ChainRush/GrappleController.cs#L18) | **코드는 유지**, 씬의 직렬화 참조를 새 `ChainOrigin` 뼈로 연결한다. |
| `Assets/_Project/Prefabs/ArmoredRunner.prefab`, `Assets/_Project/Art/Models/Runner{Alpha,Beta,Theta,Delta,Omega}.prefab` | 에디터에서 기존 프리팹 내용을 새 리그·선택 얼굴·재질 참조로 교체한다. 기존 에셋의 GUID는 유지한다. 참조 근거는 아래 목록과 같다. |
| `Assets/_Project/Scenes/ChainRushCircuit.unity`, `Assets/_Project/Scenes/ChainRushTestBench.unity` | 에디터에서 시각용 부모, `RunnerVisual`, Animator, `RunnerAnimation`, `RunnerTilt.body`, `ropeOrigin`을 연결하고 저장한다. 물리 루트·캡슐 값은 유지한다. 공용 프리팹 참조는 각각 [Circuit:396](../Assets/_Project/Scenes/ChainRushCircuit.unity#L396), [TestBench:10764](../Assets/_Project/Scenes/ChainRushTestBench.unity#L10764)에서 GUID grep으로 확인했다. |
| [Tests/EditMode/RunnerLookTests.cs:33](../Assets/_Project/Scripts/Tests/EditMode/RunnerLookTests.cs#L33), [RunnerPreviewTests.cs:30](../Assets/_Project/Scripts/Tests/EditMode/RunnerPreviewTests.cs#L30) | 새 계약·원자성·얼굴 선택·프리뷰 포즈와 크기 검증으로 갱신한다. |
| [Tests/PlayMode/ChainRushPresentationTests.cs:67](../Assets/_Project/Scripts/Tests/PlayMode/ChainRushPresentationTests.cs#L67), [ChainRushProfileTests.cs:82](../Assets/_Project/Scripts/Tests/PlayMode/ChainRushProfileTests.cs#L82), [ChainRushFlowTests.cs:573](../Assets/_Project/Scripts/Tests/PlayMode/ChainRushFlowTests.cs#L573) | 먼저 합의한 새 계약을 시험하도록 바꾼다. 기존 `MeshFilter` 비교나 관절 identity 비교를 새 모델의 실제 계약으로 대체한다. |
| [ChainRushRaceFieldTests.cs:111](../Assets/_Project/Scripts/Tests/PlayMode/ChainRushRaceFieldTests.cs#L111), [ChainRushContactTests.cs:60](../Assets/_Project/Scripts/Tests/PlayMode/ChainRushContactTests.cs#L60) | 봇 상대의 모션·얼굴·표시용 충돌 관측값 검증을 추가한다. |
| [tools/select-tests.ps1:54](../tools/select-tests.ps1#L54) | 러너 FBX·Controller·프리팹·재질·텍스처가 표현 검증 후보를 고르도록 판정을 추가한다. 현재 머티리얼·텍스처의 포괄 규칙만으로 끝내지 않는다. |
| [docs/HANDOFF.md:27](HANDOFF.md#L27), [docs/DECISIONS.md:113](DECISIONS.md#L113) | 구현 승인 후 별도 작업에서 결정·검증 결과·남은 미검증 항목을 기록한다. 기존 확정 이력은 덮어쓰지 않는다. |

`Runtime/Net/RemoteGhost.cs`의 동작 확장은 범위에서 제외한다. 복제 지점은 [RemoteGhost.cs:33](../Assets/_Project/Scripts/Runtime/Net/RemoteGhost.cs#L33)이며, 네트워크 코드는 컴파일 유지 대상으로만 둔다.

### RunnerLook의 새 공개 계약 — 제안

현재:

`public static void Apply(Transform model, GameObject look)`

추천:

`public static void Apply(RunnerVisual model, RunnerVisual look, bool openFace)`

- `false`는 닫힌 얼굴, `true`는 열린 얼굴이다. 이번 기본 표시는 닫힌 얼굴로 하고, 열린 얼굴은 에디터 설정·검증 경로로 제공한다. 새 로비 선택 UI는 만들지 않는다.
- `RunnerVisual`은 리그, 닫힌/열린 `SkinnedMeshRenderer`, Animator, `ChainOrigin`의 직렬화 참조를 가진다.
- 두 메시·머티리얼과 원본 뼈 배열을 먼저 검증한다. 원본 뼈 이름을 대상 리그에 대응시킨 뒤 적용하며, 원본 프리팹의 Transform을 대상 `bones`에 그대로 넣지 않는다.
- 대상 리그·물리 루트·캡슐·`ChainOrigin` 참조와 위치·회전·스케일은 교체하지 않는다. 선택된 얼굴의 렌더러 하나만 표시한다.
- null 인자는 `ArgumentNullException`, 뼈·슬롯·메시 계약 불일치는 `ArgumentException`으로 거부한다. 실패 시 기존 메시·재질·표시 상태를 그대로 둔다.
- 바인딩·배열·상태 해시 등은 조립 단계에 캐시한다. `Awake`가 먼저 실행됐다는 가정에 의존하지 않는다.
- 직렬화 필수 참조가 비면 `LogError(..., this)`와 `enabled=false`, 편집 중에는 `OnValidate`로 드러낸다.

직접 호출처는 검색 결과 다음 **7곳**이다. 로비·고스트는 `Apply` 직접 호출처가 아닌 간접 소비자다.

| 호출처 | 변경 |
|---|---|
| [RaceAssembler.cs:200](../Assets/_Project/Scripts/Runtime/ChainRush/Flow/RaceAssembler.cs#L200) | 조립 시 확보한 대상·원본 `RunnerVisual`과 얼굴 설정 전달 |
| [ChainRushProfileTests.cs:94](../Assets/_Project/Scripts/Tests/PlayMode/ChainRushProfileTests.cs#L94) | 5종의 새 계약 시험 |
| [RunnerLookTests.cs:38](../Assets/_Project/Scripts/Tests/EditMode/RunnerLookTests.cs#L38) | 정상 적용 시험 |
| [RunnerLookTests.cs:59](../Assets/_Project/Scripts/Tests/EditMode/RunnerLookTests.cs#L59) | 새 뼈 대응 계약 시험으로 교체 |
| [RunnerLookTests.cs:101](../Assets/_Project/Scripts/Tests/EditMode/RunnerLookTests.cs#L101) | 대상 null 시험 |
| [RunnerLookTests.cs:102](../Assets/_Project/Scripts/Tests/EditMode/RunnerLookTests.cs#L102) | 원본 null 시험 |
| [RunnerLookTests.cs:118](../Assets/_Project/Scripts/Tests/EditMode/RunnerLookTests.cs#L118) | 불일치 거부·적용 전 상태 보존 시험 |

기존 [RunnerLook.cs:15](../Assets/_Project/Scripts/Runtime/ChainRush/Visuals/RunnerLook.cs#L15)의 “전체 검증 후 변경” 형식을 유지한다. 새 컴포넌트의 참조 검사·로깅 형식은 끝까지 읽은 [RunnerTilt.cs:27](../Assets/_Project/Scripts/Runtime/ChainRush/Visuals/RunnerTilt.cs#L27)을 따른다.

### Animator 첫 사례와 상태 선택 — 제안

`Assets`의 C# 173개에서 Animator 관련 타입 사용을 찾지 못했고, `.controller`·`.overrideController`·`.anim` 파일도 찾지 못했다. 따라서 **첫 Animator Controller의 위치·형식·구동 방식 자체를 합의 대상**으로 둔다. 기존 표현 담당은 [RunnerAnimation.cs:7](../Assets/_Project/Scripts/Runtime/ChainRush/Visuals/RunnerAnimation.cs#L7)이다.

추천 형식은 공용 Generic 리그 Controller 하나, 상태 식별값과 재생 속도를 전달하는 `RunnerAnimation`, root motion 비사용이다. 클립 선택은 코드 한 곳에서 우선순위를 정하고 Controller에 전달한다. Animation Event가 시뮬 함수를 호출하는 경로는 만들지 않는다.

위에서 먼저 맞는 상태를 선택한다.

| 우선순위·조건 | 클립 | 제안하는 재생 계약·근거 |
|---|---|---|
| 일시정지 | 현재 클립 | 포즈·재생 시각 동결. 기존 정지 처리는 [RunnerAnimation.cs:41](../Assets/_Project/Scripts/Runtime/ChainRush/Visuals/RunnerAnimation.cs#L41). |
| 재시작·카운트다운·준비·복귀 대기 | `Runner_Idle` | 재시작 시 표현 타이머 초기화. 기존 초기화 호출은 [ChainRushGame.cs:330](../Assets/_Project/Scripts/Runtime/ChainRush/ChainRushGame.cs#L330), 복귀 상태는 [RunnerMotor.cs:171](../Assets/_Project/Scripts/Runtime/ChainRush/RunnerMotor.cs#L171). |
| 결승·실패 | 마지막 포즈 정지 | 실패 숙임은 제거한다. 아트 결정은 [ART_REQUEST_RUNNER.md:118](ART_REQUEST_RUNNER.md#L118). |
| 벽 충돌 신호 또는 새 러너 접촉 신호 | `Runner_Bump` | 1회 재생 후 현재 상태로 복귀. 벽은 기존 `CuePlayed(GuardHit)`를 읽는다. 발생·관측 지점은 [RunnerMotor.cs:339](../Assets/_Project/Scripts/Runtime/ChainRush/RunnerMotor.cs#L339), [ChainRushGame.cs:385](../Assets/_Project/Scripts/Runtime/ChainRush/ChainRushGame.cs#L385). |
| `player.IsSwinging` | `Runner_PoleSwing` | 스윙+부스터도 이 클립. 상태는 [RunnerMotor.cs:169](../Assets/_Project/Scripts/Runtime/ChainRush/RunnerMotor.cs#L169), 아트 계약은 [ART_REQUEST_RUNNER.md:115](ART_REQUEST_RUNNER.md#L115). |
| 위 조건 없이 `grapple.IsAttached` | `Runner_GrappleHang` | 1키 포즈 유지. 기존 그래플 포즈 분기는 [RunnerAnimation.cs:66](../Assets/_Project/Scripts/Runtime/ChainRush/Visuals/RunnerAnimation.cs#L66). |
| 비접지 | `Runner_Air` | 1키 포즈 유지. 접지 신호는 [RunnerMotor.cs:161](../Assets/_Project/Scripts/Runtime/ChainRush/RunnerMotor.cs#L161). |
| 비접지→접지 전환 | `Runner_Land` | 1회 재생. 시작 직후의 가짜 착지는 기존 조건을 보존해 제외한다. [RunnerAnimation.cs:54](../Assets/_Project/Scripts/Runtime/ChainRush/Visuals/RunnerAnimation.cs#L54) |
| 접지 중 부스터 또는 부스트존 잔여 효과 | `Runner_Boost` | 기존 꼬리와 같은 상태 정의를 읽는다. `SlingTicks > 0 || GrappleEmpowered`, 부스트존은 기존 `PadShare > 0` 판정을 사용한다. [ControlFeedback.cs:230](../Assets/_Project/Scripts/Runtime/ChainRush/Visuals/ControlFeedback.cs#L230) |
| 접지 중 전진 속도 > 0 | `Runner_Run` | 속도 비례 재생. 기존 보행 주기 `speed × 1.65`를 기준으로 맞춘다. [RunnerAnimation.cs:57](../Assets/_Project/Scripts/Runtime/ChainRush/Visuals/RunnerAnimation.cs#L57) |
| 나머지 정지 상태 | `Runner_Idle` | 반복. 클립 원본 계약은 [ART_REQUEST_RUNNER.md:109](ART_REQUEST_RUNNER.md#L109). |
| 선택 비교용, 자동 선택 안 함 | `Runner_PoleSwingEdge` | ②에만 포함된 대안이다. 기본 `PoleSwing`을 대체할지는 사용자 시각 비교로 정한다. [ART_REQUEST_RUNNER.md:134](ART_REQUEST_RUNNER.md#L134) |

러너 간 Bump에는 다음 **추가 공개 계약도 합의**한다.

- `RaceField`에 `public uint ContactSequence(int racer)`를 둔다.
- 기존 해결기의 변위·속도 변화가 있는 러너를 표시용 캐시로 기록하고, 한 틱에 한 번만 순번을 올린다. `RacerState`·스냅샷·접촉 계산값에는 넣지 않는다.
- `RunnerAnimation`에 `public void BindField(RaceField field, int racer)`를 두고, `RaceAssembler`가 필드 초기화 후 플레이어 0·상대 i+1을 연결한다. 애니메이션은 순번 변화만 읽는다.
- 솔로에는 필드 연결이 없으며 벽 충돌 신호만 읽는다. 잘못된 필드·인덱스는 예외로 거부한다.

기존 접촉 계산·적용 위치는 [RaceField.cs:206](../Assets/_Project/Scripts/Runtime/ChainRush/Race/RaceField.cs#L206)이다. 원본 포즈는 오른쪽 봉 한 벌이므로, 추천안은 좌우 반전을 추가하지 않고 왼쪽 봉에서도 원본을 표시한 뒤 사용자 플레이 확인을 받는다. 원본 범위는 [ART_REQUEST_RUNNER.md:114](ART_REQUEST_RUNNER.md#L114)다.

### 새로 만들 것 — 파일별 세 줄 선언

기존 6개 프리팹은 에디터에서 갱신하고 새 프리팹은 만들지 않는다. 새 asmdef·네임스페이스도 만들지 않는다. 아래 경로는 프로젝트 루트 기준이다.

목적: 시각 리그와 얼굴·Animator·줄 시작점 참조를 명시적으로 연결하기 위해  
경로: `Assets/_Project/Scripts/Runtime/ChainRush/Visuals/RunnerVisual.cs`  
형태: MonoBehaviour

목적: 5종이 공유하는 상태별 클립 재생을 정의하기 위해  
경로: `Assets/_Project/Art/Animations/Runner.controller`  
형태: Animator Controller — 첫 사례로 위치·형식을 함께 합의

목적: 승인된 ② 리그·메시·클립을 Unity로 가져오기 위해  
경로: `Assets/_Project/Art/Models/RunnerFlatFoot.fbx`  
형태: FBX 모델 — `art-src/Runner/RunnerFlatFoot.fbx` 복사본

목적: ALPHA 본체 팔레트를 적용하기 위해  
경로: `Assets/_Project/Art/Textures/T_RunnerPalette_Alpha_BaseColor.png`  
형태: 텍스처

목적: BETA 본체 팔레트를 적용하기 위해  
경로: `Assets/_Project/Art/Textures/T_RunnerPalette_Beta_BaseColor.png`  
형태: 텍스처

목적: THETA 본체 팔레트를 적용하기 위해  
경로: `Assets/_Project/Art/Textures/T_RunnerPalette_Theta_BaseColor.png`  
형태: 텍스처

목적: DELTA 본체 팔레트를 적용하기 위해  
경로: `Assets/_Project/Art/Textures/T_RunnerPalette_Delta_BaseColor.png`  
형태: 텍스처

목적: OMEGA 본체 팔레트를 적용하기 위해  
경로: `Assets/_Project/Art/Textures/T_RunnerPalette_Omega_BaseColor.png`  
형태: 텍스처

목적: ALPHA 본체의 비발광 재질을 연결하기 위해  
경로: `Assets/_Project/Art/Materials/M_RunnerBody_Alpha.mat`  
형태: 머티리얼

목적: BETA 본체의 비발광 재질을 연결하기 위해  
경로: `Assets/_Project/Art/Materials/M_RunnerBody_Beta.mat`  
형태: 머티리얼

목적: THETA 본체의 비발광 재질을 연결하기 위해  
경로: `Assets/_Project/Art/Materials/M_RunnerBody_Theta.mat`  
형태: 머티리얼

목적: DELTA 본체의 비발광 재질을 연결하기 위해  
경로: `Assets/_Project/Art/Materials/M_RunnerBody_Delta.mat`  
형태: 머티리얼

목적: OMEGA 본체의 비발광 재질을 연결하기 위해  
경로: `Assets/_Project/Art/Materials/M_RunnerBody_Omega.mat`  
형태: 머티리얼

목적: ALPHA 민트 발광을 연결하기 위해  
경로: `Assets/_Project/Art/Materials/M_RunnerGlow_Alpha.mat`  
형태: 머티리얼

목적: BETA 발광을 연결하기 위해  
경로: `Assets/_Project/Art/Materials/M_RunnerGlow_Beta.mat`  
형태: 머티리얼

목적: THETA 발광을 연결하기 위해  
경로: `Assets/_Project/Art/Materials/M_RunnerGlow_Theta.mat`  
형태: 머티리얼

목적: DELTA 발광을 연결하기 위해  
경로: `Assets/_Project/Art/Materials/M_RunnerGlow_Delta.mat`  
형태: 머티리얼

목적: OMEGA 발광을 연결하기 위해  
경로: `Assets/_Project/Art/Materials/M_RunnerGlow_Omega.mat`  
형태: 머티리얼

목적: ALPHA·BETA·THETA·DELTA의 닫힌 바이저 광택을 공유하기 위해  
경로: `Assets/_Project/Art/Materials/M_RunnerVisor.mat`  
형태: 머티리얼

목적: OMEGA 닫힌 바이저의 빨강과 광택을 연결하기 위해  
경로: `Assets/_Project/Art/Materials/M_RunnerVisor_Omega.mat`  
형태: 머티리얼

FBX·Controller·재질·텍스처는 §1-1의 기본 형태 목록 밖이므로 위 형태를 이번 첫 사례의 명시적 합의에 포함한다. 경로 기준은 [CONVENTIONS.md:7](RULES/CONVENTIONS.md#L7), 아트 원본과 Unity 복사본의 경계는 [같은 문서:31](RULES/CONVENTIONS.md#L31)이다.

### 검증 방법

이번에는 실행하지 않는다. 아래는 구현 승인 후 에디터 사용이 허용된 시간에 수행할 계획이다.

| 영향 평가: 바뀐 것 → 읽는 곳 | 고른 검증 |
|---|---|
| 새 스킨 계약 → `RaceAssembler.TryDress`, 프로필 겉모습, 로비 미리보기 | EditMode `RunnerLookTests`, `RunnerPreviewTests`. null·뼈 누락·슬롯 불일치 거부, 실패 전후 상태 동일, 5종×얼굴 2벌, 선택 렌더러 하나, 대상 리그 참조 유지 검증 |
| Animator·상태 우선순위 → `RunnerAnimation`, 재시작 호출 | PlayMode `ChainRushPresentationTests`. 상태표 전환, Run/Boost 속도, pause/resume, reset, Bump 종료·재진입, 기존 발소리·착지음 검증. 기존 소비 근거는 [ChainRushGame.cs:330](../Assets/_Project/Scripts/Runtime/ChainRush/ChainRushGame.cs#L330) |
| 프리팹·프로필·프리뷰 → 로비·레이스 조립 | `ChainRushProfileTests.Profiles_TheFiveRunners_EachHaveALookThatFitsTheRunnerModel`, `ChainRushFlowTests.Race_FromTheLobby_WearsTheChosenRunnersLook`, `Lobby_RunnerPage_PreviewShowsAndDrawsTheHighlightedRunner`, `Race_OpenedDirectly_RunsAsSavedAndWaitsForEnter` |
| 복제본 처리 → `GhostRunner` | 기존 `ChainRushFlowTests.Race_TheBestsGhost_FollowsItsTraceTickByTick_AndHidesAtItsEnd`를 선택하고, Animator 정지·얼굴 중복 표시 없음 검증을 추가한다. |
| 시각용 부모·줄 시작점 → `RunnerTilt`, `GrappleController` | `RunnerTiltTests`, `ChainRushInputSourceTests.Tilt_` 2건, `ChainRushCornerSwingTests.SwingLook_WhileSwinging_TurnsOnlyTheBodyInsideAndHooksThePoleTop`. 숨긴 얼굴이 복귀 깜빡임 후 켜지지 않는 검증 추가 |
| 봇 상대 조립·모션 → `HideAllButTheRunner`, 각 상대의 게임 상태 | `ChainRushRaceFieldTests`의 `Race_WithThreeOpponents_LoadsAnIsolatedWorldForEach_ShowingOnlyItsRunner`, `Race_AnOpponentWhoseWorldFailsToAssemble_StopsTheRaceAndUnloadsTheCopies`, `Field_PauseAndRestart_TheOpponentsFollowThePlayersGame`; `ChainRushBotRaceTests.Lobby_BotsThroughSharedPages_LoadsThreeSeededBotsExcludingThePlayer` |
| 접촉 관측값 → 각 러너의 Bump 표시 | `ChainRushContactTests`로 기존 접촉 결과를 확인하고, 틱당 1회 관측·재시작 초기화·비접촉 때 미증가·4명 모션 전달을 추가 검증 |
| 표현 교체 → 같은 물리 씬의 주행 | 사용자 요구에 따라 `ChainRushReplayTests.Replay_CircuitBotRun_RepeatsEveryTickBitForBit`를 명시적으로 추가. 출력 지문 **`0xEF92FB76BFD1D767`** 확인. 필드 회귀는 `ChainRushRaceFieldTests.Race_WithThreeOpponents_ThePlayersRunIsBitForBitItsSoloRun_AndEveryWorldIsOnTheSameTick`, `ChainRushContactTests.Field_SeededBotRaceTwice_RepeatsEveryRunnersPositionHash`로 확인 |
| 테스트 선택표 변경 → 향후 검증 대상 선택 | 승인 범위에 대응하는 선택 결과를 확인하고, 러너 FBX·Controller·프리팹이 검증 대상에서 빠지지 않는지 확인 |

주행 코드·프로필 이동 값·트랙 구조를 바꾸지 않으므로 **Lap 전체와 PlayMode 게이트 전체는 선택하지 않는다.** 선택 도구가 `전체`를 내면 위 영향 평가로 이름을 지정한다. 구체적인 테스트 목록을 출력했다면 그 하한을 임의로 줄이지 않는다. 기준은 [VERIFICATION.md:95](RULES/VERIFICATION.md#L95), [같은 문서:98](RULES/VERIFICATION.md#L98), [같은 문서:102](RULES/VERIFICATION.md#L102)다.

각 실행은 실제 에디터 버전·시각·필터와 결과 XML의 `testcasecount/result/passed/failed`를 보고한다. 실패 원문을 남기며, 재실행 통과로 앞선 실패를 지우지 않는다. 컴파일과 Console도 원문으로 확인한다.

에셋·에디터 확인 항목:

- 실제 임포트된 메시 10개·뼈 17개, 비변형 `Root`·`ChainOrigin`, 클립 9개와 원본 AnimStack 대응을 확인한다.
- Generic 리그·클립 반복 여부·root motion 비사용·루트의 전진 이동 없음·Root 뼈의 착지 상하 운동 보존을 확인한다.
- 회전 순서를 **RunnerTilt → 해당 프레임 뼈 포즈 평가 → GrappleController 줄 갱신**으로 검증한다. 기존 실행 순서는 [RunnerTilt.cs:9](../Assets/_Project/Scripts/Runtime/ChainRush/Visuals/RunnerTilt.cs#L9), [RunnerAnimation.cs:6](../Assets/_Project/Scripts/Runtime/ChainRush/Visuals/RunnerAnimation.cs#L6), [GrappleController.cs:8](../Assets/_Project/Scripts/Runtime/ChainRush/GrappleController.cs#L8)이다.
- 클립의 키뿐 아니라 키 사이를 재생해 접지·바닥 관통·발 미끄럼·OMEGA 어깨 관통·스킨 메시 화면 밖 잘림을 확인한다.
- 몸체 팔레트, 발광색, 닫힌 얼굴 3슬롯·열린 얼굴 2슬롯, OMEGA 전용 바이저를 확인한다.
- 바이저는 같은 조명·카메라에서 정면·사선·회전 화면으로 비교한다. 우선 URP Lit의 Metallic 0.35와 Smoothness 조절로 시각 확인한다. Blender Roughness 0.12·Coat 0.8이 자동으로 같은 결과가 됐다고 판정하지 않는다. 로컬 Lit에는 `_Smoothness`, `_Metallic`과 숨긴 ClearCoat 속성이 있다. 최초 조사에서 확인한 로컬 소스는 `Library/PackageCache/com.unity.render-pipelines.universal@8a9b4021522a/Shaders/Lit.shader:13,44`이며, 패키지 캐시는 환경별로 다시 찾는다.

사용자 플레이 확인 항목:

1. 로비 5종의 색·헬멧·바이저, 닫힌/열린 얼굴 확인.
2. 출발 대기→달리기→점프→착지, 부스트·부스트존, 그래플 매달림·봉 스윙 확인.
3. 벽·러너 충돌 움찔, 왼쪽 봉에서의 포즈, `PoleSwing`/`PoleSwingEdge` 비교.
4. 봇전 4명 모두의 모션·pause/resume·재시작·복귀 깜빡임 확인.
5. 손의 줄 시작점, 단독전 고스트의 형태와 얼굴 표시 확인.
6. 바이저 광택과 발 접지의 수용 여부를 사용자 말 그대로 기록.

### 안 하는 것

- 이번 문서 보관에서의 구현·에디터 조작·테스트 실행.
- 시뮬 이동·접촉 해결기·입력·캡슐·프로필 이동 값·트랙·리플레이 형식 변경.
- `.meta` 직접 편집, 씬·프리팹·`.asset` YAML 텍스트 편집.
- ProjectSettings·패키지·빌드 설정 변경.
- 에디터 버전 차이인 `Packages/packages-lock.json`, `ProjectSettings/ProjectVersion.txt`, `ProtoHarness.slnx` 정리.
- Blender 원본·클립 재제작, 왼쪽 봉 전용 새 모션, 새 얼굴 선택 UI.
- 네트워크 모션 확장, 새 잔상 시스템, 다른 장식 최적화.
- 승인 전 기존 에셋 삭제, 브랜치 변경·커밋·병합·푸시.

## 2. 사용자가 정할 것

| 항목 | 선택지와 장단점 | 추천·근거 |
|---|---|---|
| **1. 원본 판** | **① 발목 고정:** 원래 발목 방향 유지. Land·Bump가 위로 들리고 Run/Boost의 상하 폭이 더 크다. **② 발바닥 수평:** 접지 발이 평평하고 Land가 내려앉는다. 봉 스윙의 두 발 처리에는 별도 시각 판단이 남는다. | **②.** Root 주기 폭 Run 0.121→0.092m, Boost 0.153→0.123m, Land +0.023→−0.023m 기록. [아트 요청서:204](ART_REQUEST_RUNNER.md#L204), [같은 문서:206](ART_REQUEST_RUNNER.md#L206). 비교 PNG도 직접 확인했다. |
| **2. 모션 방식** | **b 코드로 뼈 구동:** 기존 표현 코드 형식을 이어간다. 제작된 8/9클립의 포즈·보간·무릎·발목 동작을 코드로 다시 옮기는 작업이 생긴다. **c Animator:** 제작된 클립을 사용하고 교체·비교를 에디터에서 한다. 첫 Controller 형식과 평가 순서·정지·복제본 처리를 새로 검증해야 한다. | **c.** 제작된 클립을 사실 원본으로 사용한다. 첫 사례 형식, 위 상태표, 표시 전용 접촉 관측값까지 함께 승인받는다. [아트 요청서:13](ART_REQUEST_RUNNER.md#L13), [같은 문서:166](ART_REQUEST_RUNNER.md#L166). |
| **3. 겉모습 공개 계약** | **A 공용 리그 유지·스킨 메시 교체:** `Apply(RunnerVisual, RunnerVisual, bool)`로 참조·오류 계약이 드러나고 줄·Animator 바인딩을 유지한다. 뼈 대응 검증이 필요하다. **B 겉모습 프리팹 전체 교체:** 원본 계층을 그대로 사용한다. 교체마다 `RunnerTilt`·애니메이션·줄·깜빡임 참조를 다시 연결해야 한다. | **A.** 기존의 “물리는 유지하고 겉모습만 적용” 계약과 전체 검증 후 변경 원칙을 이어간다. [RunnerLook.cs:7](../Assets/_Project/Scripts/Runtime/ChainRush/Visuals/RunnerLook.cs#L7), [같은 파일:15](../Assets/_Project/Scripts/Runtime/ChainRush/Visuals/RunnerLook.cs#L15). |
| **4. 루트 회전·FBX 예외** | **A 시각용 부모 아래 임포트 루트 보존:** `RunnerTilt`는 부모를 돌리고 자식의 임포트 회전을 유지한다. 추가 계층과 프리뷰·고스트 복사 검증이 필요하다. **B 러너 FBX에만 `bakeAxisConversion` 적용:** 임포터 쪽 축 처리를 선택한다. 적용값·리그·클립 결과를 먼저 실증해야 한다. | **A.** 물리 루트를 회전시키지 않으며, 보정을 각 사용처에 흩뿌리지 않는다. [RunnerTilt.cs:5](../Assets/_Project/Scripts/Runtime/ChainRush/Visuals/RunnerTilt.cs#L5), [DECISIONS.md:466](DECISIONS.md#L466). 원본의 `bake_space_transform=False`, leaf bone 미추가, 비변형 뼈 포함 설정을 유지한다. 에디터에서 실제 루트 회전·스케일·앞 +Z·위 +Y를 기록한다. [아트 요청서:71](ART_REQUEST_RUNNER.md#L71) |
| **5. 슬롯·바이저** | **A 닫힘 3/열림 2슬롯:** 최신 아트의 독립 바이저 재질·OMEGA 빨강을 유지한다. 최초 2슬롯 상한을 개정해야 한다. **B 전부 2슬롯:** 최초 상한을 유지한다. 바이저와 다른 면의 재질 분리를 합치기 위한 아트·셰이더 재설계가 필요하다. | **A.** 닫힘은 Body·Glow·Visor, 열림은 Body·Glow. OMEGA 닫힘만 `M_RunnerVisor_Omega`. URP Lit으로 먼저 화면 검증하고, Coat 재현이 완료 조건이면 별도 셰이더 선택을 다시 합의한다. [아트 요청서:218](ART_REQUEST_RUNNER.md#L218), [같은 문서:219](ART_REQUEST_RUNNER.md#L219) |
| **6. 기존 20개 삭제** | **A 보존:** 교체 검증 중 비교 자료를 유지한다. 미사용 에셋이 남는다. **B 아래 20개 삭제:** 교체 완료 후 옛 머티리얼을 정리한다. 남은 GUID 참조가 0인지 확인하고 에디터에서 삭제해야 한다. | **A, 이번 반영에서는 삭제 보류.** 현재 20개 모두 옛 프리팹이 참조한다. 삭제 승인에는 아래 정확한 목록만 포함하며 지오메트리·공용 모형까지 확대하지 않는다. |
| **7. 드로 배치 측정** | **A 동일 조건 4명 전후 측정 + Frame Debugger:** 전체 변화와 러너 패스를 함께 구분한다. 측정 조건·프레임을 맞추는 작업이 필요하다. **B Game 뷰 Stats 전후 비교:** 빠르다. 트랙·그림자·후처리와 러너 비용의 분리가 부족하다. | **A.** 과거 약 150배치는 8명과 1명의 차이로 계산한 값이다. 이번 4명 측정의 직접 기준값으로 대체하지 않는다. [DECISIONS.md:295](DECISIONS.md#L295) |

### 삭제 선택지 B의 정확한 20개 목록과 현재 끊길 참조

아래 표의 파일 경로는 모두 `Assets/_Project/Art/Materials/` 아래다. `.meta`에서 읽은 GUID로 `Assets`를 검색했으며 YAML은 통독하지 않았다.

| 삭제 후보 | 현재 참조 프리팹·대표 줄 |
|---|---|
| `M_RunnerAlphaArmor.mat` | [RunnerAlpha.prefab:69](../Assets/_Project/Art/Models/RunnerAlpha.prefab#L69) |
| `M_RunnerAlphaSuit.mat` | [RunnerAlpha.prefab:249](../Assets/_Project/Art/Models/RunnerAlpha.prefab#L249) |
| `M_RunnerAlphaMetal.mat` | [RunnerAlpha.prefab:877](../Assets/_Project/Art/Models/RunnerAlpha.prefab#L877) |
| `M_RunnerAlphaSignal.mat` | [RunnerAlpha.prefab:159](../Assets/_Project/Art/Models/RunnerAlpha.prefab#L159) |
| `M_RunnerBetaArmor.mat` | [RunnerBeta.prefab:69](../Assets/_Project/Art/Models/RunnerBeta.prefab#L69) |
| `M_RunnerBetaSuit.mat` | [RunnerBeta.prefab:539](../Assets/_Project/Art/Models/RunnerBeta.prefab#L539) |
| `M_RunnerBetaMetal.mat` | [RunnerBeta.prefab:1699](../Assets/_Project/Art/Models/RunnerBeta.prefab#L1699) |
| `M_RunnerBetaSignal.mat` | [RunnerBeta.prefab:234](../Assets/_Project/Art/Models/RunnerBeta.prefab#L234) |
| `M_RunnerThetaArmor.mat` | [RunnerTheta.prefab:554](../Assets/_Project/Art/Models/RunnerTheta.prefab#L554) |
| `M_RunnerThetaSuit.mat` | [RunnerTheta.prefab:249](../Assets/_Project/Art/Models/RunnerTheta.prefab#L249) |
| `M_RunnerThetaMetal.mat` | [RunnerTheta.prefab:429](../Assets/_Project/Art/Models/RunnerTheta.prefab#L429) |
| `M_RunnerThetaSignal.mat` | [RunnerTheta.prefab:69](../Assets/_Project/Art/Models/RunnerTheta.prefab#L69) |
| `M_RunnerDeltaArmor.mat` | [RunnerDelta.prefab:249](../Assets/_Project/Art/Models/RunnerDelta.prefab#L249) |
| `M_RunnerDeltaSuit.mat` | [RunnerDelta.prefab:519](../Assets/_Project/Art/Models/RunnerDelta.prefab#L519) |
| `M_RunnerDeltaMetal.mat` | [RunnerDelta.prefab:339](../Assets/_Project/Art/Models/RunnerDelta.prefab#L339) |
| `M_RunnerDeltaSignal.mat` | [RunnerDelta.prefab:69](../Assets/_Project/Art/Models/RunnerDelta.prefab#L69) |
| `M_RunnerOmegaArmor.mat` | [RunnerOmega.prefab:289](../Assets/_Project/Art/Models/RunnerOmega.prefab#L289) |
| `M_RunnerOmegaSuit.mat` | [RunnerOmega.prefab:1239](../Assets/_Project/Art/Models/RunnerOmega.prefab#L1239) |
| `M_RunnerOmegaMetal.mat` | [RunnerOmega.prefab:1149](../Assets/_Project/Art/Models/RunnerOmega.prefab#L1149) |
| `M_RunnerOmegaSignal.mat` | [RunnerOmega.prefab:109](../Assets/_Project/Art/Models/RunnerOmega.prefab#L109) |

20개를 **지금 삭제하면** 위 프리팹의 재질 참조가 끊긴다. B를 선택해도 프리팹 교체·저장 후 GUID 참조 0을 재확인하고 삭제한다.

20개와 별개인 관련 자산도 다음과 같이 남아 있다. 아래 경로는 프로젝트 루트 기준이며, 삭제 목록에 자동 포함하지 않는다.

| 관련 경로 목록 | 현재 참조 |
|---|---|
| `Assets/_Project/Art/Models/RunnerAlpha.prefab` | [Data/RunnerProfile_Alpha.asset:20](../Assets/_Project/Data/RunnerProfile_Alpha.asset#L20) |
| `Assets/_Project/Art/Models/RunnerBeta.prefab` | [Data/RunnerProfile_Beta.asset:20](../Assets/_Project/Data/RunnerProfile_Beta.asset#L20) |
| `Assets/_Project/Art/Models/RunnerTheta.prefab` | [Data/RunnerProfile_Theta.asset:20](../Assets/_Project/Data/RunnerProfile_Theta.asset#L20) |
| `Assets/_Project/Art/Models/RunnerDelta.prefab` | [Data/RunnerProfile_Delta.asset:20](../Assets/_Project/Data/RunnerProfile_Delta.asset#L20) |
| `Assets/_Project/Art/Models/RunnerOmega.prefab` | [Data/RunnerProfile_Omega.asset:20](../Assets/_Project/Data/RunnerProfile_Omega.asset#L20) |
| `Assets/_Project/Art/Models/RunnerAlphaGeometry.asset` | [RunnerAlpha.prefab:43](../Assets/_Project/Art/Models/RunnerAlpha.prefab#L43)을 포함해 총 25곳 |
| `Assets/_Project/Art/Models/RunnerBetaGeometry.asset` | [RunnerBeta.prefab:43](../Assets/_Project/Art/Models/RunnerBeta.prefab#L43)을 포함해 총 25곳 |
| `Assets/_Project/Art/Models/RunnerThetaGeometry.asset` | [RunnerTheta.prefab:43](../Assets/_Project/Art/Models/RunnerTheta.prefab#L43)을 포함해 총 25곳 |
| `Assets/_Project/Art/Models/RunnerDeltaGeometry.asset` | [RunnerDelta.prefab:43](../Assets/_Project/Art/Models/RunnerDelta.prefab#L43)을 포함해 총 25곳 |
| `Assets/_Project/Art/Models/RunnerOmegaGeometry.asset` | [RunnerOmega.prefab:83](../Assets/_Project/Art/Models/RunnerOmega.prefab#L83)을 포함해 총 25곳 |
| `Assets/_Project/Art/Models/RunnerChamfer.asset` | [Prefabs/ArmoredRunner.prefab:44](../Assets/_Project/Prefabs/ArmoredRunner.prefab#L44)를 포함해 총 25곳 |
| `Assets/_Project/Prefabs/ArmoredRunner.prefab` | 앞서 적은 두 씬의 GUID 참조와 [ChainRushSceneBuilder.cs:21](../Assets/_Project/Scripts/Editor/ChainRush/ChainRushSceneBuilder.cs#L21), [ChainRushProfileTests.cs:27](../Assets/_Project/Scripts/Tests/PlayMode/ChainRushProfileTests.cs#L27)의 경로 참조 |
| `Assets/_Project/Art/Materials/M_RunnerArmor.mat` | [ArmoredRunner.prefab:91](../Assets/_Project/Prefabs/ArmoredRunner.prefab#L91) 등 4곳 |
| `Assets/_Project/Art/Materials/M_RunnerTrim.mat` | 최초 조사 시 `Assets` GUID 검색에서 참조 0건. 삭제는 별도 승인 대상 |

프로필의 `Look`을 읽는 코드에는 [RaceAssembler.cs:141](../Assets/_Project/Scripts/Runtime/ChainRush/Flow/RaceAssembler.cs#L141), [LobbyMenu.cs:133](../Assets/_Project/Scripts/Runtime/ChainRush/Flow/LobbyMenu.cs#L133), [RunnerPreview.cs:48](../Assets/_Project/Scripts/Runtime/ChainRush/Flow/RunnerPreview.cs#L48)이 있다. 기존 프리팹 에셋을 유지하고 내부만 교체하는 추천안은 이 참조 경로를 유지한다.

### 봇전 4명 드로 배치 측정 절차 — 제안

1. 교체 전 먼저 STADIUM·PALM COAST에서 플레이어 1명+봇 3명, 같은 시드·라인업·카메라·해상도·품질·조명 조건을 기록한다. 같은 에디터 버전으로 전후를 비교한다.
2. 4명이 화면에 들어오는 출발 대기 프레임을 기본 비교점으로 삼고, 달리기·스윙·부스트 구간을 추가 측정한다.
3. Game 뷰 Stats의 Batches·SetPass·삼각형·정점과 CPU/GPU 프레임 시간을 기록한다. Frame Debugger에서 러너별 본체·발광·바이저·그림자 패스를 구분한다.
4. 교체 후 동일 조건을 반복한다. 활성 스킨 렌더러가 4명 합계 4개인지, 얼굴 중복 표시나 상대 월드의 도로 중복 렌더가 없는지도 확인한다.
5. 닫힌 얼굴과 열린 얼굴을 각각 측정한다. “렌더러 1개·슬롯 3개”를 실제 배치 수로 치환하지 않고 측정값을 보고한다.
6. 기록은 전체 비용과 러너 귀속 비용을 나눠 전후 값·증감으로 남긴다. 측정을 위해 ProjectSettings나 트랙을 바꾸지 않는다.

상대 월드의 불필요한 표시를 끄는 기존 지점은 [RaceAssembler.cs:371](../Assets/_Project/Scripts/Runtime/ChainRush/Flow/RaceAssembler.cs#L371)이며, 봇전에서는 기록 고스트를 만들지 않는다. [같은 파일:410](../Assets/_Project/Scripts/Runtime/ChainRush/Flow/RaceAssembler.cs#L410)

## 3. 위험

| 위험 | 근거·대응 |
|---|---|
| FBX 보정 회전 소실로 모델이 눕거나 앞방향이 틀어짐 | `RunnerTilt`는 매 프레임 `body.rotation`을 덮어쓴다. [RunnerTilt.cs:52](../Assets/_Project/Scripts/Runtime/ChainRush/Visuals/RunnerTilt.cs#L52). 시각용 부모와 임포트 자식을 분리하고 실제 축을 검증한다. |
| 새 바닥 피벗과 현재 캡슐 중심의 불일치 | 새 원점은 발 사이 바닥이지만, 빌더의 캡슐은 높이 1.8·center 0이다. [아트 요청서:37](ART_REQUEST_RUNNER.md#L37), [ChainRushSceneBuilder.cs:129](../Assets/_Project/Scripts/Editor/ChainRush/ChainRushSceneBuilder.cs#L129). 물리 루트를 옮기지 않고 시각용 오프셋으로 지면을 맞춘다. |
| Animator 평가와 줄 갱신이 한 프레임 어긋남 | 줄은 `ropeOrigin.position`을 읽는다. [GrappleController.cs:88](../Assets/_Project/Scripts/Runtime/ChainRush/GrappleController.cs#L88). 평가 순서와 손·줄 일치를 실측한다. |
| 뼈 배열을 잘못 연결해 찌그러짐·원본 프리팹 참조 발생 | 현 `RunnerLook`은 스킨 바인딩을 다루지 않는다. [RunnerLook.cs:21](../Assets/_Project/Scripts/Runtime/ChainRush/Visuals/RunnerLook.cs#L21). 뼈 대응·bind pose·슬롯을 적용 전에 검증한다. |
| 복귀 때 숨긴 얼굴까지 켜짐 | `RunnerTilt`는 초기 `enabled` 렌더러를 모아 다시 켠다. [RunnerTilt.cs:35](../Assets/_Project/Scripts/Runtime/ChainRush/Visuals/RunnerTilt.cs#L35), [같은 파일:68](../Assets/_Project/Scripts/Runtime/ChainRush/Visuals/RunnerTilt.cs#L68). 얼굴 선택 후 캐시하고 복귀·비활성화까지 시험한다. |
| 봇 상대에게 충돌 관측 연결이 누락됨 | 상대 자신의 `RaceAssembler.Field`는 null이다. [ChainRushRaceFieldTests.cs:167](../Assets/_Project/Scripts/Tests/PlayMode/ChainRushRaceFieldTests.cs#L167). 주 필드를 각 러너에 조립 단계에서 명시적으로 연결한다. |
| 접촉 관측 추가가 주행에 역으로 영향 | 필드의 순서는 입력→이동→접촉→판정이다. [RaceField.cs:155](../Assets/_Project/Scripts/Runtime/ChainRush/Race/RaceField.cs#L155). 표시용 캐시만 추가하고 시뮬은 이를 읽지 않게 한다. 기존 접촉·결정성 검증을 유지한다. |
| 클립 보간에서 접지·장식 관통 발생 | 원본 접지 검증에는 키 프레임만 잰 기록이 있다. [아트 요청서:203](ART_REQUEST_RUNNER.md#L203). Unity 재생 구간 전체를 확인한다. |
| 원본 재질 수와 낡은 문서 상한의 충돌 | 최신 닫힌 얼굴은 3슬롯이다. [아트 요청서:218](ART_REQUEST_RUNNER.md#L218). 3/2슬롯을 이번 승인에 명시하고 실제 비용을 측정한다. |
| 고스트·프리뷰가 Animator를 계속 재생하거나 숨긴 얼굴까지 처리함 | 복제·전체 렌더러 순회는 [GhostRunner.cs:41](../Assets/_Project/Scripts/Runtime/ChainRush/Visuals/GhostRunner.cs#L41), [RunnerPreview.cs:142](../Assets/_Project/Scripts/Runtime/ChainRush/Flow/RunnerPreview.cs#L142). 포즈 정지·얼굴 선택을 검증한다. |
| 공유 작업 트리에서 조사 후 코드·줄 번호가 달라짐 | 아트 요청서 자체도 이전 `dev` 기준임을 명시한다. [아트 요청서:159](ART_REQUEST_RUNNER.md#L159). 구현 직전에 승인 범위와 현재 파일을 재대조한다. |

모든 새 실행 경로에서 `Update`·`LateUpdate` 안의 `GetComponent`·`Find`·`new`·LINQ·문자열 결합을 금지한다. 참조 조회·할당은 조립·초기화 단계로 옮긴다.

## 4. 확인 못 한 것

- **Unity 검증 안 됨:** 이번에는 Unity MCP·배치·에디터 조작이 금지되어 컴파일·임포트·테스트·렌더·배치 측정을 실행하지 않았다.
- Unity에서의 실제 루트 회전·스케일, `bakeAxisConversion` 적용 결과와 최종 보정값.
- Generic 리그 임포트 후의 뼈 경로·bind pose·클립 이름, `RunnerRig|` 접두사의 표시 방식. FBX 안의 접두사와 8/9개 AnimStack은 확인했다.
- 키 사이 보간·전환 블렌딩의 접지, 메시 bounds에 따른 잘림, 모든 프레임의 장식 관통 여부.
- URP에서 바이저의 광택·Coat 표현, 최종 Smoothness 값. 원본 재질 숫자만으로 화면 일치를 판정하지 않았다.
- Animator를 해당 프레임 줄 갱신 전에 평가시키는 구체적인 갱신 방식과 중복 평가 방지 방법. 승인 후 실제 에디터에서 검증할 구현 세부 사항이다.
- 왼쪽 봉에서 오른쪽 원본 포즈를 사용하는 모습과 `PoleSwingEdge`의 사용자 수용 여부.
- 새 `RunnerVisual`·스킨 교체 계약·접촉 관측값의 실행 결과. 모두 아직 구현하지 않은 제안이다.
- 반영 뒤 실제 드로 배치·CPU/GPU 비용과 개선 폭.
- 다른 작업이 이후 바꾸는 파일·참조 상태. 삭제 전에는 GUID 검색을 다시 수행해야 한다.
