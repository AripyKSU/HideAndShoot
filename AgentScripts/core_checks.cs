var ledger=new HideAndShoot.Combat.MatchCombatLedger();
int checks=0;
void Check(bool value,string label){if(!value)throw new System.Exception(label);checks++;}
ledger.BeginRound(1);
Check(ledger.RecordKill(1,10,20),"first death");
Check(!ledger.RecordKill(1,30,20) && ledger.GetKills(30)==0,"duplicate victim");
Check(!ledger.RecordKill(0,10,40),"stale round");
Check(!ledger.RecordKill(1,10,10),"self kill");
ledger.BeginRound(2);ledger.RecordKill(2,10,30);ledger.RecordKill(2,40,50);
Check(ledger.GetKills(10)==2,"match accumulation");
ledger.VoidRound(1);Check(ledger.GetKills(10)==2,"stale rollback");
ledger.VoidRound(2);Check(ledger.GetKills(10)==1 && ledger.GetKills(40)==0,"void only current round");
ledger.VoidRound(2);Check(ledger.GetKills(10)==1,"rollback idempotence");
ledger.ForgetPlayer(10);Check(ledger.GetKills(10)==0,"disconnect cleanup");
var rows=new[]{
 new HideAndShoot.Networking.RelayLobbySession.ScoreRow(1,"one",1,20,0,true,true,9),
 new HideAndShoot.Networking.RelayLobbySession.ScoreRow(2,"two",2,1,1,true,true,0),
 new HideAndShoot.Networking.RelayLobbySession.ScoreRow(3,"three",1,1,2,true,true,10),
 new HideAndShoot.Networking.RelayLobbySession.ScoreRow(4,"four",1,30,3,true,true,9)};
var ranked=HideAndShoot.Networking.RelayLobbySession.RankRows(rows);
Check(ranked[0].ClientId==2,"wins before kills");
Check(ranked[1].ClientId==3,"kills before old tie breaker");
Check(ranked[2].ClientId==4 && ranked[3].ClientId==1,"preserve recent win tie breaker");
var zero=HideAndShoot.Networking.RelayLobbySession.RankRows(new[]{
 new HideAndShoot.Networking.RelayLobbySession.ScoreRow(8,"late",0,999,1,true,true,0),
 new HideAndShoot.Networking.RelayLobbySession.ScoreRow(7,"early",0,0,0,true,true,0)});
Check(zero[0].ClientId==7,"preserve zero-win join order");
Check(!UnityEditor.EditorApplication.isPlaying,"edit mode only");
return new { passed=checks, mode="EditMode", failed=0 };