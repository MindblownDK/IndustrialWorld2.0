// Assets/Scripts/VoxelEngine/Networking/CrusaderInkCalibrator.cs
//
// 14.53.1 - the tattoo finds the skin; 14.53.2 - and then STAYS on it.
//
// Why a one-shot was not enough: the first calibration ran five frames after
// spawn, while the animator was still cross-fading out of bind pose - so it
// measured a half-blended chest and kept a small residual float. And even a
// perfect single measurement goes stale the moment the pose changes: the
// chest breathes, leans and turns relative to the spine bone the ink rides.
//
// So the calibrator now works in two phases:
//   1. SETTLE + FULL SCAN (once, ~0.6 s after spawn): bake the skinned mesh
//      in the settled idle, find every vertex inside a chest-sized patch
//      around the ink anchor, remember those vertex indices.
//   2. CLING (every few frames, forever): re-bake and look at ONLY the
//      remembered chest vertices - a few hundred, not the whole body - find
//      the outermost one, and ease the ink anchor along its depth axis until
//      the text plane sits 4 mm outside the live surface. The correction is
//      applied at half strength per pass, so it converges smoothly instead
//      of popping.
//
// The crest is a child of the ink and follows with its own standoff intact.
// Dedicated servers render nothing and skip all of it. A failed bake keeps
// the current offset - cosmetics must never throw.

using System.Collections.Generic;
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
        private const int SettleFrames = 36;    // ~0.6 s - the blend out of bind pose is long over
        private const int ClingInterval = 3;    // re-measure every 3rd frame; breathing is slow
        private const float EasePerPass = 0.5f; // half the error per pass - converges, never pops

        private int _scanAtFrame;
        private int _nextClingFrame;
        private SkinnedMeshRenderer[] _renderers;
        private List<int>[] _patchIndices;      // chest vertices per renderer, found by the full scan
        private Mesh _baked;

        private void OnEnable()
        {
            if (NetworkSession.IsDedicated) { enabled = false; return; }
            _scanAtFrame = Time.frameCount + SettleFrames;
            _patchIndices = null;   // a re-enable re-scans: the pose moved on without us
        }

        private void OnDestroy()
        {
            if (_baked != null) Destroy(_baked);
        }

        private void LateUpdate()   // after the animator has posed the bones
        {
            if (Time.frameCount < _scanAtFrame) return;
            try
            {
                if (_patchIndices == null) FullScan();
                else if (Time.frameCount >= _nextClingFrame) Cling();
            }
            catch { /* a failed bake keeps the current offset */ }
        }

        /// <summary>Once: find the chest vertices around the ink anchor and
        /// remember their indices, then do the first correction at full
        /// strength - the settled idle is the pose that matters most.</summary>
        private void FullScan()
        {
            var ink = CrusaderModel.FindDeep(transform, "ChestInk");
            if (ink == null) { enabled = false; return; }
            _renderers = GetComponentsInChildren<SkinnedMeshRenderer>();
            if (_renderers == null || _renderers.Length == 0) { enabled = false; return; }
            if (_baked == null) _baked = new Mesh();

            _patchIndices = new List<int>[_renderers.Length];
            float best = float.PositiveInfinity;
            for (int r = 0; r < _renderers.Length; r++)
            {
                _patchIndices[r] = new List<int>();
                var smr = _renderers[r];
                if (smr == null || smr.sharedMesh == null) continue;
                _baked.Clear();
                smr.BakeMesh(_baked, true);   // scale baked in - compose without it
                var verts = _baked.vertices;
                Vector3 origin = smr.transform.position;
                Quaternion rot = smr.transform.rotation;
                for (int i = 0; i < verts.Length; i++)
                {
                    Vector3 local = ink.InverseTransformPoint(origin + rot * verts[i]);
                    if (Mathf.Abs(local.x) > PatchHalfWidth) continue;
                    if (local.y < -PatchBelow || local.y > PatchAbove) continue;
                    _patchIndices[r].Add(i);
                    if (local.z < best) best = local.z;   // -z = out of the chest
                }
            }
            if (!float.IsPositiveInfinity(best))
                ink.position += ink.forward * (best - SkinGap);   // full-strength snap
            _nextClingFrame = Time.frameCount + ClingInterval;
        }

        /// <summary>Every few frames: measure only the remembered chest
        /// vertices and ease the anchor onto the live surface.</summary>
        private void Cling()
        {
            _nextClingFrame = Time.frameCount + ClingInterval;
            var ink = CrusaderModel.FindDeep(transform, "ChestInk");
            if (ink == null) { enabled = false; return; }

            float best = float.PositiveInfinity;
            for (int r = 0; r < _renderers.Length; r++)
            {
                var smr = _renderers[r];
                var indices = _patchIndices[r];
                if (smr == null || smr.sharedMesh == null || indices == null || indices.Count == 0) continue;
                _baked.Clear();
                smr.BakeMesh(_baked, true);
                var verts = _baked.vertices;
                Vector3 origin = smr.transform.position;
                Quaternion rot = smr.transform.rotation;
                for (int k = 0; k < indices.Count; k++)
                {
                    int i = indices[k];
                    if (i >= verts.Length) continue;
                    Vector3 local = ink.InverseTransformPoint(origin + rot * verts[i]);
                    if (local.z < best) best = local.z;
                }
            }
            if (float.IsPositiveInfinity(best)) return;
            ink.position += ink.forward * ((best - SkinGap) * EasePerPass);
        }
    }
}
