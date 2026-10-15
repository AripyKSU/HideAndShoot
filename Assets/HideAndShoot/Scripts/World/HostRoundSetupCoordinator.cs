using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HideAndShoot.Combat;
using HideAndShoot.Networking;
using HideAndShoot.NPC;
using Unity.Netcode;
using UnityEngine;

namespace HideAndShoot.World
{
    /// <summary>
    /// Transitional Host RoundSetup -> 5-second countdown -> RoundActive wiring.
    /// Final score/redo/death/ghost/podium logic will own later state transitions.
    /// Must never conclude a round based on client claims.
    /// </summary>
    public sealed class HostRoundSetupCoordinator : MonoBehaviour
    {
        public enum HostPhase { AwaitScene, Setup, Countdown, Active, Result, Podium, Stopped }
        private HostPhase phase = HostPhase.AwaitScene;
        private MatchAvatarBootstrap avatars;
        private NpcPoolCoordinator npcPool;
        private AmmoSpawnDirector ammo;
        private NetworkManager network;
        private int roundId;
        private int participantsAtStart;
        private float countdownEndsAt;
        public int CountdownRemaining=>phase==HostPhase.Countdown
            ?Mathf.CeilToInt(Mathf.Max(0f,countdownEndsAt-Time.realtimeSinceStartup)):0;
        private Coroutine setupCoroutine;
        private Coroutine presentationCoroutine;
        private HostDeathResolver deaths;
        private readonly HashSet<ulong> redoVoters=new HashSet<ulong>();
        private readonly HashSet<ulong> roundParticipants=new HashSet<ulong>();
        [SerializeField] private float podiumDurationSeconds=8f; // provisional: Part06 duration TBD
        public bool IsRoundParticipant(ulong clientId)=>roundParticipants.Contains(clientId);
        public int RedoVoteCount=>redoVoters.Count;

        public HostPhase Phase => phase;
        public int RoundId => roundId;
        public int ParticipantsAtStart => participantsAtStart;

        private void Start()
        {
            network=NetworkManager.Singleton;
            if(network==null || !network.IsServer)return;
            avatars=GetComponent<MatchAvatarBootstrap>();
            npcPool=GetComponent<NpcPoolCoordinator>();
            ammo=GetComponent<AmmoSpawnDirector>();
            deaths=GetComponent<HostDeathResolver>();
            if(deaths!=null)deaths.WinnerCommitted+=OnWinnerCommitted;
            setupCoroutine=StartCoroutine(WaitThenBegin());
        }

        private IEnumerator WaitThenBegin()
        {
            float until=Time.realtimeSinceStartup+30f;
            while(network!=null && network.IsServer && avatars!=null &&
                  !avatars.InitialActorsReady && Time.realtimeSinceStartup<until)
                yield return null;
            if(avatars==null || !avatars.InitialActorsReady)
            {
                Debug.LogError("[HideAndShoot/Round] Cannot start setup before NGO player spawns.");
                yield break;
            }
            BeginNextRound();
        }

