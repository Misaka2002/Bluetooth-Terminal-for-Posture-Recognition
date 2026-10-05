using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Windows.Forms;

namespace PostureStatistics
{
    public sealed class TerminalForm : Form
    {
        private readonly ComboBox ports = new ComboBox();
        private readonly RoundedButton connect = new RoundedButton();
        private readonly RoundedButton demoButton = new RoundedButton();
        private readonly TextBox log = new TextBox();
        private readonly RoundedButton logToggle = new RoundedButton();
        private readonly TableLayoutPanel root = new TableLayoutPanel();
        private readonly FlowLayoutPanel logActions = new FlowLayoutPanel();
        private readonly Label timingNote = new Label();
        private readonly Label status = new Label();
        private readonly ToolTip tips = new ToolTip();
        private readonly StringBuilder pendingLog = new StringBuilder();
        private readonly Dashboard dashboard = new Dashboard();
        private readonly Timer timer = new Timer();
        private readonly object gate = new object();
        private readonly Queue<ReceivedLine> queue = new Queue<ReceivedLine>();
        private readonly LineFramer framer = new LineFramer();
        private SerialPort port;
        private SessionTracker tracker;
        private string sessionDirectory;
        private readonly string sessionsRoot;
        private int generation;
        private bool demo;
        private bool closing;
        private bool saveFailed;
        private double lastSave;
        private double lastDemo = -1;
        private long dropped;
        private string receiveError;
        private long lostLogChars;
        private DateTime savedAt;
        private bool logVisible = true;
        private string previewPose;

        private sealed class ReceivedLine
        {
            public int Generation;
            public double At;
            public string Text;
        }

