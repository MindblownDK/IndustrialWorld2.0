// Assets/Scripts/Editor/UI/UiScaleLawSetup.cs
//
// Editor authoring for the one UI scale law owned at runtime by
// VoxelEngine.UI.UiScaleSystem.
//
// Two jobs, deliberately kept apart:
//
//   AuthorDesignValues - writes the NEUTRAL authored values (ConstantPixelSize at
//   1.00x) onto a PanelSettings. Used by the Setup Wizard so the asset on disk is
//   stable and does not carry whatever the Game view happened to be when somebody
//   last pressed Play. The runtime owns the live multiplier.
//
//   Run - the non-destructive repair pass in Maintenance & Repair. Finds every
//   PanelSettings asset in the project and re-authors its scaling fields only.
//   Theme, text settings, sort order, target display, target texture, atlas
//   settings, clear flags and shaders are read but never written.
#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.UI;

namespace VoxelEngine.EditorTools
{
    /// <summary>
    /// Non-destructive authoring and repair for PanelSettings scaling. Never changes
    /// a theme, a sort order or an atlas setting, and never deletes an asset.
    /// </summary>
    public static class UiScaleLawSetup
    {
        /// <summary>
        /// Canonical shared panel. Everything in the game loads this by name from
        /// Resources, so it is the one asset that must exist for the interface to draw.
        /// </summary>
        public const string CanonicalPanelSettingsPath = "Assets/Resources/MenuPanelSettings.asset";

        private static readonly string[] ThemeCandidates =
        {
            "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss",
            "Assets/Resources/UnityDefaultRuntimeTheme.tss",
            "Packages/com.unity.ui/PackageResources/StyleSheets/Generated/Default/UnityDefaultRuntimeTheme.tss",
            "Packages/com.unity.modules.uielements/PackageResources/StyleSheets/Generated/Default/UnityDefaultRuntimeTheme.tss"
        };

        /// <summary>
        /// Writes the neutral authored law onto a PanelSettings: ConstantPixelSize at
        /// 1.00x against a 1920x1080 reference, matched on height. Safe to call on an
        /// existing asset - it only touches scaling fields and never marks anything
        /// dirty on its own, so the caller decides whether to save.
        /// </summary>
        public static void AuthorDesignValues(PanelSettings ps)
        {
            if (ps == null) return;
            UiScaleSystem.ApplyAuthored(ps);
        }

        /// <summary>
        /// Repairs every PanelSettings asset in the project to the one scale law.
        /// Creates the canonical shared panel if it is missing. Reports each change.
        /// </summary>
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("UI Scale Law",
                    "Exit Play Mode before repairing PanelSettings assets.\n\n" +
                    "At runtime the game writes the live multiplier into the shared panel, " +
                    "so repairing now would save a display-specific value into the asset.",
                    "OK");
                return;
            }

            var report = new StringBuilder();
            int scanned = 0, repaired = 0, created = 0;
            var warnings = new List<string>();

            var canonical = AssetDatabase.LoadAssetAtPath<PanelSettings>(CanonicalPanelSettingsPath);
            if (canonical == null)
            {
                canonical = CreateCanonicalPanelSettings(report, warnings);
                if (canonical != null) created++;
            }

            string[] guids = AssetDatabase.FindAssets("t:PanelSettings");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
                if (ps == null) continue;

