using System;
using HideAndShoot.Combat;
using HideAndShoot.Networking;
using HideAndShoot.World;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace HideAndShoot.UI
{
    public sealed class GameHUDPresenter : MonoBehaviour
    {
        private RelayLobbySession session;
        private Canvas canvas;
        private TextMeshProUGUI roundInfo,ammo,feedback,result,countdown,scoreboard;
        private readonly TextMeshProUGUI[] scoreNames=new TextMeshProUGUI[8];
        private readonly TextMeshProUGUI[] scoreWins=new TextMeshProUGUI[8];
        private readonly TextMeshProUGUI[] scoreKills=new TextMeshProUGUI[8];
        private readonly TextMeshProUGUI[] killFeedLabels=new TextMeshProUGUI[5];
        private TextMeshProUGUI revealWarning,detectionNotice;
        private HideAndShoot.Player.PlayerDetectionVision cachedOwnerVision;
        private readonly Image[] scoreRows=new Image[8];
        private GameObject reticle;
        private Image reticleDot;
        private ControlsGuideToggle guide;
        private float resultUntil,nextOwnerRefresh;
        private int resultRound=-1,lastRoundId=-1,lastScoreRevision=-1;
        private string winnerName;
        private PistolCombatController cachedOwnerPistol;
        private HostPlayerLife cachedOwnerLife;

        private void Start()
        {
            session=RelayLobbySession.Instance;
            BindAuthoredUI();
            if(canvas==null){enabled=false;return;}
            if(session!=null)session.RoundWinnerAnnounced+=OnWinner;
        }
        private void BindAuthoredUI()
        {
            var root=transform.Find("GameHUD_Canvas");
            if(root==null){Debug.LogError("[HideAndShoot/HUD] Missing authored GameHUD_Canvas.");return;}
            canvas=root.GetComponent<Canvas>();
            roundInfo=PanelText(root,"RoundInfo");ammo=PanelText(root,"Ammo");
            feedback=PanelText(root,"AmmoFeedback");result=PanelText(root,"WinnerLabel");
            countdown=PanelText(root,"RoundCountdown");scoreboard=PanelText(root,"RankedScore");
            reticle=root.Find("ADSCrosshair")?.gameObject;
            reticleDot=reticle?.transform.Find("CenterDot")?.GetComponent<Image>();
            guide=root.Find("ControlsGuide")?.GetComponent<ControlsGuideToggle>();
            for(int i=0;i<8;i++)
            {
                var row=root.Find("RankedScore/Row"+i);
                scoreNames[i]=row?.Find("Nickname")?.GetComponent<TextMeshProUGUI>();
                scoreWins[i]=row?.Find("Wins")?.GetComponent<TextMeshProUGUI>();
                scoreKills[i]=row?.Find("Kills")?.GetComponent<TextMeshProUGUI>();
                scoreRows[i]=row?.GetComponent<Image>();
            }
            revealWarning=PanelText(root,"NpcRevealWarning");
            detectionNotice=PanelText(root,"DetectionVisionNotice");
            for(int i=0;i<5;i++)killFeedLabels[i]=root.Find("KillFeed/Entry"+i)?.GetComponent<TextMeshProUGUI>();
            if(roundInfo==null || ammo==null || feedback==null || result==null || countdown==null ||
                scoreboard==null || reticleDot==null || guide==null || Array.Exists(scoreNames,t=>t==null) ||
                Array.Exists(scoreWins,t=>t==null) || Array.Exists(scoreKills,t=>t==null) ||
                revealWarning==null || detectionNotice==null || Array.Exists(killFeedLabels,t=>t==null) || Array.Exists(scoreRows,t=>t==null))
            {Debug.LogError("[HideAndShoot/HUD] Incomplete authored compact HUD.");canvas=null;}
        }
        private static TextMeshProUGUI PanelText(Transform root,string panel)=>
            root.Find(panel)?.Find("Text")?.GetComponent<TextMeshProUGUI>();
        private static void SetVisible(TextMeshProUGUI text,bool visible)
        {if(text!=null)text.transform.parent.gameObject.SetActive(visible);}
        private static TextMeshProUGUI Label(Transform parent,string name,Vector2 anchor,Vector2 pivot,
            Vector2 position,Vector2 size,int fontSize,TextAnchor align,Color color,bool shade=true)
        {
            var rect=TacticalUI.Rect(parent,name,anchor,position,size,pivot);
            if(shade)TacticalUI.Frame(rect,TacticalUI.Muted);
            var text=TacticalUI.Text(rect,"Text","",Vector2.zero,Vector2.zero,fontSize,color,align);
            TacticalUI.Stretch(text.rectTransform);
            text.rectTransform.offsetMin=new Vector2(14,6);text.rectTransform.offsetMax=new Vector2(-14,-6);
            text.textWrappingMode=TextWrappingModes.NoWrap;
            return text;
        }
#if UNITY_EDITOR
        public void BakeEditableLayout()
        {if(transform.Find("GameHUD_Canvas")==null)CreateCanvas();BakeCombatLayout();}
        public void BakeCombatLayout()
        {
            var root=transform.Find("GameHUD_Canvas");
            if(root==null)throw new InvalidOperationException("Missing authored HUD");
            var panel=root.Find("RankedScore").GetComponent<RectTransform>();
            panel.sizeDelta=new Vector2(640,412);
            var title=panel.Find("Text").GetComponent<RectTransform>();
            title.anchoredPosition=new Vector2(-145,-28);
            var close=panel.Find("CloseHint").GetComponent<RectTransform>();
            close.anchoredPosition=new Vector2(195,-28);
            TacticalUI.Text(panel,"WinsHeader","승",new Vector2(150,-58),new Vector2(64,22),13,TacticalUI.Muted,TextAnchor.MiddleRight,new Vector2(.5f,1));
            TacticalUI.Text(panel,"KillsHeader","킬",new Vector2(240,-58),new Vector2(72,22),13,TacticalUI.Muted,TextAnchor.MiddleRight,new Vector2(.5f,1));
            for(int i=0;i<8;i++)
            {
                var row=panel.Find("Row"+i).GetComponent<RectTransform>();
                row.sizeDelta=new Vector2(596,34);row.anchoredPosition=new Vector2(0,-88-i*36);
                var rank=row.Find("Rank").GetComponent<RectTransform>();rank.anchoredPosition=new Vector2(-278,0);
                var name=row.Find("Nickname").GetComponent<RectTransform>();
                name.anchoredPosition=new Vector2(-75,0);name.sizeDelta=new Vector2(310,30);
                var wins=row.Find("Wins").GetComponent<RectTransform>();wins.anchoredPosition=new Vector2(150,0);
                TacticalUI.Text(row,"Kills","0",new Vector2(240,0),new Vector2(72,34),18,TacticalUI.White,TextAnchor.MiddleRight);
            }
            var feed=TacticalUI.Rect(root,"KillFeed",new Vector2(1,1),new Vector2(-24,-164),new Vector2(500,150),new Vector2(1,1));
            for(int i=0;i<5;i++)
            {
                var label=TacticalUI.Text(feed,"Entry"+i,"",new Vector2(-8,-16-i*28),new Vector2(484,26),16,TacticalUI.White,TextAnchor.MiddleRight,new Vector2(1,1));
                label.rectTransform.pivot=new Vector2(1,.5f);
                label.enableAutoSizing=true;label.fontSizeMin=11;label.fontSizeMax=16;
                label.textWrappingMode=TextWrappingModes.NoWrap;
                var shadow=label.GetComponent<Shadow>()??label.gameObject.AddComponent<Shadow>();
                shadow.effectColor=new Color(0,0,0,.85f);shadow.effectDistance=new Vector2(1,-1);
                label.gameObject.SetActive(false);
            }
            var warning=Label(root,"NpcRevealWarning",new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,88),new Vector2(660,44),18,TextAnchor.MiddleCenter,TacticalUI.Gold);
            warning.enableAutoSizing=true;warning.fontSizeMin=13;warning.fontSizeMax=18;
            warning.transform.parent.gameObject.SetActive(false);
            var detection=Label(root,"DetectionVisionNotice",new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,140),new Vector2(660,44),18,TextAnchor.MiddleCenter,new Color(.79f,.58f,1f));
            detection.enableAutoSizing=true;detection.fontSizeMin=13;detection.fontSizeMax=18;
            detection.transform.parent.gameObject.SetActive(false);
        }
