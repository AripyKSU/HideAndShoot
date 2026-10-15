# HideAndShoot FPS 오른팔·권총 및 걷기 애니메이션 재구성 (2026-10-13)

사용자가 1회차 재시험에서 발견: 권총이 여전히 안 보임, 걷기 애니메이션이 안 나옴, 사격 시 장전처럼 보이는 엉뚱한 모션, 1인칭에 양팔이 다 보임. 이전 '애니메이션 연결 완료'는 실제 게임에서 재현되지 않아 무효.

## 실제 원인(에디터 진단 근거)
- 이전 `FPSKyle_Arms_0.asset`은 스킨 메시 삼각형이 **2,724개**이며 양팔을 남겨 둔 상태였다.
- `PlayerAvatar.prefab`의 `FPS_QuaterniusPistol` 렌더러는 활성이고 FPSViewModel 레이어 12였으나, 모델은 1인칭 카메라 하위 고정 `PistolViewModel_TO_BUILD`에 있고 **오른손 뼈에 붙어 있지 않았다**. 렌더러 존재만으로 화면 표시를 보장할 수 없는 구조였다.
- `AC_HumanoidShared`는 `Locomotion_Forward_Walk`(0.8초), `Locomotion_Forward_Run`(0.5초) 사용, FBX의 반복 설정이 명시되지 않았다.
- `Fire_Handgun`은 5.3초짜리 전신 클립이었다. 기존에는 총알 발사 1회에 이 클립을 재생했고 사격 중 보행을 가릴 수 있었다.
- 전신 `Aim_Idle` 상태 전환은 조준하며 이동할 때 발 동작을 멈추는 구조였다.

## 변경
- `FirstPersonRightArmMesh.cs`: 스킨 바인딩의 `HumanBodyBones.RightUpperArm` 및 하위 오른팔 뼈에 가중치가 있는 삼각형만 통과시키는 런타임 세이프티. 왼팔을 선택하지 않는다.
- `FirstPersonRightArmInstaller.cs`: 실제 Unity Editor에서 **1362개 삼각형**의 `Assets/HideAndShoot/Animations/FPS_RightArm_0.asset` 생성 및 `PlayerAvatar.prefab` 링크 확인. 원래 양팔 에셋과 월드 모델은 별도로 보존.
- `PlayerLocalMotor.cs`: 소유자의 1P Animator는 짧은 `Aim` 파지 자세 한 프레임에 정지. 캐릭터 보행 애니메이션을 1P 팔에 혼용하지 않으며 보행 시 작은 bob, 사격 시 짧은 반동. `FPS_QuaterniusPistol`의 마운트를 `RightHand` 뼈에 연결하며 손 위치를 소유자 카메라 뷰포트 오른쪽 아래(hip x .74/y .31, ADS x .58/y .41)에 매 프레임 정렬한다.
- `PistolCombatController.cs`: 5.3초 `Fire_Handgun` 전신 trigger 경로 제거, 보행을 중단시키던 전신 `Aim` 상태 진입 억제. 서버의 ADS 타이밍 검증/단발 사격/탄약·피해 규칙은 유지.
- `HumanoidClipLoopInstaller.cs`: 기존 Mixamo FBX **7개**(대기/걷기/뛰기/후진/좌우 스트레이프/조준)의 loopTime/loopPose와 root lock 설정을 Unity 에디터에서 재임포트. **실제 에디터 감사 파일 7개 UPDATED 확인.** Death/Fire 클립은 반복하지 않는다.
- 사망 및 NPC 풀 초기화 시 비활성화된 Animator.Play() 호출을 방지.
- `FpsRigDiagnosis.cs`: 메시·손뼈·모델/머티리얼·Live BlendTree 연결을 추가 점검할 수 있는 에디터 진단 도구.

## 현재 검증 결과

| 검증 | 상태 |
|---|---|
| 별도 Roslyn Runtime 빌드 | 통과 |
| 별도 Roslyn Editor 빌드 | 통과 |
| 정적 코드 경로 체크 | 11/11 통과, `QA/FPS_RIGHT_HAND_STATIC_2026-10-13.txt` |
| Unity가 실제로 오른팔 전용 Mesh 에셋 생성 | **확인** (`FPS_RightArm_0.asset`, 1362 triangles) |
| Unity가 실제 PlayerAvatar.prefab에서 오른팔 전용 Mesh GUID 참조 | **확인** |
| Unity FBX 클립 루프 재임포트 | **확인** (`QA/ANIMATION_LOOP_IMPORT_AUDIT.txt`, 7 UPDATED, 걷기 meta `loopTime: 1`) |
| Unity Play에서 총이 보이는지/손에 정확히 잡히는지 | **미검증** |
| Unity Play에서 실제 월드 보행·NPC·3인 동기화가 정상인지 | **미검증** |

## 다음 재시험 인수 기준
1. Editor가 C# 임포트를 마친 후 Host + Client로 게임을 실행한다.
2. 자기 FPS에는 **오른팔 하나와 권총 하나**만 보여야 하며, 총은 오른손에 붙어 오른쪽 아래를 따라야 한다. 다른 플레이어가 자신의 FPS용 팔/총을 볼 수 없어야 한다.
3. 조준(RMB)과 권총 발사(LMB)를 해도 **장전처럼 보이는 5.3초 전신 동작이 재생되지 않는다**. 짧은 반동 후 정상 자세로 돌아온다.
4. 다른 클라이언트에서 내 3인칭 걷기/달리기, 조준한 채 걷기, NPC 보행이 모두 **끊어지지 않고 반복**되어야 한다.
5. 1920×1080에서 오른팔/총의 크기·시야 점유율·조준 정렬을 확인한다. 별도 이미지/Play 검증 없이 정확한 FPS 화면 퀄리티를 보장하지 않는다.

### 제한
UI나 실제 Play를 자동으로 검증하지 않았으므로, 소스가 통과했다는 이유로 버그가 '완전히 해결'되었다고 기록하지 않는다. 기존 Unity Editor는 종료하거나 재시작하지 않았다.
