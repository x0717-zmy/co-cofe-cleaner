using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Co_cofe 系统垃圾清理")]
[assembly: AssemblyDescription("只清理临时文件与回收站，不触碰文档、程序与日志")]
[assembly: AssemblyProduct("Co_cofe 系统垃圾清理")]
[assembly: AssemblyCompany("冷咖啡制作")]
[assembly: AssemblyCopyright("冷咖啡制作")]
[assembly: AssemblyTrademark("Co_cofe")]
[assembly: AssemblyVersion("1.3.1.0")]
[assembly: AssemblyFileVersion("1.3.1.0")]

namespace CoCofeClean
{
    // ------------------------------------------------------------ 主题
    internal static class Theme
    {
        public static float Scale = 1f;

        public static int S(float v) { return (int)Math.Round(v * Scale); }
        public static float SF(float v) { return v * Scale; }

        public static readonly Color BgTop = Color.FromArgb(255, 253, 244, 249);
        public static readonly Color BgBottom = Color.FromArgb(255, 248, 236, 244);
        public static readonly Color Ink = Color.FromArgb(255, 56, 34, 66);
        public static readonly Color Ink2 = Color.FromArgb(255, 112, 82, 124);
        public static readonly Color Ink3 = Color.FromArgb(255, 154, 128, 162);
        public static readonly Color Accent = Color.FromArgb(255, 226, 62, 124);
        public static readonly Color Accent2 = Color.FromArgb(255, 250, 132, 174);
        public static readonly Color AccentSoft = Color.FromArgb(255, 253, 228, 238);
        public static readonly Color Card = Color.FromArgb(255, 255, 255, 255);
        public static readonly Color Edge = Color.FromArgb(255, 241, 226, 236);
        public static readonly Color EdgeHover = Color.FromArgb(255, 246, 190, 214);
        public static readonly Color Green = Color.FromArgb(255, 48, 168, 104);

        public static readonly string Family = "Microsoft YaHei UI";

        public static Font F(float pt, FontStyle style)
        {
            return new Font(Family, pt, style, GraphicsUnit.Point);
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = rad * 2f;
            if (d > r.Height) d = r.Height;
            if (d > r.Width) d = r.Width;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, RectangleF r, float rad, Color c)
        {
            using (GraphicsPath p = Round(r, rad))
            using (SolidBrush b = new SolidBrush(c)) g.FillPath(b, p);
        }

        public static void StrokeRound(Graphics g, RectangleF r, float rad, Color c, float w)
        {
            using (GraphicsPath p = Round(r, rad))
            using (Pen pen = new Pen(c, w)) g.DrawPath(pen, p);
        }

        public static void DrawText(Graphics g, string s, Font f, Color c, RectangleF r,
                                    StringFormat sf)
        {
            using (SolidBrush b = new SolidBrush(c)) g.DrawString(s, f, b, r, sf);
        }

        public static StringFormat Fmt(ContentAlignment a, bool ellipsis)
        {
            StringFormat sf = new StringFormat();
            sf.LineAlignment = a == ContentAlignment.MiddleLeft || a == ContentAlignment.TopLeft ||
                               a == ContentAlignment.BottomLeft ? StringAlignment.Center :
                               (a == ContentAlignment.MiddleRight || a == ContentAlignment.TopRight ||
                                a == ContentAlignment.BottomRight ? StringAlignment.Center : StringAlignment.Center);
            if (a == ContentAlignment.MiddleLeft || a == ContentAlignment.TopLeft || a == ContentAlignment.BottomLeft)
                sf.Alignment = StringAlignment.Near;
            else if (a == ContentAlignment.MiddleRight || a == ContentAlignment.TopRight || a == ContentAlignment.BottomRight)
                sf.Alignment = StringAlignment.Far;
            else sf.Alignment = StringAlignment.Center;

            if (a == ContentAlignment.TopLeft || a == ContentAlignment.TopCenter || a == ContentAlignment.TopRight)
                sf.LineAlignment = StringAlignment.Near;
            else if (a == ContentAlignment.BottomLeft || a == ContentAlignment.BottomCenter || a == ContentAlignment.BottomRight)
                sf.LineAlignment = StringAlignment.Far;
            else sf.LineAlignment = StringAlignment.Center;

            if (ellipsis) sf.Trimming = StringTrimming.EllipsisCharacter;
            sf.FormatFlags = StringFormatFlags.NoWrap;
            return sf;
        }
    }

    // ------------------------------------------------------------ 数据
    internal sealed class Target
    {
        public string Name;
        public string Path;
        public bool NeedsAdmin;
        public bool IsRecycle;

        public bool Enabled = true;
        public bool Skipped;
        public string SkipReason = "";

        public readonly List<string> Files = new List<string>();
        public long Bytes;
        public long Items;
        public int TooNew;
        public int Locked;
        public int Deleted;
        public long DeletedBytes;
        public string Glyph = "\uE8B7";
        public string Hint = "";

        public string SizeText
        {
            get
            {
                if (Skipped) return "跳过";
                if (IsRecycle) return Items > 0 ? Engine.FormatBytes(Bytes) : "空";
                return Files.Count > 0 ? Engine.FormatBytes(Bytes) : "干净";
            }
        }

        public string DetailText
        {
            get
            {
                if (Skipped) return SkipReason;
                if (IsRecycle) return Items > 0 ? Items.ToString("N0") + " 项" : "回收站是空的";
                string s = Files.Count.ToString("N0") + " 个文件";
                if (TooNew > 0) s += " · 跳过 " + TooNew.ToString("N0") + " 个较新";
                return s;
            }
        }
    }

    // ------------------------------------------------------------ 引擎
    internal static class Engine
    {
        private const uint SHERB_NOCONFIRMATION = 0x1;
        private const uint SHERB_NOPROGRESS = 0x2;
        private const uint SHERB_NOUI = 0x4;

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

