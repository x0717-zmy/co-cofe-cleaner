using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

[assembly: AssemblyTitle("Co_cofe 系统垃圾清理")]
[assembly: AssemblyDescription("只清理临时文件与回收站，不触碰文档、程序与日志")]
[assembly: AssemblyProduct("Co_cofe 系统垃圾清理")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace LjCleanupSafe
{
    internal static class Program
    {
        private const string AppName = "Co_cofe 系统垃圾清理";
        private const string AppVersion = "1.1.0";
        private const long KB = 1024L;
        private const long MB = 1024L * 1024L;
        private const long GB = 1024L * 1024L * 1024L;

        private const uint SHERB_NOCONFIRMATION = 0x00000001;
        private const uint SHERB_NOPROGRESS = 0x00000002;
        private const uint SHERB_NOUI = 0x00000004;

        private static readonly string SelfPath = SelfLocation();
        private static readonly bool Elevated = IsAdmin();

        private static string SelfLocation()
        {
            try { return Assembly.GetExecutingAssembly().Location ?? string.Empty; }
            catch { return string.Empty; }
        }

        #region Win32

        [StructLayout(LayoutKind.Sequential)]
        private struct SHQUERYRBINFO
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHQueryRecycleBin(string pszRootPath, ref SHQUERYRBINFO info);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string pszRootPath, uint flags);

        #endregion

        #region 选项与数据结构

        private sealed class Options
        {
            public bool DryRun;
            public bool AssumeYes;
            public int Days = 1;
            public bool SkipRecycleBin;
            public bool Help;
            public string TestRoot;
        }

        private sealed class Group
        {
            public string Name;
            public string Path;
            public bool NeedsAdmin;
            public bool IsRecycleBin;
            public bool Skipped;
            public string SkipReason;

            public readonly List<string> Files = new List<string>();
            public long Bytes;
            public long RecycledItems;

            public int TooNew;
            public int SystemSkipped;
            public int Locked;

            public int Deleted;
            public long DeletedBytes;
            public int EmptyDirsRemoved;
        }

        #endregion

        private static int Main(string[] args)
        {
            Options o;
            try
            {
                o = ParseArgs(args);
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine();
                Console.WriteLine("  参数错误：" + ex.Message);
                Console.WriteLine();
                PrintHelp();
                WaitKey();
                return 2;
            }

            if (o.Help)
            {
                PrintHelp();
                WaitKey();
                return 0;
            }

            try { Console.Title = AppName; } catch { }
            Console.WriteLine();

            PrintHeader(o);

            var groups = BuildGroups(o);
            if (groups.Count == 0)
            {
                Console.WriteLine("  没有可清理的目标，程序退出。");
                WaitKey();
                return 0;
            }

            Console.WriteLine("  正在扫描……");
            foreach (Group g in groups) Scan(g, o);

            Console.WriteLine();
            PrintScanResult(groups, o);

            long totalBytes = 0;
            long totalFiles = 0;
            int totalItems = 0;
            bool anything = false;
            foreach (Group g in groups)
            {
                if (g.Skipped) continue;
                if (g.IsRecycleBin)
                {
                    totalBytes += g.Bytes;
                    totalItems += (int)Math.Min(int.MaxValue, g.RecycledItems);
                    if (g.RecycledItems > 0) anything = true;
                }
                else
                {
                    totalBytes += g.Bytes;
                    totalFiles += g.Files.Count;
                    if (g.Files.Count > 0) anything = true;
                }
            }

            if (!anything)
            {
                Console.WriteLine();
                Console.WriteLine("  没有需要清理的内容，系统很干净。");
                WaitKey();
                return 0;
            }

            Console.WriteLine();
            Console.WriteLine("  ────────────────────────────────────────────");
            Console.WriteLine("  合计：" + FormatBytes(totalBytes) +
                              "（" + totalFiles.ToString("N0") + " 个文件" +
                              (totalItems > 0 ? "，" + totalItems.ToString("N0") + " 个回收站项目" : "") + "）");

            foreach (Group g in groups)
            {
                if (!g.Skipped && g.IsRecycleBin && g.RecycledItems > 0)
                    Console.WriteLine("  注意：回收站的 " + g.RecycledItems.ToString("N0") + " 项（" +
                                      FormatBytes(g.Bytes) + "）会被永久删除，无法恢复。");
            }

            Console.WriteLine();

            if (o.DryRun)
            {
                Console.WriteLine("  预览模式：未删除任何内容。");
                Console.WriteLine("  去掉 --dry-run 参数即可真正执行清理。");
                WaitKey();
                return 0;
            }

            if (!o.AssumeYes && !Confirm("确认清理以上内容？", false))
            {
                Console.WriteLine();
                Console.WriteLine("  已取消，未删除任何内容。");
                WaitKey();
                return 1;
            }

            var log = new List<string>();
            log.Add(AppName + " v" + AppVersion + "  运行时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            log.Add("清理阈值: " + (o.Days > 0 ? o.Days + " 天前的文件" : "不限制时间") +
                    "   管理员: " + (Elevated ? "是" : "否") + "   预览模式: " + (o.DryRun ? "是" : "否"));
            log.Add(new string('-', 60));

            Console.WriteLine();
            long freedFiles = 0, freedBytes = 0;
            int removedDirs = 0;

            foreach (Group g in groups)
            {
                if (g.Skipped) continue;

                if (g.IsRecycleBin)
                {
                    if (g.RecycledItems <= 0) continue;
                    Console.Write("  清空回收站……");
                    uint flags = SHERB_NOCONFIRMATION | SHERB_NOPROGRESS | SHERB_NOUI;
                    int hr = SHEmptyRecycleBin(IntPtr.Zero, null, flags);
                    if (hr == 0)
                    {
                        Console.WriteLine("完成（" + g.RecycledItems.ToString("N0") + " 项 / " + FormatBytes(g.Bytes) + "）");
                        log.Add("清空回收站: " + g.RecycledItems + " 项 / " + g.Bytes + " 字节");
                        freedBytes += g.Bytes;
                    }
                    else
                    {
                        Console.WriteLine("失败（HRESULT 0x" + hr.ToString("X8") + "）");
                        log.Add("清空回收站失败, HRESULT 0x" + hr.ToString("X8"));
                    }
                    continue;
                }

                if (g.Files.Count == 0) continue;
                Console.Write("  清理" + g.Name + "……");
                foreach (string file in g.Files)
                {
                    long len = 0;
                    try
                    {
                        FileInfo fi = new FileInfo(file);
                        if (!fi.Exists) continue;
                        len = fi.Length;
                        FileAttributes attrs = fi.Attributes;
                        if ((attrs & FileAttributes.ReadOnly) != 0)
                            File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
                        File.Delete(file);
                        g.Deleted++;
                        g.DeletedBytes += len;
                        log.Add("已删除  " + file);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        g.Locked++;
                        log.Add("跳过(无权限) " + file);
                    }
                    catch (IOException)
                    {
                        g.Locked++;
                        log.Add("跳过(被占用) " + file);
                    }
                    catch (Exception ex)
                    {
                        g.Locked++;
                        log.Add("跳过(" + ex.GetType().Name + ") " + file);
                    }
                }
                g.EmptyDirsRemoved = PruneEmptyDirs(g.Path, log);
                removedDirs += g.EmptyDirsRemoved;
                freedFiles += g.Deleted;
                freedBytes += g.DeletedBytes;
                Console.WriteLine("完成（" + g.Deleted.ToString("N0") + " 个文件 / " + FormatBytes(g.DeletedBytes) + "）");
            }

            Console.WriteLine();
            Console.WriteLine("  ════════════════════════════════════════════");
            Console.WriteLine("  清理完成");
            Console.WriteLine("  ════════════════════════════════════════════");
            Console.WriteLine("  删除文件：" + freedFiles.ToString("N0") + " 个");
            if (removedDirs > 0) Console.WriteLine("  清理空目录：" + removedDirs.ToString("N0") + " 个");
            Console.WriteLine("  释放空间：" + FormatBytes(freedBytes));

            int lockedTotal = 0;
            foreach (Group g in groups) if (!g.Skipped) lockedTotal += g.Locked;
            if (lockedTotal > 0)
                Console.WriteLine("  已跳过：" + lockedTotal.ToString("N0") + " 个正在使用或无权限的文件（下次运行可再清理）");

            log.Add(new string('-', 60));
            log.Add("删除文件: " + freedFiles + " 个, 清理空目录: " + removedDirs + " 个, 释放: " + freedBytes + " 字节, 跳过: " + lockedTotal + " 个");

            string logPath = WriteLog(log);
            if (logPath != null)
            {
                Console.WriteLine();
                Console.WriteLine("  操作记录已保存到：");
                Console.WriteLine("  " + logPath);
            }

            WaitKey();
            return 0;
        }

        #region 扫描与清理

        private static List<Group> BuildGroups(Options o)
        {
            var list = new List<Group>();

            if (!string.IsNullOrEmpty(o.TestRoot))
            {
                string root;
                try { root = Path.GetFullPath(o.TestRoot); }
                catch { root = o.TestRoot; }
                list.Add(new Group { Name = "测试目录", Path = root });
                return list;
            }

            list.Add(new Group { Name = "用户临时文件", Path = TrimEnd(Path.GetTempPath()) });

            string winTemp = null;
            try { winTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"); }
            catch { }
            if (!string.IsNullOrEmpty(winTemp))
                list.Add(new Group { Name = "Windows 临时文件", Path = winTemp, NeedsAdmin = true });

            if (!o.SkipRecycleBin)
                list.Add(new Group { Name = "回收站", IsRecycleBin = true });

            return list;
        }

        private static void Scan(Group g, Options o)
        {
            if (g.IsRecycleBin)
            {
                SHQUERYRBINFO info = new SHQUERYRBINFO();
                info.cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO));
                int hr = SHQueryRecycleBin(null, ref info);
                if (hr == 0)
                {
                    g.Bytes = info.i64Size;
                    g.RecycledItems = info.i64NumItems;
                }
                else
                {
                    g.Skipped = true;
                    g.SkipReason = "无法读取回收站（HRESULT 0x" + hr.ToString("X8") + "）";
                }
                return;
            }

            if (!Directory.Exists(g.Path))
            {
                g.Skipped = true;
                g.SkipReason = "目录不存在";
                return;
            }

            if (g.NeedsAdmin && !Elevated)
            {
                g.Skipped = true;
                g.SkipReason = "需要管理员权限（右键 → 以管理员身份运行）";
                return;
            }

            DateTime cutoff = o.Days > 0 ? DateTime.Now.AddDays(-o.Days) : DateTime.MinValue;

            var stack = new Stack<string>();
            stack.Push(g.Path);

            while (stack.Count > 0)
            {
                string dir = stack.Pop();

                if (IsForbiddenDir(dir)) continue;

                try
                {
                    foreach (string sub in Directory.GetDirectories(dir))
                    {
                        FileAttributes sa;
                        try { sa = File.GetAttributes(sub); }
                        catch { continue; }
                        if ((sa & FileAttributes.ReparsePoint) != 0) continue; // 不跟随联接点，避免跑到别处删文件
                        stack.Push(sub);
                    }
                }
                catch (UnauthorizedAccessException) { g.Locked++; }
                catch (IOException) { }

                string[] files;
                try { files = Directory.GetFiles(dir); }
                catch (UnauthorizedAccessException) { g.Locked++; continue; }
                catch (IOException) { continue; }

                foreach (string file in files) Consider(g, file, cutoff);
            }
        }

        private static void Consider(Group g, string file, DateTime cutoff)
        {
            if (SelfPath.Length > 0 &&
                string.Equals(file, SelfPath, StringComparison.OrdinalIgnoreCase)) return;

            FileInfo fi;
            try { fi = new FileInfo(file); }
            catch { return; }

            if (!fi.Exists) return;

            FileAttributes attrs;
            try { attrs = fi.Attributes; }
            catch { return; }

            if ((attrs & FileAttributes.System) != 0) { g.SystemSkipped++; return; }
            if (cutoff != DateTime.MinValue && fi.LastWriteTime > cutoff) { g.TooNew++; return; }

            g.Files.Add(file);
            g.Bytes += fi.Length;
        }

        private static bool IsForbiddenDir(string dir)
        {
            try
            {
                string name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar));
                if (string.Equals(name, "System Volume Information", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(name, "$Recycle.Bin", StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }
            catch { return false; }
        }

        private static int PruneEmptyDirs(string root, List<string> log)
        {
            if (!Directory.Exists(root)) return 0;

            var dirs = new List<string>();
            var stack = new Stack<string>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                string dir = stack.Pop();
                dirs.Add(dir);
                try
                {
                    foreach (string sub in Directory.GetDirectories(dir))
                    {
                        FileAttributes sa;
                        try { sa = File.GetAttributes(sub); }
                        catch { continue; }
                        if ((sa & FileAttributes.ReparsePoint) != 0) continue;
                        stack.Push(sub);
                    }
                }
                catch { }
            }

            dirs.Sort(delegate (string a, string b) { return b.Length.CompareTo(a.Length); });

            int removed = 0;
            foreach (string dir in dirs)
            {
                if (string.Equals(dir, root, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    if (Directory.GetFileSystemEntries(dir).Length == 0)
                    {
                        Directory.Delete(dir, false);
                        removed++;
                        log.Add("删除空目录 " + dir);
                    }
                }
                catch { }
            }
            return removed;
        }

        #endregion

        #region 输出

        private static void PrintHeader(Options o)
        {
            Console.WriteLine("  ════════════════════════════════════════════");
            Console.WriteLine("   " + AppName + "  v" + AppVersion);
            Console.WriteLine("  ════════════════════════════════════════════");
            Console.WriteLine("  安全规则：");
            Console.WriteLine("   · 只清理系统临时目录和回收站");
            Console.WriteLine("   · 不删除文档、图片、视频、下载、桌面文件");
            Console.WriteLine("   · 不删除程序目录、系统日志(.log)、备份(.old)、预读取缓存");
            Console.WriteLine("   · 不跟随目录联接，不会误闯用户目录");
            Console.WriteLine("   · 只清理 " + (o.Days > 0 ? o.Days + " 天以前" : "所有时间") +
                              "的临时文件，正在使用的会自动跳过");
            Console.WriteLine("   · 默认先预览，确认后才真正删除");
            if (!Elevated)
                Console.WriteLine("   · 当前未以管理员身份运行，Windows 临时文件将被跳过");
            Console.WriteLine();
        }

        private static void PrintScanResult(List<Group> groups, Options o)
        {
            int index = 0;
            foreach (Group g in groups)
            {
                index++;
                Console.WriteLine("  [" + index + "] " + g.Name);

                if (g.Skipped)
                {
                    Console.WriteLine("      跳过：" + g.SkipReason);
                    Console.WriteLine();
                    continue;
                }

                if (g.IsRecycleBin)
                {
                    Console.WriteLine("      路径：回收站（所有驱动器）");
                    Console.WriteLine("      可清理：" + g.RecycledItems.ToString("N0") + " 项 / " + FormatBytes(g.Bytes));
                    Console.WriteLine();
                    continue;
                }

                Console.WriteLine("      路径：" + g.Path);
                Console.WriteLine("      可清理：" + Pad(g.Files.Count.ToString("N0") + " 个文件", 22) +
                                  FormatBytes(g.Bytes));

                var notes = new List<string>();
                if (g.TooNew > 0) notes.Add(g.TooNew.ToString("N0") + " 个较新");
                if (g.SystemSkipped > 0) notes.Add(g.SystemSkipped.ToString("N0") + " 个系统文件");
                if (g.Locked > 0) notes.Add(g.Locked.ToString("N0") + " 个无权限");
                if (notes.Count > 0)
                    Console.WriteLine("      已跳过：" + string.Join("，", notes.ToArray()));
                Console.WriteLine();
            }
        }

        private static bool Confirm(string question, bool defaultYes)
        {
            while (true)
            {
                Console.Write("  " + question + (defaultYes ? " (Y/n) " : " (y/N) "));
                ConsoleKeyInfo k;
                try { k = Console.ReadKey(true); }
                catch { Console.WriteLine(); return false; }

                if (k.Key == ConsoleKey.Y) { Console.WriteLine("Y"); return true; }
                if (k.Key == ConsoleKey.N || k.Key == ConsoleKey.Escape) { Console.WriteLine("N"); return false; }
                if (k.Key == ConsoleKey.Enter) { Console.WriteLine(defaultYes ? "Y" : "N"); return defaultYes; }
            }
        }

        private static string WriteLog(List<string> lines)
        {
            // 依次尝试：%LOCALAPPDATA%\Co_cofe系统垃圾清理 → 程序所在目录 → %TEMP%\Co_cofe系统垃圾清理
            var candidates = new List<string>();

            try
            {
                string lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(lad)) candidates.Add(Path.Combine(lad, "Co_cofe系统垃圾清理"));
            }
            catch { }

            try
            {
                string exeDir = Path.GetDirectoryName(SelfPath);
                if (!string.IsNullOrEmpty(exeDir)) candidates.Add(exeDir);
            }
            catch { }

            try
            {
                string tmp = Path.GetTempPath();
                if (!string.IsNullOrEmpty(tmp)) candidates.Add(Path.Combine(tmp, "Co_cofe系统垃圾清理"));
            }
            catch { }

            bool debug = Environment.GetEnvironmentVariable("LJSAFE_LOG_DEBUG") == "1";

            foreach (string dir in candidates)
            {
                try
                {
                    Directory.CreateDirectory(dir);
                    string file = Path.Combine(dir, "cleanup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
                    File.WriteAllLines(file, lines.ToArray(), new UTF8Encoding(true));
                    return file;
                }
                catch (Exception ex)
                {
                    if (debug) Console.Error.WriteLine("日志写入失败(" + dir + "): " + ex.GetType().Name + ": " + ex.Message);
                }
            }
            return null;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= GB) return (bytes / (double)GB).ToString("0.00") + " GB";
            if (bytes >= MB) return (bytes / (double)MB).ToString("0.00") + " MB";
            if (bytes >= KB) return (bytes / (double)KB).ToString("0.0") + " KB";
            return bytes + " B";
        }

        private static string TrimEnd(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static int DisplayWidth(string s)
        {
            int w = 0;
            foreach (char c in s)
            {
                if (c >= 0x1100 && (
                    c <= 0x115F || c == 0x2329 || c == 0x232A ||
                    (c >= 0x2E80 && c <= 0xA4CF) ||
                    (c >= 0xAC00 && c <= 0xD7A3) ||
                    (c >= 0xF900 && c <= 0xFAFF) ||
                    (c >= 0xFE30 && c <= 0xFE6F) ||
                    (c >= 0xFF00 && c <= 0xFF60) ||
                    (c >= 0xFFE0 && c <= 0xFFE6))) w += 2;
                else w += 1;
            }
            return w;
        }

        private static string Pad(string s, int width)
        {
            int gap = width - DisplayWidth(s);
            return gap > 0 ? s + new string(' ', gap) : s + " ";
        }

        private static void PrintHelp()
        {
            Console.WriteLine();
            Console.WriteLine("  " + AppName + "  v" + AppVersion);
            Console.WriteLine();
            Console.WriteLine("  用法： Co_cofe系统垃圾清理.exe [选项]");
            Console.WriteLine();
            Console.WriteLine("    --dry-run, -n     只预览，不删除任何内容");
            Console.WriteLine("    --yes, -y         跳过确认，直接清理（可配 --dry-run 做自动检查）");
            Console.WriteLine("    --days N          只清理 N 天以前的文件，默认 1；0 表示不限时间");
            Console.WriteLine("    --keep-recycle    不处理回收站");
            Console.WriteLine("    --help, -h        显示本帮助");
            Console.WriteLine();
            Console.WriteLine("  清理范围：");
            Console.WriteLine("    %TEMP%                  当前用户临时文件");
            Console.WriteLine("    %WINDIR%\\Temp           Windows 临时文件（需管理员）");
            Console.WriteLine("    回收站                  所有驱动器的回收站");
            Console.WriteLine();
            Console.WriteLine("  示例：");
            Console.WriteLine("    Co_cofe系统垃圾清理.exe --dry-run      先看看会删什么");
            Console.WriteLine("    Co_cofe系统垃圾清理.exe --days 3       只清 3 天前的临时文件");
            Console.WriteLine();
        }

        private static void WaitKey()
        {
            try
            {
                if (Console.IsInputRedirected) return;
                Console.WriteLine();
                Console.Write("  按任意键退出...");
                Console.ReadKey(true);
                Console.WriteLine();
            }
            catch { }
        }

        #endregion

        #region 参数

        private static Options ParseArgs(string[] args)
        {
            Options o = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                switch (a.ToLowerInvariant())
                {
                    case "--dry-run":
                    case "-n":
                        o.DryRun = true;
                        break;
                    case "--yes":
                    case "-y":
                        o.AssumeYes = true;
                        break;
                    case "--keep-recycle":
                        o.SkipRecycleBin = true;
                        break;
                    case "--help":
                    case "-h":
                    case "/?":
                        o.Help = true;
                        break;
                    case "--days":
                        if (i + 1 >= args.Length)
                            throw new ArgumentException("--days 需要一个数字，例如 --days 3");
                        int days;
                        if (!int.TryParse(args[++i], out days) || days < 0)
                            throw new ArgumentException("--days 的取值必须是不小于 0 的整数");
                        o.Days = days;
                        break;
                    case "--test-root":
                        if (i + 1 >= args.Length)
                            throw new ArgumentException("--test-root 需要一个目录路径");
                        o.TestRoot = args[++i];
                        break;
                    default:
                        throw new ArgumentException("不认识的参数：" + a);
                }
            }
            return o;
        }

        private static bool IsAdmin()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        #endregion
    }
}
