using UnityEngine;

namespace HideAndShoot.World
{
    /// <summary>
    /// Direct prefab references keep NGO registrations consistent in Editor and standalone builds.
    /// Editor creates this resource; code never guesses a filesystem path at runtime.
    /// </summary>
    public sealed class NetworkPrefabCatalog : ScriptableObject
    {
        public GameObject PlayerAvatar;
        public GameObject NPCActor;
        public GameObject AmmoPickup;
    }
}
