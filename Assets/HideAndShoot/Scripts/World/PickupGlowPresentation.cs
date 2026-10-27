using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HideAndShoot.World
{
    /// <summary>Restrained HDR bloom for emissive pickups in the match scene.</summary>
    public sealed class PickupGlowPresentation : MonoBehaviour
    {
        private Volume volume;
        private VolumeProfile profile;

        private void OnEnable()
        {
            if(volume==null)
            {
                var root=new GameObject("PickupGlow_Bloom");
                root.transform.SetParent(transform,false);
                root.layer=gameObject.layer;
                volume=root.AddComponent<Volume>();
                volume.isGlobal=true;
                volume.priority=0f;
                profile=ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name="Pickup glow (runtime)";
                var bloom=profile.Add<Bloom>(true);
                bloom.threshold.Override(.95f);
                bloom.intensity.Override(.7f);
                bloom.scatter.Override(.45f);
                bloom.tint.Override(Color.white);
                volume.sharedProfile=profile;
            }
            volume.enabled=true;
            RenderPipelineManager.beginContextRendering+=PrepareCameras;
        }

        private void PrepareCameras(ScriptableRenderContext context,List<Camera> cameras)
        {
            // Runs before URP initializes each camera, including newly created spectator cameras.
            int pickupLayer=LayerMask.NameToLayer("AmmoPickup");
            foreach(var camera in cameras)
            {
                if(camera==null || camera.cameraType!=CameraType.Game ||
                   camera.gameObject.scene!=gameObject.scene ||
                   (pickupLayer>=0 && (camera.cullingMask&(1<<pickupLayer))==0))continue;
                camera.allowHDR=true;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
            }
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginContextRendering-=PrepareCameras;
            if(volume!=null)volume.enabled=false;
        }

        private void OnDestroy()
        {
            if(profile!=null)CoreUtils.Destroy(profile);
        }
    }
}