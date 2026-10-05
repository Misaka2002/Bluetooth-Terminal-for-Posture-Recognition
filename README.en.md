# Posture Dashboard

[中文版](README.md)

Posture Dashboard is a Windows Bluetooth terminal that turns incoming posture classifications into duration statistics. It helps you see which postures you held during a session and how long each lasted.

The interface shows the current posture with a 3D illustration, alongside accumulated time, the longest continuous interval, occurrence counts, the upright share, and recent posture changes. Results are saved automatically and can be exported as CSV for analysis in Excel. The interface labels are in Chinese.

![Posture Dashboard in demo mode](docs/images/overview.png)

## Getting started

The application requires Windows and .NET Framework 4.x. If you already have a compiled copy, open `PostureStatisticsTerminal.exe`. For a source checkout, build it using the instructions below first.

Pair the HC-06 in Windows Bluetooth settings, select its outgoing COM port in the app, and click **连接设备** (Connect). The serial settings are **9600 baud, 8 data bits, no parity, 1 stop bit, and no flow control**. Close any other serial tool using the port.

Valid posture records update the illustration and statistics automatically. **原始终端** (Raw terminal) hides or restores the receive log while reception, timing, and saving continue. **清屏** (Clear) clears only the displayed text; it does not reset statistics or saved logs.

Without a device, click **演示模式** (Demo mode) or run `Demo.cmd`. Entering or leaving demo mode starts a separate session, keeping simulated data separate from device measurements. **新统计会话** (New statistics session) saves the current session and starts timing from zero.

## Device messages

Send posture names in newline-terminated records, for example:

```text
POSTURE=upright
POSTURE=forward_lean
```

Eight postures are supported. Left and right refer to the person's own perspective.

| Device label | Meaning | Interface label |
| --- | --- | --- |
| `upright` | Upright sitting | 正坐 |
| `forward_lean` | Forward lean | 前倾 |
| `backward_lean` | Backward lean | 后仰 |
| `left_lean` | Left lean | 左倾 |
| `right_lean` | Right lean | 右倾 |
| `forward_hunch` | Forward hunch | 前弯含胸 |
| `left_hunch` | Left hunch | 左弯含胸 |
| `right_hunch` | Right hunch | 右弯含胸 |

The parser also accepts `LABEL=<name>` fields and standalone posture names, without case sensitivity. Messages containing only a numeric `ID` are displayed in the log but do not contribute to statistics, because class mappings can differ between models.

## How timing works

Timing begins when the terminal creates the current session. The first valid posture starts its interval. A different posture ends that interval and starts the next; repeated reports of the same posture remain one continuous interval.

Disconnecting ends the current posture immediately. If no new valid posture arrives for **2 seconds**, the state becomes unrecognized/no data. Time before connection, after disconnection, and after a timeout is tracked separately rather than assigned indefinitely to the last posture.

Posture percentages use the total recognized duration as their denominator. The longest continuous duration is the longest uninterrupted interval of a posture. Occurrence counts include intervals with positive duration. Receiving the same posture again after a disconnection or timeout starts a new interval.

The terminal uses the arrival time of complete data lines. It cannot recover activity before the session began, and switching-time precision depends on the device's reporting interval and wireless delay.

## Saving and exporting

Each session is stored in `sessions/` beside the application. Statistics are saved every 30 seconds and when disconnecting, changing modes, creating a new session, or closing normally. Reopening the app starts a new session; previous records stay on disk.

| File | Contents |
| --- | --- |
| `summary.csv` | Accumulated duration, share, longest interval, and occurrence count for each posture |
| `timeline.csv` | Start, end, and duration of each interval |
| `session.json` | Session metadata, data source, and summary metrics |
| `received.log` | Timestamped receive log |

**导出 CSV** (Export CSV) saves the current statistics in a new subdirectory of your chosen destination, avoiding overwriting previous exports. CSV files use UTF-8 with a BOM for Chinese text in Excel, and durations are recorded in seconds. **保存日志** (Save log) exports the receive log separately.

If the process is forcibly terminated, statistics since the most recent save may be lost. Normal exit saves a final snapshot, and the interface reports saving failures.

## Building from source

Run this in the project directory:

```powershell
powershell -ExecutionPolicy Bypass -File .\Build.ps1
```

The script uses the Windows .NET Framework C# compiler without downloading NuGet dependencies. It writes `PostureStatisticsTerminal.exe` to the project root. The 3D atlas is embedded in the executable, so no network access or separate image copy is needed at runtime.

Run the timing-core tests with:

```powershell
powershell -ExecutionPolicy Bypass -File .\Build.ps1 -Test
```

Tests cover posture parsing, fragmented input, posture changes, disconnection and timeout handling, duration conservation, and CSV export. Generated test output goes into `.build/`, separate from real sessions.

## Project layout

```text
src/        Application source and manifest
tests/      Timing-core tests
assets/     3D posture atlas used during compilation
docs/       Interface screenshot and asset notes
Build.ps1   Build and test entry point
Demo.cmd    Demo-mode launcher
```

`src/PostureCore.cs` handles parsing and timing, `src/TerminalForm.cs` handles the interface and serial reception, and `src/ModernControls.cs` provides rounded controls. See the [asset notes](docs/assets.md) for the atlas source and cell mapping.

Local sessions, test output, and generated executables are excluded by `.gitignore` and are not committed as source.
