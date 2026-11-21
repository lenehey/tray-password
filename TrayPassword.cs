using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;

namespace TrayPassword
{
    public class Preferences
    {
        public int Length = 20;
        public bool Letters = true;
        public bool Numbers = true;
        public bool Symbols = true;
        public bool FirstUpper = false;
        public string SymbolList = "!@#$%^&*()-_=+[]{};:,.?";

        public static Preferences Load(string path)
        {
            if (!File.Exists(path)) return new Preferences();
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null, MaxCharactersInDocument = 32768 };
            using (var reader = XmlReader.Create(path, settings))
            {
                var p = (Preferences)new XmlSerializer(typeof(Preferences)).Deserialize(reader);
                if (p.Length < 4 || p.Length > 128 || p.SymbolList == null || p.SymbolList.Length > 256)
                    throw new InvalidDataException("Settings contain invalid values.");
                return p;
            }
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temp = path + ".tmp";
            using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                new XmlSerializer(typeof(Preferences)).Serialize(file, this);
                file.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
    }

    public static class Passwords
    {
        public const string LetterSet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        public const string NumberSet = "0123456789";
        public const string UpperSet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        public static List<string> Categories(Preferences p)
        {
            if (p.Length < 4 || p.Length > 128) throw new ArgumentException("Choose a length from 4 to 128.");
            var categories = new List<string>();
            if (p.Letters) categories.Add(LetterSet);
            if (p.Numbers) categories.Add(NumberSet);
            if (p.Symbols)
            {
                var symbols = new StringBuilder();
                foreach (char c in p.SymbolList ?? "")
                {
                    // Visible ASCII punctuation: keeps categories disjoint and passwords compatible.
                    if (c < 33 || c > 126 || char.IsLetterOrDigit(c))
                        throw new ArgumentException("Symbols must be punctuation, such as !@#$% (no letters, numbers or spaces).");
                    if (symbols.ToString().IndexOf(c) < 0) symbols.Append(c);
                }
                if (symbols.Length == 0) throw new ArgumentException("Enter at least one symbol, or uncheck Symbols.");
                categories.Add(symbols.ToString());
            }
            if (categories.Count == 0) throw new ArgumentException("Select at least one character category.");
            return categories;
        }

        public static string Generate(Preferences p)
        {
            var categories = Categories(p);
            string alphabet = string.Join("", categories.ToArray());
            var buffer = new byte[256];
            var result = new char[p.Length];
            int position = buffer.Length;
            // Sample the optional prefix from A-Z, and other positions from the alphabet.
            // Reject complete candidates missing a selected category. This is uniform
            // over all passwords meeting the prefix and category requirements.
            using (var rng = RandomNumberGenerator.Create())
            {
                try
                {
                    while (true)
                    {
                        int seen = 0;
                        for (int i = 0; i < result.Length; i++)
                        {
                            string allowed = i == 0 && p.FirstUpper ? UpperSet : alphabet;
                            int value;
                            do
                            {
                                if (position == buffer.Length) { rng.GetBytes(buffer); position = 0; }
                                value = buffer[position++];
                            } while (value >= 256 - (256 % allowed.Length));
                            char c = allowed[value % allowed.Length];
                            result[i] = c;
                            for (int j = 0; j < categories.Count; j++)
                                if (categories[j].IndexOf(c) >= 0) seen |= 1 << j;
                        }
                        if (seen == (1 << categories.Count) - 1) return new string(result);
                    }
                }
                finally { Array.Clear(buffer, 0, buffer.Length); Array.Clear(result, 0, result.Length); }
            }
        }
    }

