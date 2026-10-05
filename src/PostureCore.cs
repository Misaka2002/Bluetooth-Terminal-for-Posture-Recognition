using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace PostureStatistics
{
    public sealed class PostureDefinition
    {
        public readonly string Key, Name;
        public readonly int Id;
        public PostureDefinition(int id, string key, string name) { Id = id; Key = key; Name = name; }
    }

    public static class PostureCatalog
    {
        public static readonly PostureDefinition[] Items = {
            new PostureDefinition(0, "upright", "正坐"),
            new PostureDefinition(1, "forward_lean", "前倾"),
            new PostureDefinition(2, "backward_lean", "后仰"),
            new PostureDefinition(3, "left_lean", "左倾"),
            new PostureDefinition(4, "right_lean", "右倾"),
            new PostureDefinition(5, "forward_hunch", "前弯含胸"),
            new PostureDefinition(6, "left_hunch", "左弯含胸"),
            new PostureDefinition(7, "right_hunch", "右弯含胸")
        };
        public static bool IsKnown(string key) { foreach (PostureDefinition p in Items) if (p.Key == key) return true; return false; }
        public static string NameOf(string key) { foreach (PostureDefinition p in Items) if (p.Key == key) return p.Name; return "未识别 / 无数据"; }
    }

    public static class PostureParser
    {
        private static readonly Regex Field = new Regex(@"(?:^|[,;\s])(?:POSTURE|LABEL)\s*=\s*([a-zA-Z_]+)(?=$|[,;\s])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        public static bool TryParse(string line, out string key)
        {
            key = null;
            if (String.IsNullOrWhiteSpace(line)) return false;
            string value = line.Trim().ToLowerInvariant();
            MatchCollection fields = Field.Matches(line);
            // 拒绝同一行出现多个分类字段的歧义数据；单独 ID 在不同模型中含义不同。
            if (fields.Count > 1) return false;
            if (fields.Count == 1) value = fields[0].Groups[1].Value.ToLowerInvariant();
            if (!PostureCatalog.IsKnown(value)) return false;
            key = value;
            return true;
        }
    }

    public sealed class LineFramer
    {
        private readonly StringBuilder pending = new StringBuilder();
        private bool discarding;
        public long DroppedLines { get; private set; }
        public List<string> Feed(string chunk)
        {
            List<string> result = new List<string>();
            if (chunk == null) return result;
            foreach (char c in chunk)
            {
                if (c == '\n')
                {
                    if (!discarding) result.Add(pending.ToString().TrimEnd('\r'));
                    pending.Clear(); discarding = false;
                }
                else if (!discarding)
                {
                    if (pending.Length >= 1024) { pending.Clear(); discarding = true; DroppedLines++; }
                    else pending.Append(c);
                }
            }
            return result;
        }
        public void Clear() { pending.Clear(); discarding = false; }
    }

    public sealed class PostureDuration
    {
        public string Key, Name;
        public double Seconds, LongestSeconds;
        public int Visits;
    }
    public sealed class PostureSegment
    {
        public string Key;
        public double StartSeconds, EndSeconds;
        public PostureSegment(string key, double start, double end) { Key = key; StartSeconds = start; EndSeconds = end; }
    }
    public sealed class SessionSnapshot
    {
        public DateTime StartedUtc;
        public string Mode, CurrentKey;
        public double ElapsedSeconds, ObservedSeconds, UnknownSeconds, CurrentSinceSeconds, LastDataSeconds;
        public long ValidFrames;
        public int Changes;
        public List<PostureDuration> Rows;
        public List<PostureSegment> RecentSegments;
    }

    public sealed class SessionTracker
    {
        public const double StaleSeconds = 2.0;
        private readonly Func<double> clock;
        private readonly DateTime startedUtc;
        private readonly List<PostureSegment> completed = new List<PostureSegment>();
        private string current = "unknown", lastKnown;
        private double start, lastData = -1, lastEvent;
        private long frames;
        private int changes;
        public string Mode = "live";
        public double NowSeconds { get { return Math.Max(0, clock()); } }
        public SessionTracker() { Stopwatch watch = Stopwatch.StartNew(); clock = delegate { return watch.Elapsed.TotalSeconds; }; startedUtc = DateTime.UtcNow; }
        public SessionTracker(Func<double> timeSource, DateTime utc) { if (timeSource == null) throw new ArgumentNullException("timeSource"); clock = timeSource; startedUtc = utc.ToUniversalTime(); }

        private void CloseAt(double end)
        {
            if (end > start) completed.Add(new PostureSegment(current, start, end));
            start = end;
        }
        public void Accept(string key) { Accept(key, NowSeconds); }
        public void Accept(string key, double arrivedAt)
        {
            if (!PostureCatalog.IsKnown(key)) throw new ArgumentException("未知姿态名称", "key");
            if (Double.IsNaN(arrivedAt) || Double.IsInfinity(arrivedAt)) throw new ArgumentException("无效到达时间", "arrivedAt");
            double at = Math.Max(0, Math.Min(NowSeconds, arrivedAt));
            // 数据事件使用收到完整行的时刻。快照不提交计时，因此 UI 排队不会推迟分类边界。
            if (at < lastEvent) return;
            if (current != "unknown" && at > lastData + StaleSeconds)
            {
                CloseAt(lastData + StaleSeconds); current = "unknown";
            }
            if (current != key) { CloseAt(at); current = key; }
            if (lastKnown != null && lastKnown != key) changes++;
            lastKnown = key; lastEvent = at; lastData = at; frames++;
        }
        public void Invalidate()
        {
            double at = Math.Max(lastEvent, NowSeconds);
            if (current != "unknown" && at > lastData + StaleSeconds)
            { CloseAt(lastData + StaleSeconds); current = "unknown"; }
            if (current != "unknown") { CloseAt(at); current = "unknown"; }
            lastEvent = at;
        }
        public List<PostureSegment> GetSegments() { return GetSegments(NowSeconds); }
        internal List<PostureSegment> GetSegments(double at)
        {
            double now = Math.Max(lastEvent, at);
            List<PostureSegment> result = new List<PostureSegment>(completed);
            double boundary = current == "unknown" ? now : Math.Min(now, lastData + StaleSeconds);
            if (boundary > start) result.Add(new PostureSegment(current, start, boundary));
            if (boundary < now) result.Add(new PostureSegment("unknown", boundary, now));
            return result;
        }
        public SessionSnapshot GetSnapshot() { return GetSnapshot(NowSeconds); }
        internal SessionSnapshot GetSnapshot(double at)
        {
            double now = Math.Max(lastEvent, at);
            bool stale = current != "unknown" && now >= lastData + StaleSeconds;
            SessionSnapshot s = new SessionSnapshot {
                StartedUtc = startedUtc, Mode = Mode, ElapsedSeconds = now,
                CurrentKey = stale ? "unknown" : current, CurrentSinceSeconds = stale ? lastData + StaleSeconds : start,
                LastDataSeconds = lastData, ValidFrames = frames, Changes = changes,
                Rows = new List<PostureDuration>(), RecentSegments = new List<PostureSegment>()
            };
            Dictionary<string, PostureDuration> rows = new Dictionary<string, PostureDuration>();
            foreach (PostureDefinition p in PostureCatalog.Items)
            {
                PostureDuration row = new PostureDuration { Key = p.Key, Name = p.Name };
                rows.Add(p.Key, row); s.Rows.Add(row);
            }
            List<PostureSegment> segments = GetSegments(now);
            foreach (PostureSegment part in segments)
            {
                double duration = part.EndSeconds - part.StartSeconds;
                if (part.Key == "unknown") s.UnknownSeconds += duration;
                else
                {
                    PostureDuration row = rows[part.Key]; row.Seconds += duration; row.Visits++;
                    row.LongestSeconds = Math.Max(row.LongestSeconds, duration); s.ObservedSeconds += duration;
                }
            }
            s.RecentSegments.AddRange(segments.GetRange(Math.Max(0, segments.Count - 40), Math.Min(40, segments.Count)));
            return s;
        }
    }

    public static class ReportWriter
    {
        private static string Number(double n) { return n.ToString("F6", CultureInfo.InvariantCulture); }
        private static string Quote(string text) { return "\"" + text.Replace("\"", "\"\"") + "\""; }
        private static void AtomicWrite(string file, string content, bool bom)
        {
            string temporary = file + ".tmp";
            File.WriteAllText(temporary, content, new UTF8Encoding(bom));
            if (File.Exists(file)) File.Replace(temporary, file, null);
            else File.Move(temporary, file);
        }
        public static void Save(string directory, SessionTracker tracker)
        {
            Directory.CreateDirectory(directory);
            // 固定导出时刻，确保汇总、时间线和总时长来自同一份快照。
            double now = tracker.NowSeconds;
            SessionSnapshot s = tracker.GetSnapshot(now);
            List<PostureSegment> segments = tracker.GetSegments(now);
            Dictionary<string, PostureDuration> exact = new Dictionary<string, PostureDuration>();
            foreach (PostureDefinition p in PostureCatalog.Items) exact[p.Key] = new PostureDuration { Key = p.Key, Name = p.Name };
            double unknown = 0, observed = 0;
            foreach (PostureSegment seg in segments)
            {
                double d = seg.EndSeconds - seg.StartSeconds;
                if (seg.Key == "unknown") unknown += d;
                else { PostureDuration row = exact[seg.Key]; row.Seconds += d; row.Visits++; row.LongestSeconds = Math.Max(row.LongestSeconds, d); observed += d; }
            }
            string stamp = s.StartedUtc.ToString("o", CultureInfo.InvariantCulture);
            StringBuilder summary = new StringBuilder("会话启动UTC,模式,姿态代码,姿态名称,累计秒,占有效时长百分比,连续最长秒,出现段数,会话总秒\r\n");
            foreach (PostureDefinition p in PostureCatalog.Items)
            {
                PostureDuration row = exact[p.Key];
                summary.Append(Quote(stamp)).Append(',').Append(Quote(s.Mode)).Append(',').Append(p.Key).Append(',').Append(Quote(row.Name)).Append(',')
                    .Append(Number(row.Seconds)).Append(',').Append(Number(observed > 0 ? row.Seconds / observed * 100 : 0)).Append(',')
                    .Append(Number(row.LongestSeconds)).Append(',').Append(row.Visits).Append(',').Append(Number(now)).Append("\r\n");
            }
            summary.Append(Quote(stamp)).Append(',').Append(Quote(s.Mode)).Append(",unknown,\"未识别 / 无数据\",").Append(Number(unknown)).Append(",,0,0,").Append(Number(now)).Append("\r\n");
            StringBuilder timeline = new StringBuilder("模式,姿态代码,姿态名称,开始秒,结束秒,持续秒,开始UTC,结束UTC\r\n");
            foreach (PostureSegment seg in segments)
                timeline.Append(Quote(s.Mode)).Append(',').Append(seg.Key).Append(',').Append(Quote(PostureCatalog.NameOf(seg.Key))).Append(',')
                    .Append(Number(seg.StartSeconds)).Append(',').Append(Number(seg.EndSeconds)).Append(',').Append(Number(seg.EndSeconds - seg.StartSeconds)).Append(',')
                    .Append(Quote(s.StartedUtc.AddSeconds(seg.StartSeconds).ToString("o"))).Append(',').Append(Quote(s.StartedUtc.AddSeconds(seg.EndSeconds).ToString("o"))).Append("\r\n");
            string json = "{\r\n  \"started_utc\": " + Quote(stamp) + ",\r\n  \"mode\": " + Quote(s.Mode) + ",\r\n  \"elapsed_seconds\": " + Number(now)
                + ",\r\n  \"observed_seconds\": " + Number(observed) + ",\r\n  \"unknown_seconds\": " + Number(unknown)
                + ",\r\n  \"valid_frames\": " + s.ValidFrames + ",\r\n  \"posture_changes\": " + s.Changes
                + ",\r\n  \"stale_seconds\": 2.0,\r\n  \"time_basis\": \"terminal_monotonic_receipt_time\"\r\n}\r\n";
            AtomicWrite(Path.Combine(directory, "summary.csv"), summary.ToString(), true);
            AtomicWrite(Path.Combine(directory, "timeline.csv"), timeline.ToString(), true);
            AtomicWrite(Path.Combine(directory, "session.json"), json, false);
        }
    }
}
