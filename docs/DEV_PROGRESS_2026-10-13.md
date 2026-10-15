# HideAndShoot 개발 진행 기록 (2026-10-13)

기준: `docs/숨바꼭질_게임_통합기획기술사양서.md` v1.5, Part 01–10. 이번 기록은 Play 실검증 전 소스 작업이다.

## 이번 구현
- `HostShotHistory`: 30Hz×32 과거 위치 기록을 새 라운드 때 비우고 현재 플레이어·NPC 캡슐을 재등록. 발사/렌더 시각 음수와 이력 기간 제한 보완.
- `MatchAvatarBootstrap` / `HostRoundSetupCoordinator`: 9개 후보 중 고유 위치로 매 라운드 재배치. 중도 입장자는 다음 라운드에 네트워크 아바타 생성.
- `PistolCombatController`: 조준 해제 RPC, 발사 시간 허용 범위 확인, 새 라운드에서만 5발 초기화, 승리자의 이동 허용을 별도 NetworkVariable로 분리.
- `AmmoSpawnDirector`: 사망·라운드 불일치·비활성 플레이어의 획득 거부. 10발 소지자 비소비는 여전히 임시 정책.
- `NpcPoolCoordinator`: NPC 생성 실패 시 부분 생성분 정리, 누락 컴포넌트에 대한 생존 수 조회 보호.
- `HostDeathResolver`: 이탈로 1명만 남은 경우 Host 승자 판단, 점수 중복 방지, Redo 확정 시 1초 판정 취소.
- `HostRoundSetupCoordinator`: 정상 승리 5초 연출 이후 다음 라운드 준비 호출, 3승 시 Podium 단계로 전이하는 기반. 실제 시상대 화면/로비 복귀는 미연결.
- `RelayLobbySession`: 접속자 전원(다음 라운드 대기자 포함)의 Redo 투표 전송·Host 중복 차단·투표 수 브로드캐스트. 임시 ESC 메뉴 및 x/N 오버레이.
- `PlayerLocalMotor` / `PistolCombatController`: ESC 투표 메뉴 사용 중 이동·조준 입력 억제.

## 검증 결과
- 실제 Unity 프로젝트 `C:\UnityProject\HideAndShoot` 소스 수정.
- Unity 6.3.18f1의 Bee 응답 파일을 Temp 전용 DLL 출력으로 복사해 Roslyn 실행: 최종 Runtime EXIT=0, Editor EXIT=0.
- `Assets/HideAndShoot/Resources/NetworkPrefabCatalog.asset` 및 `Animations/AC_HumanoidShared.controller` 파일 존재 확인. 카탈로그의 3개 프리팹 GUID 참조도 텍스트상 존재.
- 기존 Unity Editor PID 6720 유지. Editor 종료/재시작, Play Mode, Relay 다중 접속 QA 미실행.
- 오프라인 컴파일 통과는 실시간 Unity 씬 로드·프리팹 부착·게임플레이 통과를 의미하지 않음.

## 미완료 및 위험
- 1초 사망 전 발사 판정에 쓰는 클라이언트 fireTick은 서버 이력으로 제한하지만 신뢰 가능한 발사 시각의 완전한 증명은 아님. B안 지연/위조 정밀 QA 필요.
- 마지막 두 명, NPC 시체·풀링, 탄약 보급, 중도입장, 2~8인 렌더링·네트워크는 아직 게임 내 검증 전.
- 고스트 1인칭 지면 이동, 자기 사망 카메라, 패배자 승자 3인칭 추적, WIN/HUD, 승인 디자인, 3승 시상대·로비 복귀는 미완성.
- IMGUI 기반 ESC 메뉴는 기능 확인용 임시 표시이며 Part08 승인 UI를 대체하지 않음.
- 이탈 직후 전원 찬성 투표와 단독 생존 승리가 동시에 발생하는 우선순위, 참가자 1인일 때 다음 라운드 처리 등은 추가 설계/검증 필요.

## 다음 구현 단위
1. Host 상태 브로드캐스트, HUD/유령 관전·사망/승자 카메라.
2. 3승 시상대, 종료 후 로비 복귀, 라운드 결과·투표 통합 전이.
3. Unity Editor 종료 없이 필요한 에셋·Prefab 정적 확인 후 기능 완성 시 종합 2–8인 QA.

