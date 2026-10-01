using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutoMacro
{
    internal sealed class SettingsRecoveryDialog : Form
    {
        internal SettingsRecoveryDialog(string path)
        {
            Text = L.T("설정 복구"); ClientSize = new Size(500, 230); Font = new Font("맑은 고딕", 10);
            BackColor = Theme.Background; ForeColor = Theme.Ink; StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
            Label message = new Label { Text = L.T("설정 파일이 손상되었습니다. 원본을 백업하고 기본 설정으로 복구할 수 있습니다. 저장된 매크로는 유지됩니다.") };
            message.SetBounds(24, 24, 452, 92); Controls.Add(message);
            Label error = new Label { ForeColor = Theme.Muted }; error.SetBounds(24, 118, 452, 48); Controls.Add(error);
            Button reset = new ModernButton { Text = L.T("기본 설정으로 복구") }, cancel = new ModernButton { Text = L.T("종료"), DialogResult = DialogResult.Cancel };
            reset.SetBounds(24, 178, 278, 34); cancel.SetBounds(316, 178, 160, 34); Theme.Button(reset, true); Theme.Button(cancel, false);
            Controls.Add(reset); Controls.Add(cancel); CancelButton = cancel;
            reset.Click += delegate
            {
                try { SettingsStore.Reset(path); DialogResult = DialogResult.OK; Close(); }
                catch (Exception) { error.Text = L.T("복구하지 못했습니다. 폴더 권한과 여유 공간을 확인하세요."); }
            };
        }
        internal static bool AllowStartup(string path)
        {
            if (SettingsStore.IsValid(path)) return true;
            using (SettingsRecoveryDialog dialog = new SettingsRecoveryDialog(path)) return dialog.ShowDialog() == DialogResult.OK;
        }
    }
}
