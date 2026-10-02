using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutoMacro
{
    internal sealed class KeyPicker : Form
    {
        internal Keys SelectedKey;
        readonly Func<Keys, string> validate;
        readonly Label hint = new Label();
        bool accepted;
        readonly bool allowMouse;
        ThumbHook mouseHook;
        internal KeyPicker(string purpose, Func<Keys, string> validateKey, bool mouse = false)
        {
            allowMouse = mouse;
            validate = validateKey;
            Text = purpose; ClientSize = new Size(470, 145); Font = new Font("맑은 고딕", 10);
            BackColor = Theme.Card; ForeColor = Theme.Ink;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            hint.Text = (allowMouse ? L.T("키보드 키 또는 마우스 4·5번 버튼을 눌렀다가 떼세요.") : L.T("지정할 키를 한 번 눌렀다가 떼세요.")) + L.T("\n다른 기능에 지정된 키는 사용할 수 없습니다.\n취소하려면 창의 X 버튼을 누르세요.");
            hint.SetBounds(20, 20, 430, 110); Controls.Add(hint);
        }
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!allowMouse) return;
            mouseHook = new ThumbHook(delegate(Keys key, bool down)
            {
                if (down && !accepted) AcceptKey(key);
                else if (!down && accepted && SelectedKey == key)
                    BeginInvoke((Action)delegate { DialogResult = DialogResult.OK; Close(); });
                return true;
            });
            if (!mouseHook.Install()) hint.Text = L.T("마우스 버튼 감지 등록에 실패했습니다.\n창을 닫고 다시 시도하거나 키보드 키를 지정하세요.");
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && mouseHook != null) { mouseHook.Dispose(); mouseHook = null; }
            base.Dispose(disposing);
        }
        void AcceptKey(Keys key)
        {
            string error = validate(key);
            if (error != null) { hint.Text = error + L.T("\n다른 키를 누르세요. 취소: 창의 X 버튼"); return; }
            SelectedKey = key; accepted = true; hint.Text = InputRules.KeyName(key) + L.T(" 버튼/키를 떼면 설정됩니다.");
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            if (accepted) return true;
            AcceptKey(key); return true;
        }
        protected override void WndProc(ref Message m)
        {
            if ((m.Msg == 0x0101 || m.Msg == 0x0105) && accepted && (Keys)m.WParam.ToInt32() == SelectedKey)
            { DialogResult = DialogResult.OK; Close(); return; }
            base.WndProc(ref m);
        }
        internal bool VerifyCaptureRelease()
        {
            Message down = Message.Create(Handle, 0x0100, (IntPtr)(int)Keys.F6, IntPtr.Zero);
            ProcessCmdKey(ref down, Keys.F6);
            if (accepted) return false;
            down = Message.Create(Handle, 0x0100, (IntPtr)(int)Keys.A, IntPtr.Zero);
            ProcessCmdKey(ref down, Keys.A);
            if (!accepted || SelectedKey != Keys.A || DialogResult != DialogResult.None) return false;
            Message up = Message.Create(Handle, 0x0101, (IntPtr)(int)Keys.A, IntPtr.Zero);
            WndProc(ref up);
            return DialogResult == DialogResult.OK;
        }
    }
}
