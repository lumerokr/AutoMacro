using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace AutoMacro
{
    internal static class ReleaseNotesFormat
    {
        internal static void Render(RichTextBox box, string notes)
        {
            StringBuilder plain = new StringBuilder(); System.Collections.Generic.List<int[]> headings = new System.Collections.Generic.List<int[]>();
            using (StringReader reader = new StringReader(notes))
            {
                string line; bool code = false;
                while ((line = reader.ReadLine()) != null)
                {
                    if (Regex.IsMatch(line, @"^\s{0,3}(`{3,}|~{3,})")) { code = !code; continue; }
                    Match heading = code ? Match.Empty : Regex.Match(line, @"^#{1,6}\s+(.+?)\s*#*\s*$");
                    string display = heading.Success ? heading.Groups[1].Value : code ? line : Regex.Replace(line, @"^\s*[-*+]\s+", "• ");
                    if (!code)
                    {
                        display = Regex.Replace(display, @"\*\*(.+?)\*\*", "$1");
                        display = Regex.Replace(display, @"\\([\\`*_{}\[\]()#+\-.!~])", "$1").TrimEnd('\\');
                    }
                    if (heading.Success) headings.Add(new int[] { plain.Length, display.Length });
                    plain.Append(display).Append('\n');
                }
            }
            box.Text = plain.ToString().TrimEnd('\n'); box.SelectAll(); box.SelectionFont = box.Font; box.SelectionColor = Theme.Ink;
            using (Font headingFont = new Font(box.Font.FontFamily, 12, FontStyle.Bold))
                foreach (int[] heading in headings) { box.Select(heading[0], heading[1]); box.SelectionFont = headingFont; box.SelectionColor = Theme.Accent; }
            box.Select(0, 0);
        }
        internal static string ChangesOnly(string notes, string language = null)
        {
            if (String.IsNullOrEmpty(notes)) return "";
            MatchCollection languages = Regex.Matches(notes, @"(?m)^#[ \t]+(한국어|English|日本語)[ \t]*\r?$", RegexOptions.IgnoreCase);
            if (languages.Count > 0)
            {
                string preferred = (language ?? L.Current) == "ko" ? "한국어" : (language ?? L.Current) == "ja" ? "日本語" : "English";
                int selected = -1, english = -1;
                for (int index = 0; index < languages.Count; index++)
                {
                    string name = languages[index].Groups[1].Value;
                    if (String.Equals(name, preferred, StringComparison.OrdinalIgnoreCase)) selected = index;
                    if (String.Equals(name, "English", StringComparison.OrdinalIgnoreCase)) english = index;
                }
                if (selected < 0) selected = english < 0 ? 0 : english;
                int start = languages[selected].Index + languages[selected].Length;
                int end = selected + 1 < languages.Count ? languages[selected + 1].Index : notes.Length;
                notes = notes.Substring(start, end - start).TrimStart();
            }
            StringBuilder result = new StringBuilder(); string fence = null;
            using (StringReader reader = new StringReader(notes))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    Match code = Regex.Match(line, @"^\s{0,3}(`{3,}|~{3,})");
                    if (code.Success)
                    {
                        string marker = code.Groups[1].Value;
                        if (fence == null) fence = marker;
                        else if (marker[0] == fence[0] && marker.Length >= fence.Length && String.IsNullOrWhiteSpace(line.Substring(code.Length))) fence = null;
                    }
                    else if (fence == null)
                    {
                        // Both Markdown headings and legacy plain section titles are supported.
                        string title = Regex.Replace(line.Trim(), @"^#{1,6}\s+", "").TrimEnd('#').Trim().Trim('*').Trim();
                        bool cutoff = false;
                        foreach (string section in new string[] { "다운로드", "안내", "Downloads", "Download", "Notice", "Notices", "ダウンロード", "ご案内", "注意事項" })
                            if (String.Equals(title, section, StringComparison.OrdinalIgnoreCase)) { cutoff = true; break; }
                        if (cutoff) break;
                    }
                    result.AppendLine(line);
                }
            }
            return result.ToString().TrimEnd();
        }
    }
}
