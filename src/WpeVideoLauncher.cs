// WpeVideoLauncher - Wallpaper Engine 当前壁纸视频 -> 外部播放器(PotPlayer) 一键播放
// 零修改游戏文件。托盘常驻 + 全局热键。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("Wallpaper Engine 视频播放助手")]
[assembly: System.Reflection.AssemblyProduct("WpeVideoLauncher")]
[assembly: System.Reflection.AssemblyDescription("一键用 PotPlayer 播放当前 Wallpaper Engine 壁纸的视频")]
[assembly: System.Reflection.AssemblyCompany("")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("1.0.0")]

namespace WpeVideoLauncher
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool once = false;
            bool diag = false;
            foreach (string a in args)
            {
                if (a.Equals("--once", StringComparison.OrdinalIgnoreCase)) once = true;
                if (a.Equals("--diagnose", StringComparison.OrdinalIgnoreCase)) diag = true;
            }

            if (diag)
            {
                Environment.ExitCode = Core.Diagnose();
                return;
            }

            if (once)
            {
                // 命令行模式：直接播放并退出（方便做快捷方式/计划任务），不弹托盘
                int code = Core.PlayCurrent(null);
                Environment.ExitCode = code;
                return;
            }

            bool created;
            using (var mutex = new System.Threading.Mutex(true, "WpeVideoLauncher_SingleInstance", out created))
            {
                if (!created)
                {
                    // 已经在跑了：戳一下老实例，让它弹个气泡并把它自己的窗口带到前面
                    try
                    {
                        using (var ev = System.Threading.EventWaitHandle.OpenExisting(NotifyEventName))
                            ev.Set();
                    }
                    catch { }
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayContext());
            }
        }

        public const string NotifyEventName = @"Local\WpeVideoLauncher_Notify";
    }

    internal sealed class Config
    {
        public string ConfigFile;          // Wallpaper Engine config.json 路径（空 = 自动探测）
        public string Player = @"D:\program files\PotPlayer\PotPlayerMini64.exe";
        public string Hotkey = "Ctrl+Alt+P";
        public string Monitor = "Monitor0";
        public string ExtraArgs = "";      // 追加给播放器的参数，例如 /new
        public bool ShowBalloon = true;
        public string[] VideoExtensions = { ".mp4", ".webm", ".mkv", ".avi", ".mov", ".m4v", ".wmv", ".flv", ".mpg", ".mpeg", ".ts" };

        public static string ExeDir
        {
            get
            {
                string p = System.Reflection.Assembly.GetExecutingAssembly().Location;
                return string.IsNullOrEmpty(p) ? Environment.CurrentDirectory : Path.GetDirectoryName(p);
            }
        }

        public static string ConfigPath { get { return Path.Combine(ExeDir, "config.json"); } }

        /// 用户级配置(可选)：把 config.json 放在这里也能生效，exe 放桌面时可保持单文件
        public static string UserConfigPath
        {
            get { return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WpeVideoLauncher"), "config.json"); }
        }

        /// 内置默认值：即使没有任何 config.json 也能直接双击运行
        public const string BuiltInDefaults =
            "{\n" +
            "  \"configFile\": \"\",\n" +
            "  \"player\": \"D:/program files/PotPlayer/PotPlayerMini64.exe\",\n" +
            "  \"hotkey\": \"Ctrl+Alt+P\",\n" +
            "  \"monitor\": \"Monitor0\",\n" +
            "  \"extraArgs\": \"\",\n" +
            "  \"showBalloon\": true,\n" +
            "  \"videoExtensions\": \".mp4,.webm,.mkv,.avi,.mov,.m4v,.wmv,.flv,.mpg,.mpeg,.ts\"\n" +
            "}\n";

        /// 写入一份可编辑的配置模板
        public static string WriteTemplate(string path)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, BuiltInDefaults, new UTF8Encoding(false));
                return path;
            }
            catch (Exception ex) { Log.Warn("写模板失败 " + path + " : " + ex.Message); return null; }
        }

        public static Config Load()
        {
            var c = new Config();

            // 1) 内置默认
            string txt = BuiltInDefaults;

            // 2) exe 同目录的 config.json（若存在则覆盖默认值）
            string userTxt;
            if (TryReadAllText(ConfigPath, out userTxt)) txt = userTxt;
            else if (TryReadAllText(UserConfigPath, out userTxt)) txt = userTxt;

            c.ConfigFile = Str(txt, "configFile", c.ConfigFile);
            c.Player = Str(txt, "player", c.Player);
            c.Hotkey = Str(txt, "hotkey", c.Hotkey);
            c.Monitor = Str(txt, "monitor", c.Monitor);
            c.ExtraArgs = Str(txt, "extraArgs", c.ExtraArgs);
            string ext = Str(txt, "videoExtensions", null);
            if (!string.IsNullOrEmpty(ext))
            {
                var list = new List<string>();
                foreach (string s in ext.Split(','))
                {
                    string t = s.Trim().ToLowerInvariant();
                    if (t.Length == 0) continue;
                    if (!t.StartsWith(".")) t = "." + t;
                    list.Add(t);
                }
                if (list.Count > 0) c.VideoExtensions = list.ToArray();
            }
            string balloon = Str(txt, "showBalloon", null);
            if (balloon != null) c.ShowBalloon = !balloon.Equals("false", StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(c.ConfigFile)) c.ConfigFile = Core.FindWeConfig();
            if (string.IsNullOrEmpty(c.Player)) c.Player = null;
            return c;
        }

        // 极简 JSON 取值：只认 "key" : "value"（值必须是字符串），避免引入 JSON 依赖与转义坑
        public static string Str(string json, string key, string dflt)
        {
            Match m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.IgnoreCase);
            if (!m.Success) return dflt;
            return Unescape(m.Groups[1].Value);
        }

        public static string Unescape(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    switch (n)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (i + 4 < s.Length)
                            {
                                int cp;
                                if (int.TryParse(s.Substring(i + 1, 4), System.Globalization.NumberStyles.HexNumber, null, out cp))
                                {
                                    sb.Append((char)cp); i += 4;
                                }
                            }
                            break;
                        default: sb.Append(n); break;
                    }
                }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }

        public static bool TryReadAllText(string path, out string text)
        {
            text = null;
            try
            {
                if (!File.Exists(path)) return false;
                // WE 的 config.json 是无 BOM 的 UTF-8，其中含中文壁纸名
                var enc = new UTF8Encoding(false, false);
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs, enc, true))
                    text = sr.ReadToEnd();
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("读取失败 " + path + " : " + ex.Message);
                return false;
            }
        }
    }

    internal static class Log
    {
        private static readonly object Gate = new object();
        private static string _file;

        /// 优先写在 exe 旁边；若该目录不可写(比如放在受保护目录)，自动退到 %LOCALAPPDATA%
        public static string File
        {
            get
            {
                if (_file != null) return _file;
                string beside = Path.Combine(Config.ExeDir, "launcher.log");
                if (CanWrite(Config.ExeDir)) { _file = beside; return _file; }
                try
                {
                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WpeVideoLauncher");
                    Directory.CreateDirectory(dir);
                    if (CanWrite(dir)) { _file = Path.Combine(dir, "launcher.log"); return _file; }
                }
                catch { }
                _file = Path.Combine(Path.GetTempPath(), "WpeVideoLauncher.log");
                return _file;
            }
        }

        private static bool CanWrite(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
                string probe = Path.Combine(dir, ".wpe-write-test.tmp");
                using (var fs = new FileStream(probe, FileMode.Create, FileAccess.Write, FileShare.Read, 8, FileOptions.DeleteOnClose)) { }
                return true;
            }
            catch { return false; }
        }

        public static void Write(string level, string msg)
        {
            try
            {
                lock (Gate)
                {
                    string path = File;
                    var fi = new FileInfo(path);
                    if (fi.Exists && fi.Length > 512 * 1024) fi.Delete(); // 简单轮转
                    System.IO.File.AppendAllText(path,
                        string.Format("{0:yyyy-MM-dd HH:mm:ss} [{1}] {2}{3}", DateTime.Now, level, msg, Environment.NewLine),
                        new UTF8Encoding(true));
                }
            }
            catch { }
        }

        public static void Info(string m) { Write("INFO", m); }
        public static void Warn(string m) { Write("WARN", m); }
        public static void Error(string m) { Write("ERROR", m); }
    }

    internal sealed class WallpaperEntry
    {
        public string File;
        public string Title;
    }

    internal static class Core
    {
        private static readonly string[] WeRoots = new string[]
        {
            @"D:\program files\steam\steamapps\common\wallpaper_engine",
            @"C:\program files\steam\steamapps\common\wallpaper_engine",
            @"D:\program files (x86)\steam\steamapps\common\wallpaper_engine",
            @"C:\program files (x86)\steam\steamapps\common\wallpaper_engine",
            @"D:\steam\steamapps\common\wallpaper_engine",
            @"C:\steam\steamapps\common\wallpaper_engine",
            @"D:\steamapps\common\wallpaper_engine",
            @"C:\steamapps\common\wallpaper_engine",
        };

        public static string FindWeConfig()
        {
            // 1) 正在运行的 wallpaper64.exe 所在目录
            try
            {
                foreach (Process p in Process.GetProcessesByName("wallpaper64"))
                {
                    try
                    {
                        string dir = Path.GetDirectoryName(p.MainModule.FileName);
                        if (!string.IsNullOrEmpty(dir))
                        {
                            string c = Path.Combine(dir, "config.json");
                            if (File.Exists(c)) return c;
                        }
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            catch { }

            // 2) Steam 安装路径
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                    if (k != null)
                    {
                        object v = k.GetValue("SteamPath");
                        if (v != null)
                        {
                            string steam = v.ToString().Replace('/', '\\');
                            string c = Path.Combine(steam, @"steamapps\common\wallpaper_engine\config.json");
                            if (File.Exists(c)) return c;
                        }
                    }
            }
            catch { }

            // 3) 常见路径
            foreach (string root in WeRoots)
            {
                string c = Path.Combine(root, "config.json");
                if (File.Exists(c)) return c;
            }
            return null;
        }

        /// 当前正在使用的壁纸（按显示器）
        public static WallpaperEntry GetCurrent(string configPath, string monitor, out string reason)
        {
            reason = null;
            string txt;
            if (!Config.TryReadAllText(configPath, out txt)) { reason = "读不到 Wallpaper Engine 配置: " + configPath; return null; }

            string scope = txt;
            int wc = txt.IndexOf("\"wallpaperconfig\"", StringComparison.Ordinal);
            if (wc >= 0) scope = txt.Substring(wc);

            string file = ExtractMonitor(scope, monitor);
            if (file == null) file = ExtractFirstMonitor(scope);
            if (file == null) { reason = "配置里没有 selectedwallpapers 记录"; return null; }

            file = file.Replace('/', '\\');
            if (!File.Exists(file)) { reason = "壁纸文件不存在: " + file; return null; }
            return new WallpaperEntry { File = file };
        }

        private static string ExtractMonitor(string scope, string monitor)
        {
            if (string.IsNullOrEmpty(monitor)) return null;
            Match m = Regex.Match(scope,
                "\"" + Regex.Escape(monitor) + "\"\\s*:\\s*\\{[^{}]*?\"file\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"",
                RegexOptions.Singleline);
            return m.Success ? Config.Unescape(m.Groups[1].Value) : null;
        }

        private static string ExtractFirstMonitor(string scope)
        {
            Match m = Regex.Match(scope,
                "\"Monitor\\d+\"\\s*:\\s*\\{[^{}]*?\"file\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"",
                RegexOptions.Singleline);
            return m.Success ? Config.Unescape(m.Groups[1].Value) : null;
        }

        /// 历史记录（最近使用的壁纸），用于兜底
        public static List<WallpaperEntry> GetRecent(string configPath, int max)
        {
            var list = new List<WallpaperEntry>();
            string txt;
            if (!Config.TryReadAllText(configPath, out txt)) return list;

            int rc = txt.IndexOf("\"wallpaperconfigrecent\"", StringComparison.Ordinal);
            if (rc < 0) return list;
            string scope = txt.Substring(rc);

            // 每条记录形如: { "config": { ... "Monitor0": { "file": "..." } }, "title": "..." }
            foreach (Match m in Regex.Matches(scope,
                "\"file\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"(?:(?!\"file\").)*?\"title\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"",
                RegexOptions.Singleline))
            {
                string f = Config.Unescape(m.Groups[1].Value).Replace('/', '\\');
                string t = Config.Unescape(m.Groups[2].Value);
                if (!File.Exists(f)) continue;
                list.Add(new WallpaperEntry { File = f, Title = t });
                if (list.Count >= max) break;
            }
            return list;
        }

        /// 同目录下的其它视频（例如壁纸 mp4 旁边还有别的版本）
        public static List<string> GetSiblings(string videoFile, string[] extensions)
        {
            var list = new List<string>();
            try
            {
                string dir = Path.GetDirectoryName(videoFile);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return list;
                foreach (string f in Directory.GetFiles(dir))
                {
                    string e = Path.GetExtension(f).ToLowerInvariant();
                    foreach (string x in extensions)
                        if (e == x) { list.Add(f); break; }
                }
            }
            catch { }
            return list;
        }

        public static bool IsVideo(string path, string[] extensions)
        {
            string e = Path.GetExtension(path).ToLowerInvariant();
            foreach (string x in extensions) if (e == x) return true;
            return false;
        }

        /// 播放器自动发现：配置 -> 注册表/常见路径 -> .mp4 默认关联
        public static string ResolvePlayer(Config cfg)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(cfg.Player)) candidates.Add(cfg.Player);
            try
            {
                foreach (string key in new string[] { @"SOFTWARE\DAUM\PotPlayerMini64", @"SOFTWARE\DAUM\PotPlayerMini" })
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(key))
                        if (k != null)
                        {
                            object v = k.GetValue("ProgramPath") ?? k.GetValue("InstallPath") ?? k.GetValue("Path");
                            if (v != null) candidates.Add(v.ToString());
                        }
                }
            }
            catch { }
            candidates.Add(@"D:\program files\PotPlayer\PotPlayerMini64.exe");
            candidates.Add(@"C:\Program Files\DAUM\PotPlayer\PotPlayerMini64.exe");
            candidates.Add(@"C:\Program Files (x86)\DAUM\PotPlayer\PotPlayerMini.exe");
            candidates.Add(@"D:\Program Files\DAUM\PotPlayer\PotPlayerMini64.exe");

            foreach (string c in candidates)
            {
                if (string.IsNullOrEmpty(c)) continue;
                string p = c.Trim().Trim('"');
                if (File.Exists(p) && p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return p;
            }

            // 最后退回 .mp4 默认关联
            try
            {
                string progId = null;
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.mp4\UserChoice"))
                    if (k != null) progId = k.GetValue("ProgId") as string;
                if (progId != null)
                {
                    using (RegistryKey k = Registry.ClassesRoot.OpenSubKey(progId + @"\shell\open\command"))
                        if (k != null)
                        {
                            string cmd = k.GetValue("") as string;
                            Match m = Regex.Match(cmd ?? "", "^\"([^\"]+)\"");
                            if (m.Success && File.Exists(m.Groups[1].Value)) return m.Groups[1].Value;
                        }
                }
            }
            catch { }
            return null;
        }

        public static void Play(string player, string videoFile, string extraArgs)
        {
            var psi = new ProcessStartInfo();
            psi.FileName = player;
            psi.Arguments = "\"" + videoFile + "\"" + (string.IsNullOrEmpty(extraArgs) ? "" : " " + extraArgs);
            psi.UseShellExecute = false;
            psi.WorkingDirectory = Path.GetDirectoryName(player);
            Process.Start(psi);
            Log.Info("已启动: " + player + " \"" + videoFile + "\"");
        }

        public static void RevealInExplorer(string file)
        {
            try { Process.Start("explorer.exe", "/select,\"" + file + "\""); }
            catch (Exception ex) { Log.Error("explorer /select 失败: " + ex.Message); }
        }

        /// 自检：把关键信息写入 launcher.log，排查用
        public static int Diagnose()
        {
            var sb = new StringBuilder();
            sb.AppendLine("---------- 自检 " + DateTime.Now + " ----------");
            sb.AppendLine("exe 目录        : " + Config.ExeDir);
            var cfg = Config.Load();
            if (string.IsNullOrEmpty(cfg.ConfigFile)) cfg.ConfigFile = FindWeConfig();
            sb.AppendLine("config.json     : " + (cfg.ConfigFile ?? "(未找到)"));
            sb.AppendLine("配置文件存在    : " + (cfg.ConfigFile != null && File.Exists(cfg.ConfigFile)));
            sb.AppendLine("配置的播放器    : " + cfg.Player);
            string player = ResolvePlayer(cfg);
            sb.AppendLine("最终播放器      : " + (player ?? "(未找到)"));
            sb.AppendLine("播放器存在      : " + (player != null && File.Exists(player)));
            sb.AppendLine("热键            : " + cfg.Hotkey + " -> packed=0x" + TrayApp.ParseHotkey(cfg.Hotkey).ToString("X"));
            sb.AppendLine("显示器          : " + cfg.Monitor);
            sb.AppendLine("支持的扩展名    : " + string.Join(",", cfg.VideoExtensions));

            string reason = null;
            WallpaperEntry cur = null;
            if (cfg.ConfigFile != null) cur = GetCurrent(cfg.ConfigFile, cfg.Monitor, out reason);
            else reason = "未找到 config.json";
            if (cur == null) sb.AppendLine("当前壁纸        : (取不到) " + reason);
            else
            {
                sb.AppendLine("当前壁纸文件    : " + cur.File);
                sb.AppendLine("是视频          : " + IsVideo(cur.File, cfg.VideoExtensions));
            }
            foreach (Promo p in GetCurrentCandidates(cfg.ConfigFile))
                sb.AppendLine("  候选 " + p.Monitor + " -> " + p.File);
            foreach (string s in GetSiblings(cur == null ? null : cur.File, cfg.VideoExtensions))
                sb.AppendLine("  同目录视频: " + s);

            string text = sb.ToString();
            Log.Info(Environment.NewLine + text);
            Console.Error.Write(text);
            return 0;
        }

        public sealed class Promo { public string Monitor; public string File; }

        /// 配置里所有显示器的当前壁纸（自检/多屏用）
        public static List<Promo> GetCurrentCandidates(string configPath)
        {
            var list = new List<Promo>();
            if (string.IsNullOrEmpty(configPath)) return list;
            string txt;
            if (!Config.TryReadAllText(configPath, out txt)) return list;
            int wc = txt.IndexOf("\"wallpaperconfig\"", StringComparison.Ordinal);
            if (wc < 0) return list;
            string scope = txt.Substring(wc);
            // 裁到 selectedwallpapers 这一层, 避免把历史记录(wallpaperconfigrecent)也算进来
            int sel = scope.IndexOf("\"selectedwallpapers\"", StringComparison.Ordinal);
            if (sel >= 0)
            {
                scope = scope.Substring(sel);
                int depth = 0, end = scope.Length;
                for (int i = 0; i < scope.Length; i++)
                {
                    if (scope[i] == '{') depth++;
                    else if (scope[i] == '}')
                    {
                        depth--;
                        if (depth <= 1) { end = i + 1; break; }
                    }
                }
                scope = scope.Substring(0, end);
            }
            foreach (Match m in Regex.Matches(scope,
                "\"(Monitor\\d+)\"\\s*:\\s*\\{[^{}]*?\"file\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"",
                RegexOptions.Singleline))
                list.Add(new Promo { Monitor = m.Groups[1].Value, File = Config.Unescape(m.Groups[2].Value).Replace('/', '\\') });
            return list;
        }

        /// 热键/托盘共同入口
        public static int PlayCurrent(Config cfg)
        {
            if (cfg == null)
            {
                cfg = Config.Load();
                if (string.IsNullOrEmpty(cfg.ConfigFile)) cfg.ConfigFile = FindWeConfig();
            }
            string reason;
            WallpaperEntry cur = GetCurrent(cfg.ConfigFile, cfg.Monitor, out reason);
            if (cur == null)
            {
                Log.Warn("取当前壁纸失败: " + reason);
                return 2;
            }
            if (!IsVideo(cur.File, cfg.VideoExtensions))
            {
                Log.Warn("当前壁纸不是视频壁纸: " + cur.File);
                RevealInExplorer(cur.File);
                return 3;
            }
            string player = ResolvePlayer(cfg);
            if (player == null)
            {
                Log.Error("找不到播放器，请在 config.json 里设置 player");
                return 4;
            }
            try { Play(player, cur.File, cfg.ExtraArgs); return 0; }
            catch (Exception ex) { Log.Error("启动播放器失败: " + ex.Message); return 5; }
        }
    }

    internal sealed class TrayContext : ApplicationContext
    {
        private readonly TrayApp _app;

        public TrayContext()
        {
            _app = new TrayApp();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _app != null) _app.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed class TrayApp : IDisposable
    {
        private readonly NotifyIcon _icon;
        private readonly Config _cfg;
        private HotkeyWindow _hotkey;
        private WallpaperEntry _current;
        private Icon _ownIcon;

        public TrayApp()
        {
            _cfg = Config.Load();
            if (string.IsNullOrEmpty(_cfg.ConfigFile)) _cfg.ConfigFile = Core.FindWeConfig();
            Log.Info("启动: config=" + _cfg.ConfigFile + " player=" + _cfg.Player + " hotkey=" + _cfg.Hotkey);

            _ownIcon = BuildIcon();
            var menu = new ContextMenuStrip();
            menu.Items.Add("▶  播放当前壁纸视频", null, delegate { PlayCurrent(); });
            menu.Items.Add(new ToolStripSeparator());

            var recent = new ToolStripMenuItem("最近使用过的壁纸");
            menu.Items.Add(recent);
            this.RecentMenu = recent;
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("打开配置文件", null, delegate { OpenConfigFile(); });
            menu.Items.Add("打开配置文件所在的文件夹", null, delegate { OpenPath(Config.ExeDir); });
            menu.Items.Add("打开日志", null, delegate { OpenPath(Log.File); });
            menu.Items.Add("重新加载配置", null, delegate { Reload(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { ExitApp(); });

            _icon = new NotifyIcon();
            _icon.Icon = _ownIcon;
            _icon.Text = "Wallpaper Engine 视频播放器助手";
            _icon.ContextMenuStrip = menu;
            _icon.Visible = true;
            _icon.DoubleClick += delegate { PlayCurrent(); };
            _icon.BalloonTipTitle = "Wallpaper Engine 视频播放助手";
            _icon.BalloonTipIcon = ToolTipIcon.Info;

            if (IsHotkeyDisabled(_cfg.Hotkey))
            {
                Log.Info("热键已按配置禁用");
            }
            else
            {
                _hotkey = new HotkeyWindow(ParseHotkey(_cfg.Hotkey), delegate { PlayCurrent(); });
                if (_hotkey.Id == 0)
                    Balloon("热键注册失败", "「" + _cfg.Hotkey + "」被其它程序占用，可在托盘菜单或 config.json 里改键。");
                else
                    Balloon("已就绪", "按 " + _cfg.Hotkey + " 用播放器打开当前壁纸的视频。");
            }

            RefreshTooltip();
            Application.ApplicationExit += delegate { Dispose(); };
            StartNotifyListener();
        }

        public ToolStripMenuItem RecentMenu { get; private set; }

        private System.Threading.Thread _notifyThread;
        private System.Threading.EventWaitHandle _notifyEvent;
        private readonly System.Threading.SynchronizationContext _uiContext = System.Threading.SynchronizationContext.Current;

        /// 监听「又双击了一次 exe」的通知，给用户一个可见反馈
        private void StartNotifyListener()
        {
            try
            {
                _notifyEvent = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, Program.NotifyEventName);
                _notifyThread = new System.Threading.Thread(delegate ()
                {
                    var handles = new System.Threading.WaitHandle[] { _notifyEvent };
                    while (true)
                    {
                        try
                        {
                            if (System.Threading.WaitHandle.WaitAny(handles, 500) != 0) continue;
                            System.Threading.SynchronizationContext ctx = _uiContext;
                            if (ctx == null) continue;
                            ctx.Post(delegate
                            {
                                Balloon("程序已经在运行", "看托盘这个图标就是它；双击图标 = 播放当前壁纸视频。");
                            }, null);
                        }
                        catch (ObjectDisposedException) { return; }
                        catch { return; }
                    }
                });
                _notifyThread.IsBackground = true;
                _notifyThread.Name = "WpeNotify";
                _notifyThread.Start();
            }
            catch (Exception ex) { Log.Warn("通知监听未启动: " + ex.Message); }
        }

        private void ExitApp()
        {
            try { Dispose(); } catch { }
            Application.ExitThread();
            Application.Exit();
        }

        private Icon BuildIcon()
        {
            try
            {
                using (var bmp = new Bitmap(32, 32))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        using (var bg = new SolidBrush(Color.FromArgb(32, 40, 60)))
                            g.FillEllipse(bg, 1, 1, 30, 30);
                        using (var fg = new SolidBrush(Color.FromArgb(240, 200, 60)))
                        {
                            Point[] tri = { new Point(12, 9), new Point(24, 16), new Point(12, 23) };
                            g.FillPolygon(fg, tri);
                        }
                    }
                    IntPtr h = bmp.GetHicon();
                    Icon tmp = Icon.FromHandle(h);
                    var clone = (Icon)tmp.Clone();
                    Native.DestroyIcon(h);
                    return clone;
                }
            }
            catch { return SystemIcons.Application; }
        }

        private void Balloon(string title, string text)
        {
            if (!_cfg.ShowBalloon || _icon == null) return;
            try
            {
                _icon.BalloonTipTitle = title;
                _icon.BalloonTipText = text;
                _icon.ShowBalloonTip(4000);
            }
            catch { }
        }

        private void RefreshTooltip()
        {
            string reason = null;
            _current = null;
            if (!string.IsNullOrEmpty(_cfg.ConfigFile))
                _current = Core.GetCurrent(_cfg.ConfigFile, _cfg.Monitor, out reason);
            if (_current != null)
            {
                string name = Path.GetFileName(_current.File);
                if (name.Length > 60) name = name.Substring(0, 57) + "...";
                try { _icon.Text = "WE 视频助手 - " + (name.Length > 40 ? name.Substring(0, 40) : name); } catch { }
            }
            else
            {
                _icon.Text = "WE 视频助手";
            }
            if (!string.IsNullOrEmpty(_cfg.ConfigFile) && !File.Exists(_cfg.ConfigFile))
                Log.Warn("配置文件不存在: " + _cfg.ConfigFile);
            if (RecentMenu != null)
            {
                RecentMenu.DropDownItems.Clear();
                List<WallpaperEntry> recent = Core.GetRecent(_cfg.ConfigFile, 12);
                if (_current != null && Core.IsVideo(_current.File, _cfg.VideoExtensions))
                {
                    foreach (string sib in Core.GetSiblings(_current.File, _cfg.VideoExtensions))
                    {
                        string f = sib;
                        RecentMenu.DropDownItems.Add("同目录: " + Path.GetFileName(f), null, delegate { PlayFile(f); });
                    }
                    if (RecentMenu.DropDownItems.Count > 0) RecentMenu.DropDownItems.Add(new ToolStripSeparator());
                }
                int added = 0;
                foreach (WallpaperEntry e in recent)
                {
                    if (!Core.IsVideo(e.File, _cfg.VideoExtensions)) continue;
                    if (_current != null && string.Equals(e.File, _current.File, StringComparison.OrdinalIgnoreCase)) continue;
                    string label = string.IsNullOrEmpty(e.Title) ? Path.GetFileName(e.File) : e.Title;
                    if (label.Length > 55) label = label.Substring(0, 52) + "...";
                    string f = e.File;
                    RecentMenu.DropDownItems.Add(label, null, delegate { PlayFile(f); });
                    if (++added >= 10) break;
                }
                if (RecentMenu.DropDownItems.Count == 0)
                    RecentMenu.DropDownItems.Add(new ToolStripMenuItem("（无）") { Enabled = false });
            }
        }

        private void PlayCurrent()
        {
            string reason = null;
            WallpaperEntry cur = null;
            if (!string.IsNullOrEmpty(_cfg.ConfigFile))
                cur = Core.GetCurrent(_cfg.ConfigFile, _cfg.Monitor, out reason);
            else
                reason = "没有找到 Wallpaper Engine 的 config.json";
            if (cur == null)
            {
                Balloon("没取到当前壁纸", reason ?? "未知原因，详见 launcher.log");
                Log.Warn("PlayCurrent: " + reason);
                RefreshTooltip();
                return;
            }
            if (!Core.IsVideo(cur.File, _cfg.VideoExtensions))
            {
                Balloon("当前壁纸不是视频", Path.GetFileName(cur.File) + "\n已在资源管理器中定位该文件。");
                Core.RevealInExplorer(cur.File);
                return;
            }
            PlayFile(cur.File);
        }

        private void PlayFile(string file)
        {
            string player = Core.ResolvePlayer(_cfg);
            if (player == null)
            {
                Balloon("找不到播放器", "请在 config.json 里填写 player 字段后重新加载配置。");
                return;
            }
            try { Core.Play(player, file, _cfg.ExtraArgs); }
            catch (Exception ex)
            {
                Log.Error("启动播放器失败: " + ex.Message);
                Balloon("启动播放器失败", ex.Message);
            }
        }

        private void Reload()
        {
            try
            {
                Config fresh = Config.Load();
                _cfg.ConfigFile = fresh.ConfigFile;
                _cfg.Player = fresh.Player;
                _cfg.Monitor = fresh.Monitor;
                _cfg.ExtraArgs = fresh.ExtraArgs;
                _cfg.VideoExtensions = fresh.VideoExtensions;
                if (!string.Equals(fresh.Hotkey, _cfg.Hotkey, StringComparison.OrdinalIgnoreCase))
                {
                    _cfg.Hotkey = fresh.Hotkey;
                    if (_hotkey != null) { _hotkey.Dispose(); _hotkey = null; }
                    if (!IsHotkeyDisabled(_cfg.Hotkey))
                        _hotkey = new HotkeyWindow(ParseHotkey(_cfg.Hotkey), delegate { PlayCurrent(); });
                }
                RefreshTooltip();
                Balloon("配置已重新加载", "热键 " + _cfg.Hotkey);
            }
            catch (Exception ex) { Log.Error("重载配置失败: " + ex.Message); }
        }

        /// 打开(必要时先生成)可编辑的 config.json
        private void OpenConfigFile()
        {
            string path = Config.ConfigPath;
            if (!File.Exists(path))
            {
                string written = Config.WriteTemplate(path);
                if (written == null) written = Config.WriteTemplate(Config.UserConfigPath); // exe 目录不可写时退到用户目录
                if (written == null)
                {
                    Balloon("无法创建配置文件", "exe 所在目录和用户目录都不可写，详见 launcher.log");
                    return;
                }
                path = written;
            }
            OpenPath(path);
        }

        private static void OpenPath(string p)
        {
            try
            {
                if (string.IsNullOrEmpty(p)) return;
                if (File.Exists(p) || Directory.Exists(p)) Process.Start("explorer.exe", "\"" + p + "\"");
                else Process.Start("explorer.exe", "\"" + Path.GetDirectoryName(p) + "\"");
            }
            catch (Exception ex) { Log.Warn("打开路径失败: " + ex.Message); }
        }

        public static bool IsHotkeyDisabled(string s)
        {
            if (string.IsNullOrEmpty(s)) return true;
            string t = s.Trim().ToLowerInvariant();
            return t == "none" || t == "off" || t == "disabled" || t == "无";
        }

        public static long ParseHotkey(string s)
        {
            long mods = 0, key = 0;
            if (string.IsNullOrEmpty(s)) s = "Ctrl+Alt+P";
            foreach (string partRaw in s.Split('+'))
            {
                string part = partRaw.Trim();
                if (part.Length == 0) continue;
                switch (part.ToLowerInvariant())
                {
                    case "ctrl": case "control": mods |= Native.MOD_CONTROL; break;
                    case "alt": mods |= Native.MOD_ALT; break;
                    case "shift": mods |= Native.MOD_SHIFT; break;
                    case "win": mods |= Native.MOD_WIN; break;
                    default:
                        try { key = (long)Enum.Parse(typeof(Keys), part, true) & 0xFF; }
                        catch
                        {
                            if (part.Length == 1) key = (long)char.ToUpperInvariant(part[0]);
                        }
                        break;
                }
            }
            if (key == 0) key = (long)Keys.P;
            return (key << 16) | mods;
        }

        public void Dispose()
        {
            try { if (_notifyEvent != null) { _notifyEvent.Set(); _notifyEvent.Close(); _notifyEvent = null; } } catch { }
            if (_hotkey != null) { _hotkey.Dispose(); _hotkey = null; }
            if (_icon != null) { _icon.Visible = false; _icon.Dispose(); }
            if (_ownIcon != null) { _ownIcon.Dispose(); _ownIcon = null; }
        }
    }

    internal static class Native
    {
        public const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004, MOD_WIN = 0x0008;
        public const int WM_HOTKEY = 0x0312;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);
    }

    /// 隐藏窗口，只用来接收 WM_HOTKEY
    internal sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private const int HOTKEY_ID = 0xB1;
        private readonly Action _onHotkey;
        public int Id { get; private set; }

        public HotkeyWindow(long packed, Action onHotkey)
        {
            _onHotkey = onHotkey;
            var cp = new CreateParams();
            cp.Caption = "WpeVideoLauncherHotkey";
            cp.X = -32000;
            cp.Y = -32000;
            cp.Width = 1;
            cp.Height = 1;
            cp.Style = 0x800000; // WS_POPUP，不显示、不占任务栏
            CreateHandle(cp);
            uint mods = (uint)(packed & 0xFFFF);
            uint key = (uint)((packed >> 16) & 0xFF);
            if (Native.RegisterHotKey(Handle, HOTKEY_ID, mods, key)) { Id = HOTKEY_ID; Log.Info("热键已注册 mods=" + mods + " vk=" + key); }
            else Log.Warn("RegisterHotKey 失败, err=" + Marshal.GetLastWin32Error() + " (mods=" + mods + ", key=" + key + ")");
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
            {
                Log.Info("收到热键，开始播放");
                if (_onHotkey != null)
                {
                    try { _onHotkey(); }
                    catch (Exception ex) { Log.Error("热键处理异常: " + ex); }
                }
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (Id != 0) { Native.UnregisterHotKey(Handle, HOTKEY_ID); Id = 0; }
            if (Handle != IntPtr.Zero) DestroyHandle();
        }
    }
}
