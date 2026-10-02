using System;
using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;

namespace AutoMacro
{
    internal sealed class FileWorkDialog : Form
    {
        bool finished;
        FileWorkDialog()
        {
            SettingsUI.Configure(this, "처리 중", new Size(380, 114)); ControlBox = false;
            Label label = new Label { Text = L.T("매크로 파일을 처리하고 있습니다."), ForeColor = Theme.Ink };
            label.SetBounds(20, 18, 340, 28); Controls.Add(label);
            ProgressBar progress = new ProgressBar { Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30 };
            progress.SetBounds(20, 62, 340, 20); Controls.Add(progress);
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        { if (!finished) e.Cancel = true; base.OnFormClosing(e); }
        internal static T Run<T>(IWin32Window owner, Func<T> work)
        {
            T result = default(T); Exception failure = null;
            bool started = false;
            using (ManualResetEvent completed = new ManualResetEvent(false))
            using (FileWorkDialog dialog = new FileWorkDialog())
            using (UiBackgroundWork background = new UiBackgroundWork(dialog))
            {
                dialog.Shown += delegate
                {
                    started = background.Run(delegate
                    {
                        try { result = work(); } catch (Exception error) { failure = error; }
                        finally { completed.Set(); background.Post(delegate { dialog.finished = true; dialog.Close(); }); }
                    });
                };
                // Atomic file operations must finish even if their progress window is disposed.
                // Do not return a default value while a save/restore is still writing.
                try { dialog.ShowDialog(owner); }
                finally { if (started) completed.WaitOne(); }
            }
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
            return result;
        }
    }
}
