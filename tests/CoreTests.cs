using System;
using System.IO;
using System.Globalization;
using PostureStatistics;

internal static class CoreTests
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static void Equal(double actual, double expected, string message) { Check(Math.Abs(actual - expected) < 0.000001, message + ": " + actual + " != " + expected); }
    private static double Duration(SessionSnapshot s, string key) { foreach (PostureDuration r in s.Rows) if (r.Key == key) return r.Seconds; throw new Exception(key); }
    private static void Conserved(SessionSnapshot s) { Equal(s.ObservedSeconds + s.UnknownSeconds, s.ElapsedSeconds, "时长守恒"); }
    public static int Main(string[] args)
    {
        try
        {
            string key;
            foreach (PostureDefinition p in PostureCatalog.Items) { Check(PostureParser.TryParse("POSTURE=" + p.Key + "\r\n", out key) && key == p.Key, "8类协议"); }
            Check(PostureParser.TryParse("AI,SEQ=4,CLASS=2,LABEL=backward_lean,CONF=99", out key) && key == "backward_lean", "调试名称兼容");
            Check(PostureParser.TryParse("  UPRIGHT  ", out key) && key == "upright", "纯名称");
            Check(!PostureParser.TryParse("SEQ=10,ID=3,CONF_PCT=100", out key), "不猜ID映射");
            Check(!PostureParser.TryParse("POSTURE=upright1", out key), "坏字段");
            Check(!PostureParser.TryParse("NOT_POSTURE=upright", out key), "字段边界");
            Check(!PostureParser.TryParse("POSTURE=upright,LABEL=left_lean", out key), "歧义字段");
            Check(!PostureParser.TryParse("BT_READY", out key), "启动提示不算姿态");
            LineFramer framer = new LineFramer();
            Check(framer.Feed("POST").Count == 0, "半行等待");
            var lines = framer.Feed("URE=upright\r\nPOSTURE=left_lean\nPOSTURE=right_");
            Check(lines.Count == 2 && lines[0] == "POSTURE=upright" && lines[1] == "POSTURE=left_lean", "合并半行并拆多行");
            Check(framer.Feed("lean\n")[0] == "POSTURE=right_lean", "尾部半行");
            framer.Feed(new string('x', 20000));
            lines = framer.Feed("\nPOSTURE=upright\n");
            Check(framer.DroppedLines == 1 && lines.Count == 1 && lines[0] == "POSTURE=upright", "超长垃圾丢弃且恢复");
            framer.Feed("POSTURE=left_"); framer.Clear();
            Check(framer.Feed("lean\n")[0] == "lean", "断开清半行");

            double time = 0;
            SessionTracker tracker = new SessionTracker(delegate { return time; }, new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc));
            time = 5; var s = tracker.GetSnapshot(); Equal(s.UnknownSeconds, 5, "启动无数据"); Conserved(s);
            tracker.Accept("upright");
            for (int i = 1; i <= 10; i++) { time = 5 + i * 0.5; tracker.Accept("upright"); }
            s = tracker.GetSnapshot(); Equal(Duration(s, "upright"), 5, "稳定上报累计"); Check(s.Rows[0].Visits == 1, "同类不分段"); Check(s.ValidFrames == 11, "帧计数");
            time = 11; tracker.Accept("left_lean");
            time = 11.5; tracker.Accept("left_lean");
            time = 14.5; s = tracker.GetSnapshot();
            Equal(Duration(s, "upright"), 6, "边界归属上一类"); Equal(Duration(s, "left_lean"), 2.5, "超时截止"); Equal(s.UnknownSeconds, 6, "超时未知");
            Check(s.CurrentKey == "unknown" && s.Changes == 1, "超时状态与切换数"); Conserved(s);
            time = 15; tracker.Accept("left_lean");
            time = 16; tracker.Invalidate();
            time = 20; s = tracker.GetSnapshot(); Equal(Duration(s, "left_lean"), 3.5, "断开立即截止"); Check(s.Rows[3].Visits == 2, "断线后重进新段");
            Equal(s.Rows[3].LongestSeconds, 2.5, "连续最长"); Conserved(s);
            time = 21; tracker.Accept("right_lean"); time = 23; s = tracker.GetSnapshot();
            Check(s.CurrentKey == "unknown", "恰好达到超时阈值"); Equal(Duration(s, "right_lean"), 2, "阈值前有效"); Conserved(s);
            time = 23.5; tracker.Accept("upright", 22.9); s = tracker.GetSnapshot();
            Equal(Duration(s, "right_lean"), 1.9, "排队到达时间回溯"); Equal(Duration(s, "upright"), 6.6, "快照不提交计时"); Conserved(s);
            long count = s.ValidFrames; tracker.Accept("forward_hunch", 22); Check(tracker.GetSnapshot().ValidFrames == count, "乱序事件丢弃");
            time = 24; tracker.Invalidate(); time = 30; Conserved(tracker.GetSnapshot());

            // 长序列覆盖跨姿态、收包停顿及频繁快照，任何时刻总时长均应守恒。
            double t = 0; SessionTracker stress = new SessionTracker(delegate { return t; }, DateTime.UtcNow);
            Random random = new Random(42);
            for (int i = 0; i < 2000; i++) { t += random.NextDouble() * 3; stress.Accept(PostureCatalog.Items[random.Next(8)].Key); Conserved(stress.GetSnapshot()); }
            t += 20; Conserved(stress.GetSnapshot()); Check(stress.GetSnapshot().RecentSegments.Count <= 40, "界面时间线有界");
            string folder = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "PostureStatsTests_" + Guid.NewGuid().ToString("N"));
            tracker.Mode = "demo"; ReportWriter.Save(folder, tracker); ReportWriter.Save(folder, tracker);
            Check(File.Exists(Path.Combine(folder, "summary.csv")) && File.Exists(Path.Combine(folder, "timeline.csv")) && File.Exists(Path.Combine(folder, "session.json")), "3种导出存在且可覆盖");
            string csv = File.ReadAllText(Path.Combine(folder, "summary.csv"));
            Check(csv.Contains("正坐") && csv.Contains("\"demo\"") && csv.Split('\n').Length == 11, "中文模式与行数");
            byte[] bytes = File.ReadAllBytes(Path.Combine(folder, "summary.csv")); Check(bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191, "CSV BOM");
            Console.WriteLine("PASS: " + checks + " checks; export=" + folder); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine("FAIL: " + e); return 1; }
    }
}
