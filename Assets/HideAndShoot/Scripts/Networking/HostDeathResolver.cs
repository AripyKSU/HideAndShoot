using System;
using System.Collections.Generic;
using System.Linq;
using HideAndShoot.Combat;
using HideAndShoot.NPC;
using HideAndShoot.World;
using Unity.Netcode;
using UnityEngine;

namespace HideAndShoot.Networking
{
    /// <summary>
    /// Consumes only HostShotHistory-approved hits. A fatal hit between the last
    /// two players opens a fixed one-second outcome window. The first Host-approved
    /// fatal attacker keeps the win even if that attacker also receives a verified
    /// pre-death shot within the grace window.
    ///
    /// This is the Host outcome CORE. Scoring UI, corpse camera/ghost, podium,
    /// disconnect priority and automatic subsequent rounds remain separate work.
    /// </summary>
    public sealed class HostDeathResolver : MonoBehaviour
    {
        private readonly Dictionary<ulong,int> wins=new Dictionary<ulong,int>();
        private readonly Dictionary<ulong,long> lastScored=new Dictionary<ulong,long>();
        private long scoringSequence;
        private readonly MatchCombatLedger combatLedger=new MatchCombatLedger();
        public int GetKills(ulong clientId)=>combatLedger.GetKills(clientId);
        private NetworkManager network;
        private HostShotHistory history;
        private NpcPoolCoordinator npcs;
        private HostRoundSetupCoordinator round;
        private bool outcomePending;
        private ulong firstFatalShooter;
        private float outcomeDeadline;
        private int firstFatalRound=-1;
        private ulong winnerCommitted=ulong.MaxValue;

        public event Action<ulong,int,bool> WinnerCommitted;
        public bool OutcomePending=>outcomePending;
        public ulong CommittedWinner=>winnerCommitted;
        public int GetWins(ulong clientId) =>
            wins.TryGetValue(clientId,out var count)?count:0;
        public long GetLastScoreSequence(ulong clientId) =>
            lastScored.TryGetValue(clientId,out var sequence)?sequence:0;

        private void Start()
        {
            network=NetworkManager.Singleton;
            if(network==null || !network.IsServer)return;
            history=GetComponent<HostShotHistory>();
            npcs=GetComponent<NpcPoolCoordinator>();
            round=GetComponent<HostRoundSetupCoordinator>();
            if(history!=null)
                history.HostApprovedShot+=HandleApprovedShot;
            network.OnClientDisconnectCallback+=HandleDisconnect;
        }

        private void HandleApprovedShot(HostShotHistory.ApprovedShot shot)
        {
            if(network==null || !network.IsServer || round==null ||
               round.Phase!=HostRoundSetupCoordinator.HostPhase.Active ||
               shot.RoundId!=round.RoundId || history==null ||
               !history.TryResolveActor(shot.TargetNetworkId,shot.RoundId,
                   shot.TargetGeneration,out NetworkObject target))return;

            if(outcomePending &&
               (shot.RoundId!=firstFatalRound || Time.unscaledTime>outcomeDeadline))
                return;
            var npc=target.GetComponent<NpcWanderBrain>();
            if(npc!=null)
            {
                if(npcs!=null && npcs.ApproveDeath(target,shot.RoundId,shot.TargetGeneration))
                    foreach(var shooter in FindObjectsByType<HostPlayerLife>(FindObjectsSortMode.None))
                        if(shooter.IsSpawned && shooter.OwnerClientId==shot.ShooterClientId)
                        {shooter.HostRevealForNpcHit(shot.RoundId);break;}
                return;
            }

            var player=target.GetComponent<HostPlayerLife>();
            if(player==null)return;

            // Never count a duplicate fatal event twice; deaths are Host-only.
            int before=CountAlivePlayers();
            if(!player.HostKill(shot.RoundId,network.ServerTime.Tick))return;
            int after=CountAlivePlayers();
            if(combatLedger.RecordKill(shot.RoundId,shot.ShooterClientId,player.OwnerClientId))
                RelayLobbySession.Instance?.PublishPlayerKill(shot.ShooterClientId,player.OwnerClientId,shot.RoundId);
            RelayLobbySession.Instance?.PublishMatchState();

            if(!outcomePending && before==2 && after==1)
            {
                outcomePending=true;
                firstFatalShooter=shot.ShooterClientId;
                firstFatalRound=shot.RoundId;
                outcomeDeadline=Time.unscaledTime+1f;
                Debug.Log("[HideAndShoot/Outcome] Final-two fatal shot approved; fixed 1s window.");
            }
            // Subsequent validated B->A shot inside the window can kill A;
            // the first approved attacker above is never replaced.
        }

