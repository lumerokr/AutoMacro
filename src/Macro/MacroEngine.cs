using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;


namespace AutoMacro
{
    internal enum ActionKind { Move, MouseDown, MouseUp, Wheel, HorizontalWheel, KeyDown, KeyUp }
    internal sealed class MacroAction
    {
        internal long At;
        internal ActionKind Kind;
        internal int X, Y, Code, Scan;
        internal bool Extended;
        internal bool HasClientPosition;
        internal int ClientX, ClientY;
        internal MacroAction Copy() { return (MacroAction)MemberwiseClone(); }
        internal bool Down { get { return Kind == ActionKind.KeyDown || Kind == ActionKind.MouseDown; } }
        internal bool Up { get { return Kind == ActionKind.KeyUp || Kind == ActionKind.MouseUp; } }
        internal string Identity { get { return (Kind == ActionKind.KeyDown || Kind == ActionKind.KeyUp ? "K" : "M") + Code; } }
        internal MacroAction Release(long at)
        {
            MacroAction release = Copy(); release.At = at;
            release.Kind = Kind == ActionKind.KeyDown ? ActionKind.KeyUp : ActionKind.MouseUp; return release;
        }
        internal string Describe()
        {
            if (Kind == ActionKind.KeyDown || Kind == ActionKind.KeyUp) return ((Keys)Code).ToString() + (Kind == ActionKind.KeyDown ? L.T(" 누름") : L.T(" 뗌"));
            if (Kind == ActionKind.Move) return L.T("마우스 이동");
            if (Kind == ActionKind.Wheel || Kind == ActionKind.HorizontalWheel) return (Kind == ActionKind.Wheel ? L.T("세로 휠 ") : L.T("가로 휠 ")) + Code;
            string name = Code == 1 ? L.T("왼쪽") : Code == 2 ? L.T("오른쪽") : Code == 3 ? L.T("가운데") : L.T("버튼 ") + Code;
            return L.T("마우스 ") + name + (Kind == ActionKind.MouseDown ? L.T(" 누름") : L.T(" 뗌"));
        }
    }
    internal sealed class SavedMacro
    {
        internal string Id = Guid.NewGuid().ToString("N"), Name = "";
        internal DateTime Created = DateTime.Now;
        internal DateTime Modified = DateTime.Now;
        internal long Duration;
        internal int RepeatCount = 1, RepeatDelayMs;
        internal bool RepeatForever;
        internal Keys RunKey = Keys.None;
        internal int SpeedPercent = 100;
        internal bool WindowRelative, OriginKnown;
        internal int RecordedOriginX, RecordedOriginY;
        internal List<MacroAction> Actions = new List<MacroAction>();
    }
    internal sealed class LibraryLimitException : FormatException
    {
        internal LibraryLimitException() : base(L.T("저장 한도를 초과했습니다. 최대 1,000개 매크로, 전체 500,000개 동작까지 저장할 수 있습니다.")) { }
    }
    internal sealed class MacroLibrary
    {
        internal const int MaxMacros = 1000, MaxActionsPerMacro = 200512, MaxTotalActions = 500000;
        internal Keys RecordKey = Keys.F7, PlayKey = Keys.F9, StopKey = Keys.F8;
        internal List<SavedMacro> Items = new List<SavedMacro>();
        internal static bool ValidShortcut(Keys key) { return InputRules.ValidShortcut(key); }
        internal string ValidateShortcut(int target, Keys key)
        {
            if (!ValidShortcut(key)) return L.T("키보드 키 또는 마우스 4·5번 버튼 하나를 지정하세요.");
            Keys[] keys = { RecordKey, PlayKey, StopKey };
            for (int i = 0; i < keys.Length; i++) if (i != target && InputRules.NormalizeKey(keys[i]) == InputRules.NormalizeKey(key)) return L.T("녹화·실행·긴급 정지 단축키는 서로 달라야 합니다.");
            foreach (SavedMacro item in Items) if (item.RunKey != Keys.None && InputRules.NormalizeKey(item.RunKey) == InputRules.NormalizeKey(key)) return L.T("다른 매크로의 실행 단축키와 중복됩니다.");
            return null;
        }
        internal string ValidateRunKey(SavedMacro selected, Keys key, IEnumerable<Keys> reserved, IList<MacroAction> actions = null)
        {
            if (key == Keys.None) return null;
            if (!ValidShortcut(key)) return L.T("키보드 키 또는 마우스 4·5번 버튼 하나를 지정하세요.");
            Keys normalized = InputRules.NormalizeKey(key);
            foreach (Keys control in new Keys[] { RecordKey, PlayKey, StopKey })
                if (InputRules.NormalizeKey(control) == normalized) return L.T("시작·녹화·긴급 정지 단축키와 중복됩니다.");
            if (reserved != null) foreach (Keys control in reserved)
                if (InputRules.NormalizeKey(control) == normalized) return L.T("Clicker 입력 키 또는 단축키와 중복됩니다.");
            foreach (SavedMacro item in Items)
                if (item != selected && item.RunKey != Keys.None && InputRules.NormalizeKey(item.RunKey) == normalized) return L.T("다른 매크로의 실행 단축키와 중복됩니다.");
            foreach (MacroAction action in actions ?? selected.Actions)
                if ((action.Kind == ActionKind.KeyDown || action.Kind == ActionKind.KeyUp) && InputRules.NormalizeKey((Keys)action.Code) == normalized) return L.T("이 매크로에 기록된 입력 키와 중복됩니다.");
            return null;
        }
        internal string CheckName(string name, SavedMacro except)
        {
            string invalid = ValidateName(name); if (invalid != null) return invalid;
            foreach (SavedMacro item in Items) if (item != except && String.Equals(item.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)) return L.T("같은 이름이 있습니다. 다른 이름을 입력하세요.");
            return null;
        }
        static string ValidateName(string name)
        {
            if (String.IsNullOrWhiteSpace(name) || name.Trim().Length > 80) return L.T("이름은 공백을 제외하고 1~80자로 입력하세요.");
            foreach (char c in name) if (Char.IsControl(c)) return L.T("이름에 제어 문자를 넣을 수 없습니다.");
            return null;
        }
        internal void ValidateCapacity(IList<SavedMacro> additions = null)
        {
            int count = Items.Count, actions = 0;
            foreach (SavedMacro item in Items)
            { if (item.Actions.Count > MaxActionsPerMacro || item.Actions.Count > MaxTotalActions - actions) throw new LibraryLimitException(); actions += item.Actions.Count; }
            if (additions != null) foreach (SavedMacro item in additions)
            { if (++count > MaxMacros || item.Actions.Count > MaxActionsPerMacro || item.Actions.Count > MaxTotalActions - actions) throw new LibraryLimitException(); actions += item.Actions.Count; }
            if (count > MaxMacros) throw new LibraryLimitException();
        }
        internal void Validate()
        {
            ValidateCapacity();
            if (ValidateShortcut(0, RecordKey) != null || ValidateShortcut(1, PlayKey) != null || ValidateShortcut(2, StopKey) != null)
                throw new FormatException("Invalid shortcuts");
            HashSet<Guid> ids = new HashSet<Guid>();
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SavedMacro item in Items)
            {
                Guid id;
                if (!Guid.TryParse(item.Id, out id) || !ids.Add(id) || ValidateName(item.Name) != null || !names.Add(item.Name.Trim()) || item.Duration < 0 || item.Duration > 86400000)
                    throw new FormatException("Invalid macro");
                if (item.RepeatCount < 1 || item.RepeatCount > 1000000 || item.RepeatDelayMs < 0 || item.RepeatDelayMs > 86400000)
                    throw new FormatException("Invalid repeat settings");
                if (item.SpeedPercent < 10 || item.SpeedPercent > 1000 || (item.WindowRelative && !item.OriginKnown) || ValidateRunKey(item, item.RunKey, null) != null)
                    throw new FormatException("Invalid playback settings");
                if (item.Actions.Count == 0) throw new FormatException("Empty macro");
                long previous = 0;
                foreach (MacroAction action in item.Actions)
                {
                    if (action.At < previous || action.At > item.Duration || !Enum.IsDefined(typeof(ActionKind), action.Kind) || action.Scan < 0 || action.Scan > 65535 ||
                        ((action.Kind == ActionKind.KeyDown || action.Kind == ActionKind.KeyUp) && (action.Code < 8 || action.Code > 254)) ||
                        ((action.Kind == ActionKind.MouseDown || action.Kind == ActionKind.MouseUp) && (action.Code < 1 || action.Code > 5)))
                        throw new FormatException("Invalid action");
                    previous = action.At;
                }
            }
        }
        internal void Save(string path, bool backup = true)
        {
            Validate();
            DataNode root = new DataNode("MacroLibrary", new DataField("version", 1), new DataField("record", (int)RecordKey), new DataField("play", (int)PlayKey), new DataField("stop", (int)StopKey));
            foreach (SavedMacro item in Items)
            {
                DataNode node = new DataNode("Macro", new DataField("id", item.Id), new DataField("name", item.Name), new DataField("created", item.Created.ToString("o")), new DataField("duration", item.Duration),
                    new DataField("repeatCount", item.RepeatCount), new DataField("repeatForever", item.RepeatForever), new DataField("repeatDelayMs", item.RepeatDelayMs), new DataField("runKey", (int)item.RunKey), new DataField("speedPercent", item.SpeedPercent),
                    new DataField("modified", item.Modified.ToString("o")), new DataField("windowRelative", item.WindowRelative), new DataField("originKnown", item.OriginKnown), new DataField("originX", item.RecordedOriginX), new DataField("originY", item.RecordedOriginY));
                foreach (MacroAction a in item.Actions)
                {
                    DataNode action = new DataNode("Action", new DataField("at", a.At), new DataField("kind", (int)a.Kind), new DataField("x", a.X), new DataField("y", a.Y), new DataField("code", a.Code), new DataField("scan", a.Scan), new DataField("extended", a.Extended));
                    if (a.HasClientPosition) { action.Add(new DataField("clientX", a.ClientX)); action.Add(new DataField("clientY", a.ClientY)); }
                    node.Add(action);
                }
                root.Add(node);
            }
            DataStore.Save(path, root, backup);
        }
        internal static MacroLibrary Load(string path)
        {
            MacroLibrary library = new MacroLibrary();
            if (!DataStore.Exists(path)) return library;
            DataNode root = DataStore.Load(path);
            if (root.Name != "MacroLibrary" || (int)root.Attribute("version") != 1) throw new FormatException("Unsupported library");
            library.RecordKey = (Keys)(int)root.Attribute("record"); library.PlayKey = (Keys)(int)root.Attribute("play"); library.StopKey = (Keys)(int)root.Attribute("stop");
            foreach (DataNode node in root.Elements("Macro"))
            {
                SavedMacro item = new SavedMacro { Id = (string)node.Attribute("id"), Name = (string)node.Attribute("name"), Created = DateTime.Parse((string)node.Attribute("created"), null, System.Globalization.DateTimeStyles.RoundtripKind), Duration = (long)node.Attribute("duration") };
                item.RepeatCount = (int?)node.Attribute("repeatCount") ?? 1;
                item.RepeatForever = (bool?)node.Attribute("repeatForever") ?? false;
                item.RepeatDelayMs = (int?)node.Attribute("repeatDelayMs") ?? 0;
                item.RunKey = (Keys)((int?)node.Attribute("runKey") ?? 0);
                item.SpeedPercent = (int?)node.Attribute("speedPercent") ?? 100;
                item.Modified = node.Attribute("modified") == null ? item.Created : DateTime.Parse((string)node.Attribute("modified"), null, System.Globalization.DateTimeStyles.RoundtripKind);
                item.WindowRelative = (bool?)node.Attribute("windowRelative") ?? false; item.OriginKnown = (bool?)node.Attribute("originKnown") ?? false;
                item.RecordedOriginX = (int?)node.Attribute("originX") ?? 0; item.RecordedOriginY = (int?)node.Attribute("originY") ?? 0;
                foreach (DataNode element in node.Elements("Action"))
                {
                    MacroAction action = new MacroAction { At = (long)element.Attribute("at"), Kind = (ActionKind)(int)element.Attribute("kind"), X = (int)element.Attribute("x"), Y = (int)element.Attribute("y"), Code = (int)element.Attribute("code"), Scan = (int)element.Attribute("scan"), Extended = (bool)element.Attribute("extended") };
                    if ((element.Attribute("clientX") == null) != (element.Attribute("clientY") == null)) throw new FormatException("Incomplete client coordinate");
                    action.HasClientPosition = element.Attribute("clientX") != null;
                    if (action.HasClientPosition) { action.ClientX = (int)element.Attribute("clientX"); action.ClientY = (int)element.Attribute("clientY"); }
                    item.Actions.Add(action);
                    if (item.Actions.Count > MaxActionsPerMacro) throw new LibraryLimitException();
                }
                library.Items.Add(item);
            }
            library.Validate();
            
            return library;
        }
    }
    internal sealed class RecordingBuffer
    {
        internal readonly List<MacroAction> Actions = new List<MacroAction>();
        readonly Dictionary<string, MacroAction> held = new Dictionary<string, MacroAction>();
        long lastMove = -10;
        internal void Add(MacroAction action, long time)
        {
            if (action.Kind != ActionKind.KeyDown && action.Kind != ActionKind.KeyUp)
                foreach (MacroAction press in held.Values) if (press.Kind == ActionKind.MouseDown)
                { press.X = action.X; press.Y = action.Y; press.HasClientPosition = action.HasClientPosition; press.ClientX = action.ClientX; press.ClientY = action.ClientY; }
            if (action.Kind == ActionKind.Move && time - lastMove < 10) return;
            if (action.Kind == ActionKind.Move) lastMove = time;
            if (action.Up && !held.ContainsKey(action.Identity)) return;
            if (action.Down) held[action.Identity] = action.Copy();
            if (action.Up) held.Remove(action.Identity);
            MacroAction copy = action.Copy(); copy.At = Math.Max(0, time); Actions.Add(copy);
        }
        internal SavedMacro Finish(long duration)
        {
            foreach (MacroAction press in held.Values) Actions.Add(press.Release(duration));
            held.Clear(); return new SavedMacro { Duration = duration, Actions = new List<MacroAction>(Actions) };
        }
    }
    internal sealed class InputMonitor : IDisposable
    {
        delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        [StructLayout(LayoutKind.Sequential)] internal struct KeyboardData { public uint key, scan, flags, time; public UIntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] internal struct MouseData { public int x, y; public uint data, flags, time; public UIntPtr extra; }
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        readonly HookProc keyboardProc, mouseProc;
        readonly Func<Keys, bool, bool> control;
        readonly Action<MacroAction> record;
        readonly uint processId = (uint)Process.GetCurrentProcess().Id;
        IntPtr keyboard, mouse;
        internal InputMonitor(Func<Keys, bool, bool> shortcuts, Action<MacroAction> action)
        { control = shortcuts; record = action; keyboardProc = Keyboard; mouseProc = Mouse; }
        internal bool Install()
        {
            keyboard = SetWindowsHookEx(13, keyboardProc, GetModuleHandle(null), 0);
            mouse = SetWindowsHookEx(14, mouseProc, GetModuleHandle(null), 0);
            if (keyboard != IntPtr.Zero && mouse != IntPtr.Zero) return true;
            Dispose(); return false;
        }
        bool OwnWindow(IntPtr window) { uint id; GetWindowThreadProcessId(window, out id); return id == processId; }
        IntPtr Keyboard(int code, IntPtr message, IntPtr data)
        {
            int msg = message.ToInt32();
            if (code >= 0 && (msg == 0x100 || msg == 0x101 || msg == 0x104 || msg == 0x105))
            {
                KeyboardData k = (KeyboardData)Marshal.PtrToStructure(data, typeof(KeyboardData));
                if ((k.flags & 0x10) == 0)
                {
                    bool down = msg == 0x100 || msg == 0x104;
                    if (control((Keys)k.key, down)) return (IntPtr)1;
                    if (!OwnWindow(GetForegroundWindow())) record(new MacroAction { Kind = down ? ActionKind.KeyDown : ActionKind.KeyUp, Code = (int)k.key, Scan = (int)k.scan, Extended = (k.flags & 1) != 0 });
                }
            }
            return CallNextHookEx(keyboard, code, message, data);
        }
        internal static MacroAction DecodeMouse(int msg, MouseData m)
        {
            MacroAction action = new MacroAction { X = m.x, Y = m.y };
            if (msg == 0x200) action.Kind = ActionKind.Move;
            else if (msg == 0x20A || msg == 0x20E) { action.Kind = msg == 0x20A ? ActionKind.Wheel : ActionKind.HorizontalWheel; action.Code = (short)(m.data >> 16); }
            else
            {
                action.Kind = msg == 0x201 || msg == 0x204 || msg == 0x207 || msg == 0x20B ? ActionKind.MouseDown : ActionKind.MouseUp;
                if (msg == 0x201 || msg == 0x202) action.Code = 1;
                else if (msg == 0x204 || msg == 0x205) action.Code = 2;
                else if (msg == 0x207 || msg == 0x208) action.Code = 3;
                else if (msg == 0x20B || msg == 0x20C) { action.Code = (int)(m.data >> 16) + 3; if (action.Code > 5) return null; }
                else return null;
            }
            return action;
        }
        IntPtr Mouse(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0)
            {
                MouseData m = (MouseData)Marshal.PtrToStructure(data, typeof(MouseData));
                if ((m.flags & 1) == 0)
                {
                    MacroAction action = DecodeMouse(message.ToInt32(), m);
                    if (action != null)
                    {
                        if ((action.Down || action.Up) && action.Code >= 4 && control(action.Code == 4 ? Keys.XButton1 : Keys.XButton2, action.Down)) return (IntPtr)1;
                        if (!OwnWindow(WindowFromPoint(new Point(m.x, m.y)))) record(action);
                    }
                }
            }
            return CallNextHookEx(mouse, code, message, data);
        }
        public void Dispose()
        {
            if (keyboard != IntPtr.Zero) { UnhookWindowsHookEx(keyboard); keyboard = IntPtr.Zero; }
            if (mouse != IntPtr.Zero) { UnhookWindowsHookEx(mouse); mouse = IntPtr.Zero; }
        }
    }
    internal static class Playback
    {
        internal static long ScaledTime(long time, int speedPercent)
        {
            if (speedPercent < 10 || speedPercent > 1000) throw new ArgumentOutOfRangeException("speedPercent");
            return (time * 100 + speedPercent - 1) / speedPercent;
        }
        internal static int RepeatPause(SavedMacro macro)
        { return Math.Max(macro.RepeatDelayMs, (int)Math.Max(0, 10 - ScaledTime(macro.Duration, macro.SpeedPercent))); }
        internal static double TotalMilliseconds(SavedMacro macro)
        { return macro.RepeatForever ? Double.PositiveInfinity : ScaledTime(macro.Duration, macro.SpeedPercent) * (double)macro.RepeatCount + RepeatPause(macro) * (double)(macro.RepeatCount - 1); }
        internal static double ProgressFraction(SavedMacro macro, double elapsedMs, bool completed)
        {
            if (completed && !macro.RepeatForever) return 1;
            double duration = macro.RepeatForever ? ScaledTime(macro.Duration, macro.SpeedPercent) + RepeatPause(macro) : TotalMilliseconds(macro);
            if (duration <= 0) return 0;
            // Elapsed time includes the recording's trailing wait and the pauses between repeats.
            // Only the worker's successful completion may fill the bar completely.
            return Math.Max(0, Math.Min(0.999, elapsedMs / duration));
        }
        internal static bool RunRepeated(SavedMacro macro, WaitHandle cancel, Action<Native.Input> send, Action<long, int> progress, bool timed, Action<int> waiting = null, Func<MacroAction, MacroAction> resolve = null, Action validateTarget = null)
        {
            for (long iteration = 1; macro.RepeatForever || iteration <= macro.RepeatCount; iteration++)
            {
                if (cancel.WaitOne(0)) return false;
                if (progress != null) progress(iteration, 0);
                long current = iteration;
                if (!Run(macro, cancel, send, delegate(int n) { if (progress != null) progress(current, n); }, timed, resolve, validateTarget)) return false;
                if (!macro.RepeatForever && iteration == macro.RepeatCount) return true;
                // A zero-duration recording must not turn an infinite repeat into a busy loop.
                int pause = RepeatPause(macro);
                if (waiting != null) waiting(pause);
                if (Wait(cancel, pause, validateTarget)) return false;
            }
            return true;
        }
        internal static Native.Input Convert(MacroAction action, Rectangle screen)
        {
            Native.Input input = new Native.Input();
            if (action.Kind == ActionKind.KeyDown || action.Kind == ActionKind.KeyUp)
            {
                input.type = 1;
                input.data.keyboard.key = action.Scan == 0 ? (ushort)action.Code : (ushort)0;
                input.data.keyboard.scan = (ushort)action.Scan;
                input.data.keyboard.flags = (action.Scan != 0 ? 8u : 0u) | (action.Extended ? 1u : 0u) | (action.Kind == ActionKind.KeyUp ? 2u : 0u);
            }
            else
            {
                if (!screen.Contains(action.X, action.Y)) throw new InvalidOperationException(L.T("입력 위치가 화면 밖이어서 재생을 중단했습니다."));
                // Absolute movement in the entire virtual desktop, including negative monitor coordinates.
                input.data.mouse.dx = (int)Math.Max(0, Math.Min(65535, ((long)action.X - screen.Left) * 65535 / Math.Max(1, screen.Width - 1)));
                input.data.mouse.dy = (int)Math.Max(0, Math.Min(65535, ((long)action.Y - screen.Top) * 65535 / Math.Max(1, screen.Height - 1)));
                input.data.mouse.flags = 0x8000 | 0x4000 | 1;
                if (action.Kind == ActionKind.Wheel || action.Kind == ActionKind.HorizontalWheel)
                { input.data.mouse.flags |= action.Kind == ActionKind.Wheel ? 0x800u : 0x1000u; input.data.mouse.data = unchecked((uint)action.Code); }
                else if (action.Down || action.Up)
                {
                    uint down = action.Code == 1 ? 2u : action.Code == 2 ? 8u : action.Code == 3 ? 32u : 128u;
                    input.data.mouse.flags |= action.Down ? down : down << 1;
                    if (action.Code >= 4) input.data.mouse.data = (uint)(action.Code - 3);
                }
            }
            return input;
        }
        internal static void Send(Native.Input input)
        { if (Native.SendInput(1, new Native.Input[] { input }, Marshal.SizeOf(typeof(Native.Input))) != 1) throw new InvalidOperationException(L.T("입력을 보내지 못했습니다. 대상 앱의 권한을 확인하세요.")); }
        internal static bool OnMonitor(int x, int y, IEnumerable<Rectangle> monitors)
        { foreach (Rectangle monitor in monitors) if (monitor.Contains(x, y)) return true; return false; }
        internal static bool Run(SavedMacro macro, WaitHandle cancel, Action<Native.Input> send, Action<int> progress, bool timed, Func<MacroAction, MacroAction> resolve = null, Action validateTarget = null)
        {
            Stopwatch clock = Stopwatch.StartNew(); Rectangle screen = SystemInformation.VirtualScreen;
            Dictionary<string, MacroAction> held = new Dictionary<string, MacroAction>();
            try
            {
                for (int i = 0; i < macro.Actions.Count; i++)
                {
                    MacroAction action = macro.Actions[i];
                    long remaining = ScaledTime(action.At, macro.SpeedPercent) - clock.ElapsedMilliseconds;
                    if (Wait(cancel, timed && remaining > 0 ? (int)Math.Min(Int32.MaxValue, remaining) : 0, validateTarget)) return false;
                    if (resolve != null) action = resolve(action);
                    if (action.Kind != ActionKind.KeyDown && action.Kind != ActionKind.KeyUp)
                    {
                        if (SystemInformation.VirtualScreen != screen) throw new InvalidOperationException(L.T("화면 구성이 변경되어 재생을 중단했습니다."));
                        List<Rectangle> monitors = new List<Rectangle>(); foreach (Screen monitor in Screen.AllScreens) monitors.Add(monitor.Bounds);
                        if (!OnMonitor(action.X, action.Y, monitors)) throw new InvalidOperationException(L.T("입력 위치가 화면 밖이어서 재생을 중단했습니다."));
                    }
                    Native.Input input = Convert(action, screen);
                    // Include a possibly partially delivered press in cleanup if SendInput fails.
                    if (action.Down) held[action.Identity] = action;
                    send(input);
                    if (action.Up) held.Remove(action.Identity);
                    if (progress != null) progress(i + 1);
                }
                long tail = ScaledTime(macro.Duration, macro.SpeedPercent) - clock.ElapsedMilliseconds;
                return !Wait(cancel, timed && tail > 0 ? (int)Math.Min(Int32.MaxValue, tail) : 0, validateTarget);
            }
            finally
            {
                foreach (MacroAction press in held.Values)
                {
                    Native.Input release = Convert(press.Release(0), screen);
                    if (release.type == 0) release.data.mouse.flags &= ~(0x8000u | 0x4000u | 1u);
                    try { send(release); } catch { /* Continue releasing the remaining held inputs. */ }
                }
            }
        }
        static bool Wait(WaitHandle cancel, int delay, Action validateTarget)
        {
            if (validateTarget == null) return cancel.WaitOne(delay);
            Stopwatch clock = Stopwatch.StartNew();
            for (;;)
            {
                if (cancel.WaitOne(0)) return true;
                validateTarget(); long left = delay - clock.ElapsedMilliseconds;
                if (left <= 0) return false;
                if (cancel.WaitOne((int)Math.Min(50, left))) return true;
            }
        }
    }
}
