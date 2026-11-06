using System;
using System.Collections.Concurrent;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace QLTK.Client140
{
    /// <summary>
    /// ÄÆ°a má»i lá»‡nh Unity (Debug.Log, Screen.SetResolution, ...) vá» main thread.
    ///
    /// Báº®T BUá»˜C vá»›i IL2CPP: gá»i UnityEngine API tá»« thread khĂ¡c main thread gĂ¢y
    /// AccessViolationException (Ä‘Ă£ gáº·p khi SocketClient gá»i Debug.Log trong
    /// callback cá»§a async socket).
    /// </summary>
    internal sealed class MainThreadDispatcher : MonoBehaviour
    {
        // IL2CPP: constructor báº¯t buá»™c nháº­n IntPtr vĂ  chuyá»ƒn cho base.
        public MainThreadDispatcher(IntPtr ptr) : base(ptr) { }

        private static readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();
        private static MainThreadDispatcher _instance;

        public static bool IsReady => _instance != null;

        public static void Enqueue(Action action)
        {
            if (action == null) return;
            try { _queue.Enqueue(action); }
            catch { }
        }

        /// <summary>
        /// CĂ i dispatcher lĂªn chĂ­nh GameObject cá»§a plugin (gá»i 1 láº§n, main thread).
        /// </summary>
        public static MainThreadDispatcher Install(BasePlugin host)
        {
            if (_instance != null) return _instance;

            try { ClassInjector.RegisterTypeInIl2Cpp<MainThreadDispatcher>(); }
            catch (Exception ex) { Log140.Error("Register dispatcher loi: " + ex.Message); }

            _instance = host.AddComponent<MainThreadDispatcher>();
            return _instance;
        }

        private void Update()
        {
            int budget = 64;
            while (budget-- > 0 && _queue.TryDequeue(out Action action))
            {
                try { action(); }
                catch (Exception ex)
                {
                    // KhĂ´ng dĂ¹ng Debug.Log á»Ÿ Ä‘Ă¢y Ä‘á»ƒ trĂ¡nh Ä‘á»‡ quy vĂ´ háº¡n
                    Log140.Error("Dispatcher action loi: " + ex);
                }
            }
        }
    }
}
