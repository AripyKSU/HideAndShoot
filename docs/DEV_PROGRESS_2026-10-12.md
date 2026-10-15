# HideAndShoot 개발 진행 기록 (2026-10-12)

기준: docs/숨바꼭질_게임_통합기획기술사양서.md, v1.5 ZIP 원문 (SHA-256: d24b2fe4b4efae0fdae05bf8d53e19480bcfb5636fbfaf4798b76b31b9674473).

## 실동작 증거: 이전 검증 재사용, 이번 작업은 Play 검증 생략
- UGS Relay Host 생성/6자 코드/NGO StartHost PASS
- 2인 Client 접속/명부 Count=2, 실측 RTT 약 121–133 ms
- Host Play → Host SceneLoaded=GameScene, Client SceneLoaded=GameScene
- Host 종료 → Client SceneLoaded=MenuLobbyScene; 수정본 Client 로그 NullReferenceException 0
- QA 테스트를 위한 헤드리스 Client 종료, Unity Editor PID 33156 유지

## 코드/정적 개발 (Unity 플레이 테스트 하지 않음)
- Stage 3: RelayLobbySession의 참가자 조회·매치 상태·씬 변경 이벤트.
- Stage 4: SpawnPointRegistry (9개 고유 스폰 중복 없는 Host 선택), MatchAvatarBootstrap (NGO 씬 로드 완료 후 초기 아바타 Spawn), NetworkPrefabCatalog(실사용 프리팹 등록)와 GameplaySceneInstaller.
  Static QA: 72m 맵/9개 스폰/16개 픽업 후보/옥상2/램프2/기존 NavMesh 베이크 표식 PASS.
- Stage 5: PlayerLocalMotor (owner FPS WASD/Shift/점프없음, 마우스 시점/감도1–10), Kyle MoveSpeed BlendTree 편집기 스크립트, FP 팔 트라이앵글 추출(별도 스킨드 메시) 코드.
- Stage 6: PistolCombatController 로컬 ADS(0.2s 임시), 단발/RMB release edge, 빈탄 클릭, 중앙 raycast, Host 탄약 NetworkVariable 및 RPC 기본 자격확인.
  **아직 Host 승인 사망/히스토리/가림 판정을 연결하지 않아 캐릭터가 사망하지 않음.**
- Stage 7 일부: NpcWanderBrain (Host 걷기/정지/회전, 현재 구역 밀도+최근 방문 가중치, death/generation/round 정리).
  **NPC 16–64기 풀/Client용 NGO InstanceHandler/탄약 5초 Host Spawn은 구현되지 않음.**

## 검증 정확한 범위
- C# Unity Roslyn 컴파일 응답 파일을 재사용해 Runtime 및 Editor 스크립트를 별도 Temp DLL로 컴파일: **둘 다 Exit 0**. 원래 Unity Editor 라이브 컴파일/Prefab 에셋 생성과 동일한 검증은 아님.
- Unity Editor는 한 번도 종료하지 않았음. 새 소스 편집 후 Editor가 백그라운드라 Script refresh가 지연됨.
- 신규 설정 Installer들은 Editor가 스크립트를 리프레시하면 실행되도록 작성됨. **Resources/NetworkPrefabCatalog.asset, GameScene 스폰 서비스, AC_HumanoidShared.controller, FP Kyle arm Mesh 및 프리팹 부착 여부는 아직 확인 못했음.** 이 항목을 완료로 표시하지 않는다.
- 실시간 2인 플레이/사격/NPC/탄약은 기능이 상당 부분 완성된 후 통합 Play 테스트에서 진행.

## 다음 구현
1. Editor 자동 Refresh 이후 생성 에셋/스크립트 부착 정적 확인(Play 필요 없음).
2. Host NPC 16–64 풀 + Client NGO prefab pool handler, Ammo 5초 스폰 및 +5 획득, 나중에 RoundActive 연결.
3. Host 위치 히스토리·ray/capsule 검증 및 승인된 사망, 라운드/매치/관전/HUD.
4. 이후 통합 2–8인 실제 검증.

