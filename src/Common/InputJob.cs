using System;
using System.Threading;

namespace AutoMacro
{
    // Each run owns its cancellation handle until its input cleanup has finished.
    internal sealed class InputJob : IDisposable
    {
        readonly object gate = new object();
        readonly ManualResetEvent cancel = new ManualResetEvent(false);
        readonly Thread thread;
        bool disposed, finished;
        internal Exception Error { get; private set; }
        internal bool Completed { get { lock (gate) { return finished && !thread.IsAlive; } } }
        internal InputJob(Action<WaitHandle> action)
        {
            thread = new Thread(delegate()
            {
                try { action(cancel); }
                catch (Exception error) { Error = error; }
                finally
                {
                    lock (gate) { finished = true; if (disposed) cancel.Dispose(); }
                }
            });
            thread.IsBackground = true;
            try { thread.Start(); }
            catch { cancel.Dispose(); throw; }
        }
        internal void Stop()
        { lock (gate) { if (!disposed && !finished) cancel.Set(); } }
        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                if (finished) cancel.Dispose(); else cancel.Set();
            }
        }
    }
}