#endif
        private void CreateCanvas()
        {
            var root=new GameObject("GameHUD_Canvas",typeof(RectTransform),typeof(Canvas),
                typeof(CanvasScaler),typeof(GraphicRaycaster));
            root.transform.SetParent(transform,false);
            canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=100;
            var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1366,768);scaler.matchWidthOrHeight=.5f;
            roundInfo=Label(root.transform,"RoundInfo",new Vector2(.5f,1),new Vector2(.5f,1),
                new Vector2(0,-24),new Vector2(420,42),20,TextAnchor.MiddleCenter,TacticalUI.White);
            ammo=Label(root.transform,"Ammo",new Vector2(1,0),new Vector2(1,0),
                new Vector2(-24,24),new Vector2(200,64),34,TextAnchor.MiddleRight,TacticalUI.White);
            ammo.fontStyle=FontStyles.Bold;
            ammo.enableAutoSizing=true;ammo.fontSizeMin=28;ammo.fontSizeMax=34;
            feedback=Label(root.transform,"AmmoFeedback",new Vector2(.5f,0),new Vector2(.5f,0),
                new Vector2(0,32),new Vector2(480,42),18,TextAnchor.MiddleCenter,TacticalUI.White);
            result=Label(root.transform,"WinnerLabel",new Vector2(.5f,1),new Vector2(.5f,1),
                new Vector2(0,-102),new Vector2(720,76),32,TextAnchor.MiddleCenter,TacticalUI.White);
            result.enableAutoSizing=true;result.fontSizeMin=18;result.fontSizeMax=32;
            countdown=Label(root.transform,"RoundCountdown",new Vector2(.5f,.5f),new Vector2(.5f,.5f),
                new Vector2(0,24),new Vector2(180,140),68,TextAnchor.MiddleCenter,TacticalUI.White,false);
            countdown.fontStyle=FontStyles.Bold;
            CreateScoreboard(root.transform);
            CreateGuide(root.transform);
            CreateReticle(root.transform);
            foreach(var item in new[]{roundInfo,ammo,feedback,result,countdown,scoreboard})SetVisible(item,false);
            reticle.SetActive(false);
        }
        private void CreateScoreboard(Transform parent)
        {
            var panel=TacticalUI.Rect(parent,"RankedScore",new Vector2(.5f,.5f),new Vector2(0,12),new Vector2(520,396));
            TacticalUI.Frame(panel,TacticalUI.Muted);
            scoreboard=TacticalUI.Text(panel,"Text","순위",new Vector2(-90,-28),new Vector2(300,44),22,
                TacticalUI.White,TextAnchor.MiddleLeft,new Vector2(.5f,1));
            TacticalUI.Text(panel,"CloseHint","Tab을 놓으면 닫기",new Vector2(145,-28),new Vector2(184,30),14,
                TacticalUI.Muted,TextAnchor.MiddleRight,new Vector2(.5f,1));
            for(int i=0;i<8;i++)
            {
                var row=TacticalUI.Rect(panel,"Row"+i,new Vector2(.5f,1),new Vector2(0,-76-i*36),new Vector2(476,34));
                scoreRows[i]=row.gameObject.AddComponent<Image>();scoreRows[i].raycastTarget=false;
                TacticalUI.Text(row,"Rank",(i+1).ToString(),new Vector2(-218,0),new Vector2(26,30),16,
                    TacticalUI.Muted,TextAnchor.MiddleCenter);
                scoreNames[i]=TacticalUI.Text(row,"Nickname","",new Vector2(-18,0),new Vector2(348,30),18,TacticalUI.White);
                scoreNames[i].textWrappingMode=TextWrappingModes.NoWrap;
                scoreNames[i].enableAutoSizing=true;scoreNames[i].fontSizeMin=12;scoreNames[i].fontSizeMax=18;
                scoreWins[i]=TacticalUI.Text(row,"Wins","",new Vector2(195,0),new Vector2(68,34),18,
                    TacticalUI.White,TextAnchor.MiddleRight);
                row.gameObject.SetActive(false);
            }
            TacticalUI.Text(panel,"Rule","3승 먼저 달성",new Vector2(0,23),new Vector2(476,28),14,
                TacticalUI.Muted,TextAnchor.MiddleCenter,new Vector2(.5f,0));
        }
        private void CreateGuide(Transform parent)
        {
            var panel=TacticalUI.Rect(parent,"ControlsGuide",Vector2.zero,new Vector2(24,24),new Vector2(346,28),Vector2.zero);
            var buttonRect=TacticalUI.Rect(panel,"GuideToggle",Vector2.zero,Vector2.zero,new Vector2(346,28),Vector2.zero);
            var image=buttonRect.gameObject.AddComponent<Image>();image.color=new Color(.035f,.055f,.07f,.8f);
            var button=buttonRect.gameObject.AddComponent<Button>();
            TacticalUI.Text(buttonRect,"Text","",Vector2.zero,new Vector2(340,28),14,TacticalUI.Muted,TextAnchor.MiddleLeft);
            var content=TacticalUI.Rect(panel,"GuideContent",Vector2.zero,new Vector2(0,40),new Vector2(346,324),Vector2.zero);
            TacticalUI.Frame(content,TacticalUI.Muted);
            ControlsGuideToggle.CreateRows(content,16);
            guide=panel.gameObject.AddComponent<ControlsGuideToggle>();
            guide.Configure(button,content.gameObject,panel,new Vector2(346,364),new Vector2(346,28),false,true);
        }
        private void CreateReticle(Transform parent)
        {
            var center=TacticalUI.Rect(parent,"ADSCrosshair",new Vector2(.5f,.5f),Vector2.zero,new Vector2(32,32));
            reticle=center.gameObject;
            TacticalUI.Stripe(center,"CenterOutline",new Vector2(.5f,.5f),Vector2.zero,new Vector2(7,7),new Color(.02f,.04f,.06f,.95f));
            reticleDot=TacticalUI.Stripe(center,"CenterDot",new Vector2(.5f,.5f),Vector2.zero,new Vector2(3,3),TacticalUI.White);
            for(int i=0;i<4;i++)
            {
                var axis=i<2?Vector2.right:Vector2.up;
                var bar=TacticalUI.Stripe(center,"Bar"+i,new Vector2(.5f,.5f),axis*(i%2==0?-11:11),
                    i<2?new Vector2(6,2):new Vector2(2,6),new Color(.95f,.98f,1,.8f));
                var outline=bar.gameObject.AddComponent<Outline>();outline.effectColor=new Color(.02f,.04f,.06f,.9f);
                outline.effectDistance=new Vector2(1,-1);
            }
        }
        private void OnWinner(ulong winner,int score,bool matchOver,int round)
        {
            if(session==null || round<lastRoundId)return;
            resultRound=round;winnerName=session.GetRankedNickname(winner);
            if(string.IsNullOrEmpty(winnerName))winnerName="PLAYER";
            resultUntil=Time.unscaledTime+5;
        }
        private void ApplyVisibility(bool action,bool showAmmo,bool counting,bool winner,bool boardHeld)
        {
            bool showBoard=boardHeld && !counting && !winner;
            SetVisible(roundInfo,true);SetVisible(ammo,showAmmo);SetVisible(scoreboard,showBoard);
            SetVisible(countdown,counting);SetVisible(result,winner);
            guide.gameObject.SetActive(action && !showBoard);
            SetVisible(feedback,action && !showBoard && !string.IsNullOrEmpty(feedback.text));
        }
        private void SetScoreRow(int index,string nickname,int wins,bool local,bool present,int kills=0)
        {
            scoreRows[index].gameObject.SetActive(present);
            scoreNames[index].text=nickname;scoreWins[index].text=wins+"승";
            scoreKills[index].text=kills.ToString();
            scoreRows[index].color=local?new Color(.18f,.27f,.28f,.85f):
                index%2==0?new Color(.09f,.12f,.14f,.7f):Color.clear;
            scoreNames[index].color=local?TacticalUI.Gold:TacticalUI.White;
        }
        private void Update()
        {
            if(session==null || canvas==null)return;
            var net=NetworkManager.Singleton;
            bool active=net!=null && net.IsClient && session.IsMatchInProgress;
            bool podium=session.MatchPhase==(int)HostRoundSetupCoordinator.HostPhase.Podium;
            canvas.enabled=active && !podium && session.MatchPhase>=(int)HostRoundSetupCoordinator.HostPhase.Countdown && !session.RoundMenuVisible;
            if(!canvas.enabled)return;
            int round=session.MatchRoundId;
            if(round!=lastRoundId){lastRoundId=round;if(resultRound!=round)resultUntil=0;}
            if(lastScoreRevision!=session.MatchStateRevision)
            {
                lastScoreRevision=session.MatchStateRevision;
                int index=0;
                foreach(var row in session.Standings)
                {
                    if(index==8)break;
                    SetScoreRow(index++,row.Nickname,row.Score,row.ClientId==net.LocalClientId,true,row.Kills);
                }
                while(index<8)SetScoreRow(index++,"",0,false,false);
            }
            if(cachedOwnerPistol==null || !cachedOwnerPistol.IsSpawned || Time.unscaledTime>=nextOwnerRefresh)
            {
                nextOwnerRefresh=Time.unscaledTime+1;cachedOwnerPistol=null;cachedOwnerLife=null;cachedOwnerVision=null;
                foreach(var player in FindObjectsByType<PistolCombatController>(FindObjectsSortMode.None))
                    if(player.IsSpawned && player.IsOwner)
                    {cachedOwnerPistol=player;cachedOwnerLife=player.GetComponent<HostPlayerLife>();cachedOwnerVision=player.GetComponent<HideAndShoot.Player.PlayerDetectionVision>();break;}
            }
            var pistol=cachedOwnerPistol;var life=cachedOwnerLife;
            var motor=pistol!=null?pistol.GetComponent<HideAndShoot.Player.PlayerLocalMotor>():null;
            bool action=session.MatchPhase==(int)HostRoundSetupCoordinator.HostPhase.Active;
            bool playing=pistol!=null && pistol.RoundId.Value==round;
            bool localParticipant=false;
            foreach(var row in session.Standings)if(row.ClientId==net.LocalClientId){localParticipant=row.Participating;break;}
            bool waiting=!localParticipant || !playing;
            bool dead=life!=null && life.IsDead.Value;
            bool showAmmo=action && playing && life!=null && !dead;
            roundInfo.text="라운드 "+Mathf.Max(1,round)+(action?" · 생존 "+session.MatchAliveCount+"명":"")+
                (action && waiting?" · 다음 라운드 대기":action && dead?" · 관전 중":"");
            if(showAmmo)
            {
                int bullets=pistol.DisplayAmmo;ammo.text=bullets+" / 10";
                ammo.color=bullets==0?TacticalUI.Coral:bullets<=2?TacticalUI.Gold:TacticalUI.White;
            }
            reticle.SetActive(showAmmo && pistol.IsAiming);
            reticleDot.color=pistol!=null && pistol.State==PistolCombatController.LocalAimState.AimReady?
                new Color(.40f,1,.84f):TacticalUI.White;
            feedback.text=showAmmo?pistol.LocalFeedback:string.Empty;
            if(string.IsNullOrEmpty(feedback.text) && motor!=null && motor.IsOwner && Time.unscaledTime<motor.SensitivityVisibleUntil)
                feedback.text="마우스 감도  "+motor.Sensitivity;
            bool winner=resultRound==round && Time.unscaledTime<resultUntil &&
                session.MatchPhase==(int)HostRoundSetupCoordinator.HostPhase.Result;
            if(winner)result.text=winnerName+" · 라운드 승리";
            bool counting=session.MatchPhase==(int)HostRoundSetupCoordinator.HostPhase.Countdown;
            if(counting)countdown.text=Mathf.Clamp(session.CountdownRemaining,1,5).ToString();
            bool boardHeld=Keyboard.current!=null && Keyboard.current.tabKey.isPressed;
            ApplyVisibility(action,showAmmo,counting,winner,boardHeld);
            double remaining=life!=null?life.RevealSecondsRemaining:0d;
            bool warningVisible=showAmmo && remaining>0d && !boardHeld;
            SetVisible(revealWarning,warningVisible);
            if(warningVisible)revealWarning.text="NPC를 잘못 사격했습니다. "+Math.Ceiling(remaining)+"초 동안 모습이 노출됩니다.";
            double detectionRemaining=cachedOwnerVision!=null?cachedOwnerVision.SecondsRemaining:0d;
            bool detectionVisible=showAmmo && detectionRemaining>0d && !boardHeld;
            SetVisible(detectionNotice,detectionVisible);
            if(detectionVisible)detectionNotice.text=Math.Ceiling(detectionRemaining)+"초 동안 다른 플레이어의 위치가 보입니다.";
            int feedIndex=0;
            for(int i=session.KillFeed.Count-1;i>=0 && feedIndex<5;i--)
            {
                var entry=session.KillFeed[i];
                float age=Time.unscaledTime-entry.ReceivedAt;
                if(entry.RoundId!=round || age>=5f)continue;
                var label=killFeedLabels[feedIndex++];
                label.gameObject.SetActive(!boardHeld && !counting);
                label.text=entry.Killer+"  ->  "+entry.Victim;
                label.alpha=Mathf.Clamp01(5f-age);
            }
            while(feedIndex<5)killFeedLabels[feedIndex++].gameObject.SetActive(false);
        }
#if UNITY_EDITOR
        // Preview uses the same visibility rules and authored layout as the live HUD.
        public void Preview(string state)
        {
            BindAuthoredUI();if(canvas==null)throw new InvalidOperationException("Compact HUD not baked");
            canvas.enabled=true;
            bool action=state!="countdown" && state!="winner";
            roundInfo.text="라운드 2 · 생존 6명"+(state=="spectator"?" · 관전 중":"");
            ammo.text="2 / 10";ammo.color=TacticalUI.Gold;
            feedback.text=state=="toast"?"탄약 +5":state=="sensitivity"?"마우스 감도  5":"";
            countdown.text="3";result.text=new string('가',20)+" · 라운드 승리";
            for(int i=0;i<8;i++)SetScoreRow(i,i==2?new string('W',20):new string((char)('가'+i),20),3-i%3,i==3,true);
            ApplyVisibility(action,action && state!="spectator",state=="countdown",state=="winner",state=="scoreboard");
            guide.SetExpanded(state=="help");reticle.SetActive(action && state!="spectator" && state!="scoreboard");
        }
#endif
        private void OnDestroy(){if(session!=null)session.RoundWinnerAnnounced-=OnWinner;}
    }
}
