int checks=0;
void Check(bool ok,string name){if(!ok)throw new System.Exception(name);checks++;}
var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Static;
var sessionType=typeof(HideAndShoot.Networking.RelayLobbySession);
var session=(HideAndShoot.Networking.RelayLobbySession)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(sessionType);
var rows=new[]{new HideAndShoot.Networking.RelayLobbySession.ScoreRow(17,"검증_Player",2,14,3,true,false,27)};
var writer=new Unity.Netcode.FastBufferWriter(4096,Unity.Collections.Allocator.Temp);
try
{
 object[] args={writer,rows};sessionType.GetMethod("WriteScoreRows",flags).Invoke(session,args);
 var sent=(Unity.Netcode.FastBufferWriter)args[0];
 var reader=new Unity.Netcode.FastBufferReader(sent,Unity.Collections.Allocator.Temp);
 try
 {
  object[] receive={reader};
  var decoded=(HideAndShoot.Networking.RelayLobbySession.ScoreRow[])sessionType.GetMethod("ReadScoreRows",flags).Invoke(null,receive);
  Check(decoded.Length==1 && decoded[0].ClientId==17,"score identity");
  Check(decoded[0].Nickname==rows[0].Nickname && decoded[0].Kills==27,"unicode name and kills wire roundtrip");
  Check(decoded[0].Score==2 && decoded[0].LastScoredOrder==14 && decoded[0].JoinedOrder==3,"old ranking wire fields");
  Check(decoded[0].Participating && !decoded[0].Alive,"life flags wire fields");
 }
 finally{reader.Dispose();}
}
finally{writer.Dispose();}
var feed=(System.Collections.Generic.List<HideAndShoot.Networking.RelayLobbySession.KillFeedEntry>)sessionType.GetField("killFeed",flags).GetValue(session);
// Field initializers do not run on an uninitialized MonoBehaviour; supply the isolated buffer explicitly.
if(feed==null){feed=new System.Collections.Generic.List<HideAndShoot.Networking.RelayLobbySession.KillFeedEntry>();sessionType.GetField("killFeed",flags).SetValue(session,feed);}
var append=sessionType.GetMethod("AppendKill",flags);
append.Invoke(session,new object[]{1L,1,"A","B"});append.Invoke(session,new object[]{1L,1,"A","B"});
Check(feed.Count==1,"duplicate kill event suppressed");
for(long seq=2;seq<=7;seq++)append.Invoke(session,new object[]{seq,1,"A","B"});
Check(feed.Count==5 && feed[0].Sequence==3 && feed[4].Sequence==7,"bounded five-row feed");
return new { mode="EditMode", passed=checks, failed=0 };