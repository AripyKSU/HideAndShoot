using System;
using System.Collections.Generic;
using System.Linq;
using HideAndShoot.UI;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HideAndShoot.World
{
    [Serializable]
    public sealed class StageMapDefinition
    {
        public string Name;
        public GameObject World;
        public Transform Anchors;
        public Color Ambient = new Color(.68f,.72f,.76f);
    }

    /// <summary>One host draw per stage, with an explicit client-ready barrier before spawning.</summary>
    public sealed class StageMapDirector : MonoBehaviour
    {
        private const string SelectionMessage = "HideAndShoot.StageMap.v1";
        private const string ReadyMessage = "HideAndShoot.StageMapReady.v1";
        public const int MapCount = 3;
        [SerializeField] private StageMapDefinition[] maps;
        [SerializeField] private SpawnPointRegistry registry;
        private readonly HashSet<ulong> readyClients = new HashSet<ulong>();
        private readonly System.Random random = new System.Random();
        private NetworkManager network;
        private CustomMessagingManager messaging;
        private float nextBroadcast;
        public int SelectedMapIndex { get; private set; } = -1;
        public int SelectionRound { get; private set; }
        public string SelectedMapName => HasThreeMaps && SelectedMapIndex >= 0 ? maps[SelectedMapIndex].Name : "";
        public bool HasThreeMaps => registry != null && maps != null && maps.Length == MapCount &&
            maps.All(m => m != null && m.World != null && m.Anchors != null);
        public StageMapDefinition[] Definitions => maps;

        public static int DrawMapIndex(System.Random source) => source.Next(MapCount);

        public void Configure(StageMapDefinition[] definitions, SpawnPointRegistry spawnRegistry)
        {
            maps = definitions;
            registry = spawnRegistry;
            ApplyMap(0);
        }

        private void Awake()
        {
            if (HasThreeMaps) ApplyMap(0);
            BindMessages();
        }

        private void BindMessages()
        {
            var current = NetworkManager.Singleton;
            if (current == null || current.CustomMessagingManager == null) return;
            if (messaging == current.CustomMessagingManager) return;
            UnbindMessages();
            network = current;
            messaging = current.CustomMessagingManager;
            messaging.RegisterNamedMessageHandler(SelectionMessage, OnSelection);
            messaging.RegisterNamedMessageHandler(ReadyMessage, OnReady);
        }

        private void Update()
        {
            BindMessages();
            // Replay also repairs missed messages while a late joiner is loading the scene.
            if (network == null || !network.IsServer || SelectionRound < 1 ||
                Time.realtimeSinceStartup < nextBroadcast) return;
            nextBroadcast = Time.realtimeSinceStartup + .75f;
            BroadcastSelection();
        }

        public bool HostChooseForStage(int round)
        {
            BindMessages();
            if (!HasThreeMaps || network == null || !network.IsServer ||
                messaging == null || round <= SelectionRound) return false;
            int index = DrawMapIndex(random); // independent 1/3 draws; repeats are intentional
            readyClients.Clear();
            SelectionRound = round;
            RoundTransitionCurtain.Begin(round);
            if (!ApplyMap(index)) return false;
            readyClients.Add(network.LocalClientId);
            BroadcastSelection();
            Debug.Log("[HideAndShoot/Map] Stage " + round + " selected " + index + ": " + SelectedMapName);
            return true;
        }

        public bool ParticipantsReady(IEnumerable<ulong> participants)
        {
            return network != null && network.IsServer && participants.All(id =>
                !network.ConnectedClientsIds.Contains(id) || readyClients.Contains(id));
        }

        /// <summary>Also used by the editor authoring/audit command. No random draw here.</summary>
        public bool ApplyMap(int index)
        {
            if (!HasThreeMaps || index < 0 || index >= MapCount) return false;
            // Remove the old surface before adding the new surface at the same world origin.
            for (int i = 0; i < maps.Length; i++)
                if (i != index) maps[i].World.SetActive(false);
            maps[index].World.SetActive(true);
            registry.UseSource(maps[index].Anchors);
            RenderSettings.ambientLight = maps[index].Ambient;
            Physics.SyncTransforms();
            SelectedMapIndex = index;
            return registry.PlayerSpawnCount == 9 && registry.AmmoCandidateCount == 16;
        }

        private void BroadcastSelection()
        {
            if (messaging == null || network == null || !network.IsServer) return;
            using (var writer = new FastBufferWriter(16, Allocator.Temp))
            {
                writer.WriteValueSafe(SelectionRound);
                writer.WriteValueSafe(SelectedMapIndex);
                foreach (ulong id in network.ConnectedClientsIds)
                    if (id != network.LocalClientId)
                        messaging.SendNamedMessage(SelectionMessage, id, writer, NetworkDelivery.ReliableSequenced);
            }
        }

        private void OnSelection(ulong sender, FastBufferReader reader)
        {
            if (network == null || network.IsServer || sender != NetworkManager.ServerClientId) return;
            try
            {
                reader.ReadValueSafe(out int round);
                reader.ReadValueSafe(out int index);
                if (round < 1 || round < SelectionRound || index < 0 || index >= MapCount) return;
                if (round == SelectionRound && index != SelectedMapIndex) return;
                if (round > SelectionRound)
                {
                    RoundTransitionCurtain.Begin(round);
                    if (!ApplyMap(index)) return;
                    SelectionRound = round;
                }
                using (var writer = new FastBufferWriter(16, Allocator.Temp))
                {
                    writer.WriteValueSafe(round);
                    writer.WriteValueSafe(index);
                    messaging.SendNamedMessage(ReadyMessage, NetworkManager.ServerClientId,
                        writer, NetworkDelivery.ReliableSequenced);
                }
            }
            catch (Exception ex) { Debug.LogWarning("[HideAndShoot/Map] Invalid map selection: " + ex.Message); }
        }

        private void OnReady(ulong sender, FastBufferReader reader)
        {
            if (network == null || !network.IsServer || !network.ConnectedClientsIds.Contains(sender)) return;
            try
            {
                reader.ReadValueSafe(out int round);
                reader.ReadValueSafe(out int index);
                if (round == SelectionRound && index == SelectedMapIndex) readyClients.Add(sender);
            }
            catch (Exception ex) { Debug.LogWarning("[HideAndShoot/Map] Invalid map ready packet: " + ex.Message); }
        }

        private void UnbindMessages()
        {
            if (messaging == null) return;
            messaging.UnregisterNamedMessageHandler(SelectionMessage);
            messaging.UnregisterNamedMessageHandler(ReadyMessage);
            messaging = null;
        }
        private void OnDestroy() => UnbindMessages();
    }
}
