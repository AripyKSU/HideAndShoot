from pathlib import Path
import shutil
root=Path('C:/UnityProject/HideAndShoot')
backup=root/'AgentBackups/20261015-CombatPresentation'
def read(p): return (root/p).read_text(encoding='utf-8-sig')
def write(p,s):
    dst=root/p
    if dst.exists() and not (backup/p).exists():
        (backup/p).parent.mkdir(parents=True,exist_ok=True); shutil.copy2(dst,backup/p)
    dst.parent.mkdir(parents=True,exist_ok=True); dst.write_text(s,encoding='utf-8')
def replace(s,a,b):
    assert a in s, 'Missing anchor: '+a[:90]
    return s.replace(a,b,1)
write('Assets/HideAndShoot/Scripts/Combat/MatchCombatLedger.cs', '''using System.Collections.Generic;

namespace HideAndShoot.Combat
{
    /// <summary>Host-owned match kills with duplicate suppression and void-round rollback.</summary>
    public sealed class MatchCombatLedger
    {
        private readonly Dictionary<ulong,int> kills=new Dictionary<ulong,int>();
        private readonly Dictionary<ulong,ulong> roundVictims=new Dictionary<ulong,ulong>();
        public int RoundId { get; private set; }=-1;
        public int GetKills(ulong player)=>kills.TryGetValue(player,out int count)?count:0;
        public void BeginRound(int round)
        {
            RoundId=round;
            roundVictims.Clear();
        }
        public bool RecordKill(int round,ulong killer,ulong victim)
        {
            if(round!=RoundId || killer==victim || roundVictims.ContainsKey(victim))return false;
            roundVictims.Add(victim,killer);
            kills[killer]=GetKills(killer)+1;
            return true;
        }
        public void VoidRound(int round)
        {
            if(round!=RoundId)return;
            foreach(var killer in roundVictims.Values)
                if(kills.ContainsKey(killer))kills[killer]=System.Math.Max(0,GetKills(killer)-1);
            roundVictims.Clear();
        }
        public void ForgetPlayer(ulong player)=>kills.Remove(player);
    }
}
''')
p='Assets/HideAndShoot/Scripts/Networking/HostDeathResolver.cs';s=read(p)
s=replace(s,'private long scoringSequence;','private long scoringSequence;\n        private readonly MatchCombatLedger combatLedger=new MatchCombatLedger();\n        public int GetKills(ulong clientId)=>combatLedger.GetKills(clientId);')
s=replace(s,'npcs?.ApproveDeath(target,shot.RoundId,shot.TargetGeneration);','''if(npcs!=null && npcs.ApproveDeath(target,shot.RoundId,shot.TargetGeneration))
                    foreach(var shooter in FindObjectsByType<HostPlayerLife>(FindObjectsSortMode.None))
                        if(shooter.IsSpawned && shooter.OwnerClientId==shot.ShooterClientId)
                        {shooter.HostRevealForNpcHit(shot.RoundId);break;}''')
s=replace(s,'int after=CountAlivePlayers();','''int after=CountAlivePlayers();
            if(combatLedger.RecordKill(shot.RoundId,shot.ShooterClientId,player.OwnerClientId))
                RelayLobbySession.Instance?.PublishPlayerKill(shot.ShooterClientId,player.OwnerClientId,shot.RoundId);''')