        public static string SelfPath;
        public static bool Elevated;

        public static bool IsAdmin()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        public static List<Target> BuildTargets()
        {
            List<Target> list = new List<Target>();

            // 调试用：设置 LJSAFE_TEST_ROOT 后只扫这个目录，不碰真实目标
            string test = Environment.GetEnvironmentVariable("LJSAFE_TEST_ROOT");
            if (!string.IsNullOrEmpty(test) && Directory.Exists(test))
            {
                Target tt = new Target();
                tt.Name = "测试目录";
                tt.Path = test;
                tt.Glyph = "\uE8B7";
                list.Add(tt);
                return list;
            }

            Target user = new Target();
            user.Name = "用户临时文件";
            user.Path = TrimEnd(System.IO.Path.GetTempPath());
            user.Glyph = "\uE8B7";
            list.Add(user);

            string winTemp = null;
            try { winTemp = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"); }
            catch { }
            if (winTemp != null && winTemp.Length > 0)
            {
                Target win = new Target();
                win.Name = "Windows 临时文件";
                win.Path = winTemp;
                win.NeedsAdmin = true;
                win.Glyph = "\uE8B7";
                list.Add(win);
            }

            Target rec = new Target();
            rec.Name = "回收站";
            rec.Path = "所有驱动器";
            rec.IsRecycle = true;
            rec.Glyph = "\uE74D";
            list.Add(rec);

            return list;
        }

        public static string TrimEnd(string p)
        {
            if (string.IsNullOrEmpty(p)) return p;
            return p.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        }

        public static void Scan(Target g, int days, ref int scanned)
        {
            g.Files.Clear();
            g.Bytes = 0;
            g.Items = 0;
            g.TooNew = 0;
            g.Locked = 0;
            g.Skipped = false;
            g.SkipReason = "";

            if (g.IsRecycle)
            {
                SHQUERYRBINFO info = new SHQUERYRBINFO();
                info.cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO));
                int hr = SHQueryRecycleBin(null, ref info);
                if (hr == 0) { g.Bytes = info.i64Size; g.Items = info.i64NumItems; }
                else { g.Skipped = true; g.SkipReason = "无法读取回收站"; }
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
                g.SkipReason = "需要管理员权限";
                g.Enabled = false;
                return;
            }

            DateTime cutoff = days > 0 ? DateTime.Now.AddDays(-days) : DateTime.MinValue;
            Stack<string> stack = new Stack<string>();
            stack.Push(g.Path);

            while (stack.Count > 0)
            {
                string dir = stack.Pop();
                string leaf = "";
                try { leaf = System.IO.Path.GetFileName(dir.TrimEnd(System.IO.Path.DirectorySeparatorChar)); }
                catch { }
                if (string.Equals(leaf, "System Volume Information", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(leaf, "$Recycle.Bin", StringComparison.OrdinalIgnoreCase)) continue;

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
                catch (UnauthorizedAccessException) { g.Locked++; }
                catch (IOException) { }

                string[] files;
                try { files = Directory.GetFiles(dir); }
                catch (UnauthorizedAccessException) { g.Locked++; continue; }
                catch (IOException) { continue; }

                foreach (string file in files)
                {
                    if (SelfPath.Length > 0 &&
                        string.Equals(file, SelfPath, StringComparison.OrdinalIgnoreCase)) continue;

                    FileInfo fi;
                    try { fi = new FileInfo(file); }
                    catch { continue; }
                    if (!fi.Exists) continue;

                    FileAttributes attrs;
                    try { attrs = fi.Attributes; }
                    catch { continue; }
                    if ((attrs & FileAttributes.System) != 0) continue;
                    if (cutoff != DateTime.MinValue && fi.LastWriteTime > cutoff) { g.TooNew++; continue; }

                    g.Files.Add(file);
                    g.Bytes += fi.Length;
                    scanned++;
                }
            }
        }

        public static void Clean(Target g, List<string> log, ref int progress, int progressTotal)
        {
            if (g.IsRecycle)
            {
                if (g.Items <= 0) return;
                int hr = SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESS | SHERB_NOUI);
                if (hr == 0)
                {
                    g.Deleted = (int)Math.Min(int.MaxValue, g.Items);
                    g.DeletedBytes = g.Bytes;
                    log.Add("清空回收站: " + g.Items + " 项 / " + g.Bytes + " 字节");
                }
                else log.Add("清空回收站失败, HRESULT 0x" + hr.ToString("X8"));
                progress += progressTotal;
                return;
            }

            foreach (string file in g.Files)
            {
                try
                {
                    FileInfo fi = new FileInfo(file);
                    if (!fi.Exists) continue;
                    long len = fi.Length;
                    FileAttributes attrs = fi.Attributes;
                    if ((attrs & FileAttributes.ReadOnly) != 0)
                        File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
                    File.Delete(file);
                    g.Deleted++;
                    g.DeletedBytes += len;
                    log.Add("已删除  " + file);
                }
                catch (UnauthorizedAccessException) { g.Locked++; log.Add("跳过(无权限) " + file); }
                catch (IOException) { g.Locked++; log.Add("跳过(被占用) " + file); }
                catch (Exception ex) { g.Locked++; log.Add("跳过(" + ex.GetType().Name + ") " + file); }
                progress++;
            }

            int removed = PruneEmptyDirs(g.Path, log);
            if (removed > 0) log.Add("清理空目录 " + removed + " 个");
        }

