# <img src="https://github.com/user-attachments/assets/9c24a111-c538-4227-a83f-de10ff9cd0e2" width="50" height="50" alt="LockScreenLogo" style="vertical-align: middle; margin-right: 8px;" /> Suspended N Time

[![GitHub release (latest by date)](https://img.shields.io/github/v/release/BassemMohsen/SuspendedNTime?style=for-the-badge&color=blue)](https://github.com/BassemMohsen/SuspendedNTime/releases/latest)
[![GitHub all releases](https://img.shields.io/github/downloads/BassemMohsen/SuspendedNTime/total?style=for-the-badge&color=green)](https://github.com/BassemMohsen/SuspendedNTime/releases)
![Build Status](https://img.shields.io/github/actions/workflow/status/BassemMohsen/SuspendedNTime/dotnet-desktop.yml?branch=main&style=for-the-badge&label=Build)
![GitHub Repo stars](https://img.shields.io/github/stars/BassemMohsen/SuspendedNTime?style=for-the-badge&color=yellow&label=Stars)

⚠️ Technology Preview
Please report bugs or unexpected behavior [here](https://github.com/BassemMohsen/SuspendedNTime/issues)

⚠️ Online Games
Suspending competitive or online games may trigger anti-cheat systems or force a return to the lobby.

⚠️ Game Engine Behavior
Many games are not designed to be frozen mid-execution. Unexpected behavior is possible.


**Suspended N Time Xbox Game Bar Widget** is an Xbox Game Bar widget that allows you to pause and resume games — even titles that do not support pausing natively.
It’s especially useful when you need to temporarily freeze a game or application to free CPU and GPU resources for another task, without closing anything.

<img width="1920" height="1200" alt="Screenshot 2025-12-07 093456" src="https://github.com/user-attachments/assets/268c70af-a31d-42f2-b0fa-85bf88ae255c" />


## How it works

Suspended N Time uses low-level Windows kernel APIs:

- NtSuspendProcess()
- NtResumeProcess()

These API calls freeze and unfreeze the game (and all its child processes) directly in memory.

<img width="1920" height="536" alt="Screenshot 2025-12-07 093615" src="https://github.com/user-attachments/assets/6c962a86-71a7-40c8-85be-48a1080b1c71" />

*When a game is suspended, its resources usage:*

- CPU usage: 0%
- GPU usage: 0%
- RAM usage: unchanged

If enough applications are suspended to exceed physical RAM, Windows will move their pages to disk — standard virtual memory behavior.

*Suspend before Sleep:* Detects when going to low power (S0ix) mode and suspend foreground game process.
Many gaming laptops/desktops keep games running in the background when entering S0ix/Modern Standby → wasting battery or causing heat.
Automatically suspending prevents:
- High idle power usage
* GPU/CPU burn during sleep transitions
Running the game when the user doesn’t intend to

*Suspend on focus change:* Detects when user switches away from the game and suspend game process automatically.
Many players want to alt-tab and stop CPU/GPU burn instantly.
It also makes it easier switching to other games or back to the game launcher.

## Enhanced Power Optimization

Suspended N Time can apply an enhanced power profile optimized for Modern Standby (S0ix):

- Disables wake timers
- Enables disconnected standby (Wi-Fi and mobile radios off)
- Maximizes PCI Express power savings
- Allows CPU to idle down to 0%

<img width="1920" height="1200" alt="Screenshot 2025-12-07 093719" src="https://github.com/user-attachments/assets/ea99e7b0-c2e6-4f93-8e34-bfbdd1deea20" />

## Re-Sleep

Re-Sleep acts as a safety net against Windows modern sleep random wakeups that can be triggerd by OEM BIOS, or by drivers from Silicon vendors.

- Re-Sleep listens to Windows Kernel Power events for entering, exiting modern sleep, and the wakeup causes for exiting low power mode.
- Re-Sleep automatically returns the device back to Modern Standby (S0ix) if it wakes unexpectedly.
- If the system wakes without the user pressing the power button, it immediately returns back to low power S0ix.

💡 If you use Hibernate, you do not need Re-Sleep — leave it disabled.


# ☕ Support Me
If you like my work, you can buy me a coffee:   [![Donate via PayPal](https://img.shields.io/badge/Donate-PayPal-blue.svg)](https://paypal.me/bassemnomany)

# [:floppy_disk: Download](https://apps.microsoft.com/detail/9P74CN323T0M)

Suspended N Time is now officially available on the Microsoft Store!  
Click the badge below open directly: 

<a href="https://apps.microsoft.com/detail/9P74CN323T0M">
  <img
    src="https://get.microsoft.com/images/en-us%20dark.svg"
    alt="Get it from Microsoft Store"
    style="width: 320px; max-width: 100%; height: auto;"
  />
</a>


# Supported Devices
It should be compatiable with any Windows PC. However, I have only tested on the following hardware:
- Windows Desktop with AMD 7950X3D and Nvidia RTX GPU
- Handheld PC: MSI Claw 8 AI+ A2VM with Intel Lunar Lake.

# Bugs & Features
Found a bug and want it fixed? Have an idea for a new feature?
Please [open an issue](https://github.com/BassemMohsen/SuspendedNTime/issues) in the tracker.  

# Credits & Libraries
- [Merrit/nyrna](https://github.com/Merrit/nyrna)

Suspended N Time have drawn some inspiration for use cases from:
- XBox [Quick Resume](https://www.pcmag.com/how-to/how-to-switch-between-games-with-quick-resume-on-xbox-series-x-series-s)
- Decky Load Steam Deck Plugin [Pause Game](https://github.com/popsUlfr/SDH-PauseGames)

# Limitations & Frequently Asked Questions

❗ You cannot use Alt+Tab / Task Switcher to switch to a suspended game
Suspended processes cannot respond to compositor signals.
Because of this, Windows cannot complete the switch to a suspended window.
**Solution** To switch back to a game:
- Resume the game first from the Game Bar widget.
- Then Alt+Tab or Task Switcher will work normally.

❗ Windows may show “Not Responding – Wait or End Process?”
This is expected.
When suspended, a game cannot respond to system messages, so Windows assumes it is frozen.
**Solution**  Simply resume the game — the dialog will disappear immediately.

❗ Can't close suspended game”
This is expected.
When suspended, a game cannot respond to terminate or close signal.
**Solution**  Simply resume the game first— then close it after.

❗ System utilities or OEM tools may appear in the game list
Examples:
Armoury Crate
Windows dialogue popups
OEM overlays
Internal Windows processes
**Do not suspend these.**
If a non-game appears, report the process name and it will be added to the ignore list.

❗ Re-Sleep may not work reliably when games are suspended
Windows broadcasts a “prepare for low-power mode” signal before entering sleep.
Suspended games cannot respond with acknowledgment, which can prevent or delay automatic sleep.

❗ Display may turn off after resuming a game
If the PC wakes from sleep and you later resume a suspended game, the game may process a previously queued “low-power mode” signal.
This can cause Windows to briefly re-enter display sleep.
   
