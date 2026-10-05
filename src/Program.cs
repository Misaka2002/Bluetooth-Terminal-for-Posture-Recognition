using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace PostureStatistics
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool demo = false;
            bool compact = false;
            bool logHidden = false;
            string pose = null;
            string preview = null;
            string smoke = null;
            foreach (string arg in args)
            {
                if (arg.Equals("/demo", StringComparison.OrdinalIgnoreCase)) demo = true;
                if (arg.Equals("/compact", StringComparison.OrdinalIgnoreCase)) compact = true;
                if (arg.Equals("/loghidden", StringComparison.OrdinalIgnoreCase)) logHidden = true;
                if (arg.StartsWith("/pose:", StringComparison.OrdinalIgnoreCase)) pose = arg.Substring(6);
                if (arg.StartsWith("/preview:", StringComparison.OrdinalIgnoreCase)) preview = arg.Substring(9);
                if (arg.StartsWith("/smoke:", StringComparison.OrdinalIgnoreCase)) smoke = arg.Substring(7);
            }
            string artifact = preview ?? smoke;
            TerminalForm form = new TerminalForm(demo, artifact == null ? null : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(artifact)), "preview-sessions"));
            if (compact) form.Size = form.MinimumSize;
            if (logHidden) form.SetLogVisible(false);
            if (demo && pose != null) form.SetPreviewPose(pose);
            if (artifact != null)
            {
                // 截图窗口放在屏幕外，避免测试打扰用户。
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-20000, -20000);
                form.ShowInTaskbar = false;
                Timer capture = new Timer { Interval = smoke != null ? 500 : demo ? 4500 : 500 };
                capture.Tick += delegate
                {
                    capture.Stop();
                    if (smoke != null)
                    {
                        try { File.WriteAllText(smoke, form.SmokeChecks()); }
                        catch (Exception ex) { File.WriteAllText(smoke, "FAIL: " + ex); Environment.ExitCode = 1; }
                        form.Close(); return;
                    }
                    using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        bitmap.Save(preview, ImageFormat.Png);
                    }
                    form.Close();
                };
                form.Shown += delegate { capture.Start(); };
            }
            Application.Run(form);
        }
    }
}