        public bool BeginNextRound()
        {
            if(network==null || !network.IsServer || avatars==null ||
                !avatars.InitialActorsReady || npcPool==null || ammo==null ||
                (phase!=HostPhase.AwaitScene && phase!=HostPhase.Result))
                return false;

            var room=RelayLobbySession.Instance;
            if(room==null)return false;
            int connected=room.GetParticipantIds().Count(id=>network.ConnectedClientsIds.Contains(id));
            if(connected<2 || connected>8)return false;
            participantsAtStart=connected;
            roundParticipants.Clear();
            foreach(var id in room.GetParticipantIds())
                if(network.ConnectedClientsIds.Contains(id))roundParticipants.Add(id);
            roundId++;
            redoVoters.Clear();
            phase=HostPhase.Setup;
            PublishVoteStatus();
            ammo.EndRound();
            var history=GetComponent<HostShotHistory>();
            history?.BeginNewRound();
            if(!avatars.PrepareRoundSpawns(roundId))
            {
                phase=HostPhase.Stopped;
                Debug.LogError("[HideAndShoot/Round] Cannot assign player round spawns.");
                return false;
            }
            foreach(var life in FindObjectsByType<HostPlayerLife>(FindObjectsSortMode.None))
                if(life.IsSpawned)
                {
                    life.HostResetForRound(roundId);
                    history?.RegisterActor(life.NetworkObject,roundId,0);
                }
            GetComponent<HostDeathResolver>()?.NotifyNewRound(roundId);
            foreach(var pistol in FindObjectsByType<PistolCombatController>(FindObjectsSortMode.None))
                pistol.HostSetRound(roundId,false);

            if(!npcPool.BeginRound(roundId,participantsAtStart))
            {
                phase=HostPhase.Stopped;
                Debug.LogError("[HideAndShoot/Round] NPC population setup failed. Round will not activate.");
                return false;
            }

            phase=HostPhase.Countdown;
            countdownEndsAt=Time.realtimeSinceStartup+5f;
            room.PublishMatchState();
            StartCoroutine(CountdownThenActivate(roundId));
            Debug.Log("[HideAndShoot/Round] Countdown 5s. Participants="+participantsAtStart+
                " NPCs="+npcPool.SpawnedCount+" pickups=0");
            return true;
        }

        private IEnumerator CountdownThenActivate(int startedRound)
        {
            yield return new WaitForSecondsRealtime(5f);
            if(phase!=HostPhase.Countdown || roundId!=startedRound ||
                network==null || !network.IsServer)yield break;

            if(!ammo.BeginRound(roundId,participantsAtStart))
            {
                phase=HostPhase.Stopped;
                Debug.LogError("[HideAndShoot/Round] Ammo system failed to initialize.");
                yield break;
            }
            foreach(var pistol in FindObjectsByType<PistolCombatController>(FindObjectsSortMode.None))
                pistol.HostSetRound(roundId,true);
            phase=HostPhase.Active;
            npcPool.ReleaseCountdownMovement(roundId);
            deaths?.CheckSoleSurvivor();
            RelayLobbySession.Instance?.PublishMatchState();
            Debug.Log("[HideAndShoot/Round] RoundActive; pickup field begins at zero.");
        }

        /// <summary>Host-only. Votes come from a verified connected NGO sender.</summary>
        public bool TryVoteRedo(ulong voter,int requestedRound)
        {
            if(network==null || !network.IsServer || phase!=HostPhase.Active ||
               roundId!=requestedRound)return false;
            var session=RelayLobbySession.Instance;
            if(session==null || !session.GetParticipantIds().Contains(voter) ||
               !network.ConnectedClientsIds.Contains(voter))return false;
            if(!redoVoters.Add(voter))return false;
            PublishVoteStatus();
            TryVoidRoundByUnanimousVote();
            return true;
        }

        /// <summary>Remove the departed member, then resolve a sole survivor before redo voting.</summary>
        public void OnMemberDeparture(ulong departed)
        {
            roundParticipants.Remove(departed);
            redoVoters.Remove(departed);
            PublishVoteStatus();
            // A departure that leaves one living player resolves the round as
            // a win, even during countdown or when that player already voted redo.
            deaths?.CheckSoleSurvivor();
            if(phase==HostPhase.Result)return;
            TryVoidRoundByUnanimousVote();
        }

        public void PublishVoteStatus()
        {
            if(network==null || !network.IsServer)return;
            var session=RelayLobbySession.Instance;
            session?.PublishRoundVoteStatus(roundId,redoVoters.Count,
                session.GetParticipantIds().Count(id=>network.ConnectedClientsIds.Contains(id)));
        }

