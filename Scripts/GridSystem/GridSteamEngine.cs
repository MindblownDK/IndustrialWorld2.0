// Assets/Scripts/VoxelEngine/GridSystem/GridSteamEngine.cs
//
// THE STEAM ENGINE - a piston steam engine that produces ROTATIONAL POWER, nothing else.
//
// What it is: a machine, not a locomotive. A vertical boiler with a chimney, a horizontal
// steam cylinder, a crosshead sliding on bars, a connecting rod down to a crank pin, and
// a flywheel across the frame. While the fire is in and there is water to turn, the
// flywheel turns - and that turning IS the product. The engine generates no electricity
// and grants no traction flag: it outputs rotation (`CurrentRPM`), and anything on the
// grid that knows how to take rotation can take it - the bogie's mechanical drive, the
// brass screens' rotational tap, whatever the future hangs off a shaft.
//
// THE ANIMATION IS THE CONTRACT
// The piston gear is solved, not waved at: the crank pin circles with the flywheel, the
// crosshead position is the exact slider-crank solution (z = pin.z + sqrt(L^2 - dy^2)),
// and the connecting rod is laid between pin and crosshead at its true length and angle.
// At speed it looks like an engine; at rest it holds its last position like one.
//
// WHITE SMOKE
// The chimney makes white steam smoke - white, not soot: a coal fire with enough draft
// and water in the boiler breathes steam, and steam is what this block is selling.
//
// THE FIREBOX BURNS FIRST, THE TENDER IS THE TRAIN
// The firebox carries one private slot and burns from it first: coal, or wood at
// half the patience. When the slot runs dry it shovels out of any cargo container
// on the grid, like a real tender coaling from the wagon behind it. Water comes
// from a GridLiquidTank aboard on the move and from a WaterTower berthed. Fire
// without water loses pressure; water without fire loses it slower.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.FX;
using VoxelEngine.Items;

namespace VoxelEngine.GridSystem
{
    [DisallowMultipleComponent]
    public class GridSteamEngine : GridBlock
    {
        [Header("Boiler")]
        [Tooltip("Litres of water the boiler and tanks hold.")]
        public float waterCapacity = 800f;

        [Tooltip("Litres aboard right now. Authored empty; a fresh engine must visit a tower.")]
        public float waterStored;

        [Tooltip("Banked fire: no burn, no pressure, no rotation. Toggled from the console.")]
        public bool firing = true;

        [Tooltip("The firebox slot: coal, or wood at half the patience. Burns from here first.")]
        public ItemContainer firebox;

        /// <summary>Creates the 1-slot firebox if it is missing. Called from enable,
        /// the UI and the save paths - a null firebox is never an error.</summary>
        public void EnsureFirebox()
        {
            if (firebox == null) firebox = new ItemContainer("Firebox", 1);
            else firebox.Resize(1);
        }

        /// <summary>Boiler pressure 0..1. Runtime only: a cold boiler after a reload is
        /// honest, and a saved one would let a banked fire cheat its way to full head
        /// of steam between sessions.</summary>
        [System.NonSerialized] public float pressure;

        // ── Slider-crank geometry (matches the authored prefab) ───────────────
        private const float CrankRadius = 0.30f;
        private const float RodLength = 0.85f;
        private const float AxisHeight = 0.35f;

        // ── runtime ────────────────────────────────────────────────────────────
        private float _burnSeconds;
        private bool _lastFuelWasWood;
        private float _nextChuff;
        private float _nextSip;
        private float _lastSip = -1f;
        private float _phase;
        private float _rpm;
        private ParticleSystem _steam;
        private ItemDefinition _coal, _wood;

        private Transform _flywheelSpin, _crankPin, _crosshead, _conRod;
        private static readonly List<IGridItemStore> _storeScratch = new();

        /// <summary>Seconds of firing one unit of fuel buys. Wood is half a coal: it
        /// burns faster and colder, which is exactly what wood should do.</summary>
        private const float CoalSeconds = 120f;
        private const float WoodSeconds = 60f;

        /// <summary>Pressure above this the engine turns at all: enough head of steam
        /// to move a flywheel, not merely to hiss.</summary>
        public const float TractionPressure = 0.25f;

        public bool HasSteam => firing && pressure > TractionPressure;

