# HideAndShoot — 1회차 플레이테스트 9개 결함 수정 내역 (2026-10-13)

기준: 통합 기획·기술 사양서 v1.5, `IMPLEMENTATION_TRACKER.md`, 사용자가 실제 1회차 멀티 플레이에서 직접 발견한 9개 결함. 이 문서는 **소스 수정 및 별도 컴파일 기록**이다. Unity Play/화면 검증 통과 기록이 아니다.

| 번호 | 실험에서 확인된 문제 | 수정한 코드·리소스 |
|---|---|---|
| 1 | 없는 방 참가 시 연결 실패까지 너무 오래 걸림 | `RelayLobbySession.cs`: 전체 참가 시도를 7초로 제한, Relay 코드 조회에도 Task.WhenAny timeout, 늦은 응답은 재접속 못 하도록 시도 식별 |
| 2 | 1920×1080 방 코드 복사 버튼이 다음 줄로 밀리고 Ping과 겹침 | `MenuLobbyUI.cs`: 1줄 고정 방 코드 헤더·복사 버튼, 폰트 20, Ping/이름 행 위치 분리 |
| 3 | 나가기 버튼 글씨가 '방'만 보임 | 복제 버튼 내부 Text의 RectTransform을 버튼 전체에 stretch, 폰트·줄바꿈 보정 |
| 4 | 손/팔을 FPS처럼 오른쪽 아래에 배치 | `PlayerLocalMotor.cs`: FPSArmsRig 어깨 방향 x+.37/y-.24/z+.14; 권총 별도 오른쪽 아래 배치 |
| 5 | 걷기 모션이 올바르게 적용되지 않음 | FPS 팔 애니메이터에도 MoveSpeed/Aim/Fire 연결, 원격 플레이어는 owner MoveSpeed를 Host 경유 비신뢰성 RPC로 동기화하면서 위치 변화 fallback 유지, 사망·NPC 풀 재사용에서 Locomotion 상태 복구 |
| 6 | 총 모델 없음 | Quaternius Pistol_1 FBX와 muted-grey URP 머티리얼을 `Assets/HideAndShoot/Resources/`에 실제 추가, `PlayerLocalMotor` Awake 런타임 모델 생성, `FirstPersonPistolInstaller.cs` 별도 프리팹 직렬화 설치기로 추가 |
| 7 | 사망 후 관전 진입 시 캐릭터가 남아 있음 | `HostPlayerLife.cs`: 사망 애니메이션 1.5초 뒤 월드 모델 Renderer hide, 전 클라이언트 관찰자 적용, 다음 라운드 재표시 |
| 8 | 내 FPS 팔이 다른 플레이어에게 보임 | `ProjectSettings/TagManager.asset`: FPSViewModel 전용 레이어12, 소유자 외 FPSRig 비활성/렌더러 비활성, 사망·늦은 참가 관전 카메라에서 해당 레이어 제외 |
| 9 | 오래된 Round1/Alive/Redo/Ammo HUD, 빈 패널 등이 겹침 | 프로토타입 `GameHUDCanvas`를 GameScene 로드시 비활성·제거. 초기씬 생성기는 프로토타입을 더 이상 만들지 않음. `GameHUDPresenter`는 스코어/생존/탄약/관전/결과/카운트다운만 상태별 표시하고 Text 자식뿐 아니라 **배경 Image 부모까지** 숨김. Redo 집계는 ESC 메뉴 한 곳에만 노출 |

## 별도 검증 결과

- 오프라인 Roslyn C# Runtime/Editor 컴파일: 둘 다 EXIT=0.
- `Assets/HideAndShoot/QA/PLAYTEST_FIRST_ROUND_STATIC_2026-10-13.txt`: 소스/에셋 연결 여부 9/9 확인. 실제 렌더링·접속·동작을 통과했다는 뜻이 아니다.
- 기존 Unity Editor를 종료하거나 재시작하지 않았고 이번 수정 후 새 Play 세션은 실행하지 않았다.
- `PlayerAvatar.prefab`에 FPS 팔 렌더 메시가 이미 있는 것은 확인했지만, **신규 FPS 권총 설치기의 Unity 에디터 실행 및 프리팹 저장은 확인되지 않았다**. 따라서 Runtime Resources fallback도 추가했다.
- 현재 실행 중인 Editor가 신규 FBX/머티리얼과 C# 소스를 임포트했는지 추가 확인이 필요하다. 재시험 전에 컴파일/Asset Refresh를 확인한다.

## 재시험 인수 기준 (1920×1080, 최소 Host+Client1)

1. 없는 코드 참가가 약 7초 내에 실패하며 정상 메뉴로 돌아온다.
2. 방코드/복사/Ping 서로 겹치지 않고 나가기 버튼에 '방 나가기'가 온전히 나온다.
3. 움직이는 두 플레이어의 보행이 서로 보이고, FPS 팔·총은 소유자 화면 우하단에만 보인다.
4. 죽은 사람은 1.5초 뒤 월드 모델이 숨고, 관전 카메라로 바뀐다. 다음 라운드에서는 캐릭터가 복원된다.
5. HUD는 한 벌만 있으며 라운드 상태에 맞지 않는 텍스트/빈 배경이 없다. 재시작 투표는 ESC에서만 보여야 한다.

재시험 실패 시 스크린샷과 해당 인스턴스 Console 로그로 정확한 레이아웃·카메라/총 각도·애니메이션 오프셋을 조정한다. 이런 시각·동시성 결과는 **아직 합격으로 표시하지 않는다**.