- Static prefab GUID links: PlayerAvatar motor/combat/life, NPCActor brain, AmmoPickup trigger all matched metadata. Not gameplay-tested.
## 2026-10-13 추가 — Stage 10~11 점수판·시상대 소스 구현
- **대응 Part:** P01-03/04/08/10/12, P06 라운드·카메라, P08-04/05/06/07, P10-05/07. 통합 사양서 v1.5 준수.
- `RelayLobbySession`: Host 점수 스냅샷 메시지와 매치 시상대 메시지, 연결 중인 참가자 이름/점수/입장 순서/마지막 득점 순서/생존·라운드 참가 상태 동기화. 점수 >0 동률 최신 득점 우선, 0점 동률 선입장 우선.
- `HostDeathResolver`: 점수 부여 시 Host 순번 증가, 라운드 참가자·연결 상태를 반영한 생존 집계, 사망 승인 즉시 생존 수 스냅샷 전송.
- `HostRoundSetupCoordinator`: 5초 카운트다운 잔여 시간 스냅샷, 3승이면 5초 맵 승리 연출 이후 시상대, 시상대 약 8초(임시값) 뒤 `MenuLobbyScene` 로비 복귀. 이탈로 1인만 연결된 경우 승리 축하 5초 뒤 로비 복귀.
- 신규 `GameHUDPresenter.cs`: Host 스냅샷에 기반한 좌상단 방코드/라운드/점수·상단 생존 수·우상단 재시작 투표·우하단 탄약·ADS 중 조준점·사망 관전/다음 라운드 대기·WIN 표시. Unity UI Canvas·한글 OS 폰트 기반 초기 UI, 디자인 검증 대기.
- 신규 `MatchPodiumPresenter.cs`: GameScene 내부 전용 카메라와 Robot Kyle 전신 모델, 1·2·3위 단상, 2인 매치 3위 모델 공석, 시상대 이름·점수. 별도 ResultScene 추가 없음.
- `PlayerPrefabInstaller`: 기존 공유 AnimatorController에 `Victory` 상태를 중복 없이 추가하는 Editor Installer. 실제 `AC_HumanoidShared.controller` 파일에서 Victory 확인.
- `LocalSpectatorRig`: 관전 CharacterController가 NPC/플레이어를 물리적으로 밀지 않도록 동적 배우 Collider 충돌 무시 갱신, 기존 OnGUI 중복 문구 제거.

### 검증 범위
- Unity 6.3 원래 Bee 빌드 응답파일에서 별도 Temp DLL로 Roslyn 컴파일: Runtime EXIT=0, Editor EXIT=0.
- `Assets/HideAndShoot/QA/STAGE10_11_StaticAudit_2026-10-13.txt`: 소스와 자산 구조 정적 검사 17/17 PASS. Play 테스트로 간주하지 않음.
- Editor가 C# 스크립트 메타를 생성했고 공용 Animator의 Victory 상태도 파일에 존재. Unity Editor PID 6720 유지, 종료·재시작 또는 Play 모드 미수행.
- 실제 3D 카메라/라이트/캐릭터 포즈, HUD 반응성, NGO 패킷 순서, 2~8인 동작은 아직 미검증.

### 미해결
- `FirstPersonArmsInstaller`가 `No FP arm triangles found: Player prefab NOT saved` 로그를 출력. `PlayerAvatar.prefab`에 FPS 팔 노드가 없고 `FPSKyle_Arms_*.asset`도 없음. 별도 메시/본 구조 점검이 필요.
- 승인된 HUD 와이어프레임의 PNG는 현재 `docs/assets/인게임_HUD_와이어프레임.png`에서 확인되지 않음. 사양서의 기능/배치 기반 HUD를 우선 구성, 정확한 픽셀·디자인 검수 미완료.
- 마지막 두 명의 사망 전 발사 시각 검증과 RTT 보정은 보안/레이턴시 QA 미완료. 3승 시상대 표시 시간 8초는 [임시]값.


