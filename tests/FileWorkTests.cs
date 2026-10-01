using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace AutoMacro
{
    internal static class FileWorkTests
    {
        static void Check(bool value, string name) { if (!value) throw new Exception("File work test: " + name); }
        static void Reject(Action action, string name)
        { bool rejected = false; try { action(); } catch (LibraryLimitException) { rejected = true; } Check(rejected, name); }
        internal static void Run()
        {
            Check(typeof(AppInfo).Assembly.GetType("AutoMacro.MacroTests") == null && typeof(AppInfo).Assembly.GetType("AutoMacro.TestProgram") == null, "test implementation is excluded from application assembly");
            SavedMacro sample = new SavedMacro { Name = "sample" };
            sample.Actions.Add(new MacroAction { Kind = ActionKind.Move });
            MacroLibrary library = new MacroLibrary();
            for (int i = 0; i < MacroLibrary.MaxMacros; i++) library.Items.Add(sample);
            library.ValidateCapacity();
            Reject(delegate { library.ValidateCapacity(new SavedMacro[] { sample }); }, "merge beyond macro limit is rejected before copying");
            library.Items.Clear();
            for (int i = 0; i < 3; i++)
            {
                SavedMacro big = new SavedMacro(); int count = i == 2 ? 100000 : 200000;
                for (int n = 0; n < count; n++) big.Actions.Add(sample.Actions[0]);
                library.Items.Add(big);
            }
            library.ValidateCapacity();
            library.Items[2].Actions.Add(sample.Actions[0]);
            Reject(delegate { library.ValidateCapacity(); }, "total action limit counts all macros");
            string path = Path.Combine(Path.GetTempPath(), "AutoMacro-limits-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(path, "original file");
                Reject(delegate { library.Save(path); }, "over-limit save is rejected");
                Check(File.ReadAllText(path) == "original file" && !File.Exists(path + ".tmp"), "rejected save preserves original data");
                StringBuilder json = new StringBuilder("{\"type\":\"MacroLibrary\",\"macros\":[");
                for (int i = 0; i <= MacroLibrary.MaxMacros; i++) { if (i > 0) json.Append(','); json.Append("{}"); }
                json.Append("]}"); File.WriteAllText(path, json.ToString());
                Reject(delegate { MacroLibrary.Load(path); }, "JSON macro count is rejected before decoding items");
                json.Clear(); json.Append("{\"type\":\"MacroLibrary\",\"macros\":[{\"actions\":[");
                for (int i = 0; i <= MacroLibrary.MaxActionsPerMacro; i++) { if (i > 0) json.Append(','); json.Append('0'); }
                json.Append("]}]}"); File.WriteAllText(path, json.ToString());
                Reject(delegate { MacroLibrary.Load(path); }, "JSON per-macro action count is rejected before decoding entries");
                library = new MacroLibrary(); library.Items.Add(sample); library.Save(path);
                SavedMacro copy = MacroEditing.Copy(sample, true); copy.Name = "SAMPLE"; library.Items.Add(copy); library.Save(path);
                bool duplicate = false; try { MacroLibrary.Load(path); } catch (FormatException) { duplicate = true; }
                Check(duplicate, "indexed name validation still rejects duplicate names");
            }
            finally { foreach (string file in new string[] { path, path + ".bak", path + ".tmp" }) if (File.Exists(file)) File.Delete(file); }
        }
        internal static void RunUI()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            using (MainForm form = new MainForm(false))
            using (System.Windows.Forms.Timer probe = new System.Windows.Forms.Timer())
            {
                form.Show(); Application.DoEvents();
                MacroWorkspace page = (MacroWorkspace)typeof(MainForm).GetField("macroWorkspace", flags).GetValue(form);
                int uiThread = Thread.CurrentThread.ManagedThreadId; bool responsive = false;
                probe.Interval = 10; probe.Tick += delegate
                {
                    if (!page.Busy) return;
                    responsive = true; probe.Stop();
                    form.Close(); Check(!form.IsDisposed, "close is deferred while file operation is pending");
                }; probe.Start();
                Func<bool> work = delegate
                { Check(Thread.CurrentThread.ManagedThreadId != uiThread, "file operation runs off UI thread"); Thread.Sleep(200); return true; };
                bool result = (bool)typeof(MacroWorkspace).GetMethod("RunFileWork", flags).MakeGenericMethod(typeof(bool)).Invoke(page, new object[] { work });
                Check(result && responsive && !page.Busy, "UI continues processing during file work and leaves busy state afterward");
                Stopwatch timeout = Stopwatch.StartNew();
                while (!form.IsDisposed && timeout.ElapsedMilliseconds < 3000) { Application.DoEvents(); Thread.Sleep(5); }
                Check(form.IsDisposed, "deferred close completes after file work");
            }
            using (Form owner = new Form())
            {
                owner.Show(); bool failed = false;
                try { FileWorkDialog.Run<bool>(owner, delegate { throw new IOException("simulated disk failure"); }); } catch (IOException) { failed = true; }
                Check(failed && owner.Enabled, "worker failure restores owner and reaches caller"); owner.Close();
            }
        }
    }
}