    // Explicit Windows clipboard ownership and history/cloud opt-out formats.
    internal static class PrivateClipboard
    {
        [DllImport("user32.dll", SetLastError = true)] static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll")] static extern bool CloseClipboard();
        [DllImport("user32.dll")] static extern bool EmptyClipboard();
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetClipboardData(uint format, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterClipboardFormat(string name);
        [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr GlobalAlloc(uint flags, UIntPtr size);
        [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr memory);
        [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr memory);
        [DllImport("kernel32.dll")] static extern IntPtr GlobalFree(IntPtr memory);

        static void Put(uint format, byte[] bytes)
        {
            if (format == 0) throw new InvalidOperationException("Could not register clipboard protection.");
            IntPtr memory = GlobalAlloc(0x42, (UIntPtr)bytes.Length);
            if (memory == IntPtr.Zero) throw new OutOfMemoryException();
            try
            {
                IntPtr target = GlobalLock(memory);
                if (target == IntPtr.Zero) throw new InvalidOperationException("Could not lock clipboard memory.");
                try { Marshal.Copy(bytes, 0, target, bytes.Length); }
                finally { GlobalUnlock(memory); }
                if (SetClipboardData(format, memory) == IntPtr.Zero)
                    throw new InvalidOperationException("Could not write to the clipboard.");
                memory = IntPtr.Zero; // Windows owns this allocation now.
            }
            finally { if (memory != IntPtr.Zero) GlobalFree(memory); }
        }

        public static uint Copy(IntPtr owner, string text)
        {
            if (!OpenClipboard(owner)) throw new InvalidOperationException("Clipboard is busy. Please try Copy again.");
            byte[] bytes = Encoding.Unicode.GetBytes(text + "\0");
            try
            {
                if (!EmptyClipboard()) throw new InvalidOperationException("Could not open the clipboard for writing.");
                try
                {
                    Put(RegisterClipboardFormat("CanIncludeInClipboardHistory"), new byte[4]);
                    Put(RegisterClipboardFormat("CanUploadToCloudClipboard"), new byte[4]);
                    Put(RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing"), new byte[4]);
                    Put(13, bytes); // CF_UNICODETEXT
                    return GetClipboardSequenceNumber();
                }
                catch { EmptyClipboard(); throw; }
            }
            finally { Array.Clear(bytes, 0, bytes.Length); CloseClipboard(); }
        }

        public static bool ClearIfUnchanged(IntPtr owner, uint sequence)
        {
            if (sequence == 0 || GetClipboardSequenceNumber() != sequence) return true;
            if (!OpenClipboard(owner)) return false;
            try { return GetClipboardSequenceNumber() != sequence || EmptyClipboard(); }
            finally { CloseClipboard(); }
        }
    }

    internal sealed class PasswordWindow : Form
    {
        readonly string settingsPath;
        readonly EventWaitHandle wake;
        readonly NotifyIcon tray;
        readonly ComboBox length = new ComboBox();
        readonly CheckBox letters = new CheckBox(), numbers = new CheckBox(), symbols = new CheckBox();
        readonly CheckBox firstUpper = new CheckBox();
        readonly TextBox symbolList = new TextBox(), password = new TextBox();
        readonly Button copy = new Button();
        readonly Label status = new Label();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        bool ready, quitting, cleanedUp;
        Icon customIcon;
        string saveError;
        uint clipboardSequence;
        DateTime clearAt;

        public PasswordWindow(string path, EventWaitHandle signal)
        {
            settingsPath = path; wake = signal;
            Text = "Tray Password";
            Font = new Font("Segoe UI", 10F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(360, 218);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Icon = SystemIcons.Shield;
            using (var iconStream = typeof(PasswordWindow).Assembly.GetManifestResourceStream("TrayPassword.Icon"))
            {
                if (iconStream != null)
                {
                    using (var loadedIcon = new Icon(iconStream)) customIcon = (Icon)loadedIcon.Clone();
                    Icon = customIcon;
                }
            }
            BackColor = Color.FromArgb(248, 250, 252);
            Controls.Add(new Label { Text = "Length", AutoSize = true, Location = new Point(12, 16) });
            length.SetBounds(264, 10, 84, 28); length.DropDownStyle = ComboBoxStyle.DropDownList;
            for (int i = 4; i <= 128; i++) length.Items.Add(i);
            length.AccessibleName = "Password length"; Controls.Add(length);
            letters.Text = "Letters (A-Z, a-z)"; letters.SetBounds(12, 44, 180, 26);
            numbers.Text = "Numbers (0-9)"; numbers.SetBounds(198, 44, 150, 26);
            symbols.Text = "Symbols"; symbols.SetBounds(12, 76, 90, 26);
            symbolList.SetBounds(106, 76, 242, 26); symbolList.MaxLength = 256;
            symbolList.AccessibleName = "Allowed symbols";
            Controls.AddRange(new Control[] { letters, numbers, symbols, symbolList });
            firstUpper.Text = "First character A-Z"; firstUpper.SetBounds(12, 108, 336, 26);
            Controls.Add(firstUpper);
            password.SetBounds(12, 142, 336, 30);
            password.Font = new Font("Consolas", 12F); password.ReadOnly = true;
            password.BackColor = Color.White; password.AccessibleName = "Generated password";
            Controls.Add(password);
            var generate = new Button { Text = "Generate new", Location = new Point(12, 180), Size = new Size(162, 28) };
            copy.Text = "Copy password"; copy.SetBounds(186, 180, 162, 28);
            Controls.AddRange(new Control[] { generate, copy });
            status.SetBounds(12, 216, 336, 58); status.Visible = false;
            status.ForeColor = Color.FromArgb(80, 91, 108);
            Controls.Add(status);
            AcceptButton = generate;
            Preferences p;
            try { p = Preferences.Load(path); }
            catch (Exception) { p = new Preferences(); saveError = "Could not read saved settings. Defaults loaded; changes will replace them."; }
            length.SelectedItem = p.Length;
            letters.Checked = p.Letters; numbers.Checked = p.Numbers; symbols.Checked = p.Symbols;
            firstUpper.Checked = p.FirstUpper;
            symbolList.Text = p.SymbolList; symbolList.Enabled = symbols.Checked;
            length.SelectedIndexChanged += Changed;
            letters.CheckedChanged += Changed; numbers.CheckedChanged += Changed; symbols.CheckedChanged += Changed;
            firstUpper.CheckedChanged += Changed;
            symbolList.TextChanged += Changed;
            generate.Click += delegate { Generate(); };
            copy.Click += delegate
            {
                try
                {
                    clipboardSequence = PrivateClipboard.Copy(Handle, password.Text);
                    clearAt = DateTime.UtcNow.AddSeconds(30);
                    Status("Copied. Clipboard clears in 30 seconds.", false);
                }
                catch (Exception ex) { Status(ex.Message, true); }
            };
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open / generate password", null, delegate { Open(); });
            menu.Items.Add("Exit", null, delegate { Quit(); });
            tray = new NotifyIcon { Text = "Tray Password", Icon = Icon, ContextMenuStrip = menu, Visible = true };
            tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) Open(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (e.CloseReason == CloseReason.UserClosing && !quitting)
                { e.Cancel = true; Hide(); password.Clear(); copy.Enabled = false; }
            };
            Resize += delegate { if (WindowState == FormWindowState.Minimized) { Hide(); password.Clear(); copy.Enabled = false; } };
            timer.Interval = 250;
            timer.Tick += delegate
            {
                if (wake.WaitOne(0)) Open();
                if (clipboardSequence != 0 && DateTime.UtcNow >= clearAt)
                {
                    if (PrivateClipboard.ClearIfUnchanged(Handle, clipboardSequence))
                    { clipboardSequence = 0; if (Visible) Status(saveError ?? "", saveError != null); }
                }
            };
            timer.Start(); ready = true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !cleanedUp)
            {
                cleanedUp = true;
                timer.Stop();
                if (IsHandleCreated) PrivateClipboard.ClearIfUnchanged(Handle, clipboardSequence);
                tray.Visible = false; tray.Dispose(); timer.Dispose();
                if (customIcon != null) customIcon.Dispose();
            }
            base.Dispose(disposing);
        }
        Preferences Current()
        {
            return new Preferences { Length = (int)length.SelectedItem, Letters = letters.Checked,
                Numbers = numbers.Checked, Symbols = symbols.Checked, SymbolList = symbolList.Text,
                FirstUpper = firstUpper.Checked };
        }
        void Status(string text, bool error)
        {
            status.Text = text; status.Visible = !string.IsNullOrEmpty(text);
            status.ForeColor = error ? Color.Firebrick : Color.FromArgb(80, 91, 108);
            ClientSize = new Size(ClientSize.Width, (int)Math.Round((status.Visible ? 280 : 218) * DeviceDpiScale()));
        }
        double DeviceDpiScale()
        { using (var graphics = CreateGraphics()) return graphics.DpiY / 96.0; }
        void Changed(object sender, EventArgs e)
        {
            if (!ready) return;
            symbolList.Enabled = symbols.Checked;
            try { Current().Save(settingsPath); saveError = null; }
            catch (Exception) { saveError = "Settings could not be saved. Check folder access and available disk space."; }
            Generate();
        }
        void Generate()
        {
            try
            {
                password.Text = Passwords.Generate(Current()); copy.Enabled = true;
                Status(saveError ?? "", saveError != null);
            }
            catch (Exception ex) { password.Clear(); copy.Enabled = false; Status(ex.Message, true); }
        }
        public void Open()
        { WindowState = FormWindowState.Normal; Show(); Generate(); Activate(); }
        void Quit() { quitting = true; Application.Exit(); }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Hide(); password.Clear(); copy.Enabled = false; return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TrayPassword", "settings.xml");
            bool show = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--show") show = true;
                else if (args[i] == "--settings" && i + 1 < args.Length) path = Path.GetFullPath(args[++i]);
            }
            // Per-user, per-settings identity also permits isolated verification runs.
            string identity;
            using (var hash = SHA256.Create())
                identity = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(
                    Environment.UserName + "|" + Path.GetFullPath(path).ToUpperInvariant()))).Replace("-", "");
            bool first;
            using (var mutex = new Mutex(true, "Local\\TrayPassword-" + identity, out first))
            using (var wake = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\TrayPasswordWake-" + identity))
            {
                if (!first) { wake.Set(); return; }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                var window = new PasswordWindow(path, wake);
                // A message loop without a main form keeps startup completely in the tray.
                var handle = window.Handle;
                if (show) window.Open();
                Application.Run();
                window.Dispose();
                mutex.ReleaseMutex();
            }
        }
    }
}