        private void TryVoidRoundByUnanimousVote()
        {
            if(phase!=HostPhase.Active || network==null || !network.IsServer)return;
            var session=RelayLobbySession.Instance;
            if(session==null)return;
            var remaining=session.GetParticipantIds()
                .Where(id=>id!=ulong.MaxValue && network.ConnectedClientsIds.Contains(id))
                .ToArray();
            if(remaining.Length<1 || remaining.Any(id=>!redoVoters.Contains(id)))return;
            deaths?.CancelPendingRound(roundId);
            EndActiveRound();
            if(remaining.Length>=2)
            {
                if(!BeginNextRound())
                    Debug.LogError("[HideAndShoot/Redo] Failed to set up voted redo round.");
            }
            else
            {
                Debug.Log("[HideAndShoot/Redo] Round voided with fewer than two connected; returning to lobby without score.");
                session.HostReturnToLobby();
            }
        }

        private void OnWinnerCommitted(ulong winner,int score,bool matchOver)
        {
            if(phase!=HostPhase.Result)return;
            RelayLobbySession.Instance?.PublishRoundWinner(winner,score,matchOver,roundId);
            if(presentationCoroutine!=null)StopCoroutine(presentationCoroutine);
            presentationCoroutine=StartCoroutine(FinishPresentation(roundId,matchOver));
        }

        private IEnumerator FinishPresentation(int committedRound,bool matchOver)
        {
            // The winner/ghost camera and podium visuals are installed by the
            // presentation stage. Do not shorten the fixed five-second phase.
            yield return new WaitForSecondsRealtime(5f);
            if(network==null || !network.IsServer || phase!=HostPhase.Result ||
               committedRound!=roundId)yield break;
            if(matchOver)
            {
                phase=HostPhase.Podium;
                foreach(var pistol in FindObjectsByType<PistolCombatController>(FindObjectsSortMode.None))
                    if(pistol.IsSpawned)pistol.HostAllowWinnerMovement(false);
                RelayLobbySession.Instance?.PublishMatchState();
                RelayLobbySession.Instance?.PublishMatchPodium();
                yield return new WaitForSecondsRealtime(Mathf.Max(1f,podiumDurationSeconds));
                if(network!=null && network.IsServer && phase==HostPhase.Podium)
                    RelayLobbySession.Instance?.HostReturnToLobby();
            }
            else if(RelayLobbySession.Instance==null ||
                    RelayLobbySession.Instance.ParticipantCount<2)
            {
                // When only one connected participant remains after a dropout,
                // award their win and celebrate, then return to the room lobby.
                RelayLobbySession.Instance?.HostReturnToLobby();
            }
            else if(!BeginNextRound())
                Debug.LogError("[HideAndShoot/Match] Next round setup failed after celebration.");
        }

        /// <summary>Called only by Host result/redo logic once the round is decided.</summary>
        public void EndActiveRound(ulong winnerClientId=ulong.MaxValue)
        {
            if(network==null || !network.IsServer ||
                (phase!=HostPhase.Active && phase!=HostPhase.Countdown))return;
            phase=HostPhase.Result;
            foreach(var life in FindObjectsByType<HostPlayerLife>(FindObjectsSortMode.None))
                if(life.IsSpawned)life.HostClearReveal();
            ammo?.EndRound();
            npcPool?.FreezeMovement();
            foreach(var pistol in FindObjectsByType<PistolCombatController>(FindObjectsSortMode.None))
            {
                pistol.HostSetRound(roundId,false);
                pistol.HostAllowWinnerMovement(pistol.OwnerClientId==winnerClientId);
            }
            // Corpse presentation is allowed to finish before next round; NPC pool
            // will release them on BeginNextRound, after result phase.
        }

        private void OnDestroy()
        {
            if(setupCoroutine!=null)StopCoroutine(setupCoroutine);
            if(presentationCoroutine!=null)StopCoroutine(presentationCoroutine);
            if(deaths!=null)deaths.WinnerCommitted-=OnWinnerCommitted;
            if(network!=null && network.IsServer)
            {
                ammo?.EndRound();
                npcPool?.ReleaseWholeRound();
            }
        }
    }
}
