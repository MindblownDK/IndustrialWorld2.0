// Assets/Scripts/VoxelEngine/Transport/IDirectItemPortEndpoint.cs
using VoxelEngine.Items;

namespace VoxelEngine.Transport
{
    /// <summary>
    /// Lightweight endpoint hook for blocks whose storage semantics cannot be represented
    /// by a normal slot-limited ItemContainer (for example one-item deep drawers).
    /// ItemPipe consults this before the generic ItemPortRouting path.
    /// </summary>
    public interface IDirectItemPortEndpoint
    {
        bool IsFaceConnectable(UnityEngine.Vector3 fromWorldPos);
        int TryAcceptFromPipe(UnityEngine.Vector3 pipeWorldPos, ItemDefinition item, int count);
    }
}
