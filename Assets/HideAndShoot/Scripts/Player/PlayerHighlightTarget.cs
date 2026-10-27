using System.Collections.Generic;
using HideAndShoot.Combat;
using HideAndShoot.Networking;
using HideAndShoot.UI;
using HideAndShoot.World;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace HideAndShoot.Player
{
    /// <summary>Local presentation only. Server-owned reveal state stays on HostPlayerLife.</summary>
    public sealed class PlayerHighlightTarget : MonoBehaviour
    {
        public readonly struct DrawPart
        {
            public readonly Renderer Renderer;
            public readonly int Submeshes;
            public DrawPart(Renderer renderer,int submeshes){Renderer=renderer;Submeshes=submeshes;}
        }
        private static readonly List<PlayerHighlightTarget> targets=new List<PlayerHighlightTarget>(8);
        private static readonly RaycastHit[] rayHits=new RaycastHit[32];
        private readonly List<DrawPart> parts=new List<DrawPart>();
        private HostPlayerLife life;
        private PistolCombatController pistol;
        private PlayerDetectionVision vision;
        private Transform head;
        private TextMeshPro nameLabel;
        private Material nameMaterial;
#if UNITY_EDITOR
        // Used only by deterministic edit-mode render checks; never serialized into a build.
        public static Camera PreviewCamera;
        public bool PreviewTarget;
        public string PreviewNickname;
#endif
        private void Awake()
        {
            life=GetComponent<HostPlayerLife>();pistol=GetComponent<PistolCombatController>();
            vision=GetComponent<PlayerDetectionVision>();
            var body=transform.Find("WorldVisual");
            if(body==null)return;
            var animator=body.GetComponentInChildren<Animator>(true);
            if(animator!=null && animator.isHuman)head=animator.GetBoneTransform(HumanBodyBones.Head);
            foreach(var renderer in body.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh=renderer is SkinnedMeshRenderer skin?skin.sharedMesh:
                    renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if(mesh!=null)parts.Add(new DrawPart(renderer,mesh.subMeshCount));
            }
        }
        private void OnEnable()
        {
            if(targets.Count==0)RenderPipelineManager.beginCameraRendering+=PrepareNames;
            if(!targets.Contains(this))targets.Add(this);
        }
        private void OnDisable()
        {
            targets.Remove(this);
            if(targets.Count==0)RenderPipelineManager.beginCameraRendering-=PrepareNames;
            if(nameLabel!=null)nameLabel.gameObject.SetActive(false);
        }
        private void OnDestroy()
        {
            if(nameLabel!=null)CoreUtils.Destroy(nameLabel.gameObject);
            if(nameMaterial!=null)CoreUtils.Destroy(nameMaterial);
        }
        public static bool IsSpectator(Camera camera)
        {
#if UNITY_EDITOR
            if(camera!=null && camera==PreviewCamera)return true;
#endif
            var session=RelayLobbySession.Instance;
            var net=NetworkManager.Singleton;
            if(camera==null || camera.cameraType!=CameraType.Game || net==null || !net.IsClient ||
                session==null || !session.IsMatchInProgress ||
                (session.MatchPhase!=(int)HostRoundSetupCoordinator.HostPhase.Active &&
                 session.MatchPhase!=(int)HostRoundSetupCoordinator.HostPhase.Result))return false;
            foreach(var target in targets)
                if(target.life!=null && target.life.IsSpawned && target.life.IsOwner)
                    return target.life.IsDead.Value;
            return true; // a late joiner has a spectator camera without a player avatar
        }
        public static void Collect(Camera camera,List<DrawPart> destination)
        {
            destination.Clear();
            bool spectator=IsSpectator(camera);
            var session=RelayLobbySession.Instance;
            bool detection=false;
            if(!spectator && session!=null && session.MatchPhase==(int)HostRoundSetupCoordinator.HostPhase.Active)
                foreach(var observer in targets)
                    if(observer.life!=null && observer.life.IsOwner && observer.pistol!=null &&
                       observer.pistol.RoundId.Value==session.MatchRoundId && observer.vision!=null &&
                       observer.vision.CanSeePlayers(camera)){detection=true;break;}
            foreach(var target in targets)
            {
                bool draw=false;
#if UNITY_EDITOR
                draw=camera==PreviewCamera && target.PreviewTarget;
#endif
                if(!draw)
                {
                    if(session==null || (session.MatchPhase!=(int)HostRoundSetupCoordinator.HostPhase.Active &&
                        session.MatchPhase!=(int)HostRoundSetupCoordinator.HostPhase.Result))continue;
                    if(target.life==null || !target.life.IsSpawned || target.life.IsDead.Value || target.life.IsOwner ||
                        target.pistol==null || target.pistol.RoundId.Value!=session.MatchRoundId)continue;
                    // Union the reasons once; expiry of one reason never clears another.
                    draw=spectator || detection || target.life.RevealSecondsRemaining>0d;
                }
                if(!draw)continue;
                foreach(var part in target.parts)
                    if(part.Renderer!=null && part.Renderer.enabled && part.Renderer.gameObject.activeInHierarchy &&
                        (camera.cullingMask&(1<<part.Renderer.gameObject.layer))!=0)
                        destination.Add(part);
            }
        }
        private static void PrepareNames(ScriptableRenderContext context,Camera camera)
        {
            bool spectator=IsSpectator(camera);
            foreach(var target in targets)target.UpdateName(camera,spectator);
        }
        private void UpdateName(Camera camera,bool spectator)
        {
            bool visible=spectator && life!=null && life.IsSpawned && !life.IsDead.Value && !life.IsOwner &&
                pistol!=null && RelayLobbySession.Instance!=null && pistol.RoundId.Value==RelayLobbySession.Instance.MatchRoundId;
#if UNITY_EDITOR
            bool preview=camera==PreviewCamera && PreviewTarget && !string.IsNullOrEmpty(PreviewNickname);
            visible|=preview;
#endif
            Vector3 headPosition=head!=null?head.position:transform.position+Vector3.up*1.72f;
            Vector3 viewport=camera.WorldToViewportPoint(headPosition);
            visible=visible && viewport.z>camera.nearClipPlane && viewport.x>=0 && viewport.x<=1 && viewport.y>=0 && viewport.y<=1;
            if(visible)
            {
                Vector3 ray=headPosition-camera.transform.position;
                int count=Physics.RaycastNonAlloc(camera.transform.position,ray.normalized,rayHits,ray.magnitude,~(1<<2),QueryTriggerInteraction.Ignore);
                if(count==rayHits.Length)visible=false;
                for(int i=0;i<count && visible;i++)
                    if(rayHits[i].collider!=null && !rayHits[i].collider.transform.IsChildOf(transform))visible=false;
            }
            if(!visible){if(nameLabel!=null)nameLabel.gameObject.SetActive(false);return;}
            if(nameLabel==null)
            {
                var go=new GameObject("SpectatorNickname",typeof(TextMeshPro));
                go.transform.SetParent(transform,false);
                go.layer=parts.Count>0?parts[0].Renderer.gameObject.layer:gameObject.layer;
                nameLabel=go.GetComponent<TextMeshPro>();nameLabel.font=TacticalUI.Font;
                nameLabel.alignment=TextAlignmentOptions.Center;nameLabel.richText=false;
                nameLabel.fontSize=2.4f;nameLabel.enableAutoSizing=true;nameLabel.fontSizeMin=1.2f;nameLabel.fontSizeMax=2.4f;
                nameLabel.rectTransform.sizeDelta=new Vector2(3.6f,.45f);
                nameLabel.textWrappingMode=TextWrappingModes.NoWrap;nameLabel.overflowMode=TextOverflowModes.Truncate;
                nameLabel.color=TacticalUI.White;
                nameMaterial=new Material(nameLabel.fontSharedMaterial);
                nameMaterial.SetFloat("unity_GUIZTestMode",(float)CompareFunction.LessEqual);
                nameMaterial.SetColor("_OutlineColor",new Color(.015f,.025f,.035f,1));
                nameMaterial.SetFloat("_OutlineWidth",.15f);
                nameLabel.fontSharedMaterial=nameMaterial;nameLabel.UpdateMeshPadding();
                nameLabel.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
            }
            nameLabel.gameObject.SetActive(true);
#if UNITY_EDITOR
            if(preview)nameLabel.text=PreviewNickname;
            else
#endif
                nameLabel.text=RelayLobbySession.Instance.GetRankedNickname(life.OwnerClientId);
            nameLabel.transform.position=headPosition+Vector3.up*.34f;
            nameLabel.transform.rotation=camera.transform.rotation;
            if(nameLabel.havePropertiesChanged)nameLabel.ForceMeshUpdate();
        }
    }
}
