using System;
using System.Threading;
using System.Windows.Forms;

namespace AutoMacro
{
    // Cancellation stays alive until the final worker has released its token registrations.
    internal sealed class UiBackgroundWork : IDisposable
    {
        readonly Form owner;
        readonly object gate = new object();
        readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        int workers;
        volatile bool closed;
        internal UiBackgroundWork(Form owner) { this.owner = owner; }
        internal bool Run(Action<CancellationToken> action)
        {
            CancellationToken token;
            lock (gate) { if (closed) return false; workers++; token = cancellation.Token; }
            try
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try { action(token); }
                    catch (OperationCanceledException) { }
                    finally { Complete(); }
                });
                return true;
            }
            catch { Complete(); throw; }
        }
        void Complete()
        { lock (gate) { workers--; if (closed && workers == 0) cancellation.Dispose(); } }
        internal void Post(Action action)
        {
            if (closed || owner.IsDisposed || owner.Disposing || !owner.IsHandleCreated) return;
            try { owner.BeginInvoke((Action)delegate { if (!closed && !owner.IsDisposed && !owner.Disposing) action(); }); }
            catch (InvalidOperationException) { }
        }
        public void Dispose()
        {
            lock (gate)
            {
                if (closed) return;
                closed = true; cancellation.Cancel();
                if (workers == 0) cancellation.Dispose();
            }
        }
    }
}