        private int CountAlivePlayers()
        {
            int living=0;
            foreach(var actor in FindObjectsByType<HostPlayerLife>(FindObjectsSortMode.None))
                if(actor.IsSpawned && !actor.IsDead.Value && round!=null &&
                   actor.RoundId==round.RoundId &&
                   round.IsRoundParticipant(actor.OwnerClientId) &&
                   network!=null && network.ConnectedClientsIds.Contains(actor.OwnerClientId))
                    living++;
            return living;
        }

        private void HandleDisconnect(ulong departed)
        {
            wins.Remove(departed); // scores leave together with their owner
            lastScored.Remove(departed);
            combatLedger.ForgetPlayer(departed);
            round?.OnMemberDeparture(departed);
            if(!outcomePending)CheckSoleSurvivor();
        }

        public void CancelPendingRound(int expectedRound)
        {
            if(round==null || expectedRound!=round.RoundId)return;
            combatLedger.VoidRound(expectedRound);
            outcomePending=false;
            firstFatalShooter=ulong.MaxValue;
            firstFatalRound=-1;
        }

        /// <summary>Disconnects may decide a round without a shot.</summary>
        public void CheckSoleSurvivor()
        {
            if(network==null || !network.IsServer || outcomePending ||
               winnerCommitted!=ulong.MaxValue || round==null ||
               (round.Phase!=HostRoundSetupCoordinator.HostPhase.Active &&
                round.Phase!=HostRoundSetupCoordinator.HostPhase.Countdown))return;
            ulong candidate=ulong.MaxValue;
            int count=0;
            foreach(var life in FindObjectsByType<HostPlayerLife>(FindObjectsSortMode.None))
            {
                if(!life.IsSpawned || life.IsDead.Value ||
                   !round.IsRoundParticipant(life.OwnerClientId) ||
                   !network.ConnectedClientsIds.Contains(life.OwnerClientId))continue;
                candidate=life.OwnerClientId;
                count++;
            }
            if(count==1)CommitWinner(candidate);
        }

        private void CommitWinner(ulong chosen)
        {
            if(winnerCommitted!=ulong.MaxValue || round==null ||
               (round.Phase!=HostRoundSetupCoordinator.HostPhase.Active &&
                round.Phase!=HostRoundSetupCoordinator.HostPhase.Countdown))return;
            winnerCommitted=chosen;
            int score=GetWins(chosen)+1;
            wins[chosen]=score;
            lastScored[chosen]=++scoringSequence;
            round.EndActiveRound(chosen);
            RelayLobbySession.Instance?.PublishMatchState();
            WinnerCommitted?.Invoke(chosen,score,score>=3);
            Debug.Log("[HideAndShoot/Outcome] Winner="+chosen+" wins="+score);
        }

        private void Update()
        {
            if(!outcomePending || Time.unscaledTime<outcomeDeadline ||
               network==null || !network.IsServer)return;
            outcomePending=false;
            CommitWinner(firstFatalShooter);
        }

        /// <summary>
        /// Called when a fresh round starts; retaining wins for entire match.
        /// Does not reset the currently pending fixed one-second outcome window.
        /// </summary>
        public void NotifyNewRound(int roundId)
        {
            if(outcomePending)return;
            winnerCommitted=ulong.MaxValue;
            firstFatalRound=roundId;
            combatLedger.BeginRound(roundId);
        }

        private void OnDestroy()
        {
            if(history!=null)
                history.HostApprovedShot-=HandleApprovedShot;
            if(network!=null)
                network.OnClientDisconnectCallback-=HandleDisconnect;
        }
    }
}
