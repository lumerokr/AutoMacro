using System;
using System.Threading;
using System.Windows.Forms;

namespace AutoMacro
{
    internal sealed class StartupUpdateCheck : IDisposable
    {
        readonly Form owner;
        readonly Func<bool> canNotify;
        readonly Action<ReleaseUpdate> notify;
        readonly Func<CancellationToken, ReleaseUpdate> check;
        readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        readonly System.Windows.Forms.Timer retry = new System.Windows.Forms.Timer { Interval = 250 };
        ReleaseUpdate pending;
        bool started;
        volatile bool disposed;
        internal StartupUpdateCheck(Form owner, Func<bool> canNotify, Action<ReleaseUpdate> notify, Func<CancellationToken, ReleaseUpdate> check = null)
        {
            this.owner = owner; this.canNotify = canNotify; this.notify = notify;
            this.check = check ?? UpdateService.Check;
            owner.Shown += OnShown; owner.FormClosed += OnClosed;
            retry.Tick += delegate { TryNotify(); };
        }
        internal static bool IsNewer(ReleaseUpdate release)
        { return release != null && release.Version != null && release.Version > UpdateService.ParseVersion(AppInfo.Version); }
        void OnShown(object sender, EventArgs args)
        {
            if (started || disposed) return;
            started = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    ReleaseUpdate found = check(cancellation.Token);
                    if (!IsNewer(found) || disposed || cancellation.IsCancellationRequested) return;
                    owner.BeginInvoke((Action)delegate
                    {
                        if (disposed || owner.IsDisposed) return;
                        pending = found; retry.Start(); TryNotify();
                    });
                }
                catch (Exception) { /* Startup checks are silent on network, parsing, or cancellation errors. */ }
            });
        }
        void TryNotify()
        {
            if (disposed || pending == null || !owner.Visible || !owner.Enabled || !canNotify()) return;
            ReleaseUpdate found = pending; pending = null; retry.Stop();
            notify(found); // Exactly one notification per successful startup check.
        }
        void OnClosed(object sender, FormClosedEventArgs args) { Dispose(); }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; cancellation.Cancel(); pending = null;
            retry.Stop(); retry.Dispose();
            owner.Shown -= OnShown; owner.FormClosed -= OnClosed;
        }
    }
}
