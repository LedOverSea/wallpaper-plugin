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
            bool scan = false;
            bool scanAll = false;
            bool picker = false;
            foreach (string a in args)
            {
                if (a.Equals("--once", StringComparison.OrdinalIgnoreCase)) once = true;
                if (a.Equals("--diagnose", StringComparison.OrdinalIgnoreCase)) diag = true;
                if (a.Equals("--scan", StringComparison.OrdinalIgnoreCase)) scan = true;
                if (a.Equals("--scan-all", StringComparison.OrdinalIgnoreCase)) { scan = true; scanAll = true; }
                if (a.Equals("--picker", StringComparison.OrdinalIgnoreCase)) picker = true;
            }

            if (diag)
            {
                Environment.ExitCode = Core.Diagnose();
                return;
            }

            if (scan)
            {
                // 命令行扫描：--scan 只补新的，--scan-all 把库里现有壁纸全部补一遍
                Config sc = Config.Load();
                if (string.IsNullOrEmpty(sc.ConfigFile)) sc.ConfigFile = Core.FindWeConfig();
                AutoPlaylist.ScanResult r = AutoPlaylist.Scan(sc, scanAll);
                string plFile = Playlist.FilePath(sc);
                Console.Error.WriteLine("创意工坊目录: " + (r.WorkshopDir == null ? "(未找到)" : r.WorkshopDir));
                Console.Error.WriteLine("播放列表    : " + plFile + " (" + Playlist.Count(plFile) + " 项)");
                Console.Error.WriteLine((scanAll ? "全量扫描    : " : (r.FirstRun ? "首次扫描    : " : "增量扫描    : ")) + r.Detail);
                Environment.ExitCode = 0;
                return;
            }

            if (picker)
            {
                // 单独打开「批量选择壁纸」窗口（可以做个快捷方式指过来）
                Config pc = Config.Load();
                if (string.IsNullOrEmpty(pc.ConfigFile)) pc.ConfigFile = Core.FindWeConfig();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                string wsPick = Core.FindWorkshopDir(pc);
                if (string.IsNullOrEmpty(wsPick))
                {
                    MessageBox.Show("找不到 Wallpaper Engine 创意工坊目录。\n可以在 config.json 里用 workshopDir 手动指定。",
                        "WpeVideoLauncher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    Environment.ExitCode = 6;
                    return;
                }
                List<WallpaperInfo> allPick = Core.GetWallpapers(wsPick, pc.VideoExtensions);
                Log.Info("批量选择: 工坊=" + wsPick + " 壁纸=" + allPick.Count + " 播放列表=" + Playlist.FilePath(pc));
                using (var f = new PlaylistForm(pc, allPick))
                {
                    Log.Info("窗口 ClientSize=" + f.ClientSize.Width + "x" + f.ClientSize.Height);
                    f.ShowDialog();
                }
                Environment.ExitCode = 0;
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
        public string LogFile = "";        // launcher.log 路径；留空 = 自动(项目目录 > exe 旁边 > %LOCALAPPDATA%)
        public string PlaylistFile = "";   // 播放列表(m3u8)路径；留空 = 跟 launcher.log 同目录
        public string WorkshopDir = "";    // Wallpaper Engine 创意工坊目录；留空 = 自动探测
        public bool AutoAddDownloads = true; // 新下载的壁纸自动进播放列表
        public string[] VideoExtensions = { ".mp4", ".webm", ".mkv", ".avi", ".mov", ".m4v", ".wmv", ".flv", ".mpg", ".mpeg", ".ts" };

        /// 最近一次 Load() 的结果，供 Log 判断用户是否指定了 logFile
        public static Config Current;

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
            "  \"logFile\": \"\",\n" +
            "  \"playlistFile\": \"\",\n" +
            "  \"workshopDir\": \"\",\n" +
            "  \"autoAddDownloads\": true,\n" +
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

            // 先把 logFile 定下来再写任何日志，免得 WARN 落到默认位置
            c.LogFile = Str(txt, "logFile", c.LogFile);
            c.PlaylistFile = Str(txt, "playlistFile", c.PlaylistFile);
            c.WorkshopDir = Str(txt, "workshopDir", c.WorkshopDir);
            string autoAdd = Str(txt, "autoAddDownloads", null);
            if (autoAdd != null) c.AutoAddDownloads = !autoAdd.Equals("false", StringComparison.OrdinalIgnoreCase);
            Current = c;

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
        private static readonly object PathGate = new object();
        private static string _file;

        /// 日志落点，按优先级：
        ///   1. config.json 的 logFile（绝对路径，或相对 exe 目录）
        ///   2. 项目/源码目录（含 src\WpeVideoLauncher.cs 的那个目录）
        ///   3. exe 所在目录（仅当它不是桌面/文档/下载这类"用户看得见"的目录）
        ///   4. %LOCALAPPDATA%\WpeVideoLauncher
        ///   5. 临时目录
        /// 不再无条件写在 exe 旁边：exe 常常就放在桌面上，日志会直接糊在桌面。
        public static string File
        {
            get
            {
                if (_file != null) return _file;
                lock (PathGate)
                {
                    if (_file == null) _file = ResolveFile();
                    return _file;
                }
            }
        }

        /// 配置改了 logFile 之后调用，下次写入重新定位
        public static void Reset()
        {
            lock (PathGate) { _file = null; }
        }

        private static string ResolveFile()
        {
            // 1) 配置里显式指定
            try
            {
                string configured = Config.Current == null ? null : Config.Current.LogFile;
                if (!string.IsNullOrEmpty(configured))
                {
                    string p = Path.IsPathRooted(configured) ? configured : Path.Combine(Config.ExeDir, configured);
                    if (EnsureDir(p)) return p;
                }
            }
            catch { }

            // 2) 项目目录：开发时日志就该跟源码在一起，而不是散在桌面上
            try
            {
                string proj = FindProjectDir();
                if (proj != null && CanWrite(proj)) return Path.Combine(proj, "launcher.log");
            }
            catch { }

            // 3) exe 旁边（绿色运行），但躲开桌面/文档这类目录
            if (!IsUserFolder(Config.ExeDir) && CanWrite(Config.ExeDir))
                return Path.Combine(Config.ExeDir, "launcher.log");

            // 4) 用户数据目录
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WpeVideoLauncher");
                Directory.CreateDirectory(dir);
                if (CanWrite(dir)) return Path.Combine(dir, "launcher.log");
            }
            catch { }

            // 5) 兜底
            return Path.Combine(Path.GetTempPath(), "WpeVideoLauncher.log");
        }

        /// 本项目的源码目录 = 含 src\WpeVideoLauncher.cs 的目录。
        /// 先查 exe 自身与各级上级（exe 编译在 release\ 下时命中），
        /// 再从 exe 目录往下找 1~3 层（exe 被复制到桌面或别处时照样能找到项目）。
        private static string FindProjectDir()
        {
            try
            {
                DirectoryInfo d = new DirectoryInfo(Config.ExeDir);
                for (int i = 0; d != null && i < 12; i++, d = d.Parent)
                    if (IsProjectDir(d.FullName)) return d.FullName;
            }
            catch { }
            return FindProjectBelow(Config.ExeDir, 3);
        }

        private static bool IsProjectDir(string dir)
        {
            // 注意: 本类里有 File 属性, 必须写全名 System.IO.File
            try { return System.IO.File.Exists(Path.Combine(Path.Combine(dir, "src"), "WpeVideoLauncher.cs")); }
            catch { return false; }
        }

        /// 有界广度优先：最多 3 层、最多看 2000 个目录，跳过 .git / node_modules / 系统目录
        private static string FindProjectBelow(string root, int maxDepth)
        {
            try
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return null;
                var dirs = new Queue<string>();
                var lvls = new Queue<int>();
                dirs.Enqueue(root); lvls.Enqueue(0);
                int visited = 0;
                while (dirs.Count > 0 && visited < 2000)
                {
                    string cur = dirs.Dequeue();
                    int lvl = lvls.Dequeue();
                    if (lvl >= maxDepth) continue;
                    string[] subs;
                    try { subs = Directory.GetDirectories(cur); } catch { continue; }
                    foreach (string s in subs)
                    {
                        if (++visited > 2000) break;
                        if (SkipDir(s)) continue;
                        if (IsProjectDir(s)) return s;
                        dirs.Enqueue(s); lvls.Enqueue(lvl + 1);
                    }
                }
            }
            catch { }
            return null;
        }

        private static bool SkipDir(string dir)
        {
            try
            {
                var di = new DirectoryInfo(dir);
                FileAttributes a = di.Attributes;
                if ((a & FileAttributes.ReparsePoint) != 0) return true;   // 符号链接/junction，别跟进去
                if ((a & FileAttributes.System) != 0) return true;
                string n = di.Name;
                if (n.Length == 0 || n[0] == '.') return true;
                if (n.Equals("node_modules", StringComparison.OrdinalIgnoreCase)) return true;
                if (n.Equals("AppData", StringComparison.OrdinalIgnoreCase)) return true;
                if (n.Equals("Windows", StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }
            catch { return true; }
        }

        /// 桌面/文档/下载/图片… 这些是用户自己看的目录，不往里写日志
        private static bool IsUserFolder(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir)) return true;
                string full = Path.GetFullPath(dir).TrimEnd('\\');
                Environment.SpecialFolder[] folders = {
                    Environment.SpecialFolder.Desktop,
                    Environment.SpecialFolder.DesktopDirectory,
                    Environment.SpecialFolder.CommonDesktopDirectory,
                    Environment.SpecialFolder.MyDocuments,
                    Environment.SpecialFolder.MyMusic,
                    Environment.SpecialFolder.MyPictures,
                    Environment.SpecialFolder.MyVideos,
                    Environment.SpecialFolder.UserProfile
                };
                foreach (Environment.SpecialFolder f in folders)
                {
                    string s = Environment.GetFolderPath(f);
                    if (string.IsNullOrEmpty(s)) continue;
                    if (string.Equals(full, s.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return true;
                }
                // 下载 / OneDrive 桌面（SpecialFolder 里没有对应项）
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(home))
                {
                    string[] extra = { "Downloads", "OneDrive\\Desktop", "OneDrive\\桌面", "OneDrive\\Documents" };
                    foreach (string name in extra)
                    {
                        string s = Path.Combine(home, name).TrimEnd('\\');
                        if (string.Equals(full, s, StringComparison.OrdinalIgnoreCase)) return true;
                    }
                }
            }
            catch { }
            return false;
        }

        /// 文件所在目录不存在就建，然后确认可写
        private static bool EnsureDir(string file)
        {
            try
            {
                string dir = Path.GetDirectoryName(file);
                if (string.IsNullOrEmpty(dir)) return false;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return CanWrite(dir);
            }
            catch { return false; }
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

    /// 一个创意工坊壁纸（一个文件夹）
    internal sealed class WallpaperInfo
    {
        public string Id;        // 创意工坊 id = 文件夹名
        public string Dir;
        public string Title;     // project.json 的 title，取不到就用文件夹名
        public string Type;      // project.json 的 type: video/scene/web/application
        public string Video;     // 最大的那个视频文件，没有则 null
        public int VideoCount;
        public string Size;      // 展示用，例如 "1.2 GB"

        public bool HasVideo { get { return !string.IsNullOrEmpty(Video); } }
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

        // ---------------- 创意工坊壁纸库 ----------------

        public const string WeAppId = "431960";   // Wallpaper Engine 的 Steam AppId

        /// 创意工坊目录: <Steam库>\steamapps\workshop\content\431960
        /// 依次从 配置 -> 正在运行的 WE -> WE 的 config.json -> 当前壁纸 -> Steam 注册表 -> 常见路径 猜
        public static string FindWorkshopDir(Config cfg)
        {
            // config 里显式指定的最优先（可以直接是 431960，也可以是它的上级）
            string own = NormalizeWorkshopDir(cfg == null ? null : cfg.WorkshopDir);
            if (own != null) return own;

            var cands = new List<string>();

            foreach (string proc in new string[] { "wallpaper64", "wallpaper32" })
            {
                try
                {
                    foreach (Process p in Process.GetProcessesByName(proc))
                    {
                        try
                        {
                            string dir = Path.GetDirectoryName(p.MainModule.FileName);
                            if (!string.IsNullOrEmpty(dir)) cands.Add(dir);
                        }
                        catch { }
                        finally { p.Dispose(); }
                    }
                }
                catch { }
            }

            string weCfg = null;
            try { weCfg = FindWeConfig(); } catch { }
            if (!string.IsNullOrEmpty(weCfg))
            {
                cands.Add(Path.GetDirectoryName(weCfg));
                // 当前正在用的壁纸文件通常就在创意工坊目录里
                try
                {
                    foreach (Promo pr in GetCurrentCandidates(weCfg)) { cands.Add(pr.File); break; }
                }
                catch { }
            }

            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                    if (k != null)
                    {
                        object v = k.GetValue("SteamPath");
                        if (v != null) cands.Add(v.ToString().Replace('/', '\\'));
                    }
            }
            catch { }

            foreach (string root in WeRoots) cands.Add(root);

            foreach (string c in cands)
            {
                string ws = FromSteamApps(c);
                if (!string.IsNullOrEmpty(ws) && Directory.Exists(ws)) return ws;
            }
            return null;
        }

        /// 把 config 里填的 workshopDir 归一化：
        /// 直接给 431960 / 给它的上级(content) / 给库目录，都能认
        private static string NormalizeWorkshopDir(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir)) return null;
                string d = dir.Replace('/', '\\').TrimEnd('\\');
                if (!Directory.Exists(d)) return null;
                if (string.Equals(Path.GetFileName(d), WeAppId, StringComparison.OrdinalIgnoreCase)) return d;
                string sub = Path.Combine(d, WeAppId);
                if (Directory.Exists(sub)) return sub;
                return d;   // 就当它本身就是壁纸目录
            }
            catch { return null; }
        }

        /// 从任意位于 steamapps 里的路径(目录或文件)推出创意工坊目录
        private static string FromSteamApps(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return null;
                DirectoryInfo d = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path).Directory;
                for (int i = 0; d != null && i < 10; i++, d = d.Parent)
                    if (string.Equals(d.Name, "steamapps", StringComparison.OrdinalIgnoreCase))
                        return Path.Combine(Path.Combine(Path.Combine(d.FullName, "workshop"), "content"), WeAppId);
            }
            catch { }
            return null;
        }

        /// 列出创意工坊目录下的所有壁纸，按标题排序
        public static List<WallpaperInfo> GetWallpapers(string workshopDir, string[] exts)
        {
            var list = new List<WallpaperInfo>();
            if (string.IsNullOrEmpty(workshopDir) || !Directory.Exists(workshopDir)) return list;
            string[] dirs;
            try { dirs = Directory.GetDirectories(workshopDir); }
            catch { return list; }
            foreach (string dir in dirs) list.Add(ReadWallpaper(dir, exts));
            list.Sort(delegate (WallpaperInfo a, WallpaperInfo b)
            {
                return string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase);
            });
            return list;
        }

        /// 读一个壁纸文件夹: 标题、类型、最大的那个视频
        public static WallpaperInfo ReadWallpaper(string dir, string[] exts)
        {
            var info = new WallpaperInfo();
            info.Dir = dir;
            try { info.Id = Path.GetFileName(dir.TrimEnd('\\')); } catch { info.Id = dir; }
            info.Title = info.Id;
            info.Type = null;
            try
            {
                string txt;
                if (Config.TryReadAllText(Path.Combine(dir, "project.json"), out txt))
                {
                    string t = Config.Str(txt, "title", null);
                    if (!string.IsNullOrEmpty(t) && t.Trim().Length > 0) info.Title = t.Trim();
                    string ty = Config.Str(txt, "type", null);
                    if (!string.IsNullOrEmpty(ty)) info.Type = ty.Trim();
                }
            }
            catch { }
            FindVideos(info, exts);
            return info;
        }

        /// 找出壁纸目录里的视频，记录数量并挑出最大的一个
        private static void FindVideos(WallpaperInfo info, string[] exts)
        {
            string best = null;
            long bestLen = -1;
            int count = 0;
            var stack = new Stack<string>();
            stack.Push(info.Dir);
            int guard = 0;
            while (stack.Count > 0 && guard++ < 500)
            {
                string cur = stack.Pop();
                try
                {
                    foreach (string f in Directory.GetFiles(cur))
                    {
                        if (!IsVideo(f, exts)) continue;
                        long len = 0;
                        try { len = new FileInfo(f).Length; } catch { }
                        count++;
                        if (len > bestLen) { bestLen = len; best = f; }
                    }
                }
                catch { }
                try { foreach (string s in Directory.GetDirectories(cur)) stack.Push(s); }
                catch { }
            }
            info.VideoCount = count;
            info.Video = best;
            info.Size = HumanSize(bestLen > 0 ? bestLen : 0);
        }

        public static string HumanSize(long bytes)
        {
            if (bytes <= 0) return "-";
            double b = bytes;
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (b >= 1024 && i < u.Length - 1) { b /= 1024; i++; }
            return (b >= 100 ? b.ToString("0") : b.ToString("0.0")) + " " + u[i];
        }

        /// 下载中/没写完的临时文件
        public static bool IsPartial(string path)
        {
            string e = Path.GetExtension(path).ToLowerInvariant();
            return e == ".part" || e == ".tmp" || e == ".crdownload" || e == ".downloading" || e == ".partial";
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
            sb.AppendLine("日志文件        : " + Log.File);
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
            string plFile = Playlist.FilePath(cfg);
            sb.AppendLine("播放列表        : " + plFile + " (" + Playlist.Count(plFile) + " 项)");
            string wsDir = FindWorkshopDir(cfg);
            sb.AppendLine("创意工坊目录    : " + (wsDir == null ? "(未找到)" : wsDir));
            if (!string.IsNullOrEmpty(wsDir))
            {
                List<WallpaperInfo> all = GetWallpapers(wsDir, cfg.VideoExtensions);
                int withVideo = 0;
                foreach (WallpaperInfo w in all) if (w.HasVideo) withVideo++;
                sb.AppendLine("壁纸总数        : " + all.Count + " (含视频 " + withVideo + ")");
            }
            sb.AppendLine("自动加入新壁纸  : " + (cfg.AutoAddDownloads ? "开" : "关"));

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
        private System.Windows.Forms.Timer _scanTimer;

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

            var plMenu = new ToolStripMenuItem("播放列表");
            plMenu.DropDownItems.Add("▶  播放播放列表", null, delegate { PlayPlaylist(); });
            plMenu.DropDownItems.Add("批量选择壁纸…", null, delegate { ShowPicker(); });
            plMenu.DropDownItems.Add(new ToolStripSeparator());
            plMenu.DropDownItems.Add("把现有壁纸补进播放列表", null, delegate { ScanNow(true); });
            plMenu.DropDownItems.Add("打开播放列表文件", null, delegate { OpenPath(Playlist.FilePath(_cfg)); });
            plMenu.DropDownItems.Add("清空播放列表", null, delegate { ClearPlaylist(); });
            menu.Items.Add(plMenu);

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
            StartAutoScan();
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

        // ---------------- 播放列表 ----------------

        /// 新下载的壁纸自动进播放列表：启动 2 秒后扫一次，之后每 20 秒扫一次
        /// （没有新壁纸时只列一下目录名，几乎不花代价）
        private void StartAutoScan()
        {
            if (!_cfg.AutoAddDownloads)
            {
                Log.Info("自动加入新壁纸：已按配置关闭");
                return;
            }
            var first = new System.Windows.Forms.Timer();
            first.Interval = 2000;
            first.Tick += delegate
            {
                first.Stop();
                first.Dispose();
                ScanNow(false);
            };
            first.Start();

            _scanTimer = new System.Windows.Forms.Timer();
            _scanTimer.Interval = 20000;
            _scanTimer.Tick += delegate { ScanNow(false); };
            _scanTimer.Start();
        }

        private void ScanNow(bool force)
        {
            try
            {
                AutoPlaylist.ScanResult r = AutoPlaylist.Scan(_cfg, force);
                if (r.Added > 0)
                {
                    var names = new StringBuilder();
                    for (int i = 0; i < r.Items.Count && i < 3; i++)
                        names.Append("· ").Append(r.Items[i].Title).Append("\n");
                    if (r.Items.Count > 3) names.Append("…共 ").Append(r.Added).Append(" 项");
                    Balloon(r.FirstRun ? "已把现有壁纸加入播放列表" : "新壁纸已加入播放列表", names.ToString());
                }
                else if (force)
                {
                    Balloon("扫描完成", r.Detail);
                }
            }
            catch (Exception ex) { Log.Error("自动加入播放列表失败: " + ex.Message); }
        }

        private void PlayPlaylist()
        {
            string f = Playlist.FilePath(_cfg);
            int n = Playlist.Count(f);
            if (n == 0)
            {
                Balloon("播放列表是空的", "托盘菜单 →「播放列表」→「批量选择壁纸…」可以一次勾多个壁纸加进来。");
                return;
            }
            PlayFile(f);
        }

        private void ShowPicker()
        {
            try
            {
                string ws = Core.FindWorkshopDir(_cfg);
                if (string.IsNullOrEmpty(ws))
                {
                    Balloon("找不到创意工坊目录", "可以在 config.json 的 workshopDir 里手动指定 431960 目录。");
                    return;
                }
                List<WallpaperInfo> all = Core.GetWallpapers(ws, _cfg.VideoExtensions);
                using (var f = new PlaylistForm(_cfg, all)) { f.ShowDialog(); }
            }
            catch (Exception ex)
            {
                Log.Error("打开批量选择窗口失败: " + ex.Message);
                Balloon("打开批量选择失败", ex.Message);
            }
        }

        private void ClearPlaylist()
        {
            string f = Playlist.FilePath(_cfg);
            if (Playlist.Count(f) == 0) { Balloon("播放列表已经是空的", ""); return; }
            if (MessageBox.Show("确定清空播放列表？\n\n" + f, "WpeVideoLauncher",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            Playlist.Clear(f);
            Balloon("播放列表已清空", "之后新下载的壁纸还是会自动加进来。");
        }

        private void Reload()
        {
            try
            {
                Config fresh = Config.Load();
                Log.Reset();   // logFile 可能被改了，下次写日志重新定位
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
            if (_scanTimer != null) { _scanTimer.Stop(); _scanTimer.Dispose(); _scanTimer = null; }
            if (_hotkey != null) { _hotkey.Dispose(); _hotkey = null; }
            if (_icon != null) { _icon.Visible = false; _icon.Dispose(); }
            if (_ownIcon != null) { _ownIcon.Dispose(); _ownIcon = null; }
        }
    }

    /// 播放列表：通用 m3u8（UTF-8 带 BOM），PotPlayer / VLC / MPC-HC 都能直接播
    internal static class Playlist
    {
        private static readonly object Gate = new object();

        public sealed class Item
        {
            public string File;
            public string Title;
        }

        /// m3u8 路径：config 指定优先，否则跟 launcher.log 放同一目录
        public static string FilePath(Config cfg)
        {
            try
            {
                if (cfg != null && !string.IsNullOrEmpty(cfg.PlaylistFile))
                    return Path.IsPathRooted(cfg.PlaylistFile)
                        ? cfg.PlaylistFile
                        : Path.Combine(Config.ExeDir, cfg.PlaylistFile);
            }
            catch { }
            return Path.Combine(LogDir(), "playlist.m3u8");
        }

        private static string LogDir()
        {
            string dir = null;
            try { dir = Path.GetDirectoryName(Log.File); } catch { }
            if (string.IsNullOrEmpty(dir)) dir = Config.ExeDir;
            return dir;
        }

        /// "已经见过"的壁纸 id 记录，用来判断哪些壁纸是新下载的
        public static string SeenFilePath(string playlistFile)
        {
            string dir = null;
            try { dir = Path.GetDirectoryName(playlistFile); } catch { }
            if (string.IsNullOrEmpty(dir)) dir = Config.ExeDir;
            return Path.Combine(dir, ".playlist-seen.txt");
        }

        public static List<Item> Load(string file)
        {
            lock (Gate) { return LoadUnlocked(file); }
        }

        public static int Count(string file)
        {
            return Load(file).Count;
        }

        /// 播放列表里所有视频文件的路径
        public static HashSet<string> PathSet(string file)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Item i in Load(file)) set.Add(i.File);
            return set;
        }

        private static List<Item> LoadUnlocked(string file)
        {
            var list = new List<Item>();
            string txt;
            if (!Config.TryReadAllText(file, out txt)) return list;
            string pending = null;
            foreach (string raw in txt.Split('\n'))
            {
                string line = raw.TrimStart('\uFEFF').Trim();
                if (line.Length == 0) continue;
                if (line[0] == '#')
                {
                    if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
                    {
                        int comma = line.IndexOf(',');
                        pending = comma >= 0 ? line.Substring(comma + 1).Trim() : null;
                    }
                    continue;
                }
                var it = new Item();
                it.File = line;
                it.Title = string.IsNullOrEmpty(pending) ? Path.GetFileNameWithoutExtension(line) : pending;
                pending = null;
                list.Add(it);
            }
            return list;
        }

        /// 追加（按完整路径去重），返回真正新增的条数
        public static int Add(string file, List<Item> items, out int duplicates)
        {
            duplicates = 0;
            if (items == null || items.Count == 0) return 0;
            lock (Gate)
            {
                List<Item> cur = LoadUnlocked(file);
                var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Item i in cur) have.Add(i.File);
                int added = 0;
                foreach (Item i in items)
                {
                    if (i == null || string.IsNullOrEmpty(i.File)) continue;
                    if (!have.Add(i.File)) { duplicates++; continue; }
                    cur.Add(i);
                    added++;
                }
                if (added > 0) WriteUnlocked(file, cur);
                return added;
            }
        }

        /// 按完整路径移除，返回移除条数
        public static int Remove(string file, List<string> paths)
        {
            if (paths == null || paths.Count == 0) return 0;
            var want = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string p in paths) want.Add(p);
            lock (Gate)
            {
                List<Item> cur = LoadUnlocked(file);
                int removed = 0;
                for (int i = cur.Count - 1; i >= 0; i--)
                    if (want.Contains(cur[i].File)) { cur.RemoveAt(i); removed++; }
                if (removed > 0) WriteUnlocked(file, cur);
                return removed;
            }
        }

        public static void Clear(string file)
        {
            lock (Gate) { WriteUnlocked(file, new List<Item>()); }
        }

        private static void WriteUnlocked(string file, List<Item> items)
        {
            try
            {
                string dir = Path.GetDirectoryName(file);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var sb = new StringBuilder();
                sb.Append("#EXTM3U\r\n");
                foreach (Item i in items)
                {
                    string title = string.IsNullOrEmpty(i.Title) ? Path.GetFileNameWithoutExtension(i.File) : i.Title;
                    title = title.Replace("\r", " ").Replace("\n", " ");
                    sb.Append("#EXTINF:-1,").Append(title).Append("\r\n");
                    sb.Append(i.File).Append("\r\n");
                }
                string tmp = file + ".tmp";
                System.IO.File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(true));
                System.IO.File.Copy(tmp, file, true);
                System.IO.File.Delete(tmp);
                Log.Info("播放列表已保存: " + file + " (" + items.Count + " 项)");
            }
            catch (Exception ex) { Log.Error("写播放列表失败 " + file + " : " + ex.Message); }
        }
    }

    /// "已经见过"的壁纸 id（判断哪些是新下载的）
    internal static class SeenWallpapers
    {
        public static bool Exists(string file)
        {
            try { return System.IO.File.Exists(file); } catch { return false; }
        }

        public static HashSet<string> Load(string file)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!System.IO.File.Exists(file)) return set;
                foreach (string line in System.IO.File.ReadAllLines(file))
                {
                    string s = line.TrimStart('\uFEFF').Trim();
                    if (s.Length > 0 && s[0] != '#') set.Add(s);
                }
            }
            catch (Exception ex) { Log.Warn("读已见壁纸记录失败: " + ex.Message); }
            return set;
        }

        public static void Save(string file, HashSet<string> ids)
        {
            try
            {
                string dir = Path.GetDirectoryName(file);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var list = new List<string>(ids);
                list.Sort(StringComparer.OrdinalIgnoreCase);
                System.IO.File.WriteAllLines(file, list.ToArray(), new UTF8Encoding(true));
            }
            catch (Exception ex) { Log.Warn("写已见壁纸记录失败: " + ex.Message); }
        }
    }

    /// 把新下载的壁纸自动补进播放列表
    internal static class AutoPlaylist
    {
        public sealed class ScanResult
        {
            public string WorkshopDir;
            public string PlaylistFile;
            public bool FirstRun;
            public int Total;        // 库里壁纸总数
            public int Added;        // 本次新加入播放列表的条数
            public int Duplicates;   // 已经在列表里的
            public int Pending;      // 还在下载/还没落盘的
            public List<Playlist.Item> Items = new List<Playlist.Item>();
            public string Detail = "";
        }

        /// 扫描一次：
        ///   首次运行（或 force）= 把库里现有的壁纸全部补进播放列表；
        ///   平时 = 只补新出现的壁纸。
        public static ScanResult Scan(Config cfg, bool force)
        {
            var r = new ScanResult();
            string plFile = Playlist.FilePath(cfg);
            r.PlaylistFile = plFile;
            string seenFile = Playlist.SeenFilePath(plFile);
            string ws = Core.FindWorkshopDir(cfg);
            r.WorkshopDir = ws;
            if (string.IsNullOrEmpty(ws))
            {
                r.Detail = "没找到 Wallpaper Engine 创意工坊目录（可在 config.json 用 workshopDir 指定）";
                return r;
            }

            bool firstRun = force || !SeenWallpapers.Exists(seenFile);
            HashSet<string> seen = firstRun
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : SeenWallpapers.Load(seenFile);
            r.FirstRun = firstRun;

            string[] dirs;
            try { dirs = Directory.GetDirectories(ws); }
            catch (Exception ex) { r.Detail = "读创意工坊目录失败: " + ex.Message; return r; }
            r.Total = dirs.Length;

            // 快速路径：没有新 id 直接返回，每 20 秒的轮询几乎不花代价
            var todo = new List<string>();
            foreach (string d in dirs)
            {
                string id = Path.GetFileName(d.TrimEnd('\\'));
                if (firstRun || !seen.Contains(id)) todo.Add(d);
            }
            if (todo.Count == 0)
            {
                r.Detail = string.Format("库里 {0} 个壁纸，没有新的", r.Total);
                return r;
            }

            var items = new List<Playlist.Item>();
            foreach (string d in todo)
            {
                WallpaperInfo w = Core.ReadWallpaper(d, cfg.VideoExtensions);
                if (!Settled(w)) { r.Pending++; continue; }   // 还没下载完，下次再看
                if (w.HasVideo)
                {
                    var it = new Playlist.Item();
                    it.File = w.Video;
                    it.Title = w.Title;
                    items.Add(it);
                }
                seen.Add(w.Id);   // 没有视频的也记下，免得每次重复检查
            }

            int dup = 0;
            if (items.Count > 0) r.Added = Playlist.Add(plFile, items, out dup);
            r.Duplicates = dup;
            r.Items = items;
            SeenWallpapers.Save(seenFile, seen);

            r.Detail = string.Format("{0} 个壁纸，新加入 {1} 项，重复 {2} 项，还在下载 {3} 个",
                r.Total, r.Added, r.Duplicates, r.Pending);
            // 只在"首次"或"真有新增"时记日志，否则每 20 秒一次会把日志刷满
            if (firstRun || r.Added > 0)
                Log.Info("自动加入播放列表" + (firstRun ? "(首次全量)" : "") + ": " + r.Detail);
            return r;
        }

        /// 这个壁纸文件夹下载完了吗：有内容、没有 .part/.tmp 之类、最近 5 秒没有写入；
        /// 另外 project.json 声明是 video 类型的话，必须已经能看到视频文件。
        private static bool Settled(WallpaperInfo w)
        {
            try
            {
                if (!Directory.Exists(w.Dir)) return false;
                DateTime newest = DateTime.MinValue;
                bool any = false;
                foreach (string f in Directory.GetFiles(w.Dir, "*", SearchOption.AllDirectories))
                {
                    any = true;
                    if (Core.IsPartial(f)) return false;
                    DateTime t;
                    try { t = System.IO.File.GetLastWriteTimeUtc(f); }
                    catch { continue; }
                    if (t > newest) newest = t;
                }
                if (!any) return false;                                          // 空目录，再等等
                if ((DateTime.UtcNow - newest).TotalSeconds < 5) return false;    // 刚写过，再等等
                if (w.HasVideo) return true;
                return !(w.Type != null && w.Type.Equals("video", StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }
    }

    /// 批量选择壁纸 -> 播放列表
    internal sealed class PlaylistForm : Form
    {
        private sealed class Row
        {
            public WallpaperInfo W;
            public bool InList;

            public override string ToString()
            {
                var sb = new StringBuilder();
                sb.Append(W.HasVideo ? "● " : "○ ");
                sb.Append(W.Title);
                if (W.HasVideo)
                {
                    sb.Append("    ").Append(W.Size);
                    if (W.VideoCount > 1) sb.Append("（").Append(W.VideoCount).Append(" 个视频，取最大）");
                }
                else
                {
                    sb.Append("    [");
                    sb.Append(string.IsNullOrEmpty(W.Type) ? "没有视频文件" : W.Type + "，没有视频文件");
                    sb.Append("]");
                }
                if (InList) sb.Append("    [已在播放列表]");
                return sb.ToString();
            }
        }

        private readonly Config _cfg;
        private readonly List<WallpaperInfo> _all;
        private readonly string _plFile;
        private readonly HashSet<string> _checked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _inList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private TextBox _filter;
        private CheckedListBox _list;
        private Label _status;
        private Panel _topPanel;
        private Panel _bottomPanel;
        private FlowLayoutPanel _row1;
        private FlowLayoutPanel _row2;
        private string _feedback = "";
        private bool _loading;
        private bool _laying;

        public PlaylistForm(Config cfg, List<WallpaperInfo> all)
        {
            _cfg = cfg;
            _all = all == null ? new List<WallpaperInfo>() : all;
            _plFile = Playlist.FilePath(cfg);
            ReloadInList();
            BuildUi();
            FillList();
            Render();
        }

        private void ReloadInList()
        {
            _inList.Clear();
            foreach (string p in Playlist.PathSet(_plFile)) _inList.Add(p);
        }

        private void BuildUi()
        {
            Text = "批量选择壁纸 → 播放列表";
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.None;   // 自己按 Font.Height 算尺寸，别再让 WinForms 缩放
            ClientSize = new Size(820, 620);
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            ShowIcon = false;

            int rowH = Font.Height + 14;
            int statusH = Font.Height + 10;
            int cw = ClientSize.Width;
            int ch = ClientSize.Height;
            int bottomH = rowH * 2 + statusH;

            // 顶部搜索行
            var top = new Panel();
            _topPanel = top;
            var lbl = new Label();
            lbl.Text = "搜索:";
            lbl.Dock = DockStyle.Left;
            lbl.Width = 52;
            lbl.TextAlign = ContentAlignment.MiddleLeft;
            _filter = new TextBox();
            _filter.Dock = DockStyle.Fill;
            _filter.TextChanged += delegate { FillList(); Render(); };
            top.Controls.Add(_filter);   // Fill 的先加，边缘的后加
            top.Controls.Add(lbl);

            // 列表
            _list = new CheckedListBox();
            _list.CheckOnClick = true;
            _list.IntegralHeight = false;
            _list.BorderStyle = BorderStyle.FixedSingle;
            _list.ItemCheck += OnItemCheck;

            // 底部两行按钮 + 状态行
            var bottom = new Panel();
            _bottomPanel = bottom;

            var row1 = new FlowLayoutPanel();
            _row1 = row1;
            row1.WrapContents = false;
            row1.Margin = new Padding(0);
            row1.Controls.Add(MakeButton("全选(有视频)", delegate { CheckAll(true); }));
            row1.Controls.Add(MakeButton("全不选", delegate { CheckAll(false); }));
            row1.Controls.Add(MakeButton("反选", delegate { Invert(); }));

            var row2 = new FlowLayoutPanel();
            _row2 = row2;
            row2.WrapContents = false;
            row2.Margin = new Padding(0);
            row2.Controls.Add(MakeButton("加入播放列表", delegate { AddChecked(); }));
            row2.Controls.Add(MakeButton("从列表移除", delegate { RemoveChecked(); }));
            row2.Controls.Add(MakeButton("打开播放列表", delegate { OpenPlaylist(); }));
            row2.Controls.Add(MakeButton("关闭", delegate { Close(); }));

            _status = new Label();
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.AutoEllipsis = true;
            _status.Padding = new Padding(6, 0, 6, 0);

            bottom.Controls.Add(row1);
            bottom.Controls.Add(row2);
            bottom.Controls.Add(_status);

            Controls.Add(_list);
            Controls.Add(bottom);
            Controls.Add(top);

            ApplyLayout();
        }

        /// 按"当前真正的"客户区尺寸摆位置。
        /// 不能用 Dock/Anchor 一把梭：窗体会在构造之后被系统/DPI 改尺寸，
        /// 那时候控件的绝对坐标就落到客户区外面去了（按钮整行看不见）。
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_laying) return;
            _laying = true;
            try { ApplyLayout(); }
            finally { _laying = false; }
        }

        private void ApplyLayout()
        {
            if (_topPanel == null || _list == null || _bottomPanel == null) return;
            int rowH = Font.Height + 14;
            int statusH = Font.Height + 10;
            int cw = ClientSize.Width;
            int ch = ClientSize.Height;
            int bottomH = rowH * 2 + statusH;

            _topPanel.SetBounds(0, 0, cw, rowH);
            _bottomPanel.SetBounds(0, ch - bottomH, cw, bottomH);
            _list.SetBounds(0, rowH, cw, Math.Max(60, ch - rowH - bottomH));
            _row1.SetBounds(0, 0, cw, rowH);
            _row2.SetBounds(0, rowH, cw, rowH);
            _status.SetBounds(0, rowH * 2, cw, statusH);
        }

        private static Button MakeButton(string text, EventHandler onClick)
        {
            var b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.MinimumSize = new Size(100, 26);
            b.Margin = new Padding(4, 3, 4, 3);
            b.Click += onClick;
            return b;
        }

        private void FillList()
        {
            _loading = true;
            try
            {
                _list.Items.Clear();
                string q = _filter == null ? "" : _filter.Text.Trim();
                foreach (WallpaperInfo w in _all)
                {
                    if (q.Length > 0
                        && w.Title.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) < 0
                        && w.Id.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var r = new Row();
                    r.W = w;
                    r.InList = w.HasVideo && _inList.Contains(w.Video);
                    _list.Items.Add(r, w.HasVideo && _checked.Contains(w.Id));
                }
            }
            finally { _loading = false; }
        }

        private void OnItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (_loading) return;
            Row r = _list.Items[e.Index] as Row;
            if (r == null) return;
            if (e.NewValue == CheckState.Checked && !r.W.HasVideo)
            {
                e.NewValue = CheckState.Unchecked;
                SetFeedback("「" + r.W.Title + "」没有视频文件，不能加入播放列表");
                return;
            }
            if (e.NewValue == CheckState.Checked) _checked.Add(r.W.Id);
            else _checked.Remove(r.W.Id);
            Render();
        }

        private void CheckAll(bool on)
        {
            _loading = true;
            try
            {
                for (int i = 0; i < _list.Items.Count; i++)
                {
                    Row r = _list.Items[i] as Row;
                    if (r == null || !r.W.HasVideo) continue;
                    _list.SetItemChecked(i, on);
                    if (on) _checked.Add(r.W.Id); else _checked.Remove(r.W.Id);
                }
            }
            finally { _loading = false; }
            SetFeedback(on ? "已勾选当前列表里所有有视频的壁纸" : "已取消勾选");
        }

        private void Invert()
        {
            _loading = true;
            try
            {
                for (int i = 0; i < _list.Items.Count; i++)
                {
                    Row r = _list.Items[i] as Row;
                    if (r == null || !r.W.HasVideo) continue;
                    bool now = !_list.GetItemChecked(i);
                    _list.SetItemChecked(i, now);
                    if (now) _checked.Add(r.W.Id); else _checked.Remove(r.W.Id);
                }
            }
            finally { _loading = false; }
            SetFeedback("已反选");
        }

        private void AddChecked()
        {
            var items = new List<Playlist.Item>();
            foreach (WallpaperInfo w in _all)
            {
                if (!w.HasVideo || !_checked.Contains(w.Id)) continue;
                var it = new Playlist.Item();
                it.File = w.Video;
                it.Title = w.Title;
                items.Add(it);
            }
            if (items.Count == 0) { SetFeedback("还没有勾选任何带视频的壁纸"); return; }
            int dup = 0;
            int added = Playlist.Add(_plFile, items, out dup);
            ReloadInList();
            FillList();
            SetFeedback(string.Format("已加入 {0} 项{1}", added,
                dup > 0 ? "（" + dup + " 项本来就在列表里）" : ""));
        }

        private void RemoveChecked()
        {
            var paths = new List<string>();
            foreach (WallpaperInfo w in _all)
                if (w.HasVideo && _checked.Contains(w.Id) && _inList.Contains(w.Video))
                    paths.Add(w.Video);
            if (paths.Count == 0) { SetFeedback("勾选的壁纸都不在播放列表里"); return; }
            int removed = Playlist.Remove(_plFile, paths);
            ReloadInList();
            FillList();
            SetFeedback("已从播放列表移除 " + removed + " 项");
        }

        private void OpenPlaylist()
        {
            try
            {
                if (System.IO.File.Exists(_plFile)) Process.Start("explorer.exe", "\"" + _plFile + "\"");
                else
                {
                    string dir = Path.GetDirectoryName(_plFile);
                    if (!string.IsNullOrEmpty(dir)) Process.Start("explorer.exe", "\"" + dir + "\"");
                }
            }
            catch (Exception ex) { Log.Warn("打开播放列表失败: " + ex.Message); }
        }

        private void SetFeedback(string msg)
        {
            _feedback = msg;
            Render();
        }

        private void Render()
        {
            if (_status == null) return;
            string head = string.IsNullOrEmpty(_feedback) ? "" : _feedback + "    |    ";
            _status.Text = string.Format("{0}库里 {1} 个壁纸  ·  显示 {2}  ·  已勾选 {3}  ·  播放列表 {4} 项",
                head, _all.Count, _list.Items.Count, _checked.Count, Playlist.Count(_plFile));
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
