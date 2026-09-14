# \# KPMN Development USB-Stick Suite

# 

# > A collection of 5 USB stick tools, each with its own distinct purpose, united under a single brand identity.

# 

# \*\*The KPMN Development brand\*\* is a hobby project by Felix Koopmann — Built with the help of Claude (Anthropic) as a Vibecoding project.

# 

# 📖 \*\*\[Technical Handbook (DE / EN)](https://felixkpm.github.io/kpmn-dev-usb-stick-suite/)\*\* — full walkthrough of the suite, the infrastructure, and the Suite Builder.

# 

# \---

# 

# \## ⚠️ Disclaimer

# 

# This is a \*\*hobby project\*\*. No maintenance, support, or continued development is guaranteed. The software is provided "as-is," without any warranty of functionality, merchantability, or fitness for a particular purpose.

# 

# Where applicable, this suite downloads software packages, drivers, installers, and ISO images directly from official vendor sources at runtime. \*\*No third-party software is hosted or redistributed by KPMN Development.\*\* All third-party software remains subject to the respective licenses of each vendor.

# 

# \*\*This suite performs destructive operations on removable drives.\*\* The Suite Builder erases target drives when setting up a new stick, and MABS repartitions the target disk entirely when installing Ventoy. Both actions are preceded by an explicit warning, but a wrong drive selection cannot be undone. Verify the target drive before confirming.

# 

# Please be aware that certain features — such as the Data Vault — carry a risk of data loss due to unexpected program behavior. The Vault has \*\*no password recovery\*\*: a lost master password means permanently lost data, by design. This software has not been extensively tested. Use it thoughtfully and keep backups of anything important.

# 

# `AEGIS.exe` is signed with a \*\*self-signed "Kpmn Development" certificate\*\*. This confirms only that the binary was built on the developer's own machine — it is not a trusted publisher certificate and implies no external review or verification.

# 

# \*\*KPMN Development assumes no liability for any data loss, file corruption, or damage to your system resulting from the use of this software, except in cases of intent or gross negligence. Use at your own risk.\*\*

# 

# This disclaimer and the use of this software are governed by the laws of the Federal Republic of Germany.

# 

# This project is licensed under \*\*CC BY-NC-SA 4.0\*\*. It may not be used for commercial purposes. See the LICENSE file for details. The license covers the KPMN Development source code and brand assets only — \*\*not\*\* any third-party software downloaded by these tools, which remains under its own vendor license.

# 

# \---

# 

# \## Download

# 

# AEGIS is available as a finished, self-contained build on the \[Releases page](../../releases) — no .NET SDK, no IDE, no installation required.

# 

# 1\. Download the latest release archive (\*\*v1.0.1\*\* or newer).

# 2\. Extract it to a removable drive. AEGIS must be run from a USB stick.

# 3\. Start `AEGIS.exe`.

# 

# The Suite Builder works immediately after the first start. There is no setup screen, no server configuration, and no token to enter.

# 

# > \*\*Note:\*\* Release \*\*v1.0.0\*\* was built before the repository migration and does not work. It is kept for history only — use v1.0.1 or newer.

# 

# \---

# 

# \## The Suite

# 

# | Stick | Name | Full Name | Status |

# |-------|------|-----------|--------|

# | 1 | \*\*DART\*\* | Diagnostic \& Analysis Response Tool | ✅ v3.2.0 |

# | 2 | \*\*AVAS\*\* | Advanced Versatile Admin Suite | ✅ v1.0.2 |

# | 3 | \*\*MABS\*\* | Multi Architecture Boot Suite | ✅ Complete |

# | 4 | \*\*AEGIS\*\* | All-in-one Environment for General Index \& Suite-management | ✅ v1.0.1 |

# | 5 | \*(unnamed)\* | Portable Ubuntu System | 📋 Planned |

# 

# \---

# 

# \## DART — Diagnostic \& Analysis Response Tool

# 

# A portable diagnostic launcher that runs directly from the USB stick, with its own amber/brown theme. Rewritten from HTA/VBScript to \*\*WPF / .NET 8\*\* in v3.0 — same tool set, native UI, splash screen retained.

# 

# Tools are organized into five categories: Hardware, Disk, Network, Processes, and Misc. The former \*Security\* category was removed entirely, as the NirSoft tools it contained are consistently flagged as trojans by antivirus software.

# 

# Five of seven tools are downloaded automatically from their official sources during the build: \*\*CPU-Z, WiFi Channel Monitor, System Informer\*\* (successor to the discontinued Process Hacker), \*\*Sysinternals Suite\*\*, and \*\*TreeSize Free\*\*. \*\*HWiNFO\*\* and \*\*CrystalDiskInfo\*\* remain manual links — their only official sources block automated .NET downloads via Cloudflare bot protection.

# 

# No installation required. Everything runs relative to the launcher file.

# 

# \---

# 

# \## AVAS — Advanced Versatile Admin Suite

