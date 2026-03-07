using UnityEngine.Pool;

namespace AdvancedPooling.Core
{
    // T is constrained to a class so it can be used with Unity's IObjectPool
    public interface IPoolable<T> where T : class
    {
        // Gives the object a reference to its pool so it can release itself
        void Initialize(IObjectPool<T> pool);
        
        // Called the moment it leaves the pool
        void OnSpawn();
        
        // Called the moment it returns to the pool
        void OnDespawn();
    }
}