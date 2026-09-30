using System;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Windows.Forms;

namespace WearableBluetoothTerminal
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TerminalForm());
        }
    }

    // HC-06 配对后，Windows 将串行端口配置文件 (SPP) 暴露为 COM 口。
    // 此窗口只打开该 COM 口，不修改蓝牙模块名称、配对码或 AT 参数。
    internal sealed class TerminalForm : Form
    {
        private readonly ComboBox ports = new ComboBox();
        private readonly ComboBox baud = new ComboBox();
        private readonly Button refresh = new Button();
        private readonly Button connect = new Button();
        private readonly Button send = new Button();
        private readonly TextBox outgoing = new TextBox();
        private readonly CheckBox newline = new CheckBox();
        private readonly RichTextBox received = new RichTextBox();
        private readonly ToolStripStatusLabel status = new ToolStripStatusLabel();
        private SerialPort activePort;
        private bool closing;
        private long byteCount;
        // 只将已经收到 LF 的完整行显示并保存；未完成的行暂存在 pendingLine。
        private readonly StringBuilder receiveLog = new StringBuilder();
        private readonly StringBuilder pendingLine = new StringBuilder();

        public TerminalForm()
        {
            Text = "简易蓝牙终端 · HC-06";
            Font = new Font("Microsoft YaHei UI", 10F);
            ClientSize = new Size(960, 640);
            MinimumSize = new Size(760, 480);
            StartPosition = FormStartPosition.CenterScreen;

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(14);
            layout.ColumnCount = 1;
            layout.RowCount = 4;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            Controls.Add(layout);

            FlowLayoutPanel connection = new FlowLayoutPanel();
            connection.Dock = DockStyle.Fill;
            connection.WrapContents = false;
            connection.Controls.Add(new Label { Text = "COM 口", AutoSize = true, Margin = new Padding(0, 8, 8, 0) });
            ports.Width = 125;
            ports.DropDownStyle = ComboBoxStyle.DropDownList;
            connection.Controls.Add(ports);
            refresh.Text = "刷新";
            // 按按钮文字和系统字体自适应宽度，并保留足够内边距，避免高 DPI 下文字被裁切。
            refresh.AutoSize = true;
            refresh.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            refresh.MinimumSize = new Size(96, 34);
            refresh.Padding = new Padding(10, 2, 10, 2);
            refresh.Click += delegate { RefreshPorts(); };
            connection.Controls.Add(refresh);
            connection.Controls.Add(new Label { Text = "波特率", AutoSize = true, Margin = new Padding(14, 8, 8, 0) });
            baud.Width = 105;
            baud.DropDownStyle = ComboBoxStyle.DropDownList;
            baud.Items.AddRange(new object[] { "1200", "2400", "4800", "9600", "19200", "38400", "57600", "115200" });
            baud.SelectedItem = "9600";
            connection.Controls.Add(baud);
            connect.Text = "连接";
            // 连接按钮也会切换为“断开”；留出比刷新按钮更宽的最小区域。
            connect.AutoSize = true;
            connect.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            connect.MinimumSize = new Size(110, 34);
            connect.Padding = new Padding(10, 2, 10, 2);
            connect.Click += delegate { if (activePort == null) ConnectPort(); else DisconnectPort("已断开连接"); };
            connection.Controls.Add(connect);
            layout.Controls.Add(connection, 0, 0);

            Label hint = new Label();
            hint.Dock = DockStyle.Fill;
            hint.Text = "先在 Windows 中配对 HC-06，再选择它的传出（Outgoing）COM 口。串口格式固定为 8N1，无流控。";
            hint.ForeColor = Color.FromArgb(65, 75, 90);
            layout.Controls.Add(hint, 0, 1);

            received.Dock = DockStyle.Fill;
            received.ReadOnly = true;
            received.BackColor = Color.FromArgb(247, 249, 252);
            received.Font = new Font("Consolas", 11F);
            received.WordWrap = false;
            received.DetectUrls = false;
            layout.Controls.Add(received, 0, 2);

            TableLayoutPanel compose = new TableLayoutPanel();
            compose.Dock = DockStyle.Fill;
            compose.Padding = new Padding(0, 12, 0, 0);
            compose.ColumnCount = 5;
            compose.RowCount = 1;
            compose.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            compose.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
            compose.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 75));
            compose.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 75));
            compose.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
            outgoing.Dock = DockStyle.Fill;
            outgoing.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SendText(); } };
            compose.Controls.Add(outgoing, 0, 0);
            newline.Text = "追加 CRLF";
            newline.Checked = true;
            newline.Dock = DockStyle.Fill;
            compose.Controls.Add(newline, 1, 0);
            send.Text = "发送";
            send.Dock = DockStyle.Fill;
            send.Enabled = false;
            send.Click += delegate { SendText(); };
            compose.Controls.Add(send, 2, 0);
            Button clear = new Button { Text = "清屏", Dock = DockStyle.Fill };
            clear.Click += delegate { received.Clear(); receiveLog.Clear(); pendingLine.Clear(); byteCount = 0; UpdateStatus("显示及接收日志已清空"); };
            compose.Controls.Add(clear, 3, 0);
            Button save = new Button { Text = "保存日志", Dock = DockStyle.Fill };
            save.Click += delegate { SaveLog(); };
            compose.Controls.Add(save, 4, 0);
            layout.Controls.Add(compose, 0, 3);

            StatusStrip strip = new StatusStrip();
            strip.Items.Add(status);
            Controls.Add(strip);
            strip.Dock = DockStyle.Bottom;
            FormClosing += delegate { closing = true; DisconnectPort("已关闭"); };
            RefreshPorts();
        }

        private void RefreshPorts()
        {
            string selected = ports.SelectedItem as string;
            try
            {
                string[] names = SerialPort.GetPortNames();
                Array.Sort(names, StringComparer.OrdinalIgnoreCase);
                ports.Items.Clear();
                ports.Items.AddRange(names);
                if (selected != null && ports.Items.Contains(selected)) ports.SelectedItem = selected;
                else if (ports.Items.Count > 0) ports.SelectedIndex = 0;
                UpdateStatus(names.Length == 0 ? "未发现 COM 口，请先配对 HC-06" : "请选择 HC-06 的传出 COM 口，然后连接");
            }
            catch (Exception ex) { ShowError("读取 COM 口失败", ex); }
        }

        private void ConnectPort()
        {
            if (ports.SelectedItem == null) { UpdateStatus("没有选择 COM 口，请先配对设备并刷新"); return; }
            SerialPort candidate = new SerialPort((string)ports.SelectedItem, Int32.Parse((string)baud.SelectedItem), Parity.None, 8, StopBits.One);
            candidate.Handshake = Handshake.None;
            candidate.DtrEnable = false;
            candidate.RtsEnable = false;
            candidate.Encoding = new UTF8Encoding(false);
            candidate.ReadTimeout = 500;
            candidate.WriteTimeout = 500;
            candidate.DataReceived += OnDataReceived;
            candidate.ErrorReceived += OnSerialError;
            // 先保存实例，事件回调才能判断数据是否属于当前连接。
            activePort = candidate;
            try
            {
                candidate.Open();
                SetConnectionControls(true);
                UpdateStatus("已打开 " + candidate.PortName + "；等待接收（打开成功不代表已收到蓝牙数据）");
            }
            catch (Exception ex)
            {
                DisconnectPort("连接失败");
                ShowError("无法打开端口。请检查端口是否选对，以及是否被其他串口助手占用", ex);
            }
        }

        private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            SerialPort source = (SerialPort)sender;
            try
            {
                // ReadExisting 不等待凑满一行；SerialPort 内部解码可处理分批到达的 UTF-8 字符。
                string text = source.ReadExisting();
                if (text.Length == 0) return;
                PostToUi(delegate
                {
                    // 断开后已经排入队列的旧事件不能污染新连接。
                    if (activePort != source) return;
                    byteCount += Encoding.UTF8.GetByteCount(text);
                    // ReadExisting 每次读取可能只有半行，也可能包含多行；仅在收到 LF 后显示完整行。
                    StringBuilder completeLines = new StringBuilder();
                    for (int i = 0; i < text.Length; i++)
                    {
                        pendingLine.Append(text[i]);
                        if (text[i] == '\n')
                        {
                            completeLines.Append(pendingLine.ToString());
                            pendingLine.Clear();
                        }
                    }

                    if (completeLines.Length > 0)
                    {
                        string lines = completeLines.ToString();
                        receiveLog.Append(lines);
                        received.AppendText(lines);
                        // 长时间运行时限制屏幕文本量；接收日志只保留完整行。
                        if (received.TextLength > 200000) { received.Select(0, received.TextLength - 150000); received.SelectedText = ""; }
                        received.SelectionStart = received.TextLength;
                        received.ScrollToCaret();
                    }

                    string pendingStatus = pendingLine.Length > 0 ? "（当前行接收中）" : "";
                    UpdateStatus("已接收 " + byteCount + " 字节（按 UTF-8 文本计数） · " + source.PortName + pendingStatus);
                });
            }
            catch (Exception ex)
            {
                PostToUi(delegate { if (activePort == source) DisconnectPort("接收中断：" + ex.Message); });
            }
        }

        private void OnSerialError(object sender, SerialErrorReceivedEventArgs e)
        {
            SerialPort source = (SerialPort)sender;
            PostToUi(delegate { if (activePort == source) UpdateStatus("串口报告错误：" + e.EventType + "，请检查波特率及连接"); });
        }

        private void PostToUi(Action action)
        {
            // 串口事件运行于后台线程。只能通过 BeginInvoke 更新控件，
            // 并使用异步方式，避免 UI 线程 Close() 等待串口事件时互相阻塞。
            if (closing || IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(new Action(delegate { if (!closing && !IsDisposed) action(); })); }
            catch (InvalidOperationException) { /* 窗口可能在排队期间关闭。 */ }
        }

        private void DisconnectPort(string message)
        {
            SerialPort old = activePort;
            activePort = null;
            bool hasIncompleteTail = pendingLine.Length > 0;
            pendingLine.Clear();
            if (old != null)
            {
                old.DataReceived -= OnDataReceived;
                old.ErrorReceived -= OnSerialError;
                try { old.Close(); } catch (Exception) { /* 拔出或无线连接中断时仍继续释放资源。 */ }
                try { old.Dispose(); } catch (Exception) { /* 确保窗口仍可关闭或重新连接。 */ }
            }
            SetConnectionControls(false);
            UpdateStatus(hasIncompleteTail ? message + "（末尾未完成的数据未计入日志）" : message);
        }

        private void SetConnectionControls(bool connected)
        {
            ports.Enabled = baud.Enabled = refresh.Enabled = !connected;
            send.Enabled = connected;
            connect.Text = connected ? "断开" : "连接";
        }

        private void SendText()
        {
            SerialPort port = activePort;
            if (port == null) { UpdateStatus("请先连接 COM 口"); return; }
            string text = outgoing.Text + (newline.Checked ? "\r\n" : "");
            if (text.Length == 0) return;
            try { port.Write(text); UpdateStatus("已提交发送 " + Encoding.UTF8.GetByteCount(text) + " 字节；是否到达由接收端确认"); }
            catch (Exception ex) { DisconnectPort("发送失败：" + ex.Message); }
        }

        private void SaveLog()
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "UTF-8 文本日志 (*.txt)|*.txt";
                dialog.FileName = "HC06_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt";
                dialog.InitialDirectory = AppDomain.CurrentDomain.BaseDirectory;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(dialog.FileName, receiveLog.ToString(), new UTF8Encoding(true));
                    UpdateStatus(pendingLine.Length > 0
                        ? "完整数据行已保存；末尾未完成片段未写入：" + dialog.FileName
                        : "接收日志已保存：" + dialog.FileName);
                }
                catch (Exception ex) { ShowError("保存日志失败", ex); }
            }
        }

        private void UpdateStatus(string message) { status.Text = message; }
        private void ShowError(string message, Exception ex)
        {
            UpdateStatus(message);
            MessageBox.Show(this, message + "\r\n\r\n" + ex.Message, "简易蓝牙终端", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