# 

# A portable Windows admin and deployment tool in its own lime-green theme. Features dynamic package management controlled via JSON lists over an admin section. AVAS actively monitors the age of installed software packages and the driver database, and raises reminders when updates are due. All inside one EXE for ease of use.

# 

# \*\*Driver handling\*\* is based on \*\*SDIO\*\* (Snappy Driver Installer Origin — the original SDI project has been dead since 2017). Two database options are offered during the build:

# 

# \- \*\*Full offline database\*\* — the complete driver set, supplied by the user. It is only distributed as a \~50 GB torrent and is therefore deliberately not downloaded by the build process.

# \- \*\*Lite\*\* — automatically downloads the official Intel Ethernet driver and the SDIO scan tool; Realtek and Intel WLAN drivers are offered as clickable links.

# 

# \*\*Seven everyday applications\*\* are downloaded automatically from their official vendor pages: Firefox, Chrome (as MSI), Brave, 7-Zip, VLC, Notepad++, and VS Code. Note that Brave ships only as an online stub installer (\~1.3 MB) and requires an internet connection on the target machine to finish installing — this is flagged both during the build and inside AVAS itself.

# 

# The installer runner supports both EXE and MSI packages (MSI via `msiexec.exe`, as MSI does not support the `runas` verb). A dedicated \*\*"Check drivers only"\*\* action is available independently of the package selection.

# 

# \---

# 

# \## MABS — Multi Architecture Boot Suite

# 

# A multiboot USB stick using a fully custom Ventoy theme. The OS selection includes Windows 11, Ubuntu, and Kali Linux — this list may be expanded in the future if there is demand for additional systems. Common Secure Boot issues are handled via MOK certificate enrollment, and the boot menu is configured through `ventoy.json`.

# 

# MABS is set up entirely by the AEGIS Suite Builder: Ventoy is fetched live from its official GitHub release page and installed elevated via `Ventoy2Disk.exe VTOYCLI`, after which the KPMN theme is applied automatically from the MABS repository.

# 

# \*\*Ubuntu and Kali ISOs are downloaded automatically\*\* from the official download pages, with both sources parsed at runtime so no versions are hardcoded. \*\*Windows 11 remains manual\*\* — Microsoft's download is session-gated and its links expire after 24 hours, which cannot be automated. The build dialog links to the official Microsoft page instead.

# 

# \---

# 

# \## AEGIS — All-in-one Environment for General Index \& Suite-management

# 

# The central hub of the KPMN Suite. AEGIS builds and updates the other USB sticks, manages their contents, and serves as a unified control center — all without needing to launch the individual tools themselves. It must be run from a removable drive and detects which other KPMN sticks are currently plugged in, displaying their status live in the topbar.

# 

# Fully \*\*bilingual (DE / EN)\*\* via a central `Loc` dictionary system with a topbar switch, with its own themed dialogs throughout instead of native message boxes, a multi-resolution application icon, and automatic code signing at build time.

# 

# \### Suite Builder

# 

# The core feature: builds and updates AVAS, DART, and MABS directly from their GitHub releases. It downloads finished, self-contained binaries — \*\*nothing is compiled on the target machine\*\*.

# 

# \- \*\*No setup required.\*\* AEGIS ships with read-only access to the three repositories built in, so the Suite Builder is usable straight after the first start — no server address, organization, or token to configure.

# \- \*\*Two modes per stick\*\*, detected automatically from the current drive label:

# &#x20; - \*New stick\* — the drive is wiped completely, with an explicit warning beforehand.

# &#x20; - \*Update\* — only program files are overwritten; all other content is left untouched.

# \- \*\*MABS is treated more strictly\*\* than AVAS and DART, since installing Ventoy is a full repartitioning of the disk rather than a file deletion. The warning reflects that.

# \- \*\*Safety filter\*\*: the drive AEGIS itself is currently running from never appears in any of the three drive dropdowns.

# 

# \### Topbar

# 

# \- Live detection of connected KPMN sticks (DART / AVAS / MABS / Portable System)

# \- Color-coded status pills — online/offline visually distinguished

# \- Language switch (DE / EN)

# 

# \### Documents Tab

# 

# \- Organized folder structure for all KPMN sticks and personal documents

# \- 3-column explorer (Folders | Files | Metadata Preview — in-app file preview planned for a future version)

# \- Real subfolder navigation with double-click and back navigation

# \- Per-folder live stick detection: if the physical KPMN stick for that folder is plugged in, its files are shown live

# \- \*\*AVAS reminder passthrough\*\*: warns directly in AEGIS when AVAS' package or driver database is out of date (>150 / >180 days), without launching AVAS

# 

# \### Vault Tab

# 

# \- AES-256-GCM encryption with PBKDF2 key derivation

# \- Both filenames and contents are encrypted

# \- Master password with no recovery option (zero-knowledge by design)