                scanned++;
                if (RepairOne(ps, path, report, warnings)) repaired++;
            }

            // SaveAssets only. Refresh would rescan a twenty-thousand-file project for
            // nothing: CreateAsset already registers the new panel and writes its meta.
            if (repaired > 0 || created > 0)
                AssetDatabase.SaveAssets();

            string summary =
                $"[UI Scale Law] Scanned {scanned} PanelSettings asset(s). " +
                $"Repaired {repaired}. Created {created}.\n{report}";

            if (warnings.Count > 0)
            {
                summary += "\nLeft for review (not changed):\n";
                for (int i = 0; i < warnings.Count; i++) summary += "  - " + warnings[i] + "\n";
            }

            Debug.Log(summary);

            EditorUtility.DisplayDialog("UI Scale Law",
                $"Scanned {scanned} PanelSettings asset(s).\n" +
                $"Repaired {repaired}.\n" +
                $"Created {created}.\n\n" +
                "Scaling fields only were touched. Themes, sort orders, target displays and " +
                "atlas settings were preserved.\n\n" +
                "See the Console for the per-asset detail.",
                "OK");
        }

        // ────────────────────────────────────────────────────────────────

        private static bool RepairOne(PanelSettings ps, string path, StringBuilder report, List<string> warnings)
        {
            var changes = new List<string>();

            if (ps.scaleMode != PanelScaleMode.ConstantPixelSize)
            {
                changes.Add($"scaleMode {ps.scaleMode} -> {PanelScaleMode.ConstantPixelSize}");
                ps.scaleMode = PanelScaleMode.ConstantPixelSize;
            }

            // ConstantPixelSize is the only mode that honours an explicit multiplier,
            // so the authored value is 1.00x. Anything else here is a live multiplier
            // the runtime left behind during an Editor play session.
            if (!Mathf.Approximately(ps.scale, 1f))
            {
                changes.Add($"scale {ps.scale:0.###} -> 1 (runtime leftover)");
                ps.scale = 1f;
            }

            if (ps.referenceResolution.x != UiScaleSystem.DesignWidth ||
                ps.referenceResolution.y != UiScaleSystem.DesignHeight)
            {
                changes.Add($"referenceResolution {ps.referenceResolution.x}x{ps.referenceResolution.y} -> " +
                            $"{UiScaleSystem.DesignWidth}x{UiScaleSystem.DesignHeight}");
                ps.referenceResolution = new Vector2Int(UiScaleSystem.DesignWidth, UiScaleSystem.DesignHeight);
            }

            if (ps.screenMatchMode != PanelScreenMatchMode.MatchWidthOrHeight)
            {
                changes.Add($"screenMatchMode {ps.screenMatchMode} -> {PanelScreenMatchMode.MatchWidthOrHeight}");
                ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            }

            if (!Mathf.Approximately(ps.match, 1f))
            {
                changes.Add($"match {ps.match:0.##} -> 1 (height)");
                ps.match = 1f;
            }

            if (!Mathf.Approximately(ps.referenceDpi, UiScaleSystem.ReferenceDpi))
            {
                changes.Add($"referenceDpi {ps.referenceDpi:0.##} -> {UiScaleSystem.ReferenceDpi:0}");
                ps.referenceDpi = UiScaleSystem.ReferenceDpi;
            }

            if (!Mathf.Approximately(ps.fallbackDpi, UiScaleSystem.ReferenceDpi))
            {
                changes.Add($"fallbackDpi {ps.fallbackDpi:0.##} -> {UiScaleSystem.ReferenceDpi:0}");
                ps.fallbackDpi = UiScaleSystem.ReferenceDpi;
            }

            // Reported, never rewritten: a world-space panel is an authored choice, and
            // flipping it would move that interface into the screen overlay.
            if (ps.renderMode != PanelRenderMode.ScreenSpaceOverlay)
            {
                warnings.Add($"{path}: renderMode is {ps.renderMode}. Runtime-managed panels are " +
                             "forced to ScreenSpaceOverlay; an authored world-space panel keeps its mode.");
            }

            if (ps.themeStyleSheet == null)
                warnings.Add($"{path}: no Theme Style Sheet assigned. UI Toolkit renders it unstyled.");

            if (changes.Count == 0) return false;

            EditorUtility.SetDirty(ps);
            report.Append("  ").Append(path).Append('\n');
            for (int i = 0; i < changes.Count; i++) report.Append("      ").Append(changes[i]).Append('\n');
            return true;
        }

        private static PanelSettings CreateCanonicalPanelSettings(StringBuilder report, List<string> warnings)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");

            // Only create it when nothing else is already occupying the path, so the
            // repair can never overwrite an unrelated asset that happens to live there.
            if (AssetDatabase.LoadMainAssetAtPath(CanonicalPanelSettingsPath) != null)
            {
                warnings.Add($"{CanonicalPanelSettingsPath}: an asset of another type occupies the " +
                             "canonical path. Resolve it by hand, then re-run.");
                return null;
            }

            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            ps.name = "MenuPanelSettings";
            // AuthorDesignValues also forces ScreenSpaceOverlay, as the shared panel has
            // always been authored: world-space quads clip the HUD at the screen edges.
            AuthorDesignValues(ps);

            ThemeStyleSheet theme = null;
            for (int i = 0; i < ThemeCandidates.Length; i++)
            {
                theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemeCandidates[i]);
                if (theme != null) break;
            }
            ps.themeStyleSheet = theme;

            AssetDatabase.CreateAsset(ps, CanonicalPanelSettingsPath);
            EditorUtility.SetDirty(ps);

            report.Append("  ").Append(CanonicalPanelSettingsPath).Append(" (created)\n");
            if (theme == null)
                warnings.Add($"{CanonicalPanelSettingsPath}: created without a theme - none of the known " +
                             "UnityDefaultRuntimeTheme paths resolved. Run Setup Wizard > Core & Project " +
                             "Bootstrap > Build the main-menu scene to author it fully.");
            return ps;
        }
    }
}
#endif
