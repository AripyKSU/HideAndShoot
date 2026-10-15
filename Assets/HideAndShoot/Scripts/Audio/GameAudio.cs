using HideAndShoot.Networking;
using HideAndShoot.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HideAndShoot.Audio
{
    /// <summary>Local presentation audio. Does not send audio events over the network.</summary>
    public sealed class GameAudio : MonoBehaviour
    {
        private static GameAudio instance;
        private AudioSource ui, signals, movement, pickups, celebration, lobbyMusic, ambience;
        private AudioClip click, countdown, whistle, step, reload, cheer;
        private bool wasCounting, podiumPlayed;
        private int lastTick=-1, lastRound=-1;
        private float nextClick;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { instance=null; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap() { Ensure(); }
        private static GameAudio Ensure()
        {
            if(instance==null)
                instance=new GameObject("GameAudio_LOCAL").AddComponent<GameAudio>();
            return instance;
        }
        private AudioSource Source(string label)
        {
            var child=new GameObject(label);child.transform.SetParent(transform,false);
            var source=child.AddComponent<AudioSource>();
            source.playOnAwake=false;source.spatialBlend=0f;source.dopplerLevel=0f;
            return source;
        }
        private void Awake()
        {
            if(instance!=null && instance!=this){Destroy(gameObject);return;}
            instance=this;DontDestroyOnLoad(gameObject);
            ui=Source("UI");signals=Source("Countdown");movement=Source("OwnFootsteps");
            pickups=Source("AmmoPickup");celebration=Source("Settlement");
            lobbyMusic=Source("LobbyMusic");ambience=Source("ArenaAmbience");
            click=Resources.Load<AudioClip>("Audio/UI");countdown=Resources.Load<AudioClip>("Audio/Countdown");
            whistle=Resources.Load<AudioClip>("Audio/Whistle");step=Resources.Load<AudioClip>("Audio/RobotStep");
            reload=Resources.Load<AudioClip>("Audio/Reload");cheer=Resources.Load<AudioClip>("Audio/Cheer");
            lobbyMusic.clip=Resources.Load<AudioClip>("Audio/LobbyMusic");lobbyMusic.loop=true;lobbyMusic.volume=0;
            ambience.clip=Resources.Load<AudioClip>("Audio/Ambience");ambience.loop=true;ambience.volume=0;
        }
        public static void PlayUiClick()
        {
            var audio=Ensure();if(Time.unscaledTime<audio.nextClick)return;
            audio.nextClick=Time.unscaledTime+.045f;
            if(audio.click!=null)audio.ui.PlayOneShot(audio.click,.55f);
        }
        public static void PlayFootstep(bool running)
        {
            var audio=Ensure();if(audio.step==null)return;
            audio.movement.pitch=Random.Range(.94f,1.06f)*(running?1.08f:1f);
            audio.movement.PlayOneShot(audio.step,running?.42f:.34f);
        }
        public static void PlayAmmoPickup()
        {
            var audio=Ensure();if(audio.reload!=null)audio.pickups.PlayOneShot(audio.reload,.58f);
        }
        public static void PlaySettlement()
        {
            var audio=Ensure();if(audio.podiumPlayed)return;
            audio.podiumPlayed=true;
            if(audio.cheer!=null)audio.celebration.PlayOneShot(audio.cheer,.50f);
        }
        private static void FadeBed(AudioSource source,float target)
        {
            if(source.clip==null)return;
            if(target>0 && !source.isPlaying)source.Play();
            source.volume=Mathf.MoveTowards(source.volume,target,Time.unscaledDeltaTime*.25f);
            if(target==0 && source.volume<=.0001f && source.isPlaying)source.Stop();
        }
        private void Update()
        {
            string scene=SceneManager.GetActiveScene().name;
            var session=RelayLobbySession.Instance;
            bool arena=scene=="GameScene";
            bool podium=arena && session!=null && session.MatchPhase==(int)HostRoundSetupCoordinator.HostPhase.Podium;
            FadeBed(lobbyMusic,scene=="MenuLobbyScene"?.16f:0f);
            FadeBed(ambience,arena && !podium?.36f:0f);
            if(!arena || session==null)
            {
                wasCounting=false;lastTick=-1;lastRound=-1;podiumPlayed=false;
                if(signals.isPlaying)signals.Stop();
                if(celebration.isPlaying)celebration.Stop();
                return;
            }
            if(lastRound!=session.MatchRoundId)
            {lastRound=session.MatchRoundId;lastTick=-1;wasCounting=false;podiumPlayed=false;signals.Stop();}
            bool counting=session.MatchPhase==(int)HostRoundSetupCoordinator.HostPhase.Countdown;
            if(counting)
            {
                int tick=Mathf.Clamp(session.CountdownRemaining,1,5);
                if(tick!=lastTick)
                {
                    lastTick=tick;
                    if(countdown!=null)signals.PlayOneShot(countdown,.55f);
                }
            }
            else
            {
                if(wasCounting && session.MatchPhase==(int)HostRoundSetupCoordinator.HostPhase.Active && whistle!=null)
                    signals.PlayOneShot(whistle,.42f);
                lastTick=-1;
            }
            wasCounting=counting;
        }
        private void OnDestroy() { if(instance==this)instance=null; }
    }
}