        /// <summary>THE OUTPUT. Revolutions per minute at the flywheel: zero banked or
        /// starved, idle at first steam, full speed at full pressure. Anything that
        /// takes rotational power reads this and nothing else.</summary>
        public float CurrentRPM => _rpm;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            _coal = FindItem("coal");
            _wood = FindItem("wood_log");
            EnsureFirebox();
            _flywheelSpin = transform.Find("FlywheelSpin");
            _crankPin = transform.Find("FlywheelSpin/CrankPin");
            _crosshead = transform.Find("Crosshead");
            _conRod = transform.Find("ConRod");
            BuildSteam();
        }

        private void OnDisable()
        {
            if (_steam != null) Destroy(_steam.gameObject);
            _steam = null;
        }

        private static ItemDefinition FindItem(string id)
        {
            // The project has no item registry object; the established runtime lookup
            // is the Resources sweep PlayerInteractionTool uses for the same job.
            foreach (var item in Resources.LoadAll<ItemDefinition>(""))
                if (item != null && item.itemId == id) return item;
            foreach (var item in Resources.FindObjectsOfTypeAll<ItemDefinition>())
                if (item != null && item.itemId == id) return item;
            return null;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            var bogie = GetComponentInParent<GridRailBogie>();
            float work = bogie != null ? Mathf.Clamp01(bogie.Speed / 12f) : 0f;

            // ── Fire and pressure ──
            bool fed = firing && HasFuel() && waterStored > 0.5f;
            if (fed)
                pressure = Mathf.Min(1f, pressure + dt * (0.055f + 0.045f * (1f - work)));
            else
                pressure = Mathf.Max(0f, pressure - dt * 0.04f);

            // ── Burn: one unit of fuel per its seconds of firing ──
            if (firing && pressure > 0.05f)
            {
                _burnSeconds += dt * (0.6f + 0.8f * work);
                float unit = _lastFuelWasWood ? WoodSeconds : CoalSeconds;
                if (_burnSeconds >= unit)
                {
                    _burnSeconds = 0f;
                    ConsumeFuel();
                }
            }

            // ── Water: steam made is water spent ──
            if (pressure > 0.3f)
                waterStored = Mathf.Max(0f, waterStored - dt * (0.15f + 2.2f * work));

            // ── Sip: tank wagon on the move, water tower berthed ──
            if (Time.time >= _nextSip)
            {
                float sip = Time.time - (_lastSip < 0f ? Time.time : _lastSip);
                _lastSip = Time.time;
                _nextSip = Time.time + 0.5f;
                SipWater(sip);
            }

            // ── THE ROTATION: pressure is throttle, and a flywheel eases both ways ──
            float target = HasSteam ? Mathf.Lerp(45f, 220f, pressure) : 0f;
            _rpm = Mathf.MoveTowards(_rpm, target, dt * (target > _rpm ? 60f : 90f));
            _phase += _rpm / 60f * 360f * dt;
            if (_phase > 360f) _phase -= 360f;
            AnimateGear();

            // ── White smoke and the chuff under way ──
            if (_steam != null)
            {
                var em = _steam.emission;
                em.rateOverTime = pressure > 0.1f ? 10f + pressure * 26f + work * 40f : 0f;
            }
            if (HasSteam && _rpm > 20f && Time.time >= _nextChuff)
            {
                // Twice per revolution, the classic exhaust count.
                _nextChuff = Time.time + Mathf.Max(0.07f, 30f / Mathf.Max(1f, _rpm));
                AudioManager.PlayAt(SfxLibrary.GetVariant(Sfx.SteamChuff, 3), transform.position,
                    volume: 0.25f + work * 0.2f, pitch: Random.Range(0.92f, 1.08f), maxDistance: 22f);
            }
        }

        /// <summary>
        /// The slider-crank, solved: crank pin circles with the flywheel, the crosshead
        /// rides its bars at the exact position the rod length allows, and the rod is
        /// laid between them at its true angle. No approximations, no swimming joints.
        /// </summary>
        private void AnimateGear()
        {
            if (_flywheelSpin != null)
                _flywheelSpin.localRotation = Quaternion.Euler(0f, _phase, 0f);

            if (_crankPin == null || _crosshead == null || _conRod == null) return;

            // Pin position in ROOT space: the crosshead and rod live in root space,
            // and the flywheel mount between them carries a placement rotation.
            var pinInRoot = transform.InverseTransformPoint(_crankPin.position);

            float dy = pinInRoot.y - AxisHeight;
            float under = RodLength * RodLength - dy * dy;
            float dz = Mathf.Sqrt(Mathf.Max(0.0001f, under));
            float zCross = pinInRoot.z + dz;

            _crosshead.localPosition = new Vector3(0f, AxisHeight, zCross);

            // Rod from pin to crosshead: true direction, true length, rotated about X.
            var d = new Vector3(0f, AxisHeight - pinInRoot.y, zCross - pinInRoot.z);
            float angle = Mathf.Atan2(d.z, d.y) * Mathf.Rad2Deg;
            _conRod.localPosition = pinInRoot;
            _conRod.localRotation = Quaternion.Euler(angle, 0f, 0f);
        }

