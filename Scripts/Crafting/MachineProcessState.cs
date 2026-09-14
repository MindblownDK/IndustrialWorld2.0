// Assets/Scripts/VoxelEngine/Crafting/MachineProcessState.cs
//
// MACHINE PROCESS PERSISTENCE (9.57.0-dev) — one additive save payload for every
// processing machine, so a reload no longer throws away what the player poured
// into a machine or what the machine was in the middle of doing.
//
// Why one payload instead of a saved class per machine: the machines differ in
// their contents — a distillation plant has seven tanks, a cracker has four plus
// a catalyst bed, a flare stack has one tank and a fuel selection, a grid
// refinery has no tank of its own — but they all share the same four things
// worth saving:
//
//   1. the batch in progress (which recipe, how far into it),
//   2. the recipe the player locked on the machine panel,
//   3. the fluid in every tank the machine owns,
//   4. a handful of numbers only that machine has (catalyst bed, reactor
//      temperature, flare totals).
//
// MachineProcessState is that shape, IMachineProcessState is the contract the
// persistence layer finds the machine by, and MachineProcessPersistence holds
// the two pieces of behaviour every machine needs: resolving a saved recipe name
// against the machine's own recipe list, and moving tank readings in and out of
// the payload.
//
// Save safety: the payload is additive end to end. A save written before this
// round has no machineProcess record at all, so every machine restores exactly
// as it does today — empty containers, empty tanks, no batch, free to pick a
// recipe. No existing save field changes meaning, no item id changes, and the
// enum values stored here (LiquidType) are the appended-only ones already used
// by every other fluid record in the save.

