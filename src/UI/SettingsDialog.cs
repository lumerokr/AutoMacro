using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AutoMacro
{
    internal static class SettingsUI
    {
        internal static void Configure(Form form, string title, Size size)
        {
            form.Text = L.T(title); form.ClientSize = size;
            form.Font = new Font("맑은 고딕", 10); form.BackColor = Theme.Background; form.ForeColor = Theme.Ink;
            form.FormBorderStyle = FormBorderStyle.FixedDialog; form.MaximizeBox = form.MinimizeBox = false;
            form.StartPosition = FormStartPosition.CenterParent;
        }
        internal static List<Control> Languages(Form form, int top, int width, Action<string> select)
        {
            List<Control> controls = new List<Control>();
            string[] languages = { "ko", "en", "ja" }, names = { "한국어", "English", "日本語" };
            for (int i = 0; i < languages.Length; i++)
            {
                string language = languages[i];
                Button button = new ModernButton { Text = names[i] + (language == L.Current ? "  ✓" : ""), Tag = language };
                button.SetBounds(24, top + i * 58, width, 44); Theme.Button(button, language == L.Current);
                button.Click += delegate { select(language); }; form.Controls.Add(button); controls.Add(button);
            }
            return controls;
        }
    }
    internal sealed class LanguageDialog : Form
    {
        internal string SelectedLanguage;
        internal LanguageDialog()
        {
            SettingsUI.Configure(this, "언어 설정", new Size(330, 216));
            SettingsUI.Languages(this, 24, 282, delegate(string language) { SelectedLanguage = language; DialogResult = DialogResult.OK; Close(); });
        }
    }
    internal sealed class SettingsDialog : Form
    {
        internal string SelectedLanguage;
        internal PreparedUpdate Prepared;
        internal readonly Button LanguageTab, OtherTab;
        readonly List<Control> languageControls, otherControls = new List<Control>();
        internal SettingsDialog(Action reset = null, Action restore = null)
        {
            SettingsUI.Configure(this, "설정", new Size(400, 268));
            languageControls = SettingsUI.Languages(this, 84, 352, delegate(string language)
            { SelectedLanguage = language; DialogResult = DialogResult.OK; Close(); });
            LanguageTab = new ModernButton { Text = L.T("언어 설정") }; OtherTab = new ModernButton { Text = L.T("기타 설정") };
            LanguageTab.SetBounds(24, 24, 170, 40); OtherTab.SetBounds(206, 24, 170, 40);
            Controls.Add(LanguageTab); Controls.Add(OtherTab);
            LanguageTab.Click += delegate { SelectPage(true); }; OtherTab.Click += delegate { SelectPage(false); };
            AddAction("설정 초기화", 84, reset);
            AddAction("매크로 백업 복원", 142, restore);
            Button update = new ModernButton { Text = L.T("업데이트") };
            update.SetBounds(24, 200, 352, 44); Theme.Button(update, true); Controls.Add(update); otherControls.Add(update);
            update.Click += delegate
            {
                using (UpdateDialog dialog = new UpdateDialog())
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    { Prepared = dialog.Prepared; DialogResult = DialogResult.Abort; Close(); }
            };
            SelectPage(false);
        }
        void AddAction(string text, int top, Action action)
        {
            Button button = new ModernButton { Text = L.T(text), Enabled = action != null };
            button.SetBounds(24, top, 352, 44); Theme.Button(button, false); Controls.Add(button); otherControls.Add(button);
            button.Click += delegate { if (action != null) action(); DialogResult = DialogResult.Cancel; Close(); };
        }
        void SelectPage(bool language)
        {
            foreach (Control control in languageControls) control.Visible = language;
            foreach (Control control in otherControls) control.Visible = !language;
            Theme.Button(LanguageTab, language); Theme.Button(OtherTab, !language);
        }
    }
}
