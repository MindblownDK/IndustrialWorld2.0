// Assets/Scripts/VoxelEngine/Environment/SurfaceProfile.cs
//
// WHAT THE GROUND FEELS LIKE.
//
// One asset per surface, authored once and read by every wheel on every rig. The
// profile carries multipliers, never absolute forces: a tire owns its own grip and
// the ground only scales it, so re-tuning a tire never means re-tuning six terrain
// assets, and adding a new surface can never accidentally out-grip a tire.

using UnityEngine;
using VoxelEngine.Materials;

namespace VoxelEngine.Environment
{
    [CreateAssetMenu(menuName = "Voxel Engine/Environment/Surface Profile", fileName = "Surface_New")]
    public class SurfaceProfile : ScriptableObject
    {
        [Header("Identity")]
        public string surfaceName = "Dirt";
        [Tooltip("Shown in the wheel panel and used for dust/audio variant lookup.")]
        public Color debugTint = new Color(0.55f, 0.45f, 0.32f);

        [Header("Grip Multipliers")]
        [Tooltip("Scales how much drive/brake force the contact patch can pass. Low values allow wheelspin.")]
        [Range(0.05f, 2f)] public float forwardFriction = 1f;
        [Tooltip("Scales sideways hold. Low values let the rig drift on corners.")]
        [Range(0.05f, 2f)] public float lateralGrip = 1f;
        [Tooltip("Scales steering rate and angle authority on this surface.")]
        [Range(0.1f, 2f)] public float steeringResponse = 1f;
        [Tooltip("Coefficient of rolling drag. Sand and mud are slow even at full grip.")]
        [Range(0f, 0.25f)] public float rollingResistance = 0.025f;

        [Header("Feedback Hooks")]
        [Tooltip("Slip value above which dust/smoke FX and skid audio should fire.")]
        [Range(0f, 1f)] public float slipFxThreshold = 0.35f;
        [Tooltip("Particle tint for the slip plume this surface throws up.")]
        public Color plumeColor = new Color(0.62f, 0.54f, 0.40f, 0.55f);

        [Header("Matching — Unity Terrain")]
        [Tooltip("Terrain layer names (or fragments) that resolve to this profile, case-insensitive.")]
        public string[] terrainLayerKeywords = new string[0];

        [Header("Matching — Colliders")]
        [Tooltip("PhysicsMaterial names (or fragments) that resolve to this profile, case-insensitive.")]
        public string[] physicsMaterialKeywords = new string[0];

        [Header("Matching — Voxel World")]
        [Tooltip("Voxel materials that resolve to this profile.")]
        public MaterialId[] voxelMaterials = new MaterialId[0];

        /// <summary>
        /// Neutral multipliers used when nothing is authored or the library is missing.
        /// Deliberately NOT a ScriptableObject: a profile asset created on first access
        /// could be touched from a MonoBehaviour field initializer, and Unity forbids
        /// ScriptableObject.CreateInstance during construction. A null profile in a
        /// SurfaceSample already means "plain ground", so no object needs to exist.
        /// </summary>
        public const string FallbackName = "Ground";

        /// <summary>
        /// Blends this profile toward another by <paramref name="t"/>. Used where two
        /// sources describe the same contact patch (a worn road slab laid on sand).
        /// </summary>
        public SurfaceSample Blend(SurfaceSample other, float t)
        {
            var mine = new SurfaceSample(this);
            if (other.Profile == null) return mine;
            t = Mathf.Clamp01(t);
            return new SurfaceSample(
                t > 0.5f ? other.Profile : this,
                Mathf.Lerp(mine.Forward, other.Forward, t),
                Mathf.Lerp(mine.Lateral, other.Lateral, t),
                Mathf.Lerp(mine.Steering, other.Steering, t),
                Mathf.Lerp(mine.Rolling, other.Rolling, t));
        }
    }

    /// <summary>Resolved, already-multiplied surface values for one contact patch.</summary>
    public readonly struct SurfaceSample
    {
        public readonly SurfaceProfile Profile;
        public readonly float Forward;
        public readonly float Lateral;
        public readonly float Steering;
        public readonly float Rolling;

        public SurfaceSample(SurfaceProfile profile)
        {
            Profile = profile;
            Forward = profile != null ? profile.forwardFriction : 1f;
            Lateral = profile != null ? profile.lateralGrip : 1f;
            Steering = profile != null ? profile.steeringResponse : 1f;
            Rolling = profile != null ? profile.rollingResistance : 0.025f;
        }

        public SurfaceSample(SurfaceProfile profile, float forward, float lateral, float steering, float rolling)
        {
            Profile = profile; Forward = forward; Lateral = lateral; Steering = steering; Rolling = rolling;
        }

        public string Name => Profile != null && !string.IsNullOrEmpty(Profile.surfaceName)
            ? Profile.surfaceName : SurfaceProfile.FallbackName;
        public bool IsValid => Profile != null;

        /// <summary>Scales every multiplier (road wear, wetness, and other global modifiers).</summary>
        public SurfaceSample Scaled(float forward, float lateral)
            => new SurfaceSample(Profile, Forward * forward, Lateral * lateral, Steering, Rolling);

        /// <summary>Neutral ground. A plain struct value: safe in a field initializer.</summary>
        public static SurfaceSample Default => new SurfaceSample(null, 1f, 1f, 1f, 0.025f);
    }
}
