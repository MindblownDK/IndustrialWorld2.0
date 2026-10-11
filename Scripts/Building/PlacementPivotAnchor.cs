// Assets/Scripts/VoxelEngine/Building/PlacementPivotAnchor.cs
//
// 17.19.0-dev - the "root is the block" placement contract.
//
// Problem this solves: placement used the authored COLLIDER/RENDERER bounds as
// its authority - on a deck the root is lifted so the lowest collider point
// rests on the support, on terrain the root lands at the snapped cell centre.
// Prefabs whose visual bottoms do not match their collider bottoms (or whose
// visuals are not centred on the root) therefore place with a hidden offset:
// a replacement model parented at local (0,0,0) shows up floating or sunk by
// exactly that mismatch - a little for some blocks, a lot for others.
//
// Contract with this marker on the placed prefab (anywhere in its hierarchy):
//   - the prefab ROOT PIVOT is the block position, everywhere;
//   - on decks and foundations the root is placed exactly on the support
//     surface; on terrain it is placed at the base of the snapped cell, with
//     the surface-aligned rotation unchanged;
//   - no bounds-derived lift or recentering is applied at all.
//
// Authoring rule for replacement models: build the model so local Y = 0 is
// its BOTTOM (or centre a centred model at +height/2), parent it at local
// (0,0,0), and it will appear exactly where the ghost showed the block -
// and exactly where the prefab root sits when dragged into a scene in the
// editor, so scene and game finally agree.
//
// Prefabs WITHOUT the marker keep the established bounds-based placement
// byte-for-byte; nothing existing shifts.

using UnityEngine;

namespace VoxelEngine.Building
{
    [DisallowMultipleComponent]
    public sealed class PlacementPivotAnchor : MonoBehaviour
    {
        // Pure marker. The build system detects it through the placement
        // profile and switches the pose solver to root-authoritative mode.
    }
}
