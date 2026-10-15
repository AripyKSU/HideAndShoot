using Unity.Netcode.Components;

namespace HideAndShoot.Networking
{
    /// <summary>
    /// Owner-predicted transform for responsive local player motion.
    /// Host still requires movement sanity validation before trusting shot histories.
    /// This is a provisional sync selection pending real two-client latency tests.
    /// </summary>
    public sealed class OwnerNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative() => false;
    }
}
