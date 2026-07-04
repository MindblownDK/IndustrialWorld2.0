// Assets/Scripts/VoxelEngine/Power/Wireless/PowerReceiver.cs
//
// Pretends to be a PowerGenerator on its local cable network: the watts/sec it produces
// equals the watts a transmitter beamed to us last frame.

using UnityEngine;

namespace VoxelEngine.Power.Wireless
{
    public class PowerReceiver : MonoBehaviour
    {
        [Tooltip("How much wattage this receiver would like to receive (and then feed into its local cable network).")]
        public float requestedWatts = 500f;

        // The generator we feed into the local cable network.
        private PowerGenerator _gen;
        private float _receivedThisFrame;

        private void Awake()
        {
            _gen = GetComponent<PowerGenerator>();
            if (_gen == null) _gen = gameObject.AddComponent<PowerGenerator>();
            _gen.wattsPerSecond = 0f;
            _gen.isOn = true;
        }

        public void SetReceivedThisFrame(float w) => _receivedThisFrame = w;

        private void LateUpdate()
        {
            // Apply received watts to the connected generator. We do this in LateUpdate
            // so the transmitter has already polled receivers in its Update.
            _gen.wattsPerSecond = _receivedThisFrame;
            _receivedThisFrame  = 0f; // reset for next frame
        }
    }
}
