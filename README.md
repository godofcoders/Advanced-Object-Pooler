# ♻️ Advanced Object Pooling System (Unity 2021+)

A generic, interface-driven object pooling system for Unity. This module leverages the modern `UnityEngine.Pool` API to provide a memory-safe, zero-allocation solution for spawning high-frequency objects (bullets, particles, enemies).

## 🚀 Overview
Standard object pooling often relies on slow `List<GameObject>` searches or non-generic singleton managers. 

This architecture uses a **Generic Factory (`ComponentPoolFactory<T>`)** that wraps Unity's native stack-based pool. It enforces the `IPoolable<T>` interface, allowing objects to handle their own reset logic (`OnSpawn`/`OnDespawn`) and automatically return themselves to the pool, decoupling them completely from the spawner.

## 🧠 Key Features & Patterns

* **Generic Constraints:** The system works with any class that inherits from `MonoBehaviour` and implements `IPoolable<T>`.
* **Self-Managing Objects:** Objects are injected with a reference to their own pool upon creation, allowing them to `Release()` themselves when their task is done (e.g., after a timer or collision).
* **Zero-Allocation Runtime:** Uses Unity's `ObjectPool<T>` (Stack-based) to ensure getting and releasing objects generates **0 B** of garbage.
* **Safety Checks:** Includes optional collection checks (Editor-only) to prevent the common bug of releasing an item that is already in the pool.

## 🏗️ Lifecycle Diagram

```mermaid
graph TD
    %% Nodes
    Client["Client Script<br>(PoolDemoSpawner)"]
    Factory["Pool Factory<br>(Wraps Unity ObjectPool)"]
    Pool["Internal Stack<br>(Inactive Objects)"]
    Obj["Pooled Object<br>(Implements IPoolable)"]

    %% Flow - Spawning
    Client -->|"1. Calls Get()"| Factory
    Factory -->|"2. Checks Stack"| Pool
    Pool -.->|"3a. Pop Existing"| Obj
    Factory -.->|"3b. Instantiate New (if empty)"| Obj
    
    Factory -->|"4. Calls OnSpawn()"| Obj
    Obj -->|"5. Active in Scene"| Obj

    %% Flow - Despawning
    Obj -->|"6. Logic Complete<br>(Time/Collision)"| Obj
    Obj -->|"7. Calls Release(this)"| Factory
    Factory -->|"8. Calls OnDespawn()"| Obj
    Factory -->|"9. Push to Stack"| Pool

    %% Styling
    classDef client fill:#4b5363,stroke:#fff,stroke-width:2px,color:#fff
    classDef system fill:#2374ab,stroke:#fff,stroke-width:2px,color:#fff
    classDef logic fill:#4caf50,stroke:#fff,stroke-width:2px,color:#fff

    class Client client
    class Factory,Pool system
    class Obj logic
