using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace TimeSyncTool
{
    internal static class Program
    {
        public const string DefaultServers = "ntp.aliyun.com,ntp1.aliyun.com,time1.cloud.tencent.com,cn.pool.ntp.org,time.windows.com";
        public const string TaskName = "Win7-AutoTimeSync";

        [STAThread]
        private static int Main(string[] args)
        {
            bool install = false;
            bool uninstall = false;
            bool dryRun = false;
            bool silent = false;
            string servers = DefaultServers;
            int everyHours = 6;
            int bootMode = -1;
            int timerMode = -1;

            foreach (string raw in args)
            {
                string a = raw.Trim().ToLowerInvariant();
                if (a == "/install" || a == "-install") install = true;
                else if (a == "/uninstall" || a == "-uninstall") uninstall = true;
                else if (a == "/dryrun" || a == "-dryrun") dryRun = true;
                else if (a == "/silent" || a == "-silent") { silent = true; }
                else if (a.StartsWith("/servers:")) servers = raw.Substring(raw.IndexOf(':') + 1);
                else if (a.StartsWith("/every:")) { int.TryParse(raw.Substring(raw.IndexOf(':') + 1), out everyHours); }
                else if (a.StartsWith("/boot:")) { int.TryParse(raw.Substring(raw.IndexOf(':') + 1), out bootMode); }
                else if (a.StartsWith("/timer:")) { int.TryParse(raw.Substring(raw.IndexOf(':') + 1), out timerMode); }
            }

            Core.Init();

            if (install || uninstall || dryRun || silent || bootMode >= 0 || timerMode >= 0)
            {
                return RunConsoleMode(install, uninstall, dryRun, silent, servers, bootMode, timerMode, everyHours);
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }

        private static int RunConsoleMode(bool install, bool uninstall, bool dryRun, bool silent, string servers, int bootMode, int timerMode, int everyHours)
        {
            string msg = string.Empty;

            if (install || uninstall)
            {
                if (!Core.IsAdmin()) { msg = "需要管理员权限。"; Core.Log(msg); if (!silent) Core.MsgBox(msg, true); return 2; }
                msg = install ? Core.InstallTasks() : Core.RemoveTasks();
                msg = msg + " | " + Core.EnableBootTask(install) + " | " + Core.EnableTimerTask(install, everyHours);
                Core.Log(msg);
                if (!silent) Core.MsgBox(msg, false);
                return 0;
            }

            if (bootMode >= 0 || timerMode >= 0)
            {
                if (!Core.IsAdmin()) { msg = "需要管理员权限。"; Core.Log(msg); if (!silent) Core.MsgBox(msg, true); return 2; }
                StringBuilder sb = new StringBuilder();
                if (bootMode >= 0)
                {
                    bool okBoot = Core.EnableBootTask(bootMode == 1);
                    sb.Append("开机自动同步 → " + (bootMode == 1 ? "已开启" : "已关闭") + (okBoot ? "" : "（失败）"));
                }
                if (timerMode >= 0)
                {
                    if (sb.Length > 0) sb.Append("；");
                    bool okTimer = Core.EnableTimerTask(timerMode == 1, everyHours);
                    sb.Append("定时自动同步 → " + (timerMode == 1 ? "已开启（每 " + everyHours + " 小时）" : "已关闭") + (okTimer ? "" : "（失败）"));
                }
                msg = sb.ToString();
                Core.Log(msg);
                if (!silent) Core.MsgBox(msg, false);
                return 0;
            }

            Core.SyncResult r = Core.DoSync(servers, !dryRun, string.Empty);
            Core.Log(r.Message.Replace("\r\n", " | "));
            if (!silent) Core.MsgBox(r.Message, !r.Ok);
            return r.Ok ? 0 : 1;
        }
    }

    internal static class Core
    {
        public class SyncResult
        {
            public bool Ok;
            public bool Wrote;
            public double Offset;
            public double Rtt;
            public string Server = string.Empty;
            public string Message = string.Empty;
        }

        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint TOKEN_QUERY = 0x0008;
        private const uint SE_PRIVILEGE_ENABLED = 0x00000002;

        private static string _logPath = string.Empty;

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEMTIME
        {
            public ushort wYear; public ushort wMonth; public ushort wDayOfWeek; public ushort wDay;
            public ushort wHour; public ushort wMinute; public ushort wSecond; public ushort wMilliseconds;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID { public uint LowPart; public int HighPart; }

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID_AND_ATTRIBUTES { public LUID Luid; public uint Attributes; }

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES { public uint PrivilegeCount; public LUID_AND_ATTRIBUTES Privilege; }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
        private static extern int MessageBoxNative(IntPtr hWnd, string text, string caption, int type);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetSystemTime(ref SYSTEMTIME st);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LookupPrivilegeValueW(string system, string name, ref LUID luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES newState, int bufferLength, IntPtr previousState, IntPtr returnLength);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, uint accessMask, out IntPtr token);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        public static void Init()
        {
            string exeDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
            _logPath = Path.Combine(exeDir, "TimeSync.log");
        }

        public static bool IsAdmin()
        {
            WindowsIdentity id = WindowsIdentity.GetCurrent();
            WindowsPrincipal pr = new WindowsPrincipal(id);
            return pr.IsInRole(WindowsBuiltInRole.Administrator);
        }

        public static void MsgBox(string text, bool warn)
        {
            MessageBoxNative(IntPtr.Zero, text, "Win7 自动校时", warn ? 0x30 : 0x40);
        }

        public static void Log(string line)
        {
            try
            {
                if (_logPath.Length == 0) Init();
                string text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line.Replace("\r\n", " | ") + Environment.NewLine;
                File.AppendAllText(_logPath, text, Encoding.UTF8);
                string[] all = File.ReadAllLines(_logPath, Encoding.UTF8);
                if (all.Length > 400)
                {
                    string[] keep = new string[200];
                    Array.Copy(all, all.Length - 200, keep, 0, 200);
                    File.WriteAllLines(_logPath, keep, Encoding.UTF8);
                }
            }
            catch { }
        }

        private static long NtpFieldToTicks(byte[] buffer, int offset)
        {
            long sec = ((long)buffer[offset] << 24) | ((long)buffer[offset + 1] << 16) | ((long)buffer[offset + 2] << 8) | (long)buffer[offset + 3];
            long frac = ((long)buffer[offset + 4] << 24) | ((long)buffer[offset + 5] << 16) | ((long)buffer[offset + 6] << 8) | (long)buffer[offset + 7];
            long baseTicks = new DateTime(1900, 1, 1).Ticks;
            return baseTicks + (sec * 10000000L) + (long)Math.Round((double)frac * 10000000.0 / 4294967296.0);
        }

        private static bool Query(string server, out double offset, out double rtt)
        {
            offset = 0;
            rtt = 0;
            UdpClient udp = null;
            try
            {
                udp = new UdpClient();
                udp.Client.ReceiveTimeout = 3000;
                udp.Client.SendTimeout = 3000;
                IPAddress ip;
                if (IPAddress.TryParse(server, out ip)) udp.Connect(new IPEndPoint(ip, 123));
                else udp.Connect(server, 123);

                byte[] request = new byte[48];
                request[0] = 0x1B;

                long t1 = DateTime.UtcNow.Ticks;
                udp.Send(request, request.Length);
                IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                byte[] response = udp.Receive(ref remote);
                long t4 = DateTime.UtcNow.Ticks;

                if (response == null || response.Length < 48) return false;
                if (response[1] == 0) return false;

                long t2 = NtpFieldToTicks(response, 32);
                long t3 = NtpFieldToTicks(response, 40);

                offset = ((double)((t2 - t1) + (t3 - t4)) / 2.0) / 10000000.0;
                rtt = (double)(t4 - t1) / 10000000.0;
                if (offset > 86400.0 || offset < -86400.0) return false;
                return true;
            }
            catch { return false; }
            finally { if (udp != null) { try { udp.Close(); } catch { } } }
        }

        public static SyncResult DoSync(string serverList, bool writeTime, string preferred)
        {
            SyncResult r = new SyncResult();
            string[] parts = serverList.Split(',');
            StringBuilder errs = new StringBuilder();

            foreach (string p in parts)
            {
                string srv = p.Trim();
                if (srv.Length == 0) continue;
                double o, rt;
                if (Query(srv, out o, out rt))
                {
                    r.Ok = true;
                    r.Offset = o;
                    r.Rtt = rt;
                    r.Server = srv;
                    break;
                }
                errs.Append(srv + " ");
            }

            if (!r.Ok)
            {
                r.Message = "所有时间服务器均不可达。\r\n\r\n失败列表：" + errs.ToString().Trim() + "\r\n请检查网络，或防火墙是否放行 UDP 123 出站。";
                return r;
            }

            string head = "服务器 " + r.Server + "    偏差 " + r.Offset.ToString("F2") + " 秒    往返 " + ((int)Math.Round(r.Rtt * 1000)) + " ms";
            string note = string.Empty;
            if (preferred != null && preferred.Length > 0 && !string.Equals(preferred, r.Server, StringComparison.OrdinalIgnoreCase))
            {
                note = "选中的 " + preferred + " 连不上，已自动改用 " + r.Server + "。\r\n";
            }
            head = note + head;

            if (!writeTime)
            {
                r.Message = "[仅检测，未改动时间]\r\n" + head;
                return r;
            }

            if (Math.Abs(r.Offset) < 0.5)
            {
                r.Message = "时间已准确，无需调整。\r\n" + head;
                return r;
            }

            if (!IsAdmin())
            {
                r.Ok = false;
                r.Message = "检测到偏差 " + r.Offset.ToString("F2") + " 秒，但没有管理员权限，无法修改系统时间。";
                return r;
            }

            EnablePrivilege("SeSystemtimePrivilege");

            long targetTicks = DateTime.UtcNow.Ticks + (long)Math.Round(r.Offset * 10000000.0);
            DateTime target = new DateTime(targetTicks, DateTimeKind.Utc);

            SYSTEMTIME st = new SYSTEMTIME();
            st.wYear = (ushort)target.Year;
            st.wMonth = (ushort)target.Month;
            st.wDay = (ushort)target.Day;
            st.wDayOfWeek = (ushort)(int)target.DayOfWeek;
            st.wHour = (ushort)target.Hour;
            st.wMinute = (ushort)target.Minute;
            st.wSecond = (ushort)target.Second;
            st.wMilliseconds = (ushort)target.Millisecond;

            if (!SetSystemTime(ref st))
            {
                r.Ok = false;
                r.Message = "写入系统时间失败，错误码 " + Marshal.GetLastWin32Error() + "。";
                return r;
            }

            r.Wrote = true;
            r.Message = "已校准完成！\r\n" + head;
            return r;
        }

        private static void EnablePrivilege(string privilegeName)
        {
            IntPtr token = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out token)) return;
                TOKEN_PRIVILEGES tp = new TOKEN_PRIVILEGES();
                tp.PrivilegeCount = 1;
                tp.Privilege.Attributes = SE_PRIVILEGE_ENABLED;
                LUID luid = new LUID();
                if (!LookupPrivilegeValueW(null, privilegeName, ref luid)) return;
                tp.Privilege.Luid = luid;
                AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            }
            catch { }
            finally { if (token != IntPtr.Zero) CloseHandle(token); }
        }

        private static string SchtasksPath()
        {
            string win = Environment.GetEnvironmentVariable("SystemRoot");
            if (win == null || win.Length == 0) win = "C:\\Windows";
            return Path.Combine(Path.Combine(win, "System32"), "schtasks.exe");
        }

        private static int RunSchtasks(string arguments)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = SchtasksPath();
                psi.Arguments = arguments;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                Process proc = Process.Start(psi);
                proc.WaitForExit();
                return proc.ExitCode;
            }
            catch { return -1; }
        }

        private static string TaskCommand()
        {
            return "\"" + Process.GetCurrentProcess().MainModule.FileName + "\" /silent";
        }

        public static bool TaskExists(string name)
        {
            return RunSchtasks("/query /tn \"" + name + "\"") == 0;
        }

        public static bool EnableBootTask(bool enable)
        {
            if (!IsAdmin()) return false;
            string name = Program.TaskName + "-Boot";
            if (enable)
            {
                string args = "/create /tn \"" + name + "\" /tr \"" + TaskCommand() + "\" /sc onstart /delay 0001:00 /ru SYSTEM /rl HIGHEST /f";
                return RunSchtasks(args) == 0;
            }
            RunSchtasks("/delete /tn \"" + name + "\" /f");
            return !TaskExists(name);
        }

        public static bool EnableTimerTask(bool enable, int hours)
        {
            if (!IsAdmin()) return false;
            string name = Program.TaskName;
            if (hours < 1) hours = 1;
            if (hours > 24) hours = 24;
            if (enable)
            {
                string args = "/create /tn \"" + name + "\" /tr \"" + TaskCommand() + "\" /sc hourly /mo " + hours + " /ru SYSTEM /rl HIGHEST /f";
                return RunSchtasks(args) == 0;
            }
            RunSchtasks("/delete /tn \"" + name + "\" /f");
            return !TaskExists(name);
        }

        public static string InstallTasks()
        {
            if (!IsAdmin()) return "需要管理员权限才能安装计划任务。";
            return "自动校时设置：开机 " + (EnableBootTask(true) ? "开" : "失败") + "，定时 " + (EnableTimerTask(true, 6) ? "开" : "失败"); 
        }

        public static string RemoveTasks()
        {
            if (!IsAdmin()) return "需要管理员权限才能修改计划任务。";
            return "自动校时设置：开机 " + (EnableBootTask(false) ? "关" : "失败") + "，定时 " + (EnableTimerTask(false, 6) ? "关" : "失败");
        }
    }

    internal class MainForm : Form
    {
        private Label lblClock;
        private Label lblResult;
        private ComboBox cboServer;
        private Button btnAddSrv;
        private Button btnDelSrv;
        private CheckBox chkOnlyServers;
        private Button btnSync;
        private CheckBox chkBoot;
        private CheckBox chkTimer;
        private ComboBox cboHours;
        private Button btnClose;
        private Label lblTaskState;
        private System.Windows.Forms.Timer clockTimer;
        private bool _busy;
        private bool _loading;
        private static readonly string[] Presets = new string[]
        {
            "ntp.aliyun.com",
            "time1.cloud.tencent.com",
            "ntp.ntsc.ac.cn",
            "cn.pool.ntp.org",
            "time.windows.com",
            "120.25.115.20"
        };
        private readonly System.Collections.Generic.List<string> _customs = new System.Collections.Generic.List<string>();

        public MainForm()
        {
            Text = "Win7 自动校时";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(460, 428);
            Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular);

            Label lblTitle = new Label();
            lblTitle.Text = "系统时间校准";
            lblTitle.Font = new Font("Microsoft YaHei", 12f, FontStyle.Bold);
            lblTitle.Location = new Point(18, 12);
            lblTitle.Size = new Size(360, 26);
            Controls.Add(lblTitle);

            lblClock = new Label();
            lblClock.Location = new Point(18, 44);
            lblClock.Size = new Size(420, 24);
            lblClock.ForeColor = Color.FromArgb(0, 90, 160);
            lblClock.Font = new Font("Microsoft YaHei", 10f, FontStyle.Bold);
            Controls.Add(lblClock);

            lblResult = new Label();
            lblResult.Text = "状态：启动后自动检测偏差…";
            lblResult.Location = new Point(18, 72);
            lblResult.Size = new Size(424, 44);
            lblResult.ForeColor = Color.FromArgb(90, 90, 90);
            Controls.Add(lblResult);

            GroupBox grpSrv = new GroupBox();
            grpSrv.Text = "时间服务器（下拉选择，也可手动添加）";
            grpSrv.Location = new Point(14, 114);
            grpSrv.Size = new Size(432, 124);
            Controls.Add(grpSrv);

            Label lblSrvTitle = new Label();
            lblSrvTitle.Text = "选择服务器";
            lblSrvTitle.Location = new Point(10, 22);
            lblSrvTitle.Size = new Size(90, 18);
            lblSrvTitle.ForeColor = Color.Gray;
            grpSrv.Controls.Add(lblSrvTitle);

            cboServer = new ComboBox();
            cboServer.DropDownStyle = ComboBoxStyle.DropDown;
            cboServer.Location = new Point(10, 44);
            cboServer.Size = new Size(300, 24);
            cboServer.Font = new Font("Consolas", 9f, FontStyle.Regular);
            grpSrv.Controls.Add(cboServer);

            btnAddSrv = new Button();
            btnAddSrv.Text = "添加";
            btnAddSrv.Location = new Point(318, 43);
            btnAddSrv.Size = new Size(52, 26);
            btnAddSrv.Click += new EventHandler(btnAddSrv_Click);
            grpSrv.Controls.Add(btnAddSrv);

            btnDelSrv = new Button();
            btnDelSrv.Text = "删除";
            btnDelSrv.Location = new Point(374, 43);
            btnDelSrv.Size = new Size(52, 26);
            btnDelSrv.Click += new EventHandler(btnDelSrv_Click);
            grpSrv.Controls.Add(btnDelSrv);

            chkOnlyServers = new CheckBox();
            chkOnlyServers.Text = "只用所选服务器（连不上就报错，不自动换源）";
            chkOnlyServers.Location = new Point(10, 72);
            chkOnlyServers.Size = new Size(380, 22);
            grpSrv.Controls.Add(chkOnlyServers);

            Label lblSrvHint = new Label();
            lblSrvHint.Text = "内网 NTP 服务器：在框里输入 IP 或域名后点「添加」，配置会记在同目录 servers.ini";
            lblSrvHint.Location = new Point(10, 96);
            lblSrvHint.Size = new Size(412, 20);
            lblSrvHint.ForeColor = Color.Gray;
            grpSrv.Controls.Add(lblSrvHint);

            btnSync = new Button();
            btnSync.Text = "立即同步";
            btnSync.Font = new Font("Microsoft YaHei", 10f, FontStyle.Bold);
            btnSync.Location = new Point(18, 246);
            btnSync.Size = new Size(140, 42);
            btnSync.Click += new EventHandler(btnSync_Click);
            Controls.Add(btnSync);

            lblTaskState = new Label();
            lblTaskState.Location = new Point(170, 252);
            lblTaskState.Size = new Size(276, 32);
            lblTaskState.ForeColor = Color.Gray;
            Controls.Add(lblTaskState);

            GroupBox grpAuto = new GroupBox();
            grpAuto.Text = "自动同步设置";
            grpAuto.Location = new Point(14, 296);
            grpAuto.Size = new Size(432, 88);
            Controls.Add(grpAuto);

            chkBoot = new CheckBox();
            chkBoot.Text = "开机后自动同步（开机 1 分钟后执行一次）";
            chkBoot.Location = new Point(12, 22);
            chkBoot.Size = new Size(340, 22);
            chkBoot.CheckedChanged += new EventHandler(chkBoot_CheckedChanged);
            grpAuto.Controls.Add(chkBoot);

            chkTimer = new CheckBox();
            chkTimer.Text = "定时自动同步，每";
            chkTimer.Location = new Point(12, 52);
            chkTimer.Size = new Size(140, 22);
            chkTimer.CheckedChanged += new EventHandler(chkTimer_CheckedChanged);
            grpAuto.Controls.Add(chkTimer);

            cboHours = new ComboBox();
            cboHours.DropDownStyle = ComboBoxStyle.DropDownList;
            cboHours.Location = new Point(154, 50);
            cboHours.Size = new Size(70, 24);
            cboHours.Items.Add("1");
            cboHours.Items.Add("2");
            cboHours.Items.Add("3");
            cboHours.Items.Add("6");
            cboHours.Items.Add("12");
            cboHours.Items.Add("24");
            cboHours.SelectedIndex = 3;
            cboHours.SelectedIndexChanged += new EventHandler(cboHours_SelectedIndexChanged);
            grpAuto.Controls.Add(cboHours);

            Label lblUnit = new Label();
            lblUnit.Text = "小时执行一次（以 SYSTEM 身份运行，无弹窗）";
            lblUnit.Location = new Point(230, 54);
            lblUnit.Size = new Size(196, 20);
            lblUnit.ForeColor = Color.Gray;
            grpAuto.Controls.Add(lblUnit);

            btnClose = new Button();
            btnClose.Text = "关闭";
            btnClose.Location = new Point(358, 390);
            btnClose.Size = new Size(88, 28);
            btnClose.Click += new EventHandler(btnClose_Click);
            Controls.Add(btnClose);

            clockTimer = new System.Windows.Forms.Timer();
            clockTimer.Interval = 1000;
            clockTimer.Tick += new EventHandler(clockTimer_Tick);
            clockTimer.Start();

            LoadConfig();
            UpdateClock();
            LoadTaskState();
            Shown += new EventHandler(MainForm_Shown);
        }

        private string ConfigPath()
        {
            string exeDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
            return Path.Combine(exeDir, "servers.ini");
        }

        private void LoadConfig()
        {
            _loading = true;
            string savedHour = "6";
            string savedServer = string.Empty;

            try
            {
                if (File.Exists(ConfigPath()))
                {
                    string[] lines = File.ReadAllLines(ConfigPath(), Encoding.UTF8);
                    foreach (string raw in lines)
                    {
                        string line = raw.Trim();
                        if (line.Length == 0 || line.StartsWith("#")) continue;
                        int eq = line.IndexOf('=');
                        if (eq < 0) continue;
                        string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                        string v = line.Substring(eq + 1).Trim();
                        if (k == "server") savedServer = v;
                        else if (k == "hours") savedHour = v;
                        else if (k == "custom")
                        {
                            if (v.Length > 0 && !_customs.Contains(v)) _customs.Add(v);
                        }
                    }
                }
            }
            catch (Exception ex) { Core.Log("读取配置失败: " + ex.Message); }

            RefreshServerItems(savedServer, savedHour);
            _loading = false;
            Core.Log("加载配置: server=" + savedServer + " hours=" + savedHour + " 自定义=" + _customs.Count + " 选中=" + cboServer.Text);
        }

        private void RefreshServerItems(string selectThis, string hoursText)
        {
            cboServer.Items.Clear();
            foreach (string p in Presets) cboServer.Items.Add(p);
            foreach (string c in _customs) cboServer.Items.Add(c);

            bool found = false;
            if (selectThis != null && selectThis.Length > 0)
            {
                for (int i = 0; i < cboServer.Items.Count; i++)
                {
                    if (string.Equals(cboServer.Items[i].ToString(), selectThis, StringComparison.OrdinalIgnoreCase))
                    {
                        cboServer.SelectedIndex = i;
                        found = true;
                        break;
                    }
                }
            }
            if (!found)
            {
                if (cboServer.Items.Count > 0) cboServer.SelectedIndex = 0;
                else cboServer.Text = Presets[0];
            }

            if (hoursText != null)
            {
                for (int i = 0; i < cboHours.Items.Count; i++)
                {
                    if (cboHours.Items[i].ToString() == hoursText)
                    {
                        cboHours.SelectedIndex = i;
                        break;
                    }
                }
            }
        }

        private void SaveConfig()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# 自动校时配置，可手工编辑");
                sb.AppendLine("server=" + cboServer.Text.Trim());
                sb.AppendLine("hours=" + cboHours.SelectedItem);
                foreach (string c in _customs) sb.AppendLine("custom=" + c);
                File.WriteAllText(ConfigPath(), sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        private static bool IsValidHost(string s)
        {
            if (s == null) return false;
            s = s.Trim();
            if (s.Length < 1 || s.Length > 253) return false;
            return System.Text.RegularExpressions.Regex.IsMatch(s, @"^[A-Za-z0-9]([A-Za-z0-9\-\.]*[A-Za-z0-9])?$");
        }

        private void btnAddSrv_Click(object sender, EventArgs e)
        {
            string host = cboServer.Text.Trim();
            if (!IsValidHost(host))
            {
                MessageBox.Show("请填写合法的主机名或 IP，例如 ntp.mycompany.cn 或 10.0.0.5", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool known = false;
            for (int i = 0; i < cboServer.Items.Count; i++)
            {
                if (string.Equals(cboServer.Items[i].ToString(), host, StringComparison.OrdinalIgnoreCase))
                {
                    cboServer.SelectedIndex = i;
                    known = true;
                    break;
                }
            }
            if (!known)
            {
                if (!_customs.Contains(host)) _customs.Add(host);
                RefreshServerItems(host, null);
                Core.Log("添加服务器: " + host);
            }
            SaveConfig();
            lblResult.Text = "状态：已选择服务器 " + host + "，点「立即同步」开始校时";
            lblResult.ForeColor = Color.FromArgb(90, 90, 90);
        }

        private void btnDelSrv_Click(object sender, EventArgs e)
        {
            string host = cboServer.Text.Trim();
            if (_customs.Count == 0)
            {
                MessageBox.Show("列表里没有自定义服务器，内置服务器不可删除。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            bool removed = false;
            for (int i = _customs.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_customs[i], host, StringComparison.OrdinalIgnoreCase))
                {
                    _customs.RemoveAt(i);
                    removed = true;
                }
            }
            if (!removed)
            {
                MessageBox.Show("「" + host + "」是内置服务器，不可删除。只能删自己添加的。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Core.Log("删除服务器: " + host);
            RefreshServerItems(null, null);
            SaveConfig();
        }

        private string BuildServerList(bool onlySelected)
        {
            string current = cboServer.Text.Trim();
            if (onlySelected) return current;

            StringBuilder sb = new StringBuilder();
            if (current.Length > 0) sb.Append(current);
            foreach (string p in Presets)
            {
                if (!string.Equals(p, current, StringComparison.OrdinalIgnoreCase)) sb.Append("," + p);
            }
            foreach (string c in _customs)
            {
                if (!string.Equals(c, current, StringComparison.OrdinalIgnoreCase)) sb.Append("," + c);
            }
            return sb.ToString().TrimStart(',');
        }

        private void UpdateClock()
        {
            lblClock.Text = "当前时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "   " + DayOfWeekCn(DateTime.Now.DayOfWeek);
        }

        private static string DayOfWeekCn(DayOfWeek d)
        {
            string[] names = new string[] { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" };
            return names[(int)d];
        }

        private void LoadTaskState()
        {
            _loading = true;
            bool boot = Core.TaskExists(Program.TaskName + "-Boot");
            bool timer = Core.TaskExists(Program.TaskName);
            chkBoot.Checked = boot;
            chkTimer.Checked = timer;
            _loading = false;
            UpdateTaskLabel(boot, timer);
        }

        private void UpdateTaskLabel(bool boot, bool timer)
        {
            if (!boot && !timer) lblTaskState.Text = "当前：未开启自动同步";
            else lblTaskState.Text = "当前：" + (boot ? "开机同步 " : "") + (timer ? "定时同步 " : "") + "已开启";
        }

        private void chkBoot_CheckedChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            bool want = chkBoot.Checked;
            bool ok = Core.EnableBootTask(want);
            Core.Log("开机自动同步 => " + (want ? "开" : "关") + (ok ? " 成功" : " 失败"));
            if (!ok)
            {
                _loading = true;
                chkBoot.Checked = !want;
                _loading = false;
                MessageBox.Show("设置开机自动同步失败，可能需要管理员权限。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            UpdateTaskLabel(chkBoot.Checked, chkTimer.Checked);
        }

        private void chkTimer_CheckedChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            bool want = chkTimer.Checked;
            int hours = int.Parse(cboHours.SelectedItem.ToString());
            bool ok = Core.EnableTimerTask(want, hours);
            Core.Log("定时自动同步 => " + (want ? "开/" + hours + "小时" : "关") + (ok ? " 成功" : " 失败"));
            if (!ok)
            {
                _loading = true;
                chkTimer.Checked = !want;
                _loading = false;
                MessageBox.Show("设置定时自动同步失败，可能需要管理员权限。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            UpdateTaskLabel(chkBoot.Checked, chkTimer.Checked);
        }

        private void cboHours_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            SaveConfig();
            if (!chkTimer.Checked) return;
            int hours = int.Parse(cboHours.SelectedItem.ToString());
            bool ok = Core.EnableTimerTask(true, hours);
            Core.Log("定时间隔改为 " + hours + " 小时" + (ok ? " 成功" : " 失败"));
            UpdateTaskLabel(chkBoot.Checked, chkTimer.Checked);
        }

        private void clockTimer_Tick(object sender, EventArgs e) { UpdateClock(); }

        private void MainForm_Shown(object sender, EventArgs e) { BeginCheck(false); }

        private void BeginCheck(bool writeTime)
        {
            if (_busy) return;
            _busy = true;
            btnSync.Enabled = false;
            btnAddSrv.Enabled = false;
            btnDelSrv.Enabled = false;
            cboServer.Enabled = false;
            chkOnlyServers.Enabled = false;
            chkBoot.Enabled = false;
            chkTimer.Enabled = false;
            cboHours.Enabled = false;
            lblResult.Text = "状态：正在连接时间服务器…";
            lblResult.ForeColor = Color.FromArgb(160, 110, 0);

            string preferred = cboServer.Text.Trim();
            string servers = BuildServerList(chkOnlyServers.Checked);
            Thread t = new Thread(delegate()
            {
                Core.SyncResult r = Core.DoSync(servers, writeTime, preferred);
                Core.Log(r.Message.Replace("\r\n", " | "));
                try { BeginInvoke(new Action(delegate() { ShowResult(r); })); }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void ShowResult(Core.SyncResult r)
        {
            _busy = false;
            btnSync.Enabled = true;
            btnAddSrv.Enabled = true;
            btnDelSrv.Enabled = true;
            cboServer.Enabled = true;
            chkOnlyServers.Enabled = true;
            chkBoot.Enabled = true;
            chkTimer.Enabled = true;
            cboHours.Enabled = true;

            lblResult.Text = "状态：" + r.Message.Replace("\r\n", "  ");
            if (!r.Ok) lblResult.ForeColor = Color.FromArgb(190, 30, 30);
            else if (r.Wrote) lblResult.ForeColor = Color.FromArgb(0, 140, 60);
            else lblResult.ForeColor = Color.FromArgb(90, 90, 90);
            UpdateClock();
        }

        private void btnSync_Click(object sender, EventArgs e) { BeginCheck(true); }

        private void btnClose_Click(object sender, EventArgs e) { Close(); }
    }
}
