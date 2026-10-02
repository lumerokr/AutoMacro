using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace AutoMacro
{
    internal sealed class UpdateNotesDialog : Form
    {
        readonly RichTextBox content;
        readonly Label status;
        readonly Button retry;
        readonly UiBackgroundWork background;
        readonly Func<CancellationToken, ReleaseUpdate> fetch;
        bool busy, closing;
        readonly bool afterUpdate;
        static ReleaseUpdate lastNotes;
        internal UpdateNotesDialog(ReleaseUpdate cached = null, Func<CancellationToken, ReleaseUpdate> fetch = null, bool afterUpdate = true)
        {
            background = new UiBackgroundWork(this);
            this.afterUpdate = afterUpdate;
            if (cached == null && fetch == null && lastNotes != null && lastNotes.Version == UpdateService.ParseVersion(AppInfo.Version)) cached = lastNotes;
            this.fetch = fetch ?? UpdateService.ReadInstalledRelease;
            Text = L.T(afterUpdate ? "업데이트 완료" : "업데이트 내용"); ClientSize = new Size(640, 520); MinimumSize = new Size(640, 440);
            Font = new Font("맑은 고딕", 10); BackColor = Theme.Background; ForeColor = Theme.Ink;
            StartPosition = FormStartPosition.CenterParent; MaximizeBox = MinimizeBox = false;
            Label title = new Label { Text = "Auto Macro " + AppInfo.Version, ForeColor = Theme.Accent, Font = new Font(Font.FontFamily, 16, FontStyle.Bold) };
            title.SetBounds(24, 20, 592, 38); title.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; Controls.Add(title);
            status = new Label { Text = L.T("업데이트 내용을 불러오고 있습니다."), ForeColor = Theme.Muted };
            status.SetBounds(24, 64, 592, 38); status.Anchor = title.Anchor; Controls.Add(status);
            content = new RichTextBox { ReadOnly = true, DetectUrls = false, BorderStyle = BorderStyle.None, BackColor = Theme.Background, ForeColor = Theme.Ink, Font = Font, ScrollBars = RichTextBoxScrollBars.Vertical, TabStop = false };
            content.SetBounds(24, 104, 592, 328); content.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right; Controls.Add(content);
            retry = new ModernButton { Text = L.T("다시 시도"), Visible = false };
            retry.SetBounds(24, 452, 132, 44); retry.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; Theme.Button(retry, false); Controls.Add(retry);
            Button page = new ModernButton { Text = L.T("릴리스 페이지") };
            page.SetBounds(168, 452, 192, 44); page.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; Theme.Button(page, false); Controls.Add(page);
            Button close = new ModernButton { Text = L.T("닫기"), DialogResult = DialogResult.OK };
            close.SetBounds(484, 452, 132, 44); close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right; Theme.Button(close, true); Controls.Add(close); AcceptButton = CancelButton = close;
            retry.Click += delegate { LoadNotes(); };
            page.Click += delegate
            {
                try { Process.Start(new ProcessStartInfo("https://github.com/lumerokr/AutoMacro/releases/tag/v" + AppInfo.Version) { UseShellExecute = true }); }
                catch { status.Text = L.T("릴리스 페이지를 열지 못했습니다."); }
            };
            Shown += delegate { if (cached != null && cached.Version == UpdateService.ParseVersion(AppInfo.Version)) Display(cached); else LoadNotes(); };
        }
        void Display(ReleaseUpdate release)
        {
            if (release == null || release.Version != UpdateService.ParseVersion(AppInfo.Version)) throw new UpdateFailure(UpdateService.Invalid);
            string notes = ReleaseNotesFormat.ChangesOnly(release.Notes);
            lastNotes = release;
            ReleaseNotesFormat.Render(content, String.IsNullOrWhiteSpace(notes) ? L.T("이 버전에는 등록된 업데이트 내용이 없습니다.") : notes);
            content.SelectionStart = 0; content.SelectionLength = 0; content.ScrollToCaret();
            status.Text = L.T(afterUpdate ? "업데이트가 완료되었습니다. 변경 내용을 확인하세요." : "현재 버전의 변경 내역입니다."); retry.Visible = false;
        }
        void LoadNotes()
        {
            if (busy || closing) return;
            busy = true; retry.Enabled = false; status.Text = L.T("업데이트 내용을 불러오고 있습니다.");
            background.Run(delegate(CancellationToken token)
            {
                ReleaseUpdate release = null; bool failed = false;
                try { release = fetch(token); if (release == null || release.Version != UpdateService.ParseVersion(AppInfo.Version)) failed = true; }
                catch (OperationCanceledException) { return; }
                catch { failed = true; }
                if (token.IsCancellationRequested) return;
                background.Post(delegate
                    {
                        if (closing || IsDisposed) return;
                        busy = false; retry.Enabled = true;
                        if (failed) { status.Text = L.T("업데이트 내용만 불러오지 못했습니다. 프로그램은 정상적으로 사용할 수 있습니다."); retry.Visible = true; }
                        else Display(release);
                    });
            });
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { closing = true; background.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
