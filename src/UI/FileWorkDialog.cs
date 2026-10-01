using System;
using System.Drawing;
using System.Threading;
using System.Runtime.ExceptionServices;
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
            using (FileWorkDialog dialog = new FileWorkDialog())
            {
                dialog.Shown += delegate
                {
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        try { result = work(); } catch (Exception error) { failure = error; }
                        finally { dialog.BeginInvoke((Action)delegate { dialog.finished = true; dialog.Close(); }); }
                    });
                };
                dialog.ShowDialog(owner);
            }
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
            return result;
        }
    }
}
