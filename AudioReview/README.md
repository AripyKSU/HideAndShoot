# HideAndShoot audio refresh — 2026-10-14

요청한 1~8번을 순서대로 적용했습니다. 모든 Unity 에디터/에셋 작업은 공식 unity CLI와 열린 C:/UnityProject/HideAndShoot 에디터에서 수행했습니다.

## 들어보기

- [효과음 모음 — 게임 재생 볼륨](SFX_Preview.wav): 총소리 → UI → 카운트다운 → 호루라기 → 로봇 발소리 → 탄약 획득 → 정산 환호.
- [게임 앰비언트 20초 — 게임 재생 볼륨](Ambience_Preview.wav)
- [로비 BGM 20초 — 게임 재생 볼륨](LobbyMusic_Preview.wav)
- 효과음별 시작 시각은 preview-index.json에 있습니다.
- 이 폴더는 Unity Assets 밖에 있으며 미리듣기 파일과 제작 원본을 보관합니다.

## 적용

| 항목 | 게임 에셋 (Resources/Audio) | 재생 |
| --- | --- | --- |
| 총소리 | Pistol.wav (0.48초) | PlayerAvatar 프리팹 pistolShot. PlayerPrefabInstaller 자동 재설치도 새 총소리를 사용. AudioSource volume 0.38, 거리 1.5~32m, 로그 감쇠. 고음 감쇠와 피크 조정. |
| UI | UI.wav (0.20초) | TacticalButtonFeedback의 실제 onClick. 버튼 이벤트를 재설정하는 UI에서도 다시 연결. |
| 카운트다운 | Countdown.wav (0.32초), Whistle.wav (0.58초) | 5~1 숫자 변경당 작은 신호음, Countdown→Active 전환에 호루라기. 중간 입장 시 불필요한 시작 신호 없음. |
| 앰비언트 | Ambience.wav (227초) | GameScene, volume 0.18. 정산 때 페이드 아웃. 3초 양끝 크로스페이드. |
| 로비 BGM | LobbyMusic.wav (121초) | MenuLobbyScene, volume 0.16. 씬 전환 시 페이드. 2초 양끝 크로스페이드. |
| 로봇 이동 | RobotStep.wav (0.23초) | 본인만, CharacterController 실제 지면 이동거리 기준. 걷기 보폭 0.95m, 달리기 1.45m. 정지·잠금·비접지에서는 무음. |
| 탄약 획득 | Reload.wav (0.70초) | 본인의 서버 승인 탄약 증가이며 RoundActive인 경우에만 재생. |
| 정산 환호 | Cheer.wav (2.85초) | MatchPodiumPresenter가 정산 오버레이를 표시할 때 한 번. 로비 복귀 시 종료. |

긴 배경음은 Unity Streaming/Vorbis quality 0.65로 가져옵니다. 런타임에 GameAudio_LOCAL이 로컬 AudioSource들을 만들고 Resources의 파일을 불러옵니다. 씬 파일을 새로 굽거나 저장하지 않았습니다.

## 제작 및 출처

7개 효과음은 기존 설치된 Agent Audio CLI의 Stable Audio 3 Medium, TFLite CPU 런타임으로 새로 생성했습니다. 입력 원본은 Sources/*_Agent.wav입니다. 원본의 짧은 이벤트를 잘라 음량·고음·페이드를 조정한 뒤 연결했습니다. 모델/런타임을 재설치하거나 다른 유료 생성 서비스를 사용하지 않았습니다.

- Agent Audio: https://github.com/AIEGOBOT/agent-audio
- 모델: Stability AI Stable Audio 3 Medium. 모델 관련 조건은 Agent Audio의 THIRD_PARTY_NOTICES.md와 해당 모델 라이선스를 참고하세요.
- 로비 음악: Cheerful Arcade Theme — JorisVermeer (AI-generated 표기), https://pixabay.com/music/upbeat-cheerful-arcade-theme-425491/
- 도시 앰비언트: City-Above-Far-Ambience-Car — CliffordJohnson, https://pixabay.com/sound-effects/city-city-above-far-ambience-car-479129/
- Pixabay Content License: https://pixabay.com/service/license-summary/
- Pixabay 원본 MP3는 Sources/LobbyMusic_Pixabay.mp3와 Sources/Ambience_Pixabay.mp3에 보관합니다.

## 확인한 범위

- Unity 컴파일 오류 없음.
- 9개 WAV의 길이·샘플레이트·채널·유한 샘플·무음 여부·피크를 확인. 소스 피크는 0.32~0.70이고 클리핑 없음. asset-checks.json 참조.
- 짧은 플레이 모드 1회. 프레임 진행과 로비 음악 재생, 전체 리소스 로드, 실제 UI 버튼 이벤트의 재연결 후 클릭음, 효과음 재생 확인.
- 플레이 중 임시 상태/지면 프로브로 카운트다운 전환과 시작 호루라기, 앰비언트 시작, 환호, 걷기/달리기, 이동 잠금/정지 무음을 확인. 임시 상태는 복원하고 프로브는 제거. play-checks.json 참조.
- 전체 Relay 멀티플레이 경기는 실행하지 않았으며, 사람이 직접 들은 음색 평가를 대신하지 않습니다. 이번 검증은 파일 분석과 Unity 재생 상태 확인입니다.

