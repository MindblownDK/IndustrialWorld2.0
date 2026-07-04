// Assets/Scripts/VoxelEngine/Power/Wind/StandardWindmill.cs
using UnityEngine;
using VoxelEngine.Power;

namespace VoxelEngine.Power.Wind
{
    public class StandardWindmill : WindmillBase
    {
        public enum AssemblyStage
        {
            Tower,
            Nacelle,
            InternalParts,
            Hub,
            Wings
        }

        [Header("Assembly State")]
        public AssemblyStage currentStage = AssemblyStage.Tower;
        public bool hasGearbox = false;
        public bool hasGenerator = false;
        public int wingsInstalled = 0;
        public bool nacelleOpen = false;

        [Header("Interactivity")]
        public GameObject nacelleRoof;
        public Transform ladderStart;
        public Transform nacelleFloor;

        public void InstallPart(string partId)
        {
            switch (partId)
            {
                case "Nacelle":
                    if (currentStage == AssemblyStage.Tower) currentStage = AssemblyStage.Nacelle;
                    break;
                case "Gearbox":
                    if (currentStage == AssemblyStage.Nacelle) hasGearbox = true;
                    break;
                case "Generator":
                    if (currentStage == AssemblyStage.Nacelle) hasGenerator = true;
                    break;
                case "Hub":
                    if (currentStage == AssemblyStage.Nacelle && hasGearbox && hasGenerator) 
                        currentStage = AssemblyStage.Hub;
                    break;
                case "Wing":
                    if (currentStage == AssemblyStage.Hub && wingsInstalled < 3)
                        wingsInstalled++;
                    if (wingsInstalled == 3) currentStage = AssemblyStage.Wings;
                    break;
            }
        }

        public void ToggleNacelle()
        {
            nacelleOpen = !nacelleOpen;
            if (nacelleRoof != null)
                nacelleRoof.transform.localPosition = nacelleOpen ? new Vector3(0, 2f, 0) : Vector3.zero;
        }

        protected override void Update()
        {
            // Standard windmill only generates power if fully assembled
            if (currentStage != AssemblyStage.Wings)
            {
                wattsPerSecond = 0;
                return;
            }
            base.Update();
        }
    }
}
