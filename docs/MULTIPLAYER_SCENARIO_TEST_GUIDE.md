# HideAndShoot 멀티플레이 시나리오 테스트 가이드

`Unity 6000.3.18f1` / `Multiplayer Play Mode 1.6.2` / NGO + Relay. 실제 실행 검증 전 구성 안내.

## 준비된 시나리오

- `Assets/Settings/PlayMode/HideAndShoot_3Players_PlayModeScenario.asset`: 메인 Editor 1개 + Additional Editor 2개. **첫 테스트 권장**.
- `Assets/Settings/PlayMode/HideAndShoot_4Players_PlayModeScenario.asset`: 메인 Editor 1개 + Additional Editor 3개. 4인/NPC 32개 부하 점검용.
- 기본 `NewPlayModeScenario.asset`은 보존. 기본 시나리오에는 추가 Editor가 0개이며 불필요한 원격 인스턴스가 들어 있어 위 신규 시나리오를 선택한다.

## 실행

1. Unity에서 **Window > Multiplayer > Play Mode Scenarios**를 열거나 Play 버튼 옆 시나리오 드롭다운을 연다.
2. `HideAndShoot_3Players_PlayModeScenario` 선택 (권장). 각 인스턴스의 시작 씬은 `MenuLobbyScene`으로 지정해 두었다.
3. 시나리오 플레이를 시작하여 Main Editor와 Additional Editor 2개가 실행되는지 확인한다.
4. 각 창에서 서로 다른 닉네임을 입력한다. Main Editor에서 **방 만들기**를 눌러 Unity Relay 참가 코드를 받는다.
5. 각 Additional Editor에서 **방 참가**를 선택하여 동일 코드를 입력한다. Relay 룸 명부가 3인인지 확인한다.
6. Host 창에서 **PLAY**를 누른다. 모든 창이 `GameScene`으로 이동하고 5초 카운트다운 뒤 각자 FPS로 움직이는지 확인한다.
7. 첫 성공 후 `HideAndShoot_4Players_PlayModeScenario`로 확장한다. Additional Editor 포커스 단축키는 Windows 기준 **Ctrl+F9 ~ Ctrl+F12**다.

## 주의

- 시나리오는 추가 Editor 인스턴스를 시작할 뿐 **Relay 방 생성·자동 입장은 하지 않는다.** 각 창에서 닉네임/방 코드를 입력해야 한다.
- Relay 서비스 접근을 위해 프로젝트의 Unity Services 로그인·네트워크 연결이 필요하다. MPP Editor 인스턴스 각각은 플레이어로 실행된다.
- Unity Editor 종료·재시작 및 Play 테스트는 이 문서 작성 과정에서 실시하지 않았다. 에셋/시나리오 직렬화만 확인했다.
- 5~8인 테스트에는 MPP의 여러 Editor만으로 충분하지 않을 수 있어 로컬 Windows 플레이어 빌드나 별도 PC를 추가해야 한다.
- Unity Play 창/씬·FPS 팔·총기·관전·동시 사망·Redo·시상대가 실제 정상인지 여부는 Stage 12 QA 대상이다.
- Unity Play 시작에 실패하면 Console에서 오류 메시지와 해당 instance 이름을 먼저 확인한다. 반복적으로 Editor를 종료/재시작하지 않는다.

## Stage 12 시작 시 첫 통과 조건

3개 창에 Main Host와 Client2명이 별도 닉네임으로 표시되고, Host PLAY 이후 GameScene이 전원 열리고, 각자의 이동·NPC 걷기·탄약/승패 상태를 관찰할 수 있어야 한다.

## 2026-10-13 ?? ?? ? ?? ??

?? `NewPlayModeScenario.asset`? ??? ???? ????. ?? Multiplay Hosting ????? ??? **?? ????**??? ?? Relay ???? ???? ? ??.

?? ?? YAML ???? ?? `HideAndShoot_3Players_PlayModeScenario.asset`? `HideAndShoot_4Players_PlayModeScenario.asset`? Unity ?? ??? ???? ???. Unity `AssetDatabase.CreateAsset`? ?? `HideAndShoot_Local3Players.asset` / `HideAndShoot_Local4Players.asset`? ??? `Assets/HideAndShoot/Editor/LocalScenarioAssetInstaller.cs`? ????.

Unity?? `Assets > Refresh`? ? ???? ??? ?? ? `Window > Multiplayer > Play Mode Scenarios` ?? ??? ?? ?? `HideAndShoot_Local3Players`? ????. ?? ???? ??? Play? ???? ?? `Tools > HideAndShoot > Repair Local Multiplayer Scenarios`? ? ? ????.

??? ??? ???? ??? ?? ???? ???? Editor? Play ??? ?? ? ? 3? ??? ????? ????. ?? ?? ??? `docs/SCENARIO_ORIGINAL_REMOTE_BACKUP_2026-10-13.txt`? ????.

**?? ??:** ?? ?? ? ??? ?? Roslyn ??? ??. Unity? ? ????? ????? ?? ????? ??? ?????? ?? ???? ???.
