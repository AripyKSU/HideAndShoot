using TMPro;
using System;
using System.Collections.Generic;
using System.Linq;
using HideAndShoot.UI;
using HideAndShoot.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace HideAndShoot.Lobby
{
    public sealed class MenuLobbyUI : MonoBehaviour
    {
        // Keep the chosen name only while this game process is running.
        // A fresh launch should generate a new default, not load an old PlayerPrefs value.
        private static string sessionNickname;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSessionNickname()
        {
            sessionNickname=null;
        }

        public event Action CreateRequested;
        public event Action<string> JoinRequested;
        public event Action PlayRequested;
        public event Action LeaveRequested;
        public event Action RefreshRequested;
        private Button refreshButton,previousPage,nextPage;
        private TextMeshProUGUI pageText,browserStatus;
        private GameObject browserPanel;
        private readonly List<PublicRoom> publicRooms=new List<PublicRoom>();
        private readonly GameObject[] browserRows=new GameObject[8];
        private readonly TextMeshProUGUI[] browserCodes=new TextMeshProUGUI[8];
        private readonly TextMeshProUGUI[] browserCounts=new TextMeshProUGUI[8];
        private readonly TextMeshProUGUI[] browserStates=new TextMeshProUGUI[8];
        private readonly Button[] browserJoinButtons=new Button[8];
        private int roomPage;
        private bool refreshing;
        private TMP_InputField nickname,roomInput;
        private Button createButton,joinButton,playButton,copyButton,leaveButton;
        private GameObject roomOverlay;
        private RectTransform controlsPanel;
        private TextMeshProUGUI roomHeader,roomCodeLine,statusText,overlayStatus,roomReadiness;
        private readonly TextMeshProUGUI[] rosterNames=new TextMeshProUGUI[8];
        private readonly TextMeshProUGUI[] rosterPings=new TextMeshProUGUI[8];
        private readonly TextMeshProUGUI[] rosterHosts=new TextMeshProUGUI[8];
        private readonly Image[] rosterMarkers=new Image[8];
        // Restore exactly the authored/code-built menu visibility after a room
        // closes. Some legacy placeholders are deliberately inactive.
        private readonly Dictionary<GameObject,bool> preRoomVisibility=new Dictionary<GameObject,bool>();
        private bool busy,networkStarted,isHost,initialized;
        public string Nickname {get;private set;}
        public string RoomCode {get;private set;}=string.Empty;
        public bool IsInRoom=>roomOverlay!=null && roomOverlay.activeSelf;

        private void Awake()=>InitializeUI();

        public void InitializeUI()
        {
            if(initialized)return;
            nickname=Find<TMP_InputField>("NicknameInput");
            roomInput=Find<TMP_InputField>("RoomCodeInput");
            createButton=Find<Button>("CreateRoomButton");
            joinButton=Find<Button>("JoinRoomButton");
            playButton=Find<Button>("HostPlayButton");
            copyButton=Find<Button>("CopyCodeButton");
            roomOverlay=FindObject("RoomOverlay");
            roomHeader=Find<TextMeshProUGUI>("PeopleAndCode");
            roomCodeLine=Find<TextMeshProUGUI>("RoomCodeSameRow");
            roomReadiness=Find<TextMeshProUGUI>("RoomReadiness");
            overlayStatus=Find<TextMeshProUGUI>("RoomStatusText");
            leaveButton=Find<Button>("LeaveRoomButton");
            statusText=Find<TextMeshProUGUI>("MenuStatusText")??Find<TextMeshProUGUI>("NetworkSetupHint");
            controlsPanel=Find<RectTransform>("MenuControlsPanel");
            if(nickname==null || roomInput==null || createButton==null || joinButton==null ||
                playButton==null || copyButton==null || leaveButton==null ||
                roomOverlay==null || roomHeader==null || roomCodeLine==null ||
                roomReadiness==null || controlsPanel==null || statusText==null)
            {
                Debug.LogError("[HideAndShoot/Menu] Required menu controls missing.");
                enabled=false;return;
            }
            initialized=true;
            BindRosterRows();
            BindRoomBrowser();
            nickname.characterLimit=NicknameRules.MaxCharacters;
            if(!NicknameRules.TryNormalize(sessionNickname,out var remembered,out _))
                remembered=NicknameRules.Generate();
            nickname.text=remembered;
            sessionNickname=remembered;
            nickname.onValueChanged.AddListener(_=>ValidateInputs());
            roomInput.characterLimit=18;
            roomInput.onValueChanged.AddListener(_=>ValidateInputs());
            roomInput.onSubmit.AddListener(value=>{if(joinButton.interactable)Join();});
            createButton.onClick.AddListener(()=>{if(TryGetNickname(out _))CreateRequested?.Invoke();});
            joinButton.onClick.AddListener(Join);
            playButton.onClick.AddListener(()=>{if(playButton.interactable && isHost)PlayRequested?.Invoke();});
            copyButton.onClick.AddListener(()=>
            {
                if(string.IsNullOrEmpty(RoomCode))return;
                GUIUtility.systemCopyBuffer=RoomCode;
                ShowStatus("방 코드가 복사되었습니다.");
            });
            leaveButton.onClick.AddListener(()=>LeaveRequested?.Invoke());
            roomOverlay.SetActive(false);
            SetBusy(false);
            ShowStatus("방을 만들거나 참가하세요.");
        }

        private void BindRosterRows()
        {
            for(int i=0;i<8;i++)
            {
                var row=FindObject("Member"+i)?.transform;
                if(row==null){Debug.LogError("[HideAndShoot/Menu] Missing Member"+i);enabled=false;return;}
                rosterNames[i]=row.Find("Nickname")?.GetComponent<TextMeshProUGUI>();
                rosterPings[i]=row.Find("Ping")?.GetComponent<TextMeshProUGUI>();
                rosterHosts[i]=row.Find("HostBadge")?.GetComponent<TextMeshProUGUI>();
                rosterMarkers[i]=row.Find("Marker")?.GetComponent<Image>();
                if(rosterNames[i]==null || rosterPings[i]==null ||
                   rosterHosts[i]==null || rosterMarkers[i]==null)
                {Debug.LogError("[HideAndShoot/Menu] Incomplete Member"+i);enabled=false;return;}
            }
        }

#if UNITY_EDITOR
        // Invoked from the one-time editor scene baker. The runtime never
        // creates or repositions menu controls; users can edit scene objects.
        public void BakeEditableLayout()
        {
            controlsPanel=Find<RectTransform>("MenuControlsPanel");
            roomOverlay=FindObject("RoomOverlay");
            roomHeader=Find<TextMeshProUGUI>("PeopleAndCode");
            statusText=Find<TextMeshProUGUI>("MenuStatusText");
            nickname=Find<TMP_InputField>("NicknameInput");
            roomInput=Find<TMP_InputField>("RoomCodeInput");
            createButton=Find<Button>("CreateRoomButton");
            joinButton=Find<Button>("JoinRoomButton");
            playButton=Find<Button>("HostPlayButton");
            copyButton=Find<Button>("CopyCodeButton");
            if(controlsPanel==null || roomOverlay==null || copyButton==null)
                throw new InvalidOperationException("Cannot bake missing menu scene layout.");
            BuildMainLayout();
            BuildRoomLayout();
            BakeRoomBrowserLayout();
            roomOverlay.SetActive(false);
        }
#endif

        private void BindRoomBrowser()
        {
            browserPanel=FindObject("PublicRoomBrowser");
            refreshButton=Find<Button>("RefreshRoomsButton");
            previousPage=Find<Button>("PreviousRoomPage");nextPage=Find<Button>("NextRoomPage");
            pageText=Find<TextMeshProUGUI>("RoomPageText");browserStatus=Find<TextMeshProUGUI>("RoomBrowserStatus");
            if(browserPanel==null || refreshButton==null || previousPage==null || nextPage==null)return;
            refreshButton.onClick.AddListener(()=>RefreshRequested?.Invoke());
            previousPage.onClick.AddListener(()=>{roomPage--;RenderRooms();});
            nextPage.onClick.AddListener(()=>{roomPage++;RenderRooms();});
            for(int i=0;i<8;i++)
            {
                int slot=i;
                var row=FindObject("PublicRoomRow"+i);
                browserRows[i]=row;
                if(row==null)continue;
                browserCodes[i]=row.transform.Find("Code").GetComponent<TextMeshProUGUI>();
                browserCounts[i]=row.transform.Find("Occupancy").GetComponent<TextMeshProUGUI>();
                browserStates[i]=row.transform.Find("State").GetComponent<TextMeshProUGUI>();
                browserJoinButtons[i]=row.transform.Find("Join").GetComponent<Button>();
                browserJoinButtons[i].onClick.AddListener(()=>
                {
                    int index=roomPage*8+slot;
                    if(!browserJoinButtons[slot].interactable || index>=publicRooms.Count || !TryGetNickname(out _))return;
                    JoinRequested?.Invoke(publicRooms[index].Code);
                });
            }
            RenderRooms();
        }

        public void DisplayRooms(IEnumerable<PublicRoom> rooms)
        {
            publicRooms.Clear();publicRooms.AddRange(rooms);roomPage=0;
            if(browserStatus!=null)browserStatus.text=publicRooms.Count==0?"현재 공개된 방이 없습니다.":publicRooms.Count+"개의 방";
            RenderRooms();
        }

        public void SetRefreshing(bool value)
        {
            refreshing=value;
            if(refreshButton!=null)refreshButton.GetComponentInChildren<TextMeshProUGUI>().text=value?"검색 중…":"새로고침";
            if(value && browserStatus!=null)browserStatus.text="방 목록을 불러오는 중…";
            RenderRooms();
        }

        public void ShowRoomSearchError(string message)
        {
            if(browserStatus!=null)browserStatus.text=message;
        }

        private void RenderRooms()
        {
            int pages=Mathf.Max(1,(publicRooms.Count+7)/8);
            roomPage=Mathf.Clamp(roomPage,0,pages-1);
            if(pageText!=null)pageText.text=(roomPage+1)+"/"+pages;
            bool available=!busy && !refreshing && !IsInRoom;
            if(refreshButton!=null){refreshButton.interactable=available;TacticalUI.RefreshButtonLabel(refreshButton);}
            if(previousPage!=null){previousPage.interactable=available && roomPage>0;TacticalUI.RefreshButtonLabel(previousPage);}
            if(nextPage!=null){nextPage.interactable=available && roomPage<pages-1;TacticalUI.RefreshButtonLabel(nextPage);}
            bool valid=nickname!=null && NicknameRules.TryNormalize(nickname.text,out _,out _);
            for(int i=0;i<8;i++)
            {
                if(browserRows[i]==null)continue;
                int index=roomPage*8+i;
                browserRows[i].SetActive(index<publicRooms.Count);
                if(index>=publicRooms.Count)continue;
                var room=publicRooms[index];
                browserCodes[i].text=room.Code;browserCounts[i].text=room.Players+"/"+room.Capacity;
                browserStates[i].text=room.Players>=room.Capacity?"가득 참":room.InMatch?"게임 중":"대기 중";
                var button=browserJoinButtons[i];
                button.interactable=available && valid && room.Players<room.Capacity;
                button.GetComponentInChildren<TextMeshProUGUI>().text=room.InMatch?"관전 참가":"참가";
                TacticalUI.RefreshButtonLabel(button);
            }
        }

#if UNITY_EDITOR
        /// <summary>Explicit scene authoring only; runtime binds the saved objects.</summary>
        public void BakeRoomBrowserLayout()
        {
            var scaler=GetComponent<CanvasScaler>();
            if(scaler!=null)
            {
                scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution=new Vector2(1366,768);
                scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight=.5f;
            }
            controlsPanel=Find<RectTransform>("MenuControlsPanel");
            nickname=Find<TMP_InputField>("NicknameInput");roomInput=Find<TMP_InputField>("RoomCodeInput");
            createButton=Find<Button>("CreateRoomButton");joinButton=Find<Button>("JoinRoomButton");
            statusText=Find<TextMeshProUGUI>("MenuStatusText");
            Layout(controlsPanel,new Vector2(1,1),new Vector2(-40,-120),new Vector2(640,608),new Vector2(1,1));
            Position("NicknameLabel",new Vector2(0,283),new Vector2(584,28));
            var nicknameLabel=Find<TextMeshProUGUI>("NicknameLabel");
            nicknameLabel.fontSize=16;nicknameLabel.textWrappingMode=TextWrappingModes.NoWrap;
            Position("NicknameInput",new Vector2(0,246),new Vector2(584,42));
            Position("CreateRoomButton",new Vector2(0,198),new Vector2(584,40));
            Position("NicknameDivider",new Vector2(0,164),new Vector2(584,1));
            TacticalUI.Input(nickname);
            TacticalUI.Button(createButton,true);TacticalUI.Button(joinButton);
            createButton.GetComponentInChildren<TextMeshProUGUI>().fontSize=20;
            joinButton.GetComponentInChildren<TextMeshProUGUI>().fontSize=18;
            nickname.textComponent.fontSize=20;
            if(nickname.placeholder is TextMeshProUGUI nicknameHint)nicknameHint.fontSize=18;
            FindObject("RoomCodeLabel")?.SetActive(false);
            FindObject("ConnectionHint")?.SetActive(false);
            // One final row: input (428) + gap (12) + join button (144).
            Position("RoomCodeInput",new Vector2(-78,-250),new Vector2(428,40));
            Position("JoinRoomButton",new Vector2(220,-250),new Vector2(144,40));
            TacticalUI.Input(roomInput);
            // Apply the compact field size AFTER the theme, which resets the font to 25.
            // At 25 the line exceeds the 36px viewport and Truncate hides the whole code.
            roomInput.textComponent.fontSize=18;
            if(roomInput.placeholder is TextMeshProUGUI codeHint)
            {codeHint.text="방 코드 입력";codeHint.fontSize=17;}
            Layout(statusText.rectTransform,new Vector2(.5f,.5f),new Vector2(0,-286),new Vector2(584,24));
            statusText.fontSize=14;statusText.textWrappingMode=TextWrappingModes.NoWrap;
            statusText.enableAutoSizing=true;statusText.fontSizeMin=11;statusText.fontSizeMax=14;
            statusText.alignment=TextAlignmentOptions.Midline;

            var existing=Find<RectTransform>("PublicRoomBrowser");
            if(existing!=null)existing.SetParent(controlsPanel,false);
            var panel=TacticalUI.Rect(controlsPanel,"PublicRoomBrowser",new Vector2(.5f,1),new Vector2(0,-152),
                new Vector2(584,372),new Vector2(.5f,1));
            // The browser shares the main panel's frame instead of adding a second box.
            panel.Find("TacticalFrame")?.gameObject.SetActive(false);
            TacticalUI.Text(panel,"BrowserTitle","공개 방 목록",new Vector2(-64,-18),new Vector2(456,32),
                20,TacticalUI.White,TextAnchor.MiddleLeft,new Vector2(.5f,1));
            var refresh=BrowserButton(panel,"RefreshRoomsButton","새로고침",new Vector2(238,-18),new Vector2(108,32));
            refresh.GetComponentInChildren<TextMeshProUGUI>().fontSize=15;
            TacticalUI.Text(panel,"CodeHeading","방 코드",new Vector2(-172,-50),new Vector2(240,24),14,TacticalUI.Muted,
                TextAnchor.MiddleLeft,new Vector2(.5f,1));
            TacticalUI.Text(panel,"OccupancyHeading","인원",new Vector2(-6,-50),new Vector2(72,24),14,TacticalUI.Muted,
                TextAnchor.MiddleCenter,new Vector2(.5f,1));
            TacticalUI.Text(panel,"StateHeading","상태",new Vector2(110,-50),new Vector2(120,24),14,TacticalUI.Muted,
                TextAnchor.MiddleCenter,new Vector2(.5f,1));
            for(int i=0;i<8;i++)
            {
                var row=TacticalUI.Rect(panel,"PublicRoomRow"+i,new Vector2(.5f,1),new Vector2(0,-78-i*30),new Vector2(584,28));
                var image=row.GetComponent<Image>()??row.gameObject.AddComponent<Image>();
                image.color=i%2==0?new Color(.09f,.12f,.14f,.7f):Color.clear;image.raycastTarget=false;
                TacticalUI.Text(row,"Code","------",new Vector2(-166,0),new Vector2(228,28),16,TacticalUI.White);
                TacticalUI.Text(row,"Occupancy","1/8",new Vector2(-6,0),new Vector2(72,28),16,TacticalUI.White,TextAnchor.MiddleCenter);
                TacticalUI.Text(row,"State","대기 중",new Vector2(110,0),new Vector2(120,28),14,TacticalUI.Muted,TextAnchor.MiddleCenter);
                var join=BrowserButton(row,"Join","참가",new Vector2(238,0),new Vector2(108,28),new Vector2(.5f,.5f));
                join.GetComponentInChildren<TextMeshProUGUI>().fontSize=14;
                row.gameObject.SetActive(false);
            }
            var searchStatus=TacticalUI.Text(panel,"RoomBrowserStatus","새로고침으로 방을 검색하세요.",new Vector2(0,-317),
                new Vector2(584,22),13,TacticalUI.Muted,TextAnchor.MiddleCenter,new Vector2(.5f,1));
            searchStatus.textWrappingMode=TextWrappingModes.NoWrap;searchStatus.enableAutoSizing=true;
            searchStatus.fontSizeMin=10;searchStatus.fontSizeMax=13;
            BrowserButton(panel,"PreviousRoomPage","←",new Vector2(-88,-350),new Vector2(64,30));
            TacticalUI.Text(panel,"RoomPageText","1/1",new Vector2(0,-350),new Vector2(100,30),17,TacticalUI.White,
                TextAnchor.MiddleCenter,new Vector2(.5f,1));
            BrowserButton(panel,"NextRoomPage","→",new Vector2(88,-350),new Vector2(64,30));
            FindObject("RoomOverlay")?.transform.SetAsLastSibling();

            var rules=Find<RectTransform>("RulesPanel");
            if(rules!=null)
            {
                Layout(rules,new Vector2(0,1),new Vector2(40,-172),new Vector2(380,466),new Vector2(0,1));
                var toggle=rules.Find("GuideToggle");var body=rules.Find("GuideContent");
                if(toggle!=null && body!=null)
                {
                    Layout((RectTransform)toggle,new Vector2(.5f,1),new Vector2(0,-28),new Vector2(352,40));
                    rules.GetComponent<ControlsGuideToggle>().Configure(toggle.GetComponent<Button>(),body.gameObject,rules,
                        new Vector2(380,466),new Vector2(380,56),true);
                }
            }
        }

        private static Button BrowserButton(Transform parent,string name,string caption,Vector2 position,Vector2 size,
            Vector2? anchor=null)
        {
            var rect=TacticalUI.Rect(parent,name,anchor??new Vector2(.5f,1),position,size);
            var image=rect.GetComponent<Image>()??rect.gameObject.AddComponent<Image>();
            var button=rect.GetComponent<Button>()??rect.gameObject.AddComponent<Button>();
            button.onClick.RemoveAllListeners();
            TacticalUI.Text(rect,"Text",caption,Vector2.zero,size,17,TacticalUI.White,TextAnchor.MiddleCenter);
            TacticalUI.Button(button);
            var label=button.GetComponentInChildren<TextMeshProUGUI>();
            label.fontSize=17;label.textWrappingMode=TextWrappingModes.NoWrap;
            label.rectTransform.offsetMin=new Vector2(8,1);label.rectTransform.offsetMax=new Vector2(-8,-1);
            return button;
        }
#endif

        private void Join()
        {
            if(joinButton.interactable && TryGetNickname(out _))
                JoinRequested?.Invoke(roomInput.text.Trim().ToUpperInvariant());
        }

        private void BuildMainLayout()
        {
            var scaler=GetComponent<CanvasScaler>();if(scaler!=null)scaler.referenceResolution=new Vector2(1366,768);
            var backdrop=TacticalUI.Rect(transform,"MenuVignette",new Vector2(.5f,.5f),Vector2.zero,Vector2.zero);
            TacticalUI.Stretch(backdrop);backdrop.SetAsFirstSibling();
            var shade=backdrop.GetComponent<Image>()??backdrop.gameObject.AddComponent<Image>();
            shade.color=new Color(.015f,.025f,.035f,.45f);shade.raycastTarget=false;
            var title=Find<TextMeshProUGUI>("GameTitle");
            if(title!=null)
            {
                Layout(title.rectTransform,new Vector2(0,1),new Vector2(40,-34),new Vector2(1220,100),new Vector2(0,1));
                title.text="HIDE AND SHOOT";title.fontSize=56;title.fontStyle=FontStyles.Bold;
                title.color=TacticalUI.White;title.alignment=TextAlignmentOptions.MidlineLeft;title.raycastTarget=false;
            }
            foreach(var name in new[]{"MenuSubtitle","MenuOnline","MenuTopRule"})
            {var old=FindObject(name);if(old!=null)old.SetActive(false);}
            var rules=Find<RectTransform>("RulesPanel");
            Layout(rules,new Vector2(0,1),new Vector2(40,-172),new Vector2(380,466),new Vector2(0,1));
            foreach(var child in rules.Cast<Transform>().ToArray())
                if(child.name!="TacticalFrame")DestroyImmediate(child.gameObject);
            TacticalUI.Frame(rules,TacticalUI.Muted);
            var header=TacticalUI.Rect(rules,"GuideToggle",new Vector2(.5f,1),new Vector2(0,-28),new Vector2(352,40));
            header.gameObject.AddComponent<Image>();var toggle=header.gameObject.AddComponent<Button>();
            TacticalUI.Text(header,"Text","",Vector2.zero,new Vector2(332,36),20,TacticalUI.White);
            TacticalUI.Button(toggle);header.GetComponentInChildren<TextMeshProUGUI>().fontSize=20;
            var body=TacticalUI.Rect(rules,"GuideContent",new Vector2(.5f,1),new Vector2(0,-64),
                new Vector2(340,390),new Vector2(.5f,1));
            ControlsGuideToggle.CreateRows(body);
            TacticalUI.Text(body,"Goal","군중 속 상대를 찾아 살아남으세요.\n3승을 먼저 거두면 우승합니다.",
                new Vector2(0,40),new Vector2(340,64),17,TacticalUI.Muted,TextAnchor.MiddleLeft,new Vector2(.5f,0));
            var guide=rules.GetComponent<ControlsGuideToggle>()??rules.gameObject.AddComponent<ControlsGuideToggle>();
            guide.Configure(toggle,body.gameObject,rules,new Vector2(380,466),new Vector2(380,56),true);
            Layout(controlsPanel,new Vector2(1,1),new Vector2(-40,-172),new Vector2(600,536),new Vector2(1,1));
            TacticalUI.Frame(controlsPanel,TacticalUI.Muted);
            Position("NicknameLabel",new Vector2(0,205),new Vector2(544,32));
            var label=Find<TextMeshProUGUI>("NicknameLabel");label.text="닉네임";label.fontSize=18;label.color=TacticalUI.Muted;
            Position("NicknameInput",new Vector2(0,148),new Vector2(544,52));
            Position("CreateRoomButton",new Vector2(0,76),new Vector2(544,50));
            Position("NicknameDivider",new Vector2(0,30),new Vector2(544,1));
            Find<Image>("NicknameDivider").color=new Color(.35f,.43f,.47f,.20f);
            Position("RoomCodeLabel",new Vector2(0,-6),new Vector2(544,32));
            label=Find<TextMeshProUGUI>("RoomCodeLabel");label.text="방 코드로 참가";label.fontSize=18;label.color=TacticalUI.Muted;
            Position("RoomCodeInput",new Vector2(0,-56),new Vector2(544,52));
            Position("JoinRoomButton",new Vector2(0,-124),new Vector2(544,50));
            TacticalUI.Text(controlsPanel,"ConnectionHint","2–8명 온라인",
                new Vector2(0,-185),new Vector2(544,26),16,TacticalUI.Muted,TextAnchor.MiddleCenter);
            if(statusText!=null)
            {
                Layout(statusText.rectTransform,new Vector2(.5f,.5f),new Vector2(0,-224),new Vector2(544,44));
                statusText.fontSize=16;statusText.color=TacticalUI.White;statusText.alignment=TextAlignmentOptions.Midline;
            }
            TacticalUI.Input(nickname);TacticalUI.Input(roomInput);
            TacticalUI.Button(createButton,true);TacticalUI.Button(joinButton);
            foreach(var input in new[]{nickname,roomInput})
            {input.textComponent.fontSize=22;if(input.placeholder is TextMeshProUGUI hint)hint.fontSize=18;}
        }
        private void BuildRoomLayout()
        {
            var room=(RectTransform)roomOverlay.transform;
            Layout(room,new Vector2(.5f,.5f),Vector2.zero,new Vector2(600,592));
            TacticalUI.Frame(room,TacticalUI.Muted);room.SetAsLastSibling();
            foreach(var legacy in new[]{"ConnectedPlayers","PingColumn"})
            {var go=FindObject(legacy);if(go!=null)go.SetActive(false);}
            Layout(roomHeader.rectTransform,new Vector2(.5f,.5f),new Vector2(0,250),new Vector2(544,38));
            roomHeader.fontSize=26;roomHeader.fontStyle=FontStyles.Bold;roomHeader.color=TacticalUI.White;
            roomHeader.alignment=TextAlignmentOptions.MidlineLeft;roomHeader.richText=false;
            roomCodeLine=TacticalUI.Text(room,"RoomCodeSameRow","방 코드  ------",new Vector2(-70,195),
                new Vector2(404,46),20,TacticalUI.White);
            Layout((RectTransform)copyButton.transform,new Vector2(.5f,.5f),new Vector2(216,195),new Vector2(112,42));
            copyButton.GetComponentInChildren<TextMeshProUGUI>().text="코드 복사";
            TacticalUI.Button(copyButton);copyButton.GetComponentInChildren<TextMeshProUGUI>().fontSize=17;
            TacticalUI.Text(room,"RosterHeading","참가자",new Vector2(-60,150),new Vector2(424,28),15,TacticalUI.Muted);
            TacticalUI.Text(room,"LatencyHeading","ping",new Vector2(234,150),new Vector2(76,28),14,TacticalUI.Muted,TextAnchor.MiddleRight);
            var grid=TacticalUI.Rect(room,"RosterGrid",new Vector2(.5f,.5f),new Vector2(0,-5),new Vector2(544,272));
            if(grid.GetComponent<RectMask2D>()==null)grid.gameObject.AddComponent<RectMask2D>();
            for(int i=0;i<8;i++)
            {
                var row=TacticalUI.Rect(grid,"Member"+i,new Vector2(.5f,1),new Vector2(0,-17-i*34),new Vector2(544,32));
                var background=row.GetComponent<Image>()??row.gameObject.AddComponent<Image>();
                background.color=i%2==0?new Color(.09f,.12f,.14f,.7f):Color.clear;background.raycastTarget=false;
                rosterMarkers[i]=TacticalUI.Stripe(row,"Marker",new Vector2(0,.5f),Vector2.zero,new Vector2(2,20),TacticalUI.Muted,new Vector2(0,.5f));
                TacticalUI.Text(row,"Slot",(i+1).ToString(),new Vector2(-249,0),new Vector2(24,30),14,TacticalUI.Muted,TextAnchor.MiddleCenter);
                rosterNames[i]=TacticalUI.Text(row,"Nickname","빈 자리",new Vector2(-49,0),new Vector2(352,30),18,TacticalUI.White);
                rosterNames[i].textWrappingMode=TextWrappingModes.NoWrap;rosterNames[i].enableAutoSizing=true;
                rosterNames[i].fontSizeMin=12;rosterNames[i].fontSizeMax=18;
                rosterHosts[i]=TacticalUI.Text(row,"HostBadge","",new Vector2(163,0),new Vector2(54,30),14,TacticalUI.Gold,TextAnchor.MiddleCenter);
                rosterPings[i]=TacticalUI.Text(row,"Ping","",new Vector2(234,0),new Vector2(70,30),14,TacticalUI.Muted,TextAnchor.MiddleRight);
            }
            roomReadiness=TacticalUI.Text(room,"RoomReadiness","2명 이상 모이면 시작할 수 있습니다.",
                new Vector2(0,-165),new Vector2(544,30),16,TacticalUI.Muted,TextAnchor.MiddleCenter);
            Layout((RectTransform)playButton.transform,new Vector2(.5f,.5f),new Vector2(-140,-208),new Vector2(264,50));
            playButton.GetComponentInChildren<TextMeshProUGUI>().text="경기 시작";TacticalUI.Button(playButton,true);
            var leave=room.Find("LeaveRoomButton");
            if(leave==null){leave=Instantiate(copyButton.gameObject,room).transform;leave.name="LeaveRoomButton";}
            leaveButton=leave.GetComponent<Button>();leaveButton.onClick.RemoveAllListeners();
            leaveButton.GetComponent<HideAndShoot.UI.TacticalButtonFeedback>()?.BindClick();
            Layout((RectTransform)leave,new Vector2(.5f,.5f),new Vector2(140,-208),new Vector2(264,50));
            leaveButton.GetComponentInChildren<TextMeshProUGUI>().text="방 나가기";TacticalUI.Button(leaveButton);
            overlayStatus=TacticalUI.Text(room,"RoomStatusText","",new Vector2(0,-265),new Vector2(544,32),15,TacticalUI.White,TextAnchor.MiddleCenter);
        }
        private static void Layout(RectTransform rect,Vector2 anchor,Vector2 position,Vector2 size,Vector2? pivot=null)
        {
            rect.localScale=Vector3.one;rect.anchorMin=rect.anchorMax=anchor;rect.pivot=pivot??new Vector2(.5f,.5f);
            rect.anchoredPosition=position;rect.sizeDelta=size;
        }
        private void Position(string name,Vector2 position,Vector2 size)=>Layout(Find<RectTransform>(name),
            new Vector2(.5f,.5f),position,size);
        private T Find<T>(string name) where T:Component=>FindObject(name)?.GetComponent<T>();
        private GameObject FindObject(string name)=>GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(t=>t.name==name)?.gameObject;

        public bool TryGetNickname(out string normalized)
        {
            if(!NicknameRules.TryNormalize(nickname.text,out normalized,out string reason))
            {ShowStatus(reason);return false;}
            Nickname=normalized;
            sessionNickname=normalized;
            nickname.text=normalized;return true;
        }
        private void ValidateInputs()
        {
            if(createButton==null || joinButton==null)return;
            bool valid=NicknameRules.TryNormalize(nickname.text,out _,out _);
            createButton.interactable=!busy && !IsInRoom && valid;
            joinButton.interactable=!busy && !IsInRoom && valid && !string.IsNullOrWhiteSpace(roomInput.text);
            TacticalUI.RefreshButtonLabel(createButton);TacticalUI.RefreshButtonLabel(joinButton);
            RenderRooms();
        }
        public void SetBusy(bool value)
        {
            busy=value;nickname.interactable=!busy && !IsInRoom;roomInput.interactable=!busy && !IsInRoom;
            createButton.GetComponentInChildren<TextMeshProUGUI>().text=busy?"연결 중…":"새 방 만들기";
            joinButton.GetComponentInChildren<TextMeshProUGUI>().text=busy?"연결 중…":"방 참가";
            ValidateInputs();
        }
        public void DisplayRoom(string code,bool host)
        {
            RoomCode=code;isHost=host;SetBaseControlsVisible(false);
            if(browserPanel!=null)browserPanel.SetActive(false);
            roomOverlay.SetActive(true);playButton.gameObject.SetActive(host);playButton.interactable=false;
            Layout((RectTransform)leaveButton.transform,new Vector2(.5f,.5f),
                new Vector2(host?140:0,-208),new Vector2(host?264:544,50));
            SetBusy(false);ShowStatus(host?"방을 만들었습니다. 참가자를 기다리는 중…":"방에 참가했습니다.");
        }
        public void DisplayRoster(string[] names,string[] pings,bool[] hosts,int currentCount,
            int maxCount,bool host,bool gameStarted)
        {
            roomHeader.text="대기실  ·  "+currentCount+" / "+maxCount;
            roomCodeLine.text="방 코드   "+RoomCode;
            for(int i=0;i<8;i++)
            {
                bool present=i<currentCount && names!=null && i<names.Length;
                rosterNames[i].text=present?names[i]:"빈 자리";
                rosterNames[i].color=present?TacticalUI.White:new Color(.30f,.43f,.47f);
                bool owner=present && hosts!=null && i<hosts.Length && hosts[i];
                rosterHosts[i].text=owner?"방장":"";
                string ping=present && pings!=null && i<pings.Length?(pings[i]??string.Empty):string.Empty;
                if(ping.EndsWith(" ms") && ulong.TryParse(ping.Replace(" ms",""),out ulong latency) && latency>999)
                    ping="999+ ms";
                rosterPings[i].text=ping;
                rosterMarkers[i].color=owner?TacticalUI.Gold:present?TacticalUI.Cyan:new Color(.18f,.28f,.31f);
            }
            networkStarted=gameStarted;isHost=host;
            playButton.interactable=isHost && currentCount>=2 && !networkStarted;
            TacticalUI.RefreshButtonLabel(playButton);
            roomReadiness.text=gameStarted?"경기를 준비하는 중…":currentCount<2?
                "2명 이상 모이면 시작할 수 있습니다.":host?
                currentCount+"명 연결됨  ·  경기를 시작할 수 있습니다.":"방장이 경기를 시작할 때까지 기다려주세요.";
            roomReadiness.color=currentCount>=2?TacticalUI.Cyan:TacticalUI.Muted;
        }
        public void HideRoom(string message="")
        {
            RoomCode=string.Empty;isHost=false;networkStarted=false;
            roomOverlay.SetActive(false);
            SetBaseControlsVisible(true);
            if(browserPanel!=null)browserPanel.SetActive(true);
            // The menu hierarchy and styling are authored in the scene;
            // restoring the pre-room visibility must not change that layout.
            SetBusy(false);
            if(!string.IsNullOrEmpty(message))ShowStatus(message);
        }
        private void SetBaseControlsVisible(bool visible)
        {
            if(!visible)
            {
                // DisplayRoom can be called again while already in a room.
                // Never overwrite the original snapshot with hidden states.
                if(preRoomVisibility.Count>0)return;
                foreach(Transform child in controlsPanel)
                {
                    if(IsPersistentPanelChild(child))continue;
                    preRoomVisibility[child.gameObject]=child.gameObject.activeSelf;
                    child.gameObject.SetActive(false);
                }
            }
            else
            {
                foreach(var item in preRoomVisibility)
                    if(item.Key!=null)item.Key.SetActive(item.Value);
                preRoomVisibility.Clear();
            }
        }
        private static bool IsPersistentPanelChild(Transform child)=>child.name=="RoomOverlay";
        public void ShowStatus(string message)
        {
            if(statusText!=null)statusText.text=message;
            if(overlayStatus!=null)overlayStatus.text=message;
        }
    }
}
