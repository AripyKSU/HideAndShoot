# HideAndShoot — Unity 6.3 초기 프로젝트 구성

검증일: 2026-10-12

## 확인한 환경
- 프로젝트: C:\UnityProject\HideAndShoot
- Unity Editor: 6000.3.18f1, Universal Render Pipeline 17.3.0
- Unity packages: Netcode for GameObjects 2.7.0, Multiplayer Services 1.2.0, Unity Transport 2.7.3, Multiplayer Play Mode 1.6.2, AI Navigation 2.0.13, Input System 1.19.0
- Packages/packages-lock.json에서 패키지의 실제 resolve 확인

## 리소스 처리
- 사용자 원본 전체 보관: C:\UnityProject\HideAndShoot_AssetArchive
- 선별 런타임 에셋: Assets/HideAndShoot/ThirdParty
- Kenney FBX 584개, Mixamo 애니메이션 FBX 18개, Robot Kyle FBX 1개, Quaternius 권총 FBX 6개, Ammo FBX 1개, 텍스처 및 오디오 WAV 3개
- 임포트 프로필: Assets/HideAndShoot/Editor/HideAndShootAssetImporter.cs
- 원본 ZIP/GLB/OBJ/Blend는 삭제하지 않고 외부 아카이브로 이동

## 실제 생성 및 Unity 로그 검증
- Assets/HideAndShoot/Scenes/MenuLobbyScene.unity
- Assets/HideAndShoot/Scenes/GameScene.unity
- 두 씬 모두 Build Settings 등록; 기존 SampleScene은 보존하되 빌드 제외
- GameScene은 약 72×72m 축소 도시 프로토타입, 중앙 공원, 건물 8개와 옥상 구조 2곳
- 플레이어 스폰 앵커 9곳 및 탄약 후보 16곳
- GameScene의 NavMeshSurface 실제 BuildNavMesh 실행 및 씬 저장 완료
- NavMesh 기록: Assets/HideAndShoot/Scenes/CityNavMeshBake.complete.txt
- 프리팹 4종: Assets/HideAndShoot/Prefabs/RobotKyleWorldVisual.prefab, PlayerAvatar.prefab, NPCActor.prefab, AmmoPickup.prefab
- Robot Kyle 오른손의 권총 소켓 생성, 메탈릭 0.15 재질 적용
- 에디터 스크립트 컴파일 성공, 가장 최근 Unity 기동 로그에서 C# 컴파일 오류 없음

## 아직 구현하거나 실제 플레이 검증하지 않은 항목
- Unity Gaming Services 연결: ProjectSettings/ProjectSettings.asset의 cloudProjectId 및 organizationId가 비어 있음. 실제 Relay 방 생성/참가 테스트 전 UGS 프로젝트를 연결해야 함.
- 메뉴 UI는 기본 배치이며 닉네임 20×20 생성, 빈 이름 금지, 복사/Ping/Host PLAY/Relay 연결은 미구현.
- 네트워크 장면 전환, 플레이어 이동/ADS/사격, 클라이언트 판정 + Host 히트 검증은 미구현.
- NPC AI/풀링, 탄약 5초 주기 생성, 점수·사망·관전·라운드 및 1초 동시 사망 처리는 미구현.
- Kyle/Mixamo 애니메이션 실제 재생 및 스킨드 팔 1인칭 ViewModel 분리는 미구현.
- 권총 손잡이 각도, Kenney 머티리얼·URP 색상, NPC 스폰과 NavMesh 경사로·옥상 연결은 시각/플레이 검증 필요.
- 현재 에디터는 SampleScene을 열어둘 수 있으므로 Project 창에서 MenuLobbyScene 또는 GameScene을 직접 열어 결과 확인.
- Git 초기화 및 최초 커밋은 아직 진행하지 않음.

## 안전 및 다음 단계
- 자동 씬/프리팹 생성 스크립트는 지정 출력 파일이 이미 있으면 덮어쓰지 않도록 구성.
- 기존 원본 아카이브는 프로젝트 외부에 유지.
- 후속 구현은 통합 기획 기술 사양서 Part 01~10의 확정 규칙을 기준으로 진행.
- 다음 단계: 로비 닉네임/Relay 세션 연결 및 2클라이언트 기초 테스트.
## 2026-10-12 사용자 UI/맵 피드백 반영
- 조작 안내 4가지 입력과 목표만 표시, 닉네임 아래 구분선.
- 방 UI 불투명, 배경 입력 숨김, 개발자 전용 문구 제거.
- NW 옥상 남쪽/SE 옥상 북쪽 경사로 수정, 나무12그루/관목6개 추가, 양쪽 NavMesh PathComplete.
- QA 최소화: 단위별 컴파일 + 핵심 동작 1회, 네트워크 종합은 추후.
- Unity Editor를 닫지 않고 변경 반영.
