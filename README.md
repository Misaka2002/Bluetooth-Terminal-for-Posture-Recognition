# HC-06 蓝牙串口终端

面向 Windows PC 的轻量串口文本终端，用于接收 STM32 经 HC-06 发送的姿态数据，也可发送文本命令。程序通过 Windows 配对后创建的虚拟串口（SPP）通信，不负责搜索或配对蓝牙设备。

## 文件

- `BluetoothTerminal.exe`：Windows 可运行版本。
- `BluetoothTerminal.cs`：WinForms 源码。
- `Build.ps1`：使用 Windows 自带的 .NET Framework C# 编译器构建 EXE，不下载第三方依赖。

## 连接 HC-06

1. 在 Windows 蓝牙设置中配对 HC-06。
2. 在 Windows 的蓝牙 COM 端口设置或设备管理器中找到 HC-06 的**传出（Outgoing）**串口。COM 编号可能变化；不要默认把 NUCLEO 的 ST-LINK 虚拟串口当作 HC-06。
3. 启动 `BluetoothTerminal.exe`，点“刷新”，选择 HC-06 传出 COM 口，波特率设为 `9600`，然后点“连接”。
4. 串口格式固定为 8N1、无流控。该波特率必须与 HC-06 UART 和 STM32 USART 配置一致。

本项目使用 HC-06 经典蓝牙 SPP。电脑需要有可用的经典蓝牙适配器及驱动。串口打开成功只表示 Windows 打开了该 COM 口，不单独证明蓝牙数据正在到达。

## 接收与行处理

串口回调每次读到的数据量不等于一条消息：有时是一行的一部分，有时会包含多行。程序按 LF（`\n`）缓存并组装完整行，因此接收区和保存日志只包含收到行结束符的记录。传输过程中尚未收到结束符的尾部片段会留在缓冲区；保存日志时不写入该片段，断开连接时会提示它未计入日志。

终端按 UTF-8 解码并显示文本，不解析字段、不绘图，也不提供二进制/十六进制查看。当前姿态固件使用的文本行示例：

```text
SEQ=10,FB_CDEG=2,LR_CDEG=-14,ID=0,CONF_PCT=99
```

其中 `FB_CDEG` 和 `LR_CDEG` 是角度乘以 100，`ID` 是类别编号，`CONF_PCT` 是置信度乘以 100。终端只负责完整行显示和日志保存；字段解释由上层应用完成。

## 界面操作

- **刷新**：重新枚举 Windows COM 口。
- **连接 / 断开**：打开或释放选中的串口。
- **发送**：发送输入框中的 UTF-8 文本；可选追加 CRLF。设备端需实现对应的接收/回显逻辑才会有响应。
- **清屏**：清空显示、已缓存完整行、尚未完成的行和接收计数。
- **保存日志**：将清屏以来收到的完整行保存为带 UTF-8 BOM 的 `.txt` 文件，不包含发送记录。

长时间接收时，屏幕显示超过 200000 个字符后会裁掉较早的显示内容；保存日志的完整行缓冲仍保留到点击“清屏”或关闭程序。断开或无线链路中断后，应检查设备并重新连接。Windows 驱动不一定会立即报告无线断开。

## 从源码构建

在此目录打开 PowerShell，运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1
```

脚本调用 Windows .NET Framework 4.x 编译器，在当前目录生成 `BluetoothTerminal.exe`。此命令只对本次 PowerShell 进程指定执行策略，不会永久修改系统策略。

## 验证记录

- 2026-09-30：源码通过目录内 `Build.ps1` 编译。
- 硬件链路：项目使用者报告 HC-06 通过 Windows 传出 COM 口和 9600 baud 正常接收 STM32 姿态数据；COM 编号以各台 Windows 当前分配为准。
