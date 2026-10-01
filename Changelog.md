# IndustrialWorld — Changelog

**Branch:** `Dev`  
**Current Version:** `14.18.6-dev`

All release notes are maintained here so `Roadmap.md` remains focused on planned work and execution status.

### [14.18.6-dev] Quickened Steel

**Type:** PATCH - the replicated swing kept up poorly with the actual attack cadence.

The pack's slash clip is a full theatrical windup-slash-recover, far longer than the sword's real 0.45 s swing cooldown, so the animation dragged behind the action. The swing now plays at matched speed (clip compressed into a ~0.55 s window, speed clamped 1x-3.5x) and blends in much faster than locomotion (snap-in crossfade for the attack slot only) - the visible slash lands when the hit lands, and auto-swinging reads as continuous fast strikes.

### [14.18.5-dev] The Swing Heard Round The World

**Type:** PATCH - replicated swings finally fire.

**Cause:** `HeldToolView` is added to the CAMERA object by the setup wizard, not to the player root. The avatar's swing mirror looked it up with `GetComponent` on the player object, always got null, and silently never read the swing counter - so no swing ever left the owning client, which is why no "[Crusader] attack replicated" line ever appeared.

**Fix:** the lookup searches the player's children (the camera hangs under the player), with the local main camera as fallback. Owner-only code path, so the local camera is always the right one.

### [14.18.4-dev] A Proper Grip

**Type:** PATCH - swing replication and held-item grips on the avatar.

**Swing animation never played:**
- The join-time prime guard swallowed the FIRST replicated swing whenever no swing had happened before you joined (the initial SyncVar delivery it was waiting for never fires for an unchanged default). Priming now happens in OnStartClient, where initial state is actually consumed - every change after that is a real swing.
- The swing clip was also gated to the sword stance. Now ANY held-item swing plays it: with a sword it reads as a slash, with a pickaxe or axe as the working chop - miners and lumberjacks visibly work instead of standing still.

**Held items sat wrong in the hand** (sword crooked through the fist, pickaxe head resting inside the palm): the viewmodels are authored for the first-person camera anchor, not for a skeleton hand. New grip alignment in `PlayerAvatar`:
- The hand frame is derived from the actual finger and thumb bones (valid in any animated pose; the alignment is done once in world space and holds forever because the model is parented to the bone-riding anchor).
- Blade archetype (sword, pickaxe, axe, shovel): shaft along the fist's grip axis - blade side by the thumb, pommel by the pinky - head rolled toward the fingers' forward, fist placed on the actual grip section of each model.
- Gun archetype (pistol, rifle): barrel perpendicular to the grip axis along the fingers' forward, top of the weapon rolled to the thumb side, fist on the pistol grip.
- Palm archetype (grenades, blocks, icon-card items): centered in the palm.
- Primitive fallback bodies keep the legacy placement (no skeleton to align against).

### [14.18.3-dev] Stance On The Record

**Type:** PATCH - diagnostics only, no behaviour change.

The one-shot anim check prints ~4 seconds after an avatar spawns - with empty hands at that moment it always reads stance=0, which says nothing about whether the sword stance engages later. The driver now logs every stance TRANSITION as it happens ("[Crusader] stance -> 1 (sword) swordClips=loaded"), including whether the pack clips actually loaded, plus one line for the first replicated attack per stance session. Holding the sword and watching the Console now gives an immediate, unambiguous answer.

### [14.18.2-dev] Steel You Can See

**Type:** PATCH - held items showed as flat icon squares instead of real objects.

**Cause:** `BuildViewmodelFor` had a full set of procedural 3D builders - sword, pickaxe, axe, pistol, rifle, grenade, block cube - but the icon-card branch returned early for every item that has an icon, which is every item the setup wizard produces. The 3D builders were unreachable dead code, so every held item rendered as a flat square, in first person and in other players' hands alike.

**Fix (`HeldToolView.cs`):**
- Real shapes now come first: melee weapons show the sword (pommel, grip, crossguard, tapered blade with fuller), pistols/rifles/grenades their models, pickaxes and axes their tiered tool shapes, block items a textured cube.
- New shovel viewmodel (shaft, T-grip, angled spade blade) - shovels no longer masquerade as swords.
- The icon card remains as the fallback for gadgets and shapeless items (igniters, canisters, scanners, materials), and a tinted sphere only when even the icon is missing.
- Applies everywhere the viewmodel pipeline is used: first-person hand AND remote avatars.

### [14.18.1-dev] A Sword Is A Sword

**Type:** PATCH - the sword stance never activated (stance=0 with a sword in hand).

**Cause:** the stance keyed on the held item's `toolType`, but `Weapon_IronSword.asset` on disk still carries the constructor default `Other` - the serialized value only updates when the setup wizard is re-run, and the asset predates the wizard line that assigns `Sword`.

**Fix:** stance classification now goes by CLASS first - any melee `WeaponItem` activates the sword stance, with the `ToolType.Sword` check kept as a fallback for plain tool items. Stale asset values can no longer silently disable the stance. Re-running Tools - Voxel Engine - Voxel Engine Setup also reasserts the correct toolType on the sword asset (non-destructive), but the code no longer depends on it.

### [14.18.0-dev] Sword In Hand

**Type:** MINOR - weapon stances on the avatar, from the sword-and-shield pack. Save-compatible.

**Sword stance (`CrusaderAnimator`).** While a player holds a sword, other players see the sword-and-shield animation set instead of the base one: idle, walk, run and jump are redirected to the pack clips (slots fall back to the base clips per-file if any pack clip is missing). The stance is derived from the already-synced held item (`ToolType.Sword`) - zero new stance wire data. Empty hands or any non-sword item returns to the base set. Low-health sad idle still wins over the stance idle - a badly hurt crusader looks hurt, sword or not.

**Visible attacks.** Every swing now replicates: `HeldToolView` counts its swings, `PlayerAvatar` mirrors the counter through a light server RPC, and remote avatars play the pack's slash clip full-body once per swing (sword stance only for now - rifle/pistol will reuse the same channel). Attack yields to airborne, releases back into locomotion just before the clip ends, and joiners never replay pre-join history.

**Manual step (one multi-select, in Unity):** select ALL the FBX files inside `Resources/PlayerAnimations/Pro Sword and Shield Pack` - Rig tab - Animation Type: **Humanoid** - Apply. No Loop Time ticks needed - the driver loops by hand since 14.17.1.

**Next:** rifle and pistol stances the moment those clips land in `PlayerAnimations`, then milestone 7: proximity chat.

### [14.17.1-dev] The Crusader Lands

**Type:** PATCH - fixes the avatar being stuck in a frozen mid-air "falling" pose.

**Root causes, all in the new locomotion driver:**
- All six clip playables advanced from graph start even at zero weight, so any clip without Loop Time finished within seconds and froze on its final frame - by the time a state was shown, you saw a stale frozen frame (a mid-air frame also floats the hips, hence the "constant falling" look).
- The spawn snap (avatar teleporting to its first network position) read as an enormous vertical speed, so JUMP won the state machine right at spawn.
- Jump and slide never rewound to frame zero when entered.

**Fixes:**
- Cyclic clips (idle, sad idle, walk, run, slide) are now looped BY HAND in the driver - the import-side Loop Time tick no longer matters at all (manual step removed for good).
- Teleports (over 3 m in one frame) reset the motion measurement instead of reading as falling; a short settle grace after spawn/teleport blocks phantom jumps.
- Jump and slide replay from frame zero on entry; jump holds its landing frame instead of wrapping.
- Animator culling forced to AlwaysAnimate - rescaled Mixamo skinned bounds are not trusted to keep the skeleton updating.
- Self-diagnosis: a console warning if any clip is not imported as Humanoid, and one "[Crusader] anim check" line per avatar a few seconds after spawn with measured speed, vertical speed and active state.

### [14.17.0-dev] The Crusader Moves

**Type:** MINOR - milestone 6 finale: real animations on the rigged avatar. Save-compatible.

**Runtime locomotion (`CrusaderAnimator`, new).** The avatar plays the Mixamo clips from `Resources/PlayerAnimations` through a runtime PlayableGraph - no AnimatorController asset, no editor wiring, self-healing like the rest of the avatar (missing clips = bind pose, exactly as before):
- **Idle**, **sad idle** below 35% health, **walk/run** blended continuously by measured speed, **running slide**, and **jump** while airborne.
- Motion is derived from the avatar's own network-moved transform, measured against the avatar's OWN up axis (spherical planets - world up is meaningless almost everywhere). Only one new wire bit exists: the slide flag on the pose RPC.
- Root motion stays OFF - the slide and the weapon-pack clips are not authored in place, and the network transform owns all movement; humanoid retargeting drops the root translation cleanly.
- Crouch and slide are separate now: sliding plays its animation WITHOUT the crouch squash.
- The building pose is re-applied after animation every frame, so the raised arm survives the animator's bone writes and still breathes with the underlying clip.
- Owner avatars stop their graph entirely - nobody pays for animation they cannot see.

**Manual steps (2 minutes, in Unity):**
1. Move the `PlayerAnimations` folder into `VoxelEngineAssets/Resources/` (drag inside Unity so the metas follow) - the code loads `Resources/PlayerAnimations/<file>`.
2. Select ALL the animation FBX files (the six clips; the sword pack can wait) - Rig tab - Animation Type: **Humanoid** - Apply.
3. Select Idle, Sad_idle, Walking, Running, Running_slide - Animation tab - tick **Loop Time** - Apply. (Jumping stays unlooped.)

**Next:** weapon stances from the sword-and-shield pack (plus rifle/pistol once those clips land), then milestone 7: proximity chat.

### [14.16.0-dev] Backpacks And Blueprints

**Type:** MINOR - milestone 6 continues: back gear display and the replicated building preview with the arm-out pose. Save-compatible; no editor step.

**Back gear, shown only when carried (`CrusaderModel`, `PlayerAvatar`).** The jetpack (dark pack, orange trim, twin nozzles) and the oxygen tank (white bottle, cyan cap) now appear on every player's back exactly when the real equipment sits in their equipment slots:
- Two flag bits ride the existing change-only pose RPC - zero extra traffic when nothing changes; late joiners get it with the baseline.
- The gear mounts on the back anchor, which now rides the spine bone on the rigged body (like the tattoos), so it will follow the chest once animations land. Built lazily from primitives on first need.

**The replicated building preview (`BuildSystem`, `PlayerAvatar`).** When a player aims a placeable block, everyone nearby now sees WHAT they are about to build and WHERE:
- `BuildSystem.TryGetGhostState` reports the local preview (item id + pose); the avatar mirrors it at up to 10 Hz with a 5 cm / 2 degree deadband - showing or clearing always sends immediately, and nothing at all is sent while no preview is up.
- Remote machines instantiate the block's placed prefab through the new `BuildSystem.CreateRemoteGhost`: same strip logic and `IsCreatingGhost` guard as the local ghost (colliders off, behaviours dead, never simulated), tinted translucent CYAN so it never reads as your own green/red preview.
- Ghost pose snaps cell-to-cell exactly like the builder sees it.

**The building pose.** While a player has a preview up, their avatar raises its right arm:
- On the rigged body the right upper-arm BONE swings from wherever the bind pose put it to forward-and-slightly-down - an axis-agnostic world-space swing, so it works whatever bone axes the FBX shipped with. The rest rotation is remembered on the bone and restores exactly when the preview clears.
- The primitive fallback rotates its arm pivot the same way. The held tool rides the hand anchor through the pose on both bodies.

**Milestone 6 remaining:** animations on the rig (idle/walk - needs animation clips on the FBX), then milestone 7: proximity chat.

### [14.15.3-dev] Measured By The Skin

**Type:** PATCH - the avatar was still floating, slightly too small, ink off the chest; standing players jittered up and down on everyone else's screen.

**FIX - the model is now measured from its real baked vertices (`CrusaderModel`).** `renderer.bounds` on a skinned mesh is not the skin: it is the import-time conservative box carried around by the root bone, and it read too big in every axis - which simultaneously made the avatar too small (height normalization divided by too much), floated it (the box bottom sat below the actual feet) and pushed the tattoos forward (the box front sat ahead of the actual chest). The rig is now measured by baking each skinned mesh exactly as displayed (`BakeMesh` - creates a readable copy, no Read/Write import flag required) and taking min/max plus the chest-band front from the true vertex positions. Feet on the pivot, full 1.85 m, ink on the pecs - all from the same ground truth.

**FIX - standing players no longer shake (`PlayerAvatar`).** The owner's avatar mirrored the player transform raw, every frame - and the character controller's ground snap makes a standing player's Y micro-oscillate, so the oscillation was broadcast and replayed on every other screen (most visible standing on structures). The mirror now has a deadband: only real movement (more than ~1.6 cm or 0.4 degrees) moves the networked avatar. Walking, jumping and turning are untouched; standing still is now truly still on the wire.

### [14.15.2-dev] Feet On The Ground, One Panel Only

**Type:** PATCH - three bug fixes: floating players, tattoos at the shins, and the Item-Ports panel duplicating on every click.

**FIX - players stood a meter in the air with the ink at their shins (`CrusaderModel`).** The 14.15.1 bounds change was wrong for skinned meshes: a skinned mesh is displayed where its BONES put it, and the renderer-local bounds pushed through the renderer's transform describe somewhere else entirely (Mixamo rigs often carry armature scale the renderer node knows nothing about). Scale, ground offset and tattoo anchors all inherited the error.
- The rig is now measured BEFORE parenting, at the origin with identity rotation: there `renderer.bounds` - the truthful, bone-driven box - IS model space, and the avatar's spawn rotation cannot inflate anything. Height, centering and the feet-on-pivot offset are exact again.
- The chest-front sample maps the mesh's vertex cloud onto the displayed box per axis before filtering the chest band, absorbing any armature/mesh scale mismatch. Toes and T-pose arms still never set the reference.

**FIX - Item-Ports panel duplicated itself on every face click (`GameUIController`).** The overlay rebuilt its body by removing the previous copy BY REFERENCE; any copy the reference had lost track of survived and stacked below the fresh one - the phantom second "ITEM PORTS" section showing the pre-click state. The rebuild now clears the scroll content wholesale (nothing can survive) and carries a re-entrancy guard, so exactly one widget exists no matter what triggers a rebuild.

**FIX - port edits from the other player now appear live in the open Item-Ports overlay.** The overlay was exempt from the remote-repaint path (14.12.1) to protect in-progress interaction, which also meant the other side's edits never showed until reopen - "doesn't auto update". `RefreshOpenPanels` now rebuilds the mounted overlay body in place, skipping only while the player is typing in a filter box so a remote edit can never eat their input. Scroll position survives the repaint.

### [14.15.1-dev] Ink On Skin

**Type:** PATCH - visual fix: the tattoos floated in front of the rigged body.

**Why they floated (`CrusaderModel`).** Two compounding causes:
- The model's bounding box was assembled from world-space AABB corners, which inflate whenever the avatar's root is rotated at build time - and on a spherical planet it almost always is. Depth (and height) read larger than the body actually is.
- Even a perfect whole-body box is the wrong depth reference for a chest decal: in bind pose the TOES poke a good decimeter further forward than the pecs.

**The fix.**
- Bounds now come from each renderer's LOCAL bounds pushed through the renderer-to-root matrix chain - exact regardless of spawn rotation. This also makes the 1.85 m height normalization precise.
- The tattoo depth is sampled from the body mesh's bind-pose vertices inside the chest band (torso x-range only, so T-pose arms never count): the ink and the brand rune now sit 8 mm off the actual skin. The AABB half-depth remains as fallback when the mesh is not readable.
- Both tattoos re-parent onto the spine bone (Spine2, then Spine1, then Spine) with their world pose kept - when animations land, the ink moves with the chest instead of hanging in the air.

**One import checkbox (recommended):** select `Player.fbx` - Model tab - enable **Read/Write**. The editor can read the mesh either way, but standalone builds need this for the exact chest depth; without it they fall back to the slightly-forward AABB estimate.

### [14.15.0-dev] The Host Breathes And The Body Is Real

**Type:** MINOR - critical join-freeze fix plus the rigged player body with customizable skin. Save-compatible.

**FIX - host no longer freezes while a client is connected (`RegionFile`, `ChunkStorage`, `SphereWorld`).** The logs told the whole story: every chunk a joiner uploads made the host call `HasLocalEdit`, which read the ENTIRE region file from disk - on the main thread, once per chunk, while the background chunk writer was flushing the same files. Result: thousands of whole-file reads (the freeze) plus reader-vs-writer sharing violations in both directions (the `[ChunkStorage] Failed to flush` / `[RegionFile] Failed to read` spam).
- All region file I/O now goes through one lock, so the writer thread and the main thread can never open the same file against each other again. Both failure messages disappear.
- `HasLocalEdit` is served from `ChunkStorage`'s existing region read cache: ONE disk read per region instead of one whole-file read per chunk, and the writer keeps cached regions fresh on every flush, so the authority answer stays correct through the whole session.
- Net effect: the join catch-up costs a handful of cached region reads instead of a disk storm; the host stays responsive with clients connected.
- The `EditorWindow.Close` NullReferenceException in the same log is Unity editor noise, unrelated to game code.

**The rigged body (`CrusaderModel`).** The avatar now prefers the rigged character at `Resources/Player.fbx`:
- Instantiated at runtime, auto-scaled to exactly 1.85 m with feet on the pivot, centered, colliders stripped - no editor step and no prefab change beyond moving the FBX into a Resources folder.
- Both tattoos project onto it: the brand rune in faded red on the upper chest/shoulder, and the chest ink "The lion with little pecker develops big roar - CalleTheLion".
- The held tool anchors to the rig's right-hand BONE (found by name, finger bones excluded), so when animations arrive the tool rides the hand for free.
- Armor plates still overlay by tier when a suit is worn; the arm-mounted plates are skipped on the rigged body because bind-pose arms would not line up with fixed plates.
- The 14.14.0 primitive warrior remains as the automatic fallback whenever the resource is missing - nothing ever breaks, including before the FBX move is done.

**Customizable skin (`PlayerIdentity`, `PlayerAvatar`, pause menu).** Six skin tones, picked via swatches on the multiplayer page right under YOUR NAME:
- Stored per instance slot like the player name; applies live - the pose mirror picks the change up within a tick and restyles the body for everyone.
- A new `_skinTone` SyncVar rides the change-only pose RPC (held item, crouch, health, armor, now skin). Late joiners get it with the baseline.
- On the rigged body the tone tints the character's materials; on the primitive fallback it recolors the bare-skin parts. Tattoos and armor keep their own colors.

**Milestone 6 remaining (next rounds):** jetpack + oxygen tank shown when equipped, the arm-out building pose with a replicated building ghost, then animations on the new rig.

### [14.14.0-dev] Bare Skin And Honest Steel

**Type:** MINOR - milestone 6 continues: worn-armor display on the crusader, plus the machine I/O port config fix. Save-compatible (additive save fields only).

**FIX - machine input/output config now syncs AND persists (`WorldStatePersistence`).** Root cause found: the per-face port configuration on machines (`PortConfig` - power/data/fluid/gas direction, network type, enabled) was never captured ANYWHERE. It did not replicate to other players, and it did not even survive a save/load on a single machine - every port edit silently reset. Item-port routing (chests, filters) was always fine; only the face config was orphaned.
- `SavedPlacedBlock` gains an additive `hasPortConfig` flag plus a `facePorts` list (fixed six-face order, deterministic payload). Legacy saves leave the flag false and behave exactly as before.
- Capture rides `CaptureFactoryRuntime`, so the existing machine sync (14.12.0) picks up every port edit automatically - the change detector sees the new payload and broadcasts it like any other machine runtime change.
- Restore applies through `PortConfig`'s own setters, so each face refreshes its indicator and all four network managers (power, gas, item pipe, fluid) go dirty exactly as if the edit had been made locally - cables and connections rebuild on the remote machine the same frame.
- Open machine panels repaint via the 14.12.1 `RefreshOpenPanels` path, so the port cards update live while you watch.
- Bonus: port config now survives save/load for the first time.

**The bare warrior (`CrusaderModel` rebuilt).** Armor is DISPLAY now, not identity. The base body is a realistic bare-chested warrior - muscular build with pecs, abs and delts, chain briefs, calf-high worn-leather boots, dark hair, full beard and readable eyes - so an unarmored player actually looks unarmored:
- **The brand rune**, tattooed in faded red on the left shoulder, drawn from thin ink strokes riding just off the skin.
- **The chest ink**, small and dark across the chest: "The lion with little pecker develops big roar - CalleTheLion".
- Still built entirely at runtime from primitives on the existing avatar prefab - no editor step, self-healing after any FishNet reimport. Same 1.85 m feet-pivot proportions, same `RightArmPivot`/`RightHand`/`BackAnchor` anchors, so held tools, crouch and the upcoming building pose are untouched.

**Worn armor, shown only when worn (`PlayerAvatar` + `CrusaderModel.SetArmor`).** Every plate lives on dedicated armor rigs (body rig + a right-arm rig riding the pose pivot, so plates follow every future arm pose), inactive until a suit is equipped:
- A new `_armorTier` SyncVar rides the existing change-only pose RPC (held item, crouch, health, now armor). Zero extra traffic when nothing changes; late joiners get it with the SyncVar baseline.
- Tier reads at a glance: 1 quilted cloth, 2 hardened leather, 3 iron, 4 steel, 5 gilded, 6 dark void-metal - one shared material per tier.
- The great helm (crown band, cross face opening), tabard and red crusader cross only appear armored; hair and beard tuck away under the helm and return when it comes off.
- Same host-double-fire guard as every other SyncVar (`asServer || IsOwner`).

**Milestone 6 remaining (next rounds):** jetpack + oxygen tank shown when equipped, the arm-out building pose with a replicated building ghost.

### [14.13.0-dev] A Real Crusader At Last

**Type:** MINOR - multiplayer milestone 6 begins: the Real Crusaders player model. Save-compatible.

**The knight (`CrusaderModel`).** The placeholder capsule gives way to a procedural crusader, built at RUNTIME from primitives on the existing avatar prefab - no editor step, no prefab change, self-healing after any FishNet reimport, and consistent with the game's procedural viewmodel style. 1.85 m, pivot at the feet, matching the controller exactly:
- **Great helm** with a gold crown band and the classic cross-shaped face opening (horizontal eye slit + vertical breath slit) - the face reads which way a player is looking better than the old visor cube ever did.
- **White tabard over a steel cuirass with the red crusader cross front AND back**, pauldrons, mail arms and legs, faulds, leather belt.
- **The right arm hangs from its own pivot** and the held tool now rides in the knight's right hand - same HeldToolView models as before, and the arm pivot is exactly where the building pose (later this milestone) will take hold.
- **A back anchor** marks where the jetpack and oxygen tank mount when equipment display lands (next step).
- Crouch squashes the knight the way it squashed the capsule; nameplate and health bar are untouched; owners still never see their own body.
- Built in `Awake`, before any SyncVar callback can land - a late joiner's held-item update can arrive before `OnStartClient`, and the hand must already exist.

**Milestone 6 remaining (next rounds):** worn-armor display readable by tier and type, jetpack + oxygen tank shown when equipped, the arm-out building pose with a replicated building ghost.

**GitHub title:** `[14.13.0-dev] A real crusader at last`

**Manual steps:**
1. Pull `Dev`, recompile. No setup steps - the knight builds itself on the existing avatar prefab.
2. Look at the other player: a knight with helm, tabard and cross stands where the capsule was, facing the way they face.
3. Held check: they switch hotbar items - the tool appears in the knight's right hand.
4. Crouch check: they crouch - the knight squashes; stand - it recovers.
5. Vitals check: nameplate and the hurt-only health bar behave exactly as before.

### [14.12.1-dev] Panels Repaint When The Other Side Edits

**Type:** PATCH - bug fix: remote machine edits did not repaint an already-open panel (Thomas's find).

**The bug.** Container slot changes repaint open panels through `ItemContainer.OnChanged` - which is why item movements always showed up live. But machine runtime fields (recipe locks, on/off toggles, progress) are plain field writes with no change event: a host staring at an open assembler panel while a client locked a recipe saw nothing until closing and reopening the panel.

**The fix.** `GameUIController.RefreshOpenPanels()` - a public repaint entry that does nothing when no panel is open. Every remote apply now calls it: machine runtime applies (the reported case) and container applies too, because chest port config and drawer state also land silently outside `OnChanged`. Both players can now stare at the same panel and watch each other's edits arrive.

**GitHub title:** included in `[14.13.0-dev] A real crusader at last`

### [14.12.0-dev] The Factory Runs For Everyone

**Type:** MINOR - multiplayer milestone 5, the heart: machine runtime state over the wire. Save-compatible.

**The gap.** The factory LOOKED identical (14.9.0) and HELD identical items (14.10.0) - but every machine ran its own private simulation. Smelt progress, active batches, locked recipes, tank contents, catalyst beds, reactor temperatures: all diverged silently between machines, with containers snapping to the host's outcome as the only visible symptom.

**Machine sync (`MachineSync`, riding the second save-system seam).** The save system captures and restores the LIVE runtime of every factory block through one pair: `CaptureFactoryRuntime` / `RestoreFactoryRuntime`. That payload carries the active batch + locked recipe + every tank + machine-specific numbers of any `IMachineProcessState` machine (furnaces, electric furnaces, oil refineries, distillation plants, catalytic crackers, pumpjacks, chemical plants, flare stacks), crusher and assembler progress + on/off toggles, fluid tank and pump levels, funnel and splitter buffers, defense turret runtime, armor station progress, lighting and maritime port config. It now travels as opaque JSON (`WorldStatePersistence.CaptureMachineRuntimeJson` / `RestoreMachineRuntimeJson`) - identical architecture to container sync, zero per-machine special cases, future machines sync automatically.

**Authority - the proven rules, one deliberate difference:**
- The HOST announces every runtime change; the host's simulation is truth. Clients KEEP simulating between updates - that is what keeps progress bars moving smoothly - and are converged onto the host's outcome every poller pass (~2.5 s, slower than containers because active machines change every pass by definition; the pass length IS the steady-state bandwidth knob).
- A CLIENT announces a block's runtime only inside the interaction window (recipe locking and machine toggles are panel actions). One window covers both seams - interacting with a machine syncs its items AND its settings.
- Join merge: host runtime always overwrites the joiner's; the joiner's upload only lands on blocks whose host runtime is NOT busy (fresh merged solo machines keep their batches), and only accepted records are redistributed.

**Placement payloads (the small bonus that closes another gap).** Placing a block is a player action, so placement now opens the same interaction window - a pre-filled tank or a packed drawer placed by ANY machine announces its birth state through the container/machine pollers instead of silently swallowing it into the change-detection baseline.

**Deliberate exclusions (documented):**
- Belt/chute item lists churn every frame - live sync strips them, only the one-time join snapshot carries them. The packets you SEE mid-belt are local cosmetics; the flow itself converges at the endpoints through container + runtime sync.
- Power flow is not a payload: cables, machines and toggles replicate, so every machine derives the same power network locally.
- Clients still simulate (this is convergence, not lockstep). A machine panel may show a value settle to the host's number within a couple of seconds of opening it. True client-sim-off is dedicated-server territory (milestone 8).

**GitHub title:** `[14.12.0-dev] The factory runs for everyone`

**Manual steps:**
1. Pull `Dev`, recompile. No setup steps.
2. Smelt check: host runs a furnace - the client opens it and sees the same recipe, fuel burn and progress (within a couple of seconds of drift).
3. Recipe check: client locks a recipe on an assembler panel - the host's panel shows the same lock; toggle a machine off - it stops for both.
4. Tank check: run a pumpjack or refinery - tank litres track on both machines; a placed PRE-FILLED tank arrives full for everyone.
5. Join check: host mid-smelt, mid-refine, splitters buffering - a joining player finds every machine mid-batch with matching progress, and belts carrying items.
6. Merge check: client builds a running solo factory, joins - its machines arrive on the host with their batches intact (host had no state for them).
7. Expected quirk: mid-belt packets may differ between machines - the items arriving at the far end do not.

### [14.11.0-dev] Loot On Common Ground

**Type:** MINOR - multiplayer milestone 5 continues: dropped items over the wire. Save-compatible.

**The gap.** Physical world drops - mined block spills, a destroyed chest's contents, manual drops, creature loot, inventory overflow - existed only on the machine that created them. Your teammate could stand in a field of loot and see bare grass; worse, the same loot could effectively exist twice.

**Drop sync (`DropSync`, two seams).** Every drop path in the game funnels through `DroppedItem.Spawn` (creation) and `DroppedItem.Despawn` (every consumption: expiry, full pickup, conveyor insert). Both are now announced:
- **Identity by wire id, not position.** Drops are rigidbodies - they roll, bounce and slide on ice - so positional identity would break instantly. Every drop carries a `playerId:serial` id, assigned fresh on every pooled spawn so a reused entity can never leak a stale identity.
- **Spawn** replicates with the full stack payload as save-format JSON (`WorldStatePersistence.CaptureStackJson` / `RestoreStackJson`) - durability, charge, liquid payloads, packed drawers arrive intact. Toss physics stays local per machine (cheap, close enough for a tumbling cube); the spawning machine's one-time **settle** announcement then converges the exact rest position everywhere.
- **Pickup and belt inserts** are ownership-blind: whoever consumed the drop reports it - full consumption removes it everywhere, partial pickups and partial belt inserts shrink the stack everywhere. The world-drop budget stays honest on every machine.
- **Authority note:** every `DroppedItem.Spawn` caller was audited - all are local player actions or local destruction events, never background simulation running on multiple machines - so every machine safely announces the drops it creates. No duplicate source exists.
- **Join merge:** a chunked `DropSnapshotBroadcast` rides the handshake between the container and terrain snapshots, id-deduplicated, with settled drops frozen at their exact rest position. Pre-session solo drops get their id at gather time and merge in both directions.

**Known limits (documented):** two players grabbing the SAME drop in the same instant can each receive it - the removal broadcasts simply cross on the wire (co-op stakes, vanishingly small window). A drop sitting on a conveyor feeds whichever machine's belt sim grabs it first; belt contents themselves still diverge until machine runtime sync lands - which is now the last big piece of milestone 5, together with placement payloads.

**GitHub title:** `[14.11.0-dev] Loot on common ground`

**Manual steps:**
1. Pull `Dev`, recompile. No setup steps.
2. Drop check: throw a stack out of your inventory - the other player sees it land in the same spot (small toss differences snap together the moment it settles).
3. Pickup check: teammate walks over your drop - it vanishes for you, appears in their inventory. No double-loot.
4. Partial check: with a nearly full inventory, walk over a big stack - the remainder lying on the ground shows the same count on both machines.
5. Fidelity check: drop a damaged tool - the pickup on the other machine has the same durability.
6. Break check: destroy a filled chest - the spilled contents appear for everyone.
7. Join check: leave drops on the ground, have a player join - the drops are there, resting exactly where you see them.

### [14.10.0-dev] What The Chest Holds

**Type:** MINOR - multiplayer milestone 5 continues: container contents over the wire. Save-compatible.

**The gap.** 14.9.0 made the factory LOOK identical everywhere - but every chest, furnace and drawer was an empty shell on the other machine. Items existed only where they were deposited.

**Container sync (`ContainerSync`, riding the save-system seam).** Every well-known container - chests (including their Item-Port face config and filters), storage drawers (payloads, upgrades, controller state), furnace fuel/input/output, electric furnaces, crushers, assemblers, armor stations, pumpjacks and the rest - now replicates through ONE seam: the exact capture/restore path the save system already uses, carried as opaque JSON (`WorldStatePersistence.CaptureContainerJson` / `RestoreContainerJson`). Full fidelity for free - durability, charge, liquid payloads, packed drawers - with zero per-machine special cases, and any container the save system learns about in the future syncs automatically.

**Authority (the 14.8.1 rule applied to items).**
- The HOST announces every container change - machines still simulate on all peers, but only the host's outcome is truth.
- A CLIENT announces a block's container only in a short window after the local player interacted with that block (refreshing while a panel stays open) - so your deposits and withdrawals replicate, while your machine's own churn never fights the host.
- Applies are whole-container overwrites (the wire format carries one entry per slot, empties included), echo-guarded by baseline tracking: an applied remote state is never mistaken for a local change.
- **Join merge:** a chunked `ContainerSnapshotBroadcast` rides the handshake between the block and terrain snapshots. Host containers always overwrite the joiner's; the joiner's upload only fills containers the host has EMPTY (the contents of freshly merged solo blocks), and only accepted records are redistributed.

**Change detection without plumbing.** A slow round-robin poller (a few blocks per frame, a full pass at most every 0.75 s) captures each block's container JSON and compares strings. The pass cadence is the debounce; no per-container event wiring, no missed mutation path - anything that changes a container is caught, whatever code changed it.

**Known limits (documented, next up):** machine runtime state (smelt progress, recipes, power flow) still simulates per-machine, so a client standing at an actively running machine may see its contents snap to the host's outcome now and then - that disappears when machine simulation itself becomes host-authoritative. Dropped items and placement payloads remain unsynced.

**GitHub title:** `[14.10.0-dev] What the chest holds`

**Manual steps:**
1. Pull `Dev`, recompile. No setup steps.
2. Chest check: host drops items into a chest - they appear in the client's view of that chest (open it, contents match). Client deposits into the same chest - the host sees them.
3. Fidelity check: store a damaged tool and a charged power cell - durability bar and charge match on the other machine. Configure a chest's item ports - faces and filters match remotely.
4. Furnace check: host smelts - input shrinking and output growing replicate to a watching client.
5. Join check: fill chests, disconnect, rejoin - contents are there. Client's solo-built chest merges in WITH its items (host had no such container).
6. Expected quirk: a client watching its own copy of a RUNNING furnace may see contents snap to the host's version occasionally - machine runtime sync is the next milestone-5 step.

### [14.9.0-dev] Machines On Every Machine

**Type:** MINOR - multiplayer milestone 5 begins: item-block replication. Save-compatible.

**The gap.** Only tiered pieces replicated - a furnace, chest, conveyor line or power cable existed solely on the machine that placed it. The other player saw an empty floor where your factory stood.

**Item-block sync (`BlockSync`, the third seam).** Static world blocks placed from the hotbar now replicate live and on join, mirroring the proven BuildingSync pattern:
- **Live:** placement (hooked at the single authority point in `BuildSystem`), surviving damage with hp + cracks, and destruction (both hooked inside `PlacedBlock.Damage`, covering every caller - mining, explosions, everything). Same uniform wire path, echo-guarded, mismatch-silent.
- **Looks-right guarantee:** the placement-time cosmetic choices travel with the block - conveyor build shape (with an immediate topology refresh so belt runs connect visually), power cable variant + segment length (with visual rebuild and neighbor refresh), road placement refresh, texture/material overrides, and a power-topology dirty mark so cable networks re-form on the far side.
- **Join merge:** a chunked `BlockSnapshotBroadcast` rides the same handshake as pieces and terrain - two-way, duplicate-guarded (itemId + 25 cm), hp-converging on rejoin.
- **Grid exclusion:** blocks attached to movable grids (ships, vehicles) are excluded exactly like the save system excludes them - grids are their own milestone.
- New API: `WorldStatePersistence.FindBlockById(string)`, completing the lookup trio (items, tiered families, block items).

**What this deliberately does NOT sync yet (the heart of milestone 5, next):** container contents, machine runtime state (recipes, smelt progress, power flow, quarry state), placement payloads (a pre-filled tank arrives empty), and dropped items. The factory LOOKS identical everywhere; it does not yet RUN identically.

**GitHub title:** `[14.9.0-dev] Machines on every machine`

**Manual steps:**
1. Pull `Dev`, recompile. No setup steps.
2. Live check: place a furnace, a chest, a few conveyor segments (mix straight/corner/ramp) and a power cable run - all appear on the other machine with the same shapes, orientations and cable style.
3. Damage check: hammer a placed block without destroying it - cracks match remotely; destroy it - it vanishes on both (drops go to the breaker only).
4. Join check: build a small factory while the client is offline; client joins - the whole line is standing there.
5. Merge check: client builds its own machines offline, joins - they appear on the host too.
6. Grid check: blocks on a ship/vehicle grid stay local (expected - grids are a later milestone).

### [14.8.1-dev] The Server Wins The Terrain Merge

**Type:** PATCH - bug fix: host terrain did not come down on join (Thomas's find).

**The bug.** The 14.8.0 merge rule was "local edits win" on BOTH sides. But live terrain sync marks every replicated chunk as modified and saves it to the receiving machine's own chunk store - which is exactly right for persistence, and exactly wrong for that merge rule: after one prior shared session, the client owns a STALE COPY of every chunk around the base, so every newer host edit to those chunks was silently refused. The host's offline pit never arrived; the client's fresh marks (in chunks the host never touched) went up fine - which is precisely the asymmetry observed.

**The fix - the authority rule the architecture already promised (server-authoritative everything):**
- A CLIENT receiving on the terrain channel is receiving server-approved truth: it now ALWAYS overwrites, stale copy or not. Identical chunks from a rejoin overwrite harmlessly.
- The SERVER keeps the local-edit filter when ingesting a joiner's upload (the host world is the authority), and now relays ONLY the chunks it accepted - a joiner's stale copies can never leak to the other clients (previously every uploaded chunk was relayed regardless).
- Conflict semantics are now clean and convergent: every disputed chunk resolves to the host's version on every machine. A client's solo offline edit in a chunk the host also edited is lost to the host version - documented, and the honest price of authority.

**GitHub title:** `[14.8.1-dev] The server wins the terrain merge`

**Manual steps:**
1. Pull `Dev`, recompile on BOTH machines. No setup steps.
2. Re-run the failing check: host digs a distinctive pit while the client is offline; client joins - the pit is now there, including in areas you both played in before.
3. Re-run the up check: client digs fresh marks offline, joins - host still sees them.
4. Conflict check: both dig the SAME spot differently while apart, then join - both machines end up showing the HOST's version of that spot.
5. Rejoin a few times - no errors, no visual popping.

### [14.8.0-dev] Terrain Catch-Up On Join

**Type:** MINOR - multiplayer milestone 4, phase 2: edited terrain crosses the wire on join. Save-compatible.

**The gap.** 14.7.0 replicated live terrain ops, but anything dug or built while the other machine was offline stayed invisible - a joiner walked over your quarry pit on their own untouched terrain. Now the whole edited surface catches up the moment a seed-matching client joins.

**Edited-chunk exchange.** The chunk persistence layer already knew exactly what to send: only player-modified chunks are ever saved (pristine terrain regenerates from the seed). The join handshake now ships those chunks both ways:
- **Gather** (`SphereWorld.GatherModifiedChunks`): the disk store is enumerated (after flushing pending writes) and live loaded chunks are layered on top - live state wins. One planet per exchange: the body both players share.
- **Wire** (`TerrainChunkBroadcast`): one chunk per broadcast - the full padded voxel grid, deflate-compressed exactly like the region files, planet-tagged. Typical edited chunk: a few KB.
- **Apply** (`SphereWorld.ApplyRemoteChunk`): a LOADED chunk is overwritten in place (gen/mesh jobs completed first for safety) and remeshed; an UNLOADED chunk is parked in the local chunk store via the existing background writer, so it streams in already-edited later - the catch-up covers terrain neither player is even near.
- **Merge rule:** local edits win. After a rejoin both sides hold identical chunks (live sync marks remote-applied chunks modified), so the skip is a no-op; a true both-edited-offline conflict keeps each side's own chunk and is the documented divergence case - same spirit as the building merge.
- **Two-way, like buildings:** the joiner uploads its own solo-dug terrain BEFORE applying incoming chunks (ordered channel - the gather can never echo), and the server relays to everyone else.

**New plumbing:** `ChunkStorage.EnqueueSaveData` (park a serialized snapshot without a live chunk), `SphereWorld.HasLocalEdit`, `TerrainSync.GatherWireChunks/ApplyWireChunk` with deflate helpers.

**Limits (tracked):** the exchange covers the planet both players are on - edits on OTHER planets do not transfer until a shared-planet join happens there; a heavily mined world means a bigger join burst (one reliable broadcast per edited chunk); fluid sim state still per-machine.

**GitHub title:** `[14.8.0-dev] Terrain catch-up on join`

**Manual steps:**
1. Pull `Dev`, recompile. No setup steps.
2. Catch-up down: host digs a distinctive pit while the client is NOT connected; client joins (same seed) - the pit is there, meshed correctly.
3. Catch-up up (the merge): client digs its own marks before joining - after join the host sees them too.
4. Far-terrain check: host edits somewhere far away, client joins near spawn, then travels there - the terrain streams in already edited.
5. Rejoin check: disconnect/rejoin - no visual pop, no console errors (identical chunks are skipped).
6. Crater check: host bombs a hillside offline, client joins - the crater is in the ground (and matches a fresh live blast side by side).

### [14.7.0-dev] The Ground Moves For Everyone

**Type:** MINOR - multiplayer milestone 4 begins: live terrain replication. Save-compatible.

**Voxel brush replication.** The long pole starts with its cheapest, strongest cut: every terrain edit that goes through the `VoxelEditor` brush now replicates - pickaxe mining, terrain building/filling, grid drills, ship drills, road demolition. The design leans on two properties the brush already had:
- **Ops, not data.** A brush op is fully described by five values: integer voxel center, radius, strength, subtract flag, fill material. The brush derives every density delta from the current voxel state, so same-seed worlds that apply the same op stream converge EXACTLY - no chunk data ever crosses the wire.
- **Integer voxel space is floating-origin-proof.** World positions shift when the origin re-anchors; voxel coordinates never do. The op is announced with the exact center voxel the origin machine used, so the remote brush lands on the identical voxels at any distance from spawn.
- Remote ops grant NO drops (the miner keeps the ore), wake the fluid sim (water rushes into remote holes too) and remesh exactly like local edits. `TerrainSync` (new) mirrors the `BuildingSync` seam: no Fish-Net types in gameplay code, echo-guarded, mismatch-silent, planet-tagged - an op for a planet you are not on is skipped.

**Explosion replication.** Blasts are now shared events with a careful split:
- Replicated: the fireball/mushroom VFX, blast light, distance-based camera shake, and the crater (carved by the same shared loop, sent in voxel space).
- Deliberately NOT replicated: damage. Creature/player damage is milestone 5, and piece damage already converges through building damage sync - re-running it remotely would double-apply.

**Docs:** milestone 3 marked done in the roadmap (open item: shared build costs); the plain-text code-lock note is now a committed hardening item under milestone 8 (dedicated server), per Thomas.

**Phase 1 limits (tracked):** live ops on loaded chunks only - terrain edited while the other machine was offline does not catch up on join yet (terrain snapshot / chunk deltas are phase 2); fluid sim state itself is not synced (each machine runs its own water, converging on the same holes); grid-ship voxels are separate.

**GitHub title:** `[14.7.0-dev] The ground moves for everyone`

**Manual steps:**
1. Pull `Dev`, recompile. No setup steps.
2. Mining check: dig with a pickaxe on one machine - the same hole opens on the other, drops go to the digger only.
3. Building check: fill terrain with the build tool - the same bump grows remotely, same material color.
4. Water check: dig into a lake edge - water rushes into the new channel on BOTH machines.
5. Explosion check: detonate anything (grenade, bomb, explosive block) - the other machine sees the fireball, feels the shake if close, and gets the identical crater.
6. Drill check: run a grid/ship drill - the carve replicates while the ore goes to the drill owner's network only.

### [14.6.0-dev] Doors And Locks Over The Wire

**Type:** MINOR - multiplayer milestone 3, phase 3: shared doors, gates, hatches and code locks. Save-compatible.

**Door state replication.** The most visible gap left in building sync: a host could swing a gate open and the client still saw it closed - walking through visibly-shut doors and bouncing off open ones. Every toggle now replicates:
- All four leaf types covered: side-hinged doors, double gates (both leaves), rolling garage shutters and floor hatches (ladder deploys too).
- The swing SIDE travels with the state (`OpenSideSign`), so a door opened away from the host swings away on every machine - not mirrored.
- The announce lives inside `TieredDoor`/`TieredHatch` themselves, so every code path that toggles (direct click, frame click, keypad-gated open) replicates for free. Remote application uses a silent `SetOpenState` that can never echo.
- Doors animate on arrival - the far machine sees the leaf actually swing, gates grind at their heavy constant rate.

**Code lock replication.** Locks are now real multiplayer objects:
- Fitting a lock shows the physical lock on every machine immediately; removing it (refund stays with the remover) takes it off everywhere.
- One idempotent `LockStateBroadcast` (code, locked flag, guest list) covers every keypad authority point: code set, code changed, correct-code guest authorization, lock/unlock toggle. LED color follows everywhere.
- This makes access REAL across machines: a guest who enters the right code on their machine is authorized on the host's world too, and a locked door denies everyone consistently.

**Snapshot completeness.** `PieceSnapshot` now carries door state and full lock state, so a joiner sees open gates open, deployed hatch ladders deployed, and locked doors locked - and on REJOIN, already-present pieces now converge too: hp/cracks, door state and lock state are adopted from the origin instead of being skipped as duplicates.

**Note (pre-release honesty):** lock codes travel and rest in plain text, exactly like they do in save files. Fine for co-op pre-release; hardening belongs to the dedicated-server milestone.

**GitHub title:** `[14.6.0-dev] Doors and locks over the wire`

**Manual steps:**
1. Pull `Dev`, recompile. No setup steps.
2. Door check: open/close a door, a double gate, a garage shutter and a hatch - the other machine shows the same motion, same swing side, ladder included.
3. Lock check: craft and fit a code lock - the lock body appears on the other machine. Set a code - LED goes red everywhere.
4. Guest check: from the OTHER machine, enter the correct code - access granted there AND the guest authorization holds on the host (re-open the door from either side).
5. Join check: with a gate open and a lock fitted, connect a fresh client - gate arrives open, lock arrives red.
6. Remove check: remove the lock from the keypad menu - it disappears on both machines, item refunded to the remover only.

### [14.5.1-dev] Cracks Over The Wire

**Type:** PATCH - bug fix: replicated damage visuals.

**The bug (Thomas's find).** Decay cracks never showed on the other machine: `PlacedTieredBlock.Damage` and `StructuralLoadState.DecayTick` reported cracks locally, but nothing crossed the wire until hp hit zero - so a decaying floor bloomed cracks on the machine running the audit while everyone else watched pieces simply pop out of existence. Partial hammer/explosion hits had the same silent gap, and worse: hp quietly diverged between machines until the next snapshot.

**The fix.** A `PieceDamagedBroadcast` (family, position, hp) now rides the same uniform wire path as place/remove/upgrade. Both surviving-damage sites announce after their local crack report; the far side sets the authoritative hp and reports the same damage fraction, so cracks bloom in step everywhere and hp never diverges. Echo-safe by construction: applying remote damage sets hp directly and never re-enters the announce path. Mismatch-guarded like all building traffic.

**GitHub title:** `[14.5.1-dev] Cracks over the wire`

**Manual steps:**
1. Pull `Dev`, recompile. No setup steps.
2. Decay check: build a pillar-supported deck run, demolish the pillar - BOTH machines now show the cracks blooming across the ~10 s decay before the pieces fall.
3. Partial hit check: hammer a wall a few times without destroying it - the same cracks appear on the other machine hit by hit.
4. Convergence check: after the partial hits, finish the wall from the OTHER machine - it should take the correct remaining hits, not a full-health count.

### [14.5.0-dev] Join In Progress

**Type:** MINOR - multiplayer milestone 3, phase 2: the base snapshot on join. Save-compatible.

**Base snapshot exchange.** 14.4.0 replicated live actions only - anything built before the session stayed invisible. Now the whole standing base crosses the wire the moment a seed-matching client joins:
- **Two-way merge.** The server sends its base to the joiner AND the joiner sends its own solo-built base up; the server applies it and relays to everyone else. Two players who each built alone on the same seed walk into one merged world - the duplicate guard (family + 25 cm) settles any piece both sides had.
- **Handshake-gated.** The world-info check from 14.4.0 now completes as a real handshake: the client acks the seed comparison, and only a match opens the exchange. The client uploads its base BEFORE applying incoming chunks (ordered channel), so it can never echo the host's pieces back.
- **Restore-quality pieces.** Snapshot pieces carry hp - cracks match the origin world on arrival (`BlockDamageVisual`). Railing rise and pillar height ride along (new `TieredRailing.AppliedRise` getter). Everything arrives unarmed and silent - no thunk barrage, no decay audits second-guessing a neighbor's base.
- **Chunked wire.** 32 pieces per broadcast, any base size.

**Mismatch hardening.** A seed-mismatched client previously still exchanged live building traffic - floating pieces on the wrong terrain. Now mismatch means silence both ways: the client neither announces its own building actions nor applies incoming ones (guards in `BuildingSync.ShouldAnnounce` and every client handler). The red warning in the multiplayer tab remains the fix-it prompt.

**Refactor:** `BuildingSync.SpawnRemote` now backs both live placement and the snapshot merge - one instantiation path, one set of rules.

**Still open (tracked):** code locks on pieces do not cross the wire (snapshot or live); voxel edits, machines and item blocks are milestone 4/5; the placer pays costs alone.

**GitHub title:** `[14.5.0-dev] Join in progress - the base snapshot`

**Manual steps:**
1. Pull `Dev`, recompile. No setup steps.
2. Snapshot down: host builds a few pieces while the client is NOT connected, then the client joins (same seed) - the base appears silently on the client, correct tiers/rotations, cracks included if damaged.
3. Snapshot up (the merge): before joining, build a separate base on the CLIENT's world, then join - it appears on the host too, and both machines now show both bases.
4. Rejoin check: disconnect and rejoin - no duplicates.
5. Mismatch check: join with a wrong-seed world - red warning shows, NO buildings transfer in either direction, and pieces placed while mismatched stay local.

### [14.4.0-dev] Shared Ground

**Type:** MINOR - multiplayer milestone 3, phase 1: live building replication plus a world-identity handshake. Save-compatible.

**Building sync (tiered pieces).** For the first time, two players shape the SAME base. Place, upgrade, hammer-demolish and structural collapse now replicate live between every connected machine:
- `BuildingSync` (new) is the seam between the building system and the network: gameplay announces at its three authority points (placement in `BuildSystemV2.Place`, upgrade in `TryUpgrade`, destruction in `PlacedTieredBlock.Damage` and `StructuralLoadState.DecayTick`), the bootstrap carries it over the wire, and `BuildingSync` applies it on the far side. No Fish-Net types touch gameplay code.
- Remote pieces are instantiated exactly like save-restored pieces: correct family/tier/rotation, railing rise and pillar height preserved, and deliberately UNARMED - the local decay audit never second-guesses a neighbor's building. Convergence comes from the origin machine announcing every collapse its own audit decides, so cascades tear down identically everywhere.
- Piece identity is positional (family + snapped position within 25 cm) - the same assumption world saves already make - so no GUIDs, no registry, no schema change.
- Uniform wire path: everyone (host included) sends as a client; the server applies and relays to all other clients. Duplicate-safe, echo-guarded (`IsApplyingRemote`), and a soft placement thunk plays where a teammate builds.

**World identity handshake.** Building sync only means something on matching terrain. On join, the server now tells each client which world it runs (name + seed). Seed mismatch flips a persistent red warning in the multiplayer tab - host world and seed spelled out, with the fix (create/load a world with that seed) - and logs a console warning. Matching seeds join silently.

**New API:** `WorldStatePersistence.FindTieredByFamily(string)` - runtime tiered-definition lookup mirroring `FindItemById`.

**Phase 1 limits (deliberate, tracked in the roadmap):** same-seed worlds are required (warned, not auto-synced); only live actions replicate - bases built before the session are not yet sent to joiners (join-in-progress snapshot is the next phase); voxel edits, machines and item blocks are milestone 4/5; code locks fitted to pieces do not replicate yet; the placer pays the material cost alone.

**GitHub title:** `[14.4.0-dev] Shared ground - building sync phase 1`

**Manual steps:**
1. Pull `Dev`, recompile. No setup steps - broadcasts only, no new NetworkObjects.
2. IMPORTANT: both machines must create/load a world with the SAME SEED (same name recommended). If the client picked wrong, the multiplayer tab shows the host's seed in red - recreate with that seed.
3. Host + join. Place wood pieces on one machine - they appear on the other with a soft thunk, correct rotation, railings/pillars included.
4. Upgrade a piece with the hammer - the other machine swaps it to the same tier.
5. Hammer-demolish a piece - it vanishes on both machines.
6. Collapse test: build a pillar-supported deck run, demolish the pillar - the decay cascade tears down the same pieces on both machines (~10 s).

### [14.3.0-dev] Vitals Over The Wire

**Type:** MINOR - milestone 2 continues: replicated health, plus the nameplate orientation fix. Save-compatible.

**Nameplate fix (the sideways name).** Names billboarded against WORLD up - but the planets are spheres, so away from the pole "up" points sideways and the text rolled with it. Nameplates now level against the VIEWER's camera up, which is the only up that matters for reading text on screen. Correct at any latitude, any camera angle.

**Replicated health bar.** Other players now wear a floating bar under their nameplate:
- Rust-style honesty: INVISIBLE at full health - it appears only when someone is hurt, drains red as they drop, and vanishes when they heal up.
- The bar hangs off the nameplate so it inherits the (fixed) billboard rotation for free.
- Wire format is one quantized integer (0-100), sent only when the value changes, riding the same owner -> ServerRpc -> SyncVar pose channel as held item and crouch - one message per pose change, never per frame. Late joiners get current health in the spawn payload.

**GitHub title:** `[14.3.0-dev] Vitals over the wire`

**Manual steps:**
1. Pull `Dev`, recompile. No setup steps.
2. Nameplate check: orbit around the other player - the name stays horizontal and readable from every direction and latitude.
3. Health check: hurt one player (fall damage, hazard, wrong keypad code...). The OTHER machine sees a bar fade in under their name, red-shifting as health drops; heal to full and it disappears.
4. Late-join check: hurt the host, then connect the client fresh - the bar should already show the current health on arrival.

### [14.2.1-dev] Real Crusaders Locked Into The Roadmap

**Type:** PATCH - documentation only, no code changes.

**Milestone 6 is now the crusader player model**, deliberately placed just before proximity chat: seeing who you meet matters as much as hearing them. The full design lives in `Roadmap.md` ("Real Crusaders - Player Model & Readable Loadout") so any future agent builds against it:
- Humanoid crusader body replaces the capsule; unarmored players show a gambeson underlayer - armor is NEVER painted on by default.
- Armor renders on the model ONLY when worn, attached per equipment slot (Head/Chest/Legs/Back), with tier and type readable at a distance. Held items stay on the existing `HeldToolView` replication.
- Jetpack and oxygen tank render on the back when worn - flight/dive capability readable from afar.
- Building is a visible act: an arm-out building pose (replicated like crouch), and the placement ghost replicated so nearby players see what a teammate is about to place (rides milestone 3's piece plumbing).
- Standing rules: owner never sees own avatar, one ServerRpc per change, everything keyed by stable item ids, unknown items degrade silently.

Proximity chat moves to milestone 7, dedicated server to 8.

**GitHub title:** `[14.2.1-dev] Real crusaders on the roadmap`

**Manual steps:** none - pull whenever convenient.

### [14.2.0-dev] Avatars Come Alive

**Type:** MINOR - milestone 2 (per-player state) begins: the first visible slice is pose replication. Save-compatible.

**Remote players now show what they are doing.** Until now the other player was a sliding capsule; now:
- **Held items replicate.** The avatar carries the owner's active hotbar item in a hand anchor, using the SAME procedural models as the first-person viewmodel (`HeldToolView.BuildViewmodelFor` opened up for reuse) - pickaxes, axes, rifles, pistols, swords, block cubes, icon items. Colliders are stripped so held models never block interaction rays or physics. Unknown items on the receiving side simply show empty hands.
- **Crouching replicates.** Crouch or slide and your avatar squashes to match (body, visor and hand all scale down); stand and it pops back.
- **The flow is MP-clean:** owner watches its local hotbar/stance and calls one ServerRpc ONLY on change; the server writes SyncVars; everyone (including late joiners, via the spawn payload) applies them. No per-frame chatter, no client-to-client trust.

**Supporting changes:**
- `PlayerController`: `IsCrouched` exposed alongside the existing movement-state properties.
- `WorldStatePersistence`: public `FindItemById` runtime lookup (lazy cache build) - avatars and future networking resolve items by stable id through the same catalog persistence uses.
- `NetworkBootstrap`: pure clients see their live ping in the multiplayer menu status line ("Connected - ping 23 ms").

**GitHub title:** `[14.2.0-dev] Avatars come alive`

**Manual steps:**
1. Pull `Dev`, recompile. No setup steps - the avatar prefab is unchanged (hand anchor and pose changes are runtime-driven).
2. Two-instance test: scroll through your hotbar - the other screen shows your avatar's hand switching between the actual tool/block models. Empty slot = empty hands.
3. Crouch and slide - the other avatar squashes and recovers. Sliding shows as crouched (intentional).
4. On the joining client, open the multiplayer tab: the status line now reads "Connected - ping N ms".

### [14.1.4-dev] Unique Identity Per Game Instance

**Type:** PATCH - fixes the one-entry roster / wandering "(you)" marker from same-machine testing.

**Root cause: both test instances were the SAME player.** Unity stores PlayerPrefs per company/product, so every build of the game on one machine reads the same prefs - both instances loaded the same `ve_player_id` GUID. The server keyed both connections by one id: one roster entry everywhere, "(you)" matched on both screens, and a rename by either player renamed "the" player. Two separate machines were never affected.

**Fix 1: per-instance identity slots (`PlayerIdentity`).** At startup each running instance claims a slot through a system-wide mutex. Slot 0 keeps the original pref keys - nobody's existing identity changes - and every additional concurrent instance gets its own suffixed id and name keys. Real players run one instance per machine and always sit in slot 0; two local test builds now are two different people, with independent names.

**Fix 2: server-side duplicate-identity guard (`NetworkBootstrap`).** If a connection ever presents a player id that is already live in the session, the server admits it under a visible guest id and logs a warning - two connections can never silently collapse into one person again, regardless of what a client claims.

**GitHub title:** `[14.1.4-dev] Unique identity per game instance`

**Manual steps:**
1. Pull `Dev`, recompile. No setup steps.
2. Relaunch both instances. The FIRST one launched keeps the existing identity; the second starts fresh as "Crusader" (expected - it finally has its own identity). Rename it in the multiplayer tab.
3. Verify: the roster now shows BOTH players on both screens, "(you)" marks only yourself on each screen, renames update only the renamed player, and each avatar wears its own nameplate.

### [14.1.3-dev] Step 105 Self-Heals Broken Prefabs

**Type:** PATCH - editor tooling fix, no runtime changes.

**Step 105 failed with "You are trying to save a Prefab with a missing script."** That happens when a script GUID stops resolving - typically after Fish-Net (or any package) is reimported, moved or cleaned: components on the avatar prefab keep pointing at the old script ids, and Unity refuses to save a prefab containing missing scripts.

**Step 105 now heals this on its own** (`NetworkSetup`):
- The prefab repair strips every missing-script component from the avatar prefab (root and children) BEFORE saving, then re-adds whatever is required (NetworkObject, NetworkTransform, PlayerAvatar) and reconnects the nameplate.
- The scene wiring does the same for the 'Network' object - and finds it by name as a fallback, so a GUID breakage can never leave a broken object behind with a duplicate beside it. NetworkManager, Tugboat and NetworkBootstrap are re-added as needed and the avatar prefab is reconnected.
- Everything stays non-destructive for healthy assets: nothing is removed unless its script is already gone.

**GitHub title:** `[14.1.3-dev] Step 105 self-heal`

**Manual steps:**
1. Pull `Dev`, recompile.
2. Open the main game scene and re-run Step 105 - it should now complete, logging how many missing-script components it removed.
3. Save the scene (Ctrl+S), then do a quick host/join sanity check.

### [14.1.2-dev] Player Names That Actually Show Up

**Type:** PATCH - fixes the missing player names from the first host/join test, adds live rename, and puts proximity chat on the roadmap.

**Root cause of the missing names:** 14.1.0 baked the identity into the avatar's SyncVars BEFORE the server spawned it, and read them exactly once in `OnStartClient`. Values written before spawn can be treated as defaults and delivered late or never - so the roster registration saw an empty id (and skipped, correctly), and the nameplate kept its placeholder. Two rules fix it for good:
- **`NetworkBootstrap`** now spawns first and applies the identity AFTER - post-spawn SyncVar writes replicate as ordinary reliable updates to current observers and ride the spawn payload for late joiners.
- **`PlayerAvatar`** no longer reads identity once; it reacts to the SyncVars - registration happens the moment the player id lands (spawn payload or later update), nameplates and roster names follow every name change. Unregistration always removes exactly the id that was registered.

**Live rename, everywhere.** The 14.1.0 roster entry was a one-time snapshot - renaming never updated it. Now:
- `PlayerIdentity.LocalName` pushes changes into the local roster (`NetworkSession.UpdateDisplayName`) and, when online, re-announces the identity; the server updates that player's avatar name for everyone (`ServerSetName`).
- The pause menu name field now shows both offline and in-session, and commits on Enter/blur instead of broadcasting every keystroke.

**Roadmap: proximity voice chat is now milestone 6**, before dedicated servers - positional voice with distance falloff, relayed through the server, muted-list keyed by player id; build-vs-buy decision (Fish-Net-integrated voice asset vs custom mic-to-Opus pipeline) when the milestone starts.

**GitHub title:** `[14.1.2-dev] Player names sync fix`

**Manual steps:**
1. Pull `Dev`, recompile. No setup steps - the avatar prefab and scene wiring from Step 105 are untouched.
2. Retest host/join: both machines should now show BOTH players in the pause menu roster (yours marked "(you)") and the correct name above the other player's head.
3. While connected, change your name in the multiplayer tab and press Enter: your roster updates instantly, and within a moment the other machine's roster and your nameplate over there update too.

### [14.1.1-dev] Fix: Fish-Net Assembly References

**Type:** PATCH - compile fix, no behavior changes.

**The 14.1.0 networking scripts could not see Fish-Net.** The project compiles `Scripts/` into its own assembly definitions (`VoxelEngine`, `VoxelEngine.Editor`), and an assembly definition only sees assemblies it explicitly references - Fish-Net lives in its own `FishNet.Runtime` assembly. Both asmdefs now reference `FishNet.Runtime`, which resolves every CS0234/CS0246 error from `NetworkBootstrap`, `PlayerAvatar` and the Step 105 editor tool.

**About the warning wall:** every remaining warning comes from Fish-Net's own package code (obsolete `FindObjectOfType` calls in its demos, `#nullable` annotations in the Synapse transport, and Unity 6's new serialization analyzer inspecting Fish-Net internals). They are harmless, upstream, and not ours to edit - package code must stay untouched so updates stay clean. Optional tidy-up: the `Assets/FishNet/Demos` folder is safe to delete and removes the demo warnings.

**GitHub title:** `[14.1.1-dev] Fix Fish-Net assembly references`

**Manual steps:**
1. Pull `Dev`, let Unity recompile - the errors should be gone.
2. Continue with the 14.1.0 steps: open the main game scene, run Setup Step 105, save the scene, then the two-instance host/join test.

### [14.1.0-dev] Multiplayer Foundation - Part 2: Host and Join

**Type:** MINOR - the Fish-Net bridge. Built and verified against Fish-Net 4.7.3. Save-compatible; no schema changes.

**You can now host your world and see each other move.** One player hosts (listen server), up to 7 more join over LAN or a port-forwarded address. Each connected player appears as a player-sized avatar with a floating nameplate that follows their real position and view direction. World content is NOT synchronized yet - each side still runs its own world; building/voxel/simulation sync are milestones 3-5. This round is the transport spine everything else will ride on.

**New: `NetworkBootstrap` (`Scripts/Networking`)** - the ONLY class that talks to Fish-Net, exactly as README section 4 demands.
- Starts/stops the listen server and client connections (Tugboat transport, default port 7770).
- Identity handshake: after authenticating, a client broadcasts its stable `PlayerIdentity` (id + name) to the server; only then does the server spawn that player's avatar, with the identity baked into the spawn payload. Connection ids never leak out of this file - all state stays keyed by player id.
- Drives `NetworkSession` (Offline / Host / Client) and sweeps remote presences on disconnect. Gameplay code keeps asking `NetworkSession` and never touches the transport.

**New: `PlayerAvatar` (`Scripts/Networking`)** - the networked body of one player.
- Server-spawned, one per connection; `SyncVar` id + name; despawned on disconnect.
- The owning client mirrors its real first-person rig into the avatar every frame; Fish-Net's NetworkTransform replicates it. Owners never see their own avatar. Nameplates billboard toward whoever is looking.
- Keeps the `NetworkSession` player registry in step with spawns/despawns - on clients AND on (future) dedicated servers.
- Movement is owner-authoritative for now (standard NetworkTransform); server-side movement validation is a later hardening pass.

**Pause menu gets a MULTIPLAYER page** (`InGamePauseMenu`), same theme as everything else:
- Offline: your player name, HOST THIS WORLD, and an address field + JOIN GAME.
- Online: live status line, live player roster, DISCONNECT.
- The pause menu no longer freezes time while a session is running (the world lives on the server); offline pause behaves exactly as before. Starting a session from a frozen menu unfreezes cleanly, and SAVE & QUIT leaves the session before tearing the world down.

**New: Setup Step 105** (`Scripts/Editor/Networking/NetworkSetup.cs`, wizard button in Voxel Engine Setup):
- Authors the `NetworkPlayerAvatar` prefab (NetworkObject + NetworkTransform + PlayerAvatar, capsule body, view-direction visor, nameplate) under `VoxelEngineAssets/Networking`.
- Wires a `Network` object into the open scene: NetworkManager + Tugboat + NetworkBootstrap, avatar prefab connected. Non-destructive and re-runnable; Fish-Net picks the prefab up in its DefaultPrefabObjects collection automatically.

**GitHub title:** `[14.1.0-dev] Multiplayer foundation: host and join`

**Manual steps:**
1. Pull `Dev` and let Unity compile.
2. Open the MAIN GAME scene, run **Tools -> Voxel Engine -> Voxel Engine Setup -> Step 105**, then save the scene (Ctrl+S).
3. Two-machine test (or two builds on one machine): machine A - Esc -> MULTIPLAYER -> HOST THIS WORLD. Machine B - same menu, type A's LAN IP into HOST ADDRESS -> JOIN GAME. Expected: both see the other's capsule avatar with the right name, moving and turning live. Walk, jump, fly - the avatar should track it all.
4. Also verify: pause menu no longer freezes the world while hosting; DISCONNECT returns both sides to Offline cleanly; hosting again afterwards works.
5. If the join fails across machines, it is almost always the firewall: allow the game/editor on UDP port 7770 on the host.

### [14.0.0-dev] Multiplayer Foundation - Part 1: Identity and Session

**Type:** MAJOR milestone opener - the networking foundation begins. Pre-release schema change: code lock authorization in saves moved from a single flag to a per-player id list (old saves keep their locks and codes; authorization is simply re-earned by entering the code once).

**New module: `Scripts/Networking` (namespace `VoxelEngine.Networking`).**
- **PlayerIdentity**: a stable per-installation player id (GUID, persisted) plus a display name. Every piece of per-player state is keyed by this id from now on - and when Fish-Net lands, the id travels with the connection.
- **NetworkSession**: the single source of truth for the session - Mode (Offline / Host / Client), IsAuthority, and a player registry with join/leave events. Today it always answers "Offline, one local player", but every system that asks it instead of assuming a lone player is already multiplayer-shaped. Gameplay code will never talk to the transport directly; the coming Fish-Net bridge drives this class.

**First checklist conversion done: code locks.** `CodeLock.authorized` (one bool meaning "the player") is now `authorizedIds` - a list of player ids. Setting a new code wipes the list and authorizes the setter; a correct entry adds the enterer permanently; the owner menu opens only for players on the list. Exactly the Rust model, and exactly what the server will replicate later.

**GitHub title:** `[14.0.0-dev] Multiplayer foundation: identity`

**Manual steps:**
1. Pull `Dev` and let Unity compile. No setup steps this round.
2. Sanity test: fit a code lock, set a code, relog - you stay authorized; locks from older saves ask for their code once.
3. **Install Fish-Net now** (Unity Asset Store: "Fish-Networking" by FirstGearGames - free; import the whole package). Do not wire anything up - just import it, confirm the project still compiles, and report the Fish-Net version number (Window -> Package Manager or the FishNet/VERSION.txt file). The next part builds the bridge against exactly that version.

### [13.18.1-dev] Multiplayer Strategy Locked

**Type:** PATCH - documentation only, no code changes.

**The multiplayer decisions are now locked and written down** so every future system (human- or agent-built) is designed against them:
- Fish-Net, client-server only, server-authoritative.
- First milestone: 2-8 player listen server (one player hosts). Dedicated headless servers after that. True P2P permanently out of scope.
- Pre-release: multiplayer refactors may break save formats freely.
- `Roadmap.md` gained section 1 "Multiplayer Strategy (Fish-Net)" with the locked decisions, the 14.0.0 milestone plan (foundation -> player state -> building sync -> world sync -> simulation sync -> dedicated server) and an MP-readiness checklist.
- `README.md` gained agent guideline section 4 "Multiplayer-Ready Code" enforcing that checklist on all new code: one authority entry point per action, per-player state keyed by player id, stable ids, no client-side truth, no new static gameplay state, server-runnable physics queries.

**GitHub title:** `[13.18.1-dev] Multiplayer strategy locked`

**Manual steps:** none - pull and read.

### [13.18.0-dev] Double Doors and Keypad Polish

**Type:** MINOR - new Double Door piece plus the code lock and gate feedback round. Save-compatible: one appended family.

**New piece: Double Door.** Two quick door leaves that fill a Wall Frame opening - the same frame the Garage Door fits, now with a second option. Framed panels with rails and handles at the meeting stiles, swinging apart away from the opener at normal door speed, colliders on the hinges, permanent interaction target in the opening. Sits beside the Garage Door on the STRUCTURAL wheel and takes code locks like every other door.

**Keypad takes real keys.** The code can now be typed on the keyboard: top-row digits and the numpad both work alongside the on-screen buttons, backspace erases a digit, escape closes the pad.

**Fresh locks ask for their code.** Fitting a code lock now opens the SET NEW CODE keypad immediately. If the pad is dismissed without a code, using the door prompts for one (and opens the door once set). After that the flow is unchanged: authorized players just use the door, strangers get the keypad, and clicking the lock itself opens its settings.

**Gate passage - belt and braces.** For anyone whose gates are still solid: prefabs rebuilt before the collider pass carry an old fixed box that cannot swing. TieredDoor now detects such root-level boxes at runtime and releases them once the leaves swing past a quarter open, so gateways clear even before Step 102 is re-run. Re-running Step 102 remains the real fix - it gives the leaves proper swinging colliders.

**Code lock placement fixed.** The garage door lock moved to the side of the opening, so it no longer floats mid-air when the shutter is rolled up. Gate and big gate locks are now larger and mounted further off the leaf face so they read clearly at gate scale. Re-fit existing locks (remove via the lock menu, mount again) to get the new positions.

**GitHub title:** `[13.18.0-dev] Double doors`

**Manual steps:**
1. Pull `Dev` and let Unity compile.
2. Run Tools -> Voxel Engine -> Voxel Engine Setup, Step 102 once more: it authors the Double Door and rebuilds every gate/door prefab with swinging colliders.
3. Place a Wall Frame, fit a Double Door, walk through while it swings.
4. Walk through an open gate (both before and after re-running Step 102 - both must pass now).
5. Fit a code lock: the keypad must open by itself; type the code on the numpad.

### [13.17.0-dev] Code Locks and Open Gateways

**Type:** MINOR - new code lock system, plus the collider pass that makes every 13.15.0 piece physically honest. Save-compatible: additive save fields only.

**Why gateways were solid.** None of the roofing-and-gates families had collider definitions, so every one of them fell back to the generic full wall box: gate frames were invisible walls across their own opening, gate leaves a second wall behind that, sloped roofs carried a vertical slab, and the rebuilt flat Roof still wore the old sloped shell floating above the deck. All fixed with real geometry:
- Gate Frame / Big Gate Frame: jambs and header only - the opening is open.
- Gate / Big Gate: the leaves carry swinging mesh colliders on their hinges; walk through the moment they part. Regular Doors also had their closed-pose box moved onto the hinge, so an open door no longer blocks its doorway.
- Compound Wall: full-height box at its real height.
- Triangular walls: stepped columns following the hypotenuse, plus a solid riser post so the ground probe always connects.
- Sloped roofs: pitch-matched tilted slabs (tapered strips for the triangular panel, two planes for hip and valley corners, a stepped cap for the pyramid); the flat Roof got its proper deck box. The eave-contact audit now excludes the panel's own collider so the new honest shapes cannot satisfy their own probe.

**New system: Code Locks.** Crafted at the Crafting Bench (6 Iron Ingot + 4 Copper Ingot, unlocked by default). Right-click with the lock on a Door, Gate, Big Gate, Garage Door or Floor Hatch to fit it - the lock rides the leaf or lid, keypad studs on both faces, with a state light: red locked, green open.
- First use opens the SET NEW CODE keypad (green header): four digits, auto-locks, you are authorized.
- Strangers using the piece get the ENTER CODE keypad (red header). The right code authorizes them permanently; a wrong code stings for 5 HP.
- Authorized players use the piece normally, and clicking the lock opens its menu: LOCK / UNLOCK, CHANGE CODE, REMOVE LOCK (returns the item).
- Demolishing a locked piece hands the lock item to the demolisher; locks and their state survive save/load.

**GitHub title:** `[13.17.0-dev] Code locks`

**Manual steps:**
1. Pull `Dev` and let Unity compile.
2. Run Tools -> Voxel Engine -> Voxel Engine Setup, Step 102 once more (rebuilds all piece colliders and gate hinges).
3. Run new Step 104 (authors the Code Lock item, recipe and catalog entry).
4. Walk through an open gate and an open door; walk the flat and sloped roofs.
5. Craft a Code Lock, fit it on a gate, set a code, relog, and try a wrong code.

### [13.16.1-dev] Double-Swing Gates

**Type:** PATCH - gate usability rework. Save-compatible; no new pieces.

**Gates now open as double doors.** Both gate sizes carry two leaves on opposing hinges that swing apart away from whoever opens them, instead of one full-width leaf sweeping the entire opening.

**Deliberately slow.** The leaves turn at a constant, stately rate - the Gate takes about four seconds, the Big Gate closer to seven - to carry the mass of the piece. Regular doors keep their quick swing.

**Same operating logic as the Garage Door.** The leaves stop blocking once mostly swung, and a permanent non-blocking target fills the opening, so one look at the gateway and one press always operates the gate - no more chasing a swung-open leaf. Clicking a Gate Frame or Big Gate Frame jamb or lintel also toggles the fitted gate, exactly as the Wall Frame does for its overhead door.

**GitHub title:** `[13.16.1-dev] Double-swing gates`

**Manual steps:**
1. Pull `Dev` and let Unity compile.
2. Run Tools -> Voxel Engine -> Voxel Engine Setup (Step 102) once more: it rebuilds the two gate prefabs with the second hinge, mirrored leaf and interaction target. Nothing else changes.
3. Fit a Gate and a Big Gate; open and close each from both sides, and once by clicking the frame instead of the leaves.

### [13.16.0-dev] Gates Touch the Ground

**Type:** MINOR - ground seating for gates and wall pieces, the Compound Wall, and a hard eave-contact rule for sloped roofs. Save-compatible: one appended family.

**Gates and walls seat on the terrain surface.** Free placement snapped every root to the nearest 7.5 m grid shell, including its HEIGHT - so on any terrain between shells a gate hung in mid-air and promptly decayed for lack of base contact. Ground-standing pieces (walls, half walls, doorways, windows, wall frames, triangular walls, both gate frames and the new Compound Wall) now keep the aimed surface height and snap only horizontally, on flat and spherical worlds alike. They land base-on-ground, the base audit finds the terrain, and they stand.

**New piece: Compound Wall.** A heavy freestanding perimeter wall, one module wide at gate-frame height (1.5 storeys), with end posts, parapet cap and plinth. It lines up socket-to-socket with Gate Frames and its own segments, places straight onto the ground like the gates, takes the base audit, and lives on the ROOFS & GATES menu. Build the perimeter from Compound Walls, drop in a Gate Frame where the road enters.

**Sloped roofs must touch something.** A sloped panel is only placeable while a placed block sits under its eave line or flush against a rake edge - a wall head, a gable, a deck edge or the panel it chains from. The same contact check runs in the periodic audit, so a panel whose wall is demolished (or that ever slipped through on stale span numbers, as in the report) starts decaying immediately. Terrain does not count: roofing rests on structure, not on dirt. Sloped panels placed BEFORE this patch that were never armed are not audited - hammer those away manually.

**GitHub title:** `[13.16.0-dev] Gates touch the ground`

**Manual steps:**
1. Pull `Dev` and let Unity compile.
2. Run Tools -> Voxel Engine -> Voxel Engine Setup (Step 102) once more: it authors the Compound Wall definition, prefabs and token. Everything else is untouched.
3. Place a Gate Frame and a Big Gate Frame on open, uneven ground: both seat flush on the surface, accept their gates, and survive the audit.
4. Chain Compound Walls into a perimeter and hang a Gate Frame between two segments.
5. Try to place a Slanted Roof in open air or on bare terrain: red. Seat it on a wall head: green. Demolish that wall: the roof decays.

### [13.15.0-dev] Roofs, Gables and Gates

**Type:** MINOR - twelve new building pieces, a reworked Roof, a third build-wheel menu and roof chain snapping. Save-compatible: all new families are appended enum values; one deliberate visual change to existing roofs, see below.

**The Roof family is now the flat ceiling panel.** As agreed, family six is rebuilt as a flat deck that can double as a floor for upper levels: full floor socket set, deck span rules, walls and stairs attach to it, pillars carry it and the collapse audit treats it as a deck. EXISTING PLACED ROOFS CHANGE SHAPE from the old 26-degree panel to the flat panel after the Setup step - position, family, tier and cost are untouched.

**Eight roofing and gable pieces.** Slanted Roof (rises exactly one storey across one module - the 3-4-5 pitch, so roofs and walls always meet), Triangular Roof (flat diagonal cap), Slanted Triangular Roof, Corner Roof (true hip: two planes meeting on the diagonal ridge), Slanted Corner Roof (Inverted) (the valley twin), Pyramid Roof (pitch-matched four-sided cap over one module), Triangular Wall (right-triangle gable matching the roof diagonal exactly - two mirrored make a full gable) and Triangular Wall (Inverted) for overhangs. Sloped panels follow the span-two roof rules with the wider adoption reach.

**Roof panels snap like stairs.** A sloped panel seats its eave on a wall head, rising into the building. Aim up or down the slope of an existing panel and the next one continues the pitch a full module out and a full storey up or down; aim at its side and the panel extends the ridge line level. Triangular walls and gates place on deck edges exactly like walls.

**Two gates.** Gate Frame (one module wide, one and a half storeys tall) with its heavy swinging Gate, and Big Gate Frame (two modules wide, three storeys tall) with the colossal Big Gate - both frame-plus-door pairs like Doorway and Door, hinged at the side, upgrade-safe, and full members of the collapse system (frames take the base audit, gates are fittings that fall with their frame).

**The wheel gains menu two.** TAB/SCROLL cycles STRUCTURAL, ROOFS & GATES, then ORBITAL STATION when researched. All thirteen roofing and gate pieces live on menu two, including the flat Roof, and the wheel still remembers the menu you used last.

**GitHub title:** `[13.15.0-dev] Roofs, gables and gates`

**Manual steps:**
1. Pull `Dev` and let Unity compile.
2. Run Tools -> Voxel Engine -> Voxel Engine Setup and execute the tiered build step (Step 102). It authors the twelve new definitions, prefabs, meshes and tokens, and rebuilds the Roof prefabs flat. Non-destructive for everything already customised.
3. Scroll the build wheel: menu two reads ROOFS & GATES with thirteen pieces.
4. Seat a Slanted Roof on a wall head, then chain: aim high on it to continue up, low to continue down, at its side to extend the ridge. The span-two limit still applies.
5. Close a gable with a Triangular Wall on the open end - the hypotenuse matches the roof diagonal.
6. Place a Gate Frame on open ground and on a foundation edge, snap a Gate into it and swing it. Repeat with the Big Gate pair. Demolish a frame: its gate falls with it.
7. Place a flat Roof as a ceiling, then walk on it and build a wall on top: it behaves as a floor.

### [13.14.0-dev] Building Grows Downward

**Type:** MINOR - downward vertical building and build-wheel menu memory. Save-compatible: no schema, prefab asset, item, recipe, research or cost change.

**Vertical pieces hang below floor edges.** Aiming at the UNDERSIDE of a Floor or Floor Hatch near an edge now hangs the piece below that edge: Walls, Half Walls, Doorways, Windows and Wall Frames drop a full piece height so their head sits flush against the slab bottom, and building continues downward. Aiming at the top surface places upward exactly as before, and Foundations remain top-only. A hanging piece is carried by the deck above its head - relayed through further stacked hanging walls - and decays normally when that deck falls. Hanging pieces never GRANT span or support to anything else, so no cantilever or ladder rule loosens: they keep only themselves alive.

**The build wheel remembers its menu.** The wheel now reopens on whichever menu the player last used - structural or station - surviving wheel closes, respawns and scene reloads within the session, instead of starting from the first page every time. A remembered menu that is locked on the current save falls back to the structural page, so locked content can never appear.

**The load-bearing readout understands hanging pieces.** A deck with pieces hung below it reads LOAD-BEARING.

**GitHub title:** `[13.14.0-dev] Building grows downward`

**Manual steps:**
1. Pull `Dev` and let Unity compile. No Setup step is required.
2. Stand under a supported floor, aim at its underside near an edge and place a Wall: it hangs below the edge, head flush with the slab. Continue a second wall below the first.
3. Demolish the floor: the hanging walls decay and fall with it.
4. Confirm a hanging wall's bottom edge accepts no floor (hanging pieces carry nothing).
5. Scroll the build wheel to the station menu, close it, reopen: it opens on station. Aim at the floor with hanging walls: it reads LOAD-BEARING.

### [13.13.0-dev] Decay Before the Fall

**Type:** MINOR - ten-second decay collapse, detached pillars join the collapse system, and a floor-beside-foundation audit fix. Save-compatible: no schema, prefab asset, item, recipe, research or cost change.

**Fixed: a floor placed beside a foundation no longer self-destructs.** A deck rests ON a pillar or wall top, but hangs one full module off a Foundation SIDE - placement grants span one at exactly that geometry, yet the audit's adoption reach stopped at 5.5 m, so the foundation 7.5 m away was never adopted and the audit destroyed the deck it had just allowed. Foundation supports now use an 8.1 m adoption reach, matching placement precisely; pillar and wall reach is unchanged, so no span rule loosens anywhere else.

**Unsupported pieces decay for about ten seconds before collapsing.** Losing a load path no longer deletes a piece instantly: it drains health each audit tick with the standard crack visuals and collapses when health reaches zero - roughly ten seconds from full health, sooner for a piece already battle-damaged. Rebuilding the support during the window stops the decay where it stands (the lost health stays lost until the piece is upgraded). Chains therefore fail progressively: each piece starts decaying the moment its own carrier is gone.

**Detached pillars collapse with the building.** A pillar now stands only while its chain reaches the ground, its base rests on terrain, a Foundation, a wall line or a live deck, or its top hangs from a live block - the deliberate hanging-chain build. A pillar with none of these decays like everything else. Two detached pillars can never hold each other up: base contact deliberately ignores other pillars, and only the grounded-chain check can ground a stack. The inspection card also reads hanging pillars correctly: a deck a pillar hangs from shows LOAD-BEARING.

**Compatibility.** Arming remains unpersisted: pillars placed before this patch or restored from a save never self-collapse. Structural decay refunds nothing; hammer demolition still refunds half.

**GitHub title:** `[13.13.0-dev] Decay before the fall`

**Manual steps:**
1. Pull `Dev` and let Unity compile. No Setup step is required.
2. Place a Foundation, then a Floor off each of its four sides: every floor must stay, survive the audit and accept walls.
3. Demolish a wall under a floor: the floor cracks progressively for about ten seconds, then falls. Rebuild the wall mid-decay: the floor survives with its damage frozen.
4. Place a grounded pillar with a floor on top, then mine the terrain out from under the pillar: floor and pillar both decay and fall.
5. Hang a pillar under a supported floor: it stays. Demolish that floor: the pillar decays and falls after it.

### [13.12.0-dev] Fittings Fall With Their Frames

**Type:** MINOR - fittings join the collapse system and the inspection HUD warns about load-bearing pieces. Save-compatible: no schema, prefab asset, item, recipe, research or cost change.

**Every piece of a building now collapses with its support.** Doors, Garage Doors, Window Panes and Hatch Lids are armed at placement against the exact frame they were hung in - the Doorway, Wall Frame, Window or Floor Hatch that hosted the snap - and live exactly as long as it. A base probe would be wrong for these: a door's base line rests on the floor, but its life depends on the doorway. When the frame is demolished or collapses, its fitting follows a tenth of a second later through the 13.11.0 cascade, so a felled wall line now takes frames, fittings, railings and decks down together. Tier upgrades are safe in both directions: an upgraded fitting stays armed, and upgrading a frame re-points its hung fitting at the rebuilt object so it does not read its host as destroyed.

**The top-left inspection card warns before you swing.** Aiming at a construction piece now shows LOAD-BEARING in the status line whenever other armed pieces currently depend on it: a fitting it hosts, a wall or railing standing on its body, a deck or roof hanging from its top, or a farther cantilever deck relaying its span through it. Pieces nothing depends on keep the plain description. The check is read-only, runs only for the piece under the crosshair, and never affects physics.

**Compatibility.** As with every audit since 13.8, arming is not persisted: fittings placed before this patch or restored from a save never self-collapse. Pillars remain deliberately exempt from self-collapse - a hanging pillar chain is an intentional build.

**GitHub title:** `[13.12.0-dev] Fittings fall with their frames`

**Manual steps:**
1. Pull `Dev` and let Unity compile. No Setup step is required.
2. Build a doorway with a door on a supported floor, then demolish the doorway: the door falls with it. Repeat with a Window plus Window Pane and a Floor Hatch plus Hatch Lid.
3. Demolish the floor under that doorway instead: floor, doorway and door all cascade within about half a second.
4. Upgrade a doorway that holds a door to stone: the door must survive, and demolishing the upgraded doorway must still drop it.
5. Aim at a wall carrying a floor, the floor under a wall, a pillar under a deck and a doorway holding a door: each reads LOAD-BEARING top-left. A lone decorative wall on the ground must not.

### [13.11.0-dev] Collapse Reaches the Walls

**Type:** MINOR - faster structural audits and collapse for vertical pieces. Save-compatible: no schema, prefab asset, item, recipe, research or cost change.

**Structural updates are much faster.** The periodic audit interval drops from 0.75 to 0.3 seconds, and destruction is now event-driven on top of that: every demolished or collapsed block pings the armed pieces within nine metres, which re-audit a tenth of a second later - just after the destroyed collider is really gone. A chain collapse therefore ripples outward at roughly a tenth of a second per link instead of stepping once per interval, in both directions: decks above a removed wall and walls above a removed deck react almost immediately.

**Vertical pieces collapse with their support.** Walls, Half Walls, Doorways, Windows, Wall Frames and Railings are now armed at placement with a lightweight base audit built on the existing support-base probe: the piece stands while terrain, a Foundation, a grounded pillar chain, a stacked wall or a live deck carries its base line, and collapses when that support is destroyed. Their load state carries their own family tag, so span math, pass-through logic and deck relays never mistake them for a deck. Tier upgrades preserve the arming. Pillars are deliberately excluded - a hanging pillar chain is an intentional build governed by its own grounding logic - and fitted doors, panes and hatch lids are untouched for now.

**Compatibility.** Arming is not persisted, matching the deck audits since 13.8: vertical pieces placed before this patch or restored from a save never self-collapse; only pieces placed (or upgraded) from now on take part. Structural collapse still refunds nothing - only deliberate demolition with the hammer refunds half.

**GitHub title:** `[13.11.0-dev] Collapse reaches the walls`

**Manual steps:**
1. Pull `Dev` and let Unity compile. No Setup step is required.
2. Build foundation, floor, wall, floor-on-wall, wall again. Demolish the bottom wall: everything above it collapses within about half a second.
3. Put a railing on a pillar-supported floor, then demolish the pillar: floor and railing both fall almost immediately.
4. Demolish a deck under a wall that also touches terrain or a foundation: the wall must survive.
5. Upgrade a wall to stone and demolish its floor: the upgraded wall still collapses.

### [13.10.3-dev] Walls See What They Stand On

**Type:** PATCH - wall support-base probe correction. No save schema, prefab, item, recipe, research or cost change.

**A floor on a supported wall is placeable again.** The 13.10.1 support-base resolution probed beneath a wall with a single thin raycast from its root - but a wall's root line sits exactly on the deck edge (or the foundation rim) it stands on, so the ray grazed the collider's boundary face and missed it. The wall reported standing on nothing, offered span zero, and the floor ghost on top stayed red even though the wall was legitimately carried. The probe is now a small overlap box straddling the base line, which reliably sees the deck, foundation, terrain, grounded pillar or stacked wall the piece actually rests on.

**The evaluation is priority-ordered and exploit-safe.** Terrain, a Foundation or a grounded pillar chain immediately ground the wall (span resets to one, as always). Armed decks under the base line make the wall a pass-through carrying the best - lowest - deck span, so a wall on a span-two floor still offers span three and stays refused. Stacked walls resolve through each other and relay the deepest carrying deck. Unarmed legacy or restored decks count as stable, and only deck families do: railings, doors, panes and other fittings beside the base line carry nothing, so an edge railing cannot quietly reopen the wall ladder. The periodic audit shares the same probe, so nothing legitimately placed on a wall is later orphaned.

**GitHub title:** `[13.10.3-dev] Walls see what they stand on`

**Manual steps:**
1. Pull `Dev` and let Unity compile. No Setup step is required.
2. Wall on a supported (span-one) floor, then a floor on the wall top: green, places, and survives the audit. Repeat with the wall on a foundation rim and on open terrain.
3. Wall on a span-two floor, floor on top: still red.
4. Stack two walls on a supported floor and continue a floor from the upper wall: green, span two.
5. Add a railing on the same edge as a wall over a span-two floor and confirm the floor above the wall is still refused.

### [13.10.2-dev] Corner Pillars and Flush Joints

**Type:** PATCH - Floor-on-Pillar corner alignment and pillar-adjacent placement corrections. No save schema, prefab, item, recipe, research or cost change.

**A Floor on a Pillar now lands corner-on-pillar.** The 13.9.0 snap offset the deck half a module along one axis, putting the pillar under the middle of a floor edge. The deck now offsets half a module on both axes, so the pillar carries the floor's 90-degree corner - the point where up to four modules meet - and extends diagonally toward the builder. This matches the corner anchors already used when a pillar is placed under an existing deck, so pillar positions form one consistent lattice from either direction. The corner stays within the 5.5 m support reach, so adoption and the periodic audit behave exactly as before.

**Walls beside pillars are no longer refused.** A pillar sits flush with the deck it carries, so aiming at a pillar-supported edge very often hit the pillar rather than the deck. No wall-to-pillar join exists, so the ghost fell through to free placement, whose overlap rule vetoes anything touching a structure - the wall on that edge was permanently red and read as unsupported. Two corrections close this: aiming at a pillar that carries a Floor or Floor Hatch now resolves the join against that deck (walls, railings and every deck-hosted piece place normally), and a pillar neighbour never vetoes the placement overlap check, because pillar tops deliberately coincide with deck undersides, corners and wall lines.

**GitHub title:** `[13.10.2-dev] Corner pillars and flush joints`

**Manual steps:**
1. Pull `Dev` and let Unity compile. No Setup step is required.
2. Aim a Floor at a Pillar top from several positions: the ghost hangs its corner on the pillar and extends diagonally toward you. Place it and confirm the audit keeps it (span one, extendable).
3. Place a wall on every edge of a pillar-supported floor, including the two edges meeting the pillar corner, and while aiming directly at the pillar itself: the ghost must be green and place at the deck edge.
4. Place a pillar under an existing Floor corner, then a Floor on a fresh pillar: both use the same corner points.
5. Confirm a Railing aimed at a pillar-supported edge also snaps to the deck edge instead of refusing.

### [13.10.1-dev] Grounded Supports Win the Audit

**Type:** PATCH - structural audit ordering and wall support-base correction. No save schema, prefab, item, recipe, research or cost change.

**A grounded pillar now actually resets the floor above it.** The 0.75-second audit was a single pass over an unordered physics query: a span-two floor usually found its span-one neighbour first, survived through the relay branch and returned before the loop ever reached the new pillar, so the adoption that resets the span to one almost never ran. The audit is now two passes - every direct grounded support (pillar chain, wall, foundation) is evaluated and adopted first, and the deck-neighbour relay only runs when no direct support is in reach. Placing a grounded pillar under a span-two floor reliably resets it within a second, and floors can be extended from it again.

**The wall ladder is closed.** A wall counted as a fresh vertical support no matter what it stood on, so floor, floor, wall-on-the-suspended-floor, floor, floor could repeat forever. `TryResolveSupportBase` now walks straight down from a wall-type piece (Wall, Doorway, Window, Wall Frame, Half Wall, legacy Pillar): terrain, a Foundation or a grounded pillar chain make it a true support; standing on a suspended deck makes it a pass-through that continues that deck's span plus one instead of resetting to one. A wall on a span-two floor therefore offers span three - refused. A wall on a span-one floor still carries the next storey exactly as before, stacked walls resolve through each other, and a deck without an armed load state (anything restored from a save) counts as stable so old bases keep building normally.

**Both sides use the same rule.** Ghost placement (`ResolveStructuralSpan`) and the periodic audit (`HasLoadPath`) share the identical support-base resolution, and in the audit a pass-through wall relays its carrying deck's span so a legitimate floor standing on a wall above a span-one deck is never orphaned by the sphere radius. Pieces placed this session through the old wall exploit will collapse within a second of the patch - pieces from older saves are unaffected because restored load states are not armed.

**GitHub title:** `[13.10.1-dev] Grounded supports win the audit`

**Manual steps:**
1. Pull `Dev` and let Unity compile. No Setup step is required.
2. Foundation, two floors out (span two), third refused. Place a pillar under the second floor - underside aim or grounded beside it reaching the deck. Within a second, extend two more floors from it.
3. Place a wall on that outermost span-two floor and aim a floor at the wall top: the ghost must be red.
4. Place a wall on a span-one floor and aim a floor at the wall top: the ghost must be green (span two), and the placed floor must survive the audit.
5. Repeat check 3 after a save/reload of an older base to confirm legacy structures still accept walls and floors normally.

### [13.10.0-dev] Barrel Items Retired

**Type:** MINOR - approved content retirement, save-compatible through an item-id alias. No save schema, chunk format or placed-block change; no fresh save required. Closes the open item deferred by 11.0.0-dev.

**Empty Barrel and Crude Oil Barrel are removed.** Since 11.0.0-dev the Jack Pump draws liquid crude into its own tank and the Oil Refinery feeds through fluid pipes, so no recipe in the project produced or consumed either item. Their assets, `Recipe_EmptyBarrel` (both the live copy under `Recipes/` and the stale duplicate under `Industrial/Recipes/`) and their catalog entries are deleted from the repository.

**Saved stacks do not vanish.** `ItemIdAliases` maps `item_emptybarrel` and `item_crudeoilbarrel` to `item_steelplate`. A barrel was pressed steel, so an old save's barrel stacks come back as Steel Plate at the same count. The alias layer already guarantees a live id always wins, so nothing changes for saves that never held a barrel.

**Setup Step 10 performs the retirement and stops recreating the items.** `RetireOrphanedBarrelAssets` deletes the item, recipe and icon assets wherever a working copy still has them, scrubs `Recipe_EmptyBarrel` and null entries from the `RecipeRegistry`, and removes dead references from the `ItemPersistenceCatalog`. The step remains idempotent and safe to re-run; Refined Oil Barrel, Plastic Bar and the whole machine chain are untouched.

**Oil Logistics keeps its place in the tree.** `res_oil_extraction` no longer unlocks the barrel recipe - it unlocks nothing, exactly like the Plastics node - but keeps its position, cost and role as the prerequisite for Oil Refining and Pirate Oil Recovery. Its description, and the Oil Refinery / Chemical Plant block descriptions, now describe the liquid crude chain instead of barrel conversions.

**GitHub title:** `[13.10.0-dev] Barrel items retired`

**Manual steps:**
1. Pull `Dev` and let Unity compile. Unity will import the four asset deletions; any "missing script/reference" warning at first import is expected and disappears after the next step.
2. Run `Tools -> Voxel Engine -> Voxel Engine Setup -> 10. Build Industrial Content` once. It deletes the two barrel icons under `ItemIcons/` (they are matched by item id, whatever category folder they sit in), scrubs the recipe registry and persistence catalog, and confirms nothing recreates the retired items.
3. Open the Research UI: Oil Logistics still sits at Tier 3 Chemistry with Oil Refining and Pirate Oil Recovery behind it, and its details panel lists no unlocked recipe.
4. Load a save that holds Empty or Crude Oil Barrels: the stacks appear as Steel Plate at the same count.
5. Confirm the Oil Refinery and Chemical Plant tooltips describe tank/pipe processing and mention no barrels.

### [13.9.0-dev] Pillar Chains Reach the Ground

**Type:** MINOR - new construction capability (hanging pillar stages, pillar chaining, automatic downward growth), plus support-audit and floor-snap corrections. No save schema, prefab component set, family value, recipe or research changes. Existing saves load unchanged.

**A Pillar that cannot reach the ground now places anyway.** The 13.8.3 red-ghost refusal over deep gaps is reversed: aiming beneath a Floor, Floor Hatch or Stair over a gap deeper than 1.5 storeys places a full-length stage hanging in the air. A hanging stage is deliberately not a load-bearing support - `AdjustablePillar.IsSupportGrounded()` walks the chain of stages beneath it (cycle-guarded, depth-capped) and only reports true once the bottom stage touches terrain, a Foundation or another placed piece. The moment the chain touches down, every stage in it becomes a valid support at once.

**Pillars chain from other Pillars.** Aiming a Pillar at the lower half or underside of an existing Pillar hangs a new stage from its root; the stage meets the ground or any placed piece exactly when it is within 1.5 storeys, otherwise it places at full length for the next stage to continue. Aiming at the upper half stacks a stage on the host's top, and when a Floor, Floor Hatch or Stair underside is within reach the stacked stage sizes itself to meet it exactly. Each stage pays the existing per-storey cost multiplier for its own height.

**Grounded Pillars reliably support the deck above.** The structural audit and ghost placement no longer accept any `AdjustablePillar` blindly: a grounded chain counts, a hanging one does not. The Floor-on-Pillar snap now also recognizes a Pillar by component identity, so an older Pillar definition with stale family metadata still carries a deck edge, and the supported Floor resets to span one so building can continue outward from it.

**Mining the ground under a Pillar no longer strands it.** Once per second a placed Pillar probes beneath its root. If the ground was mined away, the Pillar keeps its top edge exactly where the structure expects it and grows downward until it touches ground again, up to the 1.5-storey maximum. Beyond that it becomes a hanging stage: it stops carrying load and the normal structural audit takes over. Players, fauna and loose physics objects never count as ground for any of these probes.

**Floors aimed at a Pillar extend toward the builder.** The deck still rests its edge on the pillar top, but the side is now chosen from where the player stands rather than the aimed face, so the new Floor always grows toward the builder and walking a deck outward feels natural.

**Reload recovery understands chains.** A restored Pillar recovers its non-standard height from the first placed piece directly above it: a chain stage meets the pillar root above, a lone pillar meets the deck underside, and any other piece ends recovery, so a middle stage can no longer stretch through its own chain. Ghost previews are now fully inert - they never probe, resize or move themselves.

**GitHub title:** `[13.9.0-dev] Pillar chains reach the ground`

**Manual steps:**
1. Pull `Dev` and let Unity compile. No new Setup step is required; if the current Pillar/Stair prefabs have never received `AdjustablePillar` and `StructuralLoadState`, run `Tools -> Voxel Engine -> Voxel Engine Setup -> 102. Rebuild Construction at Size-V6` once.
2. Aim a Pillar beneath a Floor more than 1.5 storeys above ground: the ghost is green at full length and places as a hanging stage. Confirm a Floor aimed at that hanging stage shows a red ghost.
3. Aim a Pillar at the lower half of the hanging stage: a second stage hangs from its root. Repeat until one stage meets the ground exactly. Within about a second, confirm Floors can now be placed on the chain and that the suspended Floor above accepts new neighbours again.
4. Stand on a deck and aim a Floor at a Pillar top from several positions around it: the new Floor edge lands on the pillar and the deck extends toward where you stand.
5. Place a grounded Pillar, then mine the terrain under it: within a second the Pillar grows down to the new surface with its top unmoved. Mine deeper than 1.5 storeys total and confirm it stops at maximum length and the deck above starts failing its support audit unless another support exists.
6. Stack a Pillar on top of a grounded Pillar below an existing Floor underside: the new stage sizes itself to close the gap exactly and the Floor adopts the support.
7. Save and reload with a multi-stage chain: every stage recovers its height and the chain still reports grounded.

### [13.8.4-dev] Pillar Edge Snap Compiles

**Type:** PATCH - compile repair only. No runtime behaviour, save data, public API, prefab, mesh, item, recipe, research, resource cost or balance value is changed.

**Fixed - the Floor-on-Pillar branch declared `useX` inside a scope whose containing method later declared another `useX`.** C# forbids that shadowing pattern even though the first declaration is inside an earlier conditional block, producing CS0136 at line 376. The local is now named `pillarUsesX`; every read in that branch was updated with no placement-math change.

**GitHub title:** `[13.8.4-dev] Pillar edge snap compiles`

**Manual steps:**
1. Pull `Dev` and let Unity compile.
2. Confirm CS0136 is gone.
3. Continue the 13.8.3 validation. No Setup step is required for this compile-only correction beyond Step 102 already required by 13.8.3.

### [13.8.3-dev] Pillars Meet Edges and Stair Undersides

**Type:** PATCH - adaptive-Pillar maximum height, Stair underside support, Floor-on-Pillar alignment and support-adoption correction. No save schema, family value, recipe or research changes.

**One Pillar is now capped at 1.5 storeys.** An underside ground search stops at 8.4375 m. If solid terrain is farther away, the ghost extends only to that maximum, turns red and cannot be placed. The player must stack another Pillar/support stage instead of producing a single oversized column. `AdjustablePillar` enforces the same maximum during placement, upgrade and reload reconstruction.

**Floors meet a Pillar by their edge.** A Floor or Floor Hatch aimed at a Pillar top now chooses the aimed X/Z side and offsets its centre by half a 7.5 m module. The Pillar sits under the Floor edge rather than its middle, while its measured top remains the structural anchor.

**Pillars can support a Stair at the actual underside point.** Aiming beneath a Stair uses the precise hit point on its rising underside, searches down to ground and sizes the Pillar to that local height. During structural audit, a Pillar top within 0.8 m of any Stair collider counts as direct support, instead of comparing only against the Stair root level.

**New supports reset the load path.** When an armed Floor, Floor Hatch or Stair discovers a direct Foundation, Wall or Pillar during its 0.75-second audit, it adopts that support, resets to span one and stores the new support top. Floors and Stairs extending from it can then use span two. This fixes adding a physical Pillar without gaining new placement capacity.

**Tall-Pillar pricing remains proportional within the new limit.** A standard Pillar costs 1x; a Pillar over one storey and up to 1.5 storeys costs 2x for placement and every upgrade, with the multiplied amount shown in Upgrade mode.

**GitHub title:** `[13.8.3-dev] Pillars meet edges and stair undersides`

**Manual steps:**
1. Pull `Dev` and let Unity compile. Run Setup Step 102 if the current Pillar/Stair prefabs have not received `AdjustablePillar` and `StructuralLoadState`.
2. Aim beneath a Floor less than 1.5 storeys above ground: place the Pillar and confirm its top meets the Floor edge target. Over a deeper gap, confirm the red ghost refuses and stops at 8.4375 m.
3. Add a Pillar beneath an existing span-two Floor, wait up to one second, then extend Floors from it. The supported Floor must have reset to span one, allowing one additional span-two Floor.
4. Aim beneath several points along a Stair. Confirm the Pillar height follows the slope and the Stair remains supported after placement.
5. Aim a Floor at a Pillar top from each side. Confirm the Floor edge, not its centre, lands on the Pillar.
6. Verify a 1.5-storey Pillar charges 2x placement and upgrade resources.

### [13.8.2-dev] Stairs Join the Load Path

**Type:** PATCH - stair structural-span, landing socket, adaptive-Pillar support identity and proportional material-cost correction. No save schema, family value, recipe or research changes.

**Stairs can no longer escape the cantilever rule.** Setup Step 102 now adds `StructuralLoadState` to every Stair tier and records its authored family identity. A Stair aimed at a Floor, Floor Hatch or another Stair inherits that piece's support anchor and adds one span. Stair span three is red and cannot be placed, closing the infinite chain from a Floor already two pieces away from support.

**Floors connect to both Stair landings.** A Stair's existing Bottom socket sits one module beyond the foot at the lower level, and its Top socket sits one module beyond the head at the 5.625 m upper level. `BuildSocketCompat` now accepts Floor and Floor Hatch at both sockets. The nearest socket to the aimed end wins, so aiming at the foot continues the lower deck and aiming at the head continues the upper deck.

**Adaptive Pillars are recognized by component identity.** Placement and periodic load audits accept an `AdjustablePillar` as a support even if an older definition carries stale family metadata. Its measured `currentHeight` remains the support top, fixing a physically grounded Pillar that still failed to authorize the next Floor.

**Tall Pillars cost by storey.** Placement and each tier upgrade multiply the authored cost by `ceil(currentHeight / 5.625 m)`. A one-storey Pillar keeps its existing cost; a Pillar just over one storey costs twice as much, and so on. The Upgrade-mode HUD shows the multiplied requirement and affordability rather than the base cost.

**GitHub title:** `[13.8.2-dev] Stairs join the load path`

**Manual steps:**
1. Pull `Dev`, let Unity compile, then run `Tools -> Voxel Engine -> Voxel Engine Setup -> 102. Rebuild Construction at Size-V6` to add structural state to all Stair tiers.
2. From a supported Floor, place Stairs up to span two. Attempt another Stair from a span-two Floor or Stair and confirm the ghost remains red.
3. Aim a Floor at the bottom end of a Stair and then at its top end. Confirm it snaps one module beyond the corresponding landing at the correct height.
4. Place an underside-to-ground Pillar and confirm Floors one and two can extend from the supported deck while Floor three remains red.
5. Build a Pillar taller than one storey and compare inventory consumption with a standard Pillar. Open Upgrade mode and confirm the displayed and charged material requirements use the same storey multiplier.

### [13.8.1-dev] Adaptive Pillars Carry Their Actual Height

**Type:** PATCH - variable-height Pillar support-point and suspended-span tolerance correction. No save schema, family value, prefab geometry, item, recipe, research or cost changes.

**Fixed - structural checks still treated every Pillar as exactly one storey tall.** An underside-placed Pillar could visibly reach from terrain to a Floor, but both the placement anchor and the periodic collapse audit calculated its top as `root + 5.625 m`. Any Pillar shorter or taller than that missed the deck it physically touched and therefore supplied no load path.

**Adaptive Pillars now carry at their measured top.** When a Pillar begins a Floor or Roof load path, `BuildSystemV2` uses `AdjustablePillar.currentHeight`. `StructuralLoadState` uses the same value during every 0.75-second support audit. The placement verdict and later collapse verdict therefore agree with the visible Pillar.

**The second unsupported Floor receives practical tolerance.** The original anchor remains unchanged and span three remains a hard refusal, but the geometric cap now permits 16 m instead of 15.35 m. Two 7.5 m Floor modules fit reliably across curved terrain and generated surface offsets without accidentally granting a third module.

**GitHub title:** `[13.8.1-dev] Adaptive pillars carry their actual height`

**Manual steps:**
1. Pull `Dev` and let Unity compile. No setup step is required after 13.8.0 Step 102 has been run.
2. Place a Floor over uneven terrain, aim beneath it and place a ground-reaching Pillar.
3. Extend Floor one and Floor two from that supported point. Both must be valid; Floor three must remain red.
4. Wait several seconds and confirm the Pillar-supported Floors do not collapse.
5. Remove the Pillar and confirm the unsupported run collapses unless another valid support remains.
6. Repeat with a short Pillar and a Pillar taller than one storey to verify support follows the visible top in both cases.

### [13.8.0-dev] Pillars Reach Down From Floors

**Type:** MINOR - adds underside-to-ground Pillar placement across all four construction tiers. Save-compatible: no schema or family value changes; restored variable-height Pillars reconstruct their height from world geometry.

**A Floor underside is now a Pillar placement surface.** Aim upward at the underside of a Floor or Floor Hatch while holding Pillar. The same five structural targets remain available: centre plus four true corners. From that underside anchor the build system raycasts along the floor's local down axis for up to 40 m, ignores tiered construction and selects the first solid terrain/world collider.

**The Pillar grows from ground to deck.** The ghost root moves to the ground hit and `AdjustablePillar` scales the authored 5.625 m Pillar to the measured gap. Meshes, colliders and sockets inherit the same root scale, so the preview, placed support and top connection agree. If no solid ground is found, ordinary top-of-deck Pillar placement remains available rather than manufacturing a floating support.

**All tiers retain the measured height.** Setup Step 102 adds `AdjustablePillar` to Wood, Stone, Iron and Steel Pillar prefabs. Upgrading transfers the measured height before replacing the old tier.

**Reload requires no new save field.** A variable-height Pillar is saved at its grounded root through the existing placed-piece record. On restore, the component raycasts upward to the first Floor or Floor Hatch and reconstructs its height. Standard one-storey Pillars remain unchanged.

**GitHub title:** `[13.8.0-dev] Pillars reach down from floors`

**Manual steps:**
1. Pull `Dev`, let Unity compile, then run `Tools -> Voxel Engine -> Voxel Engine Setup -> 102. Rebuild Construction at Size-V6` to add `AdjustablePillar` to all four Pillar prefabs.
2. Build a Floor over uneven or lowered terrain. Stand below it, select Pillar and aim upward at the underside near the centre and each corner.
3. Confirm the ghost starts on solid ground and ends flush against the Floor underside; move between targets and verify its length updates immediately.
4. Place the Pillar, upgrade it through all four tiers and confirm its grounded root and top height do not move.
5. Save and reload. Confirm the variable-height Pillar reconstructs the same height and still meets the Floor.
6. Aim at a Floor underside where no solid surface exists within 40 m and confirm no false ground-reaching support is created.

### [13.7.5-dev] Floors Obey the Same Two-Panel Cantilever

**Type:** PATCH - applies the existing suspended-span placement guard to Floors and Floor Hatches. No save schema, family value, prefab, item, recipe, research or cost changes.

**Fixed - the final placement guard covered Roof only.** Floors already received `StructuralLoadState`, inherited spans and collapsed after support loss, but the pre-placement refusal checked only `BuildFamily.Roof`. A Floor chain could therefore be placed indefinitely and then collapse later, exactly as shown in the screenshot. Floor and Floor Hatch are now included in the authoritative post-transform guard.

**The rule is now consistent across every suspended panel.** Span one and span two are placeable; span three is red and cannot be committed. Every Floor inherits the original Foundation or vertical-support anchor rather than creating a new origin. A new Foundation, Wall, Doorway, Window, Wall Frame, Half Wall or Pillar begins another supported run.

**Foundation geometry receives the correct allowance.** A Floor continuing from a Foundation starts one complete 7.5 m module from the Foundation centre, unlike a Roof whose first centre is half a module from a wall. The shared anchor cap is therefore 15.35 m, enough for two Foundation-supported Floor modules. The explicit span-two check still prevents a third panel for both Floors and Roofs.

**GitHub title:** `[13.7.5-dev] Floors obey the same two-panel cantilever`

**Manual steps:**
1. Pull `Dev` and let Unity compile. Run Setup Step 102 if Floor and Floor Hatch prefabs have not yet received `StructuralLoadState`.
2. From one isolated Foundation, place Floor one and Floor two in a straight line.
3. Attempt Floor three. Its ghost must be red and repeated placement clicks must do nothing.
4. Repeat sideways from Floor two and with Floor Hatches; no span-three branch may be placed.
5. Add a Foundation, Wall or Pillar beneath the refused position and confirm it starts a new valid run.
6. Remove the only support from a placed Floor run and confirm the existing collapse cascade still removes the dependent panels.

### [13.7.4-dev] Roof Span Keeps Its Original Support

**Type:** PATCH - closes lateral span-two expansion by carrying an explicit support anchor through the load path. No save schema, family value, prefab geometry, item, recipe, research or cost changes.

**A Roof now remembers the real support that started its run.** When a Wall, opening frame, Half Wall or Pillar creates span one, the candidate records that support's world-space top as `supportAnchor`. A Roof extending from another armed Roof inherits the same anchor instead of treating the neighbouring panel as a new origin.

**Both conditions must pass.** A Roof must have span one or two and its centre must remain within 11.6 m of the original support anchor. The distance covers the first centre at 3.75 m and the second at 11.25 m, with only a small curved-world tolerance. A row of span-two panels can no longer grow sideways indefinitely by each touching the same lower-span panel.

**Upgrades keep the complete load record.** Tier replacement transfers both `spanFromSupport` and `supportAnchor`, so upgrading cannot reset the permitted reach or manufacture a new support origin.

**GitHub title:** `[13.7.4-dev] Roof span keeps its original support`

**Manual steps:**
1. Pull `Dev` and let Unity compile. Run Setup Step 102 if the current Roof prefabs have not yet received `StructuralLoadState`.
2. Start from one isolated Wall or Pillar. Place Roof one and Roof two outward from it.
3. Try extending farther in the same direction and sideways from Roof two. Every candidate outside the original support's two-panel reach must remain red.
4. Add another real Wall or Pillar at the refused location. Its new anchor must allow the next two-panel run.
5. Upgrade Roof one and Roof two, then repeat the extension test; their anchor limit must remain unchanged.

### [13.7.3-dev] The Wheel Owns the Requested Family

**Type:** PATCH - closes the remaining Roof-span bypass caused by stale serialized family identity. No save data, prefab geometry, item, recipe, research or balance cost changes.

**The requested family is now authoritative for the entire placement pass.** `ComputeGhostTransform` previously received a Roof definition and repeatedly read `def.family` inside each snap and validation branch. If an older generated definition retained the wrong serialized family despite displaying as Roof in the wheel, the placement path could validate it as that other family. The wheel or legacy token selection now supplies `requestedFamily` directly to snapping, socket compatibility, fallback placement and overlap validation.

**An armed load component is resolved before legacy host metadata.** A placed Roof authored by Step 102 carries `StructuralLoadState`. When another Roof targets it, that component's span is incremented before consulting `host.definition.family`. This prevents a stale host value such as Wall from classifying every panel in the chain as a fresh span-one vertical support.

**A final rule runs outside every snap branch.** After the ghost transform is computed, the active wheel family is checked again. A requested Roof with span below one or above two is forced invalid before materials or placement input are processed. No socket, direct transform or fallback return can bypass this guard.

**GitHub title:** `[13.7.3-dev] The wheel owns the requested family`

**Manual steps:**
1. Pull `Dev`, let Unity compile, and run Setup Step 102 once so placed Roof prefabs carry `StructuralLoadState`.
2. Begin a fresh run from one Wall or Pillar. Roof one and Roof two must place; Roof three must be red.
3. Continue aiming at Roof three's outer edge and confirm repeated clicks cannot place it.
4. Add a Wall or Pillar beneath that location and confirm it becomes valid.
5. If an old already-placed Roof lacks armed structural state, rebuild the short test chain after Step 102; existing unsupported legacy roofs remain intentionally untouched.

### [13.7.2-dev] Structural Span Belongs to the Placed Piece

**Type:** PATCH - replaces proximity-only roof validation with explicit placement span and adds support-loss collapse to newly authored suspended pieces. No save schema, family value, item, recipe, research or tuned cost changes.

**Roof span is now assigned from the exact piece under the crosshair.** A Roof aimed at a Wall, opening frame, Half Wall or Pillar receives span 1. A Roof aimed at an armed span-1 Roof receives span 2. Span 3 or an unknown load path is rejected before placement. Nearby walls elsewhere in a broad physics sphere can no longer accidentally validate the chain, which is why the earlier bounded scan still allowed the photographed run.

**Suspended pieces now own and audit their load path.** Setup Step 102 adds `StructuralLoadState` to Roof, Floor and Floor Hatch prefabs. New placements record their span. Every 0.75 seconds an armed piece accepts either a real support reaching its level or a compatible neighbouring suspended piece with a lower span. If neither remains, the unsupported piece is destroyed; higher-span neighbours then fail on their following checks, producing a bounded outward collapse rather than leaving a floating sheet.

**Floors participate without inheriting the Roof limit.** Foundations and vertical supports begin a Floor load path at span 1, and connected Floors carry increasing spans for collapse ordering. The two-panel maximum remains a Roof placement rule only. Removing one wall does not collapse a deck when another valid support or lower-span route remains nearby.

**Upgrades preserve structure.** Replacing Wood with Stone, Iron or Steel transfers the armed span to the new prefab, so upgrading a supported panel cannot silently detach it from the load graph.

**GitHub title:** `[13.7.2-dev] Structural span belongs to the placed piece`

**Manual steps:**
1. Pull `Dev`, let Unity compile, then run `Tools -> Voxel Engine -> Voxel Engine Setup -> 102. Rebuild Construction at Size-V6` to add `StructuralLoadState` to all Roof, Floor and Floor Hatch tiers.
2. Start from one Wall or Pillar. Place Roof one and Roof two; both must succeed. Roof three must remain red regardless of other distant construction visible nearby.
3. Add a support under Roof three and confirm it becomes valid.
4. Build two independently supported Roof routes, remove one support and confirm the panels remain when the second lower-span route is still reachable.
5. Build a Floor from a Wall or Foundation, remove its only support and wait up to two seconds. The Floor and outward dependent Floors should collapse in order.
6. Upgrade a supported Floor and Roof, then remove their supports and confirm they still participate in collapse.

### [13.7.1-dev] Roof Support Cannot Relay Forever

**Type:** PATCH - roof placement support validation correction. No save data, public API, prefab, item, recipe, research or balance cost changes.

**Fixed - a roof chain could relay support indefinitely.** The first implementation searched a broad radius for any vertical support. That described distance, but it did not describe the load path through individual roof modules and could allow a long connected run to keep passing validation under dense construction. Validation now performs a bounded two-step roof graph instead.

**The count is explicit.** The candidate Roof is span one. It may either reach a real vertical support directly or pass through exactly one adjacent Roof, span two, which must itself reach a real support. Validation never traverses a second neighbouring Roof, so the third unsupported panel turns red regardless of how many more Roofs continue beyond it.

**Supports still meet the panel where construction actually meets.** Walls, Doorways, Windows and Wall Frames reach a Roof at its edge. Pillars at the new four-corner sockets reach the same Roof diagonally at its corner. Vertical level remains checked in each support's local up frame for curved worlds.

**GitHub title:** `[13.7.1-dev] Roof support cannot relay forever`

**Manual steps:**
1. Pull `Dev` and let Unity compile. No setup step is required.
2. Place one Wall or Pillar and build outward from it with Roofs.
3. Confirm the first Roof is valid and the second connected Roof is valid.
4. Confirm the third Roof without another vertical support turns red and cannot be placed.
5. Add a Wall or Pillar under that refused panel and confirm it becomes valid immediately.
6. Repeat from a corner Pillar and from a Wall Frame to verify both corner and edge load points.
7. Confirm existing unsupported Roofs still load and remain untouched.

### [13.7.0-dev] Roof Loads and Grounded Foundations

**Type:** MINOR - adds a structural roof-span rule and adaptive Foundation support legs. Save-compatible: no existing family value, placed-piece record or save schema changes; support legs are regenerated prefab children and recalculate from world collision.

**Roofs now require a load path.** A Roof can be placed only when the candidate root lies within two construction modules of a Wall, Doorway, Window, Wall Frame, Half Wall or Pillar whose top reaches the same roof level. Full-height supports contribute at 5.625 m and Half Walls at 2.8 m. Distance is measured in the support's local horizontal plane, so the rule remains correct on curved worlds. Existing Roofs are not removed or damaged; the rule applies only to new placement ghosts.

**Foundations grow four independent legs to solid ground.** Setup Step 102 authors one slim support near each Foundation corner and connects them to `FoundationSupportLegs`. Each leg raycasts along the Foundation's own down axis, ignores the Foundation and other tiered construction, and extends to the first solid terrain/world collider up to 40 m below. Legs update while a ghost moves and settle after placement, allowing a slab to bridge uneven terrain without floating corners or stamping one oversized central column through the landscape.

**The first Foundation after loading no longer snaps below terrain.** A new Foundation establishes a construction run and therefore uses the terrain point actually under the crosshair. It no longer rounds radial altitude to a 7.5 m construction shell, which could choose the shell below a loaded world's surface until aiming at an existing Foundation supplied a correct socket. Subsequent Foundations still continue through exact authored 7.5 m neighbour joins.

**GitHub title:** `[13.7.0-dev] Roof loads and grounded foundations`

**Manual steps:**
1. Pull `Dev`, let Unity compile, then run `Tools -> Voxel Engine -> Voxel Engine Setup -> 102. Rebuild Construction at Size-V6` to add the generated Foundation legs to all four tiers.
2. On uneven ground, move a Foundation ghost across a slope. Its four legs should independently reach the solid terrain below; place it and confirm they remain aligned after walking away and returning.
3. Save and reload the world. Without aiming at existing construction first, select Foundation and aim at untouched terrain. Its slab root must sit on the surface rather than below it.
4. Build a full-height Wall or Pillar and extend Roofs away from it. The first two-module span must remain valid; the next unsupported extension must turn red.
5. Add a Pillar or Wall beneath the refused area and confirm the Roof becomes valid again. Repeat with a Wall Frame, Doorway, Window and stacked Half Walls.
6. Confirm previously placed unsupported Roofs still load unchanged.

### [13.6.5-dev] Maximum Garage Opening

**Type:** PATCH - Garage Door interaction reach and generated opening dimensions. No save data, family value, item, recipe, research or tuned cost changes.

**The rolled shutter can be toggled from both the drum and its frame.** Interaction raycasts now admit only purposeful `TieredDoor` triggers while continuing to skip every unrelated trigger. Right-clicking the rolled drum reaches its permanent header target. Right-clicking either jamb or the header of a Wall Frame searches that opening for the nearest fitted overhead Garage Door and toggles it. The lookup happens only on interaction and remains bounded to 5.5 m around the aimed frame.

**The garage aperture now consumes almost the entire module.** Inside the 7.5 m wide by 5.625 m tall Wall Frame, the cutout grows from 5.0 by 4.3 m to **6.8 by 5.05 m**. That leaves a narrow 0.35 m structural side on each edge and a 0.575 m header instead of surrounding a vehicle opening with most of a wall.

**The shutter exactly matches the new hole.** Slat faces, separators, bottom weather bar and solid closed collider use the full 6.8 m opening width with no former 0.2 m inset. The rolling deformation uses the new 5.05 m height, and Setup Step 102 sizes the clickable drum target from the same authored width so future dimension changes cannot desynchronise it.

**GitHub title:** `[13.6.5-dev] Maximum garage opening`

**Manual steps:**
1. Pull `Dev`, let Unity compile, then run `Tools -> Voxel Engine -> Voxel Engine Setup -> 102. Rebuild Construction at Size-V6`.
2. Place a fresh Wall Frame and Garage Door. Closed, the shutter should fill the 6.8 m by 5.05 m cutout with only the slim tracks around it.
3. Open it fully, aim directly at the rolled drum and right-click; it must close.
4. Open it again, aim at the left jamb, right jamb and header of the Wall Frame in turn, right-clicking each; every surface must toggle the fitted Garage Door.
5. Confirm unrelated trigger volumes do not intercept ordinary mining, building or interaction rays.

### [13.6.4-dev] The Rolled Shutter Remains Reachable

**Type:** PATCH - Garage Door interaction, collision and rolling-geometry correction. No save data, public API, family value, item, recipe, research or balance value is changed.

**The header drum remains clickable while the opening is clear.** The 13.6.2 pass correctly disabled the Garage Door collider so the player could walk through, but that collider was also the only raycast target available for closing it. Setup Step 102 now creates `Generated_GarageInteraction`, a permanent trigger volume wrapped tightly around the header cylinder. It is non-blocking, belongs to the Garage Door hierarchy and therefore routes the standard interaction key back to the same `TieredDoor` component.

**Only blocking colliders are disabled.** `TieredDoor` now leaves trigger colliders enabled throughout opening, waiting and closing. The solid shutter collider still releases once the opening is 72 percent clear and returns during closing.

**Every moving shutter part joins the roll.** Slat separators and the bottom weather bar were authored into the static Trim mesh, so the main slat faces curled away while those details remained stretched across the opening. They now belong to the deforming Skin mesh. Only the side tracks and header drum remain static, producing one complete shutter in the roll rather than leaving pieces behind.

**GitHub title:** `[13.6.4-dev] The rolled shutter remains reachable`

**Manual steps:**
1. Pull `Dev`, let Unity compile, then run `Tools -> Voxel Engine -> Voxel Engine Setup -> 102. Rebuild Construction at Size-V6`.
2. Place a fresh Wall Frame and Garage Door, then open it fully.
3. Confirm every slat, separator and the bottom weather bar rolls into the header; only the two side tracks and cylinder should remain fixed.
4. Walk through the opening, turn back and aim at the header cylinder. The standard interaction must close the door.
5. Confirm the trigger never blocks movement and the solid shutter blocks passage again while closing.

### [13.6.3-dev] Tiered Factory Compiles Again

**Type:** PATCH - compile repair only. No runtime behaviour, save data, public API, prefab, mesh, item, recipe, research or balance value is changed.

**Fixed - `TieredPieceFactory.cs` ended with two stray `f` characters after `#endif`.** C# interpreted them as invalid top-level statements after the namespace and type declarations, producing CS8803 at line 1614 and CS1002 at line 1615. Both characters are removed. The file now ends at its editor compilation guard, and its braces remain balanced.

**GitHub title:** `[13.6.3-dev] Tiered factory compiles again`

**Manual steps:**
1. Pull `Dev` and let Unity compile.
2. Confirm CS8803 and CS1002 are gone.
3. Once compilation is clear, run Setup Step 102 for the pending 13.6.2 generated construction changes. No setup step is needed for this compile repair itself.

### [13.6.2-dev] The Shutter Rolls Into Its Drum

**Type:** PATCH - garage animation/collision, pillar target and generated-surface correction. No save schema, family value, item, recipe, research or tuned cost changes.

**The Garage Door now rolls instead of hinging.** The previous correction moved the pivot to the header but still rotated the complete shutter as one rigid board. `TieredDoor` now identifies the authored shutter skin, clones only the runtime instance mesh and curls its slats through two and a half turns around the header drum. Closing reverses the same deformation. The shared prefab mesh remains untouched.

**An open Garage Door releases the doorway.** The old root collider remained upright even after the visible shutter moved overhead. Garage-door colliders now disable once the roll has cleared 72 percent of the opening and re-enable while closing, so a Crusader or vehicle can pass through the open frame without walking into an invisible wall.

**Pillar targets are centre plus four corners.** The phrase “edge” is now implemented as the actual ninety-degree module corners where as many as four Foundations or Floors meet. The four side-midpoint targets are removed. Aiming within the middle region still selects the centre pillar; aiming outside it selects the matching signed X/Z corner at ±3.75 m on both axes.

**Wall relief has decisive depth clearance.** The first separation pass was too conservative for the project's viewing distances and depth precision. Wood boards, Stone blocks, Iron salvage plates and Steel armour panels now sit farther forward from the solid sheathing while retaining their overall silhouette, eliminating the remaining wall and Wall Frame depth fighting after Step 102 rebuilds their meshes.

**GitHub title:** `[13.6.2-dev] The shutter rolls into its drum`

**Manual steps:**
1. Pull `Dev`, let Unity compile, then run `Tools -> Voxel Engine -> Voxel Engine Setup -> 102. Rebuild Construction at Size-V6`.
2. Place a Wall Frame and Garage Door. Open it: the slats must curl into the cylinder above the opening rather than rotate as one panel.
3. After the opening is mostly clear, walk and drive through it. There must be no invisible collider. Close it and confirm it blocks passage again.
4. Hold a Pillar over a Foundation or Floor. Aim centrally for the centre target, then toward each corner. The four outer targets must sit at the true corners where four modules can meet; no side-midpoint target should remain.
5. Inspect newly rebuilt Walls and Wall Frames from shallow angles and at distance. Their boards, masonry or plates must remain stable without flickering against the backing surface.

### [13.6.1-dev] Hinges, Clearances and Stair Guards

**Type:** PATCH - construction animation, placement, generated-surface and railing-fit corrections. No save schema, family value, recipe, research or tuned existing cost changes.

**Garage Doors now open as Garage Doors.** The shared door component treated every fitting as a side-hinged leaf. Setup Step 102 now authors the Garage Door pivot at the opening header, offsets the generated shutter beneath that pivot and marks it as an overhead fitting. Runtime animation folds it upward around local X to sit over the opening; ordinary Doors retain their player-side-aware local-Y swing.

**A valid Wall Frame is no longer rejected by neighbouring construction.** The placement probe remains strict when another tiered piece already owns the exact target root, preventing duplicate placement. Other tiered colliders touching that root through a shared deck edge or roof support are treated as expected neighbours when a direct host snap is active. This closes the red Wall Frame case without globally disabling overlap checks or allowing dynamic-body overlap.

**Roofs seat above the wall cap.** The wall-top join adds 0.18 m of authored clearance for Roofs only. Floors retain their flush structural join.

**Stair Railings now follow the flight without leaning their posts.** Rotating a complete level railing onto the stair pitch rotated the posts too. `TieredRailing` now clones only the instance meshes and applies a lengthwise vertical shear: each post stays vertical, while the top and middle rails rise the full 5.625 m across the 7.5 m run. Level railings use zero shear, and shared generated mesh assets are never modified.

**Generated skins no longer share depth with their backing.** Deck finish faces previously landed exactly on the slab top, and the rear faces of Wood/Stone relief could land exactly on the solid wall sheathing. The authoring pass now gives those layers a small physical separation, removing the broad depth-buffer flicker without changing the visible dimensions or materials.

**GitHub title:** `[13.6.1-dev] Hinges, clearances and stair guards`

**Manual steps:**
1. Pull `Dev`, let Unity compile, then run `Tools -> Voxel Engine -> Voxel Engine Setup -> 102. Rebuild Construction at Size-V6`.
2. Place a Wall Frame and Garage Door. Interact from both sides: the shutter must fold upward under the header rather than swing sideways.
3. Place a Wall Frame on a Floor with adjacent walls and a Roof already present. The ghost must remain valid unless another piece already occupies that exact root.
4. Place Roofs on both faces of a Wall. Their eaves should clear and sit on the wall cap rather than intersect it.
5. Place Railings on both Stair sides. Posts must remain vertical; both horizontal rails must climb with the treads and the lowest post must begin at the stair foot.
6. Walk around Wood and Stone walls, Foundations and Floors at shallow viewing angles. The cladding and deck finish must remain stable without alternating or shimmering against their backing surfaces.
7. Recheck Pillars at the centre and four side-midpoints. This round intentionally keeps the five positions requested in 13.6.0-dev; if “the edge” means corners instead of side-midpoints, send one marked screenshot and that layout can be changed without guessing.

### [13.6.0-dev] Construction Fittings and Separate Railings

**Type:** MINOR - adds the save-compatible Railing construction family and completes the opening/fitting placement pass. `BuildFamily.Railing` is appended at value 23; no existing family, tier, save value or tuned cost is renumbered or overwritten.

**The structural wheel now reads in build order.** Foundations, floors, walls, half walls, pillars, roofs, stairs and railings lead the ring. Every fitting follows its opening: Doorway then Door, Window then Window Pane, Wall Frame then Garage Door, Floor Hatch then Hatch Lid. Step 100 audits the same complete order instead of its older ten-family subset.

**Openings now accept their actual fittings.** The compatibility table explicitly connects Window Pane to Window, Hatch Lid to Floor Hatch and Garage Door to Wall Frame. Door, shutter, pane and lid placement tolerates the expected contact with the supporting tiered deck while continuing to reject dynamic-body overlap, fixing valid fitting ghosts that stayed red.

**Structural edge rules are explicit.** Wall Frame joins Foundation and Floor edges like every other wall. Walls, Half Walls, Doorways, Windows and Wall Frames use the nearest cardinal edge rather than a centre socket. Roofs use the same half-module wall-top join as Floors. Floors can continue from Foundation edges at the Foundation top. Pillars select the deck centre inside the middle region and otherwise select the middle of the nearest of four sides, so adjacent Foundations share sensible pillar positions.

**Stair alignment no longer inherits the old scene grid.** Stair run, rise and chaining use the authored 7.5 m module and 5.625 m storey. The generated flight no longer contains an enormous solid wedge or built-in guard rails: thin treads and risers sit on two exposed sloped stringers, leaving a clean diagonal underside.

**New four-tier Railing family.** Railing is append-only family 23, generated non-destructively by Setup Step 102 with Wood, Stone, Iron and Steel prefabs, definitions, costs, tokens, colliders and a drawn wheel icon. It snaps to all four Foundation/Floor/Floor-Hatch edges and to either side of Stairs; stair placement pitches the same railing prefab along the flight.

**Upgrade information is now exclusive to Upgrade mode.** Merely holding the Building Hammer no longer places the tier and material card over the screen. The card is evaluated only after selecting the wheel's centre Upgrade action; choosing a building family or closing build mode clears that state.

**GitHub title:** `[13.6.0-dev] Construction fittings and separate railings`

**Manual steps:**
1. Pull `Dev` and let Unity compile.
2. Open `Tools -> Voxel Engine -> Voxel Engine Setup` and run `102. Rebuild Construction at Size-V6`. This creates the four Railing prefabs and their definition/token and rebuilds Stairs without embedded rails or the solid wedge. Existing tuned definitions remain preserved.
3. Re-run `100. Wire the Hammer Build Wheel` only if its audit reports a missing family. It is non-destructive.
4. Open the structural wheel and confirm this order: Foundation, Floor, Wall, Half Wall, Pillar, Roof, Stairs, Railing, Doorway, Door, Window, Window Pane, Wall Frame, Garage Door, Floor Hatch, Hatch Lid.
5. Place each opening and then its fitting. Door, Garage Door, Window Pane and Hatch Lid ghosts must turn valid at their matching centre socket and nowhere else.
6. Place Wall Frame and Half Wall on every Foundation/Floor edge. Place Roof and Floor from both faces of a Wall. Continue a Floor from a Foundation edge.
7. Aim a Pillar near a Foundation centre, then near each cardinal edge. In a square of Foundations, shared side-midpoint positions must coincide.
8. Place and chain Stairs in both directions. Confirm their landings meet the deck, their underside follows the flight as two open stringers, and no rail is built in.
9. Place Railings on all four deck edges and both Stair sides. Check all four material tiers by upgrading one Railing.
10. Hold the Hammer without selecting Upgrade: no cost card should appear. Open the build wheel and select the centre Upgrade action; the card should then appear only while aiming at a tiered piece.

### [13.5.8-dev] Authored Pieces Ignore a Stale Scene Grid

**Type:** PATCH - Size-V6 placement-dimension correction. No save data, public API, prefab identity, item, recipe, research or balance value is changed.

**Fixed - the direct placement path trusted the mutable scene grid for authored geometry.** The screenshots identify an exact half-scale failure: a Foundation moved 3.75 m, putting its centre on the old Foundation's edge instead of moving the required 7.5 m; a Floor rose 2.8125 m, putting it halfway up a 5.625 m Wall. Those are precisely the old Size-V5 grid and three-quarters of that grid. The generated Size-V6 pieces are fixed-size assets, so their structural joins cannot be calculated from a stale serialized fallback-grid setting.

**Structural joins now use the dimensions of the assets they join.** Size-V6 construction uses an authored 7.5 m module, 5.625 m full storey and 2.8 m half-wall height. Foundation and deck continuations move one complete module along normalized host axes. Wall-to-floor joins move half a module along the wall face and one complete storey upward. Using normalized axes rather than `TransformPoint` also prevents accidental prefab or parent scale from multiplying the offset.

**GitHub title:** `[13.5.8-dev] Authored pieces ignore a stale scene grid`

**Manual steps:**
1. Pull `Dev` and let Unity compile. No setup step is required.
2. Place two Foundations side by side. Their edges must meet; the second Foundation's centre must not sit on the first Foundation's edge.
3. Place a full Wall and aim a Floor at either face. The Floor underside must meet the wall top at 5.625 m, and its edge must meet the wall plane 3.75 m from the Floor centre.
4. Continue Floors from all four edges of that Floor. Every centre-to-centre step must be 7.5 m.
5. Repeat with a Half Wall: the Floor must meet its 2.8 m top.

### [13.5.7-dev] Structural Snaps Follow the Aimed Piece

**Type:** PATCH - deterministic construction snapping correction. No save data, public API, prefab identity, item, recipe, research or balance value is changed.

**Fixed - broad socket searching could still choose geometry other than the piece being aimed at.** Correcting the compatibility table was necessary but insufficient: the overlap sphere gathers every construction collider around the hit, then scores every compatible socket by distance to the hit point. In a partly built room that includes the floor behind a wall and neighbouring pieces. A nearby socket from that wider set could therefore beat the wall-face anchor or leave the intended join to fallback placement.

**The three ordinary structural joins are now deterministic.** A floor or floor hatch aimed at a wall derives its side directly in that wall's local frame and places its own edge on the wall at the exact storey height. A foundation aimed at a foundation, and a floor or floor hatch aimed at another deck, chooses the nearest edge of that specific host and continues exactly one complete module. The host's full rotation is retained, so the correction also remains valid away from a planet's equator. Specialized stairs, fittings and orbital pieces continue through the socket system.

**GitHub title:** `[13.5.7-dev] Structural snaps follow the aimed piece`

**Manual steps:**
1. Pull `Dev` and let Unity compile. No setup step is required; this is runtime placement logic and does not alter generated assets.
2. Aim at the upper half of either face of a Wall while holding a Floor. The floor edge must sit on that wall, on the aimed side, with its underside flush to the wall top.
3. Place that floor, then aim near each of its four edges with another Floor. Each ghost must continue one complete module from the aimed floor rather than falling back to the world grid.
4. Repeat the chain with Floor Hatches and repeat side-by-side placement with Foundations.
5. Test on visibly curved ground and confirm every new piece keeps the host piece's local up direction.

### [13.5.6-dev] Floors Land Beside Walls

**Type:** PATCH - construction snapping correction. No save data, public API, prefab identity, material, item, recipe, research or balance value is changed.

**Fixed - floors selected the wall centre instead of either side.** Step 102 had authored the correct half-module anchors on both faces of every wall-like piece, but the compatibility table did not accept floors on those anchors. It accepted a floor on the wall's centreline Top socket instead, so the only legal result put the middle of the floor on the wall. Floors and floor hatches now bind to the two face anchors; the centreline Top socket remains available for stacking walls and placing roofs.

**Fixed - foundation neighbours disappeared while aiming near the middle.** A neighbouring foundation socket is 7.5 m from the host centre, while the search radius was 7.25 m. Aiming near the centre therefore made every side anchor unreachable and fell back to world-grid placement. The default search now spans 8 m, enough to cover one complete module without reaching a second module. Step 102 upgrades only the recognized 3.25 m, 5.5 m and 7.25 m defaults; a designer's custom radius remains untouched.

**GitHub title:** `[13.5.6-dev] Floors land beside walls`

**Manual steps:**
1. Pull `Dev` and let Unity compile.
2. Open `Tools -> Voxel Engine -> Voxel Engine Setup` and run `102. Rebuild Construction at Size-V6`.
3. Place a Foundation, aim near its middle while holding another Foundation, and sweep toward each edge. The ghost should lock one full module beside the existing foundation without requiring the crosshair to sit in empty space.
4. Place a Wall on the foundation. Hold a Floor and aim at each face near the wall top. The floor should switch between the two sides, with its edge on the wall line; it must never centre itself across the wall.
5. Repeat the wall test with a Floor Hatch. Then stack a second Wall and place a Roof to confirm the wall's centreline top anchor still serves those families.

### [13.5.5-dev] Water Shader Compiles Before Build Anchoring

**Type:** PATCH - Setup Step 103 and water-shader compatibility correction. No save data, gameplay API, prefab identity, item, recipe, research or balance value is changed.

**Fixed - Step 103 could not create `VoxelWaterRuntime.mat`.** The full native-water shader uses fixed wake arrays, scene depth and repeated procedural-noise passes but did not declare a shader-model target. Unity could therefore import it against the default Shader Model 2.5 limits and report `VoxelEngine/VoxelWaterURP` as unsupported. Step 103 correctly refused to anchor that unsupported shader, producing the incomplete-setup message instead of hiding a bad player build. The full shader now explicitly targets Shader Model 4.5, suitable for the Windows Direct3D 11/12 build path, and the simpler in-house water shader explicitly targets 3.5.

**Water setup now has a project-owned fallback instead of failing or silently becoming generic URP Lit.** Step 103 loads both water shader assets directly through `AssetDatabase`, prefers `VoxelWaterURP`, and uses `VoxelWater` if the active graphics API cannot support the full shader. The generated water material and build guard accept either project-owned shader. Runtime liquid and procedural-water paths use the same ordered fallback.

**Existing setup-owned water materials are repaired non-destructively.** Earlier setup passes had already assigned generic URP Lit to `Mat_NativeSphericalWater` and `Mat_NativeCrudeOil` when the full shader was unsupported. Step 103 now upgrades only those recognized generated fallbacks, initializes their intended water or crude-oil profile, and preserves any unknown custom designer shader. The older native-water setup path follows the same rule on future reruns.

**GitHub title:** `[13.5.5-dev] Water shader compiles before build anchoring`

**Manual steps:**
1. Pull `Dev` and let Unity reimport both water shaders.
2. If Unity does not automatically reimport them, select `Assets/Scripts/Rendering/VoxelWaterURP.shader` and `VoxelWater.shader`, then use `Assets -> Reimport`.
3. Open `Tools -> Voxel Engine -> Voxel Engine Setup` and run `103. Anchor Runtime Shaders for Builds` again.
4. The dialog should complete without `VoxelWaterURP` or `VoxelWaterRuntime.mat` in the missing list. It can report that the two existing native liquid materials were repaired.
5. Make a clean Windows build. If the dialog explicitly says it used `VoxelWater` as the safe fallback, open the `VoxelWaterURP` Shader Inspector and send its remaining compile message before visual sign-off.

### [13.5.4-dev] Standalone Builds Keep Their Runtime Shaders

**Type:** PATCH - standalone rendering and build-safety fix. No save data, gameplay API, prefab identity, item, recipe, research or balance value is changed.

**Fixed - terrain and water could turn magenta only in a player build.** The gameplay scene deliberately leaves `CosmosBootstrap.terrainMaterial` empty. In the Editor, an `AssetDatabase` fallback silently found `Assets/VoxelEngineAssets/VoxelTerrain.mat`, so Play Mode looked correct. `AssetDatabase` does not exist in a player, the material was not under Resources, and the procedural terrain and liquid paths then relied on `Shader.Find`. Unity can strip shaders reached only by name, leaving the standalone player with the internal error material. The editor-tool reorganization did not move these runtime shaders; this was a pre-existing difference between the Editor and player paths.

**New non-destructive Setup Step 103 anchors runtime shaders for builds.** It creates dedicated terrain and water materials under `Assets/Resources/VoxelEngineRuntime`, then creates one small material anchor for every project shader under `Assets/Scripts/Rendering` and for the URP fallbacks used by procedural visuals. Existing generated material properties are preserved; a re-run only creates a missing anchor or repairs a broken shader link.

**Runtime terrain and water now load explicit Resources materials first.** `CosmosBootstrap`, `SphereWorld` and `AsteroidVoxelBody` resolve the build-safe terrain material before any named lookup. `WaterMeshBuilder` and `ProceduralWaterPatchRenderer` clone the build-safe water material before applying live liquid values, so the Resources asset is never mutated during play. Named shader lookup remains only as a hardened fallback, and a completely missing shader now produces one actionable error rather than constructing another magenta material.

**A bad build is stopped before it ships.** `RuntimeShaderBuildGuard` verifies the generated terrain, water and custom-shader anchors at build start. If Step 103 has not been run, Unity refuses the build and names the missing assets instead of producing a player full of magenta surfaces.

**GitHub title:** `[13.5.4-dev] Standalone builds keep their runtime shaders`

**Manual steps:**
1. Pull `Dev` and let Unity compile.
2. Open `Tools -> Voxel Engine -> Voxel Engine Setup` and run `103. Anchor Runtime Shaders for Builds`.
3. Read the summary dialog. It should report the terrain material, water material and shader anchors created or preserved with no missing required shader.
4. Make a clean Windows build. The Diagnostics Data setting in the Build Profile may stay at its project default; it is unrelated to the magenta materials.
5. Start a world and verify terrain, water, atmosphere, weather and space effects render normally. If any surface is still magenta, send the shader errors from the standalone player's `Player.log`.

### [13.5.3-dev] Editor-Only Tools Leave the Player Build

**Type:** PATCH - build organization only. No save data, gameplay behaviour, public runtime API, prefab content, recipe, item, research or balance value is changed.

**The editor toolchain is now organized by mechanic.** All 61 existing setup, prefab-generation, authoring, validation, debug and custom-inspector scripts have moved out of the flat `Assets/Scripts/Editor` root and into named mechanic folders: Building, Combat, Core, Cosmos, Crafting, Diagnostics, Environment, Farming, GridSystem, Inspectors, Items, Navigation, Nuclear, Power, Rail and Storage. The editor assembly definition remains at the root and is still restricted to the Unity Editor, so every category inherits the same player-build exclusion.

**Two remaining diagnostics no longer compile into the runtime assembly.** `GPUResidentDrawerValidator` moved from Rendering to Editor/Diagnostics and gained a manual validation menu command. `WaterDiagnostics` moved from WaterSim to Editor/Diagnostics; its expensive world-load probe is now scheduled by `EditorApplication` during play-mode testing instead of being called by `FluidManager`. Player builds therefore carry neither diagnostic class nor the water diagnostic state/tick branch.

**Unity references are preserved.** Every moved script kept its existing `.meta` file and GUID. New category folders have tracked folder metadata, so the reorganization does not replace scripts or break serialized references. Runtime mesh builders and authoring fallbacks that gameplay genuinely uses remain in their runtime folders.

**GitHub title:** `[13.5.3-dev] Editor-only tools leave the player build`

**Manual steps:**
1. Pull `Dev` and let Unity reimport the moved scripts and compile both `VoxelEngine` and `VoxelEngine.Editor`.
2. Confirm the Console is clear and `Tools -> Voxel Engine -> Voxel Engine Setup` still opens.
3. Enter Play Mode once. The water diagnostics should still report during editor testing, but they are no longer part of the runtime assembly.
4. Optional build check: make a Development Build and confirm it contains no `VoxelEngine.Editor` assembly. No Voxel Engine Setup step needs to be run.

### [13.5.2-dev] Two That Only Bite Later

**Type:** PATCH - two defects found on review, both in paths that look fine on a first run. No save data, no API, no cost is touched.

**Fixed - re-running the rebuild collided with the ladder mesh.** Step 102 is explicitly re-runnable, and it clears each piece's mesh asset before rebuilding it. The hatch lid builds a *second* asset, the ladder, from a different code path that was never given the same treatment. The first run was clean; the second wrote over an asset that was already loaded. The ladder asset is now cleared alongside the lid's.

**Fixed - the ladder's "reached the top" test assumed the world was flat.** It compared the player against a corner of the climb volume's world-space bounding box. A bounding box is axis aligned to the WORLD, but the ladder deliberately climbs along its own up vector precisely because this game has planets - so anywhere the ground is not level with the world axes, the corner is not the top of the ladder and the hand-back fires early or never at all. The top face is now transformed out of the volume's own frame, which is correct on any surface and costs nothing.

**GitHub title:** `[13.5.2-dev] Two that only bite later`

**Manual steps:**
1. Pull `Dev` and let Unity compile.
2. `Tools -> Voxel Engine -> Voxel Engine Setup` -> `102. Rebuild Construction at Size-V6`, then run it a **second** time. It should complete cleanly both times with no asset warnings in the Console.
3. Climb a hatch ladder somewhere well away from the equator, or anywhere the ground is visibly curved, and check you are still handed onto the floor at the top.

### [13.5.1-dev] The Dome Was Yawed Ninety Degrees

**Type:** PATCH - four corrections to the Size-V6 construction kit. No save data, no API, no cost is touched.

**Fixed - every tangential panel on the dome was rotated ninety degrees off.** A panel's local +Z has to point along the radius it sits on. Rotating +Z about Y by t gives `(sin t, 0, cos t)`, so matching a radius of `(cos a, 0, sin a)` needs **t = 90 - a**; the code used **-a**. That stands every panel on edge and fans the drum open, which is exactly what the screenshot showed. The same error was in the mullions, the banding rings and the cap bands.

The cap had a second, independent error: its panels were leaned by `atan2(r0 - r1, y1 - y0) - 90`, which is neither the slope of the band nor anything near it. A band rises by `y1 - y0` while drawing in by `r1 - r0`, so the lean is `atan2(r1 - r0, y1 - y0)` with no offset. `Quaternion.Euler` applies X before Y, so the panel now tilts in its own frame and is then swung round the drum, in that order.

**Fixed - the ladder hung below the hatch it came out of.** The lid animation slid the whole ladder down by half its length *as well as* unrolling it, so the top rung ended up two and a half metres under the opening with nothing bridging the gap - you could climb it, but never onto it. The ladder now unrolls from a fixed origin in the hatch plane, which is what a furled ladder does and what keeps its top rung at the lip. It is also one full storey long, so it reaches the floor it drops to, and it sits hard against the rear jamb instead of floating mid-opening.

**Fixed - letting go of a ladder did nothing.** `Release` cleared the rider, and `OnTriggerStay` re-grabbed them on the very next frame: release, re-engage, release, forever. Letting go on purpose now latches until the player leaves the volume. Climbing off the top also hands control back on its own - the climb stops at the lip so you step onto the floor instead of rising into the sky.

**Fixed - a storey could never be closed.** Walls carried a single Top socket at their centre line, so a floor snapped to a wall landed *centred on* the wall rather than resting beside it, and nothing lined up. Wall, Half Wall, Doorway, Wall Frame, Hull and Viewport now also carry anchors half a module to either side, which is where a floor's own edge has to land for it to sit on the wall. Roofs and Stairs had no sockets at all and now have them: neighbours and a top for roofs, a head and a foot for a flight. The plain Floor's own top surface was also still reporting the old 0.38 m slab rather than 0.42 m, so anything placed on a floor sat four centimetres low.

**Foundations are easier to line up.** At a 7.5 m module a neighbour socket sits a full module from the host's centre - 3.75 m past its edge - so with a 5.5 m search radius the aim had to be threaded into empty space to find it. The radius is now 7.25 m, which reaches the socket comfortably from anywhere on the host's deck.

**GitHub title:** `[13.5.1-dev] The dome was yawed ninety degrees`

**Manual steps:**
1. Pull `Dev` and let Unity compile.
2. `Tools -> Voxel Engine -> Voxel Engine Setup` -> `102. Rebuild Construction at Size-V6`.
3. If your scene's `BuildSystemV2` still had the 5.5 m snap radius, step 102 raises it to 7.25 m. A hand-tuned value is left alone - set it yourself if foundations still feel fussy.
4. Place an orbital Dome: a closed panelled drum with glazed bays under a ribbed cap, not a fan of loose panels.
5. Place a Floor Hatch, fit a Hatch Lid, open it. The top rung should be at the lip. Climb up, and you should be handed onto the floor at the top; press space part way up and you should drop off and stay off.
6. Build a foundation, a wall on its edge, then a floor onto the wall top - the floor should land beside the wall with its edge on the wall line. Then a roof and a staircase.

### [13.5.0-dev] Every Box Was Inside Out

**Type:** MINOR - the geometry bug behind every "half invisible" piece is fixed, windows and hatches become fittings you place yourself, the station dome becomes a habitat module, and ladders fold out and can be climbed. Save-compatible: the two new families are appended at 21-22, `BuildTier` is untouched, no cost is overwritten.

**Fixed - every generated box was wound inside out.** This is the single cause of almost everything reported. Unity culls back faces, and the engine's other generated meshes settle the convention: `WheelMeshFactory.Lathe` winds its triangles so that `cross(p1 - p0, p2 - p0)` points AWAY from the surface, and its own comment warns that getting it backwards makes a mesh render inside-out - "the near wall is culled and you see the far inner wall". Every face in `BoxMesh` was wound the other way. The near side of every box was being discarded and you were looking at the inside of the far side.

The tell was in the screenshots all along: the wood pillar, which is a **cylinder**, rendered solid, while the stone pillar, which is **boxes**, was hollow. `CylinderMesh` and `WedgeMesh` were wound correctly; only `BoxMesh` was reversed. Two swapped index lines, and walls, pillars, decks, doors, treads, plates, rivets and every other box in the construction kit now have a front.

**Fixed - a wooden pillar looked like a spring.** Cylinder UVs repeated once per texel, so a 5.3 m post wrapped the log-course texture about forty times and became a stack of rings. Round timber now takes an explicit tile count: a post reads as one log, a rail as one rail.

**Fixed - wood hardware was blacksmith iron.** The wood tier's trim - its corner posts and rails - used the dark speckled metal shared with the upper tiers, so a log cabin came out bolted together. Wood trim is now dark seasoned timber with a timber grain, and is no longer given a metallic response.

**Fixed - the entire orbital station was silently skipped.** Step 102 decided whether a prefab was safe to rebuild by matching child object NAMES, and the station parts are called Panel, Deck, Collar and so on. Every station prefab therefore failed the test, was logged as "custom work" and was never touched - which is why the dome was still the old squashed sphere. Authorship is now judged by the MESH: procedural, in our generated Meshes folder, or a Unity built-in primitive is ours; anything imported is a modeller's work and is left alone. That is both correct and stricter in the way that matters.

**The dome is a habitat module.** It was a blue blob on a plate. It is now something you could live in: a sixteen-bay panelled drum at standing height with a cross-braced door bay and two glazed bays at eye level, banded at the skirt, the waist and the eaves, under a shallow ribbed cap carrying four skylight arcs and a crown plate.

**Windows and hatches are frames now; the fitting is a separate build.** A Window used to be born glazed and a Floor Hatch born with its lid. Both now follow the rule the Doorway already set - the frame is one build and the thing that closes it is another:

* **Window Pane** - a sashed, barred glass insert that snaps into a Window's centre socket. An empty frame is a firing port, and a broken pane no longer costs you the wall.
* **Hatch Lid** - a hinged lid with a fold-out ladder, snapping into a Floor Hatch.

**The ladder folds out, and you can climb it.** Closed, the lid sits flush and the ladder is furled inside it. Interact and the lid swings up on its hinge while the ladder unrolls downward through the opening; interact again and both fold away. The ladder is climbable only while deployed, so a shut hatch is simply a floor. Climbing rides on the player controller's existing mount switch rather than threading a second state through the walk update: forward and back climb, you stay pinned to the ladder plane, and stepping out or jumping hands control straight back. The release path drops the flag on disable, on destroy and whenever the climber stops being reachable, because a player frozen on a ladder that got destroyed under them is unrecoverable.

**GitHub title:** `[13.5.0-dev] Every box was inside out`

**Manual steps:**
1. Pull `Dev` and let Unity compile.
2. `Tools -> Voxel Engine -> Voxel Engine Setup` -> `102. Rebuild Construction at Size-V6`. The station family will rebuild this time - it was being skipped, so expect the dialog's rebuilt count to jump by 32.
3. `101. Wire the Radial Wheel Family` is unchanged and does not need re-running.
4. Walk around a wall, a pillar and a foundation at every tier. Nothing should be see-through from any angle.
5. Check a Wood pillar: one log, not a coil. Check a Wood wall's corner posts: timber, not black iron.
6. Place a Window, then a Window Pane into it. Place a Floor Hatch, then a Hatch Lid into it.
7. Interact with the Hatch Lid: the lid swings up and the ladder unrolls. Walk into the ladder and hold forward to climb, back to descend, space to let go. Interact again to fold it away.
8. Place an orbital Dome and look at it - drum, door bay, glazed bays, banding, ribbed cap.

### [13.4.2-dev] Hatches That Are Actually Holes

**Type:** PATCH - three defects found while reviewing the Size-V6 geometry. No save data, no API, no cost, no balance is touched.

**Fixed - the floor hatch was planked shut.** A deck board runs the full depth of a slab, so the board crossing the opening had to be cut into the two lengths either side of it. The guard that was supposed to do that could never fire, and the wood and sheet-metal decks laid straight over the hole: the lid and the ladder were modelled and dropped through a floor that had no opening in it. Deck runs are now genuinely split around the hatch, and the stone and armoured decks - which drop whole cells rather than strips - were already correct and are unchanged.

**Fixed - the floor slab stood 4 cm above its collider.** The slab thickened from 0.38 m to 0.42 m when floors became tiered, and the collider was left behind. Standing on a floor put the player's feet four centimetres inside it, and a piece placed on top of one sat proud of the surface it was snapping to. Both the plain floor and the hatch's four deck bands are corrected. The orbital deck and junction genuinely are 0.38 m and keep it.

**Fixed - an inside-out tread rib.** The sheet-metal deck's raised tread ribs were being generated with a negative box extent, which is not a recess - it is a box wound the wrong way round, lit from inside and invisible from above. Raised elements now carry an explicit lift and keep their extents positive.

**Roadmap housekeeping.** The Recently Done block is capped at five rounds and the oldest is meant to go. The previous edit trimmed the wrong end, dropping 13.1.2 and 13.1.1 while leaving 13.0.1 behind. The block now holds exactly the five newest rounds, 13.4.2 back to 13.2.0, and everything trimmed keeps its permanent home in this file.

**GitHub title:** `[13.4.2-dev] Hatches that are actually holes`

**Manual steps:**
1. Pull `Dev` and let Unity compile.
2. `Tools -> Voxel Engine -> Voxel Engine Setup` -> `102. Rebuild Construction at Size-V6`.
3. Place a Floor Hatch and look down through it - there should be a hole, with the lid folded up on its hinge and the ladder hanging in it. Check the Wood and Sheet Metal tiers in particular; those are the two that were planked over.
4. Stand on a plain Floor and place a Wall on it - the wall's base should sit flush on the deck surface, not sunk into it.

### [13.4.1-dev] Solid Walls, and Wood That Looks Like a Log Cabin

**Type:** PATCH - a build-geometry bug fix and a surface pass across all four tiers. No save data, no API, no cost is touched.

**Fixed - the pieces were hollow.** The wood tier clad a panel with a stack of rounded logs and nothing behind them. Cylinders laid at exactly twice their radius touch tangentially and never overlap, so every course left a slot you could see, shoot and walk light through - the whole wall was a set of blinds. Every tier now lays a **solid sheathing box spanning the full panel first**, and the cladding is relief on top of it. The one-line fix that should have been there from the start: a wall is a wall, and the detail is decoration.

The same hollowness hit the stairs, which were fifteen floating treads with daylight between them. A solid wedge now sits under the flight.

**Wood, rebuilt against the reference.** The old skin was a stack of fat pipes. It is now what a log-cabin wall actually is: **horizontal boards filling the field between four round framing timbers** - a rounded post down each edge with its end proud of the panel, and a rounded rail across the top and bottom. Boards alternate a millimetre and a half of relief so raking light finds the courses. Half walls get a rounded capping rail instead of a square one, and door and window jambs get a rounded return rather than a flat bar.

**Framing follows the piece, not the panel.** A doorway is built from three clad sub-panels, and the first pass framed all four edges of each one, so posts appeared in mid-air around the opening. Each sub-panel now frames only the edges that are genuinely on the outside of the piece; the edges facing the hole get the jamb lining instead. An opening now reads as cut through a wall rather than assembled from offcuts.

**Stone.** Blocks were too big and the courses too few. The field is now small fitted blocks in running bond at roughly one every 42 cm, each nudged by a noise value so no two sit flush, inside a **quoined border** - alternating long and short corner blocks turning every framed edge, which is what stops dressed stonework reading as wallpaper.

**Sheet metal.** Uniform sheets are replaced by **salvaged plates of mixed size**, one or two per band, each rotated a degree or so off true with screw heads along its top and bottom edge, inside a riveted border bar. It reads as scavenged, which is the point of the tier.

**Armoured.** Wide recessed panels with clean seams inside a heavy border bar, plus a run of vent slots along the top band. Flat, dark and deliberate next to the sheet metal's mess.

**Foundations and floors are tiered too.** They used to be the same plank deck whatever the tier. Each now gets its own deck surface and its own skirt: plank deck with round log edge beams and corner posts; cobble grid with block courses and stone quoins; ribbed plate with a treaded top; recessed dark panels. The slab under all of it is one solid box, so no deck can ever be see-through either.

**GitHub title:** `[13.4.1-dev] Solid walls, and wood that looks like a log cabin`

**Manual steps:**
1. Pull `Dev` and let Unity compile.
2. `Tools -> Voxel Engine -> Voxel Engine Setup` -> `102. Rebuild Construction at Size-V6`. Re-running is the intended way to pick this up; it rebuilds the same prefabs in place and still refuses to touch anything carrying custom work.
3. Place a Wood wall and look through it - you should not be able to. Walk behind it: horizontal boards and round corner posts on the outside, posts and cross-braces on the inside.
4. Place a Doorway and check the opening has jamb linings and no posts floating in the gap.
5. Walk up a staircase and look at it from the side - solid underneath.
6. Upgrade one wall through all four tiers and compare: laid boards, fitted blocks with quoins, mixed salvage plates with screws, dark recessed armour.

### [13.4.0-dev] Rooms You Can Stand Up In, and Walls With a Strong Side

**Type:** MINOR - construction is rebuilt at double the footprint and half again the height, every tier gets a modelled strong exterior and weak interior, and three opening pieces are added. Save-compatible: `BuildTier` is untouched, the new families are appended at 18-20 so no placed piece is renumbered, and no existing cost is overwritten. Read the migration note at the end before running step 102 on a world you care about.

**Size.** One module was 3.75 m square with a 3.75 m storey, which is a corridor, not a room. It is now **7.5 m square with a 5.625 m storey** - double the footprint in both X and Z, and half again the height. Foundations, floors, walls, doorways, windows, roofs, stairs, pillars, half walls and every orbital station piece move together, because a kit where one piece grew is worse than one where none did. `BuildSystemV2` follows: the free-placement grid goes to 7.5 m, the socket search radius to 5.5 m and the builder's reach to 12 m, and each of those is raised only if it was still sitting on the old default.

**Strong side, weak side.** Every wall-like piece is now modelled with cladding on +Z and structure on -Z, so which way a wall faces is legible from across the valley:

* **Wood** - horizontally laid rounded logs outside; vertical posts with diagonal cross-bracing and a capping plate inside.
* **Stone** - fitted blocks in running bond with recessed mortar outside; rough-hewn rubble projecting inward around a timber lintel inside.
* **Sheet Metal** - patchwork corrugated sheets at mixed heights, riveted over a backing plate outside; a grid of L-beams with exposed bolt heads inside.
* **Armoured** - matte plates with bevelled seams and corner rivets outside; tread plate over heavy diagonal bracing inside.

The four tiers were previously the same grey box with a different tint. They are now four different buildings.

**It is modelled, not textured.** A wall carries fifty-odd real parts - individual logs, studs, braces, rivet blocks - and still costs three draw calls, because `TieredPieceFactory` welds them into one combined mesh per surface at author time and saves it as an asset. UVs are generated from each part's world size at a fixed texel density, so a 7.5 m wall and a 0.3 m rivet strip show the same grain instead of one stretched flat and the other tiled forty times. Colliders are hand-sized boxes rather than a mesh collider on the detail: a wall should be a wall, and the player should not catch on a rivet.

**Surfaces are authored assets.** `TieredSurfaces` synthesises twelve materials - four tiers by skin, frame and trim - plus one shared glazing, each with its own generated albedo: log courses, running-bond masonry, corrugation with rust blooms, armour plate with rivets, sawn timber, rubble, bolted steel, diamond tread. They are real assets in the project, so a designer can retune one and never lose the change.

**Three new pieces.**

* **Wall Frame** - a wide vehicle-sized cutout in a wall, with the drum housing and side rails already fitted. Takes a Garage Door.
* **Garage Door** - a corrugated roll-up shutter of eleven slats on an overhead drum, sized to the Wall Frame.
* **Floor Hatch** - a floor slab with a square opening, a lid folded up on its hinge and a ladder dropped through it.

All three are on the hammer wheel with their own drawn isometric icons, and all three exist at every tier.

**Doors read their tier.** A wooden door is split planks banded with iron. Sheet metal gets a small eye hatch. Armoured gets a wide vision slot. Each has modelled hinges and a lever handle rather than a sphere stuck to a cube.

**The orbital station joins the same grid.** The station set was a 2 m kit sitting beside a 3.75 m one, which meant the two families could never meet at a seam. Hull, deck, corridor, junction, viewport, airlock, dock and dome are rebuilt on the identical 7.5 m module, and rebuilt properly: ribbed pressure plating with a service conduit, magnetic tread channels, pressure frames every couple of metres, a bolted airlock hatch with eight dogs, a sixteen-segment docking collar with guide clamps, and a glazed dome built from real meridian ribs and glass panes instead of a squashed sphere.

**GitHub title:** `[13.4.0-dev] Rooms you can stand up in, and walls with a strong side`

**Manual steps:**
1. Pull `Dev` and let Unity compile.
2. **Back up the world you care about first.** Step 102 replaces the prefabs that existing placed pieces instantiate, so a base built before this update will re-form at the new size and its pieces will overlap. New worlds are unaffected.
3. `Tools -> Voxel Engine -> Voxel Engine Setup` -> `102. Rebuild Construction at Size-V6`. It takes a minute: eighty-four prefabs, each with its meshes welded and saved.
4. Read the summary dialog. Any prefab reported as "left untouched" carries hand-made children or a custom material - that is deliberate, and it will keep the old geometry until you clear the custom work.
5. Costs are not touched. The three new pieces get an opening price; everything else keeps exactly the balance you tuned.
6. Play. Equip the hammer, place a Foundation and walk it - it should read as a room. Place a Wall and walk around it: logs on one face, braces on the other.
7. Upgrade a wall through all four tiers with the hammer and watch the exterior change from logs to masonry to corrugated scrap to armour plate.
8. Place a Wall Frame, then a Garage Door into it, then a Floor Hatch in an upper floor.
9. If `BuildSystemV2` in your scene had a hand-tuned grid, snap radius or reach, step 102 left it alone - set the grid to 7.5 manually or nothing will line up.

### [13.3.0-dev] One Dial for Every Wheel, and the Jump Drive Joins It

**Type:** MINOR - every radial selector in the game now runs on one shared dial, the isometric icon solids are corrected, and the jump drive gains a destination wheel. Save-compatible: no schema change, no component renamed, every public member other systems read is unchanged (`ConveyorShapeWheel.GetMode`, `GridShapeWheel.CurrentShape`, `RoadSurfaceWheel.IsAnyOpen`, `HammerBuildWheel.ActiveFamily` / `IsOpen` / `Open` / `Close` / `ExitBuildMode`).

**Fixed - the icon solids were inside out.** The isometric projection used here puts the camera at minus-X, plus-Y, minus-Z, so the visible faces are the top and the two NEAR faces. The box routine drew the opposite set: it hid the three edges meeting at the near-bottom corner and drew the three that are actually behind the solid. Every piece therefore lost its front-facing edges and read as an open carton with the lid off. The nine visible edges are now the correct nine, and every surface detail - wall studs, door panels, window mullions, the doorway reveal, the stair treads - moved onto the z = 0 face it belongs on. The inverted slope is drawn as the solid that is actually left after the cut rather than as a cube with a cross scratched on it.

**Fixed - Escape with a hammer in hand.** The build wheel was handling the Pause key itself, unconditionally, whenever a hammer was equipped. Standing still with no dial up and no family armed, Escape therefore reported "build mode closed" and consumed the press, so the pause menu could never open. `InGamePauseMenu` already owned that contract correctly - it exits build mode only when the dial is up or a family is armed, and opens the menu otherwise - so the duplicate handler in the wheel is gone entirely. Escape now opens the menu when there is nothing to cancel, and cancels build mode when there is.

**The dial leans into the aim.** The whole wheel now translates up to 17 pixels toward the direction the hand is pointing. It is undamped and unsmoothed, like everything else on the selection path: it reads as the dial acknowledging the hand rather than as drift the hand then has to chase. Combined with the bead that rides out to the groove exactly as the deadzone is left, the wheel now answers before the wedge lights.

**One dial, six wheels.** The feel, the geometry table and the palette moved into `RadialWheelController` and `RadialWheelView`, and every radial selector in the game was rewritten to run through them:

* Hammer build wheel - eighteen construction families.
* Conveyor shape wheel - straight, ramp, vertical, per speed tier.
* Grid armour shape wheel - cube, slope, half, half-slope, corner, inverted.
* Energy pipe shape wheel - nine conduit fittings.
* Road surface wheel - asphalt, stone pathway, drawbridge.
* Jump drive wheel - new, see below.

All six share the cursor lock, the re-centred virtual pointer, the pure-angle selection with no smoothing, the release-to-select, the tap-to-pin, the wedge pop, the hover tick and the centre cancel. Four of them were still running the old per-wheel code: a CPU-painted `Texture2D` ring rebuilt pixel by pixel on every hover, an exponential-damped parallax slide, and Unicode glyphs standing in for icons. All of that is deleted, not ported - roughly 1,500 lines of duplicated wheel code collapsed into two shared classes plus six small option providers.

**Drawn icons everywhere.** `MachineShapeIcons` authors the conveyor decks with travel arrows, the armour solids, the pipe fittings as schematic ducts with end collars and bend couplings, and the road surfaces including a drawbridge with its leaf lifted off the hinge. Chevrons were tried first and rejected: they shear under the isometric projection and turn into a squiggle at 58 pixels, so direction is a single shaft with a head. `LineArtBuilder` gained the isometric primitives and `IconAtlas` is now the one cache behind every drawn icon in the game.

**New - the jump drive dial.** A destination could previously only be chosen by right-clicking the drive block and reading a scrolling list, which means leaving the cockpit view in the middle of a burn. Hold the Warp Drive key while piloting and the twelve nearest destinations lay out on the same dial: charted planets, moons, powered beacons and route-book points, each with its own drawn mark. Wedges are live - distance and price recompute while the dial is open, a destination the energy pool cannot reach is dimmed, one that has gone dark is locked out. The hub carries the drive itself: spin-up state, bank in kWh, pooled range, and a centre action that begins the spin-up, cancels it, or fires an aimed jump exactly as the bare key used to. Picking a destination on a cold drive starts the spin-up for it rather than refusing. The block panel is untouched and remains the place for tuning and resonators.

**The warp key has one owner.** `GridCockpit` deferred its one-shot warp handler to the dial, so the key cannot both charge the drive and open the wheel on the same press.

**GitHub title:** `[13.3.0-dev] One dial for every wheel, and the jump drive joins it`

**Manual steps:**
1. Pull `Dev` and let Unity compile. The Console should be clear.
2. `Tools -> Voxel Engine -> Voxel Engine Setup` -> `101. Wire the Radial Wheel Family`. Non-destructive: it adds the conveyor, armour, pipe, road and jump-drive wheel components to the player only where they are missing, and leaves anything already there alone.
3. If you have not run it yet, also run `100. Wire the Hammer Build Wheel` and read its Console audit.
4. Play. Equip the Building Hammer and confirm the icons are now closed solids with their front edges - a wall, a doorway and a pillar should look solid, not hollow.
5. Stand still with the hammer, nothing armed, and press Escape. The pause menu must open. Arm a family, press Escape again: it must cancel build mode instead.
6. Flick around the dial and watch it lean toward your aim.
7. Hold the Build Wheel key with a conveyor, a shaped grid armour block, an energy pipe and the road paver in turn - each opens the same dial with its own drawn icons.
8. Board a ship that carries an enabled Warp Drive, take the pilot seat, and hold the Warp Drive key (default `U`). Flick to a destination and release to jump; left click the centre to begin or cancel the spin-up. A quick tap of the key now pins the dial open instead of starting the charge - the charge control is the centre of the dial.

### [13.2.0-dev] The Build Dial Rebuilt Around the Flick

**Type:** MINOR - the hammer build wheel is rebuilt as a direction-driven radial dial with vector wedges and drawn piece icons. Save-compatible: no schema, no component rename, no public API removed. `ActiveFamily`, `IsOpen`, `Open`, `Close` and `ExitBuildMode` all keep their signatures, so `BuildSystemV2`, `PlayerInteractionTool` and the pause menu are untouched.

**Selection is an angle, not a hit test:** the old wheel asked the operating system cursor to land inside an annulus between 154 and 232 pixels of the ring centre, and it selected nothing outside that band - a fast flick overshot the ring and chose nothing. Selection is now pure direction. `RadialWheelInput` integrates raw mouse delta into a unit disc, clamps the length to 1 and leaves the angle completely free, so the hand can travel across the whole desk and the wedge it pointed at is still the wedge that wins. Full deflection takes 13 percent of the shorter screen edge, which is a flick rather than a drag.

**The cursor is locked and re-centred on every open:** the hardware cursor is pinned and hidden while the dial is up and the virtual pointer starts at dead centre every single time. That is what makes the muscle memory work - up is always Foundation, down-left is always Stairs, regardless of where the mouse happened to be sitting when the dial opened. A blocking panel normally hands the cursor back to the operating system, so the lock is re-asserted every frame the dial is open.

**Nothing on the selection path is smoothed:** `Atan2` in, wedge index out, same frame. The old wheel also ran an exponential-damped parallax slide on the whole ring, which made the dial physically drift under the pointer while the pointer was trying to aim at it; that is gone. The only motion left is an 85 millisecond ease-out pop on open, which finishes before a human can react and never moves a wedge.

**Release selects, tap pins:** releasing the build key with a wedge lit selects it and closes, with no confirming click. Releasing inside the centre deadzone within 200 milliseconds instead pins the dial open so it can be read at leisure - left click then confirms, and left click inside the deadzone arms upgrade mode. A second press of the build key re-arms hold-and-release.

**Vector wedges instead of a CPU-painted bitmap:** the ring was a 256 by 256 `Texture2D` re-rasterised pixel by pixel on every hover change - 65 thousand pixels of trigonometry per tick, and a permanently soft edge because the wedge boundary was baked at 256 pixels and then stretched to 560. `RadialRing` draws the wedges with `Painter2D` instead. Boundaries are exact at any resolution, a hover costs one mesh rebuild, the inter-wedge gap is specified in pixels at the mid radius so it stays visually even, and the hovered wedge physically grows 15 pixels outward and 9 inward with a soft halo behind it - something a stretched bitmap could not do without smearing.

**Drawn piece icons replace the Unicode glyphs:** the wheel used characters like the white square and the box drawings cross to stand in for a foundation and a junction, which rendered inconsistently across fonts and read as nothing in particular. `BuildPieceIcons` authors an isometric line drawing for all eighteen families - the nine visible edges of a box for the structural pieces, a hipped roof, a stepped stair, a lathe-free hexagonal hull plate, a docking collar with clamps. `LineArtBuilder` rasterises them once into a white alpha mask using a per-segment bounded distance field, and the dial tints that one mask per state, so idle, hovered, unaffordable and locked all share a single texture.

**One ring, no pages:** eight-per-page paging meant the ten structural families spilled onto a second page that had to be scrolled to. Wedge count now follows the family count, so all ten structural or all eight station pieces sit on one dial. Scroll and Tab both swap between the two sets, and the set only swaps when Orbital Construction is researched.

**The hub reads like a spec card:** the centre used to carry a page counter and a generic glyph, while the piece name and cost were crammed into 7 and 8 point labels around the ring. The ring now carries icons only. The hub carries the hovered piece: its drawn icon, its name, a one-line description from the new `BuildFamilyInfo.Description`, and a cost line per ingredient reading "50 x Wood (985,703)" - the requirement, then what is actually carried, green when it is covered and red when it is not.

**A family with no prefab is honestly dark:** a family with no registry definition, or with an empty wood-tier prefab, now paints as a dark locked wedge and refuses selection with a named reason, instead of silently arming a build mode that could never place anything.

**Audio and colour answer every change:** a rate-limited tick plays on each new wedge, pitched slightly at random so a fast sweep does not machine-gun one note; confirming plays a higher click when the cost is covered and a lower one when it is not. The palette is a warm bone dial with an iron-oxide highlight, unaffordable wedges dropped to 34 percent alpha rather than hidden.

**GitHub title:** `[13.2.0-dev] The build dial rebuilt around the flick`

**Manual steps:**
1. Pull `Dev` and let Unity compile. The Console should be clear.
2. `Tools -> Voxel Engine -> Voxel Engine Setup` -> `100. Wire the Hammer Build Wheel`. This is non-destructive: it only creates the wheel object, its `UIDocument` and missing references, and it never replaces a sorting order, panel settings or a registry you already set.
3. Read the Console output of that step. Any family it names as a locked wedge has no definition or no base-tier prefab - run `5. Build Tiered Building Content` for the structural set and `89. Build the Orbital Station Family` for the station set, then re-run step 100.
4. Play. Equip the Building Hammer, hold the Build Wheel key (default `B`), flick toward a piece and release. The cursor should vanish, the pointer nub should leave dead centre, and the wedge should light the instant the hand points at it.
5. Flick well past the edge of the ring and release: it must still select that wedge.
6. Tap the key and let go without moving: the dial should stay pinned. Left click a wedge to confirm, or left click without leaving the centre to arm upgrade mode.
7. With Orbital Construction researched, press Tab or scroll while the dial is open and confirm it swaps to the eight station pieces.

### [13.1.2-dev] Double-Width Tires and a Watertight Carcass

**Type:** PATCH - visual and fitment polish on the wheel overhaul. Save-compatible: no schema, component or API change.

**Tires are twice as wide:** the preset table is the single source of truth, so doubling tire width there (2x2 0.90 to 1.80 m, 3x3 1.35 to 2.70 m, 5x5 2.05 to 4.10 m) carries through the mesh, the tire collider box, the mount socket and the setup tool without any other edit. Tire mass is deliberately unchanged, so handling and vehicle mass stay exactly where they were tuned.

**Carcass closed:** the tire lathe was an open tube at both beads, so in the gap between the rubber and the rim flange you looked straight into the inside of the tire. The profile now closes into a watertight torus with bead heels tucked under the flange and an inner liner riding on the rim barrel at a radius that overlaps the barrel rather than meeting it. Signed volume is positive, which is only true for a closed surface with outward normals.

**Mount offset no longer double-counts width:** the knuckle plane was computed as half a cell plus 55 percent of the tire width, and the socket then added another half width on top. At the old width that was already generous; at double width it would have hung the wheel metres off the hull. The knuckle now sits at a fixed 0.60 cells, clear of the face plate and independent of the tire, and the socket alone carries the tire's half width - which also means a mismatched tire size still sits with its inner face just clear of the upright.

**Suspension cast follows the rubber:** the raycast origin moved from the knuckle plane to the tire's centre plane. On a wide tire those are half a tire apart, and casting from the knuckle would have put the contact patch under the hull instead of under the tread.

**GitHub title:** `[13.1.2-dev] Double-width tires and a watertight carcass`

**Manual steps:**
1. Pull `Dev` and let Unity compile.
2. `Tools -> Voxel Engine -> Voxel Engine Setup` -> `12. Build Grid System Content`. The wheel meshes are overwritten in place, so existing prefabs pick up the wider, closed tire.
3. Check a tire prefab from a low angle at the rim flange: no gap and no view into the carcass.
4. Place a hub, fit a tire, and confirm the wheel sits just clear of the hull with the suspension still tracking it.

### [13.1.1-dev] Inside-Out Wheel Meshes and Detached Suspension Linkage

**Type:** PATCH - fixes the see-through wheel and hub reported after the 13.1.0-dev setup run, and connects the suspension linkage in the authored prefab. No behaviour, save or API change.

**The wheels were not transparent, they were inside out:** every generated mesh was wound so its face normals pointed at the axis instead of away from it. Unity culls the near wall and draws the far inner wall, which is exactly what a semi-transparent object looks like - the scene grid showing straight through the tire, the caliper visible through the hub plate, and a closed dished rim reading as an open tube. The winding is corrected in the lathe, the coil spring tube and the wishbone, verified by signed volume: every closed generated mesh now encloses positive volume, which is only true when the normals face outward.

**Opaque by construction:** generated wheel materials no longer inherit whatever queue the shader defaulted to. Surface type, blend factors, depth write, cull mode and render queue are pinned to opaque, tint alpha is forced to 1, and baked maps import with no alpha source. The mesh fix alone resolves the reported artefact; this makes the other possible cause impossible too.

**Damper no longer a cone:** the damper was a Unity cylinder rotated 90 degrees inside a pivot that gets scaled non-uniformly along X, and a rotated child under a non-uniform parent scale shears. It is now a generated `Strut` mesh authored along +X - a fat body over the inboard section with a thin chromed rod running to the eye - so stretching it telescopes instead of tapering.

**Linkage is attached in the prefab:** the arms, spring and damper were only posed at runtime, so the authored prefab showed them unrotated at scale 1 and the carrier appeared to float beside a spike. Posing now lives in one place, `WheelLinkage`, called by the mesh builder when the prefab is authored and by `GridWheel` every physics step. Anchors moved onto the mount plate and each one gained a visible bracket, the arms land on the upper and lower ball joints of the knuckle, and the strut foot sits inboard on the lower arm so it still shortens faster than the arms swing.

**Rebuilt meshes reach existing prefabs:** `PersistGeneratedMesh` used to return the mesh already on disk, which would have handed this rebuild the old inside-out geometry. It now copies the new data into the existing asset and keeps its GUID, so prefabs already referencing a wheel mesh pick up the corrected version without a broken reference.

**GitHub title:** `[13.1.1-dev] Inside-out wheel meshes and detached suspension linkage`

**Manual steps:**
1. Pull `Dev` and let Unity compile.
2. `Tools -> Voxel Engine -> Voxel Engine Setup` -> `12. Build Grid System Content`. This run overwrites the wheel meshes in place, so the existing prefabs are corrected rather than duplicated.
3. Select a tire prefab: the rim should now be a solid dished face with the bolted centre, with nothing showing through it.
4. Select a hub prefab: the wishbones, spring and damper should visibly span from the mount plate brackets to the knuckle at rest.
5. Play-test a loaded vehicle over a kerb and watch the coil shorten and extend.

### [13.1.0-dev] Modelled Wheel Geometry and Visibly Compressing Suspension

**Type:** MINOR - the wheel hub and tire are rebuilt on generated meshes instead of stacked primitives, and the suspension linkage now animates with the travel. Save-compatible: no schema, component or API change. Also carries the fix for the construction-time exception storm reported after the last setup run.

**Setup crash fixed:** `SurfaceProfile.Fallback` created a ScriptableObject on first access, and the first access happened inside a MonoBehaviour field initializer (`GridWheel` initialising its surface sample). Unity forbids `ScriptableObject.CreateInstance` during construction, so every `AddComponent`, prefab save, asset import and inspector redraw threw `CreateScriptableObjectInstanceFromType is not allowed to be called from a MonoBehaviour constructor`. The fallback object is gone: a `SurfaceSample` with a null profile already means plain ground, so `SurfaceSample.Default` is now a plain struct value with neutral multipliers and no allocation of any kind. `SurfaceProfileLibrary.Default` and `SurfaceSampler` return null instead of a manufactured asset, and the display name falls back to the `SurfaceProfile.FallbackName` constant.

**Generated wheel meshes:** `WheelMeshFactory` builds the wheel geometry mathematically - a lathed tire carcass with a crowned shoulder and radial tread blocks cut into the crown, a dished rim with a bolt face, a vented brake disc, a helical coil spring and a wishbone arm. Every mesh is unit-sized with the wheel axis on local X, so one mesh serves all three size classes after scaling.

**Tire rebuilt:** the carcass is one lathe surface instead of a cylinder with cubes glued around it, so the tread reads as rubber on a crowned tire rather than a ring of studs. The rim is a dished bronze face with a dark bolted centre, a chromed cap and a lug ring sized to the class (8 / 10 / 12 nuts).

**Hub rebuilt and animated:** the hub keeps its chassis mount and steering knuckle but now carries real upper and lower wishbones, a coil-over with a visible spring, a telescoping damper, a brake disc and a caliper. The wishbones, spring and damper are authored one unit long pointing down local X with their pivot at the chassis anchor, so `GridWheel` aims and stretches all four at the moving carrier every physics step. The strut foot sits inboard on the lower arm, which makes it shorten faster than the arms swing - the spring visibly compresses and rebounds under load instead of the wheel sliding on an invisible axis.

**Meshes are baked to assets:** generated meshes get the same treatment as the generated textures. The setup tool sets `WheelMeshFactory.MeshPersister` while it builds grid prefabs and writes each mesh once to `Assets/VoxelEngineAssets/GridSystem/Meshes`. Without this a prefab would reference a runtime-only mesh and come back with empty mesh filters after a domain reload.

**GitHub title:** `[13.1.0-dev] Modelled wheel geometry and visibly compressing suspension`

**Manual steps:**
1. Pull `Dev` and let Unity compile. The Console should be clear - in particular no `CreateScriptableObjectInstanceFromType` errors.
2. `Tools -> Voxel Engine -> Voxel Engine Setup`.
3. Run `12. Build Grid System Content` again. It rebuilds the six wheel prefabs on the new geometry; this is non-destructive, existing items, recipes and tuned values are kept.
4. Confirm `Assets/VoxelEngineAssets/GridSystem/Meshes` now holds the generated wheel meshes, and that the hub and tire prefabs preview with geometry (not empty boxes).
5. Play-test: place a hub, snap a tire on, and drive over a kerb. The wishbones should swing, the coil should shorten under load and extend on rebound.

### [13.0.1-dev] Unity 6.5 Surface Cache API Compliance

**Type:** PATCH - clears two Unity 6.5 compile errors and one warning introduced by the wheel overhaul. No behaviour, save or API change.

**Instance-id free caches:** `SurfaceSampler` no longer calls the obsolete `Object.GetInstanceID`. The collider cache is keyed on the `Collider` itself and the terrain alphamap cache on a `(Terrain, mapX, mapZ)` tuple. This also removes a latent correctness bug: an instance id can be recycled after a destroy and would have handed the next collider a stale surface profile.

**Deprecated lookup replaced:** the wheel hub panel's Eject Tire button now uses `FindAnyObjectByType` instead of the deprecated, ordering-dependent `FindFirstObjectByType`.

**GitHub title:** `[13.0.1-dev] Unity 6.5 surface cache API compliance`

**Manual steps:** none. In Unity on `Dev`, let the scripts compile and confirm the Console is clear. No Voxel Engine Setup run is required - the 13.0.0-dev steps still stand if you have not run them yet.

### [13.0.0-dev] Modular Wheel Hub and Tire Overhaul with Terrain Surface Friction Engine

**Type:** MAJOR - the one-piece grid wheel is replaced by a two-part hub + tire system with a hand-solved suspension and a surface-aware friction engine. Existing saved vehicles restore their wheel hubs but load with NO tire fitted, so every legacy rig needs tires crafted and snapped on before it drives again.

**Two-part wheel:** `GridWheel` is now the suspension HUB: it bolts to a grid cell and owns the spring, damper, steering knuckle, brake and axle torque. `GridWheelTire` is a separate attachment that snaps onto the hub's `TireSocket` and owns radius, width, mass and rubber friction. A tire is not a lattice block, so a 5x5 carcass hangs outside the cell it is driven from and never blocks a build. A hub with no tire carries no load, makes no torque and reports NO TIRE in its panel.

**Ghost snaps to the hub:** holding a tire switches `GridBuilder` into a dedicated mount path. The ghost is drawn at the hub socket the player is aiming at, never under the crosshair, so the preview is literally the placement. Direct aim beats proximity; otherwise the nearest compatible free socket within 6 m is scored by angle to the view ray. Blocked fittings show the reason (already fitted, wrong size, no hub in range).

**Mix and match sizing:** `WheelSizeClass` (`Size_2x2`, `Size_3x3`, `Size_5x5`) drives one preset table in `WheelTuning` that resolves collider radius, spring rate, damper rate, rest length, travel limits, axle/brake/handbrake torque, steering angle, steering rate and mass for a given cell size. Any tire fits any hub: the hub re-rates its spring and damper by the radius ratio so a 5x5 tire on a 3x3 hub raises the rig instead of bottoming out.

**No WheelCollider:** `WheelSuspensionSolver` is a pure, allocation-free raycast-spring and tire-force solver. Per wheel it produces suspension force from compression and axis velocity, longitudinal force from motor torque over radius, brake force capped by both pad and ground, rolling resistance, and a lateral impulse that cancels sideways slip - then clamps the result to the friction circle of that wheel's own normal load. Whatever the circle refuses is reported as slip instead of being silently applied.

**Terrain surface friction engine:** `SurfaceProfile` (ScriptableObject) carries surface name, forward friction, lateral grip, steering response, rolling resistance and slip FX threshold. `SurfaceProfileLibrary` maps Unity TerrainLayer names, PhysicsMaterial names and voxel `MaterialId` values onto profiles through one dictionary-backed lookup. `SurfaceSampler` resolves a contact patch in priority order: asphalt road run (with its wear multipliers), explicit `SurfaceTag`, Unity Terrain alphamap dominant layer, collider PhysicsMaterial (deriving multipliers when unregistered), then the voxel material below the wheel. Terrain alphamap reads are cached per terrain cell and collider lookups by instance id, so a convoy costs no per-wheel allocation.

**Slip made visible and audible:** `GridWheel.WheelSlip` is exposed for FX and audio. `WheelSlipFx` emits a plume tinted by the current surface profile once slip passes that surface's threshold, and `WorldAudioBootstrap` feeds slip into the wheel motor channel so wheelspin is heard as well as seen. Tread wears from slip, not distance, and worn rubber loses grip down to a floor.

**Visual linkages and procedural textures:** `GridWheelMeshBuilder` authors the hub (mount plate, steering knuckle, upper and lower wishbones, coil-over strut, brake disc and caliper, mount socket) and the tire (crowned carcass, directional lugs, shoulder blocks, dished rim, hub cap, lug nuts). Both wishbones aim at the moving carrier and stretch to reach it every step. `WheelTextureFactory` generates tread rubber, machined hub steel and chromed strut maps with matching normals; the setup tool bakes each map to a .png asset so prefabs never reference a runtime-only texture.

**Save schema:** `SavedGridBlock` gains additive wheel hub fields (mounted tire item id, mount side, size class, steerable flag, suspension strength, ride height, travel, tread). Legacy saves omit them and restore a bare hub - the breaking change this MAJOR bump exists for.

**GitHub title:** `[13.0.0-dev] Modular wheel hub and tire overhaul with terrain surface friction engine`

**Manual steps:** in Unity on `Dev`, let the scripts compile and clear the Console. Run `Tools -> Voxel Engine -> Voxel Engine Setup` and press `12. Build Grid System Content` (non-destructive: existing prefabs, items, recipes and surface profiles keep their tuned values and only get missing wiring repaired).
1. Confirm the new items exist: `Wheel Hub 2x2 / 3x3 / 5x5` and `Wheel Tire 2x2 / 3x3 / 5x5`, each with an Assembler recipe.
2. Confirm `Assets/Resources/SurfaceProfileLibrary.asset` exists and lists the eight profiles under `Assets/VoxelEngineAssets/Environment/Surfaces` (Dirt, Grass, Sand, Rock, Ice, Mud, Asphalt, Metal Deck).
3. In play mode, build a small grid with a cockpit and a battery, place four wheel hubs on the sides, then hold a tire: the ghost should jump onto the hub socket. Click to fit each tire.
4. Drive it. Check the hub panel shows GROUNDED, the live surface name, slip percentage and load, and that the wishbones follow the strut over bumps.
5. Drive onto ice or sand and confirm the rig loses drive and steering bite, throws a surface-tinted plume, and that asphalt gives back the most grip.
6. Save and reload: hubs come back with the same tires, tread and tuning. Any vehicle saved before this version comes back with bare hubs - fit new tires.

### [12.41.7-dev] Endpoint-Validated Energy Pipe Topology

**Type:** PATCH - makes Energy Pipe topology, mounted surface taps, and procedural conduit bridges use one endpoint contract. A socket now carries power only when its matching connector is physically valid and visibly represented.

**One endpoint contract:** `PowerCable` now uses the same 0.85 m tolerance for network links and visual occupancy. Linked connector faces must oppose each other, and each socket accepts only one compatible neighbouring socket. This prevents a loose near-by conduit from being treated as both a connected cable and an open machine-facing terminal.

**Machine and restored-tap validation:** Cable-to-machine links now require an unoccupied endpoint facing the contacted machine surface (or direct physical contact). `SurfacePowerTap` resolves static mounted pipes through that same check after placement, load, or a neighboring topology change, so an occupied or rear-facing cable end cannot restore an invisible manual machine bridge.

**Safe local visual refresh:** `RefreshNearbyCables` now completes its physics probe before rebuilding any conduit. This prevents a rebuild from overwriting unread shared probe results and leaving a neighbouring pipe mesh stale during rapid placement or dismantling.

**GitHub title:** `[12.41.7-dev] Endpoint-validated Energy Pipe topology`

**Manual steps:** no Voxel Engine Setup run is required. In Unity on `Dev`, let the scripts compile and clear the Console.
1. With only one generator, one consumer, and Energy Pipes, place a pipe from the generator and extend its open end with a straight pipe or bend. Confirm the old terminal immediately loses its machine bridge, the new terminal owns the only bridge, and the consumer remains powered only through the visible run.
2. Rotate a pipe so an open connector points away from a nearby powered machine. Confirm that it creates neither a conduit arm nor a power path. Rotate it back toward the machine and confirm the arm and power link return.
3. Try to crowd a third pipe onto an already joined socket. Confirm it does not create a hidden parallel power connection or an extra machine bridge; use a 4-way or 6-way junction for branches.
4. Save and reload a world containing a pipe mounted to a static generator, battery, or consumer. Confirm the valid face connection restores, while an occupied socket remains represented only by its linked pipe.

### [12.41.6-dev] Dynamic Conduit Occupancy Detection and Connected Pipe Visual Updates

**Type:** PATCH - automatically updates connected and adjacent pipe structures when new pipes are placed or removed, preventing stale phantom machine bridges when extending existing pipe networks.

**Socket occupancy & directional bridge gating:** In `PowerCable.RebuildVisuals`, each conduit endpoint verifies whether it is already connected to another `PowerCable` socket (`dist <= 0.45m`). Occupied sockets never generate machine bridges. For open sockets, machine surface proximity is gated by endpoint forward alignment (`Dot(toContact, normal) > 0.15`), ensuring pipes only extend toward machine faces directly in front of their open terminal sockets.

**Real-time neighbor visual notification:** Added `PowerCable.RefreshNearbyCables(center, radius)` called whenever a new pipe is placed in `BuildSystem` or dismantled (`OnDisable`). Connecting a new pipe to the end of an existing pipe immediately updates the existing pipe's mesh, seamlessly removing its machine extension and transferring the connection to the new terminal piece.

**GitHub title:** `[12.41.6-dev] Dynamic Conduit Occupancy Detection and Connected Pipe Visual Updates`

**Manual steps:** in Unity on `Dev`, let scripts compile and clear the Console. Run `Tools -> Voxel Engine -> Voxel Engine Setup`, select `6. Build Power Content` and `17. Build Factory Foundations + HV Grid` (safe and non-destructive).
1. Place a straight Energy Pipe facing a Coal Generator or Machine: confirm the pipe's open socket extends flush into the machine wall.
2. Snap a second straight pipe (or bend) to the first pipe: confirm the first pipe immediately updates, drops its machine bridge, and the second pipe continues the run cleanly into the machine with no overlapping/stale connections.
3. Remove a pipe segment: confirm adjacent pipe structures refresh their visual state immediately.

### [12.41.5-dev] Battery Balancing Transfer Throughput and Flexible Pipe Link Tolerance

**Type:** PATCH - enables responsive real-time battery balancing across Energy Pipes and cable networks, increases battery I/O throughput to 2,000 W (2 kW) for rapid charging and discharging, and widens pipe endpoint linking tolerance to 0.85m for robust multi-segment networks.

**Battery-to-battery balancing & transfer:** Upgraded `PowerBattery` default `ioRate` from 200 W to 2,000 W (and `capacityWattHours` to 10,000 Wh). In `PowerNetworkManager.BalanceConnectedBatteries` and `TickNetworks`, battery charge/discharge and inter-battery equalisation throughput now operates at full 2 kW+ speed instead of a slow trickle. Connecting a charged battery to an uncharged or lower-charge battery through Energy Pipes balances charge percentage in real-time.

**Flexible pipe link tolerance:** Widened `PowerCable.CanLinkTo` endpoint proximity tolerance to 0.85m, ensuring multi-segment straight runs, risers, and bends reliably bond into a continuous power network regardless of placement micro-offsets.

**GitHub title:** `[12.41.5-dev] Battery Balancing Transfer Throughput and Flexible Pipe Link Tolerance`

**Manual steps:** in Unity on `Dev`, let scripts compile and clear the Console. Run `Tools -> Voxel Engine -> Voxel Engine Setup`, select `6. Build Power Content` and `17. Build Factory Foundations + HV Grid` (safe and non-destructive).
1. Place two Batteries in the world: Battery A (charged) and Battery B (empty).
2. Connect them together using Energy Pipes (e.g. Straight or Bends).
3. Open the Battery UI / Inspect HUD on both: verify that energy flows actively from Battery A into Battery B and both batteries equalize their charge percentage in real time.

### [12.41.4-dev] Pipe-on-Pipe Network Snapping, Solid Connector Interior, and Machine Face Penetration

**Type:** PATCH - fixes pipe snapping onto other pipes and junctions, eliminates invisible backface culling on connector housings and inside socket cups, and guarantees conduits visually connect and penetrate flush into machine prefabs and generators.

**Pipe-on-pipe and junction placement:** Refined placement validation in `BuildSystem` to remove probe over-rejection while preserving duplicate placement protection (`dist < 0.25m`). Players can seamlessly snap straight pipes, bends, 90-degree risers, and 4-way / 6-way junctions onto existing placed pipes and socket endpoints.

**Solid connector housing & socket interior:** Re-orthogonalized the local coordinate frame in `EnergyPipeMeshBuilder.BuildConnectorHousing` to guarantee a strictly right-handed orthonormal basis (`Cross(right, up) == normal`) and corrected triangle winding across the front face, back face, side walls, and inside socket cup cylinder walls. Socket cups and connector blocks render completely solid, opaque, and visible from all viewing angles with no see-through hollow faces.

**Machine visual conduit bridging:** In `PowerCable.RebuildVisuals`, endpoint proximity probes (`Physics.OverlapSphereNonAlloc`) immediately detect adjacent machine colliders and calculate exact surface contact points (`col.ClosestPoint`). Dual conduits now extend directly from pipe endpoints into the machine wall (with 0.06m penetration) capped by a mounting flange, ensuring zero visual gaps.

**GitHub title:** `[12.41.4-dev] Pipe-on-Pipe Network Snapping, Solid Connector Interior, and Machine Face Penetration`

**Manual steps:** in Unity on `Dev`, let scripts compile and clear the Console. Run `Tools -> Voxel Engine -> Voxel Engine Setup`, select `6. Build Power Content` and `17. Build Factory Foundations + HV Grid` (safe and non-destructive). Equip an Energy Pipe:
1. Aim at an existing placed pipe (straight, bend, or 4-way cross): confirm that new pipes snap directly to its sockets and place cleanly without being blocked.
2. Inspect the connector block from all angles (both front and rear faces, and looking into the dual circular socket cups): verify that the housing and socket interior are completely solid and opaque (no invisible or backface-culled surfaces).
3. Place an Energy Pipe facing a Coal Generator or machine prefab: verify that the dual conduits automatically extend flush into the machine's wall with no floating gaps and power flows seamlessly.

### [12.41.3-dev] Machine Power Network Bridging, Hotbar Scroll Lock, and Conduit Overlap Guard

**Type:** PATCH - restores seamless power transmission between Energy Pipes and machines (generators, batteries, consumers), adds automatic visual extension bridging flush to machine faces so pipes never float in mid-air, blocks hotbar slot switching while holding V to scale straight pipe length, and strictly prevents placing conduits inside or overlapping existing placed conduits.

**Machine power transfer & visual bridging:** Replaced legacy rigid 1-unit grid delta tests in `PowerCable.CanLinkTo` and `PowerNode.CanLinkTo` with direct endpoint-to-socket and collider surface proximity tests. Energy Pipes now reliably link to generators, batteries, and consumers across all 9 shape variants and lengths. In `RebuildVisuals`, `PowerCable` detects connected machine surfaces and extends its dual conduits and connector flange directly to meet the machine face flush, completely eliminating visual floating gaps.

**Hotbar scroll lock (Hold V + Scroll):** Updated `GameUIController` to suppress hotbar slot cycling whenever the `V` key is held. Holding V and scrolling the mouse wheel now smoothly and exclusively scales the straight energy pipe length (1m to 5m) without inadvertently switching items in the player's hotbar.

**Conduit overlap prevention:** `IsPlacementProbeColliderAllowed` in `BuildSystem` now strictly disallows conduits from overlapping or being placed inside any existing conduit colliders, regardless of block stacking flags.

**GitHub title:** `[12.41.3-dev] Machine Power Network Bridging, Hotbar Scroll Lock, and Conduit Overlap Guard`

**Manual steps:** in Unity on `Dev`, let scripts compile and clear the Console. Run `Tools -> Voxel Engine -> Voxel Engine Setup`, select `6. Build Power Content` and `17. Build Factory Foundations + HV Grid` (safe and non-destructive). Equip an Energy Pipe:
1. Hold V and scroll the mouse wheel: confirm the straight pipe length scales (1m-5m) smoothly without switching hotbar item slots.
2. Connect a Coal Generator or Battery to a power consumer using Energy Pipes: confirm power immediately flows across the circuit and machines operate.
3. Observe the pipe-to-machine interface: confirm the dual conduits extend flush into the machine collider surface without floating gaps.
4. Attempt to place a pipe directly overlapping or inside an existing placed pipe: verify placement is refused and the ghost remains red.

### [12.41.2-dev] Connector Socket Snapping, Lattice Outward Alignment, and Glare/Backface Fix

**Type:** PATCH - eliminates see-through backface culling on connector housings, removes washed-out specular glare, enables true socket-to-socket endpoint snapping across all conduit shapes (including 90-degree risers and bends), prevents overlapping/intersecting pipe placement, and supports outward-facing orientation and full 3-axis rotation when mounting energy pipes to machine lattice faces.

**Socket-to-socket endpoint snapping:** When aiming at existing placed `PowerCable` segments, `BuildSystem` queries `EnergyPipeMeshBuilder.GetLocalEndpoints` to resolve the closest target socket (e.g. top of a 90-degree riser, end of a straight run, or junction branch). The held piece's entry connector snaps flush to the target socket, aligns to the outward normal, and rotates around the connection axis with `_rotSteps` (via BuildRotate / R key or Ctrl+Scroll). Placement validation verifies that new pipes connect exclusively at valid sockets and cannot be placed intersecting through existing pipe bodies.

**Lattice face outward alignment:** When placing an Energy Pipe against a machine or block surface in `TryGetStaticSurfaceAttachmentPose`, the pipe defaults to pointing perpendicular (straight out) along the surface normal. Players can freely cycle 90-degree rotation steps across all three axes using the rotation controls to point straight out, up, down, left, or right across the lattice.

**Connector backface and glare correction:** Corrects triangle winding order across the front face, rear face, side bevels, and recessed port socket cups in `BuildConnectorHousing`, ensuring all surfaces render opaque, solid, and without hollow gaps. Adjusts material metallic (0.15 - 0.50) and smoothness (0.25 - 0.40) to eliminate harsh white specular glare and showcase rich saturated tier metal and dark rubber grommets.

**GitHub title:** `[12.41.2-dev] Connector Socket Snapping, Lattice Outward Alignment, and Glare/Backface Fix`

**Manual steps:** in Unity on `Dev`, let scripts compile and clear the Console. Run `Tools -> Voxel Engine -> Voxel Engine Setup`, select `6. Build Power Content` and `17. Build Factory Foundations + HV Grid` (safe and non-destructive). Equip an Energy Pipe:
1. Aim at a machine face (generator/battery lattice): confirm the ghost can point straight out from the face and rotates cleanly along any axis with R or Ctrl/Shift+Scroll.
2. Place a 90-degree riser (`BendUp`), then aim at its top connector: verify a second riser or straight pipe snaps directly to the top socket and continues upward or turns without clipping or offsetting into the ground.
3. Attempt to place a pipe intersecting the middle of an existing pipe: confirm placement is refused and the ghost turns red.
4. Inspect the connector block: confirm the front face is completely solid (no see-through gaps, no inverted backfaces) and the finish is rich without white specular glare.

### [12.41.1-dev] Energy Pipe Connector Housing Revamp, Shape Wheel Alignment, and V-Key Length Adjustment

**Type:** PATCH - fixes radial Energy Pipe Shape Wheel segment label centering and texture alignment, refines energy pipe connector housings with rounded rectangular blocks, recessed dual circular port bezels, and strain-relief cable collars matching Pic 5, repairs missing endpoint connectors on vertical risers and compound bends (Pic 2 & 3), enables full rotation when aiming at existing power pipes, and remaps straight pipe length adjustment to Hold V + Scroll to prevent Ctrl rotation conflict.

**Energy Pipe Shape Wheel layout:** Corrects 9-slice radial wheel segment positioning, label centering (half-slice angular offset), and ring texture radius math. Slices render with premium cream backgrounds when unselected (using crisp dark charcoal typography) and vibrant cyan backgrounds when hovered/selected (with white typography). All 9 variant titles and icons fit cleanly without overlap or edge clipping.

**Connector housing and cable revamp:** Connectors on all pipe and conduit variant endpoints are redesigned to match Pic 5:
- Rounded rectangular solid housing body with corner bevels, finished in the active cable tier material (Copper, Iron, Gold, Superconductor).
- Two recessed circular dark port socket bezels with inner cylindrical cups and metallic central contact terminals.
- Rear strain-relief cable boot collars connecting each cylindrical cable cleanly to the back of the housing.
- Both/all terminal ends on every variant (Straight, 90-degree Horizontal Elbow, 90-degree Vertical Riser, Vertical S-Step, Horizontal S-Curve, Left-to-Up Bend, Right-to-Up Bend, 4-Way Cross, 6-Way Hub) now generate complete connector housings facing the true connection normals.

**Placement rotation and snapping:** When aiming at existing placed Energy Pipes, the placement ghost snaps flush to the nearest cardinal socket/endpoint and responds fully to player rotation controls (`_rotSteps` via BuildRotate / R key, Ctrl + Scroll, and Shift + Scroll). Straight pipe length scaling is remapped from Ctrl+Scroll to Hold V + Scroll, freeing Ctrl exclusively for orientation rotation. Placed `PowerCable` instances dynamically resize their `BoxCollider` to match active variant geometry for precise cursor raycasting.

**GitHub title:** `[12.41.1-dev] Energy Pipe Connector Housing Revamp, Shape Wheel Alignment, and V-Key Length Adjustment`

**Manual steps:** in Unity on `Dev`, let scripts compile and clear the Console. Run `Tools -> Voxel Engine -> Voxel Engine Setup`, select `6. Build Power Content` and `17. Build Factory Foundations + HV Grid` (safe and non-destructive). Equip an Energy Pipe in hand: hold the Build Wheel keybind (default B) to open the Energy Pipe Shape Wheel and verify that all 9 slices display centered icons and titles with high contrast. Select the Straight variant: hold V and scroll the mouse wheel to dynamically scale length between 1m and 5m; hold Ctrl and scroll to rotate the piece in 90-degree increments. Place vertical risers (Bend Up) and compound bends (Left/Right to Up): verify that both ends feature the complete rounded rectangular connector block with recessed dual port rings matching Pic 5. Aim at placed energy pipes and rotate with R or Ctrl+Scroll to confirm full rotational snapping.

### [12.41.0-dev] Energy Pipe Shape Variant Wheel, Dual-Conduit Aesthetics, and Tier Overload Explosions

**Type:** MINOR - introduces the radial Energy Pipe Shape Wheel UI for selecting conduit shapes, dynamic length adjustment (Ctrl + Scroll for 1-5m straight runs), dual-conduit procedural geometry with bolted terminal flanges, tier-specific aesthetics (oxidized copper, rusted iron, yellow gold, and superconductor energy beam animation), and active power overload explosions for finite energy pipe tiers (Copper, Iron, Gold).

**Energy Pipe Shape Wheel & Variants:** Holding an Energy Pipe and pressing the BuildWheel keybind opens a 9-slice radial selector (`EnergyPipeShapeWheel` using UI Toolkit) supporting Straight (1-5m), 90-degree Horizontal Elbow, 90-degree Vertical Riser, Vertical S-Step, Horizontal S-Curve, Left-to-Up Compound Bend, Right-to-Up Compound Bend, 4-Way Planar Cross Junction, and 6-Way 3D Omni Hub. For the Straight variant, players can hold Ctrl and scroll the mouse wheel to dynamically scale the placed conduit length from 1m up to 5m in real time with live ghost preview updates.

**Dual-Conduit Aesthetics & Tier Styling:** Procedural mesh generation (`EnergyPipeMeshBuilder`) builds parallel dual conduits with bolted end-flange plates and spiral hazard band styling. Tier materials feature distinct visuals:
- Copper: Warm oxidized copper bronze with subtle patina undertones.
- Iron: Dark cast iron steel with warm rust highlights.
- Gold: Radiant yellow metallic gold.
- Superconductor: Clean white-cyan ceramic shell with an active animated purple energy beam line core running through the conduit.

**Conduit Overload Explosions:** Finite Energy Pipe tiers enforce their real electrical throughput ratings (Copper: 10,000 W, Iron: 30,000 W, Gold: 50,000 W). When power flow across a circuit exceeds the pipe's capacity, the overloaded pipe triggers an immediate explosion flash, enters the `OverheatedPowerCable` state (pulsating red-hot heat for 2.0 seconds), and is destroyed. Superconductor conduits remain unlimited.

**GitHub title:** `[12.41.0-dev] Energy Pipe Shape Variant Wheel, Dual-Conduit Aesthetics, and Tier Overload Explosions`

**Manual steps:** in Unity on `Dev`, let scripts compile and clear the Console. Run `Tools -> Voxel Engine -> Voxel Engine Setup`, select `6. Build Power Content` and `17. Build Factory Foundations + HV Grid` (safe and non-destructive). Equip any Energy Pipe in hand: hold the Build Wheel keybind (default B) to open the new Energy Pipe Shape Wheel and select between the 9 shape variants. With the Straight pipe selected, hold Ctrl and scroll the mouse wheel to adjust length from 1m to 5m, verifying that the placement ghost updates immediately. Place each shape variant: verify the dual parallel shafts, bolted flange plates, and tier-specific materials render cleanly. For Superconductor Energy Pipes, confirm the glowing purple energy beam core is visible. To test overload explosions, connect a high power source/load (e.g. 15,000 W+) through a 10,000 W Copper Energy Pipe: confirm the pipe immediately triggers an explosion flash, glows fiery red-hot for 2.0 seconds, and disintegrates.

### [12.40.7-dev] Energy Pipe Visual Alignment, Overload Fault Destruction, and Battery Equalisation

**Type:** PATCH - repairs Energy Pipe visual silhouette and endpoint reach, restores full connector explosion and red-hot burning line destruction upon power overload, and resolves power transfer across Energy Pipes connecting to batteries and machines without modifying save schema or public APIs.

**Energy Pipe aesthetics and connectivity:** Energy Pipe mesh generation (`IndustrialPipeMesh.ProfileFor` with `PipeStyle.WireArm`) is refactored from bulky multi-shaft wheels to a sleek, unified cylindrical conduit profile with refined joint collars, matching the polished aesthetic of gas and liquid piping. `GridCableVisuals` passes `showUnusedFaceCaps = false` to suppress 6-way unlinked face spike nubs and applies the authentic metallic tier tint to conduit shafts with polished collar accents. `PowerCable.TouchesPowerEndpoint` dynamically tests adjacent grid cell reach against machine colliders, enabling Energy Pipes to reliably connect to batteries, generators, consumers, and compact wire connectors. In addition, machine endpoint visual arms terminate flush against the target collider face rather than embedding into internal machine origins.

**Overload fault destruction:** `PowerNetworkManager` side-power measurement now accounts for battery discharge availability and charge demand alongside generators and consumers. When electrical transfer across a rated compact connector exceeds its capacity (e.g. >1,500 W on a copper LV wire span), the connector triggers an immediate high-intensity explosion flash, is destroyed, and detaches the overloaded line. Overloaded manual wires and attached cables receive `OverheatedManualWire` and `OverheatedPowerCable` feedback: they glow with pulsating emissive red-hot heat in world space for 2.0 seconds before disintegrating. Station disconnection logic protects active burning wires from premature removal.

**Battery equalisation:** Batteries connected across valid Energy Pipe and wire routes now belong to the unified power network and smoothly redistribute stored energy toward a common capacity-weighted fill percentage within battery I/O limits and available conductor bandwidth.

**GitHub title:** `[12.40.7-dev] Energy Pipe Visual Alignment, Overload Fault Destruction, and Battery Equalisation`

**Manual steps:** in Unity on `Dev`, let scripts compile and clear the Console. Run `Tools -> Voxel Engine -> Voxel Engine Setup`, select `17. Build Factory Foundations + HV Grid`, and run it once (safe and non-destructive). Place Copper, Gold, Iron, or Superconductor Energy Pipes: verify they render with clean cylindrical shafts, sleek joint collars, and no stray spiky end caps. Connect an Energy Pipe directly to a Battery, Coal Generator, Power Light, or LV Wire Connector: verify the connection is established and the visual arm meets the machine collider face flush. Connect two batteries with differing charge levels using Energy Pipes and an LV Wire Connector with a copper wire: verify charge equalisation occurs and power flows across the conduit run. To test overload faults, place a high-demand consumer or battery draw exceeding 1,500 W through a 1,500 W Copper LV Wire connector: verify the connector explodes and is destroyed, while the wire and attached cable glow pulsating red-hot for 2 seconds before disintegrating.

### [12.40.6-dev] Direct Conduit Links and Finite Manual Wire Transfer

**Type:** PATCH - corrects utility topology, power endpoint contact, and power-transfer limits without changing save data, item IDs, prefab serialization, or public API. Item, gas, liquid, Energy Pipe, and Data Cable topology now permits only real direct cardinal/coplanar neighbour links. The runtime no longer infers an L/elbow route from an offset pair and the shared conduit mesh no longer renders one. A player must place the intervening pipe or cable segment to turn a run.

**Energy Pipes and endpoints:** an Energy Pipe may discover a broad nearby power node, but it creates a machine/connector link only after its endpoint reaches that target prefab's collider surface. It no longer draws a visual arm or transfers power across nearby empty space. Energy Pipes are unlimited: `ElectricalPipeDefinition.capacityWatts` is retained only as legacy tier metadata and is no longer a network bottleneck, overload input, heat source, or destruction condition.

**Manual wires and batteries:** player-drawn LV/HV wire links retain their finite `manualLinkCapacities` and are the only links that constrain a network's transfer budget. The generated LV tiers remain 1,500 W (copper), 15,000 W (gold), and 50,000 W (graphite). A compact connector overload is now evaluated only against an attached finite manual-wire span; it flashes/removes the connector and uses the existing manual-wire red-hot fault path, never damages an Energy Pipe. Batteries in one valid power network now move stored watt-hours from batteries above the capacity-weighted common fill percentage to those below it. Equalisation honours each battery I/O rate and the unused finite manual-wire capacity for the tick.

**GitHub title:** `[12.40.6-dev] Direct conduit links and finite manual wire transfer`

**Manual steps:** in Unity on `Dev`, let scripts compile and clear the Console. Run `Tools -> Voxel Engine -> Voxel Engine Setup`, select `17. Build Factory Foundations + HV Grid`, and run it once; the setup remains non-destructive/idempotent and preserves existing production values while reconnecting missing generated assets. Verify strict utility placement by placing matching pipe pairs and Energy/Data Cable pairs with only a diagonal or height-offset gap: they must show no joining arm and no transfer. Add the real cardinal intermediary segment(s), then confirm each direct placed link joins normally. Place an Energy Pipe within its broad discovery area but with a visible gap to a generator, consumer, battery, or connector: it must not draw to or power that target. Move/place it so the endpoint reaches the target collider face and confirm it links. For a manual-wire test, use a copper LV wire and demand above 1,500 W; confirm the route is limited/trips through a compact connector rather than treating the wire as unlimited, with the manual line using its red-hot removal feedback. Repeat with gold and graphite LV wire tiers. Set a test Energy Pipe definition's legacy capacity field low, then verify a valid Energy Pipe route does not throttle, heat, or burn because of that field. Finally, connect batteries with visibly different charge percentages through a valid wire network with no external load/source; their stored charge should converge toward one common percentage at their I/O rate and within the available wire throughput. Confirm normal consumers, relays, static surface taps, connector two-link limits, Data Cable placement/removal, and existing saves remain stable.

### [12.40.5-dev] Connector Power Transfer and Overload Safety

**Type:** PATCH - repairs compact LV/HV Wire Connector power routing and makes connector terminals a strict two-link part. Connector links now use one shared two-terminal budget across nearby Energy Pipes and player-drawn manual wires; the manual-link topology pass can no longer bypass that budget, and the wire tool checks both endpoints before consuming an item. This restores generator-to-consumer transfer through a connector with two valid links while refusing a third link and directing the player to a Power Relay. Setup Step 17 now repairs generated connector nodes to the two-link limit and updates their item descriptions without changing relay limits or production values. The stale `IsCardinalNeighbour` reference in `DataCable.OnDisable` is corrected to an internal neighbour predicate, resolving CS0103.

**Overload behaviour:** before the regular network bottleneck silently throttles a two-terminal compact connector, the power manager measures the isolated generator-to-demand transfer on its two sides. If that transfer exceeds a finite Energy Pipe or manual-wire rating, the connector trips: it emits a short red overload flash and is destroyed, attached Energy Pipes glow/pulse red-hot for two seconds before destroying themselves, and a drawn manual wire detaches into the world, burns red-hot for the same interval, then disappears. Superconducting/infinite-capacity links do not trip. Overload state is runtime-only and is not saved. No save-schema or public API change is required.

**Superseded power-limit detail:** `12.40.6-dev` corrects this initial fault scope: Energy Pipes are unlimited and never burn from `capacityWatts`; only finite manual-wire spans can constrain or fault a compact connector.

**GitHub title:** `[12.40.5-dev] Connector power transfer and overload safety`

**Manual steps:** in Unity on `Dev`, first let scripts compile and confirm the prior `DataCable.cs(80,34)` CS0103 error is gone. Run `Tools -> Voxel Engine -> Voxel Engine Setup`, then select `17. Build Factory Foundations + HV Grid`; it is idempotent and updates only the generated compact connector limit/description. Place a generator, two Energy Pipes, one LV or HV Wire Connector, and a consumer in a simple isolated line. Keep generation and demand below the selected Energy Pipe rating; after topology settles, verify the consumer is powered through the connector. Attach a third Energy Pipe or manual wire to that connector: it must be refused with `Connector Full` and no wire item consumed. Confirm an LV/HV Power Relay accepts the intended additional links. For overload, use an isolated two-link connector route with a finite-rated Energy Pipe or LV wire and make the generator-to-consumer transfer exceed that rating. Verify the connector flashes and disappears, each attached Energy Pipe or manual wire is visibly red-hot for about two seconds, then the damaged cable/wire is gone and the consumer loses power. Finally verify a below-rated run, infinite-capacity link, normal cable placement, Data Cable removal/replacement, static power taps, and existing saves remain stable.

### [12.40.4-dev] Orthogonal Pipe and Cable Riser Links

**Type:** PATCH - fixes local X/Y/Z pipe and cable links that were rejected whenever a neighbouring terrain placement introduced a small off-plane offset, and fixes the resulting geometry that previously projected every connection onto one straight nearest-axis arm. Item, gas, and liquid pipe pairs now accept one bounded orthogonal secondary leg alongside their normal 1–5 cell primary lattice run; arbitrary three-axis diagonals and overlong offsets remain rejected. Energy and data cable pairs use the same one-cell bounded elbow rule. The shared industrial conduit mesh retains the secondary local delta and builds a collared ninety-degree riser, including midpoint-owned pipe/cable half-links, so an uneven-ground run visibly reaches its connected endpoint instead of terminating in empty space. Power cable target coordinates are now passed once in world space before local conversion, repairing rotated/surface-mounted cable plane errors. Pipe-pair spatial hashes cover the complete bounded elbow envelope. No save, public API, prefab, item, recipe, research, balance, or Setup change is required.

**Superseded topology detail:** `12.40.6-dev` removes inferred elbow/riser links and geometry. Turns now require player-placed direct intermediary utility segments.

**GitHub title:** `[12.40.4-dev] Orthogonal Pipe and Cable Riser Links`

**Manual steps:** no Setup run is required. In Unity on `Dev`, first allow the project to compile and clear the Console. On flat terrain, place matching Item, Gas, Water, and Energy Pipe pairs one normal local grid step apart on each of local X, local Y, and local Z; confirm every pair joins and transfers its matching resource/power. Next create a stepped or uneven-terrain run: place two matching pipes one primary grid step apart with the second endpoint roughly 0.25–1.0 cell higher or lower. Confirm the route shows continuous primary shafts and a visible collared right-angle riser at the shared midpoint, with no straight arm ending in air, then verify flow across the pair. Repeat with two Energy Pipes and, where available, two Data Cables; confirm their bundled wire geometry follows the same correct local plane/riser and the Energy Pipe still powers a connected consumer. Repeat once on a rotated grid/static face. Finally, confirm a three-axis diagonal pair, an offset greater than one cell, and a player-wrench-blocked pair do not connect; verify ordinary same-plane pipe/cable runs, static face power taps, Portal Frames, and existing saves remain unchanged.

### [12.40.3-dev] Embedded Static Lattice Compile Repair

**Type:** PATCH - resolves CS0246 when `BuildSystem` was updated without its newly introduced preview helper source file. `StaticSurfaceLatticePreview` now lives in `BuildSystem.cs`, the only script that creates it, so the static utility lattice compiles as one self-contained placement change. The matching runtime `SurfacePowerTap` helper now lives in `PowerNode.cs`, its owning power topology script, eliminating a second fragile cross-file helper dependency. Behaviour is unchanged from 12.40.2-dev: portal ground support, static utility face lattices, and touching static power taps remain intact. No save, API, prefab, item, recipe, research, or Setup change is required.

**GitHub title:** `[12.40.3-dev] Embedded static lattice compile repair`

**Manual steps:** no setup run is required. Replace `Scripts/Building/BuildSystem.cs`, `Scripts/Power/PowerNode.cs`, and `Scripts/Power/PowerCable.cs` together, then let Unity compile. Confirm the prior `StaticSurfaceLatticePreview` CS0246 error is gone before repeating the 12.40.2-dev Portal Frame, static utility lattice, and static Battery-to-consumer Energy Pipe checks.

### [12.40.2-dev] Static Utility Surface Lattice and Power Taps

**Type:** PATCH - Portal Frames now resolve their real collider support plane against the aimed surface, so the 5 m root is lifted by its 2.5 m half-height and stands on terrain instead of being buried. Pipes, energy pipes, data cables, compact relays, and LV/HV Wire Connectors now mount cleanly to ordinary static `PlacedBlock` faces through a cyan 1 m surface lattice. The mount pose uses the actual host and held collider support planes, clamps cells to the visible face, preserves a tiny non-overlap clearance, and permits only the intended host contact during placement validation. Static power cables and compact voltage terminals discover a touching battery/generator/consumer after placement or load and create a direct, face-anchored power tap; cable arms now terminate at that touched surface rather than pointing through a large machine's centre. Existing grid placement, cable extension, ports, roads, conveyors, saves, and public APIs remain compatible. No prefab/item/recipe/research authoring is required.

**GitHub title:** `[12.40.2-dev] Static utility surface lattice and power taps`

**Manual steps:** no Setup run is required. In Unity, first place a new Portal Frame on flat terrain and confirm its lower support plane rests just above the ground; existing already-buried frames are intentionally not moved. Aim an Item/Gas/Water Pipe, Energy Pipe, or LV/HV Wire Connector at a static block face: a cyan lattice must appear, the ghost must stay outside the face, and only valid cells inside the face may be selected. Mount an Energy Pipe to a static Battery, extend its run by clicking cable segments, and mount the final segment to a static powered consumer. After the normal physics/topology settle, confirm the consumer receives power and the end arms visibly meet the two machine faces. Repeat with an LV/HV Wire Connector on a large or irregular powered block, then save/reload and confirm its touching power tap rebinds. Finally, verify normal grid pipe/cable placement, conveyors, roads, portals, and a deliberately intersecting Portal Frame still behave as before.

### [12.40.1-dev] Build Placement Hot-Path Cleanup

**Type:** PATCH - audited and tightened both every-frame construction preview paths before import. Standard `BuildSystem` and Hammer/tiered `BuildSystemV2` aiming now use reusable non-alloc raycast buffers and choose the nearest usable hit without sorting; unusually dense 64-hit stacks retain exhaustive fallback queries. Static-anchor/socket discovery and placement-overlap checks now use reusable non-alloc probes with equivalent overflow fallbacks. Large static edge snapping caches each held prefab's collider/classification data and reuses a collider list for the target. Ghost renderers now rebuild material-slot arrays only when validity changes, not every preview frame; the tiered path also removes an unused placement-feedback string build. Placement rules, special pipe/road/factory/busbar/turbine paths, portal overlap protection, save data, and APIs are unchanged.

**GitHub title:** `[12.40.1-dev] Build placement hot-path cleanup`

**Manual steps:** no setup run is required. In Unity, open the Profiler with GC allocation recording, then warm each preview once before sampling. Hold an ordinary block while aiming around terrain and a dense factory, repeat with Portal Frames, and repeat with the Hammer build wheel at ordinary and socketed tiered construction. The stable ghost-preview path should show no managed allocation from ray targeting, nearby anchor/socket probing, placement-overlap probing, or reapplying an unchanged ghost tint. Confirm Portal Frames remain flush, a deliberately intersecting frame is refused, and ordinary blocks, pipes, roads, factory connections, busbars, turbine sockets, and tiered socket snaps retain their placement behaviour.

### [12.40.0-dev] Portal Networks with Destination Selection

**Type:** MINOR - portals now form deterministic multi-endpoint networks. Matching NAME + CODE still identifies the shared network, but each controller now receives a persistent endpoint id and player-editable endpoint label. The controller panel exposes a Destination selector: choose an exact endpoint and every transit through that controller routes there. The endpoint id, label, and selected destination are additive `SavedPlacedBlock` fields, so they persist across save/load without changing or invalidating existing saves; apertures still never restore open. Existing two-portal saves retain automatic pairing when exactly one matching endpoint exists. A network with more than one possible destination deliberately refuses arbitrary routing until the player chooses one. No prefab, item, recipe, research, or Setup Step 99 authoring change is required.

**GitHub title:** `[12.40.0-dev] Portal networks with destination selection`

**Manual steps:** no setup run is required. In Unity, build or load three valid powered portals. Give all controllers the same Name and Code, label their endpoints (for example Home, Mine, and Orbit), press REFRESH ENDPOINTS on Home, select Mine as its Destination, then open all three. Home's Linked to row must name Mine and a player or ship entering Home must exit Mine, never Orbit. Save and reload: Home's endpoint label and selected Mine destination must remain. Finally, make a fresh two-controller Name + Code pair without selecting a destination and confirm its established automatic pairing still works.

### [12.39.7-dev] Large Static Block Edge Snap

**Type:** PATCH - ordinary static placement now resolves a clicked static block's collider support face before the legacy 1 m grid rounding. When either collider is larger than one grid cell along that face, the held prefab uses its resolved rotation and actual enabled collider geometry to land flush just outside the target rather than inside it. This fixes adjacent 5 m Portal Frames on every face, including rotated frames, and gives future large static prefabs the same safe edge snap. Pipes, roads, factory components, power cables/busbars, and wind-turbine sockets retain their existing dedicated placement paths. No prefab, item, recipe, research, save-data, or public API change is required.

**GitHub title:** `[12.39.7-dev] Large static block edge snap`

**Manual steps:** no setup run is required. In Unity, enter a world with grid snap enabled, place a Portal Frame, then aim at each side or top/bottom face with another Portal Frame selected. The ghost must sit flush and remain valid before placement; repeat after rotating the first frame 90 degrees. Finally, extend one static pipe, road, factory connection, busbar, and turbine socket to confirm their dedicated snapping behaviour is unchanged.

### [12.39.6-dev] Portal Placement Fix Compile Repair

**Type:** PATCH - 12.39.5 shipped one compile error: the new PortalPieceHalfExtents helper was inserted between IsThinConduitPlacement's final return statement and its closing brace, nesting the helper inside that method (a local function with an accessibility modifier - CS0106). The method now closes before the helper, and the duplicated brace after the helper is removed. No behaviour change beyond what 12.39.5 intended.

**GitHub title:** `[12.39.6-dev] Portal placement fix compile repair`

**Manual steps:** none. Verify it compiles.

### [12.39.5-dev] Portal Placement Volume Fix, the Controller Monolith

**Type:** PATCH - two portal fixes. Placement: portal frames could be planted inside each other, because the build system validates static placement with a small probe at the target centre, which cannot see a partial overlap between 5 m cells. Portal-sourced blocks (frames and controllers) now get a real volume test - the union of the placed prefab's colliders - against other placed blocks; flush neighbours (touching, not interpenetrating) still pass, frames inside frames are refused, and terrain stays permissive so a ring can still kiss a slope. The portal items no longer allow stacking. The Portal Controller is rebuilt as a 2.7 x 4.5 x 2.7 m monolith: plinth, hull column, glowing screen, steel pylons with lit tips, arch over a glowing core, rear cooling fins, conduit strips, twin antennae and a spinning energy dial that rotates while the block is powered (the prefab upgrade is idempotent - re-running Step 99 grows an existing console in place, designer children are untouched). The unit's stats match its body: 3000 HP, 2500 kg, max stack 5, and its cable connect radius grows to 2.5 m so wires can reach the wider frame. Re-run Setup Step 99 to upgrade the prefab and items.

**GitHub title:** `[12.39.5-dev] Portal placement volume fix, controller monolith`

**Manual steps:** Tools -> Voxel Engine -> Voxel Engine Setup -> "99. Build Portals" (re-run: upgrades the controller prefab to the monolith, rescales existing named parts, applies the item stat changes).

### [12.39.4-dev] Portal Frames at 5x Scale

**Type:** PATCH - portal frames grow 5x: the frame cell is now a 5 m slab (5x5x1.5, rims scaled to match) instead of 1 m. Every downstream metric derives from the frames' bounds, so the whole system scales with it - cell size, aperture metrics, the per-cell surface mesh and the transit radius. The 64x64-cell cap now means apertures up to 320 m a side. Per-block power and recipes are unchanged, so a maxed 64x64-cell portal still draws about 6.16 MW while open. Setup Step 99 renames the old PortalFrame_1m prefab to PortalFrame_5m in place (the guid follows the rename, so the item's reference and every placed frame keep working), rescales its authored visuals idempotently, and creates fresh installs at the new size; frames already placed re-size with the prefab and their portal re-scans on the next tick. Frame item description updated.

**GitHub title:** `[12.39.4-dev] Portal frames at 5x scale`

**Manual steps:** Tools -> Voxel Engine -> Voxel Engine Setup -> "99. Build Portals" (re-run to upgrade an existing frame prefab).

### [12.39.3-dev] Portal Compile Fix: Setup Window PowerConsumer Qualification

**Type:** PATCH - Setup Step 99 referenced PowerConsumer bare, but VoxelEngineSetupWindow has no using for VoxelEngine.Power (the file's convention is fully qualified references - every other PowerConsumer use in it was already qualified). Both call sites now read VoxelEngine.Power.PowerConsumer. No behaviour change.

**GitHub title:** `[12.39.3-dev] Portal compile fix: setup window PowerConsumer qualification`

**Manual steps:** none. Verify it compiles.

### [12.39.2-dev] Portal Compile Fix: EntityId as the Transit Key

**Type:** PATCH - follow-up to 12.39.1: Unity 6.5's EntityId is a wrapper struct, and its implicit conversion to int is itself obsoleted as an error, so keying the transit-immunity dictionary by int no longer compiles. The dictionary is now Dictionary<EntityId, float> and the TryGetValue/indexer calls are unchanged. Runtime-only state, nothing persisted - saves are unaffected.

**GitHub title:** `[12.39.2-dev] Portal compile fix: EntityId as the transit key`

**Manual steps:** none. Verify it compiles.

### [12.39.1-dev] Portal Compile Fix: Unity 6.5 API Drift

**Type:** PATCH - three Unity 6.5 API corrections in the 12.39 portal scripts, no behaviour change. PortalFrameBlock tested cached bounds with `Bounds.valid`, which does not exist - the cache now uses an explicit flag (and reads the collider once instead of twice). PortalControllerBlock used `FindObjectsByType(FindObjectsSortMode.None)`, deprecated in favour of the parameterless overload, and `GetInstanceID()`, which Unity 6.5 obsoletes as an error - both replaced (`FindObjectsByType<T>()`, `GetEntityId()`); the transit-immunity dictionary keys change type source but nothing is persisted, so saves are unaffected.

**GitHub title:** `[12.39.1-dev] Portal compile fix: Unity 6.5 API drift`

**Manual steps:** none. Verify it compiles.

### [12.39.0-dev] Player-Built Portals: Frames, Controllers, Name + Code Pairing

**Type:** MINOR - the warp gate's successor direction ships as a buildable system: portals the player assembles block by block, in any closed shape up to 64x64 cells, linked to each other by a custom NAME and CODE. Portal Frames are static placed blocks (placeable on station hulls and planetary ground alike - they are NOT grid blocks); assembled into a sealed outline they define the aperture. The Portal Controller mounts in front (within 8 m) and owns everything: it re-scans the frame network every 3 s (co-plane check, border flood fill, enclosed-interior extraction, 64x64 cap - a ring gets a ring-shaped aperture, a square a square one), charges the portal (45 s baseline at 60 kW + 60 W per interior cell) and then opens - and the open aperture is the real bill: 20 kW + 1,500 W per cell per second, so a full 64x64 burns about 6.16 MW while open, and losing the supply collapses the portal into a 60 s cooldown. Pairing is the two fields on the panel: two OPEN, valid portals whose trimmed names match (case-insensitive) and whose trimmed codes match exactly link to each other - that pair of strings is the whole address book. While open, the first hull (grid ship) or on-foot player inside the aperture crosses to the partner's mouth: the same floating-origin hop the drive and gate use (TeleportSubjectToCosmic plus SettleGridAfterHop), arriving just outside the linked aperture flying out along its facing at entry speed, collision-vetoed by ArrivalBlocked, with a 6 s re-entry immunity and a 3 s per-side transit lock so nothing ping-pongs. The aperture renders as a generated mesh - one quad per sealed interior cell under a pulsing warp material with a graceful shader fallback - so the portal looks like exactly the portal that was built. The controller panel carries name and code fields (applied live), state, shape and cell count, scan verdict, charge percent, live draw, the open-drain preview, the linked portal's name and the OPEN/CLOSE switch. Name, code, charge and cooldown persist on the placed block (new additive SavedPlacedBlock fields - legacy saves restore exactly as before); a portal never restores open. Authored by the new non-destructive Setup Step 99: PortalFrame_1m and PortalController prefabs (visuals only when missing), static Block_PortalFrame and Block_PortalController items (category Machines), deliberately expensive recipes (frame: 12 Steel Plate + 2 Advanced Circuit + 2 Lithium @ Assembler 12 s; controller: 20 Steel Plate + 10 Advanced Circuit + 4 Uranium Ore + 6 Lithium @ Assembler 60 s), and the Stable Portals research (tier 8, behind Warp Gate). The shipped GridWarpGate stays as-is; this is the design direction going forward.

**GitHub title:** `[12.39.0-dev] Player-built portals: frames, controllers, name + code pairing`

**Manual steps:** Tools -> Voxel Engine -> Voxel Engine Setup -> "99. Build Portals" (run after 98 so the research chain connects; non-destructive). Then verify it compiles.

### [12.38.1-dev] Compile Fix: Route Types Namespace

**Type:** PATCH - GridWarpDrive.cs imported IndustrialWorld.Navigation, but RouteWaypoint and RouteBook live in VoxelEngine.Navigation (the Navigation folder carries both namespaces). The using is corrected; every other touched file was audited and already resolves its route types (qualified or correctly used). No gameplay, save or setup change beyond making 12.38 compile.

**GitHub title:** `[12.38.1-dev] Route types namespace compile fix`

**Manual steps:** none. Verify it compiles.

### [12.38.0-dev] Warp Gate Prototype

**Type:** MINOR - the Era 7 headline ships as a buildable prototype: the Warp Gate, a fixed paired transit structure. Two gates sharing a pairing code (0-99, cycled on the gate panel; 0 = unpaired) pair up; a gate that is powered, charged (90 s at 120 kW) and in vacuum opens its aperture for a 25-second window, and the first ship whose hull enters the 220 m sphere is delivered to the paired gate's rendezvous point - 2 km off the partner on the approach line, collision-vetoed like every jump (a buried partner refuses transits instead of burying ships), arriving at rest. The hop reuses the drive's exact machinery (TeleportSubjectToCosmic plus the new shared SettleGridAfterHop settle) and the full transit FX (WarpFx gains a drive-less grid overload, so gates get the same tunnel, flash and streaks). One transit consumes the window; the coils then cool 120 s. The drive's commit path is unchanged - FinishArrival and ArrivalUnsafe became thin wrappers over the new public statics, zero behaviour change. The gate panel shows charge, state (OFFLINE/UNPAIRED/CHARGING/READY/OPEN/COOLDOWN), partner, draw, aperture and cooldown; changing the pairing code resets the charge honestly. Gate charge, cooldown and pairing code persist with the grid (no schema change - new dedicated save fields, same pattern as the drive). Authored by the new non-destructive Setup Step 98: prefab (ring housing + core, visuals only when missing), GItem_WarpGate, recipe (60 Steel Plate + 20 Advanced Circuit + 10 Uranium Ore + 8 Lithium @ Assembler), research res_warpgate (tier 8, behind Warp Drive). Prototype scope stated honestly: interplanetary pairing inside the charted system - the interstellar expansion hook stays open.

**GitHub title:** `[12.38.0-dev] Warp gate prototype`

**Manual steps:** one - run Tools > Voxel Engine > Voxel Engine Setup, press button 98 "Build Warp Gate", confirm the dialog. Verify in Unity: build two ships (or a ship and a station) each with a gate and power, set the same code on both panels, fly one hull into the charged gate's aperture, and confirm it arrives 2 km off the partner with the transit FX; flip one gate's code and confirm the state reads UNPAIRED; save/reload and confirm codes and charge come back.

### [12.37.0-dev] Route-Book Warp Legs

**Type:** MINOR - the auto-run shuttle loop now buys long legs from the warp drive instead of the tanks. When an armed loop's remaining leg exceeds one warp hop plus a 100 km margin, the loop engages the ship's own drive: it aims the exact frame the drive fires along through the gyros, banks the leg price plus the arrival reserve from the pooled drive battery (auto-recharge borrows the drive without touching the player's recharge toggle), keeps cruising toward the hold point while the coils spin, and fires without the confirm wheel the moment the cone is inside two degrees. After the jump the loop re-aims from the re-anchored origin and chains the next hop until the leg is short enough to finish on thrusters. Refusals degrade honestly: no drive, cooling, atmosphere, or a short leg all just cruise; a ship without gyroscopes logs that warp legs are off and cruises until re-armed; a stalled charge or three consecutive refusals abandon the leg for 30 seconds and cruise; the pilot's keys outrank everything as always. Disarming or pausing releases the drive and clears the borrowed auto-recharge. A WARP LEGS ON/OFF row sits on the auto-run panel (default on). No recipe, setup or save changes.

**GitHub title:** `[12.37.0-dev] Route-book warp legs`

**Manual steps:** none - code-only. Verify in Unity: arm a shuttle loop whose leg is longer than one hop with a warp drive, gyros and a powered grid aboard, and watch it spin, jump, and continue the loop; watch the WARP LEGS row toggle to OFF and confirm it cruises the whole way; grab the stick mid-charge and confirm the loop yields instantly.

### [12.36.0-dev] Warp Coil Resonator Item, Drive Upgrade Slots

**Type:** MINOR - the coil upgrade is now an item you install, not a research rank. A new crafted item, the Warp Coil Resonator (4 Advanced Circuit + 6 Lithium + 2 Uranium Ore at an Assembler, recipe unlocked by the Warp Coil Resonance research), is installed in three new RESONATORS slots on the warp drive panel - each installed resonator trims 15% off the drive's spin-up (0.85^count, same maths as before, stacking with the drive-count assist). The research node no longer grants a passive rank effect; it unlocks the recipe, like every other machine research - anyone who bought ranks in 12.35 keeps the recipe unlock, ranks just no longer add anything on their own. The resonator slots persist with the ship through the grid container save path (new save branch, no schema change). The panel's Coils row now shows the installed resonator count instead of the research rank. Setup Step 97 is reworked (same button, re-run safely): it authors the item, the recipe, and connects the research node behind Warp Drive; new nodes are single-rank. Balance and prefabs untouched.

**GitHub title:** `[12.36.0-dev] Warp coil resonator item, drive upgrade slots`

**Manual steps:** one - re-run Tools > Voxel Engine > Voxel Engine Setup, press button 97 "Build Warp Coil Resonator", confirm the dialog (it now also authors the item and recipe). Verify in Unity: research Coil Resonance, craft a resonator at an Assembler, open the drive panel, drop it into a RESONATORS slot, and watch the Coils row count it and the spin-up time drop; save and reload the ship and confirm the installed resonators come back.

### [12.35.0-dev] Warp Coil Resonance Research, Setup Step 97

**Type:** MINOR - the warp drive's charge time is now research-driven, closing the last half of the multi-drive roadmap line. A new repeatable research node, Warp Coil Resonance (res_warpcoils, 3 ranks, behind Warp Drive research), trims 15% off the spin-up per rank (0.85^rank - three ranks spool a lone drive in under 28 seconds), stacking with the drive-count assist from 12.34.0-dev. The drive reads the rank live through the new ResearchManager.GetRank(string) overload; with no research manager (menu, tests) the base time is used. Authored by the new Setup Step 97 (Tools > Voxel Engine > Voxel Engine Setup): non-destructive - creates the node only when missing, always connects it behind res_warpdrive, never touches recipes, prefabs or tuned values, and is safe to re-run. The drive panel's Coils row shows the research rank (R1-R3) next to the effective spin-up. No recipe, prefab or balance changes; the node costs 80 Science T2 + 40 Science T3 per rank.

**GitHub title:** `[12.35.0-dev] Warp coil resonance research, setup step 97`

**Manual steps:** one - run Tools > Voxel Engine > Voxel Engine Setup, press button 97 "Build Warp Coil Resonance", and confirm the dialog. It creates Research/Nodes/res_warpcoils.asset and links it behind Warp Drive; re-running only reconnects what is missing. Verify in Unity: research one rank (research UI, tier 7, after Warp Drive), open the drive panel, and confirm the Coils row reads R1 with spin-up down to ~38 s on a solo drive; ranks 2 and 3 bring it to ~32 s and ~27 s.

### [12.34.0-dev] Route Destinations in the Picker, Spin-Up Assist

**Type:** MINOR - the drive-panel destination picker now lists route-book destinations: every committed cosmic route on the ship's own book appears as an amber row and jumps to its final plotted point. The end point resolves live through the existing waypoint rules - waymarks track their block, body pins ride the world, frozen points stay frozen - so a jump lands where the route GOES, not where it was recorded. Scene-local routes (road and water networks on one planet) are skipped; a deleted route greys its row to GONE; a half-written legacy waypoint degrades to its frozen plot. And multiple drives now spool together: every enabled drive on the grid resonates its coils, shortening the commanded drive's spin-up by sqrt of the count (4 drives spin twice as fast, 9 three times). The bank, power draw and cooldown are untouched - assist only shortens the spin. The panel gains a Coils row showing how many drives aid and the effective spin-up time. Research-driven charge time remains open (needs a dedicated bonus research node). No recipe, research or setup changes.

**GitHub title:** `[12.34.0-dev] Route destinations in the picker, spin-up assist`

**Manual steps:** none - code-only. Verify in Unity: record a route in space (or load a ship with one), open the drive panel, and confirm an amber route row with live distance and price; jump it and arrive at the route's end point; rename/delete the route and watch the row grey to GONE; fit two or more enabled drives and confirm the Coils row counts them and the spin-up bar fills faster than a solo drive.

### [12.33.0-dev] Warp Safety: Damage Gate, Arrival Checks, Bank Reserve

**Type:** MINOR - the drive enforces its roadmap safety rules. A drive below 35% health refuses to spin or fire (repair first; the panel state reads DAMAGED). Every arrival is collision-checked: the point is pushed at least 25 km above every charted body's surface (and off the star as before), and a blind hop scatters its arrival laterally by up to 0.5% of the hop length - mapped locks (planet, singularity, locator, picker) stay exact; if collision safety still cannot find a valid arrival the jump is refused with the ship and bank untouched. Jumps keep an arrival reserve: a full jump must leave 10% of the price in the pooled bank and a partial hop banks the same share, so a ship never lands on an empty drive; a refused partial keeps every Wh because the partial now validates the arrival before spending. The autopilot banks the leg price plus the reserve so capture legs still land on their shelf in one hop. Balance note: with defaults, one full drive flies about 91% of a hop (2500 km becomes ~2272 km); set Arrival Reserve Fraction to 0 on the prefab for the old one-hop-per-drive. New prefab fields default in code; Setup Step 50 is untouched. No recipe, research or setup changes.

**GitHub title:** `[12.33.0-dev] Warp safety: damage gate, arrival checks, bank reserve`

**Manual steps:** none - code-only. Existing Warp Drive prefabs pick the new fields up at script defaults (35% health gate, 10% arrival reserve, 25 km arrival floor, 0.5% blind scatter). Verify in Unity: batter a drive below 35% health (state DAMAGED, charging and firing refuse with a toast); fire blind hops repeatedly and watch the arrival point scatter a few km between jumps; fire a locked pick and confirm it lands exact; run the bank down near the price of a long jump and confirm the partial popup keeps roughly the reserve; a full-price jump with a full bank now leaves 10% in the drive.

### [12.32.0-dev] Warp Arrival Fix, Drive Destination Picker

**Type:** MINOR - charged warps move the ship again: the 12.31.4 hull re-anchor registered the hull as a shift root, so ShiftWorld moved it together with the world and cancelled its own anchor change — every jump was a geometric no-op (FX played, kWh drained, the cooldown ran, the ship never moved). The jumping hull now holds its scene position while the rest of the world slides past it, which is what makes the anchor change real; nested riders stay on the hull and save/load restore is strictly more correct. The warp drive panel gains a destination picker (the roadmap's open destination-select line): charted planets and moons (never the star) plus powered beacons off the grid, rows with live distance, live price and affordability colour; a row click locks the target, the drive plots the approach shelf itself (near side of a world at the arrival altitude, 2 km off a beacon on the approach line) and runs the same fuel check and confirm wheel. A short bank still offers Jump partway, now flown along the target line instead of the nose. Rows grey out when the drive is not ready and mark a destroyed beacon GONE. No recipe, research or setup changes.

**GitHub title:** `[12.32.0-dev] Warp arrival fix, destination picker`

**Manual steps:** none - code-only. Verify in Unity: charge a drive in space, fire an aimed jump, and confirm the hull actually arrives (the arrival toast distance matches the new view); open the drive panel, pick a charted world, confirm, and arrive on its near-side shelf with gravity and streaming handing over; pick a powered beacon and arrive 2 km off it; fire a short-banked lock and confirm the partial hop heads toward the target; destroy the beacon's grid and watch its row grey to GONE without errors.

### [12.31.4-dev] Warp Moves the Hull, Vacuum Life, Planet Handoff

**Type:** PATCH - charged warp re-anchors on the grid hull (not the seated pawn), so ShiftWorld actually carries the ship. SetFrame now fires OnFrameChanged so gravity, grass and voxel streaming retarget. After the hop the frame is force-re-evaluated. Livestock and grass do not spawn in vacuum. Distant planet GPU surfaces sleep above 400 km. Proximity hold uses surface distance (not centre), so approaching a world switches frame and gravity. No recipe or setup changes.

**GitHub title:** `[12.31.4-dev] Warp hull hop, vacuum, planet handoff`

**Manual steps:** none. Verify in Unity: charged warp moves the hull (not only FX); no animals or grass in space; Earth scatter gone when far; flying up to another planet streams its surface and gravity takes over.

### [12.31.3-dev] Warp Actually Jumps, Save Depth, Less Fly Hitch

**Type:** PATCH - warp FX particle velocity curves mixed Constant/TwoConstants so PlayJump threw, left the drive pending, and the teleport never ran. Streaks are constant-mode and cheaper. Packed-drawer upgrades are a flat SavedUpgradeStack so JsonUtility no longer hits depth 10. Restored kinematic hulls no longer set velocity. Origin late-object sweep is 8 s and skips inactive. No recipe or setup changes.

**GitHub title:** `[12.31.3-dev] Warp jump, save depth, fly hitch`

**Manual steps:** none. Verify in Unity: charged warp actually arrives; load a world without the drawerUpgrades spam; fly without a hitch every two seconds.

### [12.31.2-dev] SavedGrid Cosmic Fields and Star Name Compile Fix

**Type:** PATCH - SavedGrid was missing hasCosmic / cosmicX/Y/Z (the save path wrote them, the type did not declare them). Warp star check uses SunSettings.displayName. No gameplay change beyond making 12.31 compile.

**GitHub title:** `[12.31.2-dev] SavedGrid cosmic fields compile fix`

**Manual steps:** none. Verify it compiles.

### [12.31.1-dev] SolarHazard Compile Fix

**Type:** PATCH - leftover duplicate closing braces in SolarHazard.cs (CS8803). No gameplay change.

**GitHub title:** `[12.31.1-dev] SolarHazard compile fix`

**Manual steps:** none. Verify it compiles.

### [12.31.0-dev] Warp Redo, Deep-Space Save, No Sun Hops

**Type:** MINOR - warp transit is a new tunnel (cockpit overlay + hull streaks, no camera shake). A hop never arrives inside the star. Deep-space logout writes cosmic km on the hull and restores the origin before the grids, so a ship saved in space comes back. Confirm wedges click like the build wheel. Nested origin shifts (seated pawn registered twice) were the cockpit twitch; velocity along the nose is carried through the jump. SOL APPROACH no longer stacks six identical cards. No recipe or setup changes.

**GitHub title:** `[12.31.0-dev] Warp redo, deep-space save, no sun hops`

#### Warp

The old bubble/shake sequence is gone. Pre-charge stretches a forward tunnel, a short flash drops you out, then the streaks fade. Screen FX only for the seated local player; observers still see the hull tunnel. No AddShake. FOV kick is mild and reset on arrival.

#### Sun

Planet-lock skips the star. Every destination is pushed outside SolarHazard.SafeWarpStandoffKm. Heat warnings read the seat/hull, not a parked pawn, and identical toasts refresh in place.

#### Save / twitch / autopilot

Grids save hasCosmic. Load restores the player (and origin) first, then hulls from cosmic km. ShiftWorld no longer double-moves a seated pawn. Arrival snaps the body, reseats the pilot, resets camera transients, and keeps forward speed so F3 cruise has something to work with.

#### Confirm

Mouse is free when the wheel opens. Point left or right and click, same as the building menu. Ctrl still locks look if you want it.

**Manual steps:** none - code-only. Verify in Unity: save/quit in space beside a ship (hull is there on rejoin); warp (tunnel, no shake, no SOL spam, not next to the star); point-and-click JUMP/ABORT; F3 after a hop (ship moves unless you already arrived on the nav target).

### [12.30.1-dev] Confirm Cursor Compile Fix

**Type:** PATCH - ConfirmDialogHud.ApplyCursor now uses UnityEngine.Cursor so it compiles under UI Toolkit (Cursor was ambiguous with UnityEngine.UIElements.Cursor). No gameplay change.

**GitHub title:** `[12.30.1-dev] Confirm cursor compile fix`

**Manual steps:** none - code-only. Verify in Unity: the project compiles; the jump wheel still opens.

### [12.30.0-dev] Jump Confirm Slider, Input-System Fix, Seated Autopilot, Seated Save

**Type:** MINOR - the jump confirm actually stays up (it was dying every tick on UnityEngine.Input under Input System only). The wheel now has a drive-count slider: Ctrl frees the mouse to drag it, the last choice is remembered (and saved on the grid) until you change it. F3 while seated flies the hull you are in instead of looking 60 m from a parked pawn. Saving while seated no longer deletes the ship: the player is unparented for the snapshot so the hull is written as its own grid. No recipe or setup changes.

**GitHub title:** `[12.30.0-dev] Jump confirm slider, seated autopilot, seated save`

#### Confirm wheel

ConfirmDialogHud never calls UnityEngine.Input when the Input System package is the handler (that InvalidOperationException was aborting Tick, so the ring never held). Look stays locked until Ctrl; then the mouse is free for the DRIVES slider (1..N enabled drives). A/D still pick JUMP / ABORT. The selection is GridEntity.WarpDrivesToUse (0 = all), restored on load, and the same slider sits on the warp drive panel.

#### Autopilot from the seat

F3 was measuring distance from the disabled player pawn (left at the last foot position). After you flew, that was further than 60 m. Seated F3 now engages ActiveControlGrid directly.

#### Seated save

SaveAll unparents the seated pawn, writes the grid, then reparents. A nested pawn made the hull look like player hierarchy and it did not come back on rejoin.

**Manual steps:** none - code-only. Verify in Unity: press warp (wheel stays up, no Input exception); two-plus drives, Ctrl, drag the slider, jump, reopen (same count); F3 from the seat with a nav target (engages, no "no ship in reach"); sit in a ship, save/quit/rejoin (hull is there).

### [12.29.0-dev] Confirm Every Jump, Warp Leave-Behind, Beacon Map, Real Dampeners

**Type:** MINOR - seated warp always asks before the hop (full bank or partial); autopilot still fires without the wheel. The jump no longer leaves the hull/pilot behind: world origin shift moves each registered rigidbody with its transform, and arrival snaps the grid body and seated pilot onto the cockpit. The confirm wheel is dark wedges with white labels; hover is left/right of screen centre (or A/D / arrows), so abort is actually selectable. A powered Grid Beacon paints the hull on the orbital map even when unnamed. Inertia dampeners brake with the grid's thrusters and gyros instead of zeroing velocity. No recipe or setup changes.

**GitHub title:** `[12.29.0-dev] Confirm every jump, warp leave-behind, beacon map, real dampeners`

#### Confirm before warp

Firing from the seat always opens the wheel (Jump / Abort, or Jump partway when the bank is short). Enter only takes the highlighted wedge; Esc / right-click abort. Autopilot warp legs pass skipConfirm so cruise does not stall on the dialog. Cockpit warp input is ignored while the wheel is open.

#### Warp leave-behind

SpaceOrigin.ShiftWorld now adds the same delta to each registered root's Rigidbody.position. After TeleportCosmic, CommitJump (and the partial hop) snap Grid.Body.position to the transform and the seated Pilot onto the cockpit, then zero residual velocity.

#### Confirm wheel

Wedges are charcoal; hover is a dark green / dark red. Labels stay white. Hover follows mouse X versus screen centre (dead zone in the middle) plus A/D and arrows. Default hover is none, so Enter does not fire until a wedge is chosen. Click uses the hovered wedge, not the inner disc.

#### Beacon on the map

GridBeacon keeps a live All list. OrbitalTrackingService adds a powered unnamed beacon hull as an in-range contact (named GridIdentity still wins; unnamed scrap without a beacon stays off).

#### Real dampeners

Seated and autopilot hold call ApplyAutonomousDampenerThrust (thrusters opposing velocity) and gyro torque against spin. Unmanned grids still preserve the gravity axis unless hover-hold. No linearVelocity / Acceleration-30 snap.

**Manual steps:** none - code-only. Verify in Unity: sit in a charged warp drive, press the warp key (wheel, point right or D then Enter/click to jump, left or A then Enter/click or Esc to abort); fire a short-banked hop the same way; after a jump the hull and you arrive together; F3 warp legs still auto-fire with no wheel; a powered beacon on an unnamed hull appears on the orbital map; dampeners on with thrusters fitted bleed speed instead of a hard stop, and a ship with no gyros keeps spinning.

### [12.28.2-dev] Partial-Jump Wheel - Point and Click, No Mouse Lock

**Type:** PATCH - the partial-jump confirm sat under a locked cockpit cursor, so neither button could be reached. It is now a two-wedge radial in the hammer-wheel language: UIState unlocks the look, point at JUMP or ABORT, click. Enter/Space takes the highlighted wedge, Esc / right-click aborts. No recipe or setup changes.

**GitHub title:** `[12.28.2-dev] Partial-jump wheel - point and click, no mouse lock`

**Manual steps:** none - code-only. Verify in Unity: fire a short-banked warp from the seat; the cursor frees, the ring appears, point right and click (or press Enter) to jump partway, Esc or the left wedge aborts; look relocks after.

### [12.28.1-dev] Autopilot Turns the Ship, Godmode That Actually Works, Mass Cap 12x

**Type:** PATCH - engaging fly-to now points the hull: gyros swing the strongest thrust axis onto the flight line, and warp legs aim the drive cone themselves (seated or unmanned). Infinite health stopped working because every frame the prefab checkbox wrote false over the Settings toggle; Settings is now the source of truth and DoT / vacuum / heat honour it too. Mass penalty cap is 12x (was 8x). No recipe changes.

**GitHub title:** `[12.28.1-dev] Autopilot turns the ship, godmode that actually works, mass cap 12x`

#### Autopilot orientation

Cruise used to command velocity with rotation pinned at zero, and a seated warp leg handed the mouse a parked gyro. The ship never lined up, so main engines sat idle and the warp cone never closed. Gyros now own the nose while engaged: cruise points the strongest of the six thrust axes along the commanded velocity (or at the target when holding), warp points the cockpit/grid forward at the destination and fires when the cone is inside the lock. No gyros still translates; warp refuses with the named reason. Stick take-over is unchanged.

#### Infinite health

PlayerController.Update was copying the inspector checkbox onto GameSettings every tick, so the Testing row could never stay on. Settings is the live flag; the checkbox mirrors it. Poison, burn, caustic, vacuum, heat, toxic and radiation drains now skip the same way TakeDamage already did.

#### Mass cap

maxMassFactor default and fallback are 12. Step 50 writes 12 when the field is zero or still the old 8 default; any other authored value is kept.

**Manual steps:** none - code-only. Verify in Unity: F3 with gyros fitted (nose turns onto the target, warp aims and fires without touching the mouse); Settings > Testing > Infinite Health, take a hit and stand in a hazard (no damage, toggle still on after reopen); a 6400 t hull shows 12x / 208 km hop.

### [12.28.0-dev] Jump Mass Penalty - Heavy Hulls Hop Shorter

**Type:** MINOR - maximum warp range now falls as the ship gets heavier. Hop length and Wh-per-km share one live factor, sqrt(hull mass / 100 t), capped at 8x, so one full drive still equals one hop. Cargo already sits in Grid.TotalMass, so filling a hold is the same as adding plates. The drive panel shows max hop, hull mass, the penalty and the live price; the autopilot banks that heavier price. Light ships at or under 100 t are unchanged. No recipe or setup changes.

**GitHub title:** `[12.28.0-dev] Jump mass penalty - heavy hulls hop shorter`

#### How the penalty works

The drive is rated at 100 t. Below that the published 2500 km hop and 4 Wh/km still hold. Above it the factor is sqrt(live mass / 100 t), so 400 t costs 2x and hops 1250 km, 1600 t costs 4x and hops 625 km. The cap is 8x (312 km hop, 32 Wh/km) so a loaded hauler still jumps. Planet-lock jumps still travel the real distance - they just pay the heavier price. Blind hops shrink. Dumping cargo recovers range on the next tick.

#### What the player sees

The warp panel adds Max hop, Hull mass and Mass penalty (RATED in green, or 1.41x in amber) and the jump price now ticks live. A short bank on a heavy ship still offers the partial-jump popup, with the mass-adjusted numbers, and the refuse toast says dump cargo as well as fit more drives.

#### Manual steps

None - code-only. Existing Warp Drive prefabs pick up the new fields at script defaults (100 t / 8x). Re-running Tools > Voxel Engine > Voxel Engine Setup step 50 fills ratedMassKg / maxMassFactor only when they are zero; power, hop, recipe and research are not touched.

Verify in Unity: open a warp drive on a light empty hull (penalty RATED, 2500 km hop, 4 Wh/km); load cargo until well past 100 t (penalty >1, hop shorter, price up, Range now down); fire a blind hop and confirm the shorter distance; dump the cargo and watch hop and price recover.

### [12.27.0-dev] F3 Double-Toggle Fix, Orbital Map Save, Godmode That Sticks and Partial Jumps

**Type:** MINOR - F3 engaged then instantly disengaged because the hotkey fired from two tick paths; the duplicate is removed and Toggle is now guarded to one action per frame. The orbital map no longer vanishes on load: equipment instrument slots are now saved and restored like every other slot group. Infinite health actually sticks now: the toggle persists in settings (new Testing row plus the PlayerController checkbox) instead of dying with the spawner. Mouse gyro parks while the autopilot flies (warp legs excepted, where you still aim). And a short bank no longer means no jump: firing with insufficient energy offers a partial-jump popup showing distance, percentage and remainder, and Accept flies the part the bank buys. No recipe or setup changes.

**GitHub title:** `[12.27.0-dev] F3 double-toggle fix, orbital map save, godmode that sticks and partial jumps`

**Manual steps:** none - code-only. Verify in Unity: press F3 once (stays engaged); save/load with the orbital map equipped (still there); tick infinite health, reload, take a hit (no damage); engage and move the mouse (nose stays); fire short-banked (popup with % and km, Accept jumps partway).

### [12.26.0-dev] Autopilot Take-Over, F3 Key, God-Mode Toggle and Warp Screen Redo

**Type:** MINOR - the fly-to autopilot now behaves: it requires an AutoRunPilot block aboard (engage refuses and a live leg disengages without one), and grabbing the stick disengages with Autopilot disabled, player input detected instead of fighting you for the ship. The autopilot hotkey moves off P (landing gear / parking) to F3, is wired up for the first time (it was map-button-only despite the label), and stays rebindable in Settings like every other action. PlayerController gets an infiniteHealth testing toggle (damage immunity; forced respawn still works). And the warp screen is redone: thin bright speed lines instead of chunky bars, a much subtler edge glow. No recipe or setup changes.

**GitHub title:** `[12.26.0-dev] Autopilot take-over, F3 key, god-mode toggle and warp screen redo`

**Manual steps:** none - code-only. Verify in Unity: engage fly-to without an AutoRunPilot (refused), with one (flies), grab the stick (disengages with the message); press F3 to toggle; rebind it in Settings; tick infiniteHealth and take a hit; jump and confirm the new screen effect.

### [12.25.1-dev] Warp Smear Fix, Arrival Readout and Particle API Fix

**Type:** PATCH - the warp effect smeared the ship itself: the bubble's near wall washed over the hull and the streak particles were huge additive smears, so the bubble now renders its far shell only (ship stays crisp inside) and the streaks are small dim speed lines. Fixes the WarpFx compile error on this Unity version (StretchBillboard does not exist here, Stretch does) and the obsolete ShapeModule.box warning (now scale). And the arrival finally answers where you are: the jump toast shows the distance jumped, and the cockpit WARP LCD holds an arrival line for ~25 s (destination, origin to here, km, plus nearest body after a blind hop). No recipe or setup changes.

**GitHub title:** `[12.25.1-dev] Warp smear fix, arrival readout and particle API fix`

**Manual steps:** none - code-only plus one shader tweak. Verify in Unity: fire the drive and confirm it compiles clean, the ship stays crisp inside the bubble with small streaks past the hull, the toast shows the km jumped, and the cockpit WARP line holds the arrival readout afterwards.

### [12.25.0-dev] Warp FX - Jump Effect, Live Warp Key and Cockpit Warp Readout

**Type:** MINOR - the jump drive gets its moment: firing the drive now swallows the ship in a pulsing energy bubble, stretches the stars into warp streaks past the hull, tunnels the screen with a flash, punches the FOV wide and rumbles through a synthesized riser-to-whoosh (all procedural, no assets; screen FX only for the grid the player is aboard). Also fixes the warp key mixup: the v17 rebind moved warp N to U but the drive still said press N, so the prompt now shows the live binding and the setup docs say U. And the cockpit finally shows the autopilot warp leg line (aim/charge/bank) on its LCD, so a seated pilot can see the ship is waiting on their aim instead of wondering why it never fires. No recipe or setup changes.

**GitHub title:** `[12.25.0-dev] Warp FX - jump effect, live warp key and cockpit warp readout`

**Manual steps:** none - code-only plus one shader file. Verify in Unity: charge and fire the drive (U by default) and confirm the bubble/streak/screen/FOV/sound sequence plays and the ship arrives; engage the autopilot on a long leg while seated and confirm the cockpit WARP LCD line appears and the drive auto-fires once aimed.

### [12.24.1-dev] Grid Gas Topology Cache - Piped Thruster Lag Fix

**Type:** PATCH - per-tick thruster gas queries on grids walked OverlapSpheres, corridor sweeps and port-tree scans every 0.15 s per thruster (hundreds of physics probes per second per ship: the 5 FPS freezes with pipes near thrusters). Link discovery now runs once per topology change into a cached per-grid snapshot using the same predicates, and queries follow cached links in microseconds. Wrench blacklist, tank enabled/type/mode stay live per query, so routing behaviour is unchanged. No recipe or setup changes.

**GitHub title:** `[12.24.1-dev] Grid gas topology cache - piped thruster lag fix`

**Manual steps:** none - code-only fix. Verify in Unity: build a ship like the report (a few brass pipes feeding 4 hydrogen thrusters), run it, and confirm the frame rate stays smooth while thrusting; then place/remove a pipe mid-run and confirm gas connects within a couple of seconds.

### [12.24.0-dev] Warp Core - Gas Pipe Perf Fix, Warp Battery Fuel and Drive Panel

**Type:** MINOR - gas tank lookups move from per-query physics BFS to a cached tank map (the 5 FPS at 10+ pipes), and the warp drive gets its fuel model plus the hero panel: an internal battery per drive, pooled range, a recharge toggle, max-draw display, and a live lime-on-black widget with power visibly streaming in while it charges. No recipe or setup changes.

**GitHub title:** `[12.24.0-dev] Warp core - gas pipe perf fix, warp battery fuel and drive panel`

#### Gas pipes stop melting the frame rate

Every gas tank lookup used to walk the pipe network with physics: each visited pipe fired a sphere probe plus a 31-probe corridor sweep, so one question on a 10-pipe run cost 300+ overlap queries - twice a second per machine. Tank discovery now happens once per topology change plus one pipe per quarter-second on a rolling refresh, and queries are dictionary lookups with a cheap range check (no physics, no BFS, no per-query allocation). New tanks still register instantly through topology dirties, and cached links drop the moment grids drift apart instead of drawing gas across the gap. Same tanks found, same filters, roughly 10x cheaper.

#### Jump fuel: range is bought, not granted

Each warp drive now carries a 10 kWh internal battery fed from the grid bus, and all enabled drives on a grid pool their stores. Jump cost is distance times Wh-per-km (default 4, so one full drive flies exactly one fixed hop), consumed proportionally across the pool; short banks refuse with the exact numbers and the honest advice (recharge, or fit more drives). The 45-second spin-up stays as the coils warming - fuel and spin-up are independent, and a starved grid banks slowly instead of pretending. Stored energy, the recharge toggle and cooldowns persist with the grid.

#### The drive panel

Opening a warp drive now shows the hero widget: big fill %, live charge kW with a status dot, a bolt in a ring that pulses while power streams in from both sides, a time-to-full pill plus red stop, then stored/pooled/range/max-draw/price/spin-up/cooldown stats and RECHARGE, SPIN UP and JUMP controls. The autopilot banks for its legs automatically (player toggle untouched) and reports BANK vs need on the map card.

### [12.23.0-dev] Jump Legs - Autopilot Warp Integration

**Type:** MINOR - long fly-to legs now jump: the autopilot aims the ship, charges the warp drive, fires when aligned, and resumes the cruise after arrival - planet-lock captures in one jump, longer hauls by repeated aimed hops. No recipe or setup changes; no new blocks or items.

**GitHub title:** `[12.23.0-dev] Jump legs - autopilot warp integration`

#### How a warp leg flies

When the remaining distance is past one fixed hop, or a target body sits inside the drive's capture band, the cruise hands over to the warp states: WARP-AIM turns the ship (unmanned ships steer themselves through their gyros; seated pilots aim with the mouse and the map shows how many degrees off they are), WARP-CHARGE holds the aim while the drive charges, then fires automatically when charged and aligned. The ship keeps cruising throughout - a jump zeroes velocity anyway, so stopping first would only waste time. Capture jumps land exactly on the autopilot's own hold shelf, so arrival just happens.

#### Honest jumping

Everything reads the drive's live tuning: capture band, hop range and cone angle all follow the block's own numbers. Cooldowns are cruised through and shown on the map card; a stalled charge says STALLED (power?); three refused fires, unmanned aim without gyros, or 90 seconds of failing to line up abandons warp for the leg with the reason announced - the cruise still completes, just slower. Disengaging never destroys a paid-for charge: a charging drive finishes and sits ready for a manual firing.

#### Safety rails

Two new live guards ride every leg: retargeting to the sun mid-flight releases the ship instead of flying into it, and touching atmosphere releases it instead of lithobraking at cruise speed. Aiming is computed through the exact frame the drive fires along, so a sideways cockpit can no longer send the jump the wrong way.

#### What stays open

Route-book warp legs for the auto-run loops and the destination-select UI remain open under item 15; hazard avoidance, dock approach, cargo ops and atmospheric legs remain open under item 8.

### [12.22.1-dev] Fly-To Compile Fix

**Type:** PATCH - fixes the 12.22.0-dev compile error (`reason` used before assignment on the engage path) and the `FindObjectsSortMode` deprecation warnings (now uses the `FindObjectsInactive.Exclude` overload like the rest of the codebase). No behaviour changes.

**GitHub title:** `[12.22.1-dev] Fly-to compile fix`

### [12.22.0-dev] Fly To - Nav-Target Autopilot Cruise Control

**Type:** MINOR - press P (or the map's ENGAGE AUTOPILOT card) and the ship in reach flies itself to the orbital map's nav target: a braking-curve cruise computed from live thrust and mass, stick override with resume, and an arrival hold on the warp shelf. No recipe or setup changes; no new blocks, items or wizard steps.

**GitHub title:** `[12.22.0-dev] Fly to - nav-target autopilot cruise control`

#### Engage and fly

Set a nav target on the orbital map (M), stand by your ship (or stay in the seat), and press P - or use the new AUTOPILOT card on the map itself. The nearest ship within 60 m flies the leg: seated pilots keep their seat, camera and tools while the cruise owns translation, and unseated engages get a 3-second STAND CLEAR countdown first. The map card shows the live leg underneath: state, target, distance, speed and ETA.

#### Honest physics, said out loud

Speed is governed by a braking curve, not a wish: v = sqrt(2*a*d) from the ship's own directional thrust and live body mass, capped at 2500 m/s, so a heavy ship with a weak drive genuinely takes longer to stop. Legs that cannot brake along the flight line are refused (or released mid-flight) with the reason announced, legs that make no progress for 25 seconds are abandoned rather than flown forever, and the autopilot will not fly into the sun. Touching the translation stick overrides instantly and the cruise resumes on release; rotation always stays live.

#### Arrival and hold

Bodies are met at the surface plus 90 km - the same shelf the warp drive arrives on - while ships, stations and other contacts hold at 300 m. On arrival the ship keeps station and says so; P releases it. Exiting the seat mid-cruise does not stop the ship: it continues unmanned and announces that too.

#### What stays open

This is the first autopilot leg only: vacuum flight, one ship at a time, no terrain or traffic avoidance, no atmospheric legs, no docking, and no warp legs yet. Those remain open under roadmap item 8 (warp legs additionally under item 15).

### [12.21.0-dev] True Colours - Planet Hues, Pollution Readout, Bigger Text and Orbit Pace

**Type:** MINOR - planets and moons wear their authored hues on the star map, every body row carries a pollution readout (wired, awaiting the simulation), all map text is bigger, and world creation offers a Realistic / Arcade orbit pace so planets visibly sweep in arcade worlds. No recipe or setup changes; old saves default to realistic pace.

**GitHub title:** `[12.21.0-dev] True colours - planet hues, pollution readout, bigger text and orbit pace`

#### Planets in their own colours

Every planet and moon asset authors a `displayColor` - the same hue its sky beacon already wears - and the map now uses it for markers, labels and sidebar rows, so Mars reads red, ice reads pale and volcanic reads ember. Bodies without an authored hue keep the classic blue/grey kind colours. Orbit rings stay uniformly green: one trajectory language, no rainbow spaghetti.

#### Pollution, ready when you are

Each planet and moon row now shows a `POLLUTION 0%` line under its motion state. The value flows through the tracking snapshot from a clearly-marked seam that returns zero until the pollution simulation lands - wire the real per-body source there and every readout lights up with no further map changes.

#### Bigger text

All map type is bumped: floating labels grow to 12/11 px, sidebar rows to 11/9/8 px, and the header, status, focus, nav and hint lines each step up. Layout and the 310 px sidebar are unchanged; everything still fits.

#### Orbit pace at world creation

The new-world form's solar-system section gains an `ORBIT PACE` picker: `REALISTIC` (true Keplerian periods - a year takes days) or `ARCADE` (planet orbital speed x120, a year in minutes, visibly sweeping on the map). The choice is stored per world in the cosmos sidecar next to the system and seeds; old saves without the key default to realistic, and it is deliberately not editable after creation since it shapes generation. Only planet elements are accelerated - moons, rails craft, seasons and lighting keep normal time.

#### Manual steps in Unity

1. Apply the patch on top of 12.20.0-dev and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Press `M`: planets wear distinct hues in markers, labels and rows; each planet/moon row shows `POLLUTION 0%`; all text is visibly larger.
4. Create a new world: the solar-system section offers `REALISTIC` / `ARCADE` orbit pace with realistic preselected.
5. Start an arcade world, open the map and watch: planets visibly move along their rings within a minute or two.
6. Load an older save: it plays exactly as before (realistic pace, no behaviour change).

### [12.20.0-dev] Clean Chart - Trajectory Toggles and Map Legibility

**Type:** MINOR - the star map gets trajectory visibility toggles per class (planets, constructs, satellites), zoom-adaptive planet markers that stay readable when zoomed in, and label decluttering so the system's heart stops piling names on top of each other. No save, recipe or setup changes.

**GitHub title:** `[12.20.0-dev] Clean chart - trajectory toggles and map legibility`

#### Trajectory toggles

A small `TRAJECTORIES` card sits top-right of the map with three checkboxes: Planets (planet and moon paths), Grids (ship and station trajectories) and Satellites. Unchecked hides that class's rings and trails; checked shows them. The choice persists across sessions, and the equipped device's orbit-path capability stays the master switch underneath. Flipping a checkbox never pans the map or disturbs the nav target.

#### Readable at any zoom

Body markers kept to true scale shrank to dots the moment you zoomed in on their moons. Markers now keep a zoom-adaptive floor (3 px at system zoom, growing to 20 px fully zoomed in), so a planet stays a readable disc at any magnification. Click hit-testing follows the same sizes, so what you click is still what you see.

#### Declutter

A moon hugging its planet keeps its name to itself until zoomed in enough to stand clear of it, which alone clears the label pile-up at the system's heart. The belt's label rides the bottom edge of its ring instead of sitting on the crowded centre. Planet, sun and construct labels are unchanged.

#### Manual steps in Unity

1. Apply the patch on top of 12.19.2-dev and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Press `M`: the `TRAJECTORIES` card sits top-right with Planets, Grids and Satellites checked.
4. Uncheck Planets: planet and moon rings and trails vanish; recheck and they return. Same for the other two.
5. Zoom fully in on Earth: the planet stays a readable disc instead of a dot, and the Moon's label appears once it stands clear.
6. Close and reopen the game: toggle choices are remembered.

### [12.19.2-dev] True Plane - Orbit Map Projection and Path Fixes

**Type:** PATCH - fixes how orbits display on the star map: the map projected the XZ plane while the system actually orbits in the XY reference plane, so every planet drew in an edge-on row; planet solar ellipses were skipped entirely; and body rings were axis-aligned fictions. The map now projects the true orbital plane, draws every ellipse, and samples body rings and trails from the live elements. No save, recipe or setup changes.

**GitHub title:** `[12.19.2-dev] True plane - orbit map projection and path fixes`

#### The map was edge-on

Orbital elements are seeded about the reference (XY) plane, but the map projected top-down XZ - exactly edge-on to every orbit. That is why the whole system drew as a single row of planets. The projection now uses XY, so the system opens face-on: planets spread around the sun with their true shapes, and the same frame carries labels, clicks, the belt and the nav reticle with it.

#### Rings for every planet

The ellipse pass skipped any entry with a null parent pointer - which is every planet, since the sun is not a `BodyInstance`. Planets now resolve their parent by name (the sun's entry is always present), so each planet draws its solar ellipse. Craft behave exactly as before.

#### Exact paths, exact trails

Body rings are no longer axis-aligned ellipses from apoapsis/periapsis radii: the tracking service now carries each body's node, argument of periapsis and live true anomaly, and the map samples the ring through the same elements-to-position path as propagation. Whatever orientation and inclination was seeded, the ring matches it - and the body always sits exactly on its own ring. Trails are the true-anomaly arc behind the body's live position through the same sampler. The parent-surface padding that inflated moon rings is gone for bodies (their radii were already centre-based); craft keep it, since their telemetry is altitude-based.

#### Belt band

The belt gains its inner edge: the shell now reads as a band between its nearest- and furthest-rock radii instead of a single ring, with the rock motes scattered between.

#### Manual steps in Unity

1. Apply the patch on top of 12.19.1-dev and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Press `M`: planets spread face-on around the sun, each with its own ellipse - no more single row.
4. Zoom to Earth and the Moon: the Moon sits on its ring with Earth at the focus, and its trail hugs the ring behind it. (If the ring still looks off-centre, check the local `Moon_Earth` template's `orbitEccentricity` - the focus offset is real physics for the authored value; 0 gives a centred ring.)
5. Confirm clicks, the nav reticle, the belt band and the sidebar behave as in 12.19.0-dev.

### [12.19.1-dev] Local Click - Orbital Map Pointer Fix

**Type:** PATCH - fixes a compile error in the star map's click-to-acquire handler: `PointerUpEvent` exposes the cursor as `localPosition`, not `localMousePosition`, which only exists on the mouse-event family. One-word fix, no behaviour change.

**GitHub title:** `[12.19.1-dev] Local click - orbital map pointer fix`

#### Manual steps in Unity

1. Apply the patch on top of 12.19.0-dev and recompile: the CS1061 error is gone.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Press `M` and click a contact: the navigation target still acquires exactly as in 12.19.0-dev.

### [12.19.0-dev] Chart the Belt - Star Map Navigation Targets, Asteroid Fields and Trails

**Type:** MINOR - the orbital map grows its three missing pieces: click any contact to set a persistent navigation target, the system's asteroid shell draws as a rocky region instead of empty space, and orbiting bodies trail their paths behind them. No behaviour, save, recipe or setup changes.

**GitHub title:** `[12.19.0-dev] Chart the belt - star map navigation targets, asteroid fields and trails`

#### Click a contact, keep it

Clicking a contact on the map canvas now sets it as the navigation target: a gold corner-tick reticle marks it on the chart, a `NAV TARGET` line names it in the header, and its sidebar row carries a `[NAV]` tag. When the equipped device allows focus switching the map also follows the new target. Clicking empty space clears the target and hands pan control back. The target persists across sessions and always resolves live - bodies from the cosmic registry, craft from their grid identity - so it tracks a moving ship even with the map closed. The new `NavigationTarget` service is UI-free on purpose: item 8 (route recorder and autopilot) will follow it without touching the map.

#### The belt on the chart

The tracking snapshot aggregates the system's scattered rocks into one `Asteroid Belt` contact at the shell centroid, listed in the BODIES section like any other body. The map draws it as a region rather than a disc: a boundary ring at the shell radius plus up to 220 stride-sampled rocks as dust motes, so the shell reads as a belt at any zoom without swallowing the inner system. The ring is clickable by its edge - its centre is usually the sun, which keeps its own grab radius. Paint, labels and click hit-testing now share one view-frame helper, so what you click is always what you see.

#### Trails without history

Every contact with a drawn orbit ellipse now trails a fading arc behind its current position - three chunks along the last stretch of its own path, brightest at the body. The arcs are analytic, read straight off the solved apoapsis/periapsis elements in the same focus-offset convention as the ellipses, so they lie exactly on the drawn orbit with no position-history buffer to maintain. Trails share the orbit-path device gate, like the ellipses themselves.

#### Manual steps in Unity

1. Apply the patch and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Equip an Orbital Map and press `M`: zoom out and find the belt ring with its dust motes and the `Asteroid Belt` row under BODIES.
4. Click a moon: the gold reticle, the `NAV TARGET` header line and the `[NAV]` row tag appear, and the map follows it.
5. Click empty space: target and follow clear, and drag-panning works freely again.
6. Confirm orbiting moons and craft trail fading arcs along their ellipses.
7. Close the game, reopen, press `M`: the navigation target is restored.

### [12.18.0-dev] Silence Between Stars - Vacuum Audio Ducking

**Type:** MINOR - new global vacuum ducking: exterior sound now fades with the air at the listener, so spacewalks go silent while sealed cockpits keep hearing the ship. UI, music and pickup cues bypass it entirely. No behaviour, save, recipe or setup changes.

**GitHub title:** `[12.18.0-dev] Silence between stars - vacuum audio ducking`

#### One question, one answer

The new `VacuumAudio` asks the same question every frame - how much exterior sound survives at the listener - and every exterior emitter multiplies by the answer. Sampling is physical: open-sky pressure from the atmosphere model, ship-room pressure from the pressure system, station-room fill from the room solver, best air wins. Full sound at 0.3 atm and above, linear fade below, hard silence at zero. The value eases toward its target (fast out, slower back), so flying through an airlock threshold never pops.

#### What goes quiet, what stays

Machine loops, thruster roar, wind, wildlife and cave beds, the full weather mix including thunder, positional one-shots (horns, steam, placement) and tool hits all scale with the air. A pilot in a pressurised cockpit hears everything; an engineer on EVA hears nothing but the suit-adjacent layer: UI clicks, toasts, pickups and music are untouched by design. Thin high-altitude air lands in between - quieter, not gone.

#### Manual steps in Unity

1. Apply the patch and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Fly a ship out of atmosphere: machine and thruster loops fade to nothing; UI clicks and music stay.
4. Sit in a sealed, pressurised cockpit in vacuum: the ship reads full again.
5. Step back into air: the soundscape swells back over about two seconds.
6. Confirm weather, ambience and one-shots behave as before on the surface.

### [12.17.4-dev] Quiet Console - Craft Diagnostics Removed

**Type:** PATCH - removes the temporary craft-failure diagnostics now the floats are proven working: click/refusal traces, float mirrors, the render probe and the now-unused destination state helper. No behaviour, save, recipe or setup changes.

**GitHub title:** `[12.17.4-dev] Quiet console - craft diagnostics removed`

#### What left the code

The `[Craft]` click and refusal traces in both recipe browsers, the `[CraftFeedback]` mirror and position logs plus the half-second render probe in `FloatAt`, and `Crafter.DescribeSpace`, which only existed to feed the refusal line. Player-facing feedback is untouched: failed crafts still float the reason at the button and post a toast for later. The genuine error guards stay - a missing float layer still warns once, and craft exceptions still log with a full stack.

#### Manual steps in Unity

1. Apply the patch and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Click CRAFT on a blocked recipe: float and toast appear, console stays silent.
4. Craft successfully: no new console noise, queues and batches behave as before.

### [12.17.3-dev] Floats Rise By Hand - Manual Animation And Prefixed Mass

**Type:** PATCH - the render probe caught the invisible float red-handed (opacity already 0.00 half a second after spawn): style transitions snap on freshly-created elements, so the rise-and-fade is now driven per-frame by hand. Mass ratios revert to proper SI prefixes on both sides per feedback. No behaviour, save, recipe or setup changes.

**GitHub title:** `[12.17.3-dev] Floats rise by hand - manual animation and prefixed mass`

#### Transitions out, ticks in

The probe line said it all: attached panel, sane bounds, Flex display, Visible visibility - and opacity 0.00 at +500ms into a 2-second fade. UITK style transitions never animate on an element that has not painted its start values yet; they jump straight to the target. `FloatAt` now drives the animation itself with 16ms scheduler ticks over unscaled time: ease-out rise across 46px, linear fade across 2 seconds, then the ticker pauses and the label removes itself. Same look as designed, none of the transition system. The probe stays one more round to confirm opacity reads ~0.75 at +500ms.

#### Prefixes stay prefixed

The single-unit ratio experiment is reverted: current/max mass pairs print each side in its own correct SI unit again ("222.24 t / 450 kg", kilotonnes when greater), on the inventory CARGO LOAD card, the grid cargo readout and the grid terminal list. The overweight float and the console state dump follow the same rule, so every mass the player reads carries its prefix.

#### Manual steps in Unity

1. Apply the patch and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Click CRAFT on an overweight-blocked recipe: the reason visibly floats up from the button and fades over 2 seconds.
4. Confirm the PROBE line now reads opacity ~0.75 and the overweight text carries prefixes.
5. Shed load and confirm crafting resumes with no new console noise.

### [12.17.2-dev] One Unit Per Ratio - Cargo Load And Float Probe

**Type:** PATCH - the cargo load card mixed units ("222 t / 450 kg"), hiding a 500x overload at a glance; all three current/max mass ratios now share one unit. Plus a render probe on the floating text, which logs show is spawned correctly but never draws. No behaviour, save, recipe or setup changes.

**GitHub title:** `[12.17.2-dev] One unit per ratio - cargo load and float probe`

#### The overload you could not see

The 12.17.1 console ladder proved the craft refusals were legitimate: the test inventory carried 222,242 kg against a 450 kg cap. But the CARGO LOAD card formatted each side independently, printing "222.24 t / 450 kg" - two units side by side, trivially misread as fine. The card now uses the existing `MassFormat.FormatRatio`, which picks one unit from the capacity: "222242 / 450 kg". Unmissable. The same mixed-unit pattern in the grid cargo readout and the grid master terminal inventory list is fixed in the same way.

#### Hunting the invisible float

The logs also proved the float pipeline runs cleanly: callback fires, reason computed, label spawned on the TopLayer at the button's position, no exceptions - yet nothing draws, while toasts on the HUD layer render fine. A scheduled probe now logs the label's live render state half a second after spawn (attached panel, world bounds, resolved opacity, display, visibility), which separates a detached tree, an off-screen position and a transparency fault in one retest.

#### Manual steps in Unity

1. Apply the patch and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Open the inventory: CARGO LOAD shows both numbers in one unit.
4. Click CRAFT on an overweight-blocked recipe and copy the `[CraftFeedback] PROBE` console line.
5. Shed load below the cap and confirm crafting resumes with no new console noise.

### [12.17.1-dev] Feedback You Cannot Miss - Console Mirror And Toasts Return

**Type:** PATCH - the 12.17 floats never appeared in-game, so failed crafts now report three ways at once: a console log (the guaranteed channel), the floating text at the button, and the returning toast (visible after closing the panels). No behaviour, save, recipe or setup changes.

**GitHub title:** `[12.17.1-dev] Feedback you cannot miss - console mirror and toasts return`

#### Three channels, one reason

Every failed craft in both recipe browsers now logs `[Craft] Refused ...` with the exact reason plus a destination state dump (space verdict, live weight, accept-gate flag), floats the reason at the clicked CRAFT button, and posts a toast for after the panels close. Clicking CRAFT also logs `[Craft] Click ...`, which proves whether the button callback runs at all. Floats mirror to `[CraftFeedback]` in the console with their layer and position, and a throwing float can no longer swallow its own caller.

#### What this diagnoses

One retest now separates every suspect: no `[Craft] Click` means the button never fires; a `Refused` line names the exact gate (ingredients, space, weight, filter); console lines without visuals mean the feedback layers misbehave. The bench chrome was verified click-safe - every frame, divider and lamp element ignores picking.

#### Manual steps in Unity

1. Apply the patch and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Click CRAFT on a failing recipe, then copy the `[Craft]` / `[CraftFeedback]` console lines and report them plus whether the float and the toast appeared.
4. Confirm successful crafts still craft silently with no new console lines beyond the click entry.

### [12.17.0-dev] Craft Failures Float Up - Floating Feedback Text

**Type:** MINOR - new floating combat-text feedback: failed crafts now say why right at the clicked CRAFT button, naming the exact refusal (missing items, full inventory, overweight with live kg). No behaviour, save, recipe or setup changes.

**GitHub title:** `[12.17.0-dev] Craft failures float up - floating feedback text`

#### Why floats instead of toasts

The 12.16.1 failure toasts worked, but they render on the HUD layer - behind the open inventory - so the player only saw them after closing the panels. The new `BuildFeedbackHud.FloatAt` renders on the topmost tooltip layer instead: a dark pill with the reason appears at the button, rises and fades over 2 seconds, then removes itself. It never blocks input.

#### The game names the exact gate

A shared `Crafter.CraftFailReason` mirrors the craft refusal checks so both recipe browsers explain themselves identically: the station right-pane browser (bench, assembler and friends) and the inventory centre browser. "Missing ingredients" in red, "Inventory full", "Overweight 449/450 kg", containment and carry gates in amber. A partial batch that crafts some and then fails still refreshes normally; the float only appears when nothing was crafted. Exceptions still log to the console and float "Craft error".

#### Manual steps in Unity

1. Apply the patch and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Fill the inventory (or hit the weight cap) and click CRAFT on a craftable recipe: the reason floats up from the button and fades.
4. Try the same in the inventory centre browser: identical float.
5. Make space and craft again: crafts as before, no float.
6. Confirm queueing, progress, cancel and batch amounts all behave as before.

### [12.16.1-dev] Cryobed Docks Right And Craft Buttons Speak Up - Bench Fix Round

**Type:** PATCH - bugfix round for four bench-adjacent reports: the cryobed panel opened centre-screen, its oxygen bar looked plain, the filter search caret sat high while empty, and failed crafts died silently. No behaviour, save, recipe or setup changes.

**GitHub title:** `[12.16.1-dev] Cryobed docks right and craft buttons speak up - bench fix round`

#### Cryobed joins the side dock

The cryobed control panel now docks on the right like every other machine panel - full height, right edge, transparent backdrop - instead of floating centre-screen. It stays modal (Close button or Pause to dismiss), and the starship rails and hull divider from 12.16 carry over unchanged.

#### A tank worthy of the name

The oxygen readout is now a proper pressure vessel: valve cap and neck stacked above the body, cylinder shading down the flanks and level ticks at 25 / 50 / 75 %. The live fill, the empty/low/ok colour cues and the centred percentage all behave exactly as before.

#### Caret fix and honest craft buttons

The filter search fields in all three dialogs stretch their inner text element to the full field height, so the caret stays vertically centred while the field is empty instead of riding high. And the CRAFT button now reports failure: "Missing ingredients" when items are short, "No room for output" when the inventory is full or overweight, and "Craft error" (plus a console log entry) if anything throws. The success path is untouched.

#### Manual steps in Unity

1. Apply the patch and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Press E on a cryobed: panel docks right, oxygen tank has valve, shading and ticks.
4. Open each filter dialog: the empty search caret sits centred.
5. Click CRAFT on a bench recipe: crafts as before; if one ever fails, a toast now says why - report the toast text.

### [12.16.0-dev] Dialogs And Decision Cards - Chrome For The Floating UI

**Type:** MINOR - presentation-only chrome for the floating dialogs and decision cards: item filter dialogs, the two void-confirmation modals, the item-ports overlay, the cryobed config dialog, the grid screen config dialog and the crafting bench card. No behaviour, save, recipe or setup changes.

**GitHub title:** `[12.16.0-dev] Dialogs and decision cards - chrome for the floating UI`

#### Every floating card framed

The machine panels are all chromed, so this round dresses the dialogs that float above them. The three item filter dialogs, the item-ports overlay and both void confirmations take industrial chrome - hazard divider, girder frame - with the drop-limit and tank-void modals keeping their caution semantics. The cryobed dialog takes the starship hull treatment with green rails when staffed and amber when calling for a colonist; the grid screen config takes high-tech scanlines in display cyan.

#### The bench reads its queue

The crafting bench card is the one decision card with a running state, so it earns status lamps: green while the queue has work, amber at rest. The pure dialogs and modals stay lampless - nothing runs inside them, so there is nothing to report. Every value, slot, filter, toggle and button is preserved - only the presentation changed.

#### Manual steps in Unity

1. Apply the patch and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Open each filter dialog on a filtered face: hazard divider and girder frame on all three.
4. Press E on a cryobed and a grid screen: hull rails and scanline divider respectively.
5. Open the item-ports overlay, both void modals and the crafting bench: framed cards, and green bench lamps while a craft is queued.
6. Confirm filters, ports, voids, naming, screen settings and crafting all behave as before.

### [12.15.0-dev] Holo Racks And Data Brackets - Storage Goes High-Tech

**Type:** MINOR - presentation-only restyle of all eleven storage network panels onto the high-tech instruments. No behaviour, save, recipe or setup changes.

**GitHub title:** `[12.15.0-dev] Holo racks and data brackets - storage goes high-tech`

#### One network, one family

The storage network is the most advanced system in the game, so all eleven panels take high-tech chrome as a single family: storage, pattern and crafting terminals, importer, exporter, disk manipulator, NAS block, server rack, drawer, drawer controller and item display. Corner-bracket holo frames with status-reactive accents, scanline dividers - green brackets on a live rack, red on a dead one, purple on patterns, amber on exports. Every value, slot, filter, queue, search field and hint is preserved - only the instruments changed.

#### Framed on every path

The three terminals each carry an offline branch for a missing or dead rack, and each branch is framed on its own path - no fallback card ships bare. The server's divider sits below its power bar exactly where it always did; the drawer's teal accent follows its filled state.

#### Manual steps in Unity

1. Apply the patch and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Press E on each storage block: holo brackets and scanline divider on every card.
4. Check the accents read true: green when linked and online, red on NO RACK, purple patterns, amber exports, teal on a filled drawer.
5. Confirm search, sorting, filters, craft queueing, disk transfers and the offline terminal branches all behave as before.

### [12.14.0-dev] Engine Room And Helm - Maritime Chromed

**Type:** MINOR - presentation-only restyle of all fourteen maritime block panels, plus a scroller fix port. No behaviour, save, recipe or setup changes.

**GitHub title:** `[12.14.0-dev] Engine room and helm - maritime chromed`

#### The engine room goes industrial

Thirteen panels take the steel: engine, generator, gearbox, bilge pump, marine water pump, both propellers, turbocharger, waterwheel, drive shaft, shaft housing, exhaust pipe and hull. Hazard dividers, girder frames, and lamps wired to each block's own status ladder - critical heat, overstress, choke and missing exhaust read red; running, spinning, pumping and venting read green. The engine's tier-tinted divider retires with the swap; tier still shows in the block name and the coolant section it gates.

#### The helm goes starship

The helm is a nav console, not machinery, so it docks with the starship family: hull divider and status-reactive rails, matching the pilot and locator. Manned reads green, unmanned dim.

#### Fix: maritime scroller keeps frames on the panel

`MaritimeBlockUI.MakeScrollable` ports the 12.9 `ThemeFrame` guard, so the engine, generator and helm frames stay anchored while their content scrolls. Same four-line rule, same behaviour everywhere else.

#### Manual steps in Unity

1. Apply the patch and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Press E on each maritime block: girders or bulkheads, themed divider, and lamps on the thirteen machines.
4. Check the lamps read true: red on critical heat, overstress shutdown, choke, missing exhaust or oxygen, disconnected turbo, dry seal and soaked hull; green when running, spinning, pumping or venting; amber when idle.
5. Scroll the tall engine, generator and helm cards fully: frames stay pinned, content slides beneath.

### [12.13.0-dev] Sparks And Star Charts - World Machines And Nav Consoles Chromed

**Type:** MINOR - presentation-only restyle of eighteen side-docked machine panels onto the industrial and starship instruments. No behaviour, save, recipe or setup changes.

**GitHub title:** `[12.13.0-dev] Sparks and star charts - world machines and nav consoles chromed`

#### The world machines go industrial

Thirteen panels take the steel: the shared processor shell (oil refinery, chemical plant, distillation plant, catalytic cracker through one builder), wind turbine, voltage station, drone port, fluid pump, armor station, and the seven right-side cards in `GameUIController` (world battery, turret defences, container, solid-fuel furnace, coal generator, electric furnace, powerstation). Hazard dividers, girder frames, and lamps that read the block's own state - burning, generating, smelting, in flight, charging. Panels whose status already shows in bespoke chrome (voltage load bar, turret stock strip, passive chests) take divider and frame only.

#### The nav consoles go starship

The planetary observatory, auto-run pilot, connector pad, route recorder and refuel pad dock as starship telemetry with hull dividers and status-reactive rails, matching the grid-side season monitor. Every early-return branch (missing block, local-only routes, unavailable machines) is framed on its own path so no fallback card ships bare.

#### Deliberately deferred

Maritime (fourteen panels) and storage (eleven panels) are each a full round on their own and follow next. The LCD centre screens (production stats, recipe browser, ship control centre), the drop/void confirm modals, the crafting-bench card and the item-ports overlay stay on their own chrome - they are player screens, not machine cards. The two unbound UIDocument panels (`SharedMachinePanel`, `RecipeSelectionPanel`) are unused legacy and untouched.

#### Manual steps in Unity

1. Apply the patch and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Press E on each machine: girders or bulkheads, themed divider, and lamps where fitted.
4. Check the lamps read true: red when unpowered, stalled or switched off; green when burning, generating, smelting, flying or charging; amber when idle.
5. Confirm recipe books, defence toggles and sliders, ports overlays, the unavailable-machine fallbacks and the route recorder's local-only branch all behave as before.

### [12.12.0-dev] Chrome On Every Console - The Grid Set Is Complete

**Type:** MINOR - presentation-only restyle of the last ten grid block panels onto the four existing instrument families. No behaviour, save, recipe or setup changes.

**GitHub title:** `[12.12.0-dev] Chrome on every console - the grid set is complete`

#### Four families, ten consoles, zero new code

No new theme this round: every remaining `GridBlockUI` panel takes chrome from the family that matches its block. The battery joins the industrial line as power-plant equipment with a hazard divider and lamps; the vault and harvester keep their live singularity visuals under high-tech holo brackets and scanline dividers; the locator, both satellite consoles and the season monitor dock as starship telemetry with hull dividers and status-reactive rails; the wagon coupler gets brass rivets and finishes the rail set. All static styling, safe under the machine cadence rebuilds.

#### Deliberately preserved

The LED strip and spotlight keep their colour-reactive dividers - the divider is the only place the configured colour shows, so it stays, with industrial lamps and girders around it. The vault and harvester black-hole visuals, pressure gauge, efficiency bar, warning banners and live animation loops are untouched; only the outer chrome changed. Every value, slot, mode button, slider, colour key and hint is preserved.

#### Manual steps in Unity

1. Apply the patch and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Press E on each console: frames, dividers and (where fitted) lamps on every card.
4. Check the lamps read true: battery green while charging or discharging, amber idle; lights red when off or unpowered, green when lit.
5. Confirm the offline satellite branch still shows its requirements card framed, and LED/light colour keys still recolour the divider live.

### [12.11.0-dev] Girders Across The Grid - Ship Modules Get Structured Steel

**Type:** MINOR - presentation-only restyle of sixteen grid block panels onto the existing industrial instruments. No behaviour, save, recipe or setup changes.

**GitHub title:** `[12.11.0-dev] Girders across the grid - ship modules get structured steel`

#### The same steel, a second hangar

No new theme this round: the sixteen grid modules move onto the `IndustrialTheme` instruments from 12.10 - girder frame with bolt dots, hazard-block divider, tri-lamp status stack. Same static styling, safe under the machine cadence rebuilds, and the `ThemeFrame` tags keep the girders on the panel when the card scrolls.

#### The grid modules

Liquid tank, gas tank, H2/O2 generator, cargo container, ship refinery and chemical plant (through the shared processor builder), electric furnace, drill, landing gear, wheel, sliding door, grid biofarm, exhaust scrubber, gas vent, flare stack, air vent and the generic fallback card all move onto the new chrome. Every value, slot, recipe list, slider, toggle and hint is preserved - only the instruments changed. The bespoke consoles (battery, vault, harvester, locator, satellites, season monitor, LED and light, rail coupler) stay on their own instruments.

#### Manual steps in Unity

1. Apply the patch and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Press E on each module: girders, hazard divider and lamps on every card.
4. Check the lamps read true: red when off, unpowered or starved; green when running, working, open, locked, grounded or holding stock; amber when idle.
5. Confirm drain, gas type select and fill dock, recipe select, gear and wheel toggles, door sliders, and the ship terminal button all behave as before.

### [12.10.0-dev] Girders And Warning Lamps - Industrial Machines Get Structured Steel

**Type:** MINOR - presentation-only restyle of nine industrial machine panels covering ten machines (crusher and assembler share a builder). No behaviour, save, recipe or setup changes.

**GitHub title:** `[12.10.0-dev] Girders and warning lamps - industrial machines get structured steel`

#### The instrument library grows a fourth family

`IndustrialTheme` is the workhorse counterpart to the brass, holo and naval instruments: a steel girder frame of beams and posts with bolt dots along the beams, a hazard divider with a caution-block cluster, and a tri-lamp status stack - red fault, amber idle, green running - like a control cabinet. Everything is static styling with no scheduled anims, safe under the machine cadence rebuilds.

#### The workhorses

Crusher, assembler (all tiers through the shared builder), funnel, splitter, quarry, jack pump, biofarm, flare stack, gas tank and steam turbine all move onto the new chrome. Every value, slot, recipe list, filter, upgrade and hint is preserved - only the instruments changed. The turbine joins the industrial family as power-plant equipment.

#### Manual steps in Unity

1. Apply the patch and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Press E on each workhorse: girders, hazard divider and lamps on every card.
4. Check the lamps read true: red when unpowered, blocked, dry or shut; green when running; amber when idle.
5. Confirm splitter Mk3 filters, quarry upgrades and ports, flare fuel select and recovery, and gas type select and fill dock all behave as before.

### [12.9.0-dev] Rivets On The Frame - Rail Polish And A Scroller Fix

**Type:** MINOR - presentation-only: one frame-anchoring fix plus the rail-family polish. No behaviour, save, recipe or setup changes.

**GitHub title:** `[12.9.0-dev] Rivets on the frame - rail polish and a scroller fix`

#### Fix: theme frames stay on the panel

`MakeScrollable` moved every panel child into the scroller - including the starship frame rails, which then scrolled with the content and painted over section titles and slot cards (the ship reactor in the screenshot; the docking port was never wrapped, which is why it looked clean). Frame elements are now tagged and left on the panel, so the chrome stays put while the content scrolls inside it.

#### The steampunk frame, and the last two brass panels

`SteampunkTheme.Frame` completes the brass chrome: a riveted inner border with a rivet on each corner, applied to all five rail consoles. The two panels that were still half-dressed join them fully: the water tower gets a riveted divider, a TANK dial in place of the tank bar, and the frame; the bogie gets a riveted divider, seven brass selector keys (drive, reverse, snap, auto-snap, couple, uncouple) and the frame. Every value, slot and callback is unchanged.

#### Manual steps in Unity

1. Apply the patch and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Press E on the ship reactor, gatling, ship engine, ore detector and cryobed: rails stay fixed at the panel edges while the content scrolls, nothing paints over titles or slots.
4. Press E on the tower (dial, rivets, frame), the bogie (brass keys, frame) and the station, switch, engine, schedule and display consoles (riveted frames).
5. Confirm every button and slot still works.

### [12.8.0-dev] Bulkheads And Vector Needles - Ship Systems Go Sci-Fi

**Type:** MINOR - presentation-only restyle of eight ship-system panels plus a dial-centering fix. No behaviour, save, recipe or setup changes.

**GitHub title:** `[12.8.0-dev] Bulkheads and vector needles - ship systems go sci-fi`

#### The instrument library grows a third family

`StarshipTheme` is the deep-space naval counterpart to the steampunk and high-tech instruments: a bulkhead frame of side-rails with docking ticks top and bottom, a hull divider with a centred diamond, and vector meters that read fuel, buffers and efficiency as a bright needle position on a track with a ghost trail. The frame accent follows block status like the holo frame does. Everything is static styling with no scheduled anims, safe under the machine cadence rebuilds.

#### The ship systems

Gatling weapon, ship reactor, solar panel, ship hydrogen engine, docking port, ore detector, beacon and cryobed all move onto the new chrome; the reactor fuel, solar efficiency and H2 buffer gauges become vector meters. Every value, slot, button, list and hint is preserved - only the instruments changed. The battery, containment vault, harvester, locator and satellites are already bespoke animated consoles and stay untouched, as do the industrial ship modules and the rail family.

#### Fix: steampunk dials sit centred

The 3px brass bezel is a real USS border, so dial innards anchored to the padding box sat 3px off the bezel centre with ticks straying toward the cream edge. The face, tick ring, needle and hub now compensate - needles and ticks sit fully inside their circles.

#### Manual steps in Unity

1. Apply the patch and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Press E on the gatling, ship reactor, solar, ship engine, docking port, ore detector, beacon and cryobed: rails, diamond divider and vector needles on each.
4. Footplate: all three dials centred, needles and ticks fully inside the cream.
5. Confirm every slot and button still works and every value still updates live.

### [12.7.0-dev] Targeting Computers - Advanced Machines Go High-Tech

**Type:** MINOR - presentation-only restyle of six advanced machine panels. No behaviour, save, recipe or setup changes.

**GitHub title:** `[12.7.0-dev] Targeting computers - advanced machines go high-tech`

#### The instrument library grows a second family

`HighTechTheme` is the advanced-machine counterpart to the steampunk instruments: a corner-bracket holo frame that pins targeting-computer brackets to the panel corners, a scanline divider with one bright segment, segmented cell meters that read fuel, rods and buffers as discrete glowing cells instead of a continuous bar, and a hero numeric readout for the one number the operator watches. The frame accent follows machine status - green running, red fault, dim idle - so an overheating reactor wears a red frame. Everything is static styling with no scheduled anims, safe under the machine cadence rebuilds.

#### The nuclear and hydrogen lines

Reactor core, portable reactor, enrichment centrifuge, waste reprocessor, electrolyser and hydrogen engine all move onto the new chrome: hero readouts for core temperature and power output, segmented cells for control rods, fuel, enrichment, reprocessing, electrolysis and the H2/O2 buffers. Every value, slot, tank and hint is preserved - only the instruments changed. The steam turbine and water tower stay as they are for the rail-polish round, and the quarry stays industrial.

#### Manual steps in Unity

1. Apply the patch and recompile.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Press E on the reactor core, portable reactor, centrifuge, reprocessor, electrolyser and hydrogen engine: each wears corner brackets, a scanline and cell meters.
4. Run the reactor hot: past safe max the temp readout, its cells and the frame go red.
5. Confirm every slot still takes items and every value still updates live.

### [12.6.0-dev] Brass Needles And A Firebox - The Railway Moves To The Side Dock

**Type:** MINOR - new firebox slot (additive save through the container snapshot) and five consoles migrated to docked machine cards. No breaking changes.

**GitHub title:** `[12.6.0-dev] Brass needles and a firebox - the railway moves to the side dock`

#### More than a colour: the steampunk instrument library

`SteampunkTheme` is the railway's own instrument maker. Its analog dials are real gauges, not bars: a machined brass bezel on an iron body, a cream face, an eleven-mark tick ring over the 270-degree sweep with longer ends and middle, a red needle on a brass hub - and a redline past which the ticks themselves turn red, because a boiler pressure gauge redlines. Riveted dividers and brass selector keys complete the chrome. Everything is static styling with no scheduled anims, so the 4 Hz machine cadence rebuilds live panels around it safely.

#### The railway leaves the center modal

Station, switch, footplate, schedule and display all move into `RailPanels` on the right dock, opened through `OpenMachine` like every other machine - every feature carried over: station naming, roles and hold; switch routing; the whistle and the fire toggle; the full stop-list editor with its station picker and wait conditions; display kind, source and custom text. The bogie console merges into the docked truck card (retitled Bogie, brass accented) and brings its auto-snap toggle, service note and live speed with it. `RailConfigHud`, `TrainScheduleHud` and `DisplayConfigHud` are deleted. A text-field focus guard (`SteampunkTheme.IsTextInputFocused`) joins the refresh and hotkey guards, so a live rebuild never eats a station name or a dwell time mid-typing.

#### The firebox

The steam engine gains its coal slot: a 1-slot firebox the stoker burns from first, falling back to shoveling out of any cargo container aboard when it runs dry - LOAD-station coaling keeps working exactly as before. The slot persists through the standard container snapshot, so fuel survives a reload. The footplate shows it beside three needles: redlined boiler pressure, boiler water in litres, and flywheel RPM.

#### Manual steps in Unity

1. Apply the patch and recompile. Three HUD scripts are deleted - confirm the compile is clean.
2. No setup re-run is needed: no new blocks, items or recipes.
3. Press E on a station, a switch, a bogie, an engine, a schedule block and a display: each docks right with steampunk chrome.
4. Footplate: watch the three needles move; put coal in the firebox slot and confirm it burns before tender coal; reload and confirm the slot kept its fuel.
5. Station: rename it mid-game (typing must not be interrupted), set roles, open the hold, press E to come back.
6. Schedule: add, reorder and delete stops; display: switch kind, source and custom text.

### [12.5.0-dev] The Tower On The Hill - A Grand Steel Water Tower

**Type:** MINOR - the water tower is rebuilt as a landmark and gains its own console. No save changes; stored levels restore against the bigger tank.

**GitHub title:** `[12.5.0-dev] The tower on the hill - a grand steel water tower`

#### A tower worth looking at

The water tower is no longer a barrel on sticks: it is a classic six-legged steel water tower, 10 m to the brass finial. A galvanized tank with iron seam bands and rivets, a rust-red conical roof, a railed balcony walkway with a boarding gap, ladders from the ground to the balcony and up the tank, a central riser pipe with a valve house, and a stayed spout arm over the platform side. The legs lean inward with girt rings and X-bracing, and only structural parts keep colliders - rivets, rungs and rails are decor, so the tower costs no more physics than before. Existing towers rebuild into the grand tower when step 96 is re-run.

#### A tank worth filling

Capacity grows from 4 000 L to 12 000 L, with fill rates to match: 24 L/s from a water network, 6 L/s seeping from open water (now a real `seepRate` field instead of a hardcoded number). Towers still on untouched 12.4.0 tuning migrate to the new values automatically; anything already tuned is left alone. Stored water restores as before and clamps against the bigger tank, so no save work was needed.

#### The level gauge

E on the tower opens its panel on the right dock, beside the inventory, like every other machine - no center modal. A live water gauge with litres aboard, capacity, level percent and supply source, kept honest by the 4 Hz machine refresh cadence, plus a status pill that reads FULL, FILLING FROM NETWORK, SEEPING FROM OPEN WATER or ISOLATED straight from the tower's new supply tracking. The panel wears steampunk brass, because the UI matches the block: rail and steam hardware gets brass, and the fleet-wide UI round will dress every other family to match its own hardware (`LcdHudTheme` gains the shared brass palette it draws from).

#### Also in this round

`GameVersion` was stuck at 9.55.0 while the project shipped 12.4.0 - the console banner, menu footer and saves now report 12.5.0-dev from the same constant again.

#### Manual steps in Unity

1. Recompile; run **Tools -> Voxel Engine -> Voxel Engine Setup -> 96. Build the Steam Railway**.
2. In a save: place or find a Water Tower and check the new silhouette against the old barrel.
3. Press E on the tower: the level gauge shows fill, capacity and supply state live.
4. Stand it beside a water network or a pond and watch the status pill change as it drinks.
5. Berth a thirsty steam engine within reach of the spout and confirm the boiler still fills.

### [12.4.0-dev] Steam Takes The Rails - Piston Engines, Water Towers And A Whistle

**Type:** MINOR - new traction source and two blocks. Additive save fields only.

**GitHub title:** `[12.4.0-dev] Steam takes the rails - piston engines, water towers and a whistle`

#### Rotational power, and nothing else

The **Steam Engine** is a machine, not a locomotive: a vertical boiler with a chimney, a horizontal steam cylinder, a crosshead on slide bars, a connecting rod down to a crank pin and a flywheel across the frames. Its only product is ROTATION - `CurrentRPM` at the flywheel, zero banked or starved, idle at first steam, full speed at full pressure. It generates no electricity and grants no traction flag: the bogie's mechanical drive takes the flywheel's turn when the grid has no electric power (the gate walks the coupled consist at 2 Hz and asks for a shaft turning above 30 RPM), and the brass screens' rotational tap reads the same number - a steam train's departure board runs off its own engine's shaft.

#### The piston gear is solved, not waved at

The flywheel spins, the crank pin circles with it, and the crosshead rides its bars at the exact slider-crank position (z = pin.z + sqrt(L^2 - dy^2)) with the connecting rod laid between pin and crosshead at its true length and angle. At speed it reads as an engine working; at rest it holds its last position like one. The chimney breathes WHITE steam smoke - white, not soot: a coal fire with draft and water in the boiler breathes steam, and steam is what this block sells - plus `Sfx.SteamChuff` twice per revolution under way and `Sfx.SteamWhistle` on the whistle cord.

#### The tender is the train

The firebox carries no private fuel slot: it shovels coal - or wood, at half the patience - out of any cargo container on the grid, like a real tender coaling from the wagon behind it. Coaling is therefore a cargo operation: a LOAD station with a coal filter coals an engine through the berth service pass from 12.3.0, and fuel persists for free with the containers. Water comes from a `GridLiquidTank` aboard (a tank wagon) while running, and from a **Water Tower** platform-side while berthed. Fire without water loses pressure; water without fire loses it slower; neither turns the flywheel.

#### The footplate and the tower

E on an engine opens the footplate in `RailConfigHud`: boiler pressure and water as bar readouts, live flywheel RPM, LIGHT/BANK THE FIRE, and WHISTLE. Water and fire survive a reload (additive fields); pressure deliberately does not - a banked fire restarting hot between sessions would be a free head of steam. The **Water Tower** is a tank on legs with a standpipe: it fills itself from a water network it stands beside (same FluidNode source a sprinkler drinks from) or slowly from open water - a tower by a pond seeps full the way real ones were pumped. Its level saves additively.

#### New in the setup window

Step **96. Build the Steam Railway** (needs 95 for brass): authors the Steam Engine grid item and prefab (bed, frames, steam cylinder, crosshead and rod, connecting rod, spoked flywheel with crank pin, brass-banded vertical boiler, chimney), the Water Tower block and prefab, and both recipes - engine steel x24 + brass x6 + wire x4 at the assembler, tower steel x10 + brass x2 at the bench. Non-destructive, safe to re-run.

#### Manual steps in Unity

1. Recompile; run **Tools -> Voxel Engine -> Voxel Engine Setup -> 96. Build the Steam Railway**.
2. In a save: craft a Steam Engine, build it onto a train grid, put coal in any container aboard, and open the footplate (E) to watch pressure climb and the flywheel turn the piston gear.
3. Cut the grid's power (or build the train with no generator at all) and drive: the flywheel's rotation pulls the train mechanically.
4. Craft a Water Tower beside a water network or a pond, and berth a thirsty engine within reach of the standpipe.
5. Pull the whistle cord. You will know when it works.

### [12.3.0-dev] The Freight Actually Rides - Stations Service Berthed Trains

**Type:** MINOR - closes the cargo loop the schedule system was built around. No new assets, no save changes.

**GitHub title:** `[12.3.0-dev] The freight actually rides - stations service berthed trains`

#### The hole: `ServiceTrain` had no caller

`RailStation.ServiceTrain` - the transfer that moves items between a station hold and a docked train, honouring the station filter, putting back anything the destination refuses - existed since the station rework and was called from nowhere. Trains arrived, waited their condition, and left without a single item moving. The hold waits in a schedule were watching a hold that nothing but pipes ever touched, and a "LOAD until hold empty" stop released the moment a factory drained the hold, whether or not the train had taken anything.

#### The fix: a berth service pass on the bogie

`GridRailBogie` now runs a 4 Hz service pass while the train is effectively stopped (under 0.05 m/s) on rails: it finds the station whose platform it stands at (`RailStation.Nearest`, new static, same 4 m service radius), and if that station works (role not Passing) it calls `ServiceTrain` for every cargo store on the grid - every block implementing `IGridItemStore`, rescanned every 5 s so adding a wagon mid-route joins the transfer. Hand-driven trains berth and trade exactly like scheduled ones; a train braked by a signal in front of a station does not, because it is not stopped at the platform radius... it is stopped wherever the section ends, and if that is inside the radius, trading is what a real train would call a courtesy stop.

The pass reports itself: `ServicingStation` and a one-line `ServiceNote` ("Loading at Ore Head - 34 items aboard", "Berthed at Mill - train empty or station hold full"), shown in the bogie console and appended to the schedule block's waiting label, so a stalled transfer says why instead of silently watching a clock.

#### Train-side wait conditions

Two waits appended to `ScheduleWait` (saves store the int, so nothing reorders): **TRAIN EMPTY** - leave when every container aboard is empty, the natural release for an unload stop - and **TRAIN FULL** - leave when every slot aboard carries something, the natural release for a load stop. A train with no containers reads as empty and never as full. The schedule console offers both buttons and both descriptions. With these, a service pattern can say what it actually means: load until the train is full, unload until it is empty, and the hold waits stay for players who think in station terms.

#### Manual steps in Unity

None - code only, no setup step, no new assets. Recompile and in a save: give a station the LOAD role with items in its hold, put a cargo container on a train, add a stop with TRAIN FULL, and watch the note count items aboard until the train leaves full.

### [12.2.0-dev] Brass, Tubes, Cobbles And A Bogie That Actually Snaps

**Type:** MINOR - new material and console, rebuilt visuals, one placement rule tightened. All save fields additive.

**GitHub title:** `[12.2.0-dev] Brass ingots, real nixie tubes, proper ballast stone, and a bogie that snaps`

#### The bogie snaps now - and tells you about it

The old snap put the GRID ORIGIN on the railhead and kept the construct's old rotation, so a truck parked at an angle "snapped" while its wheels stayed in the air and it faced across the line. `TrySnapToTrack` now sets the pose, not a coordinate: the construct rotates to face along the track, then shifts so the truck block itself sits on the railhead, wherever on the hull it was built.

The truck is renamed **Bogie** (item, recipe, prefab block name - step 92 re-runs the rename over old assets), and E on it opens a proper console in `RailConfigHud`: ON/OFF RAILS state, speed, **AUTO-SNAP** toggle (on by default; a parked wagon stays parked when you turn it off), SNAP NOW and LIFT OFF buttons. Auto-snap polls once a second while unrailled, so a train built beside the line, reloaded, or overtaken by the line growing into the yard joins the rails by itself. The policy survives saves (additive field). The old inline attach/detach toast is gone - the state and its control live on one screen now.

#### Brass: the steampunk metal, priced to fit

New **Brass Ingot**: copper x2 + iron x1 in the furnace, one out - between copper and steel, exactly where steel (iron x2) leaves room for it, so nothing upstream changes price. Step 95 authors it and then blends ONE steel-for-brass swap into every rail piece recipe and every display-family recipe (total ingots unchanged; the blend skips recipes that already carry brass, so re-runs never stack it).

#### The nixie readout is tubes now

Not digits painted on a window: four glass envelopes with domed tops standing in brass sockets on a brass base, pins between them, one glowing digit floating inside each tube - the clock on the reference photo. A tube with nothing to show stays dark. Glass is alpha-blended URP lit so the digit reads through its envelope, and updates still flicker the emission.

#### The analog gauge is a gauge now

Square brass backplate with corner bolts, round brass body, cream face, an eleven-mark tick ring over the 240-degree sweep (ends and middle longer), red needle with a counterweight tail on a black hub, and a glass cover. The tick ring and the needle share one angle convention, so the needle actually points at its marks.

#### Ballast is crushed stone, not scattered boxes

The first cobble scatter - 26 loose cubes on a dark slab - read as boxes sprinkled on dirt in the world. The bed now builds a dense jittered GRID of angular stones (full three-axis rotation, three granite greys, shoulders sloping at the rim, tops proud of the slab so sleepers bed INTO stone) as one combined mesh per tone: three draw calls for the whole layer instead of 27 per cell, so a long line costs less than before while looking packed. Step 93 rebuilds the stone layer once on prefabs authored before this (V2 child marker); re-runs leave a good bed alone.

#### Rails are laid, not placed

Hand-placing a rail block produced a dead cell: no links, no junction logic, no ballast, and it blocked the corridor tool while looking like track. `BuildSystem.TryPlace` now refuses any block whose prefab carries a RailTrack and says why: hold the Rail Layer and drag a run.

#### Manual steps in Unity

1. Let the project recompile.
2. Run setup **92** (rename to Bogie), **93** (new stone layer) and **95** (brass ingot + brass blend) from Tools -> Voxel Engine -> Voxel Engine Setup. All three are non-destructive and safe to re-run.
3. In a save: smelt brass (copper x2 + iron x1), check a rail recipe in the crafting list shows its brass ingot, and lay a run - the bed under it is packed crushed stone now.
4. E on a bogie: the console shows rail state, auto-snap, snap now, lift off. Park a train beside track with auto-snap on and walk away - it should be railed when you look back.
5. Try placing a Rail Track block by hand: it should refuse and point you at the Rail Layer.

### [12.1.0-dev] Schedules, Screens And The Sound Of A Station Waking Up

**Type:** MINOR - two new block families plus a track bug fix. All save fields additive; old saves load unchanged.

**GitHub title:** `[12.1.0-dev] Train schedules, steampunk displays and the ballast that went missing`

#### The ballast you could not see (bug fix)

Track laid since the x3 formation could come out with sleepers and rails but no stone bed - the bed item resolved by ID, and when the lookup came back empty the corridor silently skipped the ballast pass and kept laying. Two fixes, belt and braces:

- `PlayerInteractionTool` now self-heals a missing ballast definition at drag time (`railballast` item, `Item_Stone` material) and shows one amber toast if the assets genuinely are not there, instead of charging stone and placing nothing.
- `RailCorridor.Commit` gained a re-bed pass: any committed cell that has track but no bed under it gets one. Re-bedded cells are counted, charged as stone, and named on the commit toast - so laying one new run also heals the old bare stretches it touches.

#### Train Schedule block - services without a driver

A grid block for the train itself. Build it on the same grid as the Rail Truck, press E, and write the service: an ordered list of stations, each stop with the condition that releases the train again - dwell seconds, hold until full, hold until empty, or hold while there is space left. The bogie gained real destination routing for it: `SetDestination` asks `RailNetwork` for a path, the truck walks it cell by cell (re-syncing from the path front if it is nudged off line), and fires `Arrived` - at which point the schedule advances the stop, starts the condition, and dispatches again. Stations are matched by name, so renaming a station in the console re-points every train that calls at it. The service pattern and the current stop survive a save.

#### The display family - split-flap, nixie and analog, all steampunk

One component, five authored housings, because "modular" should mean the hardware is shared and the configuration is yours. Every screen opens a console on E: kind (split-flap rows / glowing nixie digits in a brass cage / analog needle over a dial) and source (train speed, consist load, station departures, station status, or custom text).

- **Brass Display Screen** (grid): the train's own board. Takes ROTATIONAL power - while any shaft, gearbox or engine on the grid turns above idle the drums keep clicking; park the engine and the board goes dark mid-word.
- **Display Cabinet**, **Hanging Departure Board** and **Nixie Readout** (stationary): mains-fed through a small electric engine inside that turns the flap drums. The hanging board lists every scheduled service calling at the station beside it, from the schedule blocks' live state - the departure board and the timetable are the same data.
- The reference board's look is honoured: dark cards, warm text, brass bezels on everything.

And the sound that makes it a split-flap board: `Sfx.SplitFlapFlip` - click, slap, rattle - staggered per row as the cards turn edge-on, swap invisible, and land. A board updating should sound like a board updating, not like one clap. Nixie updates flicker their emission instead. Screen kind, source and custom text are saved for both grid and stationary housings (additive fields).

#### New in the setup window

Step **95. Build Steampunk Displays & Schedules** (Tools -> Voxel Engine -> Voxel Engine Setup): authors the Train Schedule and Brass Display Screen grid items and prefabs, the three stationary housings with their BlockItems, and five recipes (steel + copper wire, bench/assembler). Non-destructive as always - creates what is missing, repairs unresolvable links, never touches authored tuning. Safe to re-run.

#### Manual steps in Unity

1. Let the project recompile (Console should stay clean).
2. Run **Tools -> Voxel Engine -> Voxel Engine Setup -> 95. Build Steampunk Displays & Schedules**.
3. In a save: craft a Train Schedule (steel x4 + wire x2), build it onto a train grid next to the Rail Truck, press E and add at least two stops.
4. Craft any screen - e.g. the Brass Display Screen (steel x6 + wire x4) on a train, or a Display Cabinet (steel x8 + wire x4) at a station wired into power - press E to pick kind and source.
5. Re-lay or extend a track run over older bare stretches and watch the commit toast: the re-bed pass should charge and place ballast under them.

### [12.0.1-dev] Compile Cleanup - Obsolete Finds And A Missing-Script Call That Never Existed

**Type:** PATCH - compile diagnostics only. No save, no API, no behaviour touched.

**GitHub title:** `[12.0.1-dev] Compile cleanup: obsolete find overloads and the missing-script scrub`

#### The error: CS0117 on `GameObjectUtility.RemoveMonoBehavioursWithMissingScripts`

The retirement scrub in setup 85 called a plural API that does not exist in Unity 6.5. The project already solves this exact problem in step 17 of the setup window with the singular, per-GameObject call - `GameObjectUtility.RemoveMonoBehavioursWithMissingScript` - walked over `GetComponentsInChildren<Transform>(true)`. Step 85 now follows the same pattern instead of inventing a second one, so inactive children are scrubbed too and the two passes cannot drift apart.

#### The warnings: CS0618 on `FindObjectsByType` with `FindObjectsSortMode`

Both call sites in `LogisticsMapData` now use the `FindObjectsInactive.Exclude` overload the rest of the project already standardised on (Cryobed, SpaceOrigin and others). One was the new v2 bogie marker from 12.0.0-dev; the other was the pre-existing chest-zone gather, fixed in the same pass because a warning left in the log is a warning that hides the next real one. Sort order was never relied on at either site - markers are regrouped by kind afterwards - so nothing observes the change.

#### Manual step in Unity

None. Code-only patch; recompile and the Console should be clean. Setup 85 still needs its re-run from the 12.0.0-dev round if you have not done it yet.

### [12.0.0-dev] The v1 Train Is Gone - One Railway, Not Two

**Type:** MAJOR - removes the 11.15.0 `RailTrain` entity and everything that pointed at it. BREAKING: saves that contain a v1 locomotive or its schedule cannot carry it across; start a fresh save.

**GitHub title:** `[12.0.0-dev] The v1 train is gone - one railway, not two`

#### What was removed and why now

The rework brief in the roadmap ended with two items: automatic junctions at crossings, and retiring the 11.15.0 `RailTrain`. The junctions landed in 11.41.0-dev; the retirement is this round, and it is the reason this version is a MAJOR. A v1 train was a scheduled agent walking a private graph - the one large buildable in the game that was not a player-built grid, could not be designed block by block, took no damage, held no paint, and needed its own console. Train System v2 made a train an ordinary grid with a Rail Truck on it, and every feature since has been written once, against grids. Keeping the v1 entity meant keeping a second railway forever.

The retirement waited for a MAJOR on purpose: removing the entity breaks any save that still holds a locomotive, and that belongs in a version bump a player can see, not in a minor they cannot.

#### What changed in code

- `RailTrain` and its `TrainState` enum are deleted. Nothing else in the project compiled against them after this round.
- `RailConfigHud` loses the train console. A train configures itself through the grid terminal like every other buildable; the console keeps its station and switch panels.
- The logistics map reads `GridRailBogie` instead of the retired entity list, so v2 trains appear where v1 trains used to - name, RUNNING / REVERSING / IDLE / OFF RAIL, and an alert tint when a bogie is off the rail.
- The E-interaction branch that opened the v1 console is gone; E on a rail truck and on a coupler remain the train-side verbs.
- Setup step 85 now scrubs the dead script off any locomotive prefab it finds (`RemoveMonoBehavioursWithMissingScripts`). The prefab shell stays on disk - a MAJOR demands a fresh save, not a deleted history - but no missing-script component survives to warn on every load. The recipe left the registry back in 11.39.0.

#### What a player loses, precisely

A saved v1 locomotive and its schedule. Nothing else: track, switches, signals, stations, truck bogies, couplers and consists are all v2 systems and are untouched. A locomotive placed in an old save loads as an inert shell block in the fresh-save migration case, and as nothing at all once the save is retired, which is the honest outcome of removing an entity rather than stubbing one.

#### Manual step in Unity

Re-run setup **85** from Tools -> Voxel Engine -> Voxel Engine Setup to scrub dead scripts off old locomotive prefabs. Non-destructive: it only removes components whose script no longer exists. Start a fresh save afterwards - this is the MAJOR that the old ones were waiting out.

### [11.41.0-dev] Junctions That Join, Curves That Bend, And A Bill While You Drag

**Type:** MINOR - rail graph, track visuals and a new drag HUD. Save-compatible (one additive save field).

**GitHub title:** `[11.41.0-dev] Junctions that join, curves that bend, and a bill while you drag`

#### Turns are smooth now, because a bend is finally drawn as a bend

Two separate faults made the same kink. The rotation pass aimed each cell's WHOLE rotation at the next cell, which replaced the corridor solver's mitred yaw with the chord direction - a half-step zigzag per cell on every curve. It now keeps the solver's yaw and adds only the pitch the ground asks for.

The second fault is geometric: a track cell is a rigid box set, and on a curve the outside of a bend is longer than the inside, so rigid cells gapped outboard and overlapped inboard - the dashed, fanned corner in the screenshot. `RailTrack` now derives the signed curvature of its own through route from the link graph (circumcircle of the two most opposite neighbours) and deforms its children onto that arc: sleepers fan radially, each square to the tangent at its own station, and each rail stretches to the arc length at its own offset, so rail ends meet end-to-end through the bend. It is derived, not stored, so a save reloads and re-derives it, and a junction re-link heals it.

Rail runs also ask for a gentler fillet than roads do: 2.75 m minimum radius for single track instead of the road solver's 1.25 m, where one cell turned through 46 degrees and no dressing could read as a curve.

#### Crossings become junctions - and do not reroute your trains

Laying a line across another used to produce visible overlapping track the graph could not use: the existing cell kept its two-link straight budget, so the new arms had nothing to link into. Three changes fix it at the root.

- A station is SPLICED into the run at the exact position of any existing cell the route crosses, so that cell becomes the shared node: both lines meet it at proper cell spacing instead of stacking inside it or stopping short of it.
- Promotion to a junction now counts ARM DIRECTIONS, not neighbours - three directions 22.5 degrees apart - and runs on every link rebuild, not only on corridor commits. A hand-placed cell completing a T, and a save reloading its graph, heal into junctions through the same path. Parallel double track never promotes: its cells have no external neighbour to trigger the candidate rule.
- A fresh junction routes STRAIGHT THROUGH by default (`RailTrack.NextFrom` picks the straightest continuation of the entry leg). This is what retired the old fear that auto-junctioning would silently reroute trains: nothing turns unless a player sets the points, and whether they did is saved (additive field `railPointsSetByPlayer`), so a reload does not quietly reset a yard.

Adjacency also tightened from 1.45 m to 1.10 m. Real neighbours sit at most 1.06 m apart (1 m arc plus the gradient cap); 1.45 m let the 1.41 m lattice diagonal and parallel lines one metre abeam reach into a cell's link budget. A 5% ordering penalty keeps fore/aft ahead of a pure lateral tie without ever excluding a junction arm.

#### The bill arrives while you drag, not after the click

A new bottom-centre card, `RailCostHud`, reads out the run as it grows: metres, cell count, gauge, and per-material need against what the inventory actually holds, green while affordable and red the moment it is not. It is driven from the SAME plan the ghost draws and the commit lays, so the number on the card, the preview on the ground and the bill after the click cannot disagree. A refused run names its reason on the card in amber while you are still aiming.

#### The formation is three times the width

Sleeper 1.5 m to 4.5 m, gauge 1.05 m to 3.15 m, rail heads 0.11 m to 0.33 m, ballast bed 2.1 m to 6.3 m - the whole permanent way scaled together so the real 0.70 sleeper-to-gauge ratio survives. The Rail Truck bogie is re-gauged from the same number in step 92, so wheels sit on rail heads rather than running down the middle of the sleepers. The ghost preview measures the deck off the prefab instead of trusting a constant, so it previews exactly as wide a formation as the commit lays.

All three setup steps apply the width to prefabs authored before it through geometry-only, idempotent re-gauge passes: a prefab already at the new width is left byte-identical, and nothing but running gear moves - no component, material, recipe or tuning value is touched.

#### Manual step in Unity

Re-run setup **85** (track formation), **92** (bogie re-gauge) and **93** (ballast bed) from Tools -> Voxel Engine -> Voxel Engine Setup. All three are non-destructive re-gauge passes; existing track in a save heals its curves and junctions on load by itself.

### [11.40.0-dev] A Real Bogie, And Track That Flows

**Type:** MINOR - rail visuals, load physics and a new coupler block. Save-compatible.

**GitHub title:** `[11.40.0-dev] A real bogie, and track that flows`

#### Track now flows over hills instead of stepping up them

Your photo showed each cell as a separate level slab at a different height - a staircase, not a railway. The cause: rotation came straight from the corridor solver, which works on a **flat plane**, so every sleeper stayed perfectly level while the positions climbed underneath.

Each cell is now pitched to aim at the next one, so consecutive cells share an edge rather than overlapping at a corner. Smoothing also went from 6 passes to 18, because at 6 a real hillside still left steps big enough to see - a worst step of 1.10 m now grades to 0.29 m.

The last cell of a run aims *back* at its predecessor, so the end does not flip flat and re-create the seam there.

#### The bogie looks like a bogie

Rebuilt from your reference: two side frames carrying the axleboxes, a bolster across the middle that the wagon rests on, a centre pivot, visible coil springs over each axlebox, orange brake gear, and flanged wheels on real axles at the 1.05 m gauge the track uses.

The springs matter more than they look. They are what makes it read as something that **carries weight**, which is exactly the mechanic underneath it.

#### Weight actually matters now

| Load | 1 bogie | 2 bogies | 4 bogies |
|---|---|---|---|
| 6 t | 14.0 m/s | 14.0 | 14.0 |
| 12 t | 7.0 | 14.0 | 14.0 |
| 24 t | 3.5 | 7.0 | 14.0 |
| 48 t | 2.1 | 3.5 | 7.0 |

Mass is summed across the **whole consist** and divided by the combined rated load of every bogie in it, so your rule falls out directly: heavier is slower, more bogies is faster, and the gain is capped because the factor never exceeds 1.

Load is shared rather than per-bogie deliberately - a real consist spreads weight across every axle, and checking each bogie against only its own grid would let a player defeat the rule by putting the heavy wagon in the middle. The floor is 15% of top speed, never zero: a train that cannot move at all reads as a bug rather than as overloaded.

#### Trucks snap to rails, and cannot be stacked

Placing a truck on rail now **attaches immediately**. Previously the only way on was a button inside the block console, so a player who built a train beside a line had no indication a further step existed - it just sat there.

Stacking is refused, and the block is returned rather than sitting there inert. Stacked bogies break the load model as well as physical sense: each would claim its own rated capacity while carrying the same mass, making an overloaded train arbitrarily fast. Side-by-side trucks stay legal, because that is a real four-wheel arrangement - only *directly below* is checked.

#### Attach, detach, couple, release

- **E on a rail truck** - toggles on and off the rails
- **E on a coupler** - attaches to the car in front, or releases

The new **Wagon Coupler** block is what you asked for. It holds no physics joint - consists still follow the leader's recorded path, since a joint between kinematic bodies does nothing - it is the player-facing control for that system, mounted where the connection physically is.

#### Also

Removed the "re-run setup step 85/93" text from the failure toasts.

#### Manual step in Unity

Re-run **85** (track pitch) and **92** (new bogie, wagon coupler). Both non-destructive.

### [11.39.0-dev] Retire The Locomotive, Widen The Gauge, Make It Cobblestone

**Type:** MINOR - retires a superseded block and reworks rail visuals. Save-compatible.

**GitHub title:** `[11.39.0-dev] Retire the locomotive, widen the gauge, make it cobblestone`

The diagnostics added in 11.38.0 did their job: *"'Rail Track' has no placed prefab"* named the exact cause immediately, where three previous releases had guessed. Track is laying now.

#### The v1 locomotive is retired

Step 85 was still authoring it, so the game offered two ways to build a train - and the old one is strictly worse: it cannot carry grid blocks, take damage, be painted, pressurised, or designed by the player.

Its recipe is **removed from the registry** rather than the asset being deleted. A save may still contain one, and deleting the asset would turn that into a missing reference on load - the block would vanish from the player's world with no explanation. Dropping the recipe makes it uncraftable, so it stops being a choice for new play while anything already built keeps working until the MAJOR release that removes `RailTrain` outright.

#### The dependency is now stated

Steps 92, 93 and 94 all need step 85 - they place, lay and signal the track it authors. Step 93 already refused with a clear message, but the wizard buttons said nothing, so the ordering was only discoverable by hitting the error. All three buttons now read **"needs 85"**.

#### The gauge was far too narrow

Rails sat 0.56 m apart on a 1 m cell, which reads as a narrow ladder down the middle of a wide bed rather than a railway.

| | Was | Now |
|---|---|---|
| Gauge | 0.56 m | 1.05 m |
| Sleeper length | 0.78 m | 1.5 m |
| Sleepers per cell | 2 | 4 |
| Rail profile | 0.07 square | 0.11 x 0.12 (taller than wide) |

The gauge-to-sleeper ratio is now 0.70, which is what real track uses. Sleeper count doubled because at 1 m spacing two per cell left visible gaps at every cell boundary - a run looked like a dashed line rather than continuous track.

#### The ballast reads as cobblestone

It was one smooth cube. A flat surface has no self-shadowing, so it reads as poured concrete however it is tinted - the tint was never the problem.

The bed is now a base slab plus **26 jittered cobbles** in three tones, each with a random yaw and a slight tilt, because the shadows *between* stones are what makes ballast look like ballast. It is also wider (2.1 m) so the shoulder spreads past the sleeper ends like real ballast does.

Two details worth naming:
- **The jitter is deterministic**, seeded by a constant. A prefab authored twice must be identical, or two setup runs produce visibly different track.
- **Cobbles have no colliders.** They are decoration on the bed; 26 extra colliders per cell would be a real cost on a long line.

#### Re-tuned the stacking against the new geometry

Changing the ballast changed where its top sits, so the rail rise was recomputed rather than left alone: cobbles top out at 0.115 m, and the rail now rises 0.11 m, putting the sleeper underside at 0.110 m - **bedded into the stones** rather than floating 6.5 cm above them, which is what the old value would have produced.

#### Manual step in Unity

Re-run **85** (retires the locomotive, widens the gauge) and **93** (rebuilds the ballast). Both are non-destructive.

Existing track keeps its old prefab; the new geometry applies to track laid after the rebuild.

### [11.38.0-dev] The Ghost Was White And The Failure Was Silent

**Type:** PATCH-level fixes shipped as MINOR (new diagnostics API). Save-compatible.

**GitHub title:** `[11.38.0-dev] The ghost was white and the failure was silent`

Your screenshot solved this. The white slabs in it **are** the ghost - it was rendering the whole time, just colourless - and that told me planning was working and the failure was downstream in `Commit`.

#### Why I kept missing this

`Commit` had four early returns that all did the same thing: `return 0`. Null plan, null track block, null placed prefab, empty cell list - every one produced a bare zero, and the caller printed "the route produced no placeable cells" for all of them. The message actively misdirected the search, and I spent three releases looking at the planner because that is what it pointed at.

**Every early return now states its own reason**, surfaced in the toast and logged with the full counts - planned, solved, missed ground, blocked, skipped. A silent failure path in a tool the player invokes is a bug in its own right, independent of whatever caused it.

#### The likely root cause, and a fix that does not depend on setup order

A Rail Layer asset created by an earlier setup run has `trackBlock` null, because the field did not exist when it was authored. Step 93 repairs it - but only when re-run, and the tool in your inventory was already made.

`Commit` then hit `trackBlock == null` and returned 0 silently. The tool now **finds the Rail Track block by id at runtime** if its reference is missing, logs that it did, and carries on. An upgrade can no longer leave a dead tool in the player's hands regardless of what order steps are run in.

#### The ghost was white for the same reason the asteroids were

I set vertex colours and used URP/Unlit - which does not read vertex colours. Identical to the asteroid material bug in 11.28.1, and I made it again. The alpha was ignored too, because that fallback shader is opaque, which is why it read as a solid white slab rather than a translucent hint.

Rather than hunt for a vertex-colour shader, the ghost is now **two meshes with two real materials**: green for placeable cells, red for blocked ones. That needs no special shader, so it cannot silently stop working if the render pipeline changes.

#### What to expect now

If a run still lays nothing, the toast will name the actual cause and the Console will carry a `[RailLayer]` line with every count. That turns the next report into a fix rather than another round of guessing.

No manual Unity step, though re-running **93** will persist the recovered reference.

### [11.37.0-dev] Multi-Point Runs, Real Junctions, Honest Ghost

**Type:** MINOR - rail layer rework. Save-compatible.

**GitHub title:** `[11.37.0-dev] Multi-point runs, real junctions, honest ghost`

#### "Nothing laid" - and why I kept failing to fix it

Two releases were spent guessing at this, and the reason is that the message could not distinguish its own causes. "No placeable cells" was printed whether the solver produced nothing, the ground probe missed, or every cell was obstructed. I had no more information than you did.

So the first change is **diagnostics that name the failure with numbers**: how many cells the corridor solved, how many found no ground, how many were blocked. A message that can only say one thing cannot be debugged.

The probe itself had two real faults:

- **It took the first thing it hit.** A plain `Raycast` happily returns the player's own collider, the ghost, a train, or track already laid - and then that becomes "the ground". It now sorts all hits and skips anything with a rigidbody, any grid, any existing rail, and the ghost.
- **It used the start point's gravity for the whole run.** On a small planet the "up" at the far end of a 400 m run is measurably different, which tilts the probe. Each cell now samples its own gravity.

#### The ghost is now green and red, per cell

`EvaluateCell` checks each cell for three real obstructions and the ghost colours each one individually:

| Condition | Result |
|---|---|
| Below the waterline | red - "underwater" |
| Solid terrain above the formation | red - "buried, the route runs into terrain" |
| An existing placed block | red - "blocked by a placed block" |
| Existing rail | **allowed** - that is a crossing |

Per cell, not per run, on purpose: a route that clips one rock shows **one red cell you can nudge around** instead of turning the whole line red and leaving you to guess which end is wrong. The blocked cells are kept rather than discarded for exactly that reason - throwing them away would hide the information you need.

#### Crossings become real junctions

Plain track holds at most two links, so where a new line crossed an old one the extra arms were **silently dropped**. The rails visibly crossed and a train could not take the turn.

Any cell that ends up with three or more rail neighbours is now promoted to a switch, which widens its link budget from two to four, and both lines are re-linked afterwards - the existing line too, or it keeps the two links it had before the junction appeared. A straight run still has two neighbours, so nothing becomes a switch by accident.

#### Multi-point runs

Laying a curve meant committing a leg, then starting again from its end. Now:

- **Left-click** starts a run
- **Right-click** adds a corner - as many as you like
- **E** lays the whole route
- **Escape** cancels

The corridor solver already fillets every interior corner, so a chained route curves properly rather than forming hard angles. Left-click does nothing once a run is started, so a mis-click cannot commit a route you were still shaping.

The ghost previews the confirmed corners **plus the leg you are currently aiming**, because the fillet at the previous corner depends on where the next leg goes - showing the legs in isolation would preview a shape the commit would not produce.

No manual Unity step.

### [11.36.0-dev] Graded Formation, And A Ghost To Aim With

**Type:** MINOR - fixes a hard crash and three rail-layer failures. Save-compatible.

**GitHub title:** `[11.36.0-dev] Graded formation, and a ghost to aim with`

#### 1. The crash - my mistake, with the fix already in the file

`InvalidOperationException` every frame the Rail Layer was held: I called `Input.GetKey` for the Ctrl modifier, and this project has legacy Input switched **off** in Player Settings.

What makes this worse than a simple slip is that `PlayerInteractionTool` already had `IsCtrlHeld()` - a guarded dual-backend helper - a few hundred lines above my code, with a comment directly beside it warning that a bare `Input` call throws every frame here. I wrote a fourth variant instead of using it. Now fixed to call the existing helper, and I checked no other raw `Input` call survives in the rail path.

#### 2. "No placeable cells" on a straight, flat-looking drag

The corridor solves on a **flat plane** through the drag's start point, then every cell is dropped onto the real ground. The probe searched 6 m up and 14 m down.

On a curved planet, or any real slope, cells far from the start sit further from that plane than the probe could reach. Those cells missed the ground entirely, silently kept their flat-plane position, and the gradient check then measured the plane-versus-ground divergence as a vertical cliff - refusing the run.

The probe now reaches 60 m up and 200 m down, and a genuine miss is reported as *"No ground under that route"* instead of being silently passed on as a bogus cell.

#### 3. Rails now grade the formation instead of refusing terrain

You asked for the rails to form the ground, and that is also simply how railways are built: the formation is cut and filled to suit the track, not the other way round. Refusing every natural slope made the tool unusable on exactly the terrain a railway exists to cross.

The run is now **smoothed before it is judged**. Six light passes ease the vertical profile toward a gentle grade, with the endpoints pinned so the line still starts and finishes where you clicked. Only terrain the smoothing genuinely cannot absorb is refused.

I checked the numbers rather than guessing: a bumpy 40-cell hillside with a worst step of **0.79 m** smooths to **0.27 m**, comfortably under the 0.34 m limit. Across a valley the line fills by up to about **0.9 m** - which is exactly what the ballast bed added in 11.35.0 represents.

Only the component along gravity is smoothed. Touching the horizontal would drag the line off the route the corridor solved and undo the curve fitting.

#### 4. The missing ghost

Between the two clicks you were committing to a route you could not see, and every refusal arrived only *after* the second click.

`RailGhost` now draws the pending run every frame, built from **the same plan the commit uses** - not a separate approximation. That property matters: if the ghost shows a route, the commit lays that route. A preview computed differently from the thing it previews can lie, which is worse than no preview.

A refused run still draws, in **red**, using whatever cells it solved. Hiding it would leave you aiming blind at precisely the moment you need to see what is wrong.

No manual Unity step - 11.35.0's step 93 already authored everything.

### [11.35.0-dev] Ballast, And Rail That Actually Costs Something

**Type:** MINOR - fixes the rail layer and gives track a proper stone bed. Save-compatible.

**GitHub title:** `[11.35.0-dev] Ballast, and rail that actually costs something`

Four problems with the 11.33.0 rail layer, all reported together and all real.

#### 1. "Already laid" on empty ground

The duplicate check rejected any cell within **0.45 m** of existing track, against cells spaced **1 m** apart. That sounds safe, and on flat ground it is.

But every cell is draped onto real terrain. On a slope or through a curve, neighbouring cells pull well inside half a metre of each other - so the run rejected **its own cells**, one after another, reported that the route already had track, laid nothing, and charged nothing. On perfectly empty ground.

The threshold is now a quarter of the cell spacing, derived from the actual cell size rather than hard-coded: tight enough to catch a genuine duplicate, loose enough that legitimately adjacent draped cells survive.

#### 2. It reported the wrong reason

"Already laid" was printed whenever nothing was placed, whatever the cause. That is what made this so confusing to diagnose - a failed solve and a genuinely occupied route produced identical messages.

`Commit` now returns how many cells it skipped, and the three outcomes read differently: cells laid, all cells already occupied, or no placeable cells produced. A message that can only say one thing is not a message.

#### 3. It consumed nothing

The tool charged a generic "rail material" that the setup step wired to **steel**, so laying track never consumed track.

It now consumes the **Rail Track item itself** - the same one you craft and place by hand - plus stone for the bed:

| Per cell | Cost |
|---|---|
| Rail Track | 1 |
| Stone | 2 |

That is the right relationship: **the tool saves effort, not materials.** A laid run costs exactly what laying it by hand would, so the choice to use it is about time rather than economy.

Material is also charged against what was **actually placed**, not what was planned, so skipped cells are never billed.

#### 4. No ballast

Track sat directly on the draped ground, half-sinking into anything uneven. Real track is laid on a raised stone bed, which is exactly what the reference photo shows.

Every cell now places a **Rail Ballast** slab under the rail, and the rail is lifted onto it. The slab is 1.5 m wide against a 1 m cell, so the shoulder overhangs the sleepers the way real ballast does.

I worked the offsets out numerically rather than eyeballing them: the slab pivots at its own centre, so it is sunk by half its height to sit flush with the ground, and the rail rises 0.18 m - leaving it 5 mm proud of the bed. Sleepers rest **on** the stone rather than floating above a gap or buried inside it.

Ballast is its own mineable block rather than part of the rail prefab, because a bed and a rail wear out for different reasons and the player should be able to see and remove it.

#### Manual step in Unity

**Tools -> Voxel Engine -> Voxel Engine Setup**, then **93. Build the Rail Layer** again. It is non-destructive and will author the ballast block and rewire the tool's costs without touching anything else.

### [11.34.0-dev] One Train Per Section

**Type:** MINOR - Train System v2, phase 4. Save-compatible and additive.

**GitHub title:** `[11.34.0-dev] One train per section`

Signalling and block occupancy - the last piece of the rework brief.

#### The problem

Two trains on one line drove straight through each other. Everything else was in place - grids on rails, consists, drag-laid corridors - and the thing stopping a player running more than one train was that a second train was a guaranteed overlap rather than a scheduling problem.

#### Sections are derived, not placed

The obvious design is a signal block the player places, which owns the track after it. That has a bad failure mode: a line with no signals is one giant section, so a new player's first railway cannot run two trains and the feature is invisible until they learn it exists.

Instead a section is **derived from the graph**: the track between two junctions is one section, because a junction is exactly where routes diverge and therefore where a train's path becomes uncertain. Signalling works the moment there is track, with nothing to place, and gets finer automatically as the network grows more complex - which is precisely when it is needed.

Same principle as deep ore nodes, hazard zones and asteroid placement: derived state cannot desynchronise from the thing it describes.

**A junction is its own single-cell section.** It is the one place two routes physically share metal, so it must be exclusive even when the lines either side are clear.

#### The detail that makes it correct

Section ids are the **lowest cell hash** in the section, not the first cell found. Two trains approaching the same stretch from opposite ends must compute the *same* id - otherwise each thinks it owns a different section and both enter it, which is exactly the head-on collision the system exists to prevent.

#### Self-deadlock, avoided twice

The classic way a signalling system fails is a train blocking itself:

- **A consist claims as one entity.** The claim is made by the head bogie, so a five-car train is one claimant rather than five competing over the same section. Followers never run the claim path at all.
- **A train releases everything behind it** as it moves, keeping only the section it occupies and the one it is entering. Both are held while crossing a boundary, because a train straddles two sections at that moment. Without the release, one lap of a loop would deadlock the network against its own train.

Claims are also dropped when a train is destroyed or lifted off the rails. A claim held by a dead object would block that line forever, and the only symptom would be trains mysteriously refusing to cross empty track.

#### Braking, not teleporting

Lookahead scales with actual stopping distance (`v^2 / 2a`), so a fast train gets more warning than a shunting one. A fixed distance would either stop a slow train absurdly early or fail to stop a fast one in time. A blocked train brakes to a halt rather than stopping the frame the signal turns red, which would look broken and throw anything riding on it.

#### The signal block is deliberately not load-bearing

Occupancy is automatic. Removing every signal in the world changes nothing about whether trains collide.

What a signal does is make an invisible rule legible: a train stopping for no apparent reason is a puzzle, and a red lamp at the point it stops is an explanation. It reads state and never changes it, so it cannot disagree with the thing it reports.

#### Manual step in Unity

**Tools -> Voxel Engine -> Voxel Engine Setup**, then **94. Build the Rail Signal**. Optional - occupancy already works without it. Right-click a signal to read its state.

### [11.33.0-dev] Lay A Line, Not A Thousand Cells

**Type:** MINOR - Train System v2, phase 3. Save-compatible and additive.

**GitHub title:** `[11.33.0-dev] Lay a line, not a thousand cells`

Wider gauges and draggable smart placement - the last two items from the rework brief before signalling.

#### The problem

Laying rail one cell at a time was the most tedious thing in the game. A line between two bases is hundreds of clicks, every curve is stepped by hand, and a gradient mistake is only discovered when a train refuses to connect to its own track.

Meanwhile the road system has had click-and-drag multi-lane corridors with solved curves for a long time.

#### Reusing the road solver rather than writing a rail one

`RoadCorridor` already solves exactly this geometry: a centreline through waypoints, fillet curves at corners, N parallel lanes with an explicit four-corner footprint per cell, and a refusal when a corner is too tight for the width. The roadmap named it as the precedent, and it was the right call - writing a second solver would mean two implementations of the same maths drifting apart.

What rail adds is only what roads genuinely do not care about:

- **Gradient.** Rail refuses a slope a road drapes over. The corridor is checked against `RailTrack.maxGradientMetres` per lane *along the direction of travel* - comparing across lanes would measure the cant of the formation, which is not a gradient at all.
- **Gauge meaning.** A road's lanes are independent surfaces; a rail corridor's lanes are **parallel tracks**, so a 2-wide run is a double-track mainline rather than one wide rail.

#### Refusals, not partial success

A plan is either fully placeable or fully refused, and the refusal names the reason - the steepest rise found, the gauge that cannot take the corner, the length of an over-long drag. Laying half a line because the far end was too steep would leave the player with track that goes nowhere and no explanation.

Material is counted **before** anything is placed, so a run the player cannot afford lays nothing and charges nothing rather than stopping halfway.

#### One ordering detail that matters

`Commit` places every cell first and links the whole run afterwards. Linking as it went would let each cell fill its limited link budget with the cell behind it before the cell ahead existed - producing a line of disconnected pairs that looks like track and behaves like gravel.

Cells that already have track are skipped, so crossing an existing line extends the network through the normal adjacency rules instead of stacking duplicate rails inside each other.

#### Deliberately not included: automatic junctions

The tool does not place switches where two runs cross. Auto-junctioning needs to know which of two crossing routes is the through line, and guessing wrong would silently reroute a player's trains. Crossing corridors simply connect, and the player places a switch where they actually want one.

#### Manual step in Unity

**Tools -> Voxel Engine -> Voxel Engine Setup**, then **93. Build the Rail Layer**. It requires step 85, because it lays the same Rail Track block you place by hand.

Hold the tool, click a start, aim at the far end, click again. Right-click cancels. **Ctrl + scroll** picks the gauge, 1 to 3 parallel tracks.

### [11.32.0-dev] Couple Them Up

**Type:** MINOR - Train System v2, phase 2. Save-compatible and additive.

**GitHub title:** `[11.32.0-dev] Couple them up`

Multi-car consists: park a railed construct behind another and couple them.

#### The docking-port precedent did not survive contact

The roadmap said to couple grids "the way docking ports already join grids". Docking ports use a `FixedJoint` - and that turns out to be exactly wrong here, for two reasons I only found by reading the code rather than assuming:

- **A railed grid is kinematic** (11.31.0 takes it out of the solver so the rail constraint is exact). A joint between kinematic bodies does nothing at all.
- **A joint trails like a rope.** A wagon holding a fixed distance from the locomotive's *current position* cuts every corner and ends up beside the track on any curve.

So consists use path history instead. The leader records where it has been, and each wagon samples that trail at its own distance back.

#### Why path history is the right shape

It gives the behaviour for free that a naive follower has to fake:

- A wagon retraces the **exact route** the locomotive took, so it stays on the rails through curves and points.
- It inherits the leader's routing decisions, **including which way a switch was thrown**, with no track logic of its own.
- Spacing accumulates **along the chain**, not straight-line, so the third wagon sits three gaps back along the actual route rather than three gaps as the crow flies.

A wagon that cannot find history far enough back holds station rather than snapping to the leader, which is what stops a freshly coupled train telescoping into itself.

#### Decisions worth naming

- **A towed wagon cannot drive.** Coupling forces its power off, and its `FixedUpdate` returns before any track logic runs. Two powered bogies on one consist fight each other, and a second bogie resolving switches independently could split a train across a junction.
- **Coupling keeps the spacing you parked at**, rather than snapping to a constant, so a consist holds the shape you built.
- **Refusals say why.** Too far, already coupled, not on rails, would form a loop - each returns a reason, because "I pressed couple and nothing happened" is indistinguishable from a bug.
- **Loop detection walks the chain before coupling.** Without it, a cycle would make every consist walk in the file run forever; every walk is also bounded at 64 cars as a second line of defence.

#### Breaking a consist safely

Removing a wagon from the middle relinks both neighbours **and hands its spacing to the one behind**, so the rest of the train does not lurch forward into the gap. Destroying the locomotive uncouples the wagon behind it and re-latches it to the track, so it is immediately drivable rather than stranded following a corpse.

#### Still to come

Wider gauges, draggable smart placement, and signalling. The 11.15.0 `RailTrain` stays until those land.

No manual Unity step - the Rail Truck from step 92 is all that is needed.

### [11.31.0-dev] A Train Is Just Something You Built

**Type:** MINOR - Train System v2, phase 1. Save-compatible and additive; the 11.15.0 rail network is untouched and existing trains keep working.

**GitHub title:** `[11.31.0-dev] A train is just something you built`

Section 6.4 item 1b - unifying rail with the grid system.

#### The open question turned out to be a wrong premise

The roadmap blocked this rework on one thing: *"how does a grid-based train keep running while its chunks are unloaded?"* - because unattended operation is the entire reason rail beat rovers for bulk haul. The expected answer was a dormant analytic mode along the rail path, mirroring `OrbitalRails`.

I checked before building it, and the premise was wrong. **Grids are not chunk-streamed.** They are persistent scene objects saved by body anchor, and nothing distance-culls them. Rail track is `PlacedBlock`, likewise never distance-culled. So a grid on rails keeps ticking wherever the player is, and the hard part did not need building at all.

That is worth stating plainly because it inverts the cost of the whole rework: the property that justified keeping trains separate was never actually at risk.

#### A train is now an ordinary construct

Build any grid, put a **Rail Truck** on it, drive it onto track. There is no locomotive entity and no special vehicle type.

Everything that already works on a grid now works on a train, for free:

- Any grid block works on a wagon - containers, tanks, refineries, turrets - because it is a grid and those are grid blocks.
- Damage, paint, power, pressurisation and the inspection overlay all apply.
- The console folds into the normal grid block UI instead of a parallel rail window.

#### Why a block rather than a flag

The Orbital Programme declares a satellite through `GridIdentity` - a property of the whole construct. Being railed is deliberately different: it is **hardware**. The player builds it, pays for it, can remove it, and it takes space on the hull. A flag would make every grid a potential train for free, and the rail capability would live nowhere the player can see.

#### Design decisions worth naming

- **The slowest truck wins.** A consist is tuned from the minimum speed and weakest acceleration across every truck aboard, because a train is limited by its worst component. Taking the best would mean bolting one fast truck to a heavy wagon made the whole thing fast, which is backwards.
- **A railed grid goes kinematic.** A rail is a hard constraint, and fighting the physics solver to hold one is how a train jitters, climbs its own track, or gets shoved off by a collision. Movement uses `MovePosition`, so anything standing on the train is carried rather than left behind.
- **Removing the last truck detaches and deletes the bogie**, handing the grid back to ordinary physics rather than leaving it frozen on track it can no longer drive.
- **The track cell is not saved.** The rail graph rebuilds from placed blocks, so a bogie re-latches from its restored world position a frame after load. Only the player's intent - powered, reversed - is state.

#### Still to come in this rework

Multi-car consists, wider gauges, draggable smart placement and signalling. The 11.15.0 `RailTrain` remains in place and working; it will be retired once consists land, rather than removing a working feature before its replacement is complete.

#### Manual step in Unity

**Tools -> Voxel Engine -> Voxel Engine Setup**, then **92. Build the Rail Truck**. Build a grid, place a truck on it, park within a few metres of track, then SNAP TO RAIL and DRIVE.

### [11.30.0-dev] Don't Overwrite What You Couldn't Read

**Type:** MINOR - save schema versioning and corruption recovery. Fully backward compatible: saves made before this release load unchanged and are migrated on the spot.

**GitHub title:** `[11.30.0-dev] Don't overwrite what you couldn't read`

Section 6.6 item 9 - Interplanetary Save Data.

#### What was actually missing

I checked what the item still needed before writing anything. Orbital stations already save (`OrbitalRails` Keplerian elements, restored at the correct phase), asteroid positions are derived from the world seed and correctly never saved at all, and cargo schedules shipped in 11.24.0. The item's content was done.

What was missing was the part the roadmap called "requires save schema v2" - and looking for it turned up something considerably worse than a missing version number.

#### The bug: a failed load would silently destroy the world

The loader caught every exception, logged it, and carried on with an empty world. The autosave timer then fired a few minutes later and wrote that empty world **over the save it had just failed to read**.

So a save that was merely unreadable - a truncated write, a half-flushed file, one corrupt field - became permanently lost data, automatically, with the only warning buried in the console.

Worse, the atomic save has been writing a `.previous` sidecar on every single save for a long time. **Nothing ever read it.** A perfectly good backup sat next to the broken file while the game overwrote the original.

Three fixes:

- **The backup is now read.** A failed primary load falls back to `.previous` and recovers from it, saying so clearly.
- **A failed load blocks all saving for the session.** Not just autosave - every entry point funnels through `SaveAll`, including quit and the pause menu, and I traced all four to confirm. The file and its sidecar are left untouched so the player still has something to recover from.
- **Truncated-but-parseable saves are caught.** `JsonUtility` happily returns an object for some malformed input, so a save missing its player block - the one field every save must have - is now treated as a failure rather than loaded as an empty world.

#### Schema versioning

`SaveData` now carries `schemaVersion`, stamped on every write. Saves from before this release have no field, deserialize as 0, and are treated as v1 and migrated to v2 on load.

The v1 to v2 migration is deliberately a no-op beyond the stamp itself. Every field added to this format has been additive - a missing list just takes its default - which is exactly why saves have kept working without a version until now. That only holds while changes stay additive, and the moment one does not, there is now somewhere for the fix to live and a number to decide which fix to apply.

A save from a **newer** build is loaded rather than refused, with a clear warning that anything this build does not understand will be dropped on the next write. Silently discarding a newer save's data would be worse than saying so.

#### Verified by case, not by inspection

| Scenario | Outcome |
|---|---|
| No file yet | Normal new world |
| Good save | Loads, migrates if old |
| Primary truncated, backup good | **Recovered from backup** |
| Primary and backup both corrupt | **Saving blocked, files preserved** |
| Exception during restore | **Saving blocked, files preserved** |
| Save from a newer build | Loads with a warning |

No manual Unity step.

### [11.29.0-dev] Your Base Keeps Working

**Type:** MINOR - a new system, save-compatible. Adds serialized per-machine state that defaults to "never serviced", so existing saves simply start their clock on load.

**GitHub title:** `[11.29.0-dev] Your base keeps working`

Section 6.6 item 8 - the "persistent base state across zone transitions" half of Scene/Zone Streaming.

#### What I found

Chunk streaming already exists, and terrain edits and placed blocks already persist. So the interesting half of item 8 was not loading - it was this: **every producing machine in the game ticks in `Update`**, which means a base on a planet you have flown away from produces absolutely nothing.

That quietly undermined the whole interplanetary layer. 11.24.0 shipped cargo pads so a second world could feed the first, but there was nothing to feed them with - the mine meant to fill the pad froze the moment you left orbit.

#### Catch-up, not background simulation

The obvious fix is to keep ticking unloaded machines. That is the wrong one: it costs CPU forever, scales with everything the player has ever built, and runs thousands of Updates for things nobody can see.

Instead a machine records **when it was last serviced**, and on waking asks how much simulated time passed. For a constant-rate machine, producing N seconds of output in one step is the same result as N seconds of ticking, at a fraction of the cost and with zero per-frame work while away.

Same principle already used three times here: satellites ride analytic orbits, trains walk a graph, deep ore nodes are derived.

Time comes from `CosmicRegistry.SimulationSeconds` - the one clock that is authoritative **and saved**, so it advances across a session boundary. `Time.time` resets on load and would hand out a free harvest or none at all depending on load order.

#### Deliberate limits, stated not hidden

- **Offline output is 45% of live output.** A base you are standing in must always be the better base, or the optimal play becomes logging out - a miserable design.
- **Catch-up caps at 12 hours.** Long enough that a real break is rewarded, bounded enough that an idle world cannot bank an infinite harvest.
- **Power cannot be verified retroactively**, so the extractor assumes it held but only claims the reduced offline rate.

#### Livestock plays by a different rule, on purpose

An unattended pen still drains hunger and thirst - you genuinely have to keep it stocked. But an animal can **never starve to death while you were unable to reach it**. Condition decays to a floor above the damage threshold and health is left alone.

Returning to a field of corpses you had no opportunity to prevent is a punishment for playing the rest of the game, not a consequence of neglect. Walk away and your herd is hungry and unproductive; it is not dead.

#### A real bug this uncovered

`CargoFlightRegistry` was ticked **only from a loaded pad's `Update`**. Fly away from both ends of a route and the shipment froze in transit indefinitely - the exact failure unattended freight exists to avoid, in the feature built to avoid it.

Flights now advance on the cosmic clock, driven by a tiny always-present `OfflineSimulationDriver` bootstrapped via `RuntimeInitializeOnLoadMethod`. No scene object to place and none to forget - a feature that silently dies because something was missing from one scene is a class of bug this project has already lost two releases to.

#### Two double-pay traps closed

- A machine running **live** pins its clock every frame, or an hour of real work would also be claimed as an hour of absence.
- A machine **stopped** (unpowered, output full) also pins its clock. Without that, a jammed extractor would bank its idle hours and pay them out as catch-up - rewarding the player for the time it spent jammed.

Catch-up is surfaced on the machine's status line rather than appearing silently, because a pile of ore with no explanation reads as a bug.

No manual Unity step.

### [11.28.2-dev] Mining Was Switched Off In Space

**Type:** PATCH - makes asteroids actually minable and correctly named. No save impact.

**GitHub title:** `[11.28.2-dev] Mining was switched off in space`

The rocks look right now, but hitting one did nothing and the label read "SpaceAsteroidField". Both had real causes.

#### Nothing happened because the tool disabled itself in deep space

`PlayerInteractionTool.Update` opened with:

```
if (world == null || shootCamera == null || inventory == null || registry == null) return;
```

There is no planet voxel world in deep space, so `world` is null out there and **the entire interaction tool returned before casting a single ray**. Every asteroid branch I added in the last two releases sat downstream of that line and could never run. I had checked that `MineVoxel` handled asteroids before its own world guard, but never checked whether `MineVoxel` was being reached at all.

The gate no longer requires a world. The planet-only paths that genuinely need terrain - liquid scooping and the fire igniter - now guard individually instead, so nothing that needs voxels runs without them.

#### It was called "SpaceAsteroidField" because rocks were children of the spawner

Every UI that names a hit surface falls back to `hit.collider.transform.root.name`. Rocks were parented to the field object, so the root was the spawner.

Two fixes, because one alone would have been a patch over a symptom:

- **Rocks are no longer parented to the field.** They register with `SpaceOrigin` individually as world roots. This also removes a latent trap: `SetParent(transform, false)` keeps the *local* pose and reinterprets the spawn position, which only worked because the field happens to sit at the origin. Despawn now unregisters the root, or `SpaceOrigin` would keep shifting a growing list of dead transforms.
- **The inspection HUD understands asteroids.** It resolves the voxel material under the crosshair *before* the planet lookup and the root-name fallback, so the label reads the actual material - "Iron", "Ice" - with an ASTEROID tag and a percentage remaining.

The GameObject is also named `Asteroid (Iron)` rather than `SpaceAsteroid_Iron`, so any other UI falling back to a root name reads sensibly.

#### What I got wrong in process

Twice now I have fixed something downstream of a gate without checking the gate. Verifying that `MineVoxel` handles asteroids proves nothing if `Update` returns three hundred lines earlier. Tracing the path from input to effect - not just inspecting the destination - is what would have caught this the first time.

### [11.28.1-dev] The Density Sign Bug

**Type:** PATCH - fixes the shattered, white, near-spherical rocks that 11.28.0 produced. No save impact.

**GitHub title:** `[11.28.1-dev] The density sign bug`

Your screenshot showed the real problem clearly, and it was not "a bit blocky": the surface was **broken into disconnected floating quads**, unlit white, on a shape that was still basically a sphere. Three separate bugs, all mine.

#### 1. Empty voxels must be NEGATIVE, not zero

This was the shattered surface.

`SurfaceNetsJob` finds the iso-crossing with `(da > 0) != (db > 0)` and places the vertex at `t = da / (da - db)`. I stored air as density **0**. The sign test still fired, but `t` collapsed to exactly 0 or 1, so every vertex snapped to a cell corner instead of interpolating between them. Worse, pass 2 decides which cells are solid with `IsTerrainSolid` (also `> 0`), so the two passes disagreed about which cells even had vertices - and the quads that should have joined them were skipped. Hence floating, disconnected faces.

The engine already had the right convention and I had not looked: `SphereDensity.EvaluateAsteroidVoxel` stores **solid as +1..127 and empty as -127..-1**. Asteroids now follow it exactly, including carrying the overshoot through as negative density when a dig empties a voxel - so a fresh crater has a real gradient to interpolate against instead of a faceted edge.

#### 2. White because the shader ignored vertex colours

The mesher bakes material colour into **vertex colours**. I fell back to a plain URP/Lit material, which does not read them, so every rock rendered flat white no matter what ore was in it.

Rocks now use the terrain's own material (`SphereWorld.terrainMaterial`, the `VoxelEngine/VoxelTerrain*` vertex-colour shader). Not a lookalike - the same material asset, so rocks and ground shade identically by construction. That is also the correct answer to "use the same materials as the planets".

#### 3. Still spheres because the noise was far too weak

I had used +/-11% and +/-5.5% displacement, which is a sphere with a faint orange-peel texture. It is now three octaves at +/-38%, +/-18% and +/-8%, on top of a wider per-axis ellipsoid stretch (0.55-1.45).

#### The consequence I had to solve for

Stronger displacement means a rock can reach `radius * stretch * 1.64`, which would have grown straight through the grid padding and been **sliced flat** where it ran out of voxels. The nominal radius is now clamped against that worst case explicitly.

Solving it at 0.5 m voxels capped rocks at a 3 m pebble, so voxels are now **1 m - exactly the planet's `VoxelConstants.VOXEL_SIZE`**. The 32-cell grid then spans 32 m, giving 5-14 m rocks, and a mining brush carves the same volume out of rock as it does out of ground. Spawner range is 2.5-6 m nominal, verified to fit with no clipping at every size.

### [11.28.0-dev] Smooth Rocks, Real Materials

**Type:** MINOR - replaces asteroid meshing and shaping. Save-compatible; asteroids are procedural and were never saved.

**GitHub title:** `[11.28.0-dev] Smooth rocks, real materials`

#### The blockiness was my own mesher

11.27.0 made asteroids into voxel bodies, but I wrote a hand-rolled exposed-face mesher for them - so every rock came out as stacked cubes. The game already had a smooth iso-surface mesher, `SurfaceNetsJob`, and I did not use it.

Asteroids are now meshed with **the exact job the planets use**. Same smooth surface, same shading path, same material colours - because it is literally the same code, not a lookalike.

#### Sizing the rock to the mesher, not writing a second mesher

`SurfaceNetsJob` is hard-wired to a padded `CHUNK_SIZE_P` (34) cube. Rather than generalise it - and risk destabilising planet terrain, which is the thing that matters most in the game - an asteroid's voxel grid is **exactly one chunk**.

That is not a real limitation: at 0.5 m voxels, 32 inner cells is a 14 m rock, which is already the top of the size range we want. The upside is that asteroids inherit every future fix to terrain meshing for free, and there is only one mesher to maintain.

Rock radius is now **2.5-7 m**, which is what actually fits the grid without touching the padding. The previous 11 m ceiling would have been silently clamped.

#### Not all spheres

A field of identical balls reads as procedural filler, so shape comes from three things layered together:

- **A random ellipsoid stretch** per rock (0.62-1.35 on each axis), so they are potatoes and shards rather than balls.
- **Two octaves of value noise** on the surface radius, for an irregular silhouette.
- **Zero to two gouges** - large spherical bites taken out of the body, so some rocks are cracked or cratered rather than whole.

#### Density has to be graded, not binary

The detail that actually makes it smooth: density is **signed and graded** (ramping at the skin, full strength deeper) rather than a hard 0/127. Surface nets positions each vertex by interpolating the iso-crossing between neighbouring voxels - with a binary field every crossing lands exactly halfway and you get the blocky look back, just with triangles.

Mining follows the same rule. A dig **softens** density at the rim of the brush instead of deleting it outright, so a fresh crater is rounded the same way the original surface is. A hard cut would leave faceted holes in an otherwise smooth rock.

#### Planet materials throughout

Voxels store real `MaterialId` values and are coloured through the shared `MaterialRegistry`, so asteroid stone is the same colour as planet stone and asteroid iron matches planet iron. Mining yields the registry's configured drops. There is no separate asteroid material table that could drift out of sync.

#### One placement subtlety

The mesher emits vertices at `cell * voxelSize`, so the mesh occupies a `0..17 m` box rather than straddling the origin. The renderer and collider therefore live on a **child** pushed back by half the grid, which puts the rock's true centre on its transform - so it tumbles about itself rather than swinging around a corner, and the world-to-local mining maths lines up. I checked the mapping numerically: centre cell 16.5 lands at local 0.0 from both directions.

No manual Unity step.

### [11.27.0-dev] Rocks You Dig Into

**Type:** MINOR - replaces how asteroids work. Save-compatible; asteroids are procedural and were never saved.

**GitHub title:** `[11.27.0-dev] Rocks you dig into`

#### What was wrong

11.26.0 made asteroids spawn, but they were still **destructible props**: one lumpy mesh with a health bar. You hit it, it popped, it gave you ore. You could not tunnel into a rock, could not see where the ore was, and could not leave one half-mined and come back.

That is not asteroid mining, it is breaking a crate that happens to be in space.

#### Asteroids are now real voxel bodies

Each rock is a small dense voxel volume of **stone shot through with ore veins**. Mining carves material out of it a scoop at a time; the mesh and collider rebuild from whatever is left; the rock disappears only when it is genuinely hollowed out. Health is gone entirely - a rock is not killed, it is consumed.

Ore is placed as **veins**, not a uniform mix, and biased toward the interior. That is the whole reason to dig rather than shoot: there is something to follow, and the valuable material is actually inside. A uniform sprinkle would make every cubic metre identical and the digging pointless.

#### Why its own voxel grid and not the planet's

`SphereWorld` is planet-scale - it streams chunks around a viewer and anchors coordinates to a body's centre. An asteroid is a free-floating object a few metres across that **drifts and tumbles**. Putting it in the planet grid would mean either it cannot move, or the grid has to support moving sub-volumes, which is a far bigger change than this is worth.

So each rock owns a small voxel array in **local** space and meshes itself. Because the data is local, the whole thing moves and rotates by just moving its transform - exactly what a drifting rock needs.

#### Deliberately blocky, deliberately small

The mesher emits only **exposed faces** as quads rather than running surface nets. A rock is at most ~48 cells per axis and is remeshed only when actually mined, so a smooth mesher buys nothing - and blocky faces read as *"this is voxel material you are digging"*, which is the point.

Rock size dropped again, from 4-26 m to **3-11 m**, because a voxel rock is remeshed on every dig and cost grows with the **cube** of the radius. At 26 m a rock was 373,000 cells and ~136,000 vertices per remesh. At 11 m it is 110,000 cells and ~24,000 vertices, comfortably inside the 16-bit index limit with a 32-bit fallback in place regardless.

#### Two collider decisions, opposite ways

- **Planet-side props want convex.** That was the 11.26.0 fix.
- **A voxel asteroid must be non-convex.** A convex hull would fill in the tunnels you just dug - you would mine a cave and still bump into a solid ball. Safe here because the rock is on a kinematic rigidbody.

#### Mining had to be taught about them

`MineVoxel` returned early whenever there was no active planet world, so mining in deep space was impossible by construction. Asteroids are now intercepted **before** that guard, and carve through their own path - they have no chunk streaming, no sea level and no biome, so sharing the planet path would mean threading "is this an asteroid" through all of it.

Under-tier tools still work, just slowly, the same rule as planet mining - a player must never hit an invisible hard lock out in space with no way back. A full inventory drops the ore at the rock rather than voiding it.

I also found and removed an **older asteroid path** in the same file that still called `TakeDamage`. It would have broken the build and, worse, shortcut the new voxel path entirely.

No manual Unity step. Fly above 12 km and start digging.

### [11.26.0-dev] Asteroids That Actually Exist

**Type:** MINOR - fixes a system that never worked, and corrects a claim I made last release. Save-compatible, no save format change.

**GitHub title:** `[11.26.0-dev] Asteroids that actually exist`

#### I was wrong last release

In 11.25.0 I marked Asteroid Mining COMPLETE after reading the code and seeing spawning, ore pools, drops and colliders all present. You tested it and they never spawned. The code existed; it could not work. Reading a system is not testing it, and I should not have ticked that item off without runtime evidence.

#### Root cause: the keep-out sphere was bigger than the spawn ring

`IsInsidePlanet` rejected any spawn within `radiusKm * 2` of a body - an entire extra planet radius of exclusion.

Planets in this game are **6-8 km in radius**, so that rejected everything within **12-16 km** of a body. The spawn ring only reached **14 km**. The arithmetic:

| Planet | Radius | Old keep-out | Ring | Spawnable band |
|---|---|---|---|---|
| Mars | 6 km | 12 km | 1.2-14 km | 2 km sliver |
| Pirate World | 7 km | 14 km | 1.2-14 km | **none** |
| Olympus | 7 km | 14 km | 1.2-14 km | **none** |

And because a rock only spawns once the player is 12 km *above* the surface, the viewer sits 19 km from the centre - where a 14 km ring mostly points back at the planet it just rejected. Inside any planet's frame, effectively every attempt failed.

The margin is now a **flat clearance above the surface** (2.5 km, plus the atmosphere where a body has one) rather than a multiple of the radius. A clearance is what the rule always meant - do not put a rock inside the ground or in the air a player is flying through - and it does not scale absurdly with body size.

#### The colliders were real but half-inert

`MeshCollider` was attached but left **non-convex**. A non-convex mesh collider cannot collide with other non-convex colliders and is skipped by several sweep paths, so rocks would stop a raycast while ships flew straight through them. Convex is also *required* for a collider on a moving body, and these drift and tumble. Now convex - which is correct anyway, since an asteroid is a lumpy ball.

#### They were also far too big

You described them as small voxel spheres; they were **8-140 m**. Against a 7 km planet a 140 m rock is 4% of a planet's diameter, and against a 0.5 m grid cell it is 280 blocks across - a moon, not something you mine. Rescaled, with the field tuned to match:

| | Was | Now |
|---|---|---|
| Rock radius | 8-140 m | 4-26 m |
| Spawn ring | 1.2-14 km | 0.4-6 km |
| Separation | 450 m | 90 m |
| Cluster radius | 250-900 m | 120-420 m |
| Despawn | 30 km | 12 km |

Separation mattered: at 450 m apart, rocks 8 m across meant a "cluster" was spread fifty times wider than its members.

#### It will not fail silently again

The reason this survived review is that a fully-rejected spawn pass looked exactly like "nothing to do". The field now warns once, naming the counts, when several passes in a row produce nothing while it is empty - so a mistuned filter reports itself instead of producing an empty sky.

No manual Unity step. Fly above 12 km and look around.

### [11.25.0-dev] Plug The Ends In

**Type:** MINOR - closes the automation gap on two existing blocks. Save-compatible, no save format change; existing placed pads and stations gain ports on load.

**GitHub title:** `[11.25.0-dev] Plug the ends in`

#### What I found while looking for the next feature

I went to start Asteroid Mining (section 6.6 item 3) and found it was **already done**: `SpaceAsteroidField` spawns fields in open space, the ore pool already includes platinum, gold, cobalt and ice, `SpaceAsteroid` derives from `Damageable` with real drops, and mining ships are just grids with drills. There was nothing to build, so I have marked it complete rather than re-shipping it.

What I found instead was a genuine gap that mattered more: **the Rail Station and the Cargo Launch Pad had no item ports.** Both had to be loaded and emptied by hand.

That quietly broke the point of both blocks. The whole argument for putting the cargo hold on the station rather than the train was that a factory could fill or drain it on its own schedule while the train just turns up - and that only works if a belt can reach it. The cargo pad is the last link in a chain that starts at a mine, and it could not be fed from one.

#### Both blocks now accept belts and pipes

`RailStation` and `CargoLaunchPad` both implement `IItemPortHost`, so conveyors, chutes, funnels and pipes route into and out of them like any other machine.

They differ in one deliberate way:

- **The rail station allows both directions on its hold.** Its LOAD / UNLOAD role already decides which way cargo flows through the *train*, and a train services the hold through a separate path. Pinning port direction as well would duplicate that decision and let the two disagree.
- **The cargo pad follows its role.** A SEND pad is input-only and a RECEIVE pad is output-only, because a SEND pad is *accumulating* toward a full launch load - if a belt could also pull from it, it would never reach launch size and the pad would sit there loading forever.

Changing a pad's role now flips its port direction with it, through a property that drops the cached descriptor. Setting the raw field would have left belts facing the old way.

#### Existing builds are fixed automatically

Both use `[RequireComponent(typeof(ItemPortRouting))]` rather than having the setup step attach routing. That matters: a pad or station **already placed in a save** gains routing when it loads, instead of only newly built ones working after re-running setup.

No manual Unity step for this release.

### [11.24.1-dev] The Missing Setup Steps

**Type:** PATCH - no gameplay change, no save impact. Restores editor scripts that were never reaching the repository.

**GitHub title:** `[11.24.1-dev] The missing setup steps`

#### Root cause found

Setup steps 89, 90 and 91 kept vanishing between deliveries. The runtime code persisted every time; only the editor scripts and their wizard buttons disappeared, which is why the features looked half-shipped - all the behaviour existed but nothing could author the assets in Unity.

The cause was **workspace size**, not the code. The project was carrying an `Imported Textures` folder of **3,760 files and 49 MB**, putting the repository at **10,076 files and 119 MB** - right on the snapshot limits of roughly 10,000 files and 128 MB. Files past the cap were silently dropped when the workspace was saved, and the most recently written ones lost the race.

`Imported Textures` has been removed from this workspace at your request (it remains in your local copy). The project is now **6,321 files and 70 MB**, with comfortable headroom.

#### Restored

- **Step 89** - Orbital Station Family. Eight hammer families, four tiers each, plus the `orbital_construction` node.
- **Step 90** - Station Life Support. The oxygen source for sealed station compartments.
- **Step 91** - Interplanetary Cargo Pad. Bulk freight between bodies.
- All three wizard buttons in the setup window.

#### Verified this time

Rather than assuming writes landed, this release was checked end to end after the fact:

- All three setup scripts and their `.meta` files exist on disk.
- All three wizard buttons are present.
- Every runtime file from 11.13.0 through 11.24.0 is present - **no other losses**.
- Brace balance verified across all sixteen recently touched files.
- No orphaned `.meta` files and no `.cs` file missing one.

#### Manual step in Unity

**Tools -> Voxel Engine -> Voxel Engine Setup**, then run **89**, **90** and **91**. All three are non-destructive and safe to re-run.

### [11.24.0-dev] Freight Between Worlds

**Type:** MINOR - a new system, save-compatible. Two additive save lists, both empty until cargo pads exist.

**GitHub title:** `[11.24.0-dev] Freight between worlds`

Section 6.6 item 5 - the Interplanetary Cargo Rocket.

#### First: two setup steps were missing again

Steps **89** and **90** did not survive into the repository on their previous deliveries - the runtime code persisted every time, but the editor scripts that author the assets, and their wizard buttons, were lost. That means the station family and life support had no way to be created in Unity.

Both are restored and verified in place alongside step 91. **Please run 89, 90 and 91.** I have checked all three source files, their `.meta` files and all three wizard buttons exist this time rather than assuming the write landed.

#### Why cargo pads rather than a buildable rocket

The roadmap lists a multi-stage rocket vehicle as well. That is deliberately **not** what shipped here, because the game already has a way to reach orbit: build a grid with thrusters and fly it. A separate rocket entity would be a second flying thing that is not a player-built grid - exactly the split the rail system is being reworked to remove. Manned flight stays a grid you build.

So the cargo pad handles what a piloted grid is genuinely bad at: **unattended, repeatable, scheduled bulk freight**. The two answer different problems, which is what keeps both worth having.

This completes the logistics ladder, and it is the rung that was missing:

| Tier | Range |
|---|---|
| Belts | Metres, inside a factory |
| Trains | Kilometres, across one planet |
| Drone ports | 400 m, point to point |
| **Cargo pads** | **Between bodies** |

Without it a second planet is a place you visit rather than a place you can industrialise, because nothing built there can feed anything built at home.

#### Flights are simulated, not flown

A launch is a timer and a manifest, not a physics object - the same reasoning as trains walking a graph and satellites on analytic rails. A shipment completes whether or not either world is loaded, which is the entire promise of unattended freight.

The flights live in a central registry rather than on the pads, deliberately. A flight outlives its endpoints' loaded state: the origin is usually on a planet you have left and the destination on one you have not reached. On a pad it would stop being ticked exactly when it matters.

Transit time is derived from the real distance between the two bodies, through a square root so that near and far destinations are both usable, then clamped at both ends - a floor so nothing is instant, a ceiling so an unlucky planetary alignment cannot strand cargo for a whole session.

#### Cargo is never destroyed

Three separate cases, all resolved the same way:

- **Destination pad missing on arrival** - the flight holds and re-checks, rather than evaporating. It may simply be in an unloaded chunk, and deleting cargo for that would be silent theft.
- **Destination hold full** - the flight circles and retries.
- **Partial delivery** - the remainder stays in flight instead of being lost.

A launch also needs a **full load of one item**. A pad waits rather than burning an entire flight on a handful of ingots.

#### One subtle placement rule

A pad records which body it was built on **once**, at placement, and never re-samples it. The active body changes as the *player* travels, so a pad reading it live would think it had relocated to whatever world its owner happened to be standing on. The recorded body is saved and restored explicitly for the same reason - pads are restored while the player may be on an entirely different world.

#### Manual steps in Unity

1. **Tools -> Voxel Engine -> Voxel Engine Setup**.
2. Run **89**, **90** and **91** (89 and 90 were missing from previous drops).
3. Research **Interplanetary Logistics**.
4. Build a pad on each body, name them, set one SEND pointing at the other and one RECEIVE, then power the sender and fill its hold.

Right-click a pad for the console and its flight board.

### [11.23.0-dev] Hold Your Breath, Or Don't

**Type:** MINOR - a new system, save-compatible. One additive save list, empty until a station is actually pressurised.

**GitHub title:** `[11.23.0-dev] Hold your breath, or don't`

The world room solver - the half of Space Stations that 11.22.0 deliberately did not claim.

#### Finishing what was deferred

11.22.0 shipped the Orbital Station hammer family and explicitly refused to claim pressure integration, because `GridPressureSystem` and `GridRoom` are a **ship** system: they walk a grid's integer block dictionary and know nothing about world-placed objects. Rather than fake it, that release recorded the sealing intent and left the note. This is the missing half.

#### A separate solver, a shared algorithm

The two systems live in genuinely different coordinate spaces. A ship has an authoritative integer lattice with one block per cell; a hammer station is loose world objects at arbitrary positions. Forcing station pieces into the grid solver would mean inventing a fake grid for them, and every future change to ship pressure would have to keep that fiction alive.

So `StationRoomSolver` shares the **algorithm** that was already proven in the grid solver - a bounded flood fill with a one-cell escape shell - and keeps its own coordinate handling.

**The escape shell is the whole trick.** A fill that reaches the shell has found a way out, so that volume is open space rather than a room. That is what makes "sealed" mean something: three walls and optimism will not pressurise anything, and opening an airlock genuinely vents the compartment because the next solve escapes through it.

Solving is event-driven and coalesced - placing a piece marks the solver dirty and the fill runs at most a few times a second - so building a wall stays cheap. A fill that exceeds its cell budget is treated as open rather than allowed to run away.

#### Air has to be produced

A newly sealed volume starts **empty**. A station is built in vacuum, and assuming a full charge the moment a room closes would make the hull and the airlock decorative.

The new **Station Life Support** unit is the source. It is a placed machine that:

- Costs power continuously, so holding an atmosphere is the ongoing price of living up there - and orbital power generation finally has a real consumer.
- **Leaks.** A room slowly loses air, so life support is not a switch you flip once. Cut the power and the compartment goes stale. The rate is deliberately gentle: losing a compartment should give you time to notice, not punish you for walking away.

A **DOCK** collar does not seal, by design. A room with one in its wall will not hold pressure until a ship mates into it, which is exactly what a docking port should mean.

#### One seam, everywhere

Station rooms plug into `RoomAtmosphereService`, the existing single point that answers "is the air here breathable". Player life support, HUDs and offline survival all ask there, so a pressurised station compartment now works for all of them at once without touching any of them.

It is checked **after** ship rooms and deliberately not folded into `RoomAt`, which returns a `GridRoom` - a station room is a different type in world space, and widening that return would force every existing caller to handle a case it does not have.

#### Persistence

Only the **charge** is saved, keyed by room anchor. The rooms themselves are a pure function of which station pieces exist, and those are already saved as placed blocks - storing the geometry too would be a second copy that could disagree with the first.

Charges are applied **one frame after load**, because the pieces are restored by the same load pass and a solve running immediately would find an empty world. A charge whose room no longer exists is dropped rather than leaking into whatever replaced it.

#### Manual steps in Unity

1. **Tools -> Voxel Engine -> Voxel Engine Setup**.
2. Run **89. Build the Orbital Station Family** if you have not already - it was missing its setup file in the 11.22.0 drop and is included again here.
3. Run **90. Build Station Life Support**.
4. Seal a compartment out of station pieces, place a Life Support unit inside it, and give it power.

### [11.22.0-dev] Somewhere To Live Up There

**Type:** MINOR - a new build family and its research, save-compatible. `BuildFamily` values are APPENDED, so every piece already placed keeps its meaning.

**GitHub title:** `[11.22.0-dev] Somewhere to live up there`

Section 6.6 item 2 - Space Stations & the Orbital Station Hammer Family.

#### Eight new families on the Hammer wheel

| Piece | Role |
|---|---|
| HULL | Pressure wall with structural ribs |
| DECK | Interior floor with a utility channel |
| CORRIDOR | Open-ended tube; chains into runs |
| JUNCTION | Open on four sides; where a station branches |
| VIEWPORT | Framed reinforced window |
| AIRLOCK | Sealed hatch with a mating ring |
| DOCK | Open collar a ship mates into |
| DOME | Observation cap for a hull or junction run |

Each exists at all four build tiers, as every hammer family does. The tier ladder is deliberately flat here - upgrading a station piece is cheap rather than a second full build, because a station is already an end-game structure and making it a four-times-over material sink would just be tedium.

#### The wheel now has groups, not more pages

The obvious implementation was to append eight families to the existing list. That would have pushed the wheel from two pages to three and buried the everyday pieces a player uses constantly behind the ones they use occasionally.

Instead the wheel has two **groups**, and `TAB` swaps between them while it is open. It always opens on STRUCTURAL, because that is what gets used most even after the station set unlocks. The TAB hint only appears once Orbital Construction is researched, so the station set reads as a discovery rather than a permanently greyed-out tease.

`TAB` is contextual rather than a global keybind - it only means anything with the wheel up, so it costs no key the player might want elsewhere.

#### Station pieces only snap to station pieces

A station is a sealed pressure vessel. If a wooden wall could close a hull run you would get a "sealed" compartment with a plank in it, which is exactly the sort of thing that makes a pressure system feel arbitrary once one is wired up. The socket rules enforce the separation in both directions.

Between station pieces the rules are deliberately **permissive** - edge-to-edge on all four sides plus top and bottom. A station is built in open space with nothing to anchor to, and over-constraining the sockets would make it impossible to close a ring corridor back on itself.

#### Pressure: scoped honestly

The roadmap says airtight station pieces integrate with room pressure and oxygen. That part is **not** claimed here, and the roadmap entry is marked partial rather than ticked.

The reason: the pressure simulation in this codebase (`PressureRules`, `GridRoom`) operates on `GridBlock`. It is a **ship** system and has no concept of world-placed blocks at all. Wiring hammer pieces into it needs a world-side room solver that does not exist yet, and shipping half of one would produce compartments that look sealed and behave like open vacuum - worse than not claiming it.

What ships instead is the honest groundwork: `StationPiece` records the sealing intent per piece (every family seals except the DOCK collar, which is open by design) and keeps a registry ready for that solver.

#### Implementation note

**Tier upgrades re-tag the piece.** The upgrade path destroys and rebuilds the GameObject, so a station hull would silently stop being a station piece the first time it was upgraded from wood to steel. Both the place path and the upgrade path now call the same tagging helper.

#### Manual step in Unity

1. **Tools -> Voxel Engine -> Voxel Engine Setup**.
2. Click **89. Build the Orbital Station Family**.
3. Research **Orbital Construction**.
4. Hold the Hammer, open the build wheel, press **TAB** to reach the ORBITAL STATION set.

### [11.21.0-dev] Prospect From Orbit

**Type:** MINOR - a new payload and its research, save-compatible. No save format change; survey results are derived, not stored.

**GitHub title:** `[11.21.0-dev] Prospect from orbit`

Section 6.6 item 4 - Satellite Network: scan planets for resource deposits. The first entry from 5.0.0 Orbital Expansion.

#### Why this one first

It is the piece that makes three separate systems into one chain. 11.13.0 put satellites in orbit, 11.14.0 gave them sensor payloads, and 11.18.0 buried finite ore deposits that have to be found on foot with a hand scanner. This connects them: a satellite now prospects the ground beneath it, so the orbital programme finally pays back into the industry on the surface instead of only reporting weather.

#### The Resource Scanner

A fourth satellite payload. Where the hand-held Deep Survey Scanner reports **the single nearest deposit** within 1.4 km, an orbital scanner maps **every deposit** within 6 km of the satellite's ground track and ranks them by distance.

| Payload | Capability | Idle |
|---|---|---|
| Sensor Array | Planet-wide season telemetry | 120 W |
| Weather Radar | + live weather and forecast | 220 W |
| Climate Control Array | + weather influence | 260 W |
| Satellite Resource Scanner | Maps deep ore deposits | 340 W |

Deliberately **not** a strict upgrade of the weather tiers. A scanner is a survey instrument with no meteorological hardware at all - it reports seasons like every payload does, but has no radar and no influence. Keeping the branches distinct stops the newest payload from simply being "all of the above", which would retire the other three.

It surveys from the **satellite's** position, not the player's. Reporting what is under the player's feet would make the satellite a pointless middleman for a tool they can already carry; surveying the ground track is what makes the orbit itself matter, and what gives a player a reason to care where they put it.

Exhausted deposits are still listed, greyed out and marked EXHAUSTED, so a player does not fly to one they already drained and conclude the scanner lied to them.

#### It feeds the logistics map

Surveyed deposits now appear on the `L` map as purple triangles, with their own layer toggle and a SURVEYED DEPOSITS section in the sidebar.

Crucially the map shows **only what a scanner has actually seen**, never every deposit in the world. Revealing them all would make the scanner pointless and hand the player a finished prospecting answer for free. What the satellite has surveyed, the map draws - nothing more. With no scanner in service the section says so plainly rather than sitting empty.

#### New research

**Orbital Prospecting** (tier 6), behind Orbital Science. It gets its own node rather than riding along with the existing payloads, because prospecting from orbit is a genuinely different capability from watching the weather, and bundling it would hide it behind a name that does not suggest it exists.

#### Implementation note

**The survey result list is per-instance, not a shared static.** A static scratch list returned to callers is silently overwritten the moment a second scanner surveys, so a caller iterating one scanner's results would start reading another's partway through - the kind of bug that only appears once a player builds their second satellite.

#### Manual step in Unity

1. **Tools -> Voxel Engine -> Voxel Engine Setup**.
2. Click **84. Build the Orbital Programme** again - non-destructive, adds the Resource Scanner and the Orbital Prospecting node without touching anything authored.
3. Research Orbital Prospecting, build a Resource Scanner onto a satellite, and commit it to orbit.
4. Open the payload console for the ranked list, or press `L` to see deposits on the map.

### [11.20.0-dev] Earn The Late Game

**Type:** MINOR - a new system, save-compatible. One additive save list. Existing research nodes are untouched and keep working exactly as before.

**GitHub title:** `[11.20.0-dev] Earn the late game`

Section 6.5 item 11 - the progression half of Mythical Enemies & Bosses: enemy tiers, Boss Relic Cores, and relic-gated research.

#### What already existed

Seven creatures already ship: Basilisk, Ghoul, Griffin, Ifrit, Karkadann, Manticore and Roc, each with its own AI, drops and health bar. The bestiary was not the gap.

The gap was that **beating one meant nothing**. There were no tiers, no relics, and nothing in the tech tree that required defeating anything. The roadmap's own rule - "low-tier farming cannot replace boss progression" - had no mechanism enforcing it.

#### A relic is not a rare drop

A rare drop is a lottery ticket: kill things until the number comes up. That trains grinding, which is exactly what the roadmap forbids. So a Boss Relic Core is:

- **Guaranteed** from its boss. The encounter is the cost, not the RNG.
- **Unique** to that boss. No substituting an easier one.
- **Never consumed.** It is proof, not currency.

That last point is the important one. The relic is recorded in a permanent ledger, and research checks the ledger rather than spending the item. Consuming it would mean a player who researched one node is locked out of a second node needing the same relic - and would have to re-kill a unique boss that may never respawn.

| Relic | Boss | Gates |
|---|---|---|
| Sky Core | Storm Roc | Stellar Engineering (also needs an orbital lab) |
| Petrified Core | Elder Basilisk | Petrification Studies |
| Ember Core | Ifrit Sultan | Ember Forge Mastery |
| Brute Core | Karkadann Tyrant | reserved for heavy structural research |
| Abyss Core | Leviathan | reserved for maritime research |

Stellar Engineering is the roadmap's Star Builder / Dyson Sphere gate: it needs the relic **and** an orbiting satellite laboratory. The relic proves the encounter, the lab proves the infrastructure.

#### Boss variants are separate prefabs

`Boss_Basilisk`, `Boss_Ifrit`, `Boss_Roc` and `Boss_Karkadann` are built from instances of the existing creatures, so they inherit the authored mesh, colliders, AI and ordinary drops exactly - then get scaled up and given a health multiplier. The ordinary creatures are **completely untouched** and still spawn as before.

Health is a multiplier applied at spawn rather than a separately authored number, so a boss and its common version cannot drift apart every time the base creature is retuned.

#### Where the gate lives

`ResearchNode.ScienceCost` is typed to `ScienceItem`, and widening it would have touched every node in the project. A relic requirement is also not really a cost - it is a facility-style prerequisite, the same shape as `requiresOrbitalLab`. So it reuses that gate and plugs into `GetFacilityBlockReason`, which **both** research entry points already funnel through, so the gate cannot be bypassed by the other path.

#### The research UI now explains itself

Worth calling out because it was a pre-existing hole: the orbital gate shipped in 11.13.0 was enforced in the manager but never surfaced in the UI. The button looked live and simply did nothing when pressed. Locked nodes now state the actual requirement - "Requires a Sky Core, recovered by defeating a Roc" - which is the whole difference between a designed gate and an apparent bug. This fixes the orbital gate's presentation too.

#### Implementation note

**The relic is granted in `OnDestroy`, not in an `Update` poll.** `Damageable.Die` calls `Destroy(gameObject)` in the same frame health reaches zero, so a polling check can miss the death entirely - the object is gone before the next tick. `OnDestroy` is the only hook guaranteed to run. It is guarded against scene unload and play-mode exit, since those destroy the object too and must not hand out a relic.

#### Manual step in Unity

1. **Tools -> Voxel Engine -> Voxel Engine Setup**.
2. Click **88. Build Boss Relic Cores**.
3. Boss variants appear in `Resources/Enemies` as `Boss_*` prefabs, ready to place or spawn.

If it reports missing base creatures, run the enemy content steps first and re-run.

### [11.19.0-dev] Keep Them Alive

**Type:** MINOR - a new system, save-compatible. No save format change. Existing animals gain husbandry when the setup step runs; wild herds are unaffected until a pen is built near them.

**GitHub title:** `[11.19.0-dev] Keep them alive`

Section 6.5 item 7 - the livestock half of Fauna / Flora & Livestock.

#### What already existed

Passive fauna shipped some time ago: `PassiveAnimal` already does cows, sheep and pigs with wander-and-flee AI, correct tangent-plane movement on a spherical world, and drops on death. Raw Meat, Hide and Wool items already exist.

So this release deliberately adds **only** the husbandry layer the roadmap asked for - food, water, shelter, health, reproduction and population limits - rather than re-shipping animals that already work.

#### Husbandry is a component, not a new animal

`LivestockHusbandry` attaches to the existing `PassiveAnimal`. A second animal class would have duplicated the AI, the spherical-gravity movement and the drop handling, and left two implementations to keep in step forever.

Being additive has a nice consequence: a wild herd and a farmed herd are the same object with different components, so a player can farm animals they found rather than only animals they bought. An animal with no pen nearby behaves exactly as it always has.

#### Why livestock is worth building next to hunting

Hunting is extractive - kill once, walk further next time. Husbandry is renewable, and critically **milk and wool are produced without death**. Killing the animal ends the income, so the mechanic pushes the player toward keeping animals alive without needing a rule that says so.

| Animal | Renewable product |
|---|---|
| Cow | Milk |
| Sheep | Wool |
| Pig | None - meat and hide only |

#### Needs are a chain, not a timer

An animal that just needs feeding every N minutes is a chore. These needs feed each other:

**Hunger and thirst drive HEALTH. Health gates PRODUCTION. Health and maturity gate BREEDING.**

So a neglected pen degrades visibly rather than failing all at once: production stops first, condition drops, and only then do animals start dying - slowly, at well under one health per second. A player who logs off with a half-full trough comes back to unhappy animals, not a pen of corpses. Hungry animals also visibly slow down, which signals neglect with no UI at all.

Shelter halves consumption and speeds production. That is the entire mechanical argument for building a barn instead of leaving animals in a field.

#### The pen

`LivestockPen` owns the trough for the same reason the rail station owns the cargo hold: the animal moves and is often not where the player is, while the pen is fixed and always addressable. Put the supply on the pen and a factory can belt or pipe feed into it on its own schedule - the player keeps the pen stocked instead of chasing cows.

It feeds, waters, shelters, breeds and harvests everything within 12 m, and its console lists **every animal individually** with its own food, water and condition. A totals-only readout would report a farm as "fine" right until animals started dying, because an average hides the one starving sheep.

**The population cap is the load-bearing rule.** Uncapped breeding wrecks performance and the economy simultaneously. The cap is per-pen, enforced at the moment of breeding, and shown in the header so hitting it reads as a designed limit rather than the feature quietly breaking.

#### Implementation notes

- **Starvation does not use `TakeDamage`.** `PassiveAnimal.TakeDamage` triggers the flee reflex, so routing attrition through it would make a hungry animal bolt every frame and scatter a penned herd. Added `ApplyAttritionDamage` for damage that is not an attack.
- **Newborns are reset.** A calf is cloned from a parent so it inherits the authored prefab exactly, which also means it inherits the parent's age - it would be born adult and instantly breedable without `MarkNewborn()`.
- **Produce is refunded if it cannot be stored.** A weight-capped container can reject what `HasSpace` allowed, so the product goes back to the animal rather than vanishing.
- Feed is matched by item id substring (wheat, corn, grain, hay, biomass, root crops), so the pen works with whatever crops the project has authored and keeps working as more are added.
- **Products are direct serialized references, not a runtime name lookup.** `Item_Wool` lives under `VoxelEngineAssets`, not under a `Resources` folder, so a `Resources.LoadAll` search would have found nothing and stalled every sheep in the game while looking perfectly healthy. Setup step 87 assigns the references directly and only fills in ones that are missing.
- **Milk did not exist.** Wool and Hide were authored by the earlier fauna step but Milk never was, so cows would have filled up with nothing to hand over. The step now authors it beside the other animal products, and skips it if any Milk item already exists anywhere in the project.

#### Manual step in Unity

1. **Tools -> Voxel Engine -> Voxel Engine Setup**.
2. Click **87. Build Livestock Husbandry**. It attaches husbandry to the Cow, Sheep and Pig prefabs and authors the pen. If it reports missing animal prefabs, run the fauna content step first and re-run.
3. Craft a Livestock Pen, place it where animals graze, and put feed and water in it.

Right-click the pen for the herd roster.

### [11.18.0-dev] Something Worth Building On

**Type:** MINOR - a new system, save-compatible. One additive save list, empty until a player actually mines a node. Deposits themselves are derived, so existing worlds already have them.

**GitHub title:** `[11.18.0-dev] Something worth building on`

Section 6.5 item 6 - Caves & Resource Nodes.

#### What was already there, and what was not

Caves already exist: `PlanetField.CaveCarve` carves them into the voxel terrain, sealed under a protective crust and never below the sea. That half of the item was done.

What was missing was the second half - "large, finite ore nodes" that "encourage outpost building". Those two clauses are the same requirement said twice: a node only encourages an outpost if it is worth travelling to **and** worth staying at.

#### A deep node is a different verb, not a bigger vein

The important decision: a deep node **cannot be mined by hand at all**. It sits below the voxel world, is found with an instrument, and is extracted by a powered machine that runs unattended. That is what turns "I found ore" into "I am going to build here" - because the extractor needs power, power needs infrastructure, and infrastructure is an outpost.

It also keeps the existing tools relevant rather than obsolete:

| Tool | Role |
|---|---|
| Hand mining | Immediate, portable, tiny yield |
| Ship drill | Mobile, player-driven, follows visible veins |
| Deep Core Extractor | Fixed, unattended, huge finite yield, needs a base |

#### Where nodes are: derived. What is left: stored.

Node placement is a pure function of (world seed, position), sampled from Worley noise on a 900 m lattice at 30% occupancy - the same technique as the hazard zones in 11.17.0. No spawning, no registry, no streaming, and **every existing world already has deposits** without regenerating anything. Two players on one seed find the same nodes.

Depletion is the one thing that cannot be derived, because it is a record of what the player did. So the save stores exactly one thing: a list of `node key -> amount taken`. An untouched node has no entry, so a fresh world and a legacy save both cost zero bytes. That is the minimum possible save surface for a finite resource.

Ten materials appear, weighted so scarcity survives: iron is roughly ten times more likely than gold, and the rare ores also come in smaller nodes. A uranium find is valuable without being a permanent solution to uranium.

#### Depletion is the design

An infinite extractor would end the resource game the first time one was built. A finite one gives an outpost a **lifespan** - so the player keeps surveying, keeps expanding, and eventually abandons and relocates. That loop is why the roadmap wanted finite nodes rather than just bigger veins.

One subtle correctness point: the extractor takes from the node *first* and banks what was actually granted, rather than producing and then decrementing. Reversed, two extractors on one deposit would each mint the last item. And anything the output buffer refuses is **refunded to the deposit** rather than dropped - silently destroying ore one item at a time is the worst possible bug for a finite resource, because it is invisible.

#### Finding them

A deposit below the crust is invisible by design - if it could be seen from orbit it would be a pickup, not a find. But an invisible resource with no instrument is arbitrary rather than mysterious, so the **Deep Survey Scanner** is the other half of the feature. Carry it and a strip reports the nearest deposit and the distance to it, with a meter that fills as you close. Surveying becomes a metal-detector loop: walk, watch the number, know immediately whether the last step helped. Stand on the deposit and it switches from distance to how much is actually in it.

#### Manual step in Unity

1. **Tools -> Voxel Engine -> Voxel Engine Setup**.
2. Click **86. Build the Deep Core Programme**.
3. Craft a Deep Survey Scanner, carry it, and walk until the readout says HERE.
4. Place a Deep Core Extractor on the deposit and give it power.

Deposits exist in worlds saved before this version - nothing needs regenerating.

### [11.17.0-dev] Nowhere Is Uniformly Safe

**Type:** MINOR - a new system, save-compatible. No new save fields at all; hazard zones are derived, not stored, so existing worlds gain them on load.

**GitHub title:** `[11.17.0-dev] Nowhere is uniformly safe`

Section 6.5 items 5, 8 and 9 - Biome Hazards, Environmental Radiation Zones, Environmental Heat Zones - which are one system, so they ship as one.

#### What was actually missing

The game already had radiation damage, heat damage, hazmat plating, Radiation Shielding and Heat Tolerance upgrades. What it did not have was **anywhere in particular**.

A body had one `radiationLevel` and one surface temperature, so a planet was uniformly lethal or uniformly safe. That makes protection a packing-list item: check the number before you launch, bring the suit, never think about it again. The roadmap asks for hazard ZONES, and a zone is a different design object - it makes a planet something you read as you cross it, with hot spots to route around, and it finally gives the Geiger counter a job.

#### Zones exist without being stored

`HazardField` samples zones from noise seeded by the body's own `genParams.seed`, exactly the way ore veins already work. So:

- **No save data.** A zone is a pure function of (body, position) - identical on every load, identical across a rebuild, and **every existing world gains zones for free**.
- **No spawning, streaming or registry.** A zone can be asked about at any position, including inside chunks that were never loaded.

Same reasoning that put satellites on analytic orbits and trains on a graph: derive it when that is cheap, rather than simulate and store it.

Worley (cellular) noise rather than fractal, for the reason ore veins use it: Worley makes discrete blobs with clear centres and clear gaps, which is what an *avoidable* zone needs. Fractal noise makes a smear the player can never be sure they have left. Coverage is a deliberately low 34% - a zone that is everywhere is just a bigger planet constant wearing a costume.

#### The planet constant became the floor, not the answer

Authored `radiationLevel` still applies everywhere as a baseline, and zones only ever **add** on top. No existing world becomes safer and no authored value is overridden.

Zones also scale with the planet's own character rather than being pasted on:

| Zone | Only appears where | Because |
|---|---|---|
| Radiation | The body has any radiation at all | A clean world should not sprout hot spots it was never authored to have |
| Heat | Surface is above -10 C | A frozen moon does not grow lava fields |
| Toxic | There is real air, and it is not breathable | Toxic air needs air. The dangerous case is a dense atmosphere that is not oxygen |

#### Toxic atmosphere

The new third channel. It is stopped by **sealed air, not plating**: a breathing kit with gas remaining is total protection, and anything less is none. No partial credit on purpose - a half-sealed suit in poison gas is not half-safe.

#### The Geiger counter

A hazard the player cannot detect until they are dying in it is not a feature, it is an ambush. `HazardWarningHud` is the half that makes zones playable, and it shows exactly three things: what the hazard is, how strong it is (a meter that fills *before* the damage gets serious), and **whether the player is actually protected against that specific hazard** - because "radiation, and you are fine" and "radiation, and you are not" are completely different situations that a bare number cannot tell apart.

It only appears for a genuine LOCAL zone. A uniformly irradiated planet is a constant the player already accounts for, and a permanent warning would train them to ignore the strip exactly when a real zone shows up. Only an unprotected reading pulses; the flash is the loudest thing the strip can do, so it is reserved for what can kill you.

The strip reads `PlayerStats.LastHazard` rather than re-sampling the field. Two independent samples of the same noise can disagree at a boundary, and a HUD that says SAFE while the damage path disagrees is worse than no HUD.

#### Also

Deaths from these hazards now name their cause: DIED OF RADIATION EXPOSURE, BURNED ALIVE IN A HEAT ZONE, BREATHED A TOXIC ATMOSPHERE.

No manual Unity step - this release is pure code, and it applies to worlds that already exist.

### [11.16.0-dev] Everything On One Sheet

**Type:** MINOR - a new system, save-compatible. Adds one keybind and bumps the settings version; no save data changes.

**GitHub title:** `[11.16.0-dev] Everything on one sheet`

The Map / Radar UI - section 6.4 Improved Features item 9. Press `L`.

#### The problem it solves

Rail, drones, logistic chests and roads were each built in their own version, and none of them could see the others. The player had four networks and no way to look at them together.

So the map's real job is not decoration - it is **finding the joins that are missing**. The station with no track beside it, the drone port with no power, the base zone nothing serves. Those go in a NEEDS ATTENTION section at the top of the sidebar, because on a mature base that list is the only reason to read the rest.

#### What it shows

| Layer | Drawn as |
|---|---|
| Rail lines | Solid edges walked from the rail graph |
| Rail stations | Squares, with LOAD / UNLOAD / NO TRACK |
| Trains | Filled diamonds - the only moving thing, so the easiest shape to pick out |
| Drone routes | Solid when a drone is flying, faint when merely paired |
| Drone ports | Dots, with READY / IN FLIGHT / NO POWER / DORMANT |
| Base zones | Circles inferred from logistic chest clusters |
| Roads | Faint dotted underlay |

Every layer toggles. A map that cannot be simplified is unreadable on a mature base.

#### Base zones are inferred, not authored

There is no "base" object in this game, so the map has to work one out. A cluster of logistic chests is the honest proxy: it is exactly the thing the logistics layer already treats as one place. Chests within 48 m of any member join the same zone - the same radius a drone port actually serves, so a circle on the map means the same thing as a zone the game genuinely serves rather than a decorative blob.

A single chest is not a base. Two or more is a place worth naming.

#### Scale and reading

Metres, top-down on XZ, same projection as the orbital map so the two read consistently - but deliberately a separate map. They answer different questions and share no scale; zooming one into the other is still open on the roadmap.

The map frames itself on the full extent of everything it found when it opens, so it never opens on empty space. The background grid picks a round spacing for the current zoom and labels it, so distance is readable without a legend. Clicking any sidebar entry centres it - on a large network, finding a named station by dragging is hopeless.

#### Implementation notes

- **`LogisticsMapData` is a separate gathering layer**, for the same reason `OrbitalTrackingService` exists: the painter runs inside `generateVisualContent` and must stay cheap, while gathering walks several registries and allocates. Split, the map repaints on pan and zoom without re-walking the world. Gathering runs on a 0.5 s tick, never per frame.
- **Edges are emitted from one end only.** Rail links and drone pairings are both symmetric, so without a hash tie-break every line draws twice.
- **Labels are pooled `Label` elements**, not `MeshGenerationContext.DrawText` - the lesson recorded when the orbital map shipped.
- Roads are drawn as per-cell marks rather than traced runs. The road layer has no edge list, and building one here would duplicate work the road system deliberately does not do.

#### Controls

`L` opens and closes the map, rebindable in Settings -> Controls. Settings version 18; the existing migration fills the new binding in on old profiles without touching anything the player rebound.

No manual Unity step - this release is pure code.

### [11.15.0-dev] The Permanent Way

**Type:** MINOR - a new system, save-compatible. All new save fields are additive; a world with no rail behaves exactly as before.

**GitHub title:** `[11.15.0-dev] The permanent way`

The Train System - section 6.4 item 1, and the last unbuilt entry in that block.

#### Why a railway is not just a road

The codebase already has belts for short haul, drones for point-to-point, and roads for driving. A railway had to be a fourth distinct answer, not a reskin of one of them, or it would not be worth building. The line that makes it distinct:

**A train is a scheduled agent that walks a graph, not a vehicle that drives on a surface.**

A road is queried by position - "what am I standing on". A rail is walked by topology - "what comes next". That one difference buys the property that makes bulk haul belong on rails: **a train keeps running while its chunks are unloaded**, because walking a graph costs nothing and needs no colliders. A rover cannot do that.

This is the same reasoning that put satellites on analytic orbits in 11.13.0. Anything the player expects to keep working while they are elsewhere must not depend on being simulated.

#### The permanent way

`RailTrack` is a `PlacedBlock`, so mining, damage, saves and the inspection overlay already work on it. It auto-connects to orthogonal neighbours like the road's neighbour mask, so the player lays cells and the line forms itself with no shape to pick.

Two rules stop it being a road with extra steps:

- **Gradient.** Rail refuses a slope a road would happily drape over. A railway that climbs anything is just an expensive road, so the refusal is what makes the player cut, fill and route around terrain. The rule lives in the graph itself: too-steep cells simply do not connect, rather than connecting and then failing mysteriously when a train tries it.
- **Degree.** Plain track holds at most two connections. Three or four requires a switch, and a buffer holds one. The network stays a set of lines rather than an undifferentiated mesh, which is what makes routing meaningful.

#### Pathfinding

`RailNetwork` is a hash-grid registry (mirroring `RoadSurfaceUtility`) plus A* over the cell graph - the roadmap's "A* for trains on rail graph" item.

A* rather than the road system's corridor solver because a railway is already a sparse graph with explicit edges, which is exactly the shape A* wants, whereas roads are a dense surface that must be traced first. Reusing the road planner would mean rediscovering topology the rail graph already knows.

Edge cost is real distance divided by the cell's speed multiplier, so the planner prefers good line over a marginally shorter bad one. The heuristic is straight-line distance, which is admissible because no edge is ever shorter than the gap it spans - so the result is a genuine shortest path, not merely a path.

#### Stations and schedules

A station owns the cargo hold, not the train. A train is in motion and often unloaded; a station is fixed and always addressable. Putting the buffer at the station lets the factory either side fill or drain it on its own schedule, so the train only has to show up - which is what makes a railway asynchronous instead of something the player babysits.

Stations are matched **by name, not by reference**, the convention the drone ports already set here. A schedule that names "North Pit" keeps working after the player demolishes and rebuilds that station.

| Role | Effect |
|---|---|
| LOAD | Station hold to train |
| UNLOAD | Train to station hold |
| PASSING | Timing point, no cargo moves |

Transfers put back anything the destination refuses, so a full train never destroys cargo. A dwell cap stops a jammed or mis-filtered station stranding a train forever on an order that can never complete.

#### Driving feel

Acceleration is deliberately low - mass is what a train is for. Arrival uses `v = sqrt(2as)`, the fastest speed from which the train can still stop in the distance remaining, so it brakes into a platform instead of overshooting it.

#### Switches

Right-click a switch to set the points. One detail worth calling out: a train arriving from the leg the points happen to be set to takes the next available route instead, so a switch can never bounce a train straight back the way it came.

#### Persistence

Station names, station roles and switch settings all survive a reload.

Switch settings are re-applied **one frame after load**, deliberately. A switch clamps its selection against its live link list, and that list is still filling while neighbouring track instantiates - applying immediately would clamp against a partial list and silently change the player's routing.

#### Manual step in Unity

1. **Tools -> Voxel Engine -> Voxel Engine Setup**.
2. Click **85. Build the Rail System**.
3. Lay track between two sites, keeping the gradient gentle.
4. Place a station beside the track at each end. Right-click each to name it and set LOAD or UNLOAD.
5. Place a locomotive on the track. Right-click it, add both stops, press START SCHEDULE.

Right-click any station, train or switch to open its console.

### [11.14.0-dev] Reasons To Launch

**Type:** MINOR - new systems, save-compatible. All new save fields are additive; a world with no satellites behaves exactly as before.

**GitHub title:** `[11.14.0-dev] Reasons to launch`

Phase two of the Orbital Programme: the satellite payloads that make a launch worth doing, plus the UI change requested for the orbital map.

#### Orbital Systems is now its own box

The orbital map was sharing the LIFE SUPPORT card. It now has a dedicated ORBITAL SYSTEMS box directly below it, which is also more useful: instead of a single status line it shows the device name, its capability tier, its tracking range, and the open-map prompt. PERSONAL SYSTEMS reads 4 MODULES.

#### Satellite payloads

Three blocks, one component, escalating capability. Each requires the host construct to be classified a SATELLITE and committed to orbit - the same rule the research station uses, so there is only one requirement to learn for all orbital hardware.

| Payload | Capability | Idle | Active |
|---|---|---|---|
| Satellite Sensor Array | Planet-wide season telemetry | 120 W | - |
| Satellite Weather Radar | Adds live weather and forecast | 220 W | - |
| Satellite Climate Control Array | Adds weather influence | 260 W | 2660 W |

The sensor tier is the quiet but real win: season data for a planet **without standing on it**. Temperature, solar multiplier, wind multiplier, days remaining, next season, forecast.

The radar tier is honest about its limit. The weather simulation only runs for the body the player is actually at, so rather than fabricating a remote planet's live sky, the panel says exactly that and points at the season telemetry, which genuinely is planet-wide.

#### Weather influence, not weather command

Climate Control shifts the odds of the next weather change. It does not set the sky.

The hook is a single point: `PickNextState` is the one place `WeatherManager` chooses what comes next, so biasing the roll and scaling storm chance there covers every climate path - temperate, snow, and no-precipitation worlds - without touching the individual branches.

Two properties make this a system rather than a cheat:

- **Diminishing returns.** Satellite influence is combined through `1 - e^-total`, so each additional satellite adds less than the last and the total can never quite reach 1. A constellation steers a climate; it can never lock one. Every weather outcome stays reachable.
- **It costs.** An active climate array draws 2.66 kW. Steering an atmosphere should hurt.

Directives are MONITOR, SUPPRESS and ENCOURAGE, and the chosen directive is a standing order, so it persists through a save/load rather than quietly reverting.

#### New research

- **Climate Engineering** (tier 6) unlocks the climate array - and is itself flagged `requiresOrbitalLab`. You must already have a working satellite in orbit before you can research the ability to steer weather from orbit. This is the first shipped node that uses the orbital gate added last release, so the gate now has a real consumer rather than only a mechanism.
- The Sensor Array and Weather Radar ride along with **Orbital Science**, so getting a lab up also gets you something to point at the planet.

#### Block consoles

Both the payloads and the research station now have panels. When one is offline it states **which** requirement is missing (off / no power / not a satellite / not in orbit) and how to fix it, because a silent dead panel on a block whose entire purpose is its requirements would just read as a bug.

#### Manual step in Unity

1. **Tools -> Voxel Engine -> Voxel Engine Setup**.
2. Click **84. Build the Orbital Programme** again - it is non-destructive and will add the three new payload blocks and the Climate Engineering node without touching anything authored.
3. Research Orbital Science, build a Sensor Array or Weather Radar onto a satellite, and commit it to orbit.
4. For weather control: put a Satellite Research Station in orbit first, research Climate Engineering there, then build the Climate Control Array.

### [11.13.1-dev] Compile Fixes And Conventional Keys

**Type:** PATCH - compile fixes and a keybind correction. No save impact.

**GitHub title:** `[11.13.1-dev] Compile fixes and conventional keys`

#### Fixed

- **`OrbitalTrackingService.cs` (158, 189) CS1061 - `SunSettings` has no `name`.** `SunSettings` is a plain serializable class, not a `ScriptableObject`, so it never had the implicit `.name` that Unity objects carry. The correct field is `displayName`. Both the sun's own map entry and the fallback parent name for planets now read it.

- **`GridIdentityHud.cs` (187-188) and `OrbitalMapScreen.cs` (228-229) CS0104 - ambiguous `Cursor`.** `UnityEngine.UIElements` declares its own `Cursor` type, so in a file that pulls in both namespaces the bare name is ambiguous.

  Fixed with `using Cursor = UnityEngine.Cursor;` rather than fully-qualifying each use site. This is not an arbitrary choice - it is the convention the codebase already uses: `InGamePauseMenu.cs` has had exactly this alias for the same reason. Matching it keeps the two files consistent with the existing pattern instead of introducing a second style.

#### Changed - keybinds

`N` is the conventional rename key and the construct registry is fundamentally a rename dialog, so the two have been swapped:

| Action | Was | Now |
|---|---|---|
| Construct registry (rename/classify) | `U` | **`N`**, and **`F2`** |
| Warp Drive | `N` | **`U`** |

`F2` opens the registry as well. It is deliberately a fixed convention rather than a second rebindable binding: F2-to-rename is a platform idiom on Windows and in most file managers and editors, not a user preference, so it should always work regardless of what the primary key is bound to.

Settings version bumped to 17 with a targeted migration. Profiles still sitting on the old defaults move to the new ones; a player who deliberately rebound either key keeps their choice, since the migration only rewrites a binding that still holds the exact previous default.

#### Manual step in Unity

None.

### [11.13.0-dev] Name Your Fleet, Own The Sky

**Type:** MINOR - new systems, save-compatible. Grids without an identity, and every legacy save, load and behave exactly as before. The keybind table gains two entries and migrates itself.

**GitHub title:** `[11.13.0-dev] Name your fleet, own the sky`

This is phase one of the Orbital Programme. It delivers the foundation: construct naming and classification, the orbital map device, on-rails orbits for stations and satellites, and the orbital research gate. Satellite sensor payloads (season and weather tracking, weather influence) follow in the next release, and they are built on exactly the layers added here.

#### Construct registry - naming and classification

Every grid can now be named and classified as a VESSEL, SATELLITE or STATION. Press `U` while piloting.

A satellite is not a separate entity type, as requested: it is an ordinary player-built grid wearing a different label. That declaration is what the rest of the systems key off.

Naming lives in its own `GridIdentity` component rather than as fields on `GridEntity`, for three reasons: the physics class does not grow a UI concern, a grid that was never named costs nothing at all, and an unnamed grid still reports a stable generated designation (`LC-4471`) so the map never draws a blank label. A static registry keeps the map and the satellite services off `FindObjectsByType`.

#### The orbital map

`M` opens a full-system view of every celestial body and every named construct, with live telemetry: apoapsis, periapsis, orbital period, inclination, and a plain-language state word (ORBITING, DRIFTING, SUBORBITAL, ESCAPING, IN FLIGHT).

**The map is a device, not a menu.** It requires an Orbital Map equipped in the new personal instrument slot, which sits in the LIFE SUPPORT card next to the helmet and oxygen tank as requested. The item is expensive (12 steel + 24 copper wire at the Assembler) and gated behind the Orbital Telemetry research node, so the first one is a real milestone.

The device also defines how good the map is - tracking range, whether full telemetry shows, whether orbit ellipses draw, whether focus switching is allowed. That gives the tier ladder somewhere to go later without any new UI.

Rendering notes:

- Orbits and bodies are drawn by a single `generateVisualContent` painter, one mesh per frame, so a system with dozens of tracked objects stays cheap.
- Ellipses are offset by their focal distance so the parent body sits at a **focus** of the ellipse rather than its centre. That is the visual signature of a real orbit and the main reason the view reads like a map instead of a diagram.
- Name labels are pooled `Label` elements in an overlay rather than `MeshGenerationContext.DrawText`, which needs a font resolved at paint time and is not dependable across Unity versions.
- Out-of-range contacts are listed and flagged rather than hidden, so the player can see that a better instrument would reach them.

#### On-rails orbits

A construct classified as a satellite or station can be committed to orbit from the registry panel.

**A rigidbody cannot hold an orbit.** Float precision, a fixed timestep and the fact that an unloaded chunk stops simulating all mean a physics-integrated satellite will decay, drift, or simply stop existing while the player is away. That is fatal for a feature whose whole promise is "put a station up and it stays there".

So a committed construct switches to the same Keplerian propagation the planets and moons already use. It becomes kinematic and is placed from solved elements each frame. It cannot decay, it keeps its orbit while streamed out, and the map can draw an exact ellipse instead of guessing.

Commitment is validated rather than forced. The game refuses to freeze a construct that is inside an atmosphere, below a minimum altitude, on a suborbital path, or already escaping - and says which, instead of silently circularising and stealing the player's intent. Any release hands the construct back to physics **with the exact velocity its orbit implies**, so leaving orbit is continuous rather than a dead stop.

The state-vector to classical-elements conversion handles the degenerate cases properly: equatorial orbits (undefined ascending node) and circular orbits (undefined periapsis) are both special-cased rather than producing NaN. The mean anomaly is rewound to the simulation epoch on commit, without which the station would visibly jump the moment it was committed.

#### Orbital research gate

Research nodes gain a `requiresOrbitalLab` flag. A flagged node can only be researched at a **Satellite Research Station** aboard a construct classified as a satellite and actually in orbit.

That single rule is what turns the orbital programme from decoration into progression: to finish the tech tree you have to build a satellite, get it up, and keep it there.

The gate is enforced in `ResearchManager` on both entry paths - timed lab research and the instant inventory path - because a gate enforced in only one place is a gate that eventually leaks. When research is unavailable the game reports the blocker from the lab **closest to working**, so the player is told the last thing standing in their way ("Host satellite must be committed to a stable orbit") rather than an arbitrary complaint.

#### Persistence

Names, classifications and committed orbits all survive a save/load round trip.

An orbit is saved as its **Keplerian elements, not as a pose**. A station has to come back on the same orbit at the correct phase for the reload time, which a frozen position could never express. Orbits are restored after the grid's blocks, so the hull has its real mass and bounds before it is parked on rails.

All fields are additive. A save with no identity restores a componentless, unnamed grid exactly as before.

#### Keybinds

| Key | Action |
|---|---|
| `M` | Orbital map (requires the device) |
| `U` | Construct registry, while piloting |

`M` was reserved for the star map in the roadmap and this is that feature, so it takes the key. `U` was chosen after checking the table - `N` is the Warp Drive, `T` is Tool Cycle, `G` is Build Toggle Grid. Both are rebindable; settings version 16.

#### Manual step in Unity

1. Open **Tools -> Voxel Engine -> Voxel Engine Setup**.
2. Click **84. Build the Orbital Programme**.
3. Research **Orbital Telemetry**, craft an **Orbital Map**, equip it in the instrument slot in your LIFE SUPPORT panel.
4. Press `M`.
5. To put something up: build a grid, fly it to a stable orbit, press `U`, name it, classify it as SATELLITE or STATION, then COMMIT TO ORBIT.
6. To gate a research node behind orbit, tick **Requires Orbital Lab** on that node.

### [11.12.1-dev] Unity API Deprecations

**Type:** PATCH — compile fix and warning cleanup. No behaviour change, no save impact.

**GitHub title:** `[11.12.1-dev] Unity API deprecations`

#### Fixed

- **`TransmissionTower.cs` (141) CS0619 x2 — `Object.GetInstanceID()` is now an error.** The span cable used the two towers' instance ids to decide which end draws the shared line, so a pair is never drawn twice.

  Unity suggests `GetEntityId()`, but that only exists on the newest editor and swapping to it would break the project on any older version. The tie-break never actually needed an engine id — it only needs a value that is unique and stable per instance. It now uses a private monotonic serial assigned lazily on first use, which is version-proof and does the same job.

- **`DroneNetwork.cs` (48) and `LogisticsNetwork.cs` (58) CS0618 — `FindFirstObjectByType<T>()` is deprecated.** Both are singleton guards checking whether an instance already exists, so ordering is irrelevant and `FindAnyObjectByType<T>()` is the correct replacement (it is also the faster of the two).

These were the only three occurrences of either API in the codebase.

#### Manual step in Unity

None.

### [11.12.0-dev] Where You Will Actually Land

**Type:** MINOR — a new pilot system. Save-compatible: no block, item or save field changes. The keybind table gains one entry and migrates itself.

**GitHub title:** `[11.12.0-dev] Where you will actually land`

#### Why this round

Section 6.4 item 6, the Trajectory Camera, was the only entry in that block still marked PARTIAL. The cockpit already had an orbital flight computer reading out PE/AP, tangential and circular speed. What it could not tell you was the one thing a pilot actually wants on the way down: **where the ship touches the ground, and how hard**.

#### Two different questions, two different solvers

The existing `OrbitalTelemetry` solves a two-body conic analytically. That is correct for "what orbit am I on" and it stays exactly as it was.

The new `TrajectoryPredictor` answers a different question by forward-integrating the coast path and then probing the world:

- It steps the **same forces `GridEntity` applies in FixedUpdate** — scaled radial gravity on a planet, the flat-world fallback off one, and the atmospheric drag model using the identical block-count frontal-area estimate. The predicted curve therefore matches the flight the ship will actually have, instead of a vacuum parabola that lies inside an atmosphere.
- Semi-implicit Euler, 0.25 s steps, 45 s horizon. Same integrator family as the physics step, and unlike explicit Euler it does not spiral a circular orbit outwards.
- Each segment is raycast against the world, so the impact point is real terrain rather than an assumed sphere.

It never touches the rigidbody — it reads state, integrates a copy, and reports.

#### Performance

A coasting ship re-solves to the same curve every frame, so the solution is cached and only rebuilt when the motion actually changed. Velocity change is the dirty signal, because thrust, gravity turns and collisions all move the velocity. Hard floor of 0.05 s between solves, hard ceiling of 0.5 s before a refresh. One `LineRenderer` and one marker exist for the whole game, hidden rather than destroyed.

#### The overlay

Gated in three stages, in order: the toggle is on, the player is piloting, and the camera is in the **wide exterior view** — the second zoom-out. First-person and the tight chase view stay clean, which is what the roadmap asked for.

| Outcome | Line colour | HUD text |
|---|---|---|
| Impact | Red | `IMPACT IN 4.2s · 88 m/s` |
| Will orbit | Green | `PATH CLEAR · WILL ORBIT` |
| Leaving the well | Violet | `PATH CLEAR · LEAVING WELL` |
| Clear, still climbing | Blue | `PATH CLEAR` |

The line fades toward its far end, because the prediction is least trustworthy the further out it runs and should not claim equal confidence along its whole length. The impact marker is scaled by camera distance so it stays readable from 500 m up, and pulses so it reads as a warning rather than scenery.

While the path is on screen, the cockpit trajectory module swaps its apsis row for the plain-language impact readout — more useful than a second copy of the conic.

#### Two details that would otherwise have been bugs

- The path raycast **ignores the grid it belongs to** and the pilot. Without that filter every prediction reports an instant impact with the ship it is predicting for. Probing is also suppressed until the path has cleared the hull.
- The impact marker has **no collider** — a marker with one would be hit by the very raycasts that produced it.

#### Keybind

`J` toggles the trajectory camera, rebindable in Settings. The roadmap suggested `T`, but `T` is already Tool Cycle, so `J` was taken instead — free, and next to `K` for the Grid Inspector. The settings version bumped to 15, so existing profiles pick the new bind up automatically without losing custom binds.

#### Manual step in Unity

None. No prefab, item, recipe or research is involved — this is script-only and works as soon as it compiles.

To try it: sit in a cockpit, scroll out twice, and fly. Press `J` if you want it off.

### [11.11.0-dev] The Grid Reaches Out

**Type:** MINOR — a new power block. Save-compatible: nothing existing changes shape, and a world with no towers behaves exactly as before.

**GitHub title:** `[11.11.0-dev] The grid reaches out`

#### Why this round

Section 6.4 item 4, the last untouched entry in the Logistics 2.0 block: long-distance power poles.

Cables only link one grid step at a time. That is the right rule for a base — it keeps wiring readable and stops power tunnelling through walls — but it meant a remote site could only be powered by dragging a cable run across the world one block at a time. The drone ports made this worse rather than better: they link over 400 m and need power at BOTH ends, so the logistics reach had outgrown the power reach.

#### The tower

Two Transmission Towers within 128 m link automatically and carry the network between them. A remote outpost joins the home grid with two blocks instead of a few hundred cables.

| Property | Value |
|---|---|
| Span range | 128 m, tower to tower |
| Span capacity | 20 kW |
| Local tap | 4 m, picks up cables and machines at its own base |
| Max spans | 3 — two makes a line, three makes a junction |

#### How it fits the existing power layer

A tower is just a `PowerNode`. The power layer already merges everything reachable into one network and prices it by its weakest link, so a tower only has to do two things: be a node, and declare long edges. Generation, storage, bottleneck maths and every existing power readout keep working untouched.

The span is registered as a **manual link**, which is the mechanism the code already had for an intentional long-range edge. This matters: `CanLinkTo` short-circuits on a manual link before its distance and line-of-sight checks, so a span crosses 128 m of terrain without the tower having to opt out of the rules that keep ordinary cables honest.

The local tap is deliberately kept at 4 m rather than widening `connectRadius` to the span range. Widening it would make a tower hoover up every machine within 128 m, which is not what a pylon does.

The span is capacity-rated at 20 kW and takes part in the normal bottleneck rule, so a thin cable feeding the tower is still the limit. A tower line is wide, not infinite.

#### Visuals

A lattice pylon — four splayed legs, a mast, two cross-arms — with a hanging catenary cable drawn between spanned towers. Each pair is drawn by exactly one end, so the line is never doubled up.

#### Re-spanning

Placing or removing a tower re-spans the whole set, because a new tower can change which pairs are nearest and a removed one frees a slot on its partners. A re-entrancy guard makes a burst of towers streaming in cost one pass rather than one per tower.

#### Manual step in Unity

1. Open **Tools -> Voxel Engine -> Voxel Engine Setup**.
2. Click **83. Build the Transmission Tower**.
3. Crafted at the Crafting Bench from 8 steel ingots and 4 copper wire.
4. Place one near your generators and another within 128 m, at the remote site.
5. Run a short cable from each tower to the local machines. The two grids are now one.

### [11.10.0-dev] Landed, Not Teleported

**Type:** MINOR — chest weight limits, plus three fixes. Save-compatible: the weight field is additive and defaults to the world limit every chest already used.

**GitHub title:** `[11.10.0-dev] Landed, not teleported`

#### 1. Items arrived out of sync with the drone

The flight timer covers the whole ROUND TRIP, but the handover was wired to the end of it. So the drone reached the destination at the halfway mark, visibly released its crate, flew all the way home — and only then did the items appear. The transfer was correct; its timing was not.

Touchdown is now its own event. `TickFlight` reports the moment half the round trip has elapsed, which is exactly where the visual drone releases its crate, and the network hands the cargo over there. `BeginReturnLeg` then lets the drone fly home empty instead of ending the trip on the spot. A `CargoDelivered` flag makes the handover strictly once-only, and it is recomputed on load so a save taken after touchdown cannot deliver the same payload twice.

#### 2. The wireless panel duplicated itself

Adding a request appended a second WIRELESS LOGISTICS block below the first. The rebuild re-inserted the panel by remembered index, and that index was no longer valid once the tree had changed. The panel now lives in a fixed container that is cleared and refilled, so it cannot be inserted twice by construction.

#### 3. Drone-reachable items read as "none in range"

A request supplied by drone was reported as unavailable, because the readout only measured the 48 m wireless radius. A drone route is a supply line too. `AvailableByDroneFor` now totals what linked ports can reach, and such an item shows a blue dot and "N by drone" instead of an amber "none in range". Genuinely unreachable items are unchanged.

#### 4. Chests have weight limits

`ItemContainer` already enforced a weight limit; chests simply used the world default. The three logistic chests now carry their own, which gives them distinct roles beyond their slot counts:

| Chest | Limit | Why |
|---|---|---|
| Provider | 1200 kg | a loading bay — pipes fill it fast |
| Buffer | 900 kg | local working stock for an outpost |
| Requester | 600 kg | a delivery shelf at the point of use |

A limit of 0 means "use the world default", so every ordinary chest is untouched. Setup only fills an UNSET limit, so a value you tune by hand is never overwritten. The panel shows Load in kg with a bar that turns amber past 85% and red at FULL — which is what lets a chest that has stopped accepting deliveries explain itself instead of looking broken.

The limit is reapplied after a reload, since a restored container is a fresh object and would otherwise revert to the world default.

#### Manual step in Unity

Run **78. Build the Logistic Chests** again to stamp the weight limits onto the three prefabs. Chests already placed keep whatever their prefab gives them.

### [11.9.0-dev] Heavy Lift

**Type:** MINOR — drone upgrades and a new drone model, plus two persistence fixes. Save-compatible: every new saved field is additive and a world written before this round loads with sensible defaults.

**GitHub title:** `[11.9.0-dev] Heavy lift`

#### 1. Request lists were never saved

A requester came back from a reload asking for nothing. The cause was in the persistence layer, not the chest: both the capture and the restore went straight to `ItemPortRouting`, which knows about faces and filters but nothing about the wireless request list or a buffer's stock target. `Chest.CapturePortSnapshot` and `Chest.ApplyPortSnapshot` were written to carry both and were simply never called.

Persistence now prefers the chest's own capture and restore when the block has a `Chest`, falling back to the routing component for every other machine. Requests and buffer targets survive a reload.

#### 2. Drone ports had no persistence at all

Everything about a port reset on load: its name, its visual toggle, its tuning, its lifetime counters — and, far worse, **any flight in progress**. The cargo leaves the source chests at takeoff, so a save that caught a drone mid-air would have destroyed those items on reload.

Ports now save by position and re-bind by proximity, the same model the refuel pads use. The in-flight manifest is saved with them and the flight resumes exactly where it left off, so nothing is lost. Restore runs in two passes because a manifest can only be re-bound once every port exists.

#### 3. The drone looks the part

Rebuilt from the reference: a white composite fuselage with a raised carbon spine, **eight arms in a radial ring** each with a motor can and a two-blade rotor, twin landing skids on four legs, and a gimballed pod at the nose. The slung cargo crate still tints itself to what it is carrying and still disappears on the empty return leg. Still primitives, so no art asset is needed, and still collider-free.

#### 4. Upgradable drones

The ports take the **universal modules that already exist** (setup step 75), so there is no second upgrade economy to learn:

| Module | Header in the panel | Effect on the drone |
|---|---|---|
| Machine Speed Module | **DRONE UPGRADES** | flies faster (x1.25 per module) |
| Machine Efficiency Module | **DRONE UPGRADES** | carries more (x1.25 payload per module) |

Efficiency is inverted deliberately. On a machine it is a power multiplier below 1 (x0.8 = draws less); for a drone that reads as "each item costs you less to carry", so capacity scales by 1 divided by it. A x0.8 module gives x1.25 payload, which matches the speed module's feel rather than inventing a new curve.

Two slots per port, they stack, and the panel shows the upgraded figure with the base value beside it in green so the gain is visible. Round-trip time is priced from the upgraded speed and the payload from the upgraded capacity.

#### 5. The drone flies chest to chest

It flew port to port, which is not where the items are. The ports are the relay that makes the trip legal, but the cargo leaves a chest and arrives in a chest, so the drone visibly started and ended in the wrong place.

The network now reports which chest the payload was actually drawn from and predicts which chest will receive it, and the drone flies that route. The delivery target is a prediction only — the real destination is decided on landing, so if the situation has changed the items still go wherever they fit. The flight remains presentation-only in every case.

#### Manual step in Unity

No new setup step. If the upgrade modules are not in your world yet, run **75. Author Universal Machine Upgrade Modules** — the same modules the Electric Furnace and Oil Refinery use. Then right-click a Drone Port and drop them into the two DRONE UPGRADES slots.

### [11.8.0-dev] The Drone You Can Watch

**Type:** MINOR — a new visual system plus two bug fixes. Save-compatible.

**GitHub title:** `[11.8.0-dev] The drone you can watch`

#### 1. Filters did not refresh the wireless panel

Adding an item filter to a face updated that face's card and nothing else. The WIRELESS LOGISTICS box above it is built from the same state, so it kept showing stale numbers until the screen was closed and reopened.

The panel is now rebuilt in place whenever a face card changes, and the reverse is wired too: a request added or removed inside the logistics box restates every face card. The two halves of the screen can no longer disagree, and neither rebuild disturbs scroll position.

#### 2. The drone ports did not really have 400 m of range

They advertised 400 m, and the check was correct — but it could never be reached. The world streams: chunks are 32 m and the view distance is 6 to 8 of them, so anything past roughly 192 to 256 m is unloaded, and the blocks inside it are disabled. The port deregistered itself in `OnDisable`, so the far end of a long route simply vanished from the network well before 400 m.

Registration now lasts until the block is genuinely destroyed, so a route survives its far end being streamed out. Distance is measured from a remembered `NetworkPosition`, which stays meaningful while a port is unloaded.

What a streamed-out port cannot do is touch its chests, because they are not in memory — so it is reported as **dormant** rather than quietly dropped. The route is kept and resumes the moment the chunk loads. This is an honest limit of a streaming world rather than something a logistics system can paper over, and the panel now says so in plain words instead of leaving the player to guess.

#### 3. The transport drone

A visible drone now flies the route: it climbs away from the source, arcs to the destination carrying a crate tinted to its cargo, lands, and returns empty with its crate hidden. It is assembled from primitives, so it needs no art asset, and its colliders are stripped — it cannot bump the player or a vehicle.

It is deliberately **presentation only**. The delivery is already decided and paid for at dispatch: the cargo has left the source chests and the landing is on a timer. The drone reads the port's own `FlightProgress` rather than keeping a clock, so the model can never drift from the delivery. This matters — if the simulation waited on the model, a drone that clipped terrain or streamed out would strand real items.

Each port has its own **DRONE VISIBLE / DRONE HIDDEN** toggle in its panel, as asked. Switching it off changes nothing about the logistics.

#### 4. Unpowered links are named

A link that exists but cannot work was previously silent. The panel now reports "One linked port has no power. It cannot send or receive until it does.", pluralising properly when several are affected, and separately reports dormant ports.

#### Known limitation

`showDrone` and a renamed `portName` are runtime values and are **not yet persisted** — a reloaded world returns both to their defaults. The flight state is likewise not saved, so a drone airborne at save time completes on load rather than resuming mid-air. Worth fixing in a later round; called out here so it is not mistaken for a bug.

#### No Unity step

No setup step: recompile and the changes apply to ports already placed.

### [11.7.1-dev] The Range Readout Tells The Truth

**Type:** PATCH — fixes a misleading readout. No behaviour change to transfers, no save format change.

**GitHub title:** `[11.7.1-dev] The range readout tells the truth`

#### What was wrong

The WIRELESS LOGISTICS line on a logistic chest counted every provider and requester **in the world**, with no distance test at all. Walking a chest to the far side of the map still reported its partners as present, which read as "everything is in range" and made the 48 m limit look broken or ignored.

The limit was never actually broken. `FindNearestProviderWith` and `AvailableFor` both applied it correctly, so items only ever moved within 48 m — but the panel said otherwise, and the panel is what the player believes. The per-item "none in range" status was the only honest number on the screen.

#### The fix

`ProvidersInRangeOf` and `RequestersInRangeOf` are new, and they apply exactly the same distance and buffer-to-buffer rules as the fulfilment pass, so the readout and the transfer can no longer disagree. The panel now reports "2 providers · 1 requester **in range**", and turns amber when the count that matters for this chest's role is zero.

The world total is not discarded, just relabelled. When a chest has no partner in range but partners exist elsewhere, a second line says so and names the range — so "I built one but it is too far away" is distinguishable from "I never built one", and it points at the Drone Ports as the way to bridge the distance.

`ProviderCount` and `RequesterCount` keep their world-wide meaning and are now documented as such, with a warning not to use them for anything shown on a single chest's panel. That was the trap this bug fell into.

#### No Unity step

Recompile and reopen a chest panel.

### [11.7.0-dev] The Drone Port

**Type:** MINOR — a new block and a new transport layer. Save-compatible: nothing existing changes shape, and a world with no drone ports behaves exactly as before.

**GitHub title:** `[11.7.0-dev] The drone port`

#### Why this round

`LogisticsNetwork` answers a request instantly, but only within 48 m. That radius is deliberate — stock teleporting across a whole world would make distance meaningless — but it left the last open item in the section 6.4 logistics line: an outpost further away than that could not be supplied at all, so the player hand-carried everything.

A pair of Drone Ports bridges the gap without dissolving distance. Two ports link over 400 m and a drone physically flies the difference: it loads, takes real time in transit, lands, unloads and returns. Long-range supply costs power, a round trip and a pair of blocks instead of being free.

#### A bridge, not a third kind of storage

A Drone Port holds no inventory. It serves the logistic chests already within its own 48 m service radius, so a port is a bridge between two local networks and the player keeps using the one storage concept they already learned: a chest asks, the network answers.

The dispatch pass runs from the destination end and is demand-driven. A port looks at what the requesters around it still want, and — importantly — skips anything the local wireless network can already supply. A drone is only ever launched against a real shortfall, so stock that could have been delivered locally never takes a flight it did not need. The source is then the nearest linked port whose own providers hold the item.

#### Cargo is never destroyed

The payload is removed from the source chests at takeoff, so for the whole flight it exists in exactly one place. On landing it is offered to the destination's requesters, then to any local chest with room. If it still cannot be placed it is flown home and stored there. Only if BOTH ends are full does the drone hold the cargo, log a warning and retry every five seconds until space appears. There is no path on which items silently vanish.

#### Power

Idle 20 W, plus 140 W while a drone of that port is airborne, and the draw follows the flight state. Power gates dispatch only: a drone already in the air completes its trip, so a brown-out strands nothing permanently. Both ends must be powered for a launch, since the destination pays the cost of receiving.

#### Tuning

| Value | Default |
|---|---|
| Link range | 400 m |
| Service radius (chests served) | 48 m, matching the wireless network |
| Payload | 64 items per round trip |
| Drone speed | 18 m/s |
| Handling | 2 s at each end |
| Dispatch tick | every 2 s |

All of these are per-port fields, so a placed port can be tuned without touching code, and setup never resets an authored value.

#### Manual step in Unity

1. Open **Tools -> Voxel Engine -> Voxel Engine Setup**.
2. Click **82. Build the Drone Port**.
3. The block is crafted at the Assembler from 10 steel ingots, 4 circuits and 6 copper wire.
4. Build **two** ports, one at each site, within 400 m of each other, and give both power.
5. Put logistic chests within 48 m of each port: providers near the source, requesters near the destination.
6. Right-click a port to see its link count, the current flight with a progress bar, and lifetime trips and items delivered.

### [11.6.0-dev] The Buffer Chest

**Type:** MINOR — a new block and a new chest role. Save-compatible: the new lock mode and the new saved field are additive, and every existing chest keeps the role and the ports it already had.

**GitHub title:** `[11.6.0-dev] The buffer chest`

#### Why this round

The Logistic Chests line in section 6.4 had one item still open: the Buffer Chest, the hybrid of the other two. A Provider only gives and a Requester only takes, so a remote outpost had to reach across the whole base for every single item it consumed. A Buffer holds local working stock: it keeps itself topped up from the providers, and other requesters draw from it.

#### The third role

`PortLockMode` gains `Buffer`. It is the first role that is on the wireless network without having pinned faces, and that distinction is now explicit in the code rather than implied:

| | Wireless network | Item ports |
|---|---|---|
| **Provider** | hands stock OUT | INPUT — pinned |
| **Requester** | receives stock IN | OUTPUT — pinned |
| **Buffer** | both — receives from providers, supplies requesters | **free** — not pinned |

`Chest.IsDirectionPinned` is the new test for "are the faces locked", and it is false for a Buffer. Everything that used to read `portLock != Free` as "pinned" now asks this instead: `EnforcePortLock` leaves a Buffer's directions alone, the panel gives it the ordinary three-way face pill rather than the ON/OFF switch, and the port capability flags advertise both halves. A Buffer legitimately needs an input and an output, so pinning it would have made it useless.

`SuppliesNetwork` and `RequestsFromNetwork` replace the old two-way switch in the network's registration: a Buffer registers in both lists.

#### The stock target, and why it exists

A Buffer that requested without limit would simply drain every Provider it could reach, which is the failure mode this kind of block usually has. So a Buffer requests only up to `bufferStockTarget` (default 64) of each item, editable per chest in the panel. `Chest.ShortfallOf` returns what a chest still wants — unbounded for a Requester, the remaining gap to the target for a Buffer — and the network's transfer is capped by it.

Two buffers are also never allowed to stock each other. Without that rule a pair of buffers both requesting the same item would pass one stack back and forth forever, and a chain of them would drain the real providers unevenly. Buffers are stocked by true Providers only. The "in range" readout applies the same rule, so the number shown matches what will actually arrive.

#### Persistence

`bufferStockTarget` is player-editable at runtime, so it rides along on the port snapshot next to the request list. It is additive and a save written before this round has no value for it, which reads as zero and is explicitly ignored so the chest keeps its authored default rather than being silently zeroed.

#### Manual step in Unity

1. Open **Tools -> Voxel Engine -> Voxel Engine Setup**.
2. Click **78. Build the Logistic Chests**. It now authors three variants; the two existing ones are untouched beyond the usual non-destructive corrections.
3. The Buffer Chest is 36 slots, crafted at the Crafting Bench from 6 iron ingots, 4 copper ingots and 4 planks.
4. Place one, open it, click ITEM PORTS. It shows a violet BUFFER banner, a KEEP IN STOCK field, and a REQUESTS list.
5. Add a request and set the target. The chest fills to that number from providers in range and stops; other requesters can then draw from it.

### [11.5.2-dev] The Item Ports Panel Opens Again

**Type:** PATCH — fixes a regression introduced by 11.5.1-dev. No new systems, no save format change.

**GitHub title:** `[11.5.2-dev] The item ports panel opens again`

#### What broke

11.5.1-dev made the item-ports overlay rebuild its body in place so the REQUESTS list updates live. That rebuild opened with a guard that bailed out when the scroll view was not yet attached to a panel — sensible for a *re*build triggered from a scheduled callback, wrong for the *first* build, which runs while the overlay is still being assembled and has not been added to the root yet. The guard fired immediately, the body was never created, and the overlay opened as a title bar and a close button with nothing between them.

#### The fix

The first build now happens after the overlay is attached to the root, so the panel is live and anything the body schedules has somewhere to run. The bail-out guard is gone from the top of the rebuild: the body is always constructed, and only the scroll-offset restoration is conditional — it is skipped when the captured offset is zero, which is exactly the first-build case. The guard that remains is inside the scheduled callback, where an unattached panel really does mean "give up".

Live refresh and scroll preservation are unchanged: removing a request still updates the list immediately, and still does not scroll the panel anywhere.

### [11.5.1-dev] One Row Per Item, And The Request List Updates While You Watch

**Type:** PATCH — three bug fixes against 11.5.0-dev. No new systems, no save format change.

**GitHub title:** `[11.5.1-dev] One row per item, and the request list updates while you watch`

#### 1. Searching "iron ore" returned four Iron Ores

Only one of them worked. The cause is older than the logistic chests: `ItemDefinition` used to arrive pre-filled with `iron_ore` / `Iron Ore` as its default id and name, so any asset authored back then whose identity was never typed in kept those values and now genuinely claims to be iron ore. Three such assets survive in the project — the gravel item, the stationary radar beacon block and the fire igniter tool — and every item picker listed all four as indistinguishable rows. Picking the wrong one bound a request to gravel wearing the ore's name.

This is fixed from both ends.

The **data** is repaired by new setup step 81, which finds every asset holding the legacy id that is not the canonical ore and gives it an identity derived from its own asset name: `Item_Gravel` becomes `gravel` / "Gravel", `Block_StationaryRadarBeacon` becomes `stationary_radar_beacon` / "Stationary Radar Beacon". The same sweep repairs assets whose id was authored correctly but whose display name was left on the default, which is what made the iron and copper material assets read as ore. The canonical `Item_IronOre` and `Item_CopperOre` are never touched, nothing is deleted, no reference is repointed, and an asset that already carries its own authored identity is left alone. Safe to re-run.

The **UI** is hardened independently, so the pickers stay honest even if a duplicate id reappears later. The item catalogue behind every search box now collapses to one entry per id, and where several assets share an id it keeps the best-authored candidate: an asset whose own file name resolves to the id wins, having an icon helps, and a block or tool that merely inherited the id loses to a real resource item. So the ore beats its impostors even before step 81 is run.

Copper was checked for the same fault. Only the material asset was affected, and step 81 covers it.

#### 2. Removing a request did not refresh the panel

The REQUESTS list only redrew after the item-port screen was closed and reopened. The overlay built its body once and passed an empty `onChanged` callback, so every edit inside it correctly changed the chest and then told nobody. The callback now rebuilds the panel body in place.

Only the body is replaced — the card, the header and the scroll view itself survive the swap. **The scroll position is preserved across the rebuild** rather than snapping anywhere: the offset is captured before the swap and restated after layout resolves, so a long request list does not jump to the top, and it does not jump to the bottom either. Removing the fourth of ten requests leaves you looking at exactly where the fourth one was.

#### Manual step in Unity

1. Open **Tools -> Voxel Engine -> Voxel Engine Setup**.
2. Scroll to the bottom and click **81. Repair Stolen Item Identity**.
3. Read the Console. Every change logs with `[Setup 81]` and names the asset, its old identity and its new one.
4. If the dialog reports skipped assets, those could not be named automatically from their file name — give them an id by hand.
5. Open a Requester chest, click ITEM PORTS, then Request Item, and search "iron ore". One row, and it is the real ore.

### [11.5.0-dev] The Logistic Chests Point The Right Way, And Requests Stand Alone

**Type:** MINOR — corrects the 11.4.0-dev model. The port roles are inverted and the wireless request list becomes its own field with its own saved data. Save-compatible: the new field is additive and a pre-existing chest reads as requesting nothing.

**GitHub title:** `[11.5.0-dev] The logistic chests point the right way, and requests stand alone`

#### Why this round

11.2.0-dev pinned the ports the wrong way round, and 11.4.0-dev then built the wireless network on top of that mistake by reusing the port filters as the request list. Both are corrected here.

The item ports and the wireless network are **two different transports**, and a chest's role in one is the mirror of its role in the other:

| | Wireless network | Item ports |
|---|---|---|
| **Provider Chest** | hands stock OUT to requesters | **INPUT** — pipes and belts fill it |
| **Requester Chest** | receives stock IN from providers | **OUTPUT** — it feeds the pipes downstream |

A Provider has to be filled by something, and that something is a pipe or a belt — so its ports are inputs. A Requester exists to supply the machines past it, so its ports are outputs. Previously each was the reverse of what it needed to be, which made a Requester a dead end: the network filled it and nothing could get the items back out.

#### 1. The inversion

`Chest.PinnedDirection` is now the single place that decides a locked chest's face direction, and it returns Input for a Provider and Output for a Requester. Everything that consults it follows automatically: `EnforcePortLock`, the container capability flags in `GetPortContainers`, the legacy pipe API (`GetInputContainer` / `GetOutputContainer` / `HasOutputReady` / `CanAcceptInput`), and `TryAcceptFromPipe`, which now refuses a push into a Requester rather than into a Provider. Setup step 78 seeds the same direction, so a freshly authored prefab matches.

The port panel follows too: the face pill reads INPUT ON for a Provider and OUTPUT ON for a Requester, the banner explains the two-transport split in one line, and the round-robin toggle is hidden on a Provider (whose ports are inputs) rather than on a Requester.

#### 2. Requests are their own list

The request list is no longer scraped from the port whitelists. `Chest` carries a dedicated `_requests` list with `AddRequest` / `RemoveRequest` / `SetRequests`, and `LogisticsNetwork` reads exactly that. The two are now cleanly separated:

- **Port filter** — what may leave this chest down a pipe.
- **Request list** — what the network delivers into it.

Reusing the filters conflated those, and with the ports inverted it became impossible to express: a Requester's faces are outputs, so its filters govern departures and could never have described what it wants delivered.

#### 3. The same picker, a new place

The panel's REQUESTS section lists each requested item with a live "N in range" or "none in range" readout and a remove button, plus a "＋ Request Item" button. That button opens `ItemFilterDialog.OpenList`, a new overload of the dialog the port filters already use — same search box, same inventory-click capture, same chips. The player learns one picker and uses it for both jobs.

#### 4. Requests survive a save

`ItemPortSnapshot` gains `requestItemIds`, written and restored by the chest's existing snapshot bridge. It is additive: a save written before this round has no list, which restores as an empty request list — the correct default. No new save plumbing and no schema break.

#### What to run

1. Replace `Scripts/Building/Chest.cs`, `Scripts/UI/PortConfigHud.cs`, `Scripts/UI/ItemFilterDialog.cs`, `Scripts/Transport/LogisticsNetwork.cs`, `Scripts/Transport/ItemPortRouting.cs`, `Scripts/Transport/IItemPortHost.cs` and `Scripts/Editor/LogisticChestsSetup.cs`. Let Unity compile.
2. Re-run **step 78** so the two prefabs seed their faces in the corrected direction. Chests already placed in the world re-pin themselves on load.
3. Open a Provider Chest: the banner should say pipes fill it, and its faces should read INPUT ON. Run a belt into it and confirm items arrive.
4. Open a Requester Chest: its faces should read OUTPUT ON, and it should have a REQUESTS section. Add Iron Ingot there — not in the port filter.
5. Stock the Provider with iron ingots within 48 m. The Requester's entry should go green with a count, and ingots should arrive in batches of 16 once a second.
6. Run a pipe out of the Requester into a machine or chest: the delivered ingots should flow onward. That is the loop that was impossible before this round.
7. Save and reload: the requests, the locks and the deliveries all resume.

### [11.4.0-dev] The Chests Talk To Each Other: Wireless Request and Fulfilment

**Type:** MINOR — new system, save-compatible. One new runtime component, a registration hook on the chest, and a new panel section. No new asset, no setup step, no saved field: the request list is the per-face item filter that already saves and restores.

**GitHub title:** `[11.4.0-dev] The chests talk to each other — wireless request and fulfilment`

#### Why this round

11.2.0-dev shipped the Provider and Requester chests and the roadmap recorded exactly one thing still open on the storage line: "automated request/fulfilment routing between logistic chests (a network-level feature, not a block)". Until now the two locks only described intent — a Requester across the base from a Provider stayed empty unless the player ran a pipe between them, which is precisely the spaghetti the logistic chests exist to remove.

#### 1. The network

New `VoxelEngine.Transport.LogisticsNetwork`, a singleton that runs one fulfilment pass per second. A locked chest registers itself on enable and drops out on disable, so the network holds only logistic chests and a free chest costs it nothing. Each pass walks the requesters and, for every item a requester asks for, finds the nearest in-range provider holding it and moves up to 16 units.

| Rule | Value |
|---|---|
| Pass interval | 1 s |
| Reach | 48 m |
| Items per item-type per pass | 16 |

The metering matters: a large provider drains into a requester smoothly over several seconds rather than teleporting its whole contents in a single frame, which reads as a supply line rather than a save-load.

#### 2. What a Requester asks for

The request list is the union of the per-face **whitelists** already on the chest — no new field and no new UI to learn. A face with no filter is deliberately ignored rather than treated as "send me anything": an unconfigured chest should sit quiet instead of hoovering up the base. The player opts in by naming items in the filter, and because those filters are already part of the port snapshot, a chest's requests survive a save and reload with no schema change.

#### 3. Transfers are honest

Two traps the ore round taught us are handled explicitly. The network counts and moves the **provider's own item instance** rather than the definition the requester asked with — `ItemContainer.Remove` compares by reference, so handing it a different asset that merely means the same thing would remove nothing while still inserting into the requester, duplicating stock. And the removal is driven by what the destination actually **accepted**, so a full requester can never make items vanish.

#### 4. The panel shows the network

A locked chest's port panel gains a WIRELESS LOGISTICS section: the reach, how many providers and requesters are on the network, and — for a Requester — every item it asks for with a green dot and a live count when the network can supply it, or an amber "none in range" when it cannot. An unfulfilled request now reads as a stock problem the player can go solve, instead of looking like a broken block. A free chest's panel is unchanged.

#### What to run

1. Add `Scripts/Transport/LogisticsNetwork.cs` (new file) and replace `Scripts/Building/Chest.cs` and `Scripts/UI/PortConfigHud.cs`. Let Unity compile. There is no setup step — the network creates itself when the first logistic chest wakes.
2. Place a Provider Chest and put iron ingots in it. Place a Requester Chest within 48 m.
3. Open the Requester, switch a face on, and add Iron Ingot to that face's filter. Its panel should list Iron Ingot with a green dot and the count available.
4. Close the panel and wait. Ingots should arrive in the Requester in batches of 16, once a second, until the provider is empty or the requester is full.
5. Move the Requester out past 48 m: the entry should flip to "none in range" and deliveries should stop.
6. Save and reload with a stocked pair in place: the filters, the locks and the deliveries all resume.

### [11.3.0-dev] One Ore To Smelt: Iron Ore and Copper Ore Become Canonical

**Type:** MINOR — content consolidation, save-compatible. Two duplicate item assets are retired, every reference is repointed, and existing saves are bridged by an id alias so no stack is lost. Two new setup steps (80 and the 79 audit from 11.2.2-dev). No schema change.

**GitHub title:** `[11.3.0-dev] One ore to smelt — Iron Ore and Copper Ore become canonical`

#### Why this round

11.2.2-dev made the furnaces tolerate the duplicate ore assets, but tolerating a duplicate is not the same as not having one — and the wrong asset was winning. The furnaces accepted the bare `Items/Item_Iron` ("iron"), while the ore the player actually mines and sees in the UI is the Resource-typed `Industrial/Items/Item_IronOre` ("iron_ore") with the real icon, the Resource category and the proper description. This round picks the right one and deletes the other.

| Logical item | Survives | Retired |
|---|---|---|
| Iron Ore | `Industrial/Items/Item_IronOre` (`iron_ore`) | `Items/Item_Iron` (`iron`) |
| Copper Ore | `Industrial/Items/Item_CopperOre` (`copper_ore`) | `Items/Item_Copper` (`copper`) |

#### 1. The item id no longer defaults to iron ore

`ItemDefinition.itemId` and `displayName` defaulted to `"iron_ore"` / `"Iron Ore"`. That is the root cause behind the whole class of bug: every asset whose id was never authored silently claimed to BE iron ore, which is how a gravel item, a radar beacon block and a fire igniter tool all ended up holding that id. Both fields now default to blank — an obviously unset value that can be detected and repaired rather than quietly colliding with real content.

`ItemIdentity` follows: a blank id carries no identity, so such an asset is only ever equal to itself by reference. The old default is kept as `ItemIdentity.LegacyDefaultId` purely so step 79 can still recognise assets serialized before this change.

#### 2. Setup step 80 — Consolidate the Ore Items

New wizard button 80, implemented in `OreConsolidationSetup`. Per ore it repoints every reference to the retired duplicate and then deletes it:

- **Smelting recipes** — `Smelt_Iron`, `Smelt_Copper` and `Smelt_Steel` now take the canonical ore.
- **Crafting and machine recipes** — every input, output and byproduct, including the crusher's ore recipes.
- **The voxel material drop** — `Mat_Iron` and `Mat_Copper` now drop the canonical ore, so mining yields the item the recipes expect. This is the reference that decides what ends up in the player's hands.
- **The persistence catalogue** — the retired asset is removed and the canonical one added, so saved stacks resolve at load.

The step resolves the whole plan before writing anything and refuses to run if a canonical asset is missing. Re-running it is a no-op that reports both ores already consolidated.

#### 3. Old saves keep their ore

Deleting an asset a save refers to would normally lose the stack. New `ItemIdAliases` maps each retired id to its replacement, and `WorldStatePersistence` consults it after building the item cache — so a saved stack of `iron` loads back as Iron Ore. An alias never shadows a live id, so an asset re-introduced under a retired id takes precedence again.

#### 4. The setup no longer recreates the duplicates

Step 1 authored `Item_Iron` and `Item_Copper` from the voxel material table, so re-running it would have resurrected exactly what step 80 deletes. It now adopts the canonical Industrial asset for those two materials instead of authoring its own, leaving the icon and category intact. Steps 4, 10 and the research step resolve ore through one shared `LoadCanonicalOre` helper, so the whole project points at one asset per ore.

#### What to run

1. Add `Scripts/Items/ItemIdAliases.cs` and `Scripts/Editor/OreConsolidationSetup.cs` (new files) and replace `Scripts/Items/ItemDefinition.cs`, `Scripts/Items/ItemIdentity.cs`, `Scripts/Editor/ItemIdentityAuditSetup.cs`, `Scripts/Persistence/WorldStatePersistence.cs` and `Scripts/Editor/VoxelEngineSetupWindow.cs`. Let Unity compile.
2. Run **step 80 (Consolidate the Ore Items)**. The console logs every repointed reference and the two deletions.
3. Run **step 79 (Audit and Repair Item Identity)** afterwards to give the remaining unauthored assets their own ids and list any duplicates left.
4. Mine iron ore and copper ore. Each should stack as the icon-bearing Iron Ore / Copper Ore, and smelt in both the Solid Fuel Furnace and the Electric Furnace.
5. Load a save made before this round that held the old ore: the stacks should come back as the canonical ore rather than vanishing.

### [11.2.2-dev] The Furnaces Recognise Their Own Ore

**Type:** PATCH — bug fix. No save schema change, no API removal. One new shared helper, three machines switched to it, and one new audit step (79).

**GitHub title:** `[11.2.2-dev] The furnaces recognise their own ore`

#### The report

Both the Solid Fuel Furnace and the Electric Furnace refused iron and copper ore, reporting "Iron Ore cannot be smelt in this machine" with the ore sitting in the input slot.

#### What was actually wrong

The recipes were fine. `Smelt_Iron` and `Smelt_Copper` are authored, linked, assigned to both furnace prefabs, and point at real ingots. The fault was that the project contains **two different assets for the same ore**:

| Logical item | Asset | itemId |
|---|---|---|
| Iron Ore | `Items/Item_Iron.asset` | `iron` |
| Iron Ore | `Industrial/Items/Item_IronOre.asset` | `iron_ore` |
| Copper Ore | `Items/Item_Copper.asset` | `copper` |
| Copper Ore | `Industrial/Items/Item_CopperOre.asset` | `copper_ore` |

The smelting recipes point at the `Items/` pair. The persistence catalogue that repopulates the player's inventory on load carries the `Industrial/` pair. Both display "Iron Ore", so they are indistinguishable in the UI — but `FindRecipeForInput` compared them with `==`, which is reference equality on a ScriptableObject. Ore that arrived through the catalogue could never satisfy a recipe pointing at the other asset, so the furnace correctly concluded it had no recipe and said so.

A second, quieter fault made this hard to see. `ItemDefinition.itemId` defaults to the literal string `"iron_ore"`, so every asset whose id was never authored silently claims to be iron ore. Four assets are in that state: the real Industrial iron ore, plus a gravel item, a radar beacon block and a fire igniter tool.

#### 1. Identity instead of reference equality

New `VoxelEngine.Items.ItemIdentity` answers one question — are these two references the same item? Reference equality first, which is the fast exact path and covers every normal case. When that fails, a case-insensitive `itemId` match. The fallback deliberately rejects the unauthored default id, so a mis-authored tool can never masquerade as ore.

`Furnace`, `ElectricFurnace` and `GridElectricFurnace` now route their recipe matching, smeltable test and auto-pull top-up check through it. Nothing else changes: a recipe that matched before still matches, by the same fast path it always took.

#### 2. Setup step 79 — Audit and Repair Item Identity (Non-Destructive)

New wizard button 79, implemented in `ItemIdentityAuditSetup`. It scans every `ItemDefinition` under `VoxelEngineAssets`, gives each asset still carrying the unauthored default an id derived from its own file name, and then reports every id still claimed by more than one asset.

The one asset whose own name resolves to `iron_ore` — the real `Item_IronOre` — keeps it; the three impostors become `gravel`, `stationary_radar_beacon` and `fire_igniter`. An asset that already carries an authored id is never rewritten, and no field but `itemId` is ever touched, so running it twice is a no-op.

The remaining duplicates (the ore pairs, several nuclear items, the farming and survival food sets) are logged as warnings rather than merged: which of two assets should survive is a content decision. The furnaces now treat them as the same item either way, so smelting works regardless.

#### What to run

1. Add `Scripts/Items/ItemIdentity.cs` and `Scripts/Editor/ItemIdentityAuditSetup.cs` (new files) and replace `Scripts/Crafting/Furnace.cs`, `Scripts/Crafting/ElectricFurnace.cs`, `Scripts/GridSystem/GridElectricFurnace.cs` and `Scripts/Editor/VoxelEngineSetupWindow.cs`. Let Unity compile.
2. Put iron ore into a Solid Fuel Furnace with coal: it should smelt to an Iron Ingot. Repeat with copper ore, and with both in an Electric Furnace on a powered network.
3. Optionally run **step 79** and read the console: it reports what it repaired and lists every id still shared by two assets.

### [11.2.0-dev] The Storage Line Closes: Provider and Requester Chests

**Type:** MINOR — new content plus one new save-compatible field, no schema break. Two new storage blocks on the existing Chest component, a `portLock` field that defaults to the current behaviour, a port panel that adapts to a locked host, and one new setup step (78). Existing chests, their saves and their port snapshots are unaffected.

**GitHub title:** `[11.2.0-dev] The storage line closes — Provider and Requester chests`

#### Why this round

The roadmap's storage line has named one remaining gap since 11.1.0-dev: "only the Provider/Requester (port-locked) end of the progression remains". A plain chest can be wired either way on every face, which is flexible but means a logistics bus has no block that is guaranteed to only supply, or only receive — one mis-clicked face turns a supply buffer into a sink and quietly drains the line.

#### 1. A lock on the chest that already works

Rather than a new component, the two variants are the same `VoxelEngine.Building.Chest` with one new field, `portLock`:

| Variant | Slots | Lock | Recipe | Station |
|---|---|---|---|---|
| Provider Chest | 18 | every active face is an OUTPUT | Iron Ingot x4 + Copper Ingot x2 + Wooden Plank x2 | Crafting Bench, 3 s |
| Requester Chest | 18 | every active face is an INPUT | Iron Ingot x4 + Copper Ingot x2 + Wooden Plank x2 | Crafting Bench, 3 s |

The lock is enforced in three places so no path can contradict it. `EnforcePortLock` re-pins every active face on Awake and again after a port snapshot is restored, so an older save carrying free directions is corrected on load. `GetPortContainers` advertises only the half the lock allows, so the shared routing layer refuses the wrong transfer at the source. The legacy pipe API answers to match — a Provider returns no input container and rejects a pipe push outright; a Requester reports no output ready. `portLock` defaults to `Free`, which is exactly today's behaviour, so every existing chest and tier is untouched.

#### 2. The panel says what the block is

`PortConfigHud` now recognises a host that implements the new `IPortLockedHost` interface. A locked chest's panel gains a banner — an amber PROVIDER or blue REQUESTER badge with the rule in one line — and each face card's pill becomes a clean ON/OFF switch reading "OUTPUT ON" / "INPUT ON" / "OFF" instead of a three-way cycle, because the direction is not the player's to choose. The round-robin / priority toggle is hidden on a Requester, where it means nothing. Filters, per-face item whitelists and the two-column card grid all behave exactly as before. A free host renders the panel unchanged, down to the hint text.

#### 3. Setup step 78 — Build the Logistic Chests (Non-Destructive)

New wizard button 78 in `Tools > Voxel Engine > Voxel Engine Setup`, implemented in `LogisticChestsSetup`. Per variant it creates or repairs the prefab, the block item and the recipe, following the same contract as step 77: missing assets are authored, and an existing asset is only corrected where it cannot be right — a missing Chest component, a wrong slot count, display name or lock mode, a null recipe output or ingredient, a missing registry membership. Authored craft times, quantities, health and icons are never reset.

The one addition over step 77 is face seeding: a locked prefab with no active face at all gets its +X face switched on in the pinned direction, so the block is useful the moment it is placed. A prefab that already has an authored face layout keeps it — only a face pointing against the lock is re-pinned. The step refuses to run without step 4's content (plank / iron ingot / copper ingot must resolve) and logs every decision with the `[Setup 78]` prefix.

#### What to run

1. Add `Scripts/Editor/LogisticChestsSetup.cs` (new file) and replace `Scripts/Transport/IItemPortHost.cs`, `Scripts/Building/Chest.cs`, `Scripts/UI/PortConfigHud.cs` and `Scripts/Editor/VoxelEngineSetupWindow.cs`. Let Unity compile; the console should be clean.
2. Open `Tools > Voxel Engine > Voxel Engine Setup` and run **step 78 (Build the Logistic Chests)**.
3. Craft a Provider Chest at a Crafting Bench and place it. Right-click it: the panel should read "Provider Chest" with the amber PROVIDER banner, and each face pill should toggle between "OUTPUT ON" and "OFF" only.
4. Run a pipe into the Provider Chest from a full source: nothing should enter it. Run a pipe out of it into a Requester Chest: items should move one way only.
5. Open a plain Chest and a Steel Chest to confirm they still cycle None to Input to Output with no banner.
6. Fill a Provider Chest, switch a face off, save and reload: the items, the face state and the lock all come back.
7. Re-run step 78: the console should report "both variants already present and correct, nothing written."

### [11.1.1-dev] The Chest Tier Setup Compiles Again

**Type:** PATCH — compile fixes only. No behaviour change, no save touch, no API change.

**GitHub title:** `[11.1.1-dev] The chest tier setup compiles again`

Two compiler errors in `StorageChestTiersSetup.cs` shipped with 11.1.0-dev and blocked the whole editor assembly, so step 77 could not be run at all:

- **CS0136 at line 193** — the local `chest` declared while building a new tier prefab collided with the `chest` used in the repair branch further down the same method. The creation-path local is renamed `newChest`; the repair path is unchanged.
- **CS0019 at line 328** — `RecipeIngredient` is a struct, so `recipe.inputs[i] == null` is not a legal comparison. The null test is dropped; the remaining `item == null || count <= 0` check already covers every broken-ingredient case the guard was written for, so the repair logic behaves identically.

### [11.1.0-dev] The Chest Progression Lands: Wooden Crate, Iron Chest, Steel Chest

**Type:** MINOR — new content, save-compatible. Three new storage blocks on the existing Chest component, three new recipes, and one new setup step (77). Nothing about existing chests, their saves, or the port system changes; the pre-existing 30-slot Chest is never touched.

**GitHub title:** `[11.1.0-dev] The chest progression lands — Wooden Crate, Iron Chest, Steel Chest`

#### Why this round

The roadmap's storage line has sat at PARTIALLY COMPLETE with one gap named: "the planned Wooden Crate → Iron Chest → Steel Chest → Provider/Requester progression is not complete". The game ships a single 30-slot chest for planks x8, so early game has no cheap overflow storage and end game has no place to put the ingots the smelters just learned to make in volume.

#### 1. Three tiers on the chest that already works

Instead of a new component, the tiers are three more authored blocks on the SAME `VoxelEngine.Building.Chest` component, which already carries the port configuration, the belt/pipe plumbing, and the save/restore. A tier is just a slot count, a display name, and a recipe:

| Tier | Slots | Recipe | Station | Craft time |
|---|---|---|---|---|
| Wooden Crate | 9 | Wooden Plank x4 | Inventory | instant |
| Iron Chest | 18 | Iron Ingot x4 + Wooden Plank x2 | Crafting Bench | 2 s |
| Steel Chest | 36 | Steel Ingot x4 + Iron Ingot x4 | Assembler | 4 s |

Because the component is the one the game already knows, every surrounding system picks the tiers up with no code change: right-click opens the panel and the panel title comes from the component's `displayName`, `WorldStatePersistence` saves and restores the container plus its port snapshot by component lookup, and the item-port grid (per-face direction and filters) renders as it does for the original chest. The Wooden Crate's inventory-tier recipe means a player with four planks can build overflow storage before they have a bench.

#### 2. Setup step 77 — Build the Storage Chest Tiers (Non-Destructive)

New wizard button 77 in `Tools > Voxel Engine > Voxel Engine Setup`, implemented in `StorageChestTiersSetup`. Per tier it creates, or repairs, three assets:

- **The prefab** (`StationPrefabs/<Tier>.prefab`): a box mesh on a `Chest` component with the tier's slot count and display name. An existing prefab is only saved when something is actually wrong — a missing Chest component, a wrong slot count, or a wrong display name is corrected; every other authored property is left alone.
- **The block item** (`Blocks/Block_<Tier>.asset`): placeable, Storage category, the placed-prefab reference pointed at the verified prefab. Authored quantities, health, mining tier and icons are kept; only missing metadata and a broken placed-prefab link are repaired.
- **The recipe** (`Recipes/Recipe_<Tier>.asset`): registered in the existing `RecipeRegistry`. When the recipe already exists its quantities and craft time are preserved — only a null output or ingredient link (the same failure mode that broke the smelting recipes in 11.0.0-dev) is re-linked, and a missing registry membership is restored. When it doesn't exist, it is authored from the tier table above.

The step refuses to run without step 4's content (plank / iron ingot / steel ingot must resolve) and logs every decision with the `[Setup 77]` prefix. Icons bind through the usual icon sync — new items get their `ItemIcons` PNG the next editor session, and until then they render their tint fallback exactly like any other un-iconed item.

#### 3. What this round does NOT do

The Provider/Requester end of the roadmap line stays open: that is a port-locked variant (a chest whose faces are fixed to output-only or input-only) and needs a field on the component plus the port UI to honour it, which is its own round. This round delivers the three storage tiers the line names first.

#### What to run

1. Replace or add `Scripts/Editor/StorageChestTiersSetup.cs` (new file) and `Scripts/Editor/VoxelEngineSetupWindow.cs` (one new wizard button). Let Unity compile; the console should be clean.
2. Open `Tools > Voxel Engine > Voxel Engine Setup` and run **step 77 (Build the Storage Chest Tiers)**. The dialog lists the three tiers; the console logs every create/repair with the `[Setup 77]` prefix.
3. Craft a Wooden Crate (planks x4, straight from the inventory crafting list) and place it: the panel should read "Wooden Crate" with 9 slots. Craft an Iron Chest at a Crafting Bench and a Steel Chest at an Assembler to verify the other two tiers and their slot counts.
4. Re-run step 77 once more: the console should report "all three tiers already present and correct, nothing written." That is the non-destructive contract.
5. Place a tier chest, fill it, save and reload: the items and any per-face port configuration come back.

### [11.0.1-dev] The Furnaces Say Why They Stand Still, and the Well Sees Past Its Own Derrick

**Type:** PATCH — bug fixes only. No save changes, no API removals: the furnace panels now surface the stall reasons 11.0.0-dev already computes and logs, and the Jack Pump's well probe no longer stops at the machine's own colliders.

**GitHub title:** `[11.0.1-dev] The furnaces say why they stand still, and the well sees past its own derrick`

#### Why this round

Test feedback: an item in the input, coal in the fuel, nothing happens — and the furnace panel says "No input", which is not true; the electric furnace reads the same way, and a Jack Pump placed into a base does not pump.

#### 1. The furnace panel showed "No input" for every stall that was not "no input"

`StallReason` arrived in 11.0.0-dev — No power, No input, No recipe for this input, No fuel, Fuel slot holds something that does not burn, Output full, Switched off — and the log half was wired: each change is reported once with the reason and the state of the recipe links. The panel half never was. `TickFurnaceLiveUI` still carried the old label:

```csharp
_liveSmeltLabel.text = f.Current != null ? $"{...}% smelted" : "No input";
```

`Current` is null in every stalled case, so a furnace holding ore with broken smelt links, a furnace out of fuel, and a furnace with an empty input slot all read "No input". A player who put ore in the slot watched the machine claim the slot was empty, with no way to tell which repair path applied.

Now:

- **The smelt label carries the actual `StallReason`** (it wraps to two lines when the reason is long). "No input" appears only when the input slot really is empty.
- **The header pill carries the short form of the same reason** — NO POWER / NO INPUT / NO RECIPE / NO FUEL / BAD FUEL / OUTPUT FULL / SWITCHED OFF — red for faults the player has to fix, amber for states that are merely waiting.
- **A one-line hint under the smelt bar for the "No recipe for this input" case.** When one of the assigned recipes lost its input or output link (new public `HasBrokenRecipes` on both static furnaces), the hint names the exact step to re-run: **step 4 (Build Crafting Content)** on the fuel furnace, **steps 4 and 10** on the electric furnace (which also carries the glass recipe). When the links are sound, the hint says which item in the slot cannot be smelt in that machine.
- **First paint uses the same rule as the per-frame tick**, so the panel never flashes a stale IDLE before the reason arrives.

Nothing about the matching, fuel, or batch logic changed: `StallReason` is read, not rewritten, and a furnace with sound recipes and ore in it smelts exactly as before. The 11.0.0-dev console line is unchanged and remains the definitive report for anything the panel cannot show.

#### 2. The well probe stopped at the derrick's own collider

`Pumpjack.DetectReservoir` cast one raycast per probe column and took the first hit. The first hit is often not the world: the derrick's own collider, a foundation or plate the pump stands on, a machine under it. None of those has a `CelestialBody` in its parent chain, so the column found "no body" — and a Jack Pump installed on a base read NO CRUDE BELOW forever, on an oil world, over a real seep.

The probe now digs: a hit that is not the body's terrain advances the ray past the collider and re-casts, up to 8 hops per column, all within the configured `scanDepth`. The derrick's own collider, placed blocks, and machines are transparent to the probe, and a ray that starts inside the derrick (a zero-distance hit) steps forward a floored 0.02 m instead of stalling. A body whose ore layers do not carry crude still ends its columns immediately, so a non-oil world is refused just as fast as before. No new fields, no new setup step; the 1 s rescan cadence is unchanged.

#### What to run

1. Replace `Scripts/Crafting/Furnace.cs`, `Scripts/Crafting/ElectricFurnace.cs`, `Scripts/Crafting/Pumpjack.cs`, and `Scripts/UI/GameUIController.cs`. Let Unity compile; the console should be clean.
2. No setup step to re-run: this round authors nothing.
3. Put ore and coal in a furnace. If it smelts, done. If it does not, the panel now says why: a NO RECIPE pill with the "missing item links" hint means re-run `Tools > Voxel Engine > Voxel Engine Setup`, step 4 (and 10 for the electric furnace), once — the hint says exactly that. Any other pill text is the real cause, word for word.
4. On an oil world, stand a Jack Pump on a foundation or any block over a seep and open its panel: it should read PUMPING, not NO CRUDE BELOW. On a world without crude, NO CRUDE BELOW is still the right answer.

### [11.0.0-dev] A Parked Hull Stays Parked, the Water Probe Stops Throwing, and the Well Produces Crude

**Type:** MAJOR — this round corrects a shipped regression, and in doing so changes the save schema: the three frame-relative velocity fields added to `SavedGrid` in 10.1.0-dev are removed. The Jack Pump also stops having item slots, so its prefab changes shape. Both changes are load-compatible in one direction only — a save written by this round cannot be read by 10.1.0-dev or 10.2.0-dev — which is what the major bump records. **One setup step is required: `Tools > Voxel Engine > Voxel Engine Setup`, step 76.**

**GitHub title:** `[11.0.0-dev] A parked hull stays parked, the water probe stops throwing, and the well produces crude instead of barrels`

#### Why this round

Test feedback named four things that were wrong. Each is dealt with below in the order it was reported, and the first is a regression this project shipped in 10.1.0-dev.

#### 1. A hull sank further into the ground on every rejoin

**The cause was the frame-relative velocity from 10.1.0-dev, and it is removed.** That round stored a movable grid's linear velocity relative to the motion of the scene's reference frame, on the reasonable-sounding grounds that a raw scene velocity describes motion against a frame that is itself orbiting. The arithmetic was wrong. `SpaceOrigin.FrameVelocityKmS` is in kilometres per second and a `Rigidbody.linearVelocity` is in metres per second, and the conversion multiplied by 1000 in the right direction for one leg of the round-trip and not the other. The result: a hull parked on the ground was saved with roughly zero relative velocity, and restored with its planet's ent