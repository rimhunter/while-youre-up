# Testing

Tests run the real game (RimWorld 1.6 or 1.7 on Linux, whichever the host has installed) through a test-only runner mod in `TestRunner/`, and plain unit tests in `Tests/`.

## Run everything

    scripts/run-all

One command that runs every game launch the suite needs, one after another, and exits non-zero if any launch fails. It prints a per-launch summary (result, exit code, seconds, scenario counts). Launches, in order:

1. the default scenario list, with no extra mods (`scripts/run-scenarios`)
2. one launch per set of mods a scenario requires (see below), with the Pick Up And Haul scenarios run once per fork (Mehni's original, Continued, Reworked) and the stub-fork scenario against `TestRunner/Fixtures/puah-stub`
3. each settings-import case (`scripts/test-settings-import`, one launch per case)
4. the language check (`scripts/test-language`)

`scripts/run-all --with-screenshots` also runs the screenshot checks (below), which open a game window. `scripts/run-all --with-runner-proofs` also runs the proofs of the runner itself: `scripts/test-runner-lifetime` (the game and the lock go away when the runner is signaled), `scripts/test-map-determinism` (the same scenario gets the same map in two launches) and, on 1.7, `scripts/test-crash-report` (a native crash is named from its core dump, see below). `--stop-on-failure` stops at the first failed launch. Other options (for example `--timeout`) go to every scenario launch.

The first launch builds the mod and the runner; the rest reuse the build. A full run takes several minutes, longer than one tool call allows in some environments, so run it in a shell you can wait on.

## Run part of it

    scripts/run-scenarios                       # the default list, no extra mods
    scripts/run-scenarios -s NAME[,NAME...]     # chosen scenarios
    scripts/run-scenarios --list                # names, and which need mods
    scripts/run-scenarios --mod puah-continued -s puah-unloads-once-per-storage-destination-closest-first
    dotnet test --project Tests/WhileYoureUp.Tests

## Scenarios that need mods

A scenario declares the mods it needs in its attribute, as aliases, Workshop ids or folders under the repo, separated by spaces:

    [Scenario("my-scenario", Mods = "2023507013 3033901359")]

It is left out of the default list (so the default list passes with no extra mods), and `run-all` groups scenarios with the same set into one launch. The token `puah-fork` means "each Pick Up And Haul fork in turn". Aliases (`puah-mehni`, `puah-continued`, `puah-reworked`) are in `scripts/run-scenarios`. Scenarios marked `SelfTest = true` prove the runner itself and run only by name.

## Environment

- `RIMWORLD_GAME_LOCK`: the file the runner takes an exclusive lock on for the whole launch, so two games never run at once on a host. Default `$XDG_CACHE_HOME/rimhunter-runner/game.lock` (`~/.cache/rimhunter-runner/game.lock`). Hosts that share one game between several checkouts point every checkout at the same file.
- `RIMWORLD_DIR`, `RIMWORLD_WORKSHOP_DIR`: the game folder and the Workshop folder (defaults under the flatpak Steam in `$HOME`).
- `RIMHUNTER_RUNNER_WORK`: run folders (default `$XDG_CACHE_HOME/rimhunter-runner`, newest 15 kept).
- `DOTNET`: the dotnet executable (default `~/.dotnet/dotnet`).

## RimWorld 1.6 and 1.7

The runner reads the game's `Version.txt`, builds the mod and the runner for that version (`1.6/Assemblies` or `1.7/Assemblies`) and launches it the way that engine needs:

| | 1.7 (Waylith) | 1.6 (Unity 2022.3) |
|---|---|---|
| executable | `RimWorld` | `RimWorldLinux` |
| headless | `-Headless` | `-batchmode` (no window, real GPU device); `-nographics` is not used, it makes the game log texture-atlas errors |
| window | Wayland, only `XDG_RUNTIME_DIR` needed | X11 through Xwayland: `DISPLAY=:0` and the `/run/user/<uid>/.mutter-Xwaylandauth.*` cookie |
| Player.log | under `XDG_CONFIG_HOME` | `-logFile`, same path |
| log errors at startup | `ERROR`/`FATAL` lines in Player.log | the game's own log record (`Verse.Log.Messages`, errors only): Unity's Player.log carries no level per line |
| environment | `DOTNET_EnableWriteXorExecute=0` (MonoMod re-patching vs SELinux, see ticket #25) | none: Mono has no tiered JIT or W^X |

Unity allows GUI calls only inside `OnGUI`, and `OnGUI` does not run under `-batchmode`. A scenario that draws GUI itself outside the game's frame is marked `WindowedOnUnity = true`: on 1.7 it stays in the headless default list; on 1.6 it returns `IEnumerator`, runs its drawing through `ScenarioContext.InGuiPass`, needs `--windowed`, and `run-all` gives it a windowed launch of its own ("GUI in a window (1.6)").

A scenario can be limited to one game line with `OnlyOn = "1.6"`: `run-all` leaves it out on other lines, and running it by name there fails with a message. `hugslib-opens-the-mod-settings-with-the-mod-patches-applied-and-no-error` is one: HugsLib (Workshop 818773962) has no 1.7 build. It loads HugsLib, checks the startup log has no error, and opens the mod's settings through HugsLib's `OptionsDialogExtensions.GetModSettingsWindow`, which HugsLib's transpiler puts in place of the game's `new Dialog_ModSettings(mod)` in the options dialog. HugsLib 12 no longer has `Dialog_VanillaModSettings`, the type the original's HugsLib patch targeted, so the mod's settings-dialog patches apply to the game's `Dialog_ModSettings`, which is what HugsLib opens.

## The runner owns the game

`scripts/run-scenarios` stops the game it started when it is signaled (TERM, INT, HUP) and exits 128 plus the signal number. The game also dies with the runner if the runner is killed outright, and holds the lock file descriptor, so the lock stays held until the game has exited.

## Native crashes

When the game dies of a signal, `scripts/run-scenarios` reads the game's core dump with `coredumpctl info` (systemd-coredump; it waits up to 90 s for the dump) and prints the crashing thread's first frames. A crash it recognizes is named, with the ticket, and the command exits 6 instead of 2:

- `KNOWN UPSTREAM CRASH (orum/rimhunter#28)`: the JIT faults in `ILStubResolver::ResolveToken` under MonoMod's exception helper (`mm-exhelper`). The JIT hook of MonoMod.Core 1.3.6, inside Harmony 3.0.0-rc.3 (the Harmony mod's 1.7 build on the Workshop, mod version 3.0.1.0), re-enters the JIT while it compiles a P/Invoke stub. Any launch with Harmony can hit it, rarely, while the game starts; the mod under test is not involved. Harmony 3.0.0-rc.5 (Harmony mod 3.0.2.0) no longer does this. Rerun the launch.

Other exit codes are unchanged (see the docstring of `scripts/run-scenarios`). When no dump is found, the runner says so and keeps exit 2.

`scripts/test-crash-report` proves it: the frame reader on captured stacks in `scripts/fixtures/`, a launch that aborts the game natively (`selftest-native-crash`, exit 2, frames printed), and a launch that sets up the MonoMod re-entry in the real game (`selftest-monomod-jit-reentry-crash`, exit 6, named). That last check goes red once the Workshop Harmony no longer has the bug; then the known-crash entry in `run-scenarios` can go.

Two launch options help with crash triage:

- `--no-mods`: the game with no mods at all (no Harmony, no runner, no mod under test). It boots, builds the quick-test map and quits by itself (`-QuitAfterBoot`, 1.7 only); the command checks only that it quit cleanly.
- `--game-env NAME=VALUE` (repeatable): an extra environment variable for the game, which otherwise starts with a clean environment, for example `DOTNET_JitStdOutFile=/tmp/jit.txt --game-env DOTNET_JitDisasmSummary=1` (every method the JIT compiles, in order) or `MONOMOD_LogToFile=/tmp/mm.log --game-env MONOMOD_LogSpam=1`. It is applied last, so it can override the runner's own settings such as `DOTNET_EnableWriteXorExecute`.

## Maps are deterministic

Every scenario map is generated from a fixed seed derived from the scenario name, in a world whose seed string the runner fixes. The result file records `map_seed` and `map_fingerprint` for each scenario. A vanilla error such as "Found no good central spot" therefore repeats on every run of a scenario that hits it; it is never filtered out.

## Screenshot checks (windowed)

Some scenarios check what a player sees on screen: they open a real game window, set up the moment, and save a screenshot rendered by the game itself (the engine's `ScreenCapture.CaptureScreenshot`, which RimWorld 1.6's Unity also has). Each one asserts its outcome from the frame: the text the game drew while the screenshot was taken (with its position on screen) and the pixels of the saved PNG. A person, or an agent, then looks at every screenshot.

    scripts/run-scenarios --windowed -s screenshot-pawn-picks-up-an-item-on-the-way-to-its-job --save-screenshots /tmp/shots
    scripts/run-scenarios --windowed --stage-mod 2034960453 -s screenshot-mod-list-warns-when-the-original-while-youre-up-is-also-enabled
    scripts/run-scenarios --windowed --mod puah-continued -s screenshot-puah-inventory-fills-with-items-on-the-way,screenshot-puah-unloads-closest-storage-first
    scripts/run-all --with-screenshots     # every screenshot scenario, grouped by mods, copies saved under the logs folder

- A screenshot scenario is marked `Windowed = true` and returns `IEnumerator`: it `yield return`s to let the game render frames, and `yield return s.Screenshot("name")` to capture one. It is left out of the default list, and the runner refuses to run it without `--windowed`.
- The window opens on the host's desktop session (on Wayland, only `XDG_RUNTIME_DIR` is needed), so these need a logged-in desktop and are not part of the default `run-all`.
- `ScenarioContext` helpers: `SelectOnly(thing)`, `LookAt(cell, cellsAcross)`, `ScreenPixelOf(cell)`, `Screenshot(name)`. A `Shot` gives `Texts` (each drawn label with its screen box, ink box, whether it was inside a scroll view and whether it overflowed its box), `Image` (the decoded PNG), `ShowsText`, `FractionAlong` and `CountIn` for pixel checks. The game is paused while a screenshot scenario renders frames; it ticks only through `Tick` and `RunUntil`.
- `--stage-mod SPEC` puts a mod in the launch's Mods folder without activating it, so a scenario can enable it on the game's own mod page (`StageMods = "..."` in the attribute documents what the scenario needs).
- Full-size PNGs are in the run folder's `screenshots/`; `--save-screenshots DIR` also writes copies scaled to 1152 pixels wide with a 128 color palette (a few hundred KB each), small enough to attach to an issue. It needs Pillow with libimagequant, which keeps small colored lines and icons true to color.