s=replace(s,'lastScored.Remove(departed);','lastScored.Remove(departed);\n            combatLedger.ForgetPlayer(departed);')
s=replace(s,'if(round==null || expectedRound!=round.RoundId)return;','if(round==null || expectedRound!=round.RoundId)return;\n            combatLedger.VoidRound(expectedRound);')
s=replace(s,'winnerCommitted=ulong.MaxValue;\n            firstFatalRound=roundId;','winnerCommitted=ulong.MaxValue;\n            firstFatalRound=roundId;\n            combatLedger.BeginRound(roundId);')
write(p,s)
p='Assets/HideAndShoot/Scripts/Combat/HostPlayerLife.cs';s=read(p)
s=replace(s,'private Animator worldAnimator;','''public const double NpcRevealSeconds=10d;
        public NetworkVariable<double> RevealedUntil=new NetworkVariable<double>(
            0d,NetworkVariableReadPermission.Everyone,NetworkVariableWritePermission.Server);
        public double RevealSecondsRemaining=>IsSpawned && !IsDead.Value && NetworkManager!=null
            ? System.Math.Max(0d,RevealedUntil.Value-NetworkManager.ServerTime.Time):0d;
        public void HostRevealForNpcHit(int expectedRound)
        {
            if(!IsServer || IsDead.Value || expectedRound!=roundId)return;
            RevealedUntil.Value=NetworkManager.ServerTime.Time+NpcRevealSeconds;
        }
        public void HostClearReveal(){if(IsServer)RevealedUntil.Value=0d;}

        private Animator worldAnimator;''')
s=replace(s,'roundId=round;','roundId=round;\n            RevealedUntil.Value=0d;')
s=replace(s,'serverDeathTick=authoritativeDeathTick;','serverDeathTick=authoritativeDeathTick;\n            RevealedUntil.Value=0d;')
write(p,s)
p='Assets/HideAndShoot/Scripts/World/HostRoundSetupCoordinator.cs';s=read(p)
s=replace(s,'phase=HostPhase.Result;\n            ammo?.EndRound();','''phase=HostPhase.Result;
            foreach(var life in FindObjectsByType<HostPlayerLife>(FindObjectsSortMode.None))
                if(life.IsSpawned)life.HostClearReveal();
            ammo?.EndRound();''')
write(p,s)
p='Assets/HideAndShoot/Scripts/Networking/RelayLobbySession.cs';s=read(p)
s=replace(s,'public readonly int Score;','public readonly int Score;\n            public readonly int Kills;')
s=replace(s,'int joinedOrder,bool participating,bool alive)','int joinedOrder,bool participating,bool alive,int kills=0)')
s=replace(s,'ClientId=id; Nickname=nickname; Score=score;','ClientId=id; Nickname=nickname; Score=score; Kills=kills;')
s=replace(s,'private readonly List<ScoreRow> standings=', '''public readonly struct KillFeedEntry
        {
            public readonly long Sequence;
            public readonly int RoundId;
            public readonly string Killer,Victim;
            public readonly float ReceivedAt;
            public KillFeedEntry(long sequence,int round,string killer,string victim)
            {Sequence=sequence;RoundId=round;Killer=killer;Victim=victim;ReceivedAt=Time.unscaledTime;}
        }
        private const string KillMessage="HideAndShoot.PlayerKill.v1";
        private readonly List<KillFeedEntry> killFeed=new List<KillFeedEntry>(5);
        public IReadOnlyList<KillFeedEntry> KillFeed=>killFeed;
        private long killSequence,lastReceivedKill;

        public static ScoreRow[] RankRows(IEnumerable<ScoreRow> rows)=>rows
            .OrderByDescending(row=>row.Score).ThenByDescending(row=>row.Kills)
            .ThenByDescending(row=>row.Score>0?row.LastScoredOrder:0L)
            .ThenBy(row=>row.JoinedOrder).ThenBy(row=>row.ClientId).ToArray();

        private readonly List<ScoreRow> standings=''')
s=s.replace('HideAndShoot.MatchState.v1','HideAndShoot.MatchState.v2').replace('HideAndShoot.MatchPodium.v1','HideAndShoot.MatchPodium.v2')
s=replace(s,'standings.Clear();\n            MatchRoundId=0;','standings.Clear();\n            killFeed.Clear();killSequence=lastReceivedKill=0;\n            MatchRoundId=0;')
s=replace(s,'manager.CustomMessagingManager.RegisterNamedMessageHandler(PodiumMessage, OnPodiumReceived);','manager.CustomMessagingManager.RegisterNamedMessageHandler(PodiumMessage, OnPodiumReceived);\n            manager.CustomMessagingManager.RegisterNamedMessageHandler(KillMessage, OnPlayerKillReceived);')
s=replace(s,'manager.CustomMessagingManager.UnregisterNamedMessageHandler(PodiumMessage);','manager.CustomMessagingManager.UnregisterNamedMessageHandler(PodiumMessage);\n                manager.CustomMessagingManager.UnregisterNamedMessageHandler(KillMessage);')
s=replace(s,'i,participating,alive));','i,participating,alive,resolver!=null?resolver.GetKills(id):0));')
a='''// Score descending; newer positive ties win; zero ties retain lobby order.
            return result.OrderByDescending(row=>row.Score)
                .ThenByDescending(row=>row.Score>0?row.LastScoredOrder:0L)
                .ThenBy(row=>row.JoinedOrder)
                .ThenBy(row=>row.ClientId).ToArray();'''
