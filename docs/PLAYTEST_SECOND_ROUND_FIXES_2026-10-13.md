# HideAndShoot — 두 번째 플레이테스트 6개 결함 대응 (2026-10-13)

사용자가 보고한 문제: 방 코드 복사 버튼의 수직 어긋남, 탄약 상자 미스폰, 1인칭·다른 캐릭터 권총 미표시, 조준 중 팔 모양 이상, 탄환의 시각 효과 및 판정 부족, NPC가 5초 카운트다운 중 먼저 걷는 현상.

## 원인과 수정

| 문제 | 확인된 원인 | 수정 |
|---|---|---|
| 방 코드·복사 줄 높이 | 기존 헤더 Y=205, 복사 버튼 Y=142 | 인원 수 표시와 코드 줄을 분리. **코드 Text·복사 버튼을 모두 중심 Y=142**로 배치해 재사용 |
| 탄약 생성·획득 | 기존 로그는 RoundActive 시작만 기록하고 SpawnOne 성공 및 실제 표시를 확인할 근거가 없었음. 원래 NGO 프리팹 3종의 직렬화 해시가 모두 같아 네트워크 생성 식별 충돌도 의심됨 | Host 기준 시간으로 5초 첫 스폰/이후 5초 간격 유지, NavMesh 바닥 높이+0.55m에 표시, 성공 시 net ID·위치 기록, 빛·부유 애니메이션, 호스트 접촉 중복 검사/근접 검사. NGO OnValidate로 네트워크 프리팹 고유 해시 재계산/검증하는 설치기 추가 |
| 1인칭 및 월드 권총 | 권총 FBX는 있지만 1인칭 마운트가 리그 손뼈에 따라 화면 밖에 놓이거나 월드 후보 총이 숨겨질 수 있음 | `SharedPistolVisual`로 실제 `PistolViewModel.fbx`/머티리얼 사용. 본인 무기는 카메라 우하단 고정·크기 보정. 월드 플레이어·NPC 무기는 각 아바타 `RightHand`에 매번 보이도록 별도 생성 |
| 조준 시 팔 | FPS 오른팔 고정 파지 애니메이션이 Zoom 때 어색함 | ADS 구간에서는 **1인칭 오른팔 렌더러만 숨김**. 권총은 유지. 줌 FOV 75→43, 전방 카메라 이동 0.16m |
| 피격·탄환 연출 | 기존 히트스캔 방식은 발사 궤적을 렌더링하지 않음. 작은 크로스헤어와 피격 캡슐 | `PistolLaserTracer`: 0.22초간 밝은 레이저 볼트를 화면/네트워크 관전자에게 표시. 크로스헤어 72pt, 100×100 + Outline. 플레이어·NPC 피격 캡슐 반지름 0.33→0.43, HostShotHistory 허용 오차 0.15→0.18 |
| 카운트다운 NPC 움직임 | `NpcWanderBrain.ResetForSpawn`에서 곧바로 NavMesh 이동 시작 | 초기 생성 시 이동 잠금, 모든 NPC는 5초간 정지. Host가 `HostPhase.Active` 전환 후 풀의 NPC 이동 해제. Result에서 재잠금 |

## 변경 파일(주요)
- `Scripts/Lobby/MenuLobbyUI.cs`
- `Scripts/World/AmmoSpawnDirector.cs`, `AmmoPickupActor.cs`
- `Scripts/Player/SharedPistolVisual.cs`, `PlayerLocalMotor.cs`
- `Scripts/NPC/NpcWanderBrain.cs`, `NpcPoolCoordinator.cs`
- `Scripts/World/HostRoundSetupCoordinator.cs`
- `Scripts/Combat/PistolCombatController.cs`, `PistolLaserTracer.cs`
- `Scripts/Networking/HostShotHistory.cs`
- `Scripts/UI/GameHUDPresenter.cs`
- `Prefabs/PlayerAvatar.prefab`, `NPCActor.prefab`
- `Editor/NetworkPrefabHashRepair.cs` 및 `GunAmmoPrefabAudit.cs`

## 검증

- `Assets/HideAndShoot/QA/PLAYTEST_SECOND_ROUND_STATIC_2026-10-13.txt` — 수정 소스 경로 12/12 검사 PASS.
- Runtime/Editor 별도 Roslyn 컴파일 — EXIT=0.
- **Unity 플레이 검증 미실시**. 실제 권총 화면/탄약 NPC 동기화/레이저 보임은 플레이에서 검증 필요.
- 원본 PlayModeScenario 변경 없음; 기존 Unity Editor를 종료하거나 재시작하지 않았음.

### 재시험 순서
1. Unity에서 Play 종료 후 **Assets → Refresh**, Console C# 에러가 없는지 확인. `QA/NGO_PREFAB_HASH_VERIFY.txt`가 생성되고 `UNIQUE: True`인지 확인. 아니라면 `Tools > HideAndShoot > Verify Unique NGO Prefab IDs` 실행.
2. 호스트+클라이언트 각각 로비 코드 텍스트와 복사 버튼 Y 일치 확인.
3. 카운트다운 5초 동안 플레이어와 NPC 모두 정지하고 종료 즉시 NPC 보행 시작 확인.
4. 시작 후 5초 시점에 첫 탄약 생성 확인. Console `[HideAndShoot/Ammo] SPAWN success` 기록과 실제 아이템 표시·획득 확인. 이후 간격 5초, 동시 최대 참가자 수.
5. 내 화면 우하단 권총 하나, 월드 아바타/모든 NPC 손의 권총, ADS 시 오른팔만 숨고 카메라 줌/큰 크로스헤어 표시 확인.
6. 허용된 사격 한 발에 짧은 레이저 궤적 표시·Host 명중 판정 확인. 벽 뒤 타격은 계속 불가.

Native Unity 해시 복구 실행 및 실제 호스트/클라이언트 연동 결과는 현재 미확인 상태이며, 이를 실제 플레이 성공으로 표시하지 않는다.