using System;
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Crafting
{
    /// <summary>One fluid tank's reading at save time.</summary>
    [Serializable]
    public class MachineTankState
    {
        /// <summary>Position of the tank in the machine's tank list. Authoritative.</summary>
        public int index;

        /// <summary>The tank's own label, kept as a fallback match in case a prefab ever reorders its tanks.</summary>
        public string label = string.Empty;

        /// <summary>LiquidType as an int. The enum is appended-only, so a stored value never changes meaning.</summary>
        public int liquid;

        /// <summary>Litres in the tank.</summary>
        public float litres;
    }

    /// <summary>
    /// Additive snapshot of one machine's live process and fluid state. Written by
    /// IMachineProcessState.CaptureProcessState and read back by RestoreProcessState.
    /// </summary>
    [Serializable]
    public class MachineProcessState
    {
        /// <summary>Asset name of the batch in progress. Empty = the machine was idle.</summary>
        public string recipeName = string.Empty;

        /// <summary>Asset name of the recipe the player locked on the panel. Empty = automatic.</summary>
        public string selectedRecipeName = string.Empty;

        /// <summary>Seconds of progress into the batch in progress.</summary>
        public float progressSeconds;

        /// <summary>Every tank the machine owns that carries contents, by index.</summary>
        public List<MachineTankState> tanks = new List<MachineTankState>();

        /// <summary>Keys of the machine-specific numbers, parallel to <see cref="extraValues"/>.</summary>
        public List<string> extraKeys = new List<string>();

        /// <summary>Values of the machine-specific numbers, parallel to <see cref="extraKeys"/>.</summary>
        public List<float> extraValues = new List<float>();

        /// <summary>Write a machine-specific number, replacing any earlier value under the same key.</summary>
        public void SetExtra(string key, float value)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (extraKeys == null) extraKeys = new List<string>();
            if (extraValues == null) extraValues = new List<float>();
            int at = extraKeys.IndexOf(key);
            if (at >= 0 && at < extraValues.Count) { extraValues[at] = value; return; }
            extraKeys.Add(key);
            extraValues.Add(value);
        }

        /// <summary>Read a machine-specific number, falling back to the live value when the key is absent or unusable.</summary>
        public float GetExtra(string key, float fallback)
        {
            if (string.IsNullOrEmpty(key) || extraKeys == null || extraValues == null) return fallback;
            int at = extraKeys.IndexOf(key);
            if (at < 0 || at >= extraValues.Count) return fallback;
            float value = extraValues[at];
            if (float.IsNaN(value) || float.IsInfinity(value)) return fallback;
            return value;
        }

        /// <summary>Read a machine-specific switch, carried as 0 or 1.</summary>
        public bool GetExtraBool(string key, bool fallback) => GetExtra(key, fallback ? 1f : 0f) >= 0.5f;

        /// <summary>True when this payload carries nothing worth applying — keeps a restore from touching live settings.</summary>
        public bool IsEmpty => string.IsNullOrEmpty(recipeName)
                            && string.IsNullOrEmpty(selectedRecipeName)
                            && progressSeconds <= 0f
                            && (tanks == null || tanks.Count == 0)
                            && (extraKeys == null || extraKeys.Count == 0);
    }

    /// <summary>
    /// Implemented by every machine whose live process state the world cannot
    /// rederive. The persistence layer finds the machine through this interface,
    /// so it never has to know which machines exist.
    /// </summary>
    public interface IMachineProcessState
    {
        /// <summary>Write this machine's live process state into the payload.</summary>
        void CaptureProcessState(MachineProcessState state);

        /// <summary>Apply a payload written by <see cref="CaptureProcessState"/> back onto this machine.</summary>
        void RestoreProcessState(MachineProcessState state);
    }

    /// <summary>Shared recipe-name resolution and tank transfer used by every IMachineProcessState.</summary>
    public static class MachineProcessPersistence
    {
        /// <summary>The name written for a recipe: its asset name, which survives panel rebuilds and reloads alike.</summary>
        public static string NameOf(ProcessingRecipe recipe) => recipe == null ? string.Empty : recipe.name;

        /// <summary>
        /// Find the machine's own recipe object for a saved name. Matches the asset
        /// name first (what gets written) and the recipe's display label second, so a
        /// renamed asset with an unchanged label still lands on the right recipe.
        /// Returns null — never a guess — when nothing matches.
        /// </summary>
        public static ProcessingRecipe Resolve(List<ProcessingRecipe> knownRecipes, string savedName, string owner)
        {
            if (string.IsNullOrEmpty(savedName)) return null;

            if (knownRecipes != null)
            {
                for (int i = 0; i < knownRecipes.Count; i++)
                    if (knownRecipes[i] != null && string.Equals(knownRecipes[i].name, savedName, StringComparison.Ordinal))
                        return knownRecipes[i];

                for (int i = 0; i < knownRecipes.Count; i++)
                    if (knownRecipes[i] != null && string.Equals(knownRecipes[i].GetDisplayName(), savedName, StringComparison.Ordinal))
                        return knownRecipes[i];
            }

            if (!string.IsNullOrEmpty(owner))
                Debug.LogWarning($"[{owner}] Saved recipe '{savedName}' is not in this machine's recipe list; it resumes on its first runnable recipe instead.");
            return null;
        }

        /// <summary>Write every tank's liquid type and level into the payload, keyed by list index.</summary>
        public static void CaptureTanks(MachineProcessState state, IReadOnlyList<MachineFluidTank> tanks)
        {
            if (state == null || tanks == null) return;
            if (state.tanks == null) state.tanks = new List<MachineTankState>();
            state.tanks.Clear();

            for (int i = 0; i < tanks.Count; i++)
            {
                var tank = tanks[i];
                if (tank == null) continue;
                state.tanks.Add(new MachineTankState
                {
                    index = i,
                    label = tank.label ?? string.Empty,
                    liquid = (int)tank.liquid,
                    litres = Mathf.Max(0f, tank.stored)
                });
            }
        }

        /// <summary>
        /// Pour a saved reading back into the live tanks: type and litres, clamped to
        /// the tank's own capacity. A tank that is no longer there is skipped rather
        /// than invented, and a saved liquid that is not a defined type is ignored so
        /// the tank keeps the type the prefab gave it.
        /// </summary>
        public static void RestoreTanks(MachineProcessState state, IReadOnlyList<MachineFluidTank> tanks)
        {
            if (state == null || state.tanks == null || tanks == null) return;

            for (int i = 0; i < state.tanks.Count; i++)
            {
                var saved = state.tanks[i];
                if (saved == null) continue;

                var tank = FindTank(tanks, saved);
                if (tank == null) continue;

                if (Enum.IsDefined(typeof(LiquidType), saved.liquid))
                    tank.liquid = (LiquidType)saved.liquid;

                float litres = (float.IsNaN(saved.litres) || float.IsInfinity(saved.litres)) ? 0f : saved.litres;
                tank.stored = Mathf.Clamp(litres, 0f, Mathf.Max(0f, tank.capacity));
            }
        }

        private static MachineFluidTank FindTank(IReadOnlyList<MachineFluidTank> tanks, MachineTankState saved)
        {
            if (saved.index >= 0 && saved.index < tanks.Count && tanks[saved.index] != null)
                return tanks[saved.index];

            if (!string.IsNullOrEmpty(saved.label))
            {
                for (int i = 0; i < tanks.Count; i++)
                {
                    var tank = tanks[i];
                    if (tank != null && string.Equals(tank.label, saved.label, StringComparison.Ordinal))
                        return tank;
                }
            }
            return null;
        }
    }
}
