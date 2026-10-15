exec(open('C:/UnityProject/HideAndShoot/AgentScripts/feature_core.py',encoding='utf-8').read().split("write('Assets/")[0])
p='Assets/HideAndShoot/Scripts/UI/GameHUDPresenter.cs';s=read(p)
s=replace(s,'private readonly Image[] scoreRows=', '''private readonly TextMeshProUGUI[] scoreKills=new TextMeshProUGUI[8];
        private readonly TextMeshProUGUI[] killFeedLabels=new TextMeshProUGUI[5];
        private TextMeshProUGUI revealWarning;
        private readonly Image[] scoreRows=''')
s=replace(s,'scoreWins[i]=row?.Find("Wins")?.GetComponent<TextMeshProUGUI>();','scoreWins[i]=row?.Find("Wins")?.GetComponent<TextMeshProUGUI>();\n                scoreKills[i]=row?.Find("Kills")?.GetComponent<TextMeshProUGUI>();')
s=replace(s,'            if(roundInfo==null || ammo==null', '''            revealWarning=PanelText(root,"NpcRevealWarning");
            for(int i=0;i<5;i++)killFeedLabels[i]=root.Find("KillFeed/Entry"+i)?.GetComponent<TextMeshProUGUI>();
            if(roundInfo==null || ammo==null''')
s=replace(s,'Array.Exists(scoreWins,t=>t==null) || Array.Exists(scoreRows,t=>t==null))','Array.Exists(scoreWins,t=>t==null) || Array.Exists(scoreKills,t=>t==null) ||\n                revealWarning==null || Array.Exists(killFeedLabels,t=>t==null) || Array.Exists(scoreRows,t=>t==null))')
s=replace(s,'{if(transform.Find("GameHUD_Canvas")==null)CreateCanvas();}', '''{if(transform.Find("GameHUD_Canvas")==null)CreateCanvas();BakeCombatLayout();}
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
        }''')
s=replace(s,'private void SetScoreRow(int index,string nickname,int wins,bool local,bool present)','private void SetScoreRow(int index,string nickname,int wins,bool local,bool present,int kills=0)')
s=replace(s,'scoreNames[index].text=nickname;scoreWins[index].text=wins+"승";','scoreNames[index].text=nickname;scoreWins[index].text=wins+"승";\n            scoreKills[index].text=kills.ToString();')
s=replace(s,'SetScoreRow(index++,row.Nickname,row.Score,row.ClientId==net.LocalClientId,true);','SetScoreRow(index++,row.Nickname,row.Score,row.ClientId==net.LocalClientId,true,row.Kills);')
s=replace(s,'            ApplyVisibility(action,showAmmo,counting,winner,boardHeld);','''            ApplyVisibility(action,showAmmo,counting,winner,boardHeld);
            double remaining=life!=null?life.RevealSecondsRemaining:0d;
            bool warningVisible=showAmmo && remaining>0d && !boardHeld;
            SetVisible(revealWarning,warningVisible);
            if(warningVisible)revealWarning.text="NPC를 잘못 사격했습니다. "+Math.Ceiling(remaining)+"초 동안 모습이 노출됩니다.";
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
            while(feedIndex<5)killFeedLabels[feedIndex++].gameObject.SetActive(false);''')
write(p,s)
print('HUD source written: authored Kills column, bounded 5-row kill feed, separate countdown warning.')