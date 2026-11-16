#if RW_1_7
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace WyuTestRunner.Scenarios
{
    // orum/rimhunter#28: sets up, in the real game, the re-entrant JIT that Harmony 3.0.0-rc.3's
    // MonoMod hook causes, so the game crashes the way the rare startup crash does. It runs from the
    // runner's Mod constructor, before the game's own threads compile that stub. A fresh thread
    // enters a lock held here; its spin-wait needs the P/Invoke stub of Thread.SpinWaitInternal,
    // whose compile goes through MonoMod's hook, which creates a ThreadLocal slot under that same
    // lock (MonoMod's ThreadLocal<IAllocatedMemory> lock), spins again and compiles the same stub
    // re-entrantly. The outer compile then reads freed IL stub state and the JIT faults.
    static class JitReentryCrash
    {
        public const string Name = "selftest-monomod-jit-reentry-crash";
        static string s_outcome = "the trigger did not run: it runs from the runner's Mod constructor only when this is the only selected scenario";
        static Action s_enter, s_exit;
        static volatile bool s_released;
        static bool s_blockedUntilRelease;

        static void Contend()
        {
            s_enter();
            s_exit();
            s_blockedUntilRelease = s_released;
        }

        static void Noop() { }

        [Scenario("selftest-monomod-jit-reentry-crash", SelfTest = true, MapSize = 30, OnlyOn = "1.7")]
        static void Crash(ScenarioContext s) => s.Fail(s_outcome);

        public static void Trigger()
        {
            var harmony = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "0Harmony");
            if (harmony == null) { s_outcome = "0Harmony is not loaded"; return; }
            var allocated = harmony.GetType("MonoMod.Core.Platforms.IAllocatedMemory");
            if (allocated == null) { s_outcome = "0Harmony has no MonoMod.Core.Platforms.IAllocatedMemory"; return; }
            var threadLocal = typeof(ThreadLocal<>).MakeGenericType(allocated);
            RuntimeHelpers.RunClassConstructor(threadLocal.TypeHandle);
            var monoModLock = (Lock)threadLocal.GetField("s_idManagerLock", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
            if (monoModLock == null) { s_outcome = "ThreadLocal<IAllocatedMemory> has no s_idManagerLock"; return; }
            s_enter = monoModLock.Enter;
            s_exit = monoModLock.Exit;

            var heat = new Lock();
            for (int round = 0; round < 6; round++)
            {
                for (int i = 0; i < 100; i++) heat.Enter();
                for (int i = 0; i < 100; i++) heat.Exit();
                Thread.Sleep(150);
            }

            RuntimeHelpers.PrepareMethod(typeof(JitReentryCrash).GetMethod(nameof(Contend), BindingFlags.NonPublic | BindingFlags.Static).MethodHandle);
            var warm = new Thread(Noop);
            warm.Start();
            warm.Join();

            monoModLock.Enter();
            var contender = new Thread(Contend);
            contender.Start();
            Thread.Sleep(300);
            s_released = true;
            monoModLock.Exit();
            contender.Join();
            s_outcome = "the game survived (contender blocked until release: " + s_blockedUntilRelease + "): the MonoMod JIT hook in this Harmony did not re-enter the JIT, or Thread.SpinWaitInternal's stub was already compiled";
        }
    }
}
#endif
