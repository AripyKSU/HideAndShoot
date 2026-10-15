if(UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)throw new System.Exception("Preview must stay in edit mode.");
var ui=UnityEngine.Object.FindFirstObjectByType<HideAndShoot.Lobby.MenuLobbyUI>();
var preview=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
var rt=new UnityEngine.RenderTexture(1366,768,24);
var prior=UnityEngine.RenderTexture.active;
UnityEngine.Texture2D pixels=null;
try
{
    var cameraRoot=new UnityEngine.GameObject("LayoutPreviewCamera",typeof(UnityEngine.Camera));
    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraRoot,preview);
    var camera=cameraRoot.GetComponent<UnityEngine.Camera>();
    camera.scene=preview;camera.clearFlags=UnityEngine.CameraClearFlags.SolidColor;
    camera.backgroundColor=new UnityEngine.Color(.065f,.09f,.11f);camera.orthographic=true;
    camera.transform.position=new UnityEngine.Vector3(0,0,-10);camera.targetTexture=rt;
    var clone=UnityEngine.Object.Instantiate(ui.gameObject);
    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(clone,preview);
    UnityEngine.Object.DestroyImmediate(clone.GetComponent<HideAndShoot.Lobby.MenuLobbyUI>());
    foreach(var guide in clone.GetComponentsInChildren<HideAndShoot.UI.ControlsGuideToggle>(true))guide.enabled=false;
    var canvas=clone.GetComponent<UnityEngine.Canvas>();
    canvas.renderMode=UnityEngine.RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1f;
    var scaler=clone.GetComponent<UnityEngine.UI.CanvasScaler>();
    scaler.enabled=false;canvas.scaleFactor=1f;
    var all=clone.GetComponentsInChildren<UnityEngine.Transform>(true);
    all.First(x=>x.name=="RoomOverlay").gameObject.SetActive(false);
    for(int i=0;i<8;i++)
    {
        var row=all.First(x=>x.name=="PublicRoomRow"+i);row.gameObject.SetActive(true);
        row.Find("Code").GetComponent<TMPro.TextMeshProUGUI>().text="AB"+(1234+i);
        row.Find("Occupancy").GetComponent<TMPro.TextMeshProUGUI>().text=(i+1)+"/8";
        row.Find("State").GetComponent<TMPro.TextMeshProUGUI>().text=i==7?"가득 참":i==2?"게임 중":"대기 중";
        row.Find("Join").GetComponent<UnityEngine.UI.Button>().interactable=i!=7;
        row.Find("Join/Text").GetComponent<TMPro.TextMeshProUGUI>().text=i==2?"관전 참가":"참가";
    }
    all.First(x=>x.name=="RoomPageText").GetComponent<TMPro.TextMeshProUGUI>().text="1/2";
    all.First(x=>x.name=="RoomBrowserStatus").GetComponent<TMPro.TextMeshProUGUI>().text="12개의 방";
    all.First(x=>x.name=="MenuStatusText").GetComponent<TMPro.TextMeshProUGUI>().text="방을 만들거나 참가하세요.";
    var nickname=all.First(x=>x.name=="NicknameInput").GetComponent<TMPro.TMP_InputField>();
    nickname.text="추운 기린";
    var roomCode=all.First(x=>x.name=="RoomCodeInput").GetComponent<TMPro.TMP_InputField>();
    roomCode.SetTextWithoutNotify("AB1234");roomCode.ForceLabelUpdate();
    foreach(var text in clone.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))text.ForceMeshUpdate(true,true);
    UnityEngine.Canvas.ForceUpdateCanvases();
    camera.Render();UnityEngine.RenderTexture.active=rt;
    pixels=new UnityEngine.Texture2D(1366,768,UnityEngine.TextureFormat.RGB24,false);
    pixels.ReadPixels(new UnityEngine.Rect(0,0,1366,768),0,0);pixels.Apply();
    var path="C:/UnityProject/HideAndShoot/Temp/RightLobby_1366x768.png";
    System.IO.File.WriteAllBytes(path,UnityEngine.ImageConversion.EncodeToPNG(pixels));
    // Check the projected rectangles in the requested reference resolution.
    var names=new[]{"MenuControlsPanel","NicknameInput","CreateRoomButton","PublicRoomBrowser","PublicRoomRow7","PreviousRoomPage","RoomPageText","NextRoomPage","RoomCodeInput","JoinRoomButton","MenuStatusText"};
    var geometry=names.Select(name=>
    {
        var rect=(UnityEngine.RectTransform)all.First(x=>x.name==name);
        var corners=new UnityEngine.Vector3[4];rect.GetWorldCorners(corners);
        var points=corners.Select(x=>UnityEngine.RectTransformUtility.WorldToScreenPoint(camera,x)).ToArray();
        return new {name,left=points.Min(x=>x.x),right=points.Max(x=>x.x),bottom=points.Min(x=>x.y),top=points.Max(x=>x.y)};
    }).ToArray();
    return new {path,width=1366,height=768,playing=UnityEditor.EditorApplication.isPlaying,
        roomCodeFontSize=roomCode.textComponent.fontSize,
        visibleCodeGlyphs=roomCode.textComponent.textInfo.characterInfo.Take(roomCode.textComponent.textInfo.characterCount).Count(x=>x.isVisible),geometry};
}
finally
{
    UnityEngine.RenderTexture.active=prior;
    if(pixels!=null)UnityEngine.Object.DestroyImmediate(pixels);
    rt.Release();UnityEngine.Object.DestroyImmediate(rt);
    UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview);
}