## Stage 0–11 누락 보완 완료 및 Stage 12로 인계 (2026-10-13)

- 사용자 요청에 따라 실제 Unity Play·멀티/성능 검증 없이, Tracker에 표시되었던 구체적인 구현 누락을 우선 처리했다.
- 기존 FP 팔 실패 원인은 `Robot Kyle.fbx.meta`의 `isReadable: 0`. 이를 1로 변경하고 `FirstPersonArmsInstaller`에 ModelImporter Read/Write 보호 로직을 추가했다.
- Unity Editor가 별도 `Assets/HideAndShoot/Animations/FPSKyle_Arms_0.asset`을 실제 생성했으며 `PlayerAvatar.prefab`의 `FPS_KyleArmsSkinned` 직렬화도 확인했다.
- `RemoteMotionAnimator`가 PlayerAvatar/NPCActor 모두에 직렬화됨을 GUID로 확인했다. 소유자 입력/Host NPC 애니메이션은 기존대로 두고, 원격 프록시는 네트워크 Transform 이동 변화에서 걷기 속도를 계산한다.
- `PistolCombatController`는 Host 승인 조준 상태를 `SyncRemoteAimRpc`로 비소유자에 전달한다.
- `LateJoinSpectatorRig`를 `GameSceneRuntimeConfigurator`에 연결하여 현재 라운드에 캐릭터가 없는 중도 입장자가 지면 기반 로컬 관전 후 다음 라운드에 참여하도록 했다.
- `HostRoundSetupCoordinator`/`MatchAvatarBootstrap`: 신규 라운드는 연결된 참가자만 반영, 만장일치 Redo 이후 잔류자 1명일 때 점수 없이 로비로 복귀하도록 보완했다.
- `NpcPoolCoordinator`: 최초 생성 시 이미 배치된 NPC/참가자 캡슐 중첩 방지. 이동 중 강제 분산은 도입하지 않았다.
- `RelayLobbySession`: 늦은 NGO 씬 동기화 이후에도 Host Redo 투표 수신이 가능하도록 1초 명부 갱신 시 투표 집계를 재전송한다.
- `Assets/HideAndShoot/QA/STAGE12_IMPLEMENTATION_HANDOFF_2026-10-13.txt`: 코드·직렬화·자산 정적 검사 30/30 PASS. 실제 QA 합격과 동일하지 않다.
- `docs/STAGE12_INTEGRATION_QA_HANDOFF.md`: QA 시나리오 및 검증 유보 항목 인계 완료.
- Unity Editor 재시작·종료 없음. 오프라인 Roslyn Runtime/Editor 컴파일 오류 0.

### 현재 상태
Stage 12로 이관했지만 실제 2~8인 Unity Play, FPS 팔 시각 검증, HUD 와이어프레임 일치, 위조 발사 시각의 허용오차·레이턴시 튜닝, 8명/NPC64 성능·충돌 검증은 시작하지 않았다. 사양서 [추후 확정] 항목도 임의로 확정하지 않았다.


## First multiplayer playtest bugfix (2026-10-13)
- Nine reported issues addressed in source/assets. See docs/PLAYTEST_FIRST_ROUND_FIXES_2026-10-13.md; Runtime and Editor offline Roslyn compile EXIT=0. Play retest pending.

## FPS right-arm and animation redo (2026-10-13)
- Right arm 1362-triangle mesh generated and PlayerAvatar prefab link verified; Mixamo 7 idle/walk/run/aim loops reimported, 5.3s full-body fire clip suppressed, pistol attached at runtime to RightHand and viewport repositioned. See docs/FPS_RIGHT_ARM_ANIMATION_REPAIR_2026-10-13.md. Native Play QA pending.

## Second multiplayer playtest 6-issue fixes (2026-10-13)
- Lobby code/copy alignment, 5s ammo spawn visible pickup+host contact audit, shared FPS/World pistols, ADS hide arm/43-degree zoom, laser bullet/crosshair/larger Host history hit capsule, NPC countdown movement lock. See docs/PLAYTEST_SECOND_ROUND_FIXES_2026-10-13.md. Static 12/12 + offline C# compile pass; native Play and NGO hash editor verification pending.