s=replace(s,a,'// Preserve existing tie breakers after match kills.\n            return RankRows(result);')
s=replace(s,'writer.WriteValueSafe(row.Score);','writer.WriteValueSafe(row.Score);\n                writer.WriteValueSafe(row.Kills);')
s=replace(s,'reader.ReadValueSafe(out int score);\n                reader.ReadValueSafe(out long scoredOrder);','reader.ReadValueSafe(out int score);\n                reader.ReadValueSafe(out int kills);\n                reader.ReadValueSafe(out long scoredOrder);')
s=replace(s,'joinedOrder<0 || joinedOrder>=MaxPlayers || scoredOrder<0)','joinedOrder<0 || joinedOrder>=MaxPlayers || scoredOrder<0 || kills<0 || kills>10000)')
s=replace(s,'new ScoreRow(id,name,score,scoredOrder,joinedOrder,participating,alive);','new ScoreRow(id,name,score,scoredOrder,joinedOrder,participating,alive,kills);')
s=replace(s,'        private ScoreRow[] CaptureHostStandings()', '''        private void AppendKill(long sequence,int round,string killer,string victim)
        {
            if(sequence<=lastReceivedKill)return;
            lastReceivedKill=sequence;
            if(killFeed.Count==5)killFeed.RemoveAt(0);
            killFeed.Add(new KillFeedEntry(sequence,round,killer,victim));
        }
        public void PublishPlayerKill(ulong killer,ulong victim,int round)
        {
            if(manager==null || !manager.IsHost || !inMatch || killer==victim)return;
            string killerName=GetRankedNickname(killer),victimName=GetRankedNickname(victim);
            long sequence=++killSequence;
            AppendKill(sequence,round,killerName,victimName);
            if(manager.CustomMessagingManager==null)return;
            using(var writer=new FastBufferWriter(2048,Allocator.Temp))
            {
                writer.WriteValueSafe(sequence);writer.WriteValueSafe(round);
                writer.WriteValueSafe(killerName);writer.WriteValueSafe(victimName);
                foreach(var id in manager.ConnectedClientsIds)
                    if(id!=manager.LocalClientId)
                        manager.CustomMessagingManager.SendNamedMessage(KillMessage,id,writer,NetworkDelivery.ReliableSequenced);
            }
        }
        private void OnPlayerKillReceived(ulong sender,FastBufferReader reader)
        {
            if(manager==null || !manager.IsClient || manager.IsHost || !inMatch ||
                sender!=NetworkManager.ServerClientId)return;
            try
            {
                reader.ReadValueSafe(out long sequence);reader.ReadValueSafe(out int round);
                reader.ReadValueSafe(out string killer);reader.ReadValueSafe(out string victim);
                if(round<MatchRoundId || sequence<=0 || string.IsNullOrEmpty(killer) ||
                    string.IsNullOrEmpty(victim) || killer.Length>128 || victim.Length>128)return;
                AppendKill(sequence,round,killer,victim);
            }
            catch(Exception ex){Debug.LogWarning("[HideAndShoot/KillFeed] Invalid event: "+ex.Message);}
        }

        private ScoreRow[] CaptureHostStandings()''')
write(p,s)
print('Core written: ledger, host kill/reveal, round cleanup, score v2, ranking, reliable kill feed; originals backed up.')