using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Xml.Serialization;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace ToggleGoat
{
    public sealed class Preferences
    {
        public double Size = 144;
        public string Monitor = "rightmost";
        public string[] Pictures = new string[4];
        public void Normalize()
        {
            if (double.IsNaN(Size) || double.IsInfinity(Size)) Size = 144;
            Size = Math.Max(72, Math.Min(288, Size));
            if (Pictures == null || Pictures.Length != 4) Pictures = new string[4];
            if (String.IsNullOrEmpty(Monitor)) Monitor = "rightmost";
        }
        public Preferences Clone() { return new Preferences { Size = Size, Monitor = Monitor, Pictures = (string[])Pictures.Clone() }; }
    }

    internal static class Native
    {
        [DllImport("user32.dll")] internal static extern short GetKeyState(int key);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int ht, uint flags);
        [DllImport("user32.dll")] internal static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll")] internal static extern int SetWindowLong(IntPtr h, int index, int value);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr h);
        [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left, Top, Right, Bottom; }
        internal static int State(bool num, bool caps) { return (num ? 1 : 0) | (caps ? 2 : 0); }
        internal static int ReadState() { return State((GetKeyState(0x90) & 1) != 0, (GetKeyState(0x14) & 1) != 0); }
    }

    internal static class Pictures
    {
        internal static readonly string[] Keys = { "idle", "num", "caps", "both" };
        internal static readonly string[] Labels = { "Idle · both off", "Num Lock on", "Caps Lock on", "Num + Caps on" };
        internal static BitmapSource Load(int index, string path)
        {
            using (Stream stream = String.IsNullOrEmpty(path)
                ? Assembly.GetExecutingAssembly().GetManifestResourceStream("ToggleGoat." + Keys[index] + ".png")
                : File.OpenRead(path))
            {
                if (stream == null) throw new IOException("The built-in picture is missing.");
                if (stream.Length > 32 * 1024 * 1024) throw new IOException("Please choose an image smaller than 32 MB.");
                var image = new BitmapImage();
                image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 1200; image.StreamSource = stream; image.EndInit(); image.Freeze();
                // Fit the visible artwork, ignoring transparent outer padding. Never stretch the drawing.
                var rgba = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
                int w = rgba.PixelWidth, h = rgba.PixelHeight, stride = w * 4;
                byte[] bytes = new byte[stride * h]; rgba.CopyPixels(bytes, stride, 0);
                int left = w, top = h, right = -1, bottom = -1;
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                    if (bytes[y * stride + x * 4 + 3] > 24)
                    { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
                if (right < left) throw new IOException("That picture is completely transparent.");
                left = Math.Max(0, left - 3); top = Math.Max(0, top - 3);
                right = Math.Min(w - 1, right + 3); bottom = Math.Min(h - 1, bottom + 3);
                var crop = new CroppedBitmap(image, new Int32Rect(left, top, right - left + 1, bottom - top + 1));
                crop.Freeze(); return crop;
            }
        }
    }

    internal sealed class Store
    {
        internal readonly string Root;
        internal Store(string root) { Root = Path.GetFullPath(root); }
        internal Preferences Load()
        {
            try { using (var f = File.OpenRead(Path.Combine(Root, "settings.xml")))
                {
                    var p = (Preferences)new XmlSerializer(typeof(Preferences)).Deserialize(f); p.Normalize();
                    for (int i = 0; i < 4; i++) if (!String.IsNullOrEmpty(p.Pictures[i]) && !Path.IsPathRooted(p.Pictures[i]))
                        p.Pictures[i] = Path.Combine(Root, p.Pictures[i]);
                    return p;
                } }
            catch { return new Preferences(); }
        }
        internal Preferences Save(Preferences source)
        {
            var p = source.Clone(); p.Normalize(); Directory.CreateDirectory(Root);
            // Copy selected files into managed storage, so moving the originals won't break the buddy.
            for (int i = 0; i < 4; i++) if (!String.IsNullOrEmpty(p.Pictures[i]))
            {
                Pictures.Load(i, p.Pictures[i]);
                if (!String.Equals(Path.GetDirectoryName(Path.GetFullPath(p.Pictures[i])), Root, StringComparison.OrdinalIgnoreCase))
                {
                    string dest = Path.Combine(Root, Pictures.Keys[i] + "-" + Guid.NewGuid().ToString("N") + Path.GetExtension(p.Pictures[i]));
                    File.Copy(p.Pictures[i], dest); p.Pictures[i] = dest;
                }
            }
            string path = Path.Combine(Root, "settings.xml"), temp = path + ".tmp";
            var portable = p.Clone();
            for (int i = 0; i < 4; i++) if (!String.IsNullOrEmpty(portable.Pictures[i]) &&
                String.Equals(Path.GetDirectoryName(portable.Pictures[i]), Root, StringComparison.OrdinalIgnoreCase))
                    portable.Pictures[i] = Path.GetFileName(portable.Pictures[i]);
            using (var f = File.Create(temp)) new XmlSerializer(typeof(Preferences)).Serialize(f, portable);
            if (File.Exists(path))
            {
                try { File.Replace(temp, path, null, true); }
                catch (UnauthorizedAccessException) { File.Copy(temp, path, true); File.Delete(temp); }
                catch (PlatformNotSupportedException) { File.Copy(temp, path, true); File.Delete(temp); }
            }
            else File.Move(temp, path);
            return p;
        }
    }

    internal sealed class Buddy : Window
    {
        internal readonly Store Store;
        internal Preferences Prefs;
        internal BitmapSource[] Art = new BitmapSource[4];
        internal readonly Image Sprite;
        internal int CurrentState = -1;
        internal int PreviewState = -1;
        private Forms.NotifyIcon tray;
        private Forms.ToolStripMenuItem visibilityItem;
        private DispatcherTimer timer;
        private Settings settings;
        private EventWaitHandle reopen;
        private IntPtr hwnd;
        private string diagnosticPath, lastDiagnostic;
        private int ticks;
        internal Buddy(Store store, EventWaitHandle signal, string diagnostics)
        {
            Store = store; Prefs = store.Load(); reopen = signal; diagnosticPath = diagnostics;
            Title = "Toggle Goat"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true;
            ShowInTaskbar = false; ShowActivated = false; Focusable = false;
            Width = Height = Prefs.Size;
            Sprite = new Image { Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Bottom,
                ToolTip = "Toggle Goat — right-click for settings" };
            RenderOptions.SetBitmapScalingMode(Sprite, BitmapScalingMode.HighQuality);
            Content = Sprite;
            var menu = new ContextMenu();
            var edit = new MenuItem { Header = "Settings…" }; edit.Click += delegate { OpenSettings(); }; menu.Items.Add(edit);
            var hide = new MenuItem { Header = "Hide buddy" }; hide.Click += delegate { ToggleVisible(); }; menu.Items.Add(hide);
            menu.Items.Add(new Separator());
            var quit = new MenuItem { Header = "Quit Toggle Goat" }; quit.Click += delegate { Close(); }; menu.Items.Add(quit);
            Sprite.ContextMenu = menu;
            Sprite.MouseLeftButtonDown += delegate(object sender, System.Windows.Input.MouseButtonEventArgs e)
            { if (e.ClickCount == 2) OpenSettings(); };
            ReloadArt();
            SourceInitialized += delegate
            {
                hwnd = new WindowInteropHelper(this).Handle;
                Native.SetWindowLong(hwnd, -20, Native.GetWindowLong(hwnd, -20) | 0x08000000 | 0x80);
                HwndSource.FromHwnd(hwnd).AddHook(Hook);
                Place();
            };
            Loaded += delegate { Place(); UpdateState(); };
            SetupTray();
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(125) };
            timer.Tick += delegate
            {
                UpdateState();
                if (reopen != null && reopen.WaitOne(0)) { if (!IsVisible) ToggleVisible(); OpenSettings(); }
                if (++ticks % 16 == 0) Place();
            };
            timer.Start();
            Closed += delegate { timer.Stop(); if (settings != null) settings.Close(); tray.Visible = false; tray.Dispose(); Application.Current.Shutdown(); };
        }
        private IntPtr Hook(IntPtr h, int message, IntPtr wp, IntPtr lp, ref bool handled)
        {
            if (message == 0x21) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE
            if (message == 0x7E || message == 0x1A || message == 0x02E0)
                Dispatcher.BeginInvoke(new Action(Place), DispatcherPriority.Background);
            return IntPtr.Zero;
        }
        private void SetupTray()
        {
            tray = new Forms.NotifyIcon { Text = "Toggle Goat", Visible = true };
            using (var bitmap = new System.Drawing.Bitmap(32, 32))
            {
                using (var g = System.Drawing.Graphics.FromImage(bitmap))
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ToggleGoat.idle.png"))
                using (var original = System.Drawing.Image.FromStream(stream))
                { g.Clear(System.Drawing.Color.Transparent); g.DrawImage(original, 0, 0, 32, 32); }
                IntPtr icon = bitmap.GetHicon();
                using (var borrowed = System.Drawing.Icon.FromHandle(icon)) tray.Icon = (System.Drawing.Icon)borrowed.Clone();
                Native.DestroyIcon(icon);
            }
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Settings…", null, delegate { Dispatcher.BeginInvoke(new Action(OpenSettings)); });
            visibilityItem = new Forms.ToolStripMenuItem("Hide buddy");
            visibilityItem.Click += delegate { Dispatcher.BeginInvoke(new Action(ToggleVisible)); }; menu.Items.Add(visibilityItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Quit Toggle Goat", null, delegate { Dispatcher.BeginInvoke(new Action(Close)); });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { Dispatcher.BeginInvoke(new Action(OpenSettings)); };
        }
        internal void ReloadArt()
        {
            for (int i = 0; i < 4; i++)
            {
                try { Art[i] = Pictures.Load(i, Prefs.Pictures[i]); }
                catch { Art[i] = Pictures.Load(i, null); }
            }
            ShowState();
        }
        internal void UpdateState()
        {
            int next = Native.ReadState();
            if (next != CurrentState)
            {
                CurrentState = next; ShowState();
                tray.Text = "Toggle Goat | Num " + ((next & 1) != 0 ? "ON" : "off") + " | Caps " + ((next & 2) != 0 ? "ON" : "off");
                if (settings != null) settings.UpdateLive();
            }
            WriteDiagnostics();
        }
        internal void ShowState() { Sprite.Source = Art[PreviewState >= 0 ? PreviewState : Math.Max(0, CurrentState)]; }
        internal Forms.Screen SelectedScreen()
        {
            var all = Forms.Screen.AllScreens;
            return all.FirstOrDefault(s => s.DeviceName == Prefs.Monitor)
                ?? all.OrderByDescending(s => s.Bounds.Right).ThenBy(s => s.Bounds.Top).First();
        }
        internal void Place()
        {
            if (hwnd == IntPtr.Zero) return;
            Width = Height = Prefs.Size;
            var source = PresentationSource.FromVisual(this);
            double scale = source == null ? 1 : source.CompositionTarget.TransformToDevice.M11;
            int size = (int)Math.Round(Prefs.Size * scale), gap = (int)Math.Round(8 * scale);
            var work = SelectedScreen().WorkingArea;
            int x = Math.Max(work.Left, work.Right - size - gap), y = Math.Max(work.Top, work.Bottom - size - gap);
            Native.SetWindowPos(hwnd, new IntPtr(-1), x, y, size, size, 0x10); // never take focus
        }
        internal void ToggleVisible()
        {
            if (IsVisible) Hide(); else { Show(); Place(); }
            visibilityItem.Text = IsVisible ? "Hide buddy" : "Show buddy";
        }
        internal void OpenSettings()
        {
            if (settings == null)
            {
                settings = new Settings(this);
                settings.Closed += delegate { settings = null; PreviewState = -1; ReloadArt(); Place(); };
                settings.Show();
            }
            if (settings.WindowState == WindowState.Minimized) settings.WindowState = WindowState.Normal;
            settings.Activate();
        }
        private void WriteDiagnostics()
        {
            if (String.IsNullOrEmpty(diagnosticPath)) return;
            Native.RECT rect; Native.GetWindowRect(hwnd, out rect);
            var work = SelectedScreen().WorkingArea;
            string value = "state=" + Pictures.Keys[Math.Max(0, CurrentState)] + "\npreview=" + PreviewState
                + "\nsize=" + Prefs.Size + "\nvisible=" + IsVisible
                + "\nrect=" + rect.Left + "," + rect.Top + "," + rect.Right + "," + rect.Bottom
                + "\nwork=" + work.Left + "," + work.Top + "," + work.Right + "," + work.Bottom;
            if (value != lastDiagnostic) try { File.WriteAllText(diagnosticPath, value); lastDiagnostic = value; } catch (IOException) { }
        }
    }

    internal sealed class Settings : Window
    {
        private readonly Buddy buddy;
        private readonly Preferences draft;
        private readonly Image[] thumbnails = new Image[4];
        private readonly TextBlock[] fileNames = new TextBlock[4];
        private readonly TextBlock status = new TextBlock();
        private readonly TextBlock sizeLabel = new TextBlock();
        private readonly ComboBox monitor = new ComboBox();
        private readonly Slider size;
        private bool saved;
        private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(35, 43, 41));
        private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(100, 111, 106));
        internal Settings(Buddy owner)
        {
            buddy = owner; draft = owner.Prefs.Clone();
            Title = "Toggle Goat · Settings"; Width = 640; Height = Math.Min(850, SystemParameters.WorkArea.Height - 32); MinWidth = 550; MinHeight = 440;
            WindowStartupLocation = WindowStartupLocation.CenterScreen; Background = new SolidColorBrush(Color.FromRgb(247, 248, 244));
            FontFamily = new FontFamily("Segoe UI"); FontSize = 13; Foreground = Ink;
            var shell = new DockPanel { Margin = new Thickness(26), Background = Background }; Content = shell;
            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            DockPanel.SetDock(footer, Dock.Bottom); shell.Children.Add(footer);
            var cancel = Button("Cancel", delegate { Close(); }); footer.Children.Add(cancel);
            var save = Button("Save settings", delegate { Save(); }); save.IsDefault = true;
            save.Background = new SolidColorBrush(Color.FromRgb(205, 234, 216)); footer.Children.Add(save);
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; shell.Children.Add(scroll);
            var body = new StackPanel(); scroll.Content = body;
            body.Children.Add(new TextBlock { Text = "Toggle Goat", FontSize = 29, FontWeight = FontWeights.SemiBold });
            body.Children.Add(new TextBlock { Text = "A tiny buddy for your keyboard's big feelings.", Foreground = Muted, Margin = new Thickness(0, 4, 0, 12) });
            status.Foreground = Muted; status.Margin = new Thickness(0, 0, 0, 18); body.Children.Add(status); UpdateLive();
            body.Children.Add(new TextBlock { Text = "YOUR FOUR LOOKS", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Muted, Margin = new Thickness(0, 0, 0, 8) });
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 }; body.Children.Add(grid);
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                var card = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(10), BorderBrush = new SolidColorBrush(Color.FromRgb(224, 229, 222)), BorderThickness = new Thickness(1), Padding = new Thickness(10), Margin = new Thickness(0, 0, 9, 9) };
                var stack = new StackPanel(); card.Child = stack; grid.Children.Add(card);
                stack.Children.Add(new TextBlock { Text = Pictures.Labels[i], FontWeight = FontWeights.SemiBold });
                thumbnails[i] = new Image { Source = buddy.Art[i], Height = 77, Stretch = Stretch.Uniform, Margin = new Thickness(0, 5, 0, 4) };
                stack.Children.Add(thumbnails[i]);
                fileNames[i] = new TextBlock { FontSize = 10, Foreground = Muted, TextTrimming = TextTrimming.CharacterEllipsis }; stack.Children.Add(fileNames[i]); UpdateName(i);
                var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) }; stack.Children.Add(actions);
                actions.Children.Add(Button("Choose…", delegate { Choose(index); }));
                actions.Children.Add(Button("Preview", delegate { Preview(index); }));
                actions.Children.Add(Button("Reset", delegate { draft.Pictures[index] = null; thumbnails[index].Source = Pictures.Load(index, null); UpdateName(index); Preview(index); }));
            }
            var live = Button("Return buddy to live keys", delegate { buddy.PreviewState = -1; buddy.ShowState(); });
            live.HorizontalAlignment = HorizontalAlignment.Left; body.Children.Add(live);
            body.Children.Add(new TextBlock { Text = "Transparent PNGs look best. JPG, BMP and GIF also work (still image).", FontSize = 11, Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 16) });
            sizeLabel.FontWeight = FontWeights.SemiBold; body.Children.Add(sizeLabel);
            size = new Slider { Minimum = 72, Maximum = 288, Value = draft.Size, TickFrequency = 12, IsSnapToTickEnabled = true, Margin = new Thickness(0, 7, 0, 4) };
            size.ValueChanged += delegate { draft.Size = size.Value; buddy.Prefs.Size = size.Value; buddy.Place(); UpdateSize(); };
            body.Children.Add(size); UpdateSize();
            body.Children.Add(new TextBlock { Text = "Approximate inches; the physical size depends on your display scaling.", FontSize = 11, Foreground = Muted, Margin = new Thickness(0, 0, 0, 14) });
            body.Children.Add(new TextBlock { Text = "Screen · bottom-right, above the taskbar", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 7) });
            monitor.Items.Add(new ComboBoxItem { Content = "Rightmost screen (automatic)", Tag = "rightmost" });
            foreach (var screen in Forms.Screen.AllScreens)
                monitor.Items.Add(new ComboBoxItem { Content = screen.DeviceName + (screen.Primary ? " · primary" : ""), Tag = screen.DeviceName });
            monitor.SelectedIndex = 0;
            foreach (ComboBoxItem item in monitor.Items) if ((string)item.Tag == draft.Monitor) monitor.SelectedItem = item;
            monitor.SelectionChanged += delegate { draft.Monitor = (string)((ComboBoxItem)monitor.SelectedItem).Tag; buddy.Prefs.Monitor = draft.Monitor; buddy.Place(); };
            body.Children.Add(monitor);
            body.Children.Add(new TextBlock { Text = "Right-click your buddy or its tray icon for Settings, Hide and Quit.\nYour pictures and preferences are saved beside the app in the Data folder.", TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = Muted, Margin = new Thickness(0, 14, 0, 0) });
            var original = owner.Prefs.Clone();
            Closed += delegate { if (!saved) buddy.Prefs = original; };
        }
        private static Button Button(string text, RoutedEventHandler action)
        { var b = new Button { Content = text, Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(0, 0, 5, 0), MinHeight = 29, Cursor = System.Windows.Input.Cursors.Hand }; b.Click += action; return b; }
        private void UpdateName(int i) { fileNames[i].Text = String.IsNullOrEmpty(draft.Pictures[i]) ? "Built-in drawing" : Path.GetFileName(draft.Pictures[i]); }
        private void UpdateSize() { sizeLabel.Text = "Buddy size · " + (draft.Size / 96).ToString("0.##") + " in (approx.)"; }
        internal void UpdateLive() { int s = Math.Max(0, buddy.CurrentState); status.Text = "LIVE KEYS     NUM " + ((s & 1) != 0 ? "ON" : "OFF") + "     ·     CAPS " + ((s & 2) != 0 ? "ON" : "OFF"); }
        private void Preview(int i) { buddy.Art[i] = (BitmapSource)thumbnails[i].Source; buddy.PreviewState = i; buddy.ShowState(); }
        private void Choose(int i)
        {
            var dialog = new OpenFileDialog { Title = "Choose a picture for " + Pictures.Labels[i], Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif", CheckFileExists = true };
            if (dialog.ShowDialog(this) != true) return;
            try { var picture = Pictures.Load(i, dialog.FileName); draft.Pictures[i] = dialog.FileName; thumbnails[i].Source = picture; UpdateName(i); Preview(i); }
            catch (Exception e) { MessageBox.Show(this, "Couldn't use that picture.\n\n" + e.Message, "Choose another picture", MessageBoxButton.OK, MessageBoxImage.Information); }
        }
        private void Save()
        {
            try { buddy.Prefs = buddy.Store.Save(draft); saved = true; Close(); }
            catch (Exception e) { MessageBox.Show(this, "Couldn't save settings. Keep Toggle Goat in a folder you can write to, such as Documents.\n\n" + e.Message, "Settings not saved", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
    }

    internal static class Program
    {
        [STAThread] private static int Main(string[] args)
        {
            string data = Arg(args, "--data-dir") ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
            if (args.Contains("--self-test")) return SelfTest(data);
            if (args.Contains("--render-test")) return RenderTest(data);
            try
            {
                string hash;
                using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(data).ToUpperInvariant()))).Replace("-", "").Substring(0, 20);
                bool created;
                using (var mutex = new Mutex(true, "Local\\ToggleGoat." + hash, out created))
                using (var signal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\ToggleGoat.Open." + hash))
                {
                    if (!created) { signal.Set(); return 0; }
                    var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    var buddy = new Buddy(new Store(data), signal, Arg(args, "--diagnostics"));
                    buddy.Show();
                    if (args.Contains("--settings")) buddy.OpenSettings();
                    app.Run(); mutex.ReleaseMutex(); return 0;
                }
            }
            catch (Exception e) { MessageBox.Show("Toggle Goat couldn't start.\n\n" + e.Message, "Toggle Goat", MessageBoxButton.OK, MessageBoxImage.Error); return 1; }
        }
        private static string Arg(string[] a, string key) { int i = Array.IndexOf(a, key); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }
        private static void SaveRender(FrameworkElement element, double width, double height, string path)
        {
            element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(element);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var f = File.Create(path)) encoder.Save(f);
        }
        private static int RenderTest(string folder)
        {
            Directory.CreateDirectory(folder);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var buddy = new Buddy(new Store(folder), null, null); buddy.UpdateState();
            var settings = new Settings(buddy);
            SaveRender((FrameworkElement)settings.Content, 624, 810, Path.Combine(folder, "settings-preview.png"));
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4, Background = new SolidColorBrush(Color.FromRgb(225, 230, 224)) };
            for (int i = 0; i < 4; i++)
            {
                var stack = new StackPanel { Margin = new Thickness(12) };
                stack.Children.Add(new TextBlock { Text = Pictures.Labels[i], Margin = new Thickness(0, 0, 0, 16) });
                stack.Children.Add(new Image { Source = buddy.Art[i], Width = 144, Height = 144, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Bottom });
                grid.Children.Add(stack);
            }
            SaveRender(grid, 760, 220, Path.Combine(folder, "states-preview.png"));
            settings.Close(); buddy.Close(); return 0;
        }
        private static int SelfTest(string folder)
        {
            Directory.CreateDirectory(folder); var result = new List<string>();
            try
            {
                for (int n = 0; n < 2; n++) for (int c = 0; c < 2; c++)
                    if (Native.State(n != 0, c != 0) != n + c * 2) throw new Exception("State mapping failed");
                result.Add("PASS: all four lock-state combinations");
                for (int i = 0; i < 4; i++)
                {
                    var image = Pictures.Load(i, null);
                    if (image.PixelWidth < 20 || image.PixelHeight < 20) throw new Exception("Invalid image");
                    var raw = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
                    byte[] p = new byte[raw.PixelWidth * raw.PixelHeight * 4]; raw.CopyPixels(p, raw.PixelWidth * 4, 0);
                    if (!Enumerable.Range(0, p.Length / 4).Any(x => p[x * 4 + 3] == 0)) throw new Exception("Image lacks transparency: " + Pictures.Keys[i]);
                    result.Add("PASS: " + Pictures.Keys[i] + " decoded with transparent pixels");
                }
                var store = new Store(folder); var config = new Preferences { Size = 120, Monitor = "test-monitor" };
                store.Save(config); var loaded = store.Load();
                if (loaded.Size != 120 || loaded.Monitor != "test-monitor") throw new Exception("Settings round-trip failed");
                result.Add("PASS: saved size and monitor survive reload");
                string sample = Path.Combine(folder, "..", "import-test.png");
                using (var input = Assembly.GetExecutingAssembly().GetManifestResourceStream("ToggleGoat.idle.png"))
                using (var output = File.Create(sample)) input.CopyTo(output);
                config.Pictures[0] = sample;
                var imported = store.Save(config); loaded = store.Load();
                if (Path.GetDirectoryName(imported.Pictures[0]) != Path.GetFullPath(folder) || !File.Exists(loaded.Pictures[0])) throw new Exception("Import persistence failed");
                Pictures.Load(0, loaded.Pictures[0]);
                string xml = File.ReadAllText(Path.Combine(folder, "settings.xml"));
                if (xml.Contains(Path.GetFullPath(folder))) throw new Exception("Paths should be portable");
                result.Add("PASS: imported image is copied, reloads, and uses a portable saved path");
                config.Pictures[0] = null; store.Save(config);
                if (store.Load().Pictures[0] != null) throw new Exception("Default image reset failed");
                result.Add("PASS: resetting a custom picture restores the built-in drawing");
                File.WriteAllText(Path.Combine(folder, "settings.xml"), "invalid settings");
                if (store.Load().Size != 144) throw new Exception("Corrupt settings fallback failed");
                result.Add("PASS: corrupt settings recover to defaults");
                var bounded = new Preferences { Size = double.NaN, Pictures = new string[0] }; bounded.Normalize();
                if (bounded.Size != 144 || bounded.Pictures.Length != 4) throw new Exception("Validation failed");
                result.Add("PASS: invalid settings are normalized");
                File.WriteAllLines(Path.Combine(folder, "self-test.txt"), result); return 0;
            }
            catch (Exception e) { result.Add("FAIL: " + e); File.WriteAllLines(Path.Combine(folder, "self-test.txt"), result); return 1; }
        }
    }
}
