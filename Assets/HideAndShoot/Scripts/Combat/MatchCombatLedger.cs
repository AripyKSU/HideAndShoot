using System.Collections.Generic;

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
