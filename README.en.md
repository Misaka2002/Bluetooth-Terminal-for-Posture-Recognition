# HC-06 Bluetooth Serial Terminal

[中文版](README.md)

A lightweight serial text terminal for Windows PCs. It receives posture data sent by an STM32 through an HC-06 and can also send text commands. The app communicates through the virtual serial (SPP) port created when Windows pairs with the module; it does not scan for or pair with Bluetooth devices itself.

## Files

- `BluetoothTerminal.exe`: ready-to-run Windows application.
- `BluetoothTerminal.cs`: WinForms source code.
- `Build.ps1`: builds the executable with the .NET Framework C# compiler included with Windows; no third-party dependencies are downloaded.

## Connect to the HC-06

1. Pair the HC-06 in Windows Bluetooth settings.
2. Find the HC-06 **outgoing** COM port in Windows Bluetooth COM port settings or Device Manager. The COM number may change. Do not mistake the NUCLEO ST-LINK virtual COM port for the HC-06 port.
3. Launch `BluetoothTerminal.exe`, click **Refresh**, select the HC-06 outgoing COM port, set the baud rate to `9600`, and click **Connect**.
4. The serial format is fixed at 8N1 with no flow control. The baud rate must match both the HC-06 UART and the STM32 USART configuration.

This project uses classic Bluetooth SPP through the HC-06. The PC needs a working classic Bluetooth adapter and driver. Successfully opening a COM port only confirms that Windows opened that port; it does not by itself prove that Bluetooth data is arriving.

## Receiving and line handling

Each serial read may contain part of a line or several lines. The app buffers data and assembles complete lines using LF (`\n`). Therefore, the receive view and saved logs include only records that have reached a line terminator. A trailing fragment without a terminator remains buffered; it is not written to the log, and the app reports when such a fragment is omitted on disconnect.

Incoming bytes are decoded as UTF-8 and displayed as text. The app does not parse fields, plot data, or provide binary/hex viewing. A text line currently used by the posture firmware looks like this:

```text
SEQ=10,FB_CDEG=2,LR_CDEG=-14,ID=0,CONF_PCT=99
```

`FB_CDEG` and `LR_CDEG` are angles multiplied by 100, `ID` is a class identifier, and `CONF_PCT` is confidence multiplied by 100. The terminal only displays complete lines and saves logs; interpretation of these fields belongs to the higher-level application.

## Interface

- **Refresh**: re-enumerates Windows COM ports.
- **Connect / Disconnect**: opens or releases the selected serial port.
- **Send**: sends the text in the input box as UTF-8. CRLF can optionally be appended. The device must implement the corresponding receive or echo behavior to respond.
- **Clear**: clears the display, cached complete lines, any unfinished line, and the receive counter.
- **Save Log**: saves complete lines received since the last clear to a `.txt` file with a UTF-8 BOM. Sent text is not included.

During long sessions, displayed text is trimmed after it exceeds 200,000 characters. The complete-line log buffer remains available until **Clear** is clicked or the app closes. If the connection is dropped or the wireless link is interrupted, check the device and reconnect. The Windows driver may not report a wireless disconnect immediately.

## Build from source

Open PowerShell in this directory and run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1
```

The script uses the .NET Framework 4.x compiler included with Windows and writes `BluetoothTerminal.exe` to the current directory. The execution policy option applies only to this PowerShell process; it does not permanently change the system policy.

## Validation notes

- **2026-09-30**: The source was compiled with the included `Build.ps1` script.
- **Hardware link**: The project owner reported successful reception of STM32 posture data through the HC-06 outgoing COM port at 9600 baud. The COM number depends on the current Windows assignment.
