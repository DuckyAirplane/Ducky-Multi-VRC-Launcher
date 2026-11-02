using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DuckyMultiVrc {
    public static class Grid {
        public static Rectangle[] Create(int count, Rectangle area) {
            if (count < 1 || count > 16) throw new ArgumentOutOfRangeException("count");
            int columns = 1; double best = double.MaxValue;
            for (int c = 1; c <= count; c++) {
                int r = (count + c - 1) / c;
                double aspect = (area.Width / (double)c) / (area.Height / (double)r);
                double score = Math.Abs(Math.Log(aspect / (16.0 / 9))) + (c * r - count) / (double)count * .25;
                if (score < best) { best = score; columns = c; }
            }
            int rows = (count + columns - 1) / columns;
            var tiles = new Rectangle[count];
            for (int i = 0; i < count; i++) {
                int row = i / columns, col = i % columns;
                int rowCount = Math.Min(columns, count - row * columns);
                int left = area.Left + col * area.Width / rowCount;
                int top = area.Top + row * area.Height / rows;
                int right = area.Left + (col + 1) * area.Width / rowCount;
                int bottom = area.Top + (row + 1) * area.Height / rows;
                tiles[i] = Rectangle.FromLTRB(left, top, right, bottom);
            }
            return tiles;
        }
    }

    // A request is issued once. Missing windows stop the queue rather than
    // repeating a request whose outcome is unknown.
    public sealed class LaunchBudget {
        public readonly int Requested;
        public int Issued { get; private set; }
        public bool Stopped { get; private set; }
        public LaunchBudget(int count) {
            if (count < 1 || count > 16) throw new ArgumentOutOfRangeException("count");
            Requested = count;
        }
        public bool Take() {
            if (Stopped || Issued >= Requested) return false;
            Issued++; return true;
        }
        public void Stop() { Stopped = true; }
    }

    public static class ClientOwnership {
        public static bool CanClose(int processId, ICollection<int> owned, ICollection<int> preexisting) {
            return owned.Contains(processId) && !preexisting.Contains(processId);
        }
    }

    internal static class Native {
        internal delegate bool EnumProc(IntPtr window, IntPtr data);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback, IntPtr data);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern int GetClassName(IntPtr window, StringBuilder name, int length);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] internal static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] internal static extern int SetWindowLong(IntPtr window, int index, int value);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsZoomed(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }

        // Enumerating HWNDs also works when a protected game process doesn't
        // expose CommandLine or Process.MainWindowHandle to the launcher.
        internal static Dictionary<IntPtr, int> GameWindows() {
            var result = new Dictionary<IntPtr, int>();
            EnumWindows(delegate(IntPtr h, IntPtr unused) {
                if (!IsWindowVisible(h)) return true;
                var className = new StringBuilder(256);
                GetClassName(h, className, className.Capacity);
                if (className.ToString() != "UnityWndClass") return true;
                uint id; GetWindowThreadProcessId(h, out id);
                try {
                    using (var p = Process.GetProcessById((int)id)) {
                        if (string.Equals(p.ProcessName, "VRChat", StringComparison.OrdinalIgnoreCase)) result[h] = (int)id;
                    }
                } catch (ArgumentException) { } catch (System.ComponentModel.Win32Exception) { }
                return true;
            }, IntPtr.Zero);
            return result;
        }
        internal static bool Place(IntPtr h, Rectangle tile) {
            if (!IsWindow(h)) return false;
            if (IsIconic(h) || IsZoomed(h)) ShowWindow(h, 9);
            int style = GetWindowLong(h, -16);
            if ((style & 0x00CF0000) != 0) SetWindowLong(h, -16, style & ~0x00CF0000);
            int ex = GetWindowLong(h, -20);
            if ((ex & 0x00020301) != 0) SetWindowLong(h, -20, ex & ~0x00020301);
            // Do not activate the game or change its Z-order.
            if (!SetWindowPos(h, IntPtr.Zero, tile.X, tile.Y, tile.Width, tile.Height, 0x0034)) return false;
            Rect actual;
            return GetWindowRect(h, out actual) && actual.Left == tile.Left && actual.Top == tile.Top && actual.Right == tile.Right && actual.Bottom == tile.Bottom;
        }
    }

    internal sealed class DisplayChoice {
        internal readonly Screen Screen;
        internal DisplayChoice(Screen screen) { Screen = screen; }
        public override string ToString() {
            string name = Screen.DeviceName.Replace("\\\\.\\", "");
            return string.Format("{0}  |  {1} x {2}  |  position {3}, {4}{5}", name, Screen.Bounds.Width, Screen.Bounds.Height, Screen.Bounds.Left, Screen.Bounds.Top, Screen.Primary ? "  |  Main" : "");
        }
    }

    public sealed class LauncherForm : Form {
        readonly TextBox count = new TextBox();
        readonly ComboBox monitor = new ComboBox();
        readonly TextBox path = new TextBox();
        readonly Button browse = new Button(), start = new Button(), stop = new Button(), closeAll = new Button();
        readonly Label status = new Label(), summary = new Label();
        readonly Panel preview = new Panel();
        readonly ListBox activity = new ListBox();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly List<IntPtr> owned = new List<IntPtr>();
        readonly List<int> ownedProcesses = new List<int>();
        HashSet<IntPtr> baseline;
        HashSet<int> baselineProcesses;
        Rectangle[] tiles;
        LaunchBudget budget;
        DateTime waitingSince, nextLaunch;
        bool waiting, active, settling;
        bool closing, pendingClose;
        DateTime closeDeadline;
        readonly HashSet<IntPtr> closeRequested = new HashSet<IntPtr>();
        string selectedLauncher;
        int placementFailures;
        readonly string dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DuckyMultiVRCLauncher");

        public LauncherForm() {
            Text = "Ducky Multi VRC Launcher";
            ClientSize = new Size(760, 750); MinimumSize = new Size(780, 790);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(22, 28, 35); ForeColor = Color.FromArgb(232, 237, 242);
            Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
            var heading = LabelAt("Ducky Multi VRC Launcher", 28, 22, 700, 42);
            heading.Font = new Font("Segoe UI Semibold", 23);
            LabelAt("Choose a count. Choose a monitor. Launch once.", 30, 69, 700, 28).ForeColor = Color.Silver;
            LabelAt("CLIENTS TO OPEN", 30, 112, 250, 24);
            count.SetBounds(30, 140, 110, 32); count.MaxLength = 2; Controls.Add(count);
            LabelAt("1-16 additional clients  /  20 FPS each", 158, 142, 530, 28);
            LabelAt("TARGET MONITOR", 30, 190, 650, 24);
            monitor.DropDownStyle = ComboBoxStyle.DropDownList; monitor.SetBounds(30, 218, 700, 34);
            foreach (var screen in Screen.AllScreens.OrderBy(s => s.Bounds.Left).ThenBy(s => s.Bounds.Top)) monitor.Items.Add(new DisplayChoice(screen));
            monitor.SelectedIndex = -1; Controls.Add(monitor);
            LabelAt("VRCHAT LAUNCHER", 30, 268, 650, 24);
            path.SetBounds(30, 296, 580, 32); path.Text = FindLauncher(); Controls.Add(path);
            browse.Text = "Browse..."; browse.SetBounds(620, 294, 110, 34); Controls.Add(browse);
            browse.Click += delegate { using (var dialog = new OpenFileDialog { Filter = "VRChat launcher (launch.exe)|launch.exe", CheckFileExists = true }) { if (dialog.ShowDialog(this) == DialogResult.OK) path.Text = dialog.FileName; } };
            preview.SetBounds(30, 348, 290, 162); preview.BackColor = Color.FromArgb(13, 18, 24); Controls.Add(preview);
            preview.Paint += PaintPreview;
            summary.SetBounds(340, 352, 390, 150); summary.Text = "Enter a client count and select a monitor.\n\nExisting VRChat windows, including your main client, stay untouched."; Controls.Add(summary);
            start.Text = "Launch clients"; start.SetBounds(30, 530, 290, 44); start.BackColor = Color.FromArgb(42, 197, 172); start.ForeColor = Color.FromArgb(10, 32, 29); start.FlatStyle = FlatStyle.Flat; Controls.Add(start);
            stop.Text = "Stop launching"; stop.SetBounds(334, 530, 190, 44); stop.Enabled = false; Controls.Add(stop);
            closeAll.Text = "Close all clients"; closeAll.SetBounds(538, 530, 192, 44); closeAll.Enabled = false; Controls.Add(closeAll);
            status.SetBounds(30, 586, 700, 45); status.Text = "Ready. No clients will open until you click Launch clients."; Controls.Add(status);
            activity.SetBounds(30, 636, 700, 84); activity.BackColor = Color.FromArgb(13, 18, 24); activity.ForeColor = Color.Gainsboro; activity.BorderStyle = BorderStyle.None; Controls.Add(activity);
            count.TextChanged += delegate { UpdatePreview(); }; monitor.SelectedIndexChanged += delegate { UpdatePreview(); };
            start.Click += delegate { BeginLaunch(); }; stop.Click += delegate { Halt("Stopped. Existing clients were left open."); };
            closeAll.Click += delegate { CloseClients(); };
            timer.Interval = 1000; timer.Tick += delegate { try { TickLaunch(); } catch (Exception ex) { Halt("Stopped: " + ex.Message); } };
            FormClosing += delegate { if (budget != null) budget.Stop(); timer.Stop(); };
        }
        Label LabelAt(string text, int x, int y, int width, int height) {
            var l = new Label { Text = text }; l.SetBounds(x,y,width,height); Controls.Add(l); return l;
        }
        static string FindLauncher() {
            var roots = new List<string>();
            try {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")) { if (key != null && key.GetValue("SteamPath") != null) roots.Add(key.GetValue("SteamPath").ToString()); }
            } catch (System.Security.SecurityException) { } catch (UnauthorizedAccessException) { }
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
            foreach (string root in roots.ToArray()) {
                string vdf = Path.Combine(root, @"steamapps\libraryfolders.vdf");
                try {
                    if (File.Exists(vdf)) foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\"")) roots.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
                } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            return roots.Select(r => Path.Combine(r, @"steamapps\common\VRChat\launch.exe")).FirstOrDefault(File.Exists) ?? "";
        }
        void UpdatePreview() {
            int n; var display = monitor.SelectedItem as DisplayChoice;
            if (int.TryParse(count.Text, out n) && n >= 1 && n <= 16 && display != null) {
                var a = display.Screen.WorkingArea; var grid = Grid.Create(n, a);
                summary.Text = string.Format("{0} clients at 20 FPS\n{1} x {2} usable pixels\n\nEvery tile stays inside this monitor.\nExisting clients stay untouched.", n, a.Width, a.Height);
            }
            preview.Invalidate();
        }
        void PaintPreview(object sender, PaintEventArgs e) {
            int n; var display = monitor.SelectedItem as DisplayChoice;
            if (!int.TryParse(count.Text, out n) || n < 1 || n > 16 || display == null) return;
            var source = display.Screen.WorkingArea;
            double scale = Math.Min((preview.Width-16)/(double)source.Width, (preview.Height-16)/(double)source.Height);
            var a = new Rectangle(8,8,Math.Max(1,(int)(source.Width*scale)),Math.Max(1,(int)(source.Height*scale)));
            int index = 0;
            foreach (var tile in Grid.Create(n,a)) {
                using (var brush = new SolidBrush(Color.FromArgb(30,105,101))) e.Graphics.FillRectangle(brush, tile);
                e.Graphics.DrawRectangle(Pens.Black,tile.X,tile.Y,tile.Width-1,tile.Height-1);
                TextRenderer.DrawText(e.Graphics,(++index).ToString(),Font,tile,Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            }
        }
        void Note(string message) { status.Text = message; activity.Items.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + message); activity.TopIndex = activity.Items.Count-1; }
        void BeginLaunch() {
            if (active) return;
            // One launch session per application lifetime: uncertain launches
            // cannot be repeated by clicking the button again.
            if (budget != null) { Note("This session is finished. Close this launcher before starting a new session."); return; }
            int n; var display = monitor.SelectedItem as DisplayChoice;
            if (!int.TryParse(count.Text,out n) || n < 1 || n > 16) { Note("Enter a whole number from 1 to 16."); count.Focus(); return; }
            if (display == null) { Note("Select a target monitor first."); monitor.Focus(); return; }
            if (!File.Exists(path.Text) || !string.Equals(Path.GetFileName(path.Text),"launch.exe",StringComparison.OrdinalIgnoreCase)) { Note("Choose VRChat's launch.exe using Browse."); return; }
            if (!Screen.AllScreens.Any(s => s.DeviceName == display.Screen.DeviceName)) { Note("The selected monitor is disconnected. Reopen the launcher to refresh monitors."); return; }
            selectedLauncher = Path.GetFullPath(path.Text);
            tiles = Grid.Create(n, display.Screen.WorkingArea);
            baseline = new HashSet<IntPtr>(Native.GameWindows().Keys);
            baselineProcesses = new HashSet<int>();
            foreach (var p in Process.GetProcessesByName("VRChat")) { using(p) baselineProcesses.Add(p.Id); }
            Directory.CreateDirectory(Path.Combine(dataDirectory,"logs"));
            budget = new LaunchBudget(n); active = true; waiting = false; settling = false;
            start.Enabled = false; stop.Enabled = true; count.Enabled = false; monitor.Enabled = false; path.Enabled = false; browse.Enabled = false;
            nextLaunch = DateTime.UtcNow; Note("Launching " + n + " clients. Existing VRChat windows are excluded."); timer.Start();
        }
        void IssueOne() {
            if (!budget.Take()) return;
            // Snapshot every visible existing window, not only the main client.
            foreach (var h in Native.GameWindows().Keys) if (!owned.Contains(h)) baseline.Add(h);
            int slot = budget.Issued-1; var tile = tiles[slot]; int profile = 101+slot;
            string log = Path.Combine(dataDirectory,"logs", "profile-"+profile+"-"+Guid.NewGuid().ToString("N")+".log");
            string args = string.Format("--profile={0} --no-vr --fps=20 -screen-width {1} -screen-height {2} -screen-fullscreen 0 -popupwindow -logFile \"{3}\"", profile,tile.Width,tile.Height,log);
            var info = new ProcessStartInfo(selectedLauncher,args) { WorkingDirectory=Path.GetDirectoryName(selectedLauncher), UseShellExecute=false };
            try { using(var p = Process.Start(info)) { } }
            catch(Exception ex) { Halt("Launch failed. No automatic retry: " + ex.Message); return; }
            waiting = true; waitingSince = DateTime.UtcNow;
            closeAll.Enabled = true;
            Note(string.Format("Started request {0} of {1}. Waiting for its window...",budget.Issued,budget.Requested));
        }
        void TickLaunch() {
            if (closing) { TickClosing(); return; }
            if (!active) return;
            var windows = Native.GameWindows();
            // Unity can recreate its HWND while loading. Keep the tile bound
            // to the client process instead of retaining a stale window handle.
            for(int i=0;i<owned.Count;i++) {
                var replacement = windows.Where(w => w.Value == ownedProcesses[i]).ToArray();
                if(replacement.Length == 1) owned[i] = replacement[0].Key;
            }
            if (waiting) {
                var fresh = windows.Where(w => !baseline.Contains(w.Key) && !baselineProcesses.Contains(w.Value) && !ownedProcesses.Contains(w.Value)).ToArray();
                if (fresh.Length > 1) { Halt("More than one new VRChat window appeared. Stopped to avoid selecting the wrong client."); return; }
                if (fresh.Length == 1) {
                    owned.Add(fresh[0].Key); ownedProcesses.Add(fresh[0].Value); waiting = false; nextLaunch = DateTime.UtcNow.AddSeconds(10);
                    Note(string.Format("Window {0} of {1} detected and assigned to its tile.",owned.Count,budget.Requested));
                } else if ((DateTime.UtcNow-waitingSince).TotalSeconds > 90) {
                    Halt("A client did not expose a window within 90 seconds. Stopped; no duplicate launch was attempted."); return;
                }
            }
            placementFailures = 0;
            for (int i=0; i<owned.Count; i++) if (!Native.Place(owned[i],tiles[i])) placementFailures++;
            if (!waiting && DateTime.UtcNow >= nextLaunch) {
                if (budget.Issued < budget.Requested) IssueOne();
                else if (!settling) { settling=true; nextLaunch=DateTime.UtcNow.AddSeconds(20); Note("All requested windows detected. Finishing automatic arrangement..."); }
                else {
                    Halt(placementFailures==0 ? "Done. " + owned.Count + " clients arranged. No additional clients will launch." : "Launch complete, but " + placementFailures + " window(s) did not accept their tile size. No extra clients launched.");
                }
            }
        }
        void Halt(string message) {
            active=false; timer.Stop(); if(budget!=null) budget.Stop(); stop.Enabled=false;
            Note(message);
        }
        void CloseClients() {
            pendingClose = waiting;
            Halt("Closing this launcher's clients. Your previously open clients stay open.");
            closing = true; closeAll.Enabled = false;
            closeDeadline = DateTime.UtcNow.AddSeconds(pendingClose ? 90 : 15);
            timer.Start(); TickClosing();
        }
        void TickClosing() {
            var windows = Native.GameWindows();
            // A launch already handed to VRChat may finish after Close all was
            // clicked. Watch that one pending request without launching more.
            if (pendingClose) {
                var fresh = windows.Where(w => !baseline.Contains(w.Key) && !baselineProcesses.Contains(w.Value) && !ownedProcesses.Contains(w.Value)).ToArray();
                if (fresh.Length == 1) {
                    ownedProcesses.Add(fresh[0].Value); pendingClose = false;
                } else if (fresh.Length > 1) {
                    pendingClose = false;
                    Note("An unidentifiable new client was left open; closing only this launcher's identified clients.");
                }
            }
            var targets = windows.Where(w => ClientOwnership.CanClose(w.Value, ownedProcesses, baselineProcesses)).ToArray();
            foreach (var target in targets) {
                if (closeRequested.Contains(target.Key)) continue;
                // WM_CLOSE requests a normal application shutdown, without
                // force-killing processes or changing any unrelated client.
                if (Native.PostMessage(target.Key, 0x0010, IntPtr.Zero, IntPtr.Zero)) closeRequested.Add(target.Key);
            }
            if ((targets.Length == 0 && !pendingClose) || DateTime.UtcNow >= closeDeadline) {
                closing = false; timer.Stop();
                closeAll.Enabled = targets.Length > 0;
                Note(targets.Length == 0 && !pendingClose ? "All identified launcher clients closed. Previously open clients were left alone." : "Close requested. Some clients are still starting or did not close; they were not force-terminated.");
            }
        }
        public void RenderPreview(string filename) {
            count.Text="2"; if(monitor.Items.Count>0) monitor.SelectedIndex=0;
            ShowInTaskbar=false; StartPosition=FormStartPosition.Manual; Location=new Point(-32000,-32000);
            Show(); Application.DoEvents(); PerformLayout();
            using(var bitmap=new Bitmap(Width,Height)) { DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height)); bitmap.Save(filename); }
            Hide();
        }
    }

    internal static class Program {
        [STAThread] static int Main(string[] args) {
            if (args.Length>0 && args[0]=="--self-test") return SelfTest();
            bool created;
            using(var mutex = new Mutex(true,@"Local\DuckyMultiVRCLauncher",out created)) {
                if(!created) { MessageBox.Show("Ducky Multi VRC Launcher is already open. Use the existing window.","Ducky Multi VRC Launcher"); return 1; }
                try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch(EntryPointNotFoundException) { }
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                using(var form=new LauncherForm()) {
                    if(args.Length==2 && args[0]=="--preview") { form.RenderPreview(args[1]); return 0; }
                    Application.Run(form);
                }
            }
            return 0;
        }
        static int SelfTest() {
            int cases=0;
            foreach(var size in new[]{new Size(1920,1032),new Size(1919,1031),new Size(1080,1872),new Size(3840,2112),new Size(2560,1392)}) {
                for(int n=1;n<=16;n++) {
                    var area=new Rectangle(-1920,-200,size.Width,size.Height); var grid=Grid.Create(n,area); long total=0;
                    if(grid.Length!=n) throw new Exception("Count mismatch");
                    for(int i=0;i<n;i++) {
                        if(!area.Contains(grid[i]) || grid[i].Width<=0 || grid[i].Height<=0) throw new Exception("Out of bounds");
                        total+=(long)grid[i].Width*grid[i].Height;
                        for(int j=0;j<i;j++) if(grid[i].IntersectsWith(grid[j])) throw new Exception("Overlap");
                    }
                    if(total!=(long)area.Width*area.Height) throw new Exception("Unused area");
                    var budget=new LaunchBudget(n); for(int t=0;t<50;t++) budget.Take();
                    if(budget.Issued!=n) throw new Exception("Excess launch requests");
                    budget=new LaunchBudget(n); budget.Take(); budget.Stop();
                    if(budget.Take() || budget.Issued!=1) throw new Exception("Stop failed"); cases++;
                }
            }
            var owned = new HashSet<int>(new[]{20,30,10});
            var preexisting = new HashSet<int>(new[]{10,40});
            if(ClientOwnership.CanClose(10,owned,preexisting) || ClientOwnership.CanClose(40,owned,preexisting) || ClientOwnership.CanClose(50,owned,preexisting)) throw new Exception("Unrelated client selected for close");
            if(!ClientOwnership.CanClose(20,owned,preexisting) || !ClientOwnership.CanClose(30,owned,preexisting)) throw new Exception("Owned client not selected for close");
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"self-test-results.txt"), cases+" grid/launch cases passed; close ownership checks passed (main and unrelated clients excluded). No clients launched or closed.");
            return 0;
        }
    }
}
