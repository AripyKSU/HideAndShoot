using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace HideAndShoot.Networking
{
    /// <summary>
    /// NGO prefab instance handler used for pooled NPCs and pickups on clients.
    /// Host-created objects are managed by their own world coordinator.
    /// All objects stay active only while spawned and can be reused next round.
    /// </summary>
    public sealed class PooledNetworkInstanceHandler : INetworkPrefabInstanceHandler
    {
        private readonly GameObject prefab;
        private readonly Stack<NetworkObject> inactive = new Stack<NetworkObject>();
        private readonly HashSet<NetworkObject> owned = new HashSet<NetworkObject>();

        public PooledNetworkInstanceHandler(GameObject prefab)
        {
            this.prefab = prefab != null ? prefab :
                throw new ArgumentNullException(nameof(prefab));
        }

        public NetworkObject Instantiate(ulong ownerClientId, Vector3 position, Quaternion rotation)
        {
            NetworkObject instance = null;
            while (inactive.Count > 0 && instance == null)
                instance = inactive.Pop();

            if (instance == null)
            {
                GameObject created = UnityEngine.Object.Instantiate(prefab, position, rotation);
                instance = created.GetComponent<NetworkObject>();
                if (instance == null)
                {
                    UnityEngine.Object.Destroy(created);
                    return null;
                }
                owned.Add(instance);
            }

            instance.transform.SetPositionAndRotation(position, rotation);
            instance.gameObject.SetActive(true);
            return instance;
        }

        public void Destroy(NetworkObject instance)
        {
            if (instance == null || !owned.Contains(instance))
                return;

            var resettable = instance.GetComponent<IPooledActorReset>();
            resettable?.ResetAfterNetworkDespawn();
            instance.gameObject.SetActive(false);
            inactive.Push(instance);
        }

        public void Dispose()
        {
            foreach (var actor in owned)
                if (actor != null)
                    UnityEngine.Object.Destroy(actor.gameObject);
            owned.Clear();
            inactive.Clear();
        }
    }

    /// <summary>Optional cleanup for the handler's reusable networked actors.</summary>
    public interface IPooledActorReset
    {
        void ResetAfterNetworkDespawn();
    }
}
