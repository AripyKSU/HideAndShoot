using HideAndShoot.NPC;
using HideAndShoot.UI;
using HideAndShoot.Player;
using HideAndShoot.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HideAndShoot.World
{
    /// <summary>
    /// Failsafe for GameScene service components while prefab/scene editor
    /// installers finish migrating the generated prototype. No scene reloads.
    /// </summary>
    public static class GameSceneRuntimeConfigurator
    {
        public static void EnsureConfigured(Scene scene)
        {
            if(!scene.IsValid() || !scene.isLoaded || scene.name!="GameScene")return;
            GameObject anchors=null;
            GameObject services=null;
            foreach(var root in scene.GetRootGameObjects())
            {
                if(root.name=="GameplayAnchors")anchors=root;
                if(root.name=="GameplayServices")services=root;
            }
            if(anchors==null)
            {
                Debug.LogError("[HideAndShoot] GameplayAnchors missing from GameScene.");
                return;
            }
            if(anchors.GetComponent<SpawnPointRegistry>()==null)
                anchors.AddComponent<SpawnPointRegistry>();

            // The editable GameHUD_Canvas, RoundMenuCanvas and PodiumOverlay
            // are authored and saved in the GameScene by EditableUiSceneBaker.
            // Runtime components only bind/update them; never replace UI art.

            if(services==null)
            {
                services=new GameObject("GameplayServices");
                SceneManager.MoveGameObjectToScene(services,scene);
            }
            if(services.GetComponent<HostShotHistory>()==null)
                services.AddComponent<HostShotHistory>();
            if(services.GetComponent<HostDeathResolver>()==null)
                services.AddComponent<HostDeathResolver>();
            if(services.GetComponent<NpcZonePopulation>()==null)
                services.AddComponent<NpcZonePopulation>();
            if(services.GetComponent<NpcPoolCoordinator>()==null)
                services.AddComponent<NpcPoolCoordinator>();
            if(services.GetComponent<PickupGlowPresentation>()==null)
                services.AddComponent<PickupGlowPresentation>();
            if(services.GetComponent<AmmoSpawnDirector>()==null)
                services.AddComponent<AmmoSpawnDirector>();
            if(services.GetComponent<MatchAvatarBootstrap>()==null)
                services.AddComponent<MatchAvatarBootstrap>();
            if(services.GetComponent<HostRoundSetupCoordinator>()==null)
                services.AddComponent<HostRoundSetupCoordinator>();
            if(services.GetComponent<GameHUDPresenter>()==null)
                services.AddComponent<GameHUDPresenter>();
            if(services.GetComponent<RoundTransitionCurtain>()==null)
                services.AddComponent<RoundTransitionCurtain>();
            if(services.GetComponent<RoundMenuPresenter>()==null)
                services.AddComponent<RoundMenuPresenter>();
            if(services.GetComponent<MatchPodiumPresenter>()==null)
                services.AddComponent<MatchPodiumPresenter>();
            if(services.GetComponent<LateJoinSpectatorRig>()==null)
                services.AddComponent<LateJoinSpectatorRig>();
        }
    }
}