# \- Add / remove / open files — note: opening currently relies on temporary decryption and the system's default app, which is an interim solution; the goal is to handle all file viewing fully encrypted within AEGIS via a planned built-in document previewer

# \- Auto-lock after 5 minutes of inactivity, on tab switch, or on app close

# \- Deliberately \*\*not\*\* a password manager — that belongs to Stick 5

# 

# \### Templates Tab

# 

# \- Document templates: add, create new file from template (Save As → opens with default app), remove

# \- Text snippets: RTF editor with Bold / Italic / Underline / List, save/load, copy to clipboard with formatting

# 

# \### Device Backups Tab

# 

# \- \*\*Driver backup\*\* via DISM

# \- \*\*User data backup\*\*

# \- \*\*Wi-Fi credential backup\*\* — stored encrypted in the Vault

# \- Overview of existing backups with deletion confirmation

# 

# \---

# 

# \## Infrastructure

# 

# AVAS, DART, and MABS each live in their own GitHub repository and are distributed as GitHub releases, which the Suite Builder downloads at runtime. Only fully tested, watertight builds are released there — a stricter bar than for AEGIS' own source code, since the Suite Builder deploys these binaries automatically onto other people's machines.

# 

# This replaces an earlier self-hosted Gitea server. That server ran on a private LAN address and was never reachable from outside the home network; exposing it properly would have meant permanent port forwarding and taking on uptime responsibility for a Raspberry Pi. GitHub handles that instead. Nothing changes for the user — the Suite Builder needed no configuration before and needs none now.

# 

# \*\*Development workflow:\*\* AEGIS itself is developed on the `dev` branch of this repository. `main` belongs exclusively to the project owner and is reached through pull requests only, never through a direct push.

# 

# \*\*No hosting of third-party software\*\* — a project-wide policy. Every download (drivers, tools, installers, ISOs, Ventoy itself) is fetched live from the respective official vendor. Nothing is hosted or redistributed by KPMN Development.

# 

# \---

# 

# \## Documentation

# 

# A bilingual (DE / EN) technical handbook lives at `docs/index.html` and is published via GitHub Pages at \*\*\[felixkpm.github.io/kpmn-dev-usb-stick-suite](https://felixkpm.github.io/kpmn-dev-usb-stick-suite/)\*\*. It covers the overall use case, the release infrastructure, and the Suite Builder step by step.

# 

# \---

# 

# \## Stick 5 — Portable Ubuntu System

# 

# \*\*Status:\*\* Planned

# 

# A fully portable Linux environment — browser, password manager, and editor — independent of the host system. Useful when working on unfamiliar or untrusted machines. Currently a concept only; nothing has been built yet.

# 

# \---

# 

# \## Roadmap

# 

# \### ✅ v1.0 — Released

# 

# \- \[x] Suite Builder — automatic setup and update of AVAS, DART, and MABS from GitHub releases, with no user configuration

# \- \[x] Device Backups Tab (drivers, user data, Wi-Fi credentials)

# \- \[x] Multilanguage support (DE / EN)

# \- \[x] AVAS reminder passthrough — package and driver database age warnings shown directly in AEGIS

# \- \[x] DART rewritten from HTA/VBScript to WPF / .NET 8

# \- \[x] Automatic ISO provisioning for MABS (Ubuntu, Kali)

# \- \[x] Code signing and a dedicated application icon for AEGIS

# \- \[x] Bilingual technical handbook, live on GitHub Pages

# \- \[x] AEGIS published as a ready-to-run release download

# 

# \### 📋 v1.1 — Planned

# 

# \- \[ ] Real file preview in the Documents Tab — built-in document previewer (also planned to replace temporary decryption in the Vault)

# \- \[ ] Stick 5 (Portable Ubuntu System) — build and integrate

# 

# \### 🚫 Not planned (by design or externally blocked)

# 

# \- \*\*Automatic download of HWiNFO and CrystalDiskInfo\*\* — their only official sources block automated .NET downloads via Cloudflare bot protection. Manual links remain.

# \- \*\*Automatic download of the Windows 11 ISO\*\* — Microsoft's download is session-gated with links expiring after 24 hours.

# \- \*\*Bundling the full offline driver database\*\* — only distributed as a \~50 GB torrent; users supply it themselves.

# \- \*\*Password manager inside the AEGIS Vault\*\* — intentionally scoped to Stick 5.

# \- \*\*Vault password recovery\*\* — omitted deliberately; zero-knowledge by design.

# 

# \---

# 

# \## Built With

# 

# \- WPF / C# / .NET 8.0 (AEGIS, AVAS, DART)

# \- Ventoy + GRUB2 (MABS)

# \- GitHub Releases (distribution infrastructure)

# \- Claude (Anthropic) — AI-assisted development

# 

# \---

# 

# \*© 2026 Felix Koopmann — KPMN Development with help of claude.ai (Anthropic). A Vibecoding Project.\*

# \*Licensed under \[CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/). Not for commercial use.\*