        private bool HasFuel() => HasFuelInFirebox() || HasFuelInStores();

        private bool HasFuelInFirebox()
        {
            if (firebox == null || firebox.Size < 1) return false;
            if (_coal == null && _wood == null) return false;
            var s = firebox.GetSlot(0);
            return s.item == _coal || s.item == _wood;
        }

        private bool HasFuelInStores()
        {
            if (_coal == null && _wood == null) return false;
            foreach (var store in Stores())
            {
                var c = store.ItemStore;
                for (int i = 0; i < c.Size; i++)
                {
                    var slot = c.GetSlot(i);
                    if (slot.item == _coal || slot.item == _wood) return true;
                }
            }
            return false;
        }

        private void ConsumeFuel()
        {
            // The firebox burns first; the tender (any cargo container aboard) is the fallback.
            if (firebox != null && firebox.Size > 0)
            {
                if (_coal != null && firebox.Remove(_coal, 1) > 0) { _lastFuelWasWood = false; return; }
                if (_wood != null && firebox.Remove(_wood, 1) > 0) { _lastFuelWasWood = true; return; }
            }
            foreach (var store in Stores())
            {
                var c = store.ItemStore;
                if (_coal != null && c.Remove(_coal, 1) > 0) { _lastFuelWasWood = false; return; }
                if (_wood != null && c.Remove(_wood, 1) > 0) { _lastFuelWasWood = true; return; }
            }
        }

        private IEnumerable<IGridItemStore> Stores()
        {
            _storeScratch.Clear();
            var grid = Grid;
            if (grid != null)
                foreach (var b in grid.AllBlocks)
                    if (b is IGridItemStore s && s.ItemStore != null) _storeScratch.Add(s);
            return _storeScratch;
        }

        private void SipWater(float dt)
        {
            if (waterStored >= waterCapacity - 0.01f) return;

            // A tank wagon in the consist feeds the boiler on the move.
            var grid = Grid;
            if (grid != null)
            {
                foreach (var b in grid.AllBlocks)
                {
                    if (b is GridLiquidTank tank && tank.liquidType == LiquidType.Water
                        && tank.stored > 1f && b != this)
                    {
                        float take = Mathf.Min(4f * Mathf.Max(dt, 0.01f), tank.stored,
                            waterCapacity - waterStored);
                        tank.stored -= take;
                        waterStored += take;
                        if (waterStored >= waterCapacity - 0.01f) return;
                    }
                }
            }

            // Platform-side: a water tower within reach of the standpipe.
            var tower = VoxelEngine.Building.WaterTower.Nearest(transform.position, 6f);
            if (tower != null)
            {
                float take = tower.TakeSome(30f * Mathf.Max(dt, 0.01f));
                waterStored = Mathf.Min(waterCapacity, waterStored + take);
            }
        }

        /// <summary>The whistle: one pull, one long two-note cry across the valley.</summary>
        public void Whistle()
        {
            AudioManager.PlayAt(SfxLibrary.GetVariant(Sfx.SteamWhistle, 2), transform.position,
                volume: 0.9f, pitch: Random.Range(0.96f, 1.04f), maxDistance: 120f);
            if (_steam != null)
            {
                var em = _steam.emission;
                em.rateOverTime = 90f;
            }
        }

        // ── white chimney steam ────────────────────────────────────────────────
        private void BuildSteam()
        {
            if (_steam != null) return;
            var go = new GameObject("ChimneySteam") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 1.55f, -1.15f);
            _steam = go.AddComponent<ParticleSystem>();
            var main = _steam.main;
            main.loop = true;
            main.startLifetime = 2.0f;
            main.startSpeed = 2.4f;
            main.startSize = 0.8f;
            main.startColor = new Color(1f, 1f, 1f, 0.55f);
            main.gravityModifier = -0.03f;
            var em = _steam.emission;
            em.rateOverTime = 0f;
            var shape = _steam.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 10f;
            shape.radius = 0.10f;
            var col = _steam.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.55f, 0f), new GradientAlphaKey(0.35f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var siz = _steam.sizeOverLifetime;
            siz.enabled = true;
            siz.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 2.8f)));
            _steam.Play();
        }
    }
}
