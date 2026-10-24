# PresentMon minimal FPS collector

Version: 2.6.0, upstream standalone x64 console release (not the service/MSI/GUI).

Executable source: https://github.com/GameTechDev/PresentMon/releases/download/v2.6.0/PresentMon-2.6.0-x64.exe

Release: https://github.com/GameTechDev/PresentMon/releases/tag/v2.6.0

Corresponding source and notices: https://github.com/GameTechDev/PresentMon/tree/v2.6.0

License source: https://raw.githubusercontent.com/GameTechDev/PresentMon/v2.6.0/LICENSE.txt

The original executable is embedded in RevoHelper. Enabling the performance monitor extracts the collector into the current user's versioned local cache and starts an owned hidden child process. This does not install a service, driver, or inject code into the measured application. Turning off the monitor stops its collector. The standalone upstream executable is otherwise unmodified. Retain LICENSE.txt in the source package and include it in Third-Party.txt when distributing RevoHelper.
