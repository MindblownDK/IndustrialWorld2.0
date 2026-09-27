// Assets/Scripts/VoxelEngine/GridSystem/Wheels/WheelLinkage.cs
//
// ONE POSER FOR THE SUSPENSION LINKAGE.
//
// The wishbones, the coil spring and the damper are authored one unit long down
// local +X with their pivot sitting on the chassis anchor. Posing them is therefore
// nothing more than "rotate to face the carrier, scale X to the distance" — but it
// has to happen in two places: the mesh builder, so the authored prefab already
// looks bolted together, and GridWheel, every physics step, so the spring visibly
// compresses. Two copies of that maths drift apart, so there is exactly one here.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public static class WheelLinkage
    {
        /// <summary>How far along the lower arm the strut foot sits, measured from the
        /// chassis anchor. Inboard of the ball joint, so the strut shortens faster than
        /// the arms swing — that difference is what reads as compression.</summary>
        public const float StrutFootAlongArm = 0.86f;

        /// <summary>Poses every linkage part found under a steering pivot.</summary>
        public static void Pose(Transform steerPivot, Vector3 carrierLocal)
        {
            if (steerPivot == null) return;
            Pose(steerPivot.Find("UpperArm"), steerPivot.Find("LowerArm"),
                 steerPivot.Find("Spring"), steerPivot.Find("Damper"), carrierLocal);
        }

        /// <summary>Poses pre-cached linkage parts. Used per physics step by the hub.</summary>
        public static void Pose(Transform upperArm, Transform lowerArm, Transform spring, Transform damper,
            Vector3 carrierLocal)
        {
            // The upper arm lands on the top ball joint of the knuckle, the lower arm on
            // the bottom one, so the upright stays roughly upright through the travel.
            float knuckleHalf = Mathf.Abs(carrierLocal.x) * 0.10f;
            Aim(upperArm, carrierLocal + new Vector3(0f, knuckleHalf, 0f));
            Aim(lowerArm, carrierLocal - new Vector3(0f, knuckleHalf, 0f));

            Vector3 footAnchor = lowerArm != null ? lowerArm.localPosition : carrierLocal;
            Vector3 strutFoot = Vector3.Lerp(footAnchor, carrierLocal - new Vector3(0f, knuckleHalf, 0f),
                StrutFootAlongArm);
            Aim(spring, strutFoot);
            Aim(damper, strutFoot);
        }

        /// <summary>Points a unit-length part at a target in the same local space and
        /// stretches it to reach, leaving its authored thickness alone.</summary>
        public static void Aim(Transform part, Vector3 targetLocal)
        {
            if (part == null) return;
            Vector3 delta = targetLocal - part.localPosition;
            float length = delta.magnitude;
            if (length < 0.0005f) return;
            part.localRotation = Quaternion.FromToRotation(Vector3.right, delta / length);
            var scale = part.localScale;
            part.localScale = new Vector3(length, scale.y, scale.z);
        }
    }
}
