using UnityEngine;

namespace HideAndShoot
{
    /// <summary>
    /// Authoring anchor, not a network spawn implementation.
    /// Host will select unique PlayerSpawn anchors each round.
    /// </summary>
    public sealed class WorldAnchor : MonoBehaviour
    {
        public enum AnchorKind { PlayerSpawn, AmmoCandidate }
        public AnchorKind Kind;
        public int Index;

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            Gizmos.color = Kind == AnchorKind.PlayerSpawn ?
                new Color(0.25f, 0.70f, 1.0f, 1f) :
                new Color(1.0f, 0.75f, 0.05f, 1f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.4f,
                Kind == AnchorKind.PlayerSpawn ? 0.75f : 0.45f);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 1.5f);
        }
#endif
    }
}