        private static int PruneEmptyDirs(string root, List<string> log)
        {
            if (!Directory.Exists(root)) return 0;
            List<string> dirs = new List<string>();
            Stack<string> stack = new Stack<string>();
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
            dirs.Sort(delegate(string a, string b) { return b.Length.CompareTo(a.Length); });
            int removed = 0;
            foreach (string d in dirs)
            {
                if (string.Equals(d, root, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    if (Directory.GetFileSystemEntries(d).Length == 0) { Directory.Delete(d, false); removed++; }
                }
                catch { }
            }
            return removed;
        }

        public static string FormatBytes(long b)
        {
            const long KB = 1024, MB = 1024 * 1024, GB = 1024L * 1024 * 1024;
            if (b >= GB) return (b / (double)GB).ToString("0.00") + " GB";
            if (b >= MB) return (b / (double)MB).ToString("0.00") + " MB";
            if (b >= KB) return (b / (double)KB).ToString("0.0") + " KB";
            return b + " B";
        }

        public static string WriteLog(List<string> lines)
        {
            List<string> candidates = new List<string>();
            try
            {
                string lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(lad)) candidates.Add(System.IO.Path.Combine(lad, "Co_cofe系统垃圾清理"));
            }
            catch { }
            try
            {
                string exeDir = SelfPath == null ? null : System.IO.Path.GetDirectoryName(SelfPath);
                if (!string.IsNullOrEmpty(exeDir)) candidates.Add(exeDir);
            }
            catch { }
            try
            {
                string tmp = System.IO.Path.GetTempPath();
                if (!string.IsNullOrEmpty(tmp)) candidates.Add(System.IO.Path.Combine(tmp, "Co_cofe系统垃圾清理"));
            }
            catch { }

            foreach (string dir in candidates)
            {
                try
                {
                    Directory.CreateDirectory(dir);
                    string file = System.IO.Path.Combine(dir, "cleanup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
                    File.WriteAllLines(file, lines.ToArray(), new UTF8Encoding(true));
                    return file;
                }
                catch { }
            }
            return null;
        }
    }

    // ------------------------------------------------------------ 胶囊按钮
    internal sealed class PillButton : Control
    {
        public bool Primary = true;
        private bool hover, down;

        public PillButton(string text, bool primary)
        {
            Primary = primary;
            Text = text;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Font = Theme.F(10f, FontStyle.Bold);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;

            RectangleF r = new RectangleF(0, down ? Theme.SF(1) : 0, Width - 1, Height - 1);
            float rad = r.Height / 2f;

            if (!Enabled)
            {
                Theme.FillRound(g, r, rad, Color.FromArgb(255, 243, 234, 240));
                Theme.DrawText(g, Text, Font, Color.FromArgb(255, 190, 170, 186), r, Theme.Fmt(ContentAlignment.MiddleCenter, false));
                return;
            }

            if (Primary)
            {
                Color c1 = hover ? Color.FromArgb(255, 236, 84, 144) : Theme.Accent;
                Color c2 = hover ? Color.FromArgb(255, 253, 150, 188) : Theme.Accent2;
                using (GraphicsPath p = Theme.Round(r, rad))
                using (LinearGradientBrush b = new LinearGradientBrush(
                    new PointF(r.X, r.Y), new PointF(r.X, r.Bottom), c1, c2))
                    g.FillPath(b, p);
                Theme.DrawText(g, Text, Font, Color.White, r, Theme.Fmt(ContentAlignment.MiddleCenter, false));
            }
            else
            {
                Theme.FillRound(g, r, rad, hover ? Theme.AccentSoft : Color.White);
                Theme.StrokeRound(g, r, rad, hover ? Theme.EdgeHover : Theme.Edge, Theme.SF(1f));
                Theme.DrawText(g, Text, Font, Theme.Accent, r, Theme.Fmt(ContentAlignment.MiddleCenter, false));
            }
        }
    }

    // ------------------------------------------------------------ 天数分段
    internal sealed class DayPills : Control
    {
        private readonly int[] values = new int[] { 1, 3, 7, 0 };
        private readonly string[] labels = new string[] { "1 天", "3 天", "7 天", "全部" };
        public int Value = 1;
        private int hoverIndex = -1;

        public event EventHandler Changed;

        public DayPills()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Font = Theme.F(8.5f, FontStyle.Regular);
        }