        public TerminalForm(bool startDemo) : this(startDemo, null) { }
        internal TerminalForm(bool startDemo, string testSessionsRoot)
        {
            sessionsRoot = testSessionsRoot ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sessions");
            Text = "姿态观察台 · 蓝牙统计终端";
            Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Regular, GraphicsUnit.Pixel);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1240, 960);
            MinimumSize = new Size(1050, 840);
            BackColor = Color.FromArgb(242, 246, 251);
            StartPosition = FormStartPosition.CenterScreen;
            root.Dock = DockStyle.Fill; root.Padding = new Padding(24); root.ColumnCount = 1; root.RowCount = 6;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            Controls.Add(root);
            FlowLayoutPanel toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true, Margin = new Padding(0) };
            toolbar.Controls.Add(new Label { Text = "设备", AutoSize = true, ForeColor = Color.FromArgb(98, 117, 140), Margin = new Padding(0, 15, 12, 0) });
            ports.DropDownStyle = ComboBoxStyle.DropDownList;
            ports.DrawMode = DrawMode.OwnerDrawFixed; ports.ItemHeight = 28;
            ports.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                e.DrawBackground(); string label = e.Index >= 0 ? ports.Items[e.Index].ToString() : "选择 COM";
                TextRenderer.DrawText(e.Graphics, label, ports.Font, e.Bounds, Color.FromArgb(42, 65, 93), TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
                e.DrawFocusRectangle();
            };
            RoundedPanel portShell = new RoundedPanel { Size = new Size(136, 46), Margin = new Padding(0, 4, 10, 0) };
            ports.Location = new Point(8, 7); ports.Width = 120; ports.FlatStyle = FlatStyle.Flat;
            portShell.Controls.Add(ports); toolbar.Controls.Add(portShell);
            AddButton(toolbar, "刷新", delegate { RefreshPorts(); }, false);
            toolbar.Controls.Add(new Label { Text = "9600 · 8N1", AutoSize = true, ForeColor = Color.FromArgb(98, 117, 140), Margin = new Padding(4, 15, 14, 0) });
            ConfigureButton(connect, "连接设备", true);
            connect.Click += delegate { if (port == null) Connect(); else Disconnect("设备已断开"); };
            toolbar.Controls.Add(connect);
            ConfigureButton(demoButton, "演示模式", false);
            demoButton.Click += delegate { ChangeMode(!demo); };
            toolbar.Controls.Add(demoButton);
            AddButton(toolbar, "新统计会话", delegate { RestartSession(); }, false);
            AddButton(toolbar, "导出 CSV", delegate { Export(); }, false);
            root.Controls.Add(toolbar, 0, 0);
            dashboard.Dock = DockStyle.Fill;
            root.Controls.Add(dashboard, 0, 1);
            // 计时说明拥有独立布局行，不再贴在绘图区底部被下一行遮住。
            timingNote.Text = "按终端收到完整数据行的时刻统计 · 未识别时长单独记录";
            timingNote.Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Regular, GraphicsUnit.Pixel);
            timingNote.ForeColor = Color.FromArgb(103, 121, 140);
            timingNote.Dock = DockStyle.Fill; timingNote.Margin = new Padding(3, 0, 0, 0);
            timingNote.TextAlign = ContentAlignment.MiddleLeft;
            root.Controls.Add(timingNote, 0, 2);
            log.Dock = DockStyle.Fill;
            log.ReadOnly = true;
            log.Multiline = true;
            log.ScrollBars = ScrollBars.Both;
            log.Font = new Font("Consolas", 9F);
            log.BackColor = Color.FromArgb(23, 39, 58);
            log.ForeColor = Color.FromArgb(193, 216, 230);
            log.BorderStyle = BorderStyle.None;
            log.WordWrap = false;
            log.Margin = new Padding(0, 10, 0, 8);
            root.Controls.Add(log, 0, 4);
            logActions.Dock = DockStyle.Fill; logActions.WrapContents = false; logActions.Margin = new Padding(0); logActions.Padding = new Padding(0, 8, 0, 4);
            ConfigureButton(logToggle, "原始终端：显示", false);
            logToggle.Click += delegate { SetLogVisible(!logVisible); };
            logActions.Controls.Add(logToggle);
            AddButton(logActions, "清屏", delegate { log.Clear(); }, false);
            AddButton(logActions, "保存日志", delegate { ExportLog(); }, false);
            logActions.Controls.Add(new Label { Text = "隐藏仅改变显示 · 接收与记录持续运行", Font = new Font("Microsoft YaHei UI", 17F, FontStyle.Regular, GraphicsUnit.Pixel), AutoSize = true, ForeColor = Color.FromArgb(108, 127, 151), Margin = new Padding(10, 16, 0, 0) });
            root.Controls.Add(logActions, 0, 3);
            status.Dock = DockStyle.Fill;
            status.AutoEllipsis = true;
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.ForeColor = Color.FromArgb(87, 105, 125);
            status.Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Regular, GraphicsUnit.Pixel);
            root.Controls.Add(status, 0, 5);
            RefreshPorts();
            demo = startDemo;
            NewSession();
            if (testSessionsRoot == null)
            {
                Rectangle workArea = Screen.PrimaryScreen.WorkingArea;
                Size available = new Size(Math.Max(600, workArea.Width - 48), Math.Max(600, workArea.Height - 48));
                MinimumSize = new Size(Math.Min(MinimumSize.Width, available.Width), Math.Min(MinimumSize.Height, available.Height));
                Size = new Size(Math.Min(Width, available.Width), Math.Min(Height, available.Height));
                if (ClientSize.Height < 870) SetLogVisible(false);
            }
            timer.Interval = 200;
            timer.Tick += delegate { Tick(); };
            timer.Start();
            FormClosing += OnClosing;
        }

        private static void ConfigureButton(RoundedButton button, string text, bool primary)
        {
            button.Text = text;
            button.Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Regular, GraphicsUnit.Pixel);
            button.AutoSize = false; button.Size = new Size(Math.Max(80, TextRenderer.MeasureText(text, button.Font).Width + 32), 44);
            button.Primary = primary; button.Margin = new Padding(0, 4, 10, 4);
        }
        private static void AddButton(Control panel, string text, EventHandler handler, bool primary)
        {
            RoundedButton button = new RoundedButton(); ConfigureButton(button, text, primary); button.Click += handler; panel.Controls.Add(button);
        }
        internal void SetLogVisible(bool visible)
        {
            logVisible = visible; root.SuspendLayout(); log.Visible = visible;
            root.RowStyles[4].Height = visible ? 126 : 0;
            logToggle.Text = visible ? "原始终端：显示" : "原始终端：隐藏";
            root.ResumeLayout(true); dashboard.Invalidate();
        }
        internal void SetPreviewPose(string key)
        {
            if (!demo || !PostureCatalog.IsKnown(key)) return;
            previewPose = key; tracker.Accept(key); UpdateDashboard();
        }
        private void RefreshPorts()
        {
            try
            {
            string selected = ports.SelectedItem as string;
            ports.Items.Clear();
            string[] names = SerialPort.GetPortNames(); Array.Sort(names);
            ports.Items.AddRange(names);
            if (selected != null && ports.Items.Contains(selected)) ports.SelectedItem = selected;
            else if (ports.Items.Count > 0) ports.SelectedIndex = 0;
            }
            catch (Exception ex) { MessageBox.Show(this, "刷新串口失败：" + ex.Message, "串口枚举", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        private void NewSession()
        {
            lock (gate) { generation++; queue.Clear(); framer.Clear(); receiveError = null; }
            tracker = new SessionTracker(); tracker.Mode = demo ? "demo" : "live";
            sessionDirectory = Path.Combine(sessionsRoot, DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N"));
            lastSave = 0; lastDemo = -1; dropped = 0;
            log.Clear(); pendingLog.Clear(); lostLogChars = 0;
            Log(demo ? "[演示] 模拟数据，与真实设备会话分开保存。" : "[实时] 等待设备数据。统计从本会话启动开始。未识别或无数据计入空白时间。");
            demoButton.Text = demo ? "退出演示" : "演示模式";
            SaveSession(); UpdateDashboard();
        }
        private bool SaveSession()
        {
            lastSave = tracker.NowSeconds;
            try { Directory.CreateDirectory(sessionDirectory); ReportWriter.Save(sessionDirectory, tracker); FlushLog(); saveFailed = false; savedAt = DateTime.Now; lastSave = tracker.NowSeconds; UpdateStatus(); return true; }
            catch (Exception ex) { saveFailed = true; status.ForeColor = Color.FromArgb(186, 49, 60); status.Text = "保存失败 · " + ex.Message + " · 请导出或重试，关闭时将提示"; return false; }
        }
        private bool PreserveBeforeReplace()
        {
            if (SaveSession()) return true;
            return MessageBox.Show(this, "会话未能保存。继续将丢弃本会话内存中的统计。\n\n" + status.Text, "保存失败", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK;
        }
        private void RestartSession()
        {
            if (!PreserveBeforeReplace()) return;
            Disconnect("重开统计会话"); NewSession();
        }
        private void ChangeMode(bool newDemo)
        {
            if (!PreserveBeforeReplace()) return;
            Disconnect("切换统计模式"); demo = newDemo; NewSession();
        }
        private void Connect()
        {
            if (demo) { if (!PreserveBeforeReplace()) return; demo = false; NewSession(); }
            if (ports.SelectedItem == null) { MessageBox.Show(this, "请先配对蓝牙设备，再刷新并选择它的传出 COM 口。", "没有串口"); return; }
            SerialPort candidate = new SerialPort((string)ports.SelectedItem, 9600, Parity.None, 8, StopBits.One);
            candidate.Handshake = Handshake.None; candidate.ReadTimeout = 500; candidate.WriteTimeout = 500;
            int token; SessionTracker source = tracker;
            lock (gate) { generation++; token = generation; queue.Clear(); framer.Clear(); receiveError = null; port = candidate; }
            candidate.DataReceived += delegate
            {
                try
                {
                    lock (gate)
                    {
                        if (closing || token != generation || port != candidate) return;
                        // 到达时刻在串口线程捕获，UI 忙碌不会延长上一姿态。
                        double arrived = source.NowSeconds;
                        string chunk = candidate.ReadExisting();
                        foreach (string line in framer.Feed(chunk))
                        {
                            if (queue.Count >= 512) { queue.Dequeue(); dropped++; }
                            queue.Enqueue(new ReceivedLine { Generation = token, At = arrived, Text = line });
                        }
                    }
                }
                catch (Exception ex) { lock (gate) { if (token == generation) receiveError = ex.Message; } }
            };
            candidate.ErrorReceived += delegate { lock (gate) { if (token == generation) receiveError = "串口报告通信错误"; } };
            try { candidate.Open(); connect.Text = "断开设备"; ports.Enabled = false; Log("[连接] " + candidate.PortName + " · 9600 baud · 8N1"); UpdateStatus(); }
            catch (Exception ex) { Disconnect("连接失败：" + ex.Message); MessageBox.Show(this, ex.Message, "连接失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        private void Disconnect(string reason)
        {
            SerialPort old;
            lock (gate) { generation++; old = port; port = null; queue.Clear(); framer.Clear(); receiveError = null; }
            tracker.Invalidate();
            if (old != null) { try { old.Close(); } catch { } try { old.Dispose(); } catch { } }
            connect.Text = "连接设备"; ports.Enabled = true;
            Log("[连接] " + reason); SaveSession();
        }
        private void Tick()
        {
            string error; List<ReceivedLine> batch = new List<ReceivedLine>();
            lock (gate) { error = receiveError; receiveError = null; while (queue.Count > 0) batch.Add(queue.Dequeue()); }
            if (error != null) Disconnect("接收失败：" + error);
            if (!demo && port != null)
            {
                if (!port.IsOpen) Disconnect("设备连接已关闭");
                else foreach (ReceivedLine line in batch)
                {
                    if (line.Generation != generation) continue;
                    string key; Log(line.Text);
                    if (PostureParser.TryParse(line.Text, out key)) tracker.Accept(key, line.At);
                }
            }
            if (demo && tracker.NowSeconds - lastDemo >= 0.8)
            {
                lastDemo = tracker.NowSeconds;
                int index = ((int)(lastDemo / 3)) % 8;
                string key = previewPose ?? PostureCatalog.Items[index].Key;
                tracker.Accept(key); Log("[演示] POSTURE=" + key);
            }
            if (tracker.NowSeconds - lastSave >= 30) SaveSession();
            if (pendingLog.Length > 0 && !saveFailed)
            {
                try { FlushLog(); }
                catch (Exception ex) { saveFailed = true; status.ForeColor = Color.FromArgb(186, 49, 60); status.Text = "接收日志保存失败 · " + ex.Message + " · 自动保存将重试"; }
            }
            UpdateDashboard();
        }
        private void UpdateDashboard() { dashboard.Snapshot = tracker.GetSnapshot(); dashboard.Invalidate(); if (!saveFailed) UpdateStatus(); }
        private void UpdateStatus()
        {
            if (saveFailed) return;
            status.ForeColor = Color.FromArgb(87, 105, 125);
            status.Text = (demo ? "演示会话 · 模拟数据" : port == null ? "实时会话 · 未连接" : "实时会话 · 已连接 " + port.PortName) + " · 有效记录 " + tracker.GetSnapshot().ValidFrames + " 条 · 最近保存 " + savedAt.ToString("HH:mm:ss") + " · 每 30 秒自动保存" + (dropped > 0 ? " · 已丢弃拥堵行 " + dropped : "");
            tips.SetToolTip(status, "当前会话目录：" + sessionDirectory);
        }
        private void Log(string text)
        {
            if (text.Length > 2048) text = text.Substring(0, 2048) + " …";
            string record = DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine;
            pendingLog.Append(record);
            if (pendingLog.Length > 262144) { int remove = pendingLog.Length - 262144; pendingLog.Remove(0, remove); lostLogChars += remove; }
            log.AppendText(record);
            if (log.TextLength > 60000) { log.Select(0, log.TextLength - 45000); log.SelectedText = ""; }
            log.SelectionStart = log.TextLength; log.ScrollToCaret();
        }
        private void Export()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog { Description = "选择 CSV 和会话报告的导出目录" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { string target = Path.Combine(dialog.SelectedPath, "姿态会话_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8)); ReportWriter.Save(target, tracker); Log("[导出] " + target); MessageBox.Show(this, "已导出 summary.csv、timeline.csv 和 session.json。\n" + target, "导出完成"); }
                catch (Exception ex) { MessageBox.Show(this, "导出失败：" + ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }
        private void FlushLog()
        {
            Directory.CreateDirectory(sessionDirectory);
            if (pendingLog.Length > 0) { File.AppendAllText(Path.Combine(sessionDirectory, "received.log"), pendingLog.ToString(), new UTF8Encoding(true)); pendingLog.Clear(); }
            if (lostLogChars > 0) throw new IOException("保存故障期间有 " + lostLogChars + " 个日志字符超出缓冲限制；统计报告仍保留");
        }
        private void ExportLog()
        {
            using (SaveFileDialog dialog = new SaveFileDialog { Filter = "接收日志 (*.log)|*.log|文本文件 (*.txt)|*.txt", FileName = "接收日志_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { FlushLog(); File.Copy(Path.Combine(sessionDirectory, "received.log"), dialog.FileName, true); MessageBox.Show(this, "接收日志已保存。", "保存完成"); }
                catch (Exception ex) { MessageBox.Show(this, "日志保存失败：" + ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }
        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            // 保存失败必须明确提示；取消关闭可继续导出或重试。
            if (!SaveSession())
            {
                DialogResult choice = MessageBox.Show(this, "保存失败，统计仍在内存中。\n选择“重试”再次保存；“忽略”关闭并丢弃未保存统计；“中止”留在窗口。\n\n" + status.Text, "退出前保存失败", MessageBoxButtons.AbortRetryIgnore, MessageBoxIcon.Warning);
                if (choice == DialogResult.Abort || (choice == DialogResult.Retry && !SaveSession())) { e.Cancel = true; return; }
            }
            closing = true; timer.Stop();
            SerialPort old; lock (gate) { generation++; old = port; port = null; queue.Clear(); }
            if (old != null) { try { old.Close(); } catch { } try { old.Dispose(); } catch { } }
        }
        internal string SmokeChecks()
        {
            timer.Stop();
            List<string> passed = new List<string>();
            if (!dashboard.AtlasAvailable) throw new Exception("嵌入姿态图集不存在或无效");
            HashSet<Rectangle> cells = new HashSet<Rectangle>();
            for (int i = 0; i < 8; i++)
            {
                Rectangle cell = dashboard.AtlasCell(i);
                if (cell.Width <= 0 || cell.Height <= 0 || !cells.Add(cell)) throw new Exception("图集索引无效或重复");
            }
            passed.Add("嵌入 3D 图集与八类唯一索引通过");
            SessionTracker beforeToggle = tracker; string directoryBeforeToggle = sessionDirectory;
            long framesBeforeToggle = tracker.GetSnapshot().ValidFrames;
            int queueBeforeToggle = queue.Count, generationBeforeToggle = generation;
            bool initialVisible = logVisible;
            SetLogVisible(!initialVisible); SetLogVisible(initialVisible);
            if (!Object.ReferenceEquals(beforeToggle, tracker) || sessionDirectory != directoryBeforeToggle || tracker.GetSnapshot().ValidFrames != framesBeforeToggle || queue.Count != queueBeforeToggle || generation != generationBeforeToggle) throw new Exception("原始终端切换影响统计");
            passed.Add("原始终端两次切换保持统计、队列、会话与连接代际不变");
            Size originalSize = Size;
            foreach (Size size in new Size[] { originalSize, MinimumSize })
            {
                Size = size; PerformLayout(); root.PerformLayout(); logActions.PerformLayout();
                SetLogVisible(true); root.PerformLayout();
                RectangleF shown = dashboard.DistributionBounds;
                RectangleF postureShown = dashboard.PostureBounds;
                float scaleShown = dashboard.LayoutScale;
                int dashboardWidth = dashboard.Width;
                if (shown.Height / scaleShown < 176) throw new Exception("最小窗口的统计行间距不足");
                if (TextRenderer.MeasureText(timingNote.Text, timingNote.Font).Height > timingNote.ClientSize.Height || !root.ClientRectangle.Contains(timingNote.Bounds)) throw new Exception("计时说明被裁切");
                Control toolbar = root.GetControlFromPosition(0, 0);
                foreach (Control child in toolbar.Controls)
                    if (child.Visible && child is Button && !toolbar.ClientRectangle.Contains(child.Bounds)) throw new Exception("设备行按钮越界：" + child.Text);
                foreach (Control child in logActions.Controls)
                    if (child.Visible && child is Button && !logActions.ClientRectangle.Contains(child.Bounds)) throw new Exception("操作栏按钮越界：" + child.Text + " " + child.Bounds);
                SetLogVisible(false); PerformLayout(); root.PerformLayout(); logActions.PerformLayout();
                RectangleF hidden = dashboard.DistributionBounds;
                RectangleF postureHidden = dashboard.PostureBounds;
                if (dashboard.Width != dashboardWidth || Math.Abs(dashboard.LayoutScale - scaleShown) > 0.0001 || Math.Abs(hidden.Width - shown.Width) > 0.001 || Math.Abs(hidden.X - shown.X) > 0.001 || postureShown != postureHidden) throw new Exception("终端切换改变了横向布局、字号比例或顶部卡片大小");
                if (hidden.Height <= shown.Height) throw new Exception("隐藏终端没有纵向扩展统计卡片");
                if (TextRenderer.MeasureText(timingNote.Text, timingNote.Font).Height > timingNote.ClientSize.Height) throw new Exception("隐藏终端时计时说明被裁切");
                foreach (Control child in logActions.Controls)
                    if (child.Visible && child is Button && !logActions.ClientRectangle.Contains(child.Bounds)) throw new Exception("隐藏终端时按钮越界：" + child.Text);
                SetLogVisible(initialVisible);
            }
            Size = originalSize;
            passed.Add("默认、最小窗口及隐藏终端状态按钮实际几何边界通过");
            passed.Add("计时说明独立行完整显示；显示切换横向尺寸、字号比例及顶部卡片恒定，仅纵向伸缩");
            string firstDirectory = sessionDirectory;
            tracker.Accept("upright");
            if (tracker.GetSnapshot().ValidFrames != 1) throw new Exception("核心接入失败");
            passed.Add("核心分类接入通过");
            int oldGeneration = generation;
            lock (gate)
            {
                queue.Enqueue(new ReceivedLine { Generation = oldGeneration, At = tracker.NowSeconds, Text = "POSTURE=forward_lean" });
                framer.Feed("POSTURE=left_");
            }
            RestartSession();
            if (sessionDirectory == firstDirectory || queue.Count != 0 || generation == oldGeneration || framer.Feed("lean\n")[0] != "lean" || tracker.GetSnapshot().ValidFrames != 0) throw new Exception("重开或旧连接隔离失败");
            passed.Add("重开会话、半行清除、旧连接队列隔离通过");
            string liveDirectory = sessionDirectory;
            ChangeMode(true);
            if (tracker.Mode != "demo" || sessionDirectory == liveDirectory || port != null) throw new Exception("进入演示隔离失败");
            tracker.Accept("left_lean");
            string demoDirectory = sessionDirectory;
            ChangeMode(false);
            if (tracker.Mode != "live" || sessionDirectory == demoDirectory || tracker.GetSnapshot().ValidFrames != 0) throw new Exception("退出演示隔离失败");
            passed.Add("演示进入退出均创建独立会话通过");
            string goodDirectory = sessionDirectory;
            string blocked = Path.Combine(sessionsRoot, "save-blocker.txt");
            File.WriteAllText(blocked, "test"); sessionDirectory = blocked;
            if (SaveSession() || !saveFailed || !status.Text.StartsWith("保存失败")) throw new Exception("保存失败不可见");
            sessionDirectory = goodDirectory;
            if (!SaveSession() || saveFailed) throw new Exception("保存恢复失败");
            passed.Add("保存失败可见、恢复保存通过");
            if (!File.Exists(Path.Combine(goodDirectory, "summary.csv")) || !File.Exists(Path.Combine(demoDirectory, "session.json"))) throw new Exception("会话报告未生成");
            if (log.Text.Contains("POSTURE=forward_lean")) throw new Exception("旧会话日志未清除");
            passed.Add("会话文件存在、日志清屏通过");
            return String.Join(Environment.NewLine, passed.ToArray());
        }
    }

    internal sealed class Dashboard : Control
    {
        private SessionSnapshot snapshot;
        public SessionSnapshot Snapshot
        {
            get { return snapshot; }
            set
            {
                bool reset = snapshot == null || value == null || snapshot.StartedUtc != value.StartedUtc;
                snapshot = value;
                int index = 0;
                if (value != null) foreach (PostureDuration row in value.Rows)
                {
                    if (row.Key == "unknown") continue;
                    double target = row.Seconds / Math.Max(1, value.ObservedSeconds);
                    if (reset) displayedBars[index] = target;
                    else displayedBars[index] += (target - displayedBars[index]) * 0.28;
                    if (Math.Abs(target - displayedBars[index]) < 0.005) displayedBars[index] = target;
                    index++;
                }
            }
        }
        private readonly Color ink = Color.FromArgb(25, 45, 66);
        private readonly Color muted = Color.FromArgb(103, 121, 140);
        private readonly Color accent = Color.FromArgb(25, 110, 214);
        private readonly ToolTip rowTip = new ToolTip();
        private string lastTip = "";
        private Image atlas;
        private readonly double[] displayedBars = new double[8];
        // 缩放只取决于窗口宽度。终端行隐藏时，仅统计卡片的高度与行距改变。
        internal float LayoutScale { get { return Width / 1136f; } }
        private float StatisticsHeight { get { return Math.Max(1, Height / Math.Max(0.001f, LayoutScale) - 274); } }
        internal RectangleF DistributionBounds { get { float s = LayoutScale; return new RectangleF(0, 266 * s, 720 * s, StatisticsHeight * s); } }
        internal RectangleF PostureBounds { get { float s = LayoutScale; return new RectangleF(0, 64 * s, 465 * s, 188 * s); } }
        internal bool AtlasAvailable { get { return atlas != null && atlas.Width >= 4 && atlas.Height >= 2; } }
        public Dashboard()
        {
            DoubleBuffered = true; ResizeRedraw = true;
            try
            {
                using (Stream stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("PostureStatistics.PostureAtlas.png"))
                {
                    if (stream != null) using (Image source = Image.FromStream(stream)) atlas = new Bitmap(source);
                }
            }
            catch { atlas = null; }
        }
        internal Rectangle AtlasCell(int index)
        {
            if (!AtlasAvailable || index < 0 || index > 7) throw new ArgumentOutOfRangeException("index");
            // 左右以人物自身视角为准；图集中的两个侧倾格按这一口径映射。
            int[] mapping = { 0, 1, 2, 4, 3, 5, 6, 7 }; index = mapping[index];
            int w = atlas.Width / 4; int h = atlas.Height / 2;
            return new Rectangle(index % 4 * w, index / 4 * h, w, h);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { if (atlas != null) atlas.Dispose(); rowTip.Dispose(); }
            base.Dispose(disposing);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            float scale = LayoutScale;
            string tip = "";
            float step = (StatisticsHeight - 74) / 7;
            if (Snapshot != null && scale > 0 && step > 0 && e.X / scale < 720 && e.Y / scale >= 311 && e.Y / scale < 311 + step * 7 + 18)
            {
                int index = Math.Min(7, (int)((e.Y / scale - 311) / step));
                if (index < Snapshot.Rows.Count)
                {
                    PostureDuration row = Snapshot.Rows[index];
                    tip = row.Name + "：累计 " + Duration(row.Seconds) + "，最长连续 " + Duration(row.LongestSeconds) + "，进入 " + row.Visits + " 次\n占有效记录 " + (Snapshot.ObservedSeconds > 0 ? (100 * row.Seconds / Snapshot.ObservedSeconds).ToString("0.0") : "0.0") + "%（无数据时长不计入分母）";
                }
            }
            else if (scale > 0 && e.X / scale >= 236 && e.X / scale <= 436 && e.Y / scale >= 78 && e.Y / scale <= 238)
                tip = "3D 姿态示意 · 左右按人物自身视角\n侧倾表现躯干整体倾斜；弯含胸表现背部弯曲与头肩下沉。";
            if (tip != lastTip) { rowTip.SetToolTip(this, tip); lastTip = tip; }
        }
        private static string Duration(double seconds) { TimeSpan span = TimeSpan.FromSeconds(Math.Max(0, seconds)); return ((long)span.TotalHours).ToString("00") + span.ToString(@"\:mm\:ss"); }
        private void TextAt(Graphics g, string text, float size, Color color, float x, float y, bool bold)
        {
            using (Font font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel))
            using (Brush brush = new SolidBrush(color)) g.DrawString(text, font, brush, x, y);
        }
        private void Card(Graphics g, float x, float y, float width, float height)
        {
            using (GraphicsPath shadow = ModernDrawing.Round(new RectangleF(x + 1, y + 3, width - 2, height - 2), 14))
            using (Brush brush = new SolidBrush(Color.FromArgb(230, 237, 246))) g.FillPath(brush, shadow);
            using (GraphicsPath path = ModernDrawing.Round(new RectangleF(x, y, width - 1, height - 3), 14))
            using (Brush brush = new SolidBrush(Color.White))
            using (Pen pen = new Pen(Color.FromArgb(221, 230, 241))) { g.FillPath(brush, path); g.DrawPath(pen, path); }
        }
        private void Chip(Graphics g, string text, float x, float y, float width, Color color)
        {
            using (GraphicsPath path = ModernDrawing.Round(new RectangleF(x, y, width, 30), 15))
            using (Brush brush = new SolidBrush(Color.FromArgb(20, color))) g.FillPath(brush, path);
            using (Brush dot = new SolidBrush(color)) g.FillEllipse(dot, x + 12, y + 11, 7, 7);
            TextAt(g, text, 12, color, x + 26, y + 6, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); SessionSnapshot s = Snapshot; if (s == null) return;
            Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            // 横向位置、宽度与字号只随窗口宽度变化；纵向高度独立分配。
            float scale = LayoutScale; if (scale <= 0) return;
            float statisticsHeight = StatisticsHeight;
            float rowStep = (statisticsHeight - 74) / 7;
            g.ScaleTransform(scale, scale);
            TextAt(g, "姿态观察台", 26, ink, 0, 1, true);
            TextAt(g, "POSTURE INSIGHTS    /    让每一段记录清晰可见", 11, muted, 2, 36, false);
            Chip(g, s.Mode == "demo" ? "演示会话 · 模拟数据" : "实时会话 · 设备数据", 918, 10, 215, s.Mode == "demo" ? Color.FromArgb(178, 114, 24) : accent);
            Card(g, 0, 64, 465, 188);
            string name = "未识别 / 无数据";
            foreach (PostureDefinition p in PostureCatalog.Items) if (p.Key == s.CurrentKey) name = p.Name;
            TextAt(g, "当前姿态", 12, muted, 22, 82, false);
            TextAt(g, s.CurrentKey == "unknown" ? "等待数据" : name, 26, ink, 20, 112, true);
            TextAt(g, s.CurrentKey == "unknown" ? "连接设备后开始观察" : "持续 " + Duration(s.ElapsedSeconds - s.CurrentSinceSeconds), 13, muted, 22, 148, false);
            Chip(g, s.CurrentKey == "unknown" ? "未识别 / 无数据" : "正在记录", 22, 182, 172, s.CurrentKey == "unknown" ? muted : Color.FromArgb(13, 149, 139));
            TextAt(g, "2 秒超时自动转为未识别", 11, muted, 22, 231, false);
            DrawPerson(g, new RectangleF(236, 78, 200, 160), s.CurrentKey);
            double upright = 0;
            foreach (PostureDuration row in s.Rows) if (row.Key == "upright") upright = row.Seconds;
            Metric(g, 481, "总运行时间", Duration(s.ElapsedSeconds), "从本会话启动计时", accent);
            Metric(g, 704, "有效记录时长", Duration(s.ObservedSeconds), "无数据 " + Duration(s.UnknownSeconds), Color.FromArgb(13, 149, 139));
            Metric(g, 927, "正坐占比", s.ObservedSeconds > 0 ? (upright / s.ObservedSeconds * 100).ToString("0.0") + "%" : "—", "分母为有效记录时长", Color.FromArgb(13, 149, 139));
            Card(g, 0, 266, 720, statisticsHeight); Card(g, 736, 266, 400, statisticsHeight);
            TextAt(g, "累计姿态分布", 16, ink, 22, 280, true);
            TextAt(g, "累计时长  /  最长连续  /  次数", 11, muted, 458, 284, false);
            int n = 0;
            foreach (PostureDuration row in s.Rows)
            {
                if (row.Key == "unknown") continue;
                float y = 312 + n * rowStep;
                TextAt(g, row.Name, 12, row.Key == s.CurrentKey ? accent : ink, 22, y - 2, row.Key == s.CurrentKey);
                using (GraphicsPath path = ModernDrawing.Round(new RectangleF(125, y + 4, 285, 8), 4))
                using (Brush bg = new SolidBrush(Color.FromArgb(236, 241, 248))) g.FillPath(bg, path);
                float barWidth = (float)(285 * displayedBars[n]);
                if (barWidth > 1)
                    using (GraphicsPath path = ModernDrawing.Round(new RectangleF(125, y + 4, barWidth, 8), 4))
                    using (Brush bar = new SolidBrush(row.Key == "upright" ? Color.FromArgb(13, 149, 139) : accent)) g.FillPath(bar, path);
                TextAt(g, Duration(row.Seconds) + "   /   " + Duration(row.LongestSeconds) + "   /   " + row.Visits, 12, muted, 437, y - 2, false); n++;
            }
            TextAt(g, "最近姿态切换", 16, ink, 758, 280, true);
            TextAt(g, s.Changes + " 次切换 · " + s.ValidFrames + " 条有效记录", 11, muted, 758, 306, false);
            int count = 0;
            for (int i = s.RecentSegments.Count - 1; i >= 0 && count < 6; i--)
            {
                PostureSegment segment = s.RecentSegments[i]; string label = "未识别 / 无数据";
                foreach (PostureDefinition p in PostureCatalog.Items) if (p.Key == segment.Key) label = p.Name;
                float y = 334 + count * (statisticsHeight - 96) / 5;
                using (Brush dot = new SolidBrush(segment.Key == "unknown" ? muted : accent)) g.FillEllipse(dot, 758, y + 5, 6, 6);
                TextAt(g, s.StartedUtc.ToLocalTime().AddSeconds(segment.StartSeconds).ToString("HH:mm:ss"), 11, muted, 777, y, false);
                TextAt(g, label, 12, ink, 859, y - 1, false); count++;
            }
            if (count == 0) TextAt(g, "接收第一条有效姿态后开始记录", 12, muted, 758, 334, false);
        }
        private void Metric(Graphics g, float x, string title, string value, string note, Color color)
        {
            Card(g, x, 64, 209, 188);
            using (GraphicsPath icon = ModernDrawing.Round(new RectangleF(x + 20, 80, 28, 28), 8))
            using (Brush brush = new SolidBrush(Color.FromArgb(20, color))) g.FillPath(brush, icon);
            using (Pen pen = new Pen(color, 2)) { g.DrawEllipse(pen, x + 26, 86, 16, 16); g.DrawLine(pen, x + 34, 89, x + 34, 95); g.DrawLine(pen, x + 34, 95, x + 38, 95); }
            TextAt(g, title, 13, muted, x + 20, 120, false);
            TextAt(g, value, 25, ink, x + 17, 149, true); TextAt(g, note, 11, muted, x + 20, 226, false);
        }
        private void DrawPerson(Graphics g, RectangleF destination, string key)
        {
            int index = -1;
            foreach (PostureDefinition posture in PostureCatalog.Items) if (posture.Key == key) index = posture.Id;
            if (index >= 0 && AtlasAvailable)
            {
                Rectangle cell = AtlasCell(index); g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                float factor = Math.Min(destination.Width / cell.Width, destination.Height / cell.Height);
                RectangleF fit = new RectangleF(destination.X + (destination.Width - cell.Width * factor) / 2, destination.Y + (destination.Height - cell.Height * factor) / 2, cell.Width * factor, cell.Height * factor);
                g.DrawImage(atlas, fit, cell, GraphicsUnit.Pixel); return;
            }
            float cx = destination.X + destination.Width / 2, cy = destination.Y + destination.Height / 2;
            using (Brush brush = new SolidBrush(Color.FromArgb(240, 245, 251))) g.FillEllipse(brush, cx - 66, cy - 66, 132, 132);
            using (Pen pen = new Pen(Color.FromArgb(198, 213, 232), 3)) g.DrawArc(pen, cx - 35, cy - 35, 70, 70, 25, 285);
            TextAt(g, index < 0 ? "等待识别" : "姿态图加载中", 12, muted, cx - 30, cy + 47, false);
        }
    }
}
