using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace AutoMacro
{
    internal sealed class UpdateDialog : Form
    {
        internal PreparedUpdate Prepared;
        readonly Label latest, message;
        readonly Button check, install;
        readonly ProgressBar progress;
        readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        ReleaseUpdate release;
        bool busy;
        volatile bool closing;
        readonly object gate = new object();
        internal UpdateDialog(ReleaseUpdate detected = null, bool notification = false)
        {
            Text = L.T(notification ? "새 버전 감지" : "업데이트"); ClientSize = new Size(480, 318); Font = new Font("맑은 고딕", 10);
            BackColor = Theme.Background; ForeColor = Theme.Ink; StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
            Label current = new Label { Text = String.Format(L.T("현재 버전: {0}"), AppInfo.Version), ForeColor = Theme.Accent };
            current.SetBounds(24, 24, 432, 30); Controls.Add(current);
            latest = new Label { Text = L.T("최신 버전: 확인 전") }; latest.SetBounds(24, 64, 432, 30); Controls.Add(latest);
            message = new Label { Text = L.T("업데이트 확인을 눌러 최신 버전을 확인하세요."), ForeColor = Theme.Muted };
            message.SetBounds(24, 108, 432, 86); Controls.Add(message);
            progress = new ProgressBar { Visible = false }; progress.SetBounds(24, 210, 432, 10); Controls.Add(progress);
            check = new ModernButton { Text = L.T(notification ? "나중에" : "업데이트 확인") }; install = new ModernButton { Text = L.T("최신 버전으로 업데이트"), Enabled = false };
            check.SetBounds(24, 250, 156, 44); install.SetBounds(192, 250, 264, 44);
            Theme.Button(check, false); Theme.Button(install, true); Controls.Add(check); Controls.Add(install);
            check.Click += delegate { if (notification) { DialogResult = DialogResult.Cancel; Close(); } else Run(false); };
            install.Click += delegate
            {
                if (L.Confirm(this, L.T("최신 버전으로 업데이트하고 프로그램을 다시 시작할까요? 설정과 저장된 매크로는 유지됩니다."), false) == DialogResult.Yes) Run(true);
            };
            if (detected != null)
            {
                ShowRelease(detected);
                if (notification && install.Enabled) message.Text = L.T("새로운 최신 버전이 감지되었습니다. 지금 업데이트할 수 있습니다.");
            }
        }
        void Post(Action action)
        {
            if (closing || IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke((Action)delegate { if (!closing && !IsDisposed) action(); }); } catch (InvalidOperationException) { }
        }
        void Run(bool download)
        {
            if (busy) return;
            busy = true; check.Enabled = install.Enabled = false;
            if (!download) { release = null; latest.Text = L.T("최신 버전: 확인 중"); }
            message.Text = L.T(download ? "업데이트 파일을 다운로드하고 검증하고 있습니다." : "최신 버전을 확인하고 있습니다.");
            progress.Value = 0; progress.Style = download ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee; progress.Visible = true;
            ReleaseUpdate selected = release;
            ThreadPool.QueueUserWorkItem(delegate
            {
                PreparedUpdate prepared = null;
                try
                {
                    if (download)
                    {
                        prepared = UpdateService.Prepare(selected, cancellation.Token, delegate(int value) { Post(delegate { progress.Value = value; }); });
                        PreparedUpdate ready = prepared;
                        // A close/cancel never leaves a prepared update or installs anything.
                        lock (gate)
                        {
                            if (cancellation.IsCancellationRequested || closing) { ready.Discard(); return; }
                            Prepared = ready;
                        }
                        Post(delegate { busy = false; DialogResult = DialogResult.OK; Close(); });
                    }
                    else
                    {
                        ReleaseUpdate found = UpdateService.Check(cancellation.Token);
                        Post(delegate { ShowRelease(found); });
                    }
                }
                catch (OperationCanceledException) { if (prepared != null) prepared.Discard(); }
                catch (Exception error)
                {
                    string text = error is UpdateFailure ? error.Message : UpdateService.Network;
                    Post(delegate { busy = false; progress.Visible = false; check.Enabled = true; install.Enabled = false; if (!download) latest.Text = L.T("최신 버전: 확인 실패"); message.Text = L.T(text); });
                }
            });
        }
        internal void ShowRelease(ReleaseUpdate found)
        {
            release = found; busy = false; progress.Visible = false; check.Enabled = true; install.Enabled = false;
            if (found == null) { latest.Text = L.T("최신 버전: 등록된 릴리스가 없습니다."); message.Text = L.T("아직 공개된 정식 릴리스가 없습니다. 나중에 다시 확인하세요."); return; }
            latest.Text = String.Format(L.T("최신 버전: {0}"), found.Version.ToString(3));
            if (found.Version <= UpdateService.ParseVersion(AppInfo.Version)) message.Text = L.T("현재 최신 버전을 사용 중입니다.");
            else if (found.Url == null) message.Text = L.T("새 버전이 있지만 업데이트 파일이 아직 등록되지 않았습니다.");
            else { message.Text = L.T("새 버전을 사용할 수 있습니다. 업데이트 후 자동으로 다시 시작합니다."); install.Enabled = true; }
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            lock (gate) { closing = true; cancellation.Cancel(); if (Prepared != null && DialogResult != DialogResult.OK) { Prepared.Discard(); Prepared = null; } }
            base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) lock (gate)
            { closing = true; cancellation.Cancel(); if (Prepared != null && DialogResult != DialogResult.OK) { Prepared.Discard(); Prepared = null; } }
            base.Dispose(disposing);
        }
    }
}