        private int SegmentWidth { get { return Width / values.Length; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;

            RectangleF track = new RectangleF(0, 0, Width - 1, Height - 1);
            Theme.FillRound(g, track, Height / 2f, Color.FromArgb(255, 249, 240, 246));

            int sw = SegmentWidth;
            for (int i = 0; i < values.Length; i++)
            {
                RectangleF seg = new RectangleF(i * sw + Theme.SF(1), Theme.SF(2), sw - Theme.SF(2), Height - Theme.SF(4));
                bool sel = values[i] == Value;
                if (sel) Theme.FillRound(g, seg, seg.Height / 2f, Color.White);
                else if (i == hoverIndex) Theme.FillRound(g, seg, seg.Height / 2f, Color.FromArgb(90, 255, 255, 255));
                Theme.DrawText(g, labels[i], Font, sel ? Theme.Accent : Theme.Ink3, seg,
                               Theme.Fmt(ContentAlignment.MiddleCenter, false));
            }

            if (Value >= 0)
            {
                int idx = 0;
                for (int i = 0; i < values.Length; i++) if (values[i] == Value) idx = i;
                RectangleF seg = new RectangleF(idx * sw + Theme.SF(1), Theme.SF(2), sw - Theme.SF(2), Height - Theme.SF(4));
                Theme.StrokeRound(g, seg, seg.Height / 2f, Color.FromArgb(120, 246, 190, 214), Theme.SF(1f));
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = e.X / Math.Max(1, SegmentWidth);
            if (i != hoverIndex) { hoverIndex = i; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hoverIndex = -1; Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int i = e.X / Math.Max(1, SegmentWidth);
            if (i >= 0 && i < values.Length)
            {
                Value = values[i];
                Invalidate();
                if (Changed != null) Changed(this, EventArgs.Empty);
            }
            base.OnMouseDown(e);
        }
    }

    // ------------------------------------------------------------ 分类行
    internal sealed class CatRow : Control
    {
        public Target T;
        private bool hover;

        public event EventHandler Toggled;
        public event EventHandler ElevateRequested;

        public CatRow(Target t)
        {
            T = t;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (!T.Skipped)
            {
                T.Enabled = !T.Enabled;
                Invalidate();
                if (Toggled != null) Toggled(this, EventArgs.Empty);
            }
            else if (T.NeedsAdmin && ElevateRequested != null)
            {
                ElevateRequested(this, EventArgs.Empty);
            }
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            if (Theme.Scale > 1.2f) g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            RectangleF card = new RectangleF(0, 0, Width - 1, Height - 1);
            float rad = Theme.SF(14);
            bool active = !T.Skipped && T.Enabled;

            Theme.FillRound(g, card, rad, Color.White);
            Theme.StrokeRound(g, card, rad, hover && !T.Skipped ? Theme.EdgeHover : Theme.Edge, Theme.SF(1f));

            int alpha = T.Skipped ? 110 : (T.Enabled ? 255 : 130);

            // 勾选圆
            float cs = Theme.SF(22);
            RectangleF box = new RectangleF(Theme.SF(18), (Height - cs) / 2f, cs, cs);
            if (T.Enabled && !T.Skipped)
            {
                Theme.FillRound(g, box, cs / 2f, Theme.Accent);
                using (Pen p = new Pen(Color.White, Theme.SF(2.2f)))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                    g.DrawLines(p, new PointF[] {
                        new PointF(box.X + cs * 0.26f, box.Y + cs * 0.52f),
                        new PointF(box.X + cs * 0.45f, box.Y + cs * 0.70f),
                        new PointF(box.X + cs * 0.76f, box.Y + cs * 0.32f)
                    });
                }
            }
            else
            {
                using (Pen p = new Pen(Color.FromArgb(160, Theme.Ink3), Theme.SF(1.6f)))
                    g.DrawEllipse(p, box.X, box.Y, cs, cs);
            }

            // 图标字形
            float gx = box.Right + Theme.SF(14);
            using (Font gf = new Font("Segoe MDL2 Assets", 13f, FontStyle.Regular, GraphicsUnit.Point))
            using (SolidBrush gb = new SolidBrush(Color.FromArgb(alpha, Theme.Accent)))
                g.DrawString(T.Glyph, gf, gb, new RectangleF(gx, 0, Theme.SF(28), Height), Theme.Fmt(ContentAlignment.MiddleLeft, false));

            // 名称 + 详情
            float tx = gx + Theme.SF(32);
            float tw = Width - tx - Theme.SF(120);
            using (Font nf = Theme.F(10.5f, FontStyle.Bold))
                Theme.DrawText(g, T.Name, nf, Color.FromArgb(alpha, Theme.Ink),
                    new RectangleF(tx, Theme.SF(12), tw, Theme.SF(22)), Theme.Fmt(ContentAlignment.MiddleLeft, true));
            using (Font df = Theme.F(8.3f, FontStyle.Regular))
                Theme.DrawText(g, T.DetailText, df, Color.FromArgb((int)(alpha * 0.75), active ? Theme.Ink2 : Theme.Ink3),
                    new RectangleF(tx, Theme.SF(34), tw, Theme.SF(18)), Theme.Fmt(ContentAlignment.MiddleLeft, true));

            // 右侧大小
            using (Font sf2 = Theme.F(10.5f, FontStyle.Bold))
            {
                Color c = T.Skipped ? (T.NeedsAdmin ? Theme.Accent : Theme.Ink3) : (T.Enabled ? Theme.Ink : Theme.Ink3);
                Theme.DrawText(g, T.SizeText, sf2, Color.FromArgb(alpha, c),
                    new RectangleF(Width - Theme.SF(118), 0, Theme.SF(100), Height),
                    Theme.Fmt(ContentAlignment.MiddleRight, false));
            }
        }
    }

    // ------------------------------------------------------------ 主窗体
    internal sealed class CleanForm : Form
    {
        private enum Phase { Scanning, Ready, Cleaning, Done }

        private readonly List<Target> targets = new List<Target>();
        private readonly List<CatRow> rows = new List<CatRow>();
        private readonly List<string> log = new List<string>();

        private PillButton btnScan, btnClean, btnAdmin;
        private DayPills pills;

        private Phase phase = Phase.Scanning;
        private string status = "正在扫描…";
        private string summaryValue = "…";
        private string summaryFiles = "";
        private string summaryRecycle = "";

        private string logPath = "";
        private string lastResultText = "";

        private int progressDone, progressTotal;
        private bool busy;
        private Image logo;
        private ToolTip tips;
        private RectangleF rLogo, rTitle, rDivider, rSummary, rPills, rHint, rStatus, rBar, rVersion;
        private Font fTitle, fCap, fBig, fSmall, fTiny;

        public CleanForm()
        {
            Engine.SelfPath = Application.ExecutablePath;
            Engine.Elevated = Engine.IsAdmin();

            // 关键：控件位置全部由 Theme.S() 手工换算，禁止 WinForms 按字体再缩放一遍
            AutoScaleMode = AutoScaleMode.None;

            Text = "Co_cofe 系统垃圾清理";
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Theme.S(560), Theme.S(620));
            DoubleBuffered = true;
            BackColor = Theme.BgBottom;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("APP_LOGO"))
                    if (s != null) logo = Image.FromStream(s);
            }
            catch { }

            fTitle = Theme.F(17f, FontStyle.Bold);

            fCap = Theme.F(8.5f, FontStyle.Regular);
            fBig = Theme.F(25f, FontStyle.Bold);
            fSmall = Theme.F(9.5f, FontStyle.Bold);
            fTiny = Theme.F(8f, FontStyle.Regular);

