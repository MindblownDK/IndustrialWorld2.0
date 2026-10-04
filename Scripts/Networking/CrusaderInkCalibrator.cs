// Assets/Scripts/VoxelEngine/Networking/CrusaderInkCalibrator.cs
//
// 14.53.1 - the tattoo finds the skin. The chest ink (and the crest riding
// it) is positioned at build time against the BIND pose - but the body never
// stands in bind pose. The locomotion idle relaxes the spine and pulls the
// chest back a few centimeters, so an offset that was flush at build floats
// visibly in front of the animated body (live-test screenshot, side view).
//
// Fix: a one-shot calibration against the REAL skin. A few frames after the
// rigged body starts animating, this bakes the skinned mesh in its current
// pose, finds the most outward skin point in a chest-sized patch around the
// ink anchor, and shifts the anchor along its own depth axis until the text
// plane sits 4 mm outside that surface. The crest is a child of the ink, so
// it comes along automatically with its own 12 mm standoff intact. Runs
// once, then turns itself off - cost is one mesh bake per avatar, ever.
//
// Added by the rigged-body builder only; the primitive fallback body does
// not animate and keeps its hand-placed offsets.

using UnityEngine;

namespace VoxelEngine.Networking
{
    public class CrusaderInkCalibrator : MonoBehaviour
    {
        /// <summary>How far outside the measured skin the text plane sits.</summary>
        private const float SkinGap = 0.004f;
        // The chest patch, in ink-local meters: wide enough for the pecs,
        // tall enough to cover both the motto and the crest above it, and
        // tight enough to exclude chin, arms and hands in any idle pose.
        private const float PatchHalfWidth = 0.12f;
        private const float PatchBelow = 0.12f;
        private const float PatchAbove = 0.18f;
        private const int SettleFrames = 5;   // let the animator leave bind pose first

        private int _calibrateAtFrame;

        private void OnEnable()
        {
            _calibrateAtFrame = Time.frameCount + SettleFrames;
        }

        private void LateUpdate()   // after the animator has posed the bones
        {
            if (Time.frameCount < _calibrateAtFrame) return;
            try { Calibrate(); }
            catch { /* a failed calibration keeps the build-time offset */ }
            enabled = false;
        }

        private void Calibrate()
        {
            var ink = CrusaderModel.FindDeep(transform, "ChestInk");
            if (ink == null) return;
            var renderers = GetComponentsInChildren<SkinnedMeshRenderer>();
            if (renderers == null || renderers.Length == 0) return;

            // Most outward skin point in the patch, in ink space (-Z is out
            // of the chest, so "most outward" is the MINIMUM z).
            float best = float.PositiveInfinity;
            var baked = new Mesh();
            foreach (var smr in renderers)
            {
                if (smr == null || smr.sharedMesh == null) continue;
                baked.Clear();
                smr.BakeMesh(baked, true);   // scale baked in - compose without it
                var verts = baked.vertices;
                Vector3 origin = smr.transform.position;
                Quaternion rot = smr.transform.rotation;
                for (int i = 0; i < verts.Length; i++)
                {
                    Vector3 local = ink.InverseTransformPoint(origin + rot * verts[i]);
                    if (Mathf.Abs(local.x) > PatchHalfWidth) continue;
                    if (local.y < -PatchBelow || local.y > PatchAbove) continue;
                    if (local.z < best) best = local.z;
                }
            }
            Destroy(baked);
            if (float.IsPositiveInfinity(best)) return;

            // Shift the anchor along its own depth axis so the skin point
            // lands SkinGap behind the text plane - works in both directions
            // (floating text moves in, buried text moves out).
            float delta = best - SkinGap;
            ink.position += ink.forward * delta;
        }
    }
}
