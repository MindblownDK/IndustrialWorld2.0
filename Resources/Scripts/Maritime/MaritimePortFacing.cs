// Assets/Scripts/VoxelEngine/Maritime/MaritimePortFacing.cs
//
// Tiny data tag on maritime attachment ports: the LOCAL direction (relative
// to the port's direct parent — always the machine/model root) a connecting
// block should attach FROM. Every port built by MaritimeMeshBuilder.Port()
// and the liquid-tank markers in Step 13 carry one, so snapping, ghost
// rotation and pipe visuals read TRUE authored port orientation instead of
// guessing an axis from a position offset (which mis-aimed ports that sit
// near a machine's centre line).

using UnityEngine;

namespace VoxelEngine.Maritime
{
    [DisallowMultipleComponent]
    public class MaritimePortFacing : MonoBehaviour
    {
        [Tooltip("Outward attach direction in the port's PARENT (machine root) space. +Z of the port container is aligned to this for mesh-builder ports.")]
        public Vector3 localOutward = Vector3.forward;
    }
}
