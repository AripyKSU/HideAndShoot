#if HIDEANDSHOOT_PODIUM_BUILD_QA
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using HideAndShoot.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace HideAndShoot.UI
{
    // Included only in the opt-in QA build, never in ordinary player builds.
    public sealed class PodiumBuildProbe : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if(!Environment.GetCommandLineArgs().Contains("-hs-podium-qa"))return;
            var root=new GameObject("PodiumBuildProbe");
            DontDestroyOnLoad(root);
            root.AddComponent<PodiumBuildProbe>();
        }

        private IEnumerator Start()
        {
            SceneManager.LoadScene("GameScene");
            yield return null;
            yield return new WaitForSecondsRealtime(2);
            var report=new StringBuilder("Standalone release podium regression\n");
            var folder=Path.GetDirectoryName(Application.dataPath);
            int exitCode=1;
            try
            {
                var primitive=GameObject.CreatePrimitive(PrimitiveType.Cube);
                var defaultMaterial=primitive.GetComponent<Renderer>().sharedMaterial;
                report.AppendLine("DefaultPrimitiveShader="+(defaultMaterial!=null && defaultMaterial.shader!=null?
                    defaultMaterial.shader.name:"missing"));
                Destroy(primitive);
                var presenter=FindFirstObjectByType<MatchPodiumPresenter>();
                if(presenter==null)throw new Exception("Authored podium presenter missing");
                foreach(var scaler in FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                {
                    report.AppendLine("Canvas "+scaler.name+" reference="+scaler.referenceResolution);
                    foreach(Transform child in scaler.transform)
                    {
                        if(child is RectTransform rect)
                            report.AppendLine("  "+child.name+" anchor="+rect.anchorMin+" pivot="+rect.pivot+
                                " position="+rect.anchoredPosition+" size="+rect.sizeDelta);
                    }
                }
                presenter.ShowPodium(new[]{
                    new RelayLobbySession.ScoreRow(0,"Champion",3,0,0,true,true),
                    new RelayLobbySession.ScoreRow(1,"Runner Up",2,1,1,true,true),
                    new RelayLobbySession.ScoreRow(2,"Third Place",1,2,2,true,true)});
                var stage=presenter.transform.Find("MatchPodium_3D");
                if(stage==null)throw new Exception("Podium stage missing");
                var blocks=stage.GetComponentsInChildren<MeshRenderer>().Where(r=>
                    r.name=="StageFloor" || r.name.StartsWith("Podium_") ||
                    r.name.StartsWith("RankAccent_") || r.name.StartsWith("StageRail_")).ToArray();
                if(blocks.Length!=10)throw new Exception("Expected 10 stage blocks, got "+blocks.Length);
                foreach(var block in blocks)
                {
                    var material=block.sharedMaterial;
                    if(material==null || material.shader==null || !material.shader.isSupported ||
                        material.shader.name!="Universal Render Pipeline/Lit" || !material.IsKeywordEnabled("_EMISSION"))
                        throw new Exception("Invalid built material on "+block.name);
                    report.AppendLine("PASS | "+block.name+" URP Lit, supported, emission included");
                }
                var overlay=presenter.transform.Find("PodiumOverlay");
                if(overlay==null || !overlay.gameObject.activeSelf)throw new Exception("Podium overlay missing");
                report.AppendLine("PASS | Authored result overlay active");
                var camera=stage.GetComponentInChildren<Camera>();
                var previous=RenderTexture.active;
                var original=camera.targetTexture;
                var target=new RenderTexture(1366,768,24);
                var texture=new Texture2D(1366,768,TextureFormat.RGB24,false);
                try
                {
                    camera.targetTexture=target;camera.aspect=1366f/768;
                    camera.Render();camera.Render();
                    RenderTexture.active=target;
                    texture.ReadPixels(new Rect(0,0,1366,768),0,0);texture.Apply();
                    var pixels=texture.GetPixels32();
                    int magenta=pixels.Count(p=>p.r>200 && p.b>200 && p.g<80);
                    int visible=pixels.Count(p=>p.r>50 || p.g>50 || p.b>50);
                    report.AppendLine("MagentaPixels="+magenta+" VisiblePixels="+visible);
                    File.WriteAllBytes(Path.Combine(folder,"PodiumBuild_1366x768.png"),texture.EncodeToPNG());
                    if(magenta>10 || visible<5000)throw new Exception("Broken or blank podium render");
                    report.AppendLine("PASS | Real player render contains no error-magenta stage");
                    exitCode=0;
                }
                finally
                {
                    camera.targetTexture=original;RenderTexture.active=previous;
                    Destroy(texture);Destroy(target);
                }
            }
            catch(Exception error){report.AppendLine("FAIL | "+error);Debug.LogException(error);}
            File.WriteAllText(Path.Combine(folder,"PodiumBuildRegression.txt"),report.ToString());
            Debug.Log(report.ToString());
            Application.Quit(exitCode);
        }
    }
}
#endif