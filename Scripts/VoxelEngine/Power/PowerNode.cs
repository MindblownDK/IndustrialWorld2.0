// Assets/Scripts/VoxelEngine/Power/PowerNode.cs
using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Power
{
    public enum PowerNodeKind { Cable, Generator, Consumer, Battery }

    /// <summary>
    /// Base for anything that participates in a power network. Auto-registers/unregisters
    /// with the PowerNetworkManager.
    /// </summary>
    public abstract class PowerNode : MonoBehaviour
    {
        public abstract PowerNodeKind Kind { get; }

        [Tooltip("Distance at which this node will auto-connect to neighbouring nodes/cables.")]
        public float connectRadius = 3.0f;

        // Network membership — assigned by PowerNetworkManager.
        [System.NonSerialized] public PowerNetwork network;
        [System.NonSerialized] public List<PowerNode> neighbours = new();

        protected virtual void OnEnable()  { PowerNetworkManager.EnsureInstance(); PowerNetworkManager.Instance.Register(this); }
        protected virtual void OnDisable() { PowerNetworkManager.Instance?.Unregister(this); }
    }
}
