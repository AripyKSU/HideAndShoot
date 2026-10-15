using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

namespace HideAndShoot.Networking
{
    public readonly struct PublicRoom
    {
        public readonly string Code;
        public readonly int Players,Capacity;
        public readonly bool InMatch;
        public PublicRoom(string code,int players,int capacity,bool inMatch)
        {Code=code;Players=players;Capacity=capacity;InMatch=inMatch;}
    }

    /// <summary>
    /// Public Lobby advertisements for Relay rooms. NGO remains the membership authority;
    /// the directory's player count is explicit metadata, not Lobby's member count.
    /// Only room code, occupancy and match status are published, never nicknames.
    /// </summary>
    public sealed class PublicRoomDirectory : MonoBehaviour
    {
        private const string Protocol="HideAndShoot.RelayRooms.v1";
        private string lobbyId,code;
        private int epoch,lastCount=-1;
        private bool lastInMatch,maintaining;
        private float nextMaintenance,lastHeartbeat;
        private static float nextQueryAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetQueryClock()=>nextQueryAt=0;

        private static DataObject Public(string value)=>new DataObject(DataObject.VisibilityOptions.Public,value);
        private Dictionary<string,DataObject> Metadata(int count,bool inMatch)=>new Dictionary<string,DataObject>
        {
            {"protocol",new DataObject(DataObject.VisibilityOptions.Public,Protocol,DataObject.IndexOptions.S1)},
            {"relayCode",Public(code)},
            {"players",Public(count.ToString(CultureInfo.InvariantCulture))},
            {"inMatch",Public(inMatch?"1":"0")}
        };

        public async Task AdvertiseAsync(string relayCode)
        {
            int version=++epoch;
            code=relayCode;
            var lobby=await LobbyService.Instance.CreateLobbyAsync(relayCode,RelayLobbySession.MaxPlayers,
                new CreateLobbyOptions{IsPrivate=false,Data=Metadata(1,false)});
            if(this==null || version!=epoch)
            {
                await DeleteAsync(lobby.Id);
                return;
            }
            lobbyId=lobby.Id;lastCount=1;lastInMatch=false;
            nextMaintenance=Time.realtimeSinceStartup;lastHeartbeat=Time.realtimeSinceStartup;
        }

        private void Update()
        {
            var room=RelayLobbySession.Instance;
            if(string.IsNullOrEmpty(lobbyId) || maintaining || room==null || !room.IsRoomHost ||
                Time.realtimeSinceStartup<nextMaintenance)return;
            nextMaintenance=Time.realtimeSinceStartup+3f;
            MaintainAsync(room.ParticipantCount,room.IsMatchInProgress);
        }

        private async void MaintainAsync(int count,bool inMatch)
        {
            maintaining=true;
            string id=lobbyId;
            try
            {
                if(count!=lastCount || inMatch!=lastInMatch)
                {
                    await LobbyService.Instance.UpdateLobbyAsync(id,new UpdateLobbyOptions{Data=Metadata(count,inMatch)});
                    if(lobbyId==id){lastCount=count;lastInMatch=inMatch;}
                }
                if(lobbyId==id && Time.realtimeSinceStartup-lastHeartbeat>=12f)
                {
                    await LobbyService.Instance.SendHeartbeatPingAsync(id);
                    if(lobbyId==id)lastHeartbeat=Time.realtimeSinceStartup;
                }
            }
            catch(Exception ex)
            {
                if(lobbyId==id)Debug.LogWarning("[HideAndShoot/Rooms] Directory update failed: "+ex.Message);
            }
            finally{maintaining=false;}
        }

        public void Close()
        {
            ++epoch;
            string id=lobbyId;lobbyId=null;
            if(!string.IsNullOrEmpty(id))_ = DeleteAsync(id);
        }

        private static async Task DeleteAsync(string id)
        {
            try{await LobbyService.Instance.DeleteLobbyAsync(id);}
            catch(Exception ex){Debug.LogWarning("[HideAndShoot/Rooms] Directory cleanup: "+ex.Message);}
            // A lost host's advertisement also expires when its heartbeat stops.
        }

        public static async Task<List<PublicRoom>> QueryAsync()
        {
            var result=new List<PublicRoom>();
            var seen=new HashSet<string>();
            string token=null;
            do
            {
                // Lobby allows one query per second. Follow all continuations without random sampling.
                float delay=nextQueryAt-Time.realtimeSinceStartup;
                if(delay>0)await Task.Delay(TimeSpan.FromSeconds(delay));
                nextQueryAt=Time.realtimeSinceStartup+1.1f;
                var page=await LobbyService.Instance.QueryLobbiesAsync(new QueryLobbiesOptions
                {
                    Count=100,SampleResults=false,ContinuationToken=token,
                    Filters=new List<QueryFilter>{new QueryFilter(QueryFilter.FieldOptions.S1,Protocol,QueryFilter.OpOptions.EQ)},
                    Order=new List<QueryOrder>{new QueryOrder(true,QueryOrder.FieldOptions.Created)}
                });
                foreach(var lobby in page.Results)
                {
                    string relay=Value(lobby,"relayCode");
                    if(string.IsNullOrWhiteSpace(relay) || !seen.Add(relay))continue;
                    if(!int.TryParse(Value(lobby,"players"),out int count))continue;
                    result.Add(new PublicRoom(relay,Mathf.Clamp(count,1,lobby.MaxPlayers),
                        lobby.MaxPlayers,Value(lobby,"inMatch")=="1"));
                }
                string previous=token;token=page.ContinuationToken;
                if(page.Results.Count==0 || token==previous)break;
            }while(!string.IsNullOrEmpty(token));
            return result;
        }

        private static string Value(Unity.Services.Lobbies.Models.Lobby lobby,string key)=>
            lobby.Data!=null && lobby.Data.TryGetValue(key,out var data)?data.Value:string.Empty;
        private void OnApplicationQuit()=>Close();
        private void OnDestroy()=>Close();
    }
}
