using System;
using UnityEngine;

namespace HideAndShoot.Combat
{
    // One ordered collision query supplies both the candidate and the visible endpoint.
    public static class PistolAimUtility
    {
        public static bool TryFirstHit(Ray ray, float range, Transform shooter, out RaycastHit first)
        {
            var hits = Physics.RaycastAll(ray, range, ~0, QueryTriggerInteraction.Collide);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            int hitLayer = LayerMask.NameToLayer("HitCapsule");
            foreach (var hit in hits)
            {
                var collider = hit.collider;
                if (collider == null || (shooter != null && collider.transform.IsChildOf(shooter))) continue;
                int layer = collider.gameObject.layer;
                if (layer == LayerMask.NameToLayer("AmmoPickup") ||
                    layer == LayerMask.NameToLayer("WorldActor") ||
                    layer == LayerMask.NameToLayer("FPSViewModel") ||
                    layer == LayerMask.NameToLayer("LocalHiddenBody") ||
                    collider is CharacterController) continue;
                if (collider.isTrigger && layer != hitLayer) continue;
                first = hit;
                return true;
            }
            first = default;
            return false;
        }
    }
}
