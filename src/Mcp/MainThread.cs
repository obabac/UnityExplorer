using System;
using System.Threading;
#if INTEROP
using System.Threading.Tasks;
#endif

#nullable enable

namespace UnityExplorer.Mcp
{
#if INTEROP
    internal static class MainThread
    {
        private static SynchronizationContext? _context;

        public static void Capture()
        {
            _context = SynchronizationContext.Current;
        }

        public static bool IsCaptured => _context != null;

        public static Task Run(Action action)
        {
            if (_context == null)
            {
                action();
                return Task.CompletedTask;
            }
            if (SynchronizationContext.Current == _context)
            {
                action();
                return Task.CompletedTask;
            }
            var tcs = new TaskCompletionSource<object?>();
            _context.Post(_ =>
            {
                try { action(); tcs.SetResult(null); }
                catch (Exception ex) { tcs.SetException(ex); }
            }, null);
            return tcs.Task;
        }

        public static Task<T> Run<T>(Func<T> func)
        {
            if (_context == null)
            {
                return Task.FromResult(func());
            }
            if (SynchronizationContext.Current == _context)
            {
                return Task.FromResult(func());
            }
            var tcs = new TaskCompletionSource<T>();
            _context.Post(_ =>
            {
                try { tcs.SetResult(func()); }
                catch (Exception ex) { tcs.SetException(ex); }
            }, null);
            return tcs.Task;
        }

        public static Task<T> RunAsync<T>(Func<Task<T>> func)
        {
            if (_context == null)
            {
                return func();
            }
            var tcs = new TaskCompletionSource<T>();
            _context.Post(async _ =>
            {
                try { tcs.SetResult(await func().ConfigureAwait(false)); }
                catch (Exception ex) { tcs.SetException(ex); }
            }, null);
            return tcs.Task;
        }

        public static Task RunAsync(Func<Task> func)
        {
            if (_context == null)
            {
                return func();
            }
            var tcs = new TaskCompletionSource<object?>();
            _context.Post(async _ =>
            {
                try { await func().ConfigureAwait(false); tcs.SetResult(null); }
                catch (Exception ex) { tcs.SetException(ex); }
            }, null);
            return tcs.Task;
        }
    }
#elif MONO
    internal static class MainThread
    {
        private static SynchronizationContext? _context;

        public static void Capture()
        {
            _context = SynchronizationContext.Current;
        }

        public static bool IsCaptured => _context != null;

        public static void Run(Action action)
        {
            if (action == null) return;
            if (_context == null)
            {
                action();
                return;
            }
            if (SynchronizationContext.Current == _context)
            {
                action();
                return;
            }

            Exception? ex = null;
            using (var done = new ManualResetEvent(false))
            {
                _context.Post(_ =>
                {
                    try { action(); }
                    catch (Exception e) { ex = e; }
                    finally { done.Set(); }
                }, null);
                done.WaitOne();
            }
            if (ex != null) throw ex;
        }

        public static T Run<T>(Func<T> func)
        {
            if (func == null) return default!;
            if (_context == null)
            {
                return func();
            }
            if (SynchronizationContext.Current == _context)
            {
                return func();
            }

            T result = default!;
            Exception? ex = null;
            using (var done = new ManualResetEvent(false))
            {
                _context.Post(_ =>
                {
                    try { result = func(); }
                    catch (Exception e) { ex = e; }
                    finally { done.Set(); }
                }, null);
                done.WaitOne();
            }
            if (ex != null) throw ex;
            return result;
        }

        public static void RunAsync(Action action)
        {
            ThreadPool.QueueUserWorkItem(_ => Run(action));
        }
    }
#else
    internal static class MainThread
    {
        public static void Capture() { }
        public static bool IsCaptured => false;
        public static void Run(Action action) { action(); }
        public static T Run<T>(Func<T> func) => func();
        public static void RunAsync(Action action) { action(); }
    }
#endif
}
