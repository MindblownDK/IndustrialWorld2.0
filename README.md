# 🏭 IndustrialWorld

[![Engine](https://img.shields.io/badge/Unity-6.4+-black.svg?style=flat-square&logo=unity&logoColor=white)](https://unity.com/)
[![Focus](https://img.shields.io/badge/Design-Sleek%20%7C%20Minimal%20%7C%20Premium-E0A96D.svg?style=flat-square)](#)
[![Target](https://img.shields.io/badge/AI--Optimized-Context%20Ready-00ADB5.svg?style=flat-square)](#-ai-agent-system-prompt--execution-guidelines)

`IndustrialWorld` is a next-generation automation and industrial simulation project built from the ground up on **Unity 6.4**. The project is architected with a strict core philosophy: **radical simplicity in execution, absolute premium quality in aesthetics, and incredibly fluid, responsive user experiences.** Instead of overwhelming players with visual clutter, `IndustrialWorld` focuses on minimal, sleek, high-fidelity UI/UX design, micro-interactions, and pristine production quality to deliver a deeply satisfying and complete game feel.

---

## 🧭 Project Blueprint & Core Pillars

When building or modifying features for this project, always adhere to our three core execution pillars:

```
┌─────────────────────────────────────────────────────────────────┐
│                       INDUSTRIAL WORLD                          │
├───────────────────┬───────────────────────┬─────────────────────┤
│   1. SIMPLICITY   │   2. SLEEK AESTHETICS │ 3. PRODUCTION VALUE │
│ Lean code systems │ Minimalist HUD & UIs  │ Micro-interactions  │
│ Modular design    │ High-contrast layouts │ Absolute completeness│
└───────────────────┴───────────────────────┴─────────────────────┘
```

1. **Simplicity First:** Code architecture must be clean, highly decoupled, and easily readable. Game mechanics should be intuitive but possess deep systemic emergence. Avoid over-engineering.
2. **Sleek & Pretty Visuals:** UIs must feature plenty of breathing room (negative space), elegant typography, smooth transitions, and monochromatic/muted corporate-industrial color palettes accented with subtle glowing indicators.
3. **Flawless Quality ("Game Feel"):** Every click, hover, drag, and state change must feel tactile. Use subtle juice—easing functions, screenshake, particle bursts, and programmatic UI scaling—to make the game feel polished and finished.

---

## 🤖 AI Agent System Prompt & Execution Guidelines

> **🚨 CRITICAL INSTRUCTION FOR REASONING LLMs & LLMs AUTOMATIONS:**
> You are acting as an expert Senior Unity Systems Architect and High-Fidelity Technical UI/UX Designer. Read this section before generating code, proposing features, or refactoring scripts within this repository.

### 🧠 1. Cognitive Operational Constraints
* **Efficiency First:** Write highly optimized C# code conforming to **Unity 6.4 standards**. Leverage new Unity 6 performance improvements, efficient memory allocation, and the Universal Render Pipeline (URP).
* **Zero Bloat:** Never provide repetitive or boilerplate logic unless explicitly requested. Prefer concise, modular, and self-documenting code using appropriate design patterns (Observer, Factory, Component-based architectures).
* **The "Finished Product" Rule:** When generating code or assets, never write half-finished placeholders (`// TODO: Implement later`). Every piece of code must be fully fleshed out, safe against null exceptions, and complete.

### 🎨 2. UI & Interaction Philosophy (Strict Requirements)
Our UI is **not** a traditional cluttered industrial spreadsheet. It is closer to a luxury premium OS dashboard.
* **Component Preference:** Use **UI Toolkit** (preferred) or heavily styled **TextMeshPro + Canvas Engine**.
* **Animation & Motion:** Never let UI elements appear or disappear instantly. Always animate transitions programmatically (e.g., using `DOTween` or Unity's native `UIToolkit` transition rules) with professional easing functions like `EaseInOutQuad` or `EaseOutCubic`.
* **Micro-interactions:** Every UI button must have distinctive, beautiful, and distinct visual states for `Normal`, `Hovered`, `Pressed`, and `Disabled`. Use slight scale shifts (e.g., scale to `1.03x` on hover) and color-interpolation over `0.1s`.

### 💻 3. Coding Style Guide
* Follow standard C# PascalCase naming conventions for methods/classes, camelCase for local variables, and `_camelCase` for private fields.
* Implement structured event-driven code using `System.Action` or `UnityEngine.Events` to prevent hard-coupling between logic and UI systems.
* Use explicit namespace structures matching the directory architecture: `IndustrialWorld.Core`, `IndustrialWorld.UI`, `IndustrialWorld.Simulation`, etc.

---

## 🛠️ Step-by-Step AI Development Workflow

When tasked to build a feature or fix a bug in `IndustrialWorld`, execute the following loop:

```
  [1] ANALYZE   ──> Review current systems, dependencies, and scene hierarchy.
       │
  [2] MINIMIZE  ──> Strip out unnecessary steps. Design the cleanest code possible.
       │
  [3] BEAUTIFY  ──> Ensure visual feedback, UI styling, and animations look pristine.
       │
  [4] HARDEN    ──> Add robust error handling, null-checks, and performance validation.
```

1. **Analyze Requirements:** Understand how the feature interacts with existing world-state systems.
2. **Design For Simplicity:** Keep data structures simple. Use ScriptableObjects for configuration and data-driven designs wherever applicable.
3. **Execute High-Fidelity UI/UX:** Proactively add UI handling code, juice, and sound triggers to match our high-quality guidelines.
4. **Harden & Format:** Clean up the script, ensure proper encapsulation, document complex logic blocks, and eliminate any possible runtime warning.

---

## 🏗️ Technical Architecture Matrix

The project structure is broken down into clean, single-responsibility domains:

| Module Namespace | Primary Target / Purpose | Style Guidance |
| :--- | :--- | :--- |
| **`IndustrialWorld.Core`** | Global GameManager, state machines, bootstrapper. | Robust, performant, persistent across scene loads. |
| **`IndustrialWorld.Simulation`** | Processing nodes, factories, logistical pathfinding. | Lightweight data types, deterministic, performant logic loop. |
| **`IndustrialWorld.UI`** | Menus, contextual overlays, building HUDs, holographic displays. | Sleek layout, responsive vector assets, elegant animations. |
| **`IndustrialWorld.FX`** | Post-processing pipelines, screen space effects, audio triggers. | Subtle, premium look using URP volumetric settings. |

---

## 🚀 Getting Started with Unity 6.4

### Prerequisites
* **Unity Hub** with **Unity 6.4.x** installed.
* **Render Pipeline:** Universal Render Pipeline (URP). Keep modern post-processing active (Bloom, ACES Tonemapping, Motion Blur) to support the sleek look.

### Repository Conventions
* **Main Branch:** Protected. All work must be done via clean feature branches (`feature/your-feature-name`).
* **Commit Messages:** Must be descriptive and semantic (e.g., `feat(ui): add sleek contextual radial menu for machine upgrades`).

---

<p align="center">
  Designed with absolute precision. Focused on simplicity. Built for pure quality.
</p>
