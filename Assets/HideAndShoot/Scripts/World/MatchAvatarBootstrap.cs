using System.Collections.Generic;
using System.Linq;
using HideAndShoot.Networking;
using HideAndShoot.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HideAndShoot.World
{
    /// <summary>
    /// Match-scene entry point. Spawns only initial lobby participants after NGO
    /// completes the network scene load. In-progress late joiners wait until next round.
    /// Later the round system will call ResetForNextRound rather than reload the scene.
    /// </summary>
    public sealed class MatchAvatarBootstrap : MonoBehaviour
    {
        [SerializeField] private SpawnPointRegistry spawnPoints;
        private readonly Dictionary<ulong, NetworkObject> activeAvatars = new Dictionary<ulong, NetworkObject>();
        private NetworkManager network;
        private bool initialSpawnsCompleted;
        public bool InitialActorsReady => initialSpawnsCompleted;
        public int AvatarCount => activeAvatars.Count;


        private void Start()
        {
            network = NetworkManager.Singleton;
            if (spawnPoints == null)
                spawnPoints = FindFirstObjectByType<SpawnPointRegistry>();
            if (network == null || !network.IsListening) return;
            if (network.SceneManager != null)
                network.SceneManager.OnLoadEventCompleted += OnLoadEventCompleted;
        }

        private void OnLoadEventCompleted(string sceneName, LoadSceneMode mode,
            List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
        {
            if (sceneName != "GameScene" || network == null || !network.IsHost ||
                initialSpawnsCompleted) return;

            // NGO guarantees clientsCompleted received the scene before avatar spawn.
            foreach (ulong player in network.ConnectedClientsIds)
                if (!clientsCompleted.Contains(player) && player != network.LocalClientId)
                {
                    Debug.LogWarning("[HideAndShoot] Initial avatar spawning deferred: client scene not ready.");
                    return;
                }

            CreateInitialActors();
        }

        private void CreateInitialActors()
        {
            if (initialSpawnsCompleted || spawnPoints == null) return;
            var session = RelayLobbySession.Instance;
            var catalog=Resources.Load<NetworkPrefabCatalog>("NetworkPrefabCatalog");
            if (session == null || catalog == null || catalog.PlayerAvatar == null) return;

            ulong[] ids=session.GetParticipantIds()
                .Where(id=>network.ConnectedClientsIds.Contains(id)).ToArray();
            if(ids.Length<2 || ids.Length>8 ||
               !spawnPoints.TryDrawPlayerSpawns(ids.Length,
                System.Environment.TickCount, out Vector3[] positions,
                out Quaternion[] rotations))
            {
                Debug.LogError("[HideAndShoot] Cannot assign distinct spawn positions for participants.");
                return;
            }

            // Assign all positions before creating any actors: an entry can't get
            // the same anchor as another participant.
            for (int i=0;i<ids.Length;i++)
            {
                if (!network.ConnectedClientsIds.Contains(ids[i])) continue;
                var instance=Instantiate(catalog.PlayerAvatar,positions[i],rotations[i]);
                var net=instance.GetComponent<NetworkObject>();
                if (net==null)
                {
                    Destroy(instance);
                    Debug.LogError("[HideAndShoot] Player prefab lacks NetworkObject");
                    continue;
                }
                net.SpawnAsPlayerObject(ids[i],true);
                var history=FindFirstObjectByType<HostShotHistory>();
                if(history!=null)history.RegisterActor(net,1,0);
                activeAvatars[ids[i]]=net;
            }
            initialSpawnsCompleted = activeAvatars.Count==ids.Length && ids.Length>=2;
            Debug.Log("[HideAndShoot] Initial avatars spawned: "+activeAvatars.Count+
                ". 5-second countdown and round control are handled by later round system.");
        }

        /// <summary>Reassign unique ground spawns and admit late joiners at the next round only.</summary>
        public bool PrepareRoundSpawns(int roundId,IReadOnlyCollection<ulong> admittedParticipants=null)
        {
            if(network==null || !network.IsServer || !initialSpawnsCompleted ||
               spawnPoints==null || roundId<1)return false;
            var session=RelayLobbySession.Instance;
            var catalog=Resources.Load<NetworkPrefabCatalog>("NetworkPrefabCatalog");
            if(session==null || catalog==null || catalog.PlayerAvatar==null)return false;
            var ids=session.GetParticipantIds()
                .Where(id=>network.ConnectedClientsIds.Contains(id) &&
                    (admittedParticipants==null || admittedParticipants.Contains(id))).ToArray();
            if(ids.Length<2 || ids.Length>8 ||
               !spawnPoints.TryDrawPlayerSpawns(ids.Length,
                   unchecked(System.Environment.TickCount+roundId*137),
                   out Vector3[] positions,out Quaternion[] rotations))return false;

            var eligible=new HashSet<ulong>(ids);
            foreach(var stale in activeAvatars.Keys.Where(id=>!eligible.Contains(id)).ToArray())
                activeAvatars.Remove(stale);
            for(int i=0;i<ids.Length;i++)
            {
                ulong id=ids[i];
                if(!network.ConnectedClientsIds.Contains(id))return false;
                if(!TryGetAvatar(id,out NetworkObject avatar) || !avatar.IsSpawned)
                {
                    var go=Instantiate(catalog.PlayerAvatar,positions[i],rotations[i]);
                    avatar=go.GetComponent<NetworkObject>();
                    if(avatar==null)
                    {
                        Destroy(go);
                        Debug.LogError("[HideAndShoot/Round] Missing player NetworkObject.");
                        return false;
                    }
                    avatar.SpawnAsPlayerObject(id,true);
                    activeAvatars[id]=avatar;
                }
                var motor=avatar.GetComponent<PlayerLocalMotor>();
                if(motor!=null)motor.HostAssignRoundSpawn(positions[i],rotations[i],roundId);
                else avatar.transform.SetPositionAndRotation(positions[i],rotations[i]);
            }
            return true;
        }

        public bool TryGetAvatar(ulong clientId, out NetworkObject avatar) =>
            activeAvatars.TryGetValue(clientId,out avatar) && avatar != null;

        /// <summary>
        /// Remove a disconnected player's robot on the host and all clients.
        /// NGO may already have despawned its PlayerObject at this point.
        /// </summary>
        public void RemoveDepartedAvatar(ulong clientId)
        {
            if(network==null || !network.IsServer ||
               !activeAvatars.TryGetValue(clientId,out var avatar))return;
            activeAvatars.Remove(clientId);
            if(avatar==null)return;

            FindFirstObjectByType<HostShotHistory>()?.UnregisterActor(avatar);
            if(avatar.IsSpawned)avatar.Despawn(true);
            else Destroy(avatar.gameObject);
        }

        private void OnDestroy()
        {
            if (network != null && network.SceneManager != null)
                network.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
            activeAvatars.Clear();
        }
    }
}
