# Monitor LG

A Windows tray icon that turns an LG monitor off and on by cutting its signal. Tested with mouse and keyboard input
simulated by software: it didn't wake the monitor.

## Usage

* Left-click the icon or press `Ctrl+Alt+F10`: turns the monitor off or on.
* Right-click > *Salir* (Exit): turns the monitor on and closes the app.
* From the command line: `MonitorLG.exe apagar | encender | alternar | salir` (off | on | toggle | exit).

## Installation

Requirements: Windows x64 with .NET Framework 4.x and the Parsec VDD driver (tested with 0.45).

Run `dist\MonitorLGSetup.exe`. It installs for the current user, without admin, in `%LOCALAPPDATA%\Programs\MonitorLG`,
with a Start menu shortcut and optional start with Windows. Uninstall it from Settings > Apps.

Without a window: `MonitorLGSetup.exe /S`, plus `/sin-inicio` to keep it from starting with Windows.

## Building

`build.cmd`. It uses the `csc.exe` included with Windows, with no dependencies.

## How it works

It doesn't use DDC/CI to turn the monitor off: on the LG ULTRAGEAR tested (GSM5BB2, DisplayPort), values `04` and `05`
of code `D6` were sent without error and the monitor kept reporting "on".

Instead, it creates a virtual screen with Parsec VDD, moves the desktop there and disables the LG's output. That
display layout isn't saved in Windows. If the app is closed abruptly, the driver removes the virtual screen and
Windows goes back to the LG.

## Limitations

* It isn't the same as turning it off with the power button: the monitor is left without a signal.
* It treats every monitor with the `GSM` manufacturer code as an LG, and turns all of them off.
* Not tested: physical mouse and keyboard, clicking the icon, start with Windows after a reboot, and sleep or signing
  out with the monitor off.
* The log is written to `%TEMP%\MonitorLG.log`.