            BuildUi();
            Shown += delegate { RunScan(); };
        }

        private void BuildUi()
        {
            targets.AddRange(Engine.BuildTargets());

            foreach (Target t in targets)
            {
                CatRow row = new CatRow(t);
                row.Toggled += delegate { UpdateSummary(); };
                Controls.Add(row);
                rows.Add(row);
            }

            tips = new ToolTip();
            tips.AutoPopDelay = 12000;
            tips.InitialDelay = 350;
            tips.ReshowDelay = 100;
            tips.ShowAlways = true;
            foreach (CatRow row in rows)
            {
                if (row.T.NeedsAdmin)
                    tips.SetToolTip(row, Engine.Elevated
                        ? "Windows 临时文件（已解锁，可勾选清理）"
                        : "Windows 临时文件受系统保护，需要管理员权限才能清理。\r\n点下方「以管理员身份运行」，重启后这行会自动变成可勾选。");
                else if (row.T.IsRecycle)
                    tips.SetToolTip(row, "清空所有驱动器的回收站（含 U 盘），删除后不可恢复");
                else
                    tips.SetToolTip(row, "当前用户的临时文件，程序退出后一般可以安全删除");
            }

            pills = new DayPills();
            pills.Changed += delegate { RunScan(); };
            Controls.Add(pills);

            btnScan = new PillButton("重新扫描", false);
            btnScan.Click += delegate { RunScan(); };
            Controls.Add(btnScan);

            btnAdmin = new PillButton(Engine.Elevated ? "已以管理员运行" : "以管理员身份运行", false);
            btnAdmin.Click += delegate
            {
                if (Engine.Elevated)
                    MessageBox.Show(this, "当前已经是管理员模式。\r\n关掉本窗口再重新打开，即可回到普通模式。",
                        "Co_cofe 系统垃圾清理", MessageBoxButtons.OK, MessageBoxIcon.Information);
                else
                    TryElevate();
            };
            Controls.Add(btnAdmin);

            btnClean = new PillButton("立即清理", true);
            btnClean.Click += delegate { DoClean(); };
            Controls.Add(btnClean);

            if (tips != null)
            {
                tips.SetToolTip(btnAdmin, Engine.Elevated
                    ? "已经是管理员模式；关掉窗口重新打开即可回到普通模式"
                    : "以管理员身份重新启动，解锁 Windows 临时文件");
                tips.SetToolTip(btnScan, "重新扫描当前勾选的分类");
                tips.SetToolTip(btnClean, "清理勾选分类里超出保留天数的文件");
            }

            LayoutAll();
            SetPhase(Phase.Scanning);

            // 最小尺寸 = 设计尺寸，防止缩到布局挤在一起
            try { MinimumSize = SizeFromClientSize(new Size(Theme.S(560), Theme.S(620))); } catch { }

            RestoreWindow();
        }

        // 所有控件尺寸都由 Theme.S() 决定，DPI 定下来后再调一次即可整体重排
        private void LayoutAll()
        {
            int W = ClientSize.Width;
            int H = ClientSize.Height;
            int m = Theme.S(24);
            int cw = W - m * 2;
            if (cw < Theme.S(300)) cw = Theme.S(300);

            rLogo = new RectangleF(m, Theme.S(14), Theme.S(56), Theme.S(56));
            rTitle = new RectangleF(m + Theme.S(70), Theme.S(14), cw - Theme.S(70), Theme.S(56));
            rDivider = new RectangleF(m, Theme.S(84), cw, 1);
            rSummary = new RectangleF(m, Theme.S(96), cw, Theme.S(92));

            // 分类行：跟着窗口宽度拉伸，高度固定
            int y = Theme.S(200);
            foreach (CatRow r in rows)
            {
                r.Bounds = new Rectangle(m, y, cw, Theme.S(66));
                y += Theme.S(66) + Theme.S(8);
            }

            // 天数选择紧跟分类行
rPills = new RectangleF(m + Theme.S(56), y + Theme.S(6), Theme.S(300), Theme.S(34));
            if (pills != null) pills.Bounds = Rectangle.Round(rPills);

            // 底部整体锚定在窗口底边，多出来的高度由中间空白吸收
            int bottom = H - Theme.S(20);
            rVersion = new RectangleF(W - m - Theme.S(166), bottom - Theme.S(16), Theme.S(166), Theme.S(16));
            rBar = new RectangleF(m, rVersion.Y - Theme.S(14), cw, Theme.S(5));
            rStatus = new RectangleF(m, rBar.Y - Theme.S(24), cw, Theme.S(20));
            rHint = new RectangleF(m, rStatus.Y - Theme.S(22), cw, Theme.S(16));

            int btnY = (int)rHint.Y - Theme.S(10) - Theme.S(46);
            int minY = y + Theme.S(48);
            if (btnY < minY) btnY = minY;

            int scanW = Theme.S(140), adminW = Theme.S(168), cleanW = Theme.S(180), gap = Theme.S(10);
            int scanX = m;
            int cleanX = W - m - cleanW;
            // 管理员按钮放在两个按钮中间，窗口窄时也不会互相压住
            int adminX = (scanX + scanW + cleanX - adminW) / 2;
            if (adminX < scanX + scanW + gap) adminX = scanX + scanW + gap;
            if (adminX + adminW > cleanX - gap) adminX = cleanX - gap - adminW;

            if (btnScan != null) btnScan.Bounds = new Rectangle(scanX, btnY, scanW, Theme.S(46));
            if (btnClean != null) btnClean.Bounds = new Rectangle(cleanX, btnY, cleanW, Theme.S(46));
            if (btnAdmin != null) btnAdmin.Bounds = new Rectangle(adminX, btnY, adminW, Theme.S(46));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            LayoutAll();
            Invalidate();
        }

        // 句柄建好后复查真实 DPI：万一进程不是 DPI 感知（提权、兼容性设置等），
        // 就用窗口 DC 的真实 DPI 重排一次，保证任何启动方式下界面一致
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                using (Graphics g = CreateGraphics())
                {
                    float dpi = g.DpiX / 96f;
                    if (dpi > 0.5f && Math.Abs(dpi - Theme.Scale) > 0.01f)
                    {
                        Theme.Scale = dpi;
                        LayoutAll();
                        Invalidate();
                    }
                }
            }
            catch { }
        }

        private void SetPhase(Phase p)
        {
            phase = p;
            if (btnScan != null) btnScan.Enabled = (p != Phase.Scanning && p != Phase.Cleaning);
            if (btnClean != null) btnClean.Enabled = (p == Phase.Ready);
            if (btnClean != null) btnClean.Text = p == Phase.Cleaning ? "清理中…" : (p == Phase.Done ? "再清一次" : "立即清理");
            if (btnAdmin != null)
            {
                btnAdmin.Text = Engine.Elevated ? "已以管理员运行" : "以管理员身份运行";
                btnAdmin.Enabled = p != Phase.Scanning && p != Phase.Cleaning;
            }
            if (pills != null) pills.Enabled = (p != Phase.Scanning && p != Phase.Cleaning);
            foreach (CatRow r in rows) r.Enabled = (p == Phase.Ready);
            Invalidate();
        }

        private void TryElevate()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath);
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process.Start(psi);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法以管理员身份重启：" + ex.Message, "Co_cofe 系统垃圾清理",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ---------------- 扫描 ----------------
        private void RunScan()
        {
            if (busy) return;
            busy = true;
            log.Clear();
            lastResultText = "";
            progressDone = 0;
            progressTotal = 0;
            status = "正在扫描…";
            summaryValue = "…";
            summaryFiles = "";
            summaryRecycle = "";
            SetPhase(Phase.Scanning);
            foreach (CatRow r in rows) r.Invalidate();

            int days = pills.Value;
            ThreadPool.QueueUserWorkItem(delegate
            {
                int scanned = 0;
                foreach (Target t in targets)
                {
                    try { Engine.Scan(t, days, ref scanned); }
                    catch (Exception ex) { t.Skipped = true; t.SkipReason = "扫描失败"; log.Add("扫描失败 " + t.Name + ": " + ex.Message); }
                }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        busy = false;
                        foreach (Target t in targets)
                            if (t.IsRecycle) t.Enabled = t.Items > 0;
                        status = "";
                        SetPhase(Phase.Ready);
                        UpdateSummary();
                    });
                }
                catch { }
            });
        }

        private void UpdateSummary()
        {
            long bytes = 0;
            long files = 0;
            long recItems = 0;
            foreach (Target t in targets)
            {
                if (t.Skipped || !t.Enabled) continue;
                bytes += t.Bytes;
                files += t.IsRecycle ? 0 : t.Files.Count;
                if (t.IsRecycle) recItems += t.Items;
            }
            summaryValue = Engine.FormatBytes(bytes);
            summaryFiles = files.ToString("N0") + " 个文件" + (recItems > 0 ? " · 回收站 " + recItems.ToString("N0") + " 项" : "");
            summaryRecycle = recItems > 0 ? recItems.ToString("N0") + " 项" : "";
            foreach (CatRow r in rows) r.Invalidate();
            if (btnClean != null && phase == Phase.Ready) btnClean.Enabled = bytes > 0;
            Invalidate();
        }

        // ---------------- 清理 ----------------
        private void DoClean()
        {
            if (busy) return;

            long bytes = 0;
            long recItems = 0;
            foreach (Target t in targets)
            {
                if (t.Skipped || !t.Enabled) continue;
                bytes += t.Bytes;
                if (t.IsRecycle) recItems += t.Items;
            }

            if (bytes <= 0)
            {
                MessageBox.Show(this, "没有需要清理的内容。", "Co_cofe 系统垃圾清理",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string msg = "即将清理 " + Engine.FormatBytes(bytes) + "。";
            if (recItems > 0) msg += "\r\n\r\n注意：回收站里的 " + recItems.ToString("N0") + " 项会被永久删除，无法恢复。";
            if (MessageBox.Show(this, msg + "\r\n\r\n继续吗？", "确认清理",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            busy = true;
            log.Clear();
            log.Add("Co_cofe 系统垃圾清理 v1.3.1   运行时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            log.Add("只清理 " + (pills.Value > 0 ? pills.Value + " 天前的文件" : "所有时间的临时文件") +
                    "   管理员: " + (Engine.Elevated ? "是" : "否"));
            log.Add(new string('-', 60));

            progressDone = 0;
            progressTotal = 0;
            foreach (Target t in targets)
            {
                if (t.Skipped || !t.Enabled) continue;
                progressTotal += t.IsRecycle ? 1 : Math.Max(1, t.Files.Count);
            }
            foreach (Target t in targets) { t.Deleted = 0; t.DeletedBytes = 0; }

            status = "正在清理…";
            SetPhase(Phase.Cleaning);

            List<Target> work = new List<Target>();
            foreach (Target t in targets) if (!t.Skipped && t.Enabled) work.Add(t);

            ThreadPool.QueueUserWorkItem(delegate
            {
                int progress = 0;
                int total = progressTotal;
                foreach (Target t in work)
                {
                    try { Engine.Clean(t, log, ref progress, total); }
                    catch (Exception ex) { log.Add("清理失败 " + t.Name + ": " + ex.Message); }
                    try
                    {
                        if (t.IsRecycle) progress += 1;
                        int pct = total <= 0 ? 100 : (int)(progress * 100L / total);
                        if (pct > 100) pct = 100;
                        int pid = progress;
                        BeginInvoke((MethodInvoker)delegate
                        {
                            progressDone = pid;
                            status = "正在清理 · " + t.Name;
                            Invalidate();
                        });
                    }
                    catch { }
                }

                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        progressDone = Math.Max(progressTotal, 1);
                        progressTotal = Math.Max(progressTotal, 1);
                        long freed = 0;
                        int filesDeleted = 0;
                        foreach (Target t in work) { freed += t.DeletedBytes; filesDeleted += t.Deleted; }
                        lastResultText = freed > 0
                            ? "清理完成 · 已释放 " + Engine.FormatBytes(freed) + "（" + filesDeleted.ToString("N0") + " 个文件/项目）"
                            : "没有可删除的文件";
                        log.Add(new string('-', 60));
                        log.Add("释放: " + freed + " 字节, 删除: " + filesDeleted + " 个");
                        logPath = Engine.WriteLog(log);
                        busy = false;
                        status = lastResultText;
                        SetPhase(Phase.Done);
                        RunRescanAfterClean();
                    });
                }
                catch { }
            });
        }

        private void RunRescanAfterClean()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                Thread.Sleep(120);
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        int days = pills.Value;
                        foreach (Target t in targets)
                        {
                            try
                            {
                                int scanned = 0;
                                Engine.Scan(t, days, ref scanned);
                            }
                            catch { }
                        }
                        foreach (Target t in targets)
                            if (t.IsRecycle) t.Enabled = t.Items > 0;
                        SetPhase(Phase.Ready);
                        UpdateSummary();
                        Invalidate();
                    });
                }
                catch { }
            });
        }

        // ---------------- 记住窗口位置/大小 ----------------
        private static string settingsPath;

        private static string SettingsFile()
        {
            if (settingsPath != null) return settingsPath;
            List<string> dirs = new List<string>();
            try
            {
                string lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(lad)) dirs.Add(System.IO.Path.Combine(lad, "Co_cofe系统垃圾清理"));
            }
            catch { }
            try
            {
                string exeDir = System.IO.Path.GetDirectoryName(Application.ExecutablePath);
                if (!string.IsNullOrEmpty(exeDir)) dirs.Add(exeDir);
            }
            catch { }
            foreach (string d in dirs)
            {
                try
                {
                    Directory.CreateDirectory(d);
                    settingsPath = System.IO.Path.Combine(d, "window.txt");
                    return settingsPath;
                }
                catch { }
            }
            return null;
        }

        private void SaveWindow()
        {
            try
            {
                string path = SettingsFile();
                if (path == null) return;
                bool max = WindowState == FormWindowState.Maximized;
                Rectangle r = max ? RestoreBounds : Bounds;
                if (r.Width <= 0 || r.Height <= 0) return;
                float sc = Theme.Scale <= 0f ? 1f : Theme.Scale;
                string text = string.Format("{0},{1},{2},{3},{4}",
                    (int)Math.Round(r.Left / sc), (int)Math.Round(r.Top / sc),
                    (int)Math.Round(r.Width / sc), (int)Math.Round(r.Height / sc),
                    max ? 1 : 0);
                File.WriteAllText(path, text, new UTF8Encoding(false));
            }
            catch { }
        }

        private void RestoreWindow()
        {
            try
            {
                string path = SettingsFile();
                if (path == null || !File.Exists(path)) return;
                string[] p = File.ReadAllText(path).Trim().Split(',');
                if (p.Length < 4) return;

                int l, t, w, h;
                if (!int.TryParse(p[0].Trim(), out l)) return;
                if (!int.TryParse(p[1].Trim(), out t)) return;
                if (!int.TryParse(p[2].Trim(), out w)) return;
                if (!int.TryParse(p[3].Trim(), out h)) return;
                bool max = p.Length > 4 && p[4].Trim() == "1";

                float sc = Theme.Scale <= 0f ? 1f : Theme.Scale;
                l = (int)Math.Round(l * sc); w = (int)Math.Round(w * sc);
                t = (int)Math.Round(t * sc); h = (int)Math.Round(h * sc);

                if (w < MinimumSize.Width) w = MinimumSize.Width;
                if (h < MinimumSize.Height) h = MinimumSize.Height;

                Rectangle r = new Rectangle(l, t, w, h);

                // 必须还有相当一部分落在某个屏幕上（防止拔掉显示器后窗口跑到屏幕外）
                Rectangle best = Rectangle.Empty;
                long bestArea = 0;
                foreach (Screen s in Screen.AllScreens)
                {
                    Rectangle inter = Rectangle.Intersect(s.WorkingArea, r);
                    long a = (long)inter.Width * inter.Height;
                    if (a > bestArea) { bestArea = a; best = s.WorkingArea; }
                }
                if (best.IsEmpty || bestArea * 4 < (long)r.Width * r.Height)
                {
                    StartPosition = FormStartPosition.CenterScreen;
                    return;
                }

                // 标题栏别跑出屏幕
                if (r.Top < best.Top) r.Y = best.Top;
                if (r.Bottom - Theme.S(40) > best.Bottom) r.Y = best.Bottom - Theme.S(40) - r.Height + Theme.S(40);
                if (r.Right - Theme.S(120) < best.Left) r.X = best.Left - Theme.S(120) + r.Width / 2;
                if (r.Left + Theme.S(120) > best.Right) r.X = best.Right - Theme.S(120) - r.Width / 2;

                Bounds = r;
                StartPosition = FormStartPosition.Manual;
                if (max) WindowState = FormWindowState.Maximized;
            }
            catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveWindow();
            base.OnFormClosing(e);
        }

        // ---------------- 绘制 ----------------
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;

            using (LinearGradientBrush bg = new LinearGradientBrush(
                new Point(0, 0), new Point(0, ClientSize.Height), Theme.BgTop, Theme.BgBottom))
                g.FillRectangle(bg, ClientRectangle);

            // 顶部粉色光晕
            using (GraphicsPath gp = new GraphicsPath())
            {
                float w = ClientSize.Width * 1.1f;
                gp.AddEllipse(ClientSize.Width / 2f - w / 2f, -Theme.SF(150), w, Theme.SF(220));
                using (PathGradientBrush pgb = new PathGradientBrush(gp))
                {
                    pgb.CenterColor = Color.FromArgb(40, 250, 150, 190);
                    pgb.SurroundColors = new Color[] { Color.FromArgb(0, 250, 150, 190) };
                    g.FillPath(pgb, gp);
                }
            }

            // 头部
            if (logo != null)
            {
                using (GraphicsPath clip = Theme.Round(rLogo, Theme.SF(14)))
                {
                    Region old = g.Clip;
                    g.SetClip(clip);
                    g.DrawImage(logo, rLogo);
                    g.Clip = old;
                }
                Theme.StrokeRound(g, rLogo, Theme.SF(14), Color.FromArgb(60, 120, 70, 120), Theme.SF(1f));
            }

            Theme.DrawText(g, "Co_cofe 系统垃圾清理", fTitle, Theme.Ink, rTitle,
                Theme.Fmt(ContentAlignment.MiddleLeft, false));

            using (Pen p = new Pen(Color.FromArgb(120, 240, 220, 232), Theme.SF(1f)))
                g.DrawLine(p, rDivider.X, rDivider.Y, rDivider.Right, rDivider.Y);

            // 汇总卡片
            RectangleF sum = rSummary;
            using (GraphicsPath sp = Theme.Round(new RectangleF(sum.X + Theme.SF(1), sum.Y + Theme.SF(3), sum.Width, sum.Height), Theme.SF(18)))
            using (SolidBrush sb = new SolidBrush(Color.FromArgb(16, 90, 40, 90)))
                g.FillPath(sb, sp);
            Theme.FillRound(g, sum, Theme.SF(18), Color.White);
            Theme.StrokeRound(g, sum, Theme.SF(18), Theme.Edge, Theme.SF(1f));

            Theme.DrawText(g, "可释放空间", fCap, Theme.Ink3,
                new RectangleF(sum.X + Theme.S(22), sum.Y + Theme.S(12), Theme.S(200), Theme.S(16)),
                Theme.Fmt(ContentAlignment.MiddleLeft, false));
            Theme.DrawText(g, summaryValue, fBig, Theme.Ink,
                new RectangleF(sum.X + Theme.S(20), sum.Y + Theme.S(28), Theme.S(260), Theme.S(46)),
                Theme.Fmt(ContentAlignment.MiddleLeft, false));
            if (summaryFiles.Length > 0)
                Theme.DrawText(g, summaryFiles, fTiny, Theme.Ink3,
                    new RectangleF(sum.Right - Theme.S(250), sum.Y + Theme.S(26), Theme.S(228), Theme.S(18)),
                    Theme.Fmt(ContentAlignment.MiddleRight, true));

            // 「只清理」标签
            Theme.DrawText(g, "只清理", fCap, Theme.Ink2,
                new RectangleF(rPills.X - Theme.S(56), rPills.Y + Theme.S(8), Theme.S(52), Theme.S(18)),
                Theme.Fmt(ContentAlignment.MiddleLeft, false));

            // 小提示：没提权时告诉用户 Windows 垃圾需要管理员权限
            if (!Engine.Elevated)
            {
                using (Font icf = new Font("Segoe MDL2 Assets", 8.5f, FontStyle.Regular, GraphicsUnit.Point))
                using (SolidBrush ib = new SolidBrush(Theme.Accent))
                    g.DrawString("\uE946", icf, ib,
                        new RectangleF(rHint.X, rHint.Y, Theme.S(16), Theme.S(16)),
                        Theme.Fmt(ContentAlignment.MiddleLeft, false));

                Theme.DrawText(g, "开启管理员模式才能清理 Windows 临时文件", fTiny, Theme.Ink3,
                    new RectangleF(rHint.X + Theme.S(16), rHint.Y, rHint.Width - Theme.S(16), Theme.S(16)),
                    Theme.Fmt(ContentAlignment.MiddleLeft, false));
            }

            // 状态（只在扫描/清理/完成时出现，空闲时不显示文字）
            if (status.Length > 0)
                Theme.DrawText(g, status, fSmall, Theme.Ink2, rStatus,
                    Theme.Fmt(ContentAlignment.MiddleLeft, true));

            RectangleF bar = rBar;
            Theme.FillRound(g, bar, Theme.SF(2.5f), Color.FromArgb(255, 238, 222, 232));
            float pct = 0f;
            if (progressTotal > 0) pct = Math.Min(1f, (float)progressDone / progressTotal);
            if (pct > 0.001f)
            {
                RectangleF fill = new RectangleF(bar.X, bar.Y, Math.Max(bar.Height, bar.Width * pct), bar.Height);
                using (GraphicsPath fp = Theme.Round(fill, Theme.SF(2.5f)))
                using (LinearGradientBrush lb = new LinearGradientBrush(
                    new PointF(fill.X, fill.Y), new PointF(fill.Right, fill.Y), Theme.Accent, Theme.Accent2))
                    g.FillPath(lb, fp);
            }

            // 页脚（只留版本号）
            Theme.DrawText(g, "v1.3.1 · 冷咖啡制作", fTiny, Theme.Ink3, rVersion,
                Theme.Fmt(ContentAlignment.MiddleRight, false));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (logo != null) logo.Dispose();
                if (fTitle != null) fTitle.Dispose();

                if (fCap != null) fCap.Dispose();
                if (fBig != null) fBig.Dispose();
                if (fSmall != null) fSmall.Dispose();
                if (fTiny != null) fTiny.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main()
        {
            try { SetProcessDPIAware(); } catch { }
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                    Theme.Scale = g.DpiX / 96f;
            }
            catch { Theme.Scale = 1f; }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Application.Run(new CleanForm());
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Co_cofe 系统垃圾清理");
            }
        }
    }
}
