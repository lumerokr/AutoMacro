using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace AutoMacro
{
    internal sealed class StartupNotice : Form
    {
        internal static string Responsibility { get { return L.T("매크로 프로그램 사용의 책임은 온전히 사용자 본인에게 있습니다."); } }
        internal static string Caution { get { return L.T("안티치트, 보안 프로그램 등이 가동중인 곳에서는 사용을 자제해주세요.\n이 프로그램은 어떠한 우회 기능도 제공하지 않습니다."); } }
        readonly string noticePath;
        readonly Label error = new Label();
        internal readonly Button Confirm = new ModernButton();
        internal StartupNotice(string path)
        {
            noticePath = path;
            Text = L.T("Auto Macro · 사용 안내"); ClientSize = new Size(580, 340);
            Font = new Font("맑은 고딕", 10); BackColor = Theme.Background; ForeColor = Theme.Ink;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            using (Stream stream = typeof(StartupNotice).Assembly.GetManifestResourceStream("AutoMacro.ico"))
                if (stream != null) using (Icon icon = new Icon(stream, new Size(48, 48))) Icon = (Icon)icon.Clone();
            Label title = new Label { Text = L.T("사용 전 안내"), Font = new Font("맑은 고딕", 19, FontStyle.Bold), AutoSize = true };
            title.Location = new Point(28, 24); Controls.Add(title);
            Label first = new Label { Text = Responsibility }; first.SetBounds(30, 91, 520, 46); Controls.Add(first);
            Label second = new Label { Text = Caution }; second.SetBounds(30, 143, 520, 86); Controls.Add(second);
            error.SetBounds(30, 240, 520, 34); error.ForeColor = Theme.Muted; Controls.Add(error);
            Confirm.Text = L.T("이해했습니다."); Confirm.SetBounds(30, 284, 520, 38); Theme.Button(Confirm, true); Controls.Add(Confirm);
            Confirm.Click += delegate
            {
                try { SaveAcknowledgement(noticePath); DialogResult = DialogResult.OK; Close(); }
                catch (Exception) { error.Text = L.T("확인 여부를 저장하지 못했습니다. 쓰기 가능한 폴더에서 실행해주세요."); }
            };
            AcceptButton = Confirm;
        }
        internal static bool IsAcknowledged(string path)
        {
            try
            {
                if (!DataStore.Exists(path)) return false;
                DataNode root = SettingsStore.Read(path);
                bool accepted = (int?)root.Element("NoticeVersion") == 1 && (bool?)root.Element("Acknowledged") == true;
                
                return accepted;
            }
            catch (Exception) { return false; }
        }
        internal static void SaveAcknowledgement(string path)
        {
            SettingsStore.Update(path, new DataNode("NoticeVersion", 1), new DataNode("Acknowledged", true));
        }
        internal static bool AllowStartup(string path)
        {
            if (IsAcknowledged(path)) return true;
            using (StartupNotice notice = new StartupNotice(path)) return notice.ShowDialog() == DialogResult.OK;
        }
    }
}
