# Changelog

"While You're Up (rimhunter)" is a modified version of "While You're Up" v4.0.4 by Christopher S. Galpin (CodeOptimist), under the GNU AGPL v3 or later. This file lists every change from v4.0.4, with its date, as the AGPL requires for a modified version. Original copyright and license: see LICENSE, NOTICE and MODIFICATIONS.

Credit: Anarchoped's "While You're Up - 1.6 Unofficial Update" (AGPL) was read only as a reference. Nothing in this version was copied from it. If that changes, the entry says so here and names Anarchoped.

Add each new change under "Unreleased" with its date as `- YYYY-MM-DD: what changed`. At release, rename the heading to the version and release date.

## 4.1.0 (2026-11-15)

### Identity and license

- 2026-11-15: renamed to "While You're Up (rimhunter)", packageId `rimhunter.whileyoureup`, version 4.1.0, author "CodeOptimist (Christopher Galpin), rimhunter", supported versions 1.6 and 1.7, Harmony as a dependency.
- 2026-11-15: the mod list now warns when `CodeOptimist.JobsOfOpportunity`, `zsbk.patch16.whileyoureup`, `hoodie.whileyoureup` or `kevlou127.WhileHYouOreHUpHQ1V0S` is also enabled.
- 2026-11-15: added MODIFICATIONS ("Modifications Copyright 2026 rimhunter, under the GNU AGPL v3 or later"), this CHANGELOG and WORKSHOP.md. LICENSE and NOTICE are unchanged from v4.0.4.
- 2026-11-15: the settings window shows a one-line license notice (new key `License_Notice`, English only).
- 2026-11-15: the license notice sits in its own row beside the restore-defaults button instead of crowding the window's top line.
- 2026-11-15: `Source/` is included in the packaged mod folder (`scripts/package`). The test runner and tests are not.
- 2026-11-15: removed `About/PublishedFileId.txt`, which held the original mod's Workshop id.
- 2026-11-15: WORKSHOP.md (the Workshop page text, for the owner to paste) stays in the repository and is no longer copied into the packaged mod folder.
- 2026-11-15: `About.xml` links the source repository (https://github.com/rimhunter/while-youre-up), and the Harmony download link points at `pardeike/HarmonyRimWorld` (it named a repository that does not exist).
- 2026-11-15: added CONTRIBUTING.md.
- 2026-11-15: every translation from v4.0.4 is kept. The original ships English only, so `Languages/English` is the only folder. New keys are English only.

### Build

- 2026-11-14: one project builds both game versions (1.6 and 1.7) into `1.6/Assemblies` and `1.7/Assemblies`, with per-version load folders in `LoadFolders.xml`; compiled against Krafs.Rimworld.Ref and Lib.Harmony 2.4.3.
- 2026-11-14: vendored the CodeOptimist library at 892b8b1 under `Source/CodeOptimist`.
- 2026-11-14: rewrote `WeakDictionary`, `NonThingFilter` and `Listing_TreeNonThingFilter`, which are not in the public library, with unit tests for `WeakDictionary`.
- 2026-11-14: fixed the compile breaks on 1.6 and 1.7: haulables collection type, `FindPathNow`, publicized private members, `LudeonTK` namespace, `Frame` materials, `AddFinishAction`, a `Label` overload.
- 2026-11-14: added CI for the build.
- 2026-11-15: the Release build is what ships and what the tests run; the `#if DEBUG` job-start patch is no longer in the shipped DLL.

### Fixes for 1.6 and 1.7

- 2026-11-14: Closer-to hauls for construction work again on 1.7: the construction transpiler is rewritten for the new IL, where the old closure fields are locals, and supplies are hauled closer first.
- 2026-11-14: Hauls of Opportunity work on 1.7: the opportunity transpiler anchors loudly on the expected IL and the path check uses the new found flag. A pawn forming a caravan is skipped.
- 2026-11-14: fixed a native crash at new-game start. The building filters read and wrote `ThingFilter` fields at the 1.4 layout on 1.6 and 1.7; the type replacement now maps fields and fails loudly when the layouts differ.
- 2026-11-15: the type replacement behind the building filters now refuses a method that its twin type lacks, unless the method is on an explicit keep-on-original list (the eleven `ThingFilter` and `Listing_TreeThingFilter` members that have no twin yet); it used to keep calling the original silently.
- 2026-11-15: Opportunity hauls scan far fewer haulables on a large colony: each haulable is skipped until the range expansion at which it first qualifies, one that exceeds the maximum total trip is dropped once, and rejected haulables are removed in one pass instead of one by one. Which item is chosen does not change.
- 2026-11-15: `WeakDictionary` lookups no longer allocate a `WeakReference` per call (they probe with a reusable key), which the haul checks run on every work scan.
- 2026-11-15: the Pick Up And Haul store-cell cache is no longer cleared at the end of every Pick Up And Haul check; it is cleared when the carrier, the game tick or the detour changes, and every cached cell is checked against the storage as it is now before it is offered (removed storage, storage that stopped accepting the item, and a reserved or blocked cell are never offered).

### Settings

- 2026-11-14: on the first launch with no settings of its own, the mod copies the original mod's saved settings (every value that still has a matching setting) and never imports again. An unreadable file is logged as an error naming the file.

- 2026-11-15: the settings import only considers files that are the original mod's (the Workshop 2034960453 settings file, or a file that names the original's settings class). Another mod's unreadable settings file is skipped; a recognizable original that cannot be read or parsed is still logged as an error naming the file.
- 2026-11-15: the default storage-building filters now recognize LWM's Adaptive Deep Storage (`ASF.DeepStorage`) like v4.0.4 did the original Deep Storage (`lwm.deepstorage`): its storing has a delay, so Opportunity hauls skip it by default, and Closer-to hauls may use it by default.

### Pick Up And Haul

- 2026-11-15: the mod detects Mehni's original, Pick Up and Haul - Continued and Pick Up And Haul - Reworked. For a fork whose types are present but lack a member the mod needs, one red error names the fork, its packageId and every missing member, and the Pick Up And Haul features are switched off (everything else keeps working).
- 2026-11-15: Pick Up And Haul - Reworked already hauls on the way, unloads closest first and carries materials closer itself, so it is recognized, logged once and left unpatched.
- 2026-11-15: a closer-to haul between equal-priority storage leaves the haul list alone (nothing already in storage ends up on the list of things to haul), with a scenario that proves it for each fork.

### Testing

- 2026-11-14: added a test-only scenario runner (`TestRunner/`, `scripts/run-scenarios`) that launches the game and checks jobs, item positions, report text and log errors. It is never shipped.
- 2026-11-15: added storage-mod scenarios for Deep Storage and Neat Storage (no job loop with five pawns, exactly full cells and 6000-tick runs). They declare the mods they need (`Mods = "..."`), are left out of the default list and run by `scripts/run-all`, one launch per mod set.
- 2026-11-15: `scripts/run-all` runs every launch the suite needs (the default scenarios, one launch per set of mods a scenario declares, each settings-import case, the language check) and exits non-zero if any fails; the default list passes with no extra mods.
- 2026-11-15: the runner stops the game it started when it is signaled and holds the game lock until the game has exited; the lock path comes from `RIMWORLD_GAME_LOCK` (default under the user's cache folder) and no personal path is in the repository.
- 2026-11-15: every scenario map is generated from a fixed seed derived from the scenario name, recorded in the result file with a fingerprint of the generated map.
- 2026-11-15: scenarios for the Pick Up And Haul store-cell cache with the mod loaded, and a speed scenario for a large colony with Pick Up And Haul.
