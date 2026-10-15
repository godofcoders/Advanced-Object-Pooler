using UnityEngine.Pool;

namespace AdvancedPooling.Core
{
    /// <summary>Lifecycle contract for components managed by a pool on Unity's main thread.</summary>
    public interface IPoolable<T> where T : class
    {
        /// <summary>Called once per instance while inactive. Do not rely on Awake having run yet.</summary>
        void Initialize(IObjectPool<T> pool);

        /// <summary>Called after activation and placement. Reset per-use state here.</summary>
        void OnSpawn();

        /// <summary>Called before deactivation. Cancel timers and subscriptions here.</summary>
        void OnDespawn();
    }
}
