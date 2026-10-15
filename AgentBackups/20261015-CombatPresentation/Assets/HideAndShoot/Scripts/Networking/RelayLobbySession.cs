using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HideAndShoot.Lobby;
using HideAndShoot.World;
using HideAndShoot.Combat;
using UnityEngine.InputSystem;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HideAndShoot.Networking
{
    /// <summary>
    /// Persistent NGO host/client over Unity Relay.
    /// This service owns lobby connections, not round spawns or the game rules.
    /// Player avatar spawning is deferred until RoundSetup (10-07).
    /// </summary>
    public sealed class RelayLobbySession : MonoBehaviour
    {
        public const int MaxPlayers = 8;

        // Persistent room state for subsequent round/spawn systems. Host is authoritative.
        public static RelayLobbySession Instance { get; private set; }
        public bool IsMatchInProgress => inMatch;
        public bool RoundMenuVisible => roundMenuVisible;
        public bool IsRoomHost => manager != null && manager.IsHost;
        public bool IsConnected => manager != null && manager.IsListening;
        public int ParticipantCount => joinOrder.Count;
        public ulong[] GetParticipantIds() => joinOrder.ToArray();
        public string GetParticipantNickname(ulong clientId) =>
            names.TryGetValue(clientId, out var value) ? value : string.Empty;
        public event Action<bool> MatchSceneChanged;
        public event Action<ulong,int,bool,int> RoundWinnerAnnounced;
        public event Action<ScoreRow[]> MatchPodiumAnnounced;

        public readonly struct ScoreRow
        {
            public readonly ulong ClientId;
            public readonly string Nickname;
            public readonly int Score;
            public readonly long LastScoredOrder;
            public readonly int JoinedOrder;
            public readonly bool Participating;
            public readonly bool Alive;
            public ScoreRow(ulong id,string nickname,int score,long scoredOrder,
                int joinedOrder,bool participating,bool alive)
            {
                ClientId=id; Nickname=nickname; Score=score;
                LastScoredOrder=scoredOrder; JoinedOrder=joinedOrder;
                Participating=participating; Alive=alive;
            }
        }

        private readonly List<ScoreRow> standings=new List<ScoreRow>(MaxPlayers);
        public IReadOnlyList<ScoreRow> Standings=>standings;
        public int MatchRoundId { get; private set; }
        public int MatchAliveCount { get; private set; }
        public int MatchPhase { get; private set; }
        public string RoomCode=>displayCode;
        public int RedoYesCount=>redoStatusYes;
        public int RedoEligibleCount=>redoStatusEligible;
        public int CountdownRemaining {get; private set;}
        public int MatchStateRevision {get; private set;}
        public string GetRankedNickname(ulong clientId)
        {
            if(names.TryGetValue(clientId,out var name))return name;
            foreach(var row in standings)
                if(row.ClientId==clientId)return row.Nickname;
            return string.Empty;
        }
        private const string RosterMessage = "HideAndShoot.Roster.v1";
        private const string RedoVoteMessage = "HideAndShoot.RedoVote.v1";
        private const string RedoStateMessage = "HideAndShoot.RedoState.v1";
        private const string WinnerMessage = "HideAndShoot.RoundWinner.v1";
        private const string MatchStateMessage = "HideAndShoot.MatchState.v1";
        private const string PodiumMessage = "HideAndShoot.MatchPodium.v1";
        private bool roundMenuVisible;
        private int redoStatusRound=-1;
        private int redoStatusYes;
        private int redoStatusEligible;
        private const float JoinTimeoutSeconds = 7f;

        private readonly Dictionary<ulong, string> names = new Dictionary<ulong, string>();
        private readonly List<ulong> joinOrder = new List<ulong>();

        private NetworkManager manager;
        private UnityTransport transport;
        private MenuLobbyUI menu;
        private bool busy;
        private bool isJoining;
        private bool closing;
        private bool inMatch;
        private bool listenersRegistered;
        private float joinStartedAt;
        private int joinAttempt;
        private float nextRosterUpdate;
        private string displayCode = "";
        private string localName = "";
        private int lastRosterCount = -1;
        private PooledNetworkInstanceHandler npcClientPool;
        private PooledNetworkInstanceHandler ammoClientPool;
        private GameObject registeredNpcPrefab;
        private GameObject registeredAmmoPrefab;
        private PublicRoomDirectory directory;
        private bool refreshingRooms;
        private static Task authenticationTask;


        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (FindFirstObjectByType<RelayLobbySession>() != null) return;
            var root = new GameObject("HideAndShoot_Session");
            DontDestroyOnLoad(root);
            root.AddComponent<RelayLobbySession>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            directory=GetComponent<PublicRoomDirectory>()??gameObject.AddComponent<PublicRoomDirectory>();

            manager = FindFirstObjectByType<NetworkManager>();
            if (manager == null)
            {
                var root = new GameObject("NGO_NetworkManager");
                DontDestroyOnLoad(root);
                transport = root.AddComponent<UnityTransport>();
                manager = root.AddComponent<NetworkManager>();
            }
            else
            {
                transport = manager.GetComponent<UnityTransport>();
                if (transport == null)
                    transport = manager.gameObject.AddComponent<UnityTransport>();
                DontDestroyOnLoad(manager.gameObject);
            }

            if (manager.NetworkConfig == null)
                manager.NetworkConfig = new NetworkConfig();
            manager.NetworkConfig.NetworkTransport = transport;
            manager.NetworkConfig.ConnectionApproval = true;
            manager.NetworkConfig.EnableSceneManagement = true;
            manager.NetworkConfig.PlayerPrefab = null; // Lobby participants have no PlayerAvatar yet.
            manager.NetworkConfig.AutoSpawnPlayerPrefabClientSide = false;
            // Every peer must register matching NetworkObjects before NGO StartHost/StartClient.
            var catalog = Resources.Load<NetworkPrefabCatalog>("NetworkPrefabCatalog");
            if (catalog != null)
            {
                if (catalog.PlayerAvatar != null) manager.AddNetworkPrefab(catalog.PlayerAvatar);
                if (catalog.NPCActor != null)
                {
                    manager.AddNetworkPrefab(catalog.NPCActor);
                    registeredNpcPrefab = catalog.NPCActor;
                    npcClientPool = new PooledNetworkInstanceHandler(catalog.NPCActor);
                    manager.PrefabHandler.AddHandler(catalog.NPCActor, npcClientPool);
                }
                if (catalog.AmmoPickup != null)
                {
                    manager.AddNetworkPrefab(catalog.AmmoPickup);
                    registeredAmmoPrefab = catalog.AmmoPickup;
                    ammoClientPool = new PooledNetworkInstanceHandler(catalog.AmmoPickup);
                    manager.PrefabHandler.AddHandler(catalog.AmmoPickup, ammoClientPool);
                }
            }
            else Debug.LogError("[HideAndShoot] NetworkPrefabCatalog missing. Run gameplay scene installer.");

            manager.ConnectionApprovalCallback = ApproveConnection;
            manager.OnClientConnectedCallback += OnClientConnected;
            manager.OnClientDisconnectCallback += OnClientDisconnected;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void Start()
        {
            BindMenuIfAvailable();
            if (Application.isEditor) return;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != "-hs-join-code") continue;
                string code = args[i + 1].Trim();
                if (code.Length > 0 && menu != null)
                {
                    Debug.Log("[HideAndShoot/QA] Standalone CLI join requested.");
                    JoinRoom(code);
                }
                break;
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            roundMenuVisible=false;
            standings.Clear();
            MatchRoundId=0;
            MatchAliveCount=0;
            MatchPhase=0;
            CountdownRemaining=0;
            MatchStateRevision++;
            if (scene.name == "GameScene")
                inMatch = true;
            else if (scene.name == "MenuLobbyScene")
                inMatch = false;

            Debug.Log("[HideAndShoot/QA] SceneLoaded=" + scene.name +
                " IsClient=" + (manager != null && manager.IsClient) +
                " IsHost=" + (manager != null && manager.IsHost));
            if(scene.name=="GameScene")
                GameSceneRuntimeConfigurator.EnsureConfigured(scene);
            BindMenuIfAvailable();
            if (scene.name == "GameScene" || scene.name == "MenuLobbyScene")
                MatchSceneChanged?.Invoke(inMatch);
        }

        private void BindMenuIfAvailable()
        {
            var next = FindFirstObjectByType<MenuLobbyUI>();
            if (ReferenceEquals(next, menu)) return;
            UnbindMenu();
            menu = next;
            if (menu == null) return;

            menu.CreateRequested += CreateRoom;
            menu.JoinRequested += JoinRoom;
            menu.LeaveRequested += LeaveRoom;
            menu.PlayRequested += HostPlay;
            menu.RefreshRequested += RefreshRooms;

            if (manager.IsListening && !string.IsNullOrEmpty(displayCode))
            {
                menu.DisplayRoom(displayCode, manager.IsHost);
                if (manager.IsHost) PublishRoster();
            }
            else RefreshRooms();
        }

        private void UnbindMenu()
        {
            // Unity destroyed-objects compare equal to null even while the C# reference lives.
            var previous = menu;
            menu = null;
            if (previous == null) return;
            previous.CreateRequested -= CreateRoom;
            previous.JoinRequested -= JoinRoom;
            previous.LeaveRequested -= LeaveRoom;
            previous.PlayRequested -= HostPlay;
            previous.RefreshRequested -= RefreshRooms;
        }

        private void Update()
        {
            if(inMatch && manager!=null && manager.IsClient &&
               SceneManager.GetActiveScene().name=="GameScene" &&
               Keyboard.current!=null && Keyboard.current.escapeKey.wasPressedThisFrame)
                SetRoundMenuOpen(!roundMenuVisible);

            if (isJoining && Time.realtimeSinceStartup - joinStartedAt > JoinTimeoutSeconds)
            {
                isJoining = false;
                EndSession("방 연결 시간이 초과됐습니다. 코드를 확인하고 다시 시도하세요.");
            }

            if (manager != null && manager.IsHost && !closing && Time.unscaledTime >= nextRosterUpdate)
            {
                nextRosterUpdate = Time.unscaledTime + 1f;
                PublishRoster();
                if(inMatch)
                {
                    PublishMatchState();
                    // A late joiner may miss the join-time tally while NGO is
                    // synchronizing GameScene; rebroadcast it at roster cadence.
                    FindFirstObjectByType<HostRoundSetupCoordinator>()?.PublishVoteStatus();
                }
            }
        }

        private void SetRoundMenuOpen(bool open)
        {
            roundMenuVisible=open;
            Cursor.lockState=open?CursorLockMode.None:CursorLockMode.Locked;
            Cursor.visible=open;
        }

        public void ResumeFromRoundMenu()=>SetRoundMenuOpen(false);

        public void VoteFromRoundMenu()
        {
            if(!inMatch || manager==null || !manager.IsClient)return;
            RequestRedoVoteFromLocal();
            SetRoundMenuOpen(false);
        }

        private async void CreateRoom()
        {
            if (!TryStartAction()) return;
            int attempt=++joinAttempt;
            try
            {
                menu.ShowStatus("방을 만드는 중…");
                await EnsureAuthenticatedAsync();
                if(this==null || attempt!=joinAttempt)return;
                var allocation = await RelayService.Instance.CreateAllocationAsync(MaxPlayers - 1);
                string code = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                if(this==null || attempt!=joinAttempt)return;
                transport.SetRelayServerData(HostRelayData(allocation));

                displayCode = code;
                manager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(localName);
                if (!manager.StartHost()) throw new InvalidOperationException("NGO Host 시작에 실패했습니다.");

                RegisterRosterHandler();
                names[manager.LocalClientId] = localName;
                if (!joinOrder.Contains(manager.LocalClientId))
                    joinOrder.Add(manager.LocalClientId);
                await directory.AdvertiseAsync(code);
                if(this==null || attempt!=joinAttempt)return;
                menu.DisplayRoom(displayCode, true);
                PublishRoster();
                Debug.Log("[HideAndShoot] Relay host created, code available in UI.");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EndSession("방을 만들지 못했습니다. 잠시 후 다시 시도하세요.");
            }
            finally
            {
                busy = false;
                if (menu != null) menu.SetBusy(false);
            }
        }

        private async void JoinRoom(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                menu.ShowStatus("방 코드를 입력하세요.");
                return;
            }
            if (!TryStartAction()) return;
            // Bound the entire join (authentication, code lookup, NGO handshake).
            int attempt = ++joinAttempt;
            isJoining = true;
            joinStartedAt = Time.realtimeSinceStartup;

            try
            {
                menu.ShowStatus("방에 참가하는 중…");
                await EnsureAuthenticatedAsync();
                if (!isJoining || attempt != joinAttempt) return;

                var pending = RelayService.Instance.JoinAllocationAsync(code.Trim());
                var completed = await Task.WhenAny(pending,
                    Task.Delay(TimeSpan.FromSeconds(JoinTimeoutSeconds)));
                if (completed != pending)
                {
                    _ = pending.ContinueWith(t => { var ignored = t.Exception; },
                        TaskContinuationOptions.OnlyOnFaulted);
                    throw new TimeoutException("Relay room-code lookup timed out.");
                }
                var joinAllocation = await pending;
                if (!isJoining || attempt != joinAttempt) return;
                transport.SetRelayServerData(ClientRelayData(joinAllocation));
                displayCode = code.Trim().ToUpperInvariant();
                manager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(localName);

                if (!manager.StartClient()) throw new InvalidOperationException("NGO Client 시작에 실패했습니다.");
                RegisterRosterHandler();
                menu.ShowStatus("방에 연결하는 중…");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EndSession("방에 참가하지 못했습니다. 방 코드를 확인하고 다시 시도하세요.");
            }
            finally
            {
                busy = false;
                if (menu != null) menu.SetBusy(isJoining);
            }
        }

        private bool TryStartAction()
        {
            if (busy || manager.IsListening || closing || isJoining || menu == null) return false;
            if (!menu.TryGetNickname(out localName)) return false;
            busy = true;
            menu.SetBusy(true);
            return true;
        }

        private static Task EnsureAuthenticatedAsync()
        {
            if(authenticationTask==null || authenticationTask.IsCompleted)
                authenticationTask=AuthenticateAsync();
            return authenticationTask;
        }

        private static async Task AuthenticateAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        private async void RefreshRooms()
        {
            if(refreshingRooms || menu==null || manager.IsListening)return;
            refreshingRooms=true;
            var target=menu;
            target.SetRefreshing(true);
            try
            {
                await EnsureAuthenticatedAsync();
                var rooms=await PublicRoomDirectory.QueryAsync();
                if(target!=null)target.DisplayRooms(rooms);
            }
            catch(Exception ex)
            {
                Debug.LogWarning("[HideAndShoot/Rooms] Room search failed: "+ex.Message);
                if(target!=null)target.ShowRoomSearchError("방 목록을 불러오지 못했습니다. 새로고침으로 다시 시도하세요.");
            }
            finally
            {
                refreshingRooms=false;
                if(target!=null)target.SetRefreshing(false);
            }
        }

        private static RelayServerData HostRelayData(Allocation allocation)
        {
            var endpoint = allocation.ServerEndpoints.FirstOrDefault(e => e.ConnectionType == "dtls");
            if (endpoint == null) throw new InvalidOperationException("Relay DTLS endpoint is unavailable.");
            return new RelayServerData(endpoint.Host, (ushort)endpoint.Port,
                allocation.AllocationIdBytes, allocation.ConnectionData,
                allocation.ConnectionData, allocation.Key, endpoint.Secure, false);
        }

        private static RelayServerData ClientRelayData(JoinAllocation allocation)
        {
            var endpoint = allocation.ServerEndpoints.FirstOrDefault(e => e.ConnectionType == "dtls");
            if (endpoint == null) throw new InvalidOperationException("Relay DTLS endpoint is unavailable.");
            return new RelayServerData(endpoint.Host, (ushort)endpoint.Port,
                allocation.AllocationIdBytes, allocation.ConnectionData,
                allocation.HostConnectionData, allocation.Key, endpoint.Secure, false);
        }
        private void ApproveConnection(NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            response.CreatePlayerObject = false;
            response.Pending = false;

            // The host's own client is included in the connected list.
            if (manager.ConnectedClientsIds.Count >= MaxPlayers || names.Count >= MaxPlayers)
            {
                response.Approved = false;
                response.Reason = "방이 가득 찼습니다.";
                return;
            }

            string nickname = Encoding.UTF8.GetString(request.Payload ?? Array.Empty<byte>());
            if (!NicknameRules.TryNormalize(nickname, out nickname, out var reason))
            {
                response.Approved = false;
                response.Reason = reason;
                return;
            }

            response.Approved = true;
            names[request.ClientNetworkId] = nickname;
            if (!joinOrder.Contains(request.ClientNetworkId))
                joinOrder.Add(request.ClientNetworkId);
        }

        private void OnClientConnected(ulong id)
        {
            if (manager.IsHost)
            {
                if (id == manager.LocalClientId)
                {
                    names[id] = localName;
                    if (!joinOrder.Contains(id)) joinOrder.Add(id);
                }
                PublishRoster();
                if(inMatch)
                {
                    FindFirstObjectByType<HostRoundSetupCoordinator>()?.PublishVoteStatus();
                    PublishMatchState();
                }
            }
            else if (manager.IsClient && id == manager.LocalClientId)
            {
                isJoining = false;
                if (menu != null) menu.DisplayRoom(displayCode, false);
                if (menu != null) menu.ShowStatus("방에 연결됐습니다.");
            }
        }

        private void OnClientDisconnected(ulong id)
        {
            if (closing) return;
            if (manager.IsHost)
            {
                if(inMatch)
                    FindFirstObjectByType<MatchAvatarBootstrap>()?.RemoveDepartedAvatar(id);
                names.Remove(id);
                joinOrder.Remove(id);
                PublishRoster();
                if(inMatch)
                {
                    FindFirstObjectByType<HostRoundSetupCoordinator>()?.PublishVoteStatus();
                    PublishMatchState();
                }
            }
            else if (manager.IsClient && !manager.IsServer &&
                     (id == manager.LocalClientId || id == NetworkManager.ServerClientId))
            {
                string reason = manager.DisconnectReason;
                EndSession(string.IsNullOrEmpty(reason)
                    ? "방장 연결이 종료되어 메인 메뉴로 돌아왔습니다."
                    : "연결이 종료되었습니다. 다시 시도하세요.");
            }
        }

        private void RegisterRosterHandler()
        {
            if (listenersRegistered || manager.CustomMessagingManager == null) return;
            manager.CustomMessagingManager.RegisterNamedMessageHandler(RosterMessage, OnRosterReceived);
            manager.CustomMessagingManager.RegisterNamedMessageHandler(RedoVoteMessage, OnRedoVoteReceived);
            manager.CustomMessagingManager.RegisterNamedMessageHandler(RedoStateMessage, OnRedoStateReceived);
            manager.CustomMessagingManager.RegisterNamedMessageHandler(WinnerMessage, OnWinnerReceived);
            manager.CustomMessagingManager.RegisterNamedMessageHandler(MatchStateMessage, OnMatchStateReceived);
            manager.CustomMessagingManager.RegisterNamedMessageHandler(PodiumMessage, OnPodiumReceived);
            listenersRegistered = true;
        }

        private void UnregisterRosterHandler()
        {
            if (!listenersRegistered) return;
            if (manager != null && manager.CustomMessagingManager != null)
            {
                manager.CustomMessagingManager.UnregisterNamedMessageHandler(RosterMessage);
                manager.CustomMessagingManager.UnregisterNamedMessageHandler(RedoVoteMessage);
                manager.CustomMessagingManager.UnregisterNamedMessageHandler(RedoStateMessage);
                manager.CustomMessagingManager.UnregisterNamedMessageHandler(WinnerMessage);
                manager.CustomMessagingManager.UnregisterNamedMessageHandler(MatchStateMessage);
                manager.CustomMessagingManager.UnregisterNamedMessageHandler(PodiumMessage);
            }
            listenersRegistered = false;
        }

        /// <summary>All connected room members, including next-round waiters, may vote.</summary>
        public void RequestRedoVoteFromLocal()
        {
            if(manager==null || !manager.IsListening || !inMatch)return;
            // The Host match snapshot works even for a late joiner who does
            // not yet own a playable avatar this round.
            int requestedRound=MatchRoundId;
            if(requestedRound<=0)return;
            if(manager.IsHost)
            {
                FindFirstObjectByType<HostRoundSetupCoordinator>()?
                    .TryVoteRedo(manager.LocalClientId,requestedRound);
                return;
            }
            if(manager.CustomMessagingManager==null)return;
            using(var writer=new FastBufferWriter(32,Allocator.Temp))
            {
                writer.WriteValueSafe(requestedRound);
                manager.CustomMessagingManager.SendNamedMessage(
                    RedoVoteMessage,NetworkManager.ServerClientId,writer);
            }
        }

        private void OnRedoVoteReceived(ulong sender,FastBufferReader reader)
        {
            if(manager==null || !manager.IsHost || !inMatch)return;
            try
            {
                reader.ReadValueSafe(out int round);
                FindFirstObjectByType<HostRoundSetupCoordinator>()?
                    .TryVoteRedo(sender,round);
            }
            catch(Exception ex)
            {
                Debug.LogWarning("[HideAndShoot/Redo] Ignored invalid vote: "+ex.Message);
            }
        }

        public void PublishRoundVoteStatus(int round,int yes,int eligible)
        {
            if(manager==null || !manager.IsHost)return;
            redoStatusRound=round;
            redoStatusYes=yes;
            redoStatusEligible=eligible;
            if(manager.CustomMessagingManager==null)return;
            using(var writer=new FastBufferWriter(32,Allocator.Temp))
            {
                writer.WriteValueSafe(round);
                writer.WriteValueSafe(yes);
                writer.WriteValueSafe(eligible);
                foreach(var id in manager.ConnectedClientsIds)
                    if(id!=manager.LocalClientId)
                        manager.CustomMessagingManager.SendNamedMessage(RedoStateMessage,id,writer);
            }
        }

        private void OnRedoStateReceived(ulong sender,FastBufferReader reader)
        {
            if(manager==null || !manager.IsClient || manager.IsHost ||
               sender!=NetworkManager.ServerClientId)return;
            try
            {
                reader.ReadValueSafe(out int round);
                reader.ReadValueSafe(out int yes);
                reader.ReadValueSafe(out int eligible);
                if(round<0 || eligible<0 || eligible>MaxPlayers ||
                   yes<0 || yes>eligible)return;
                redoStatusRound=round;
                redoStatusYes=yes;
                redoStatusEligible=eligible;
            }
            catch(Exception ex)
            {
                Debug.LogWarning("[HideAndShoot/Redo] Ignored tally: "+ex.Message);
            }
        }

        public void PublishRoundWinner(ulong winner,int score,bool matchOver,int round)
        {
            if(manager==null || !manager.IsHost || !inMatch)return;
            RoundWinnerAnnounced?.Invoke(winner,score,matchOver,round);
            if(manager.CustomMessagingManager==null)return;
            using(var writer=new FastBufferWriter(64,Allocator.Temp))
            {
                writer.WriteValueSafe(winner);
                writer.WriteValueSafe(score);
                writer.WriteValueSafe(matchOver);
                writer.WriteValueSafe(round);
                foreach(var id in manager.ConnectedClientsIds)
                    if(id!=manager.LocalClientId)
                        manager.CustomMessagingManager.SendNamedMessage(WinnerMessage,id,writer);
            }
        }

        private void OnWinnerReceived(ulong sender,FastBufferReader reader)
        {
            if(manager==null || !manager.IsClient || manager.IsHost ||
               sender!=NetworkManager.ServerClientId || !inMatch)return;
            try
            {
                reader.ReadValueSafe(out ulong winner);
                reader.ReadValueSafe(out int score);
                reader.ReadValueSafe(out bool matchOver);
                reader.ReadValueSafe(out int round);
                if(score<1 || score>3 || round<1)return;
                RoundWinnerAnnounced?.Invoke(winner,score,matchOver,round);
            }
            catch(Exception ex)
            {
                Debug.LogWarning("[HideAndShoot/Result] Ignored invalid winner packet: "+ex.Message);
            }
        }

        private ScoreRow[] CaptureHostStandings()
        {
            if(manager==null || !manager.IsServer)return Array.Empty<ScoreRow>();
            var resolver=FindFirstObjectByType<HostDeathResolver>();
            var round=FindFirstObjectByType<HostRoundSetupCoordinator>();
            var lives=FindObjectsByType<HostPlayerLife>(FindObjectsSortMode.None);
            var result=new List<ScoreRow>(MaxPlayers);
            for(int i=0;i<joinOrder.Count;i++)
            {
                ulong id=joinOrder[i];
                if(!names.ContainsKey(id) || !manager.ConnectedClientsIds.Contains(id))
                    continue;
                bool participating=round!=null && round.IsRoundParticipant(id);
                bool alive=false;
                if(participating)
                    foreach(var life in lives)
                        if(life.IsSpawned && life.OwnerClientId==id &&
                           !life.IsDead.Value && life.RoundId==round.RoundId)
                        {
                            alive=true;
                            break;
                        }
                result.Add(new ScoreRow(id,names[id],
                    resolver!=null?resolver.GetWins(id):0,
                    resolver!=null?resolver.GetLastScoreSequence(id):0,
                    i,participating,alive));
            }
            // Score descending; newer positive ties win; zero ties retain lobby order.
            return result.OrderByDescending(row=>row.Score)
                .ThenByDescending(row=>row.Score>0?row.LastScoredOrder:0L)
                .ThenBy(row=>row.JoinedOrder)
                .ThenBy(row=>row.ClientId).ToArray();
        }

        private void WriteScoreRows(ref FastBufferWriter writer,ScoreRow[] rows)
        {
            writer.WriteValueSafe(rows.Length);
            foreach(var row in rows)
            {
                writer.WriteValueSafe(row.ClientId);
                writer.WriteValueSafe(row.Nickname);
                writer.WriteValueSafe(row.Score);
                writer.WriteValueSafe(row.LastScoredOrder);
                writer.WriteValueSafe(row.JoinedOrder);
                writer.WriteValueSafe(row.Participating);
                writer.WriteValueSafe(row.Alive);
            }
        }

        private static ScoreRow[] ReadScoreRows(ref FastBufferReader reader)
        {
            reader.ReadValueSafe(out int count);
            if(count<0 || count>MaxPlayers)throw new InvalidOperationException("Invalid match score count");
            var result=new ScoreRow[count];
            for(int i=0;i<count;i++)
            {
                reader.ReadValueSafe(out ulong id);
                reader.ReadValueSafe(out string name);
                reader.ReadValueSafe(out int score);
                reader.ReadValueSafe(out long scoredOrder);
                reader.ReadValueSafe(out int joinedOrder);
                reader.ReadValueSafe(out bool participating);
                reader.ReadValueSafe(out bool alive);
                if(name==null || name.Length>128 || score<0 || score>3 ||
                   joinedOrder<0 || joinedOrder>=MaxPlayers || scoredOrder<0)
                    throw new InvalidOperationException("Invalid match score row");
                result[i]=new ScoreRow(id,name,score,scoredOrder,joinedOrder,participating,alive);
            }
            return result;
        }

        /// <summary>Periodic Host snapshot, also called immediately on results/roster changes.</summary>
        public void PublishMatchState()
        {
            if(manager==null || !manager.IsHost || !inMatch ||
               SceneManager.GetActiveScene().name!="GameScene")return;
            var round=FindFirstObjectByType<HostRoundSetupCoordinator>();
            ScoreRow[] rows=CaptureHostStandings();
            standings.Clear();
            standings.AddRange(rows);
            MatchStateRevision++;
            MatchRoundId=round!=null?round.RoundId:0;
            MatchPhase=round!=null?(int)round.Phase:0;
            MatchAliveCount=rows.Count(row=>row.Alive);
            CountdownRemaining=round!=null?round.CountdownRemaining:0;
            if(manager.CustomMessagingManager==null)return;
            var writer=new FastBufferWriter(4096,Allocator.Temp);
            try
            {
                writer.WriteValueSafe(MatchRoundId);
                writer.WriteValueSafe(MatchPhase);
                writer.WriteValueSafe(MatchAliveCount);
                writer.WriteValueSafe(CountdownRemaining);
                WriteScoreRows(ref writer,rows);
                foreach(var id in manager.ConnectedClientsIds)
                    if(id!=manager.LocalClientId)
                        manager.CustomMessagingManager.SendNamedMessage(MatchStateMessage,id,writer);
            }
            finally { writer.Dispose(); }
        }

        private void OnMatchStateReceived(ulong sender,FastBufferReader reader)
        {
            if(manager==null || !manager.IsClient || manager.IsHost ||
               !inMatch || sender!=NetworkManager.ServerClientId)return;
            try
            {
                reader.ReadValueSafe(out int round);
                reader.ReadValueSafe(out int phase);
                reader.ReadValueSafe(out int alive);
                reader.ReadValueSafe(out int remaining);
                if(round<0 || alive<0 || alive>MaxPlayers || phase<0 || phase>6 ||
                   remaining<0 || remaining>5)
                    return;
                ScoreRow[] rows=ReadScoreRows(ref reader);
                // Never roll back across rounds due to an older packet.
                if(round<MatchRoundId)return;
                MatchRoundId=round;
                MatchPhase=phase;
                MatchAliveCount=alive;
                CountdownRemaining=remaining;
                standings.Clear();
                standings.AddRange(rows);
                MatchStateRevision++;
            }
            catch(Exception ex)
            {
                Debug.LogWarning("[HideAndShoot/Match] Ignored invalid score snapshot: "+ex.Message);
            }
        }

        /// <summary>Exactly once after reaching three points; still in GameScene.</summary>
        public void PublishMatchPodium()
        {
            if(manager==null || !manager.IsHost || !inMatch)return;
            ScoreRow[] top=CaptureHostStandings().Take(3).ToArray();
            MatchPodiumAnnounced?.Invoke(top);
            if(manager.CustomMessagingManager==null)return;
            var writer=new FastBufferWriter(2048,Allocator.Temp);
            try
            {
                WriteScoreRows(ref writer,top);
                foreach(var id in manager.ConnectedClientsIds)
                    if(id!=manager.LocalClientId)
                        manager.CustomMessagingManager.SendNamedMessage(PodiumMessage,id,writer);
            }
            finally { writer.Dispose(); }
        }

        private void OnPodiumReceived(ulong sender,FastBufferReader reader)
        {
            if(manager==null || !manager.IsClient || manager.IsHost ||
               !inMatch || sender!=NetworkManager.ServerClientId)return;
            try
            {
                ScoreRow[] top=ReadScoreRows(ref reader);
                if(top.Length<1 || top.Length>3 || top[0].Score!=3)return;
                MatchPodiumAnnounced?.Invoke(top);
            }
            catch(Exception ex)
            {
                Debug.LogWarning("[HideAndShoot/Match] Ignored invalid podium: "+ex.Message);
            }
        }

        private void PublishRoster()
        {
            if (!manager.IsHost || string.IsNullOrEmpty(displayCode) || closing) return;
            joinOrder.RemoveAll(id => !names.ContainsKey(id));

            var rows = BuildRosterRows();
            if (menu != null) menu.DisplayRoster(rows.names, rows.pings, rows.hosts, joinOrder.Count, MaxPlayers, true, inMatch);
            if (manager.CustomMessagingManager == null) return;

            using (var writer = new FastBufferWriter(4096, Allocator.Temp))
            {
                writer.WriteValueSafe(displayCode);
                writer.WriteValueSafe(joinOrder.Count);
                foreach (var id in joinOrder)
                {
                    writer.WriteValueSafe(names[id]);
                    writer.WriteValueSafe(id == NetworkManager.ServerClientId);
                    ulong rtt = id == manager.LocalClientId ? 0 : transport.GetCurrentRtt(id);
                    writer.WriteValueSafe(rtt);
                }

                // Send only to remote clients. NGO host renders directly.
                foreach (var id in manager.ConnectedClientsIds)
                    if (id != manager.LocalClientId)
                        manager.CustomMessagingManager.SendNamedMessage(RosterMessage, id, writer);
            }
        }

        private (string[] names, string[] pings, bool[] hosts) BuildRosterRows()
        {
            var left = new List<string>();
            var right = new List<string>();
            var hosts = new List<bool>();
            foreach (ulong id in joinOrder)
            {
                string label = names[id];
                hosts.Add(id == NetworkManager.ServerClientId);
                ulong rtt = id == manager.LocalClientId ? 0 : transport.GetCurrentRtt(id);
                string latency = id == manager.LocalClientId ? "Host" :
                    rtt == 0 ? "-- ms" : rtt + " ms";
                left.Add(label);
                right.Add(latency);
            }
            return (left.ToArray(), right.ToArray(), hosts.ToArray());
        }

        private void OnRosterReceived(ulong sender, FastBufferReader reader)
        {
            if (manager.IsHost || sender != NetworkManager.ServerClientId) return;

            try
            {
                reader.ReadValueSafe(out string code);
                reader.ReadValueSafe(out int count);
                if (count < 1 || count > MaxPlayers) return;
                var left = new string[count];
                var right = new string[count];
                var hosts = new bool[count];
                ulong observedRtt = 0;
                for (int i = 0; i < count; i++)
                {
                    reader.ReadValueSafe(out string name);
                    reader.ReadValueSafe(out bool host);
                    reader.ReadValueSafe(out ulong rtt);
                    if (!host && rtt > 0) observedRtt = rtt;
                    left[i] = name;
                    hosts[i] = host;
                    right[i] = host ? "Host" : rtt == 0 ? "-- ms" : rtt + " ms";
                }

                displayCode = code;
                if (menu != null)
                {
                    if (!menu.IsInRoom) menu.DisplayRoom(code, false);
                    menu.DisplayRoster(left, right, hosts, count, MaxPlayers, false, inMatch);
                    // Per-second roster UI refresh is expected. Avoid log spam.
                    if (count != lastRosterCount)
                        Debug.Log("[HideAndShoot] Client roster synchronized: "+count+" participants.");
                    lastRosterCount = count;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[HideAndShoot] Ignored invalid roster packet: " + ex.Message);
            }
        }

        private void HostPlay()
        {
            if (!manager.IsHost || closing || inMatch || joinOrder.Count < 2 ||
                joinOrder.Count > MaxPlayers)
                return;

            if (manager.SceneManager == null)
            {
                if (menu != null) menu.ShowStatus("게임을 시작할 수 없습니다. 잠시 후 다시 시도하세요.");
                return;
            }

            var status = manager.SceneManager.LoadScene("GameScene", LoadSceneMode.Single);
            if (status != Unity.Netcode.SceneEventProgressStatus.Started)
            {
                if (menu != null) menu.ShowStatus("게임을 시작할 수 없습니다. 다시 시도하세요.");
                return;
            }

            inMatch = true;
            if (menu != null) menu.ShowStatus("게임 씬으로 이동하는 중…");
            PublishRoster();
            Debug.Log("[HideAndShoot] Host Play: NGO GameScene load started.");
        }

        public void HostReturnToLobby()
        {
            if (!manager.IsHost || !manager.IsListening || !inMatch ||
                manager.SceneManager == null) return;
            if (manager.SceneManager.LoadScene("MenuLobbyScene", LoadSceneMode.Single)
                == Unity.Netcode.SceneEventProgressStatus.Started)
            {
                inMatch = false;
                Debug.Log("[HideAndShoot] NGO lobby scene load started.");
            }
        }

        private void LeaveRoom() => EndSession("방에서 나왔습니다.");

        private void EndSession(string message)
        {
            if (closing) return;
            closing = true;
            isJoining = false;
            ++joinAttempt; // invalidate any outstanding Relay request
            busy = false;

            directory?.Close();
            UnregisterRosterHandler();
            if (manager != null && (manager.IsListening || manager.IsClient || manager.IsServer))
                manager.Shutdown();

            names.Clear();
            joinOrder.Clear();
            displayCode = "";
            inMatch = false;
            lastRosterCount = -1;

            if (menu != null) menu.HideRoom(message);
            if (SceneManager.GetActiveScene().name == "GameScene")
                StartCoroutine(ReturnToMenuAfterDisconnect(message));
            closing = false;
        }

        private IEnumerator ReturnToMenuAfterDisconnect(string reason)
        {
            yield return null; // do not load a scene inside NGO's disconnect callback
            if (SceneManager.GetActiveScene().name == "GameScene")
                SceneManager.LoadScene("MenuLobbyScene", LoadSceneMode.Single);
            yield return null;
            if (menu != null) menu.ShowStatus(reason);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            UnbindMenu();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (manager != null)
            {
                manager.OnClientConnectedCallback -= OnClientConnected;
                manager.OnClientDisconnectCallback -= OnClientDisconnected;
                manager.ConnectionApprovalCallback = null;
            }
            UnregisterRosterHandler();
            if (manager != null && manager.PrefabHandler != null)
            {
                if (registeredNpcPrefab != null) manager.PrefabHandler.RemoveHandler(registeredNpcPrefab);
                if (registeredAmmoPrefab != null) manager.PrefabHandler.RemoveHandler(registeredAmmoPrefab);
            }
            npcClientPool?.Dispose();
            ammoClientPool?.Dispose();
        }
    }
}
