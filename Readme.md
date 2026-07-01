# KPMN Development USB-Stick Suite
 
> A collection of purpose-built USB stick tools, each with its own distinct purpose, united under a single brand identity.
 
**The KPMN Development brand** is a hobby project by Felix Koopmann — Built with the help of Claude (Anthropic) as a Vibecoding project.
 
---
 
## ⚠️ Disclaimer
 
This is a **hobby project**. No maintenance, support, or continued development is guaranteed. The software is provided "as-is," without any warranty of functionality, merchantability, or fitness for a particular purpose.
 
Where applicable, this suite downloads software packages and installers directly from official vendor sources using tools like `curl` or `wget`. No third-party software is hosted or redistributed by KPMN Development. All third-party software remains subject to the respective licenses of each vendor.
 
Please be aware that certain features — such as the Data Vault — carry a risk of data loss due to unexpected program behavior. This software has not been extensively tested. Use it thoughtfully and keep backups of anything important.
 
**KPMN Development assumes no liability for any data loss, file corruption, or damage to your system resulting from the use of this software, except in cases of intent or gross negligence. Use at your own risk.**
 
This disclaimer and the use of this software are governed by the laws of the Federal Republic of Germany.
 
This project is licensed under **CC BY-NC-SA 4.0**. It may not be used for commercial purposes. See the LICENSE file for details.
 
---
 
## The Suite
 
| Stick | Name | Full Name | Status |
|-------|------|-----------|--------|
| 1 | **DART** | Diagnostic & Analysis Response Tool | ✅ Complete |
| 2 | **AVAS** | Advanced Versatile Admin Suite | ✅ Complete |
| 3 | **MABS** | Multi Architecture Boot Suite | ✅ Complete |
| 4 | **AEGIS** | All-in-one Environment for General Index & Suite-management | 🔧 In Development |
| 5 | *(unnamed)* | Portable Ubuntu System | 📋 Planned |
 
---
 
## DART — Diagnostic & Analysis Response Tool
 
A portable diagnostic launcher that runs directly from the USB stick. Opens tools like CPU-Z, HWiNFO, CrystalDiskInfo, Process Hacker, and more — organized into six categories: Hardware, Disk, Network, Security, Processes, and Misc.
 
No installation required. Everything runs relative to the launcher file.
 
---
 
## AVAS — Advanced Versatile Admin Suite
 
A portable Windows admin and deployment tool. Features dynamic package management controlled via JSON lists over an admin section, and driver integration via SDI. AVAS actively monitors the age of installed software packages and the SDI driver database, and raises reminders when updates are due. All inside one EXE for ease of use.
 
---
 
## MABS — Multi Architecture Boot Suite
 
A multiboot USB stick using a fully custom Ventoy theme. The current OS selection includes Windows 11, Ubuntu, and Kali Linux — this list may be expanded in the future if there is demand for additional systems. Common Secure Boot issues are handled via MOK certificate enrollment.
 
---
 
## AEGIS — All-in-one Environment for General Index & Suite-management
 
The central hub of the KPMN Suite. AEGIS is designed to build and update the other USB sticks in the suite, manage their contents, and serve as a unified control center — all without needing to launch the individual tools themselves. It must be run from a removable drive and detects which other KPMN sticks are currently plugged in, displaying their status live in the topbar.
 
### Current State
 
**Topbar**
- Live detection of connected KPMN sticks (DART / AVAS / MABS / Portable System)
- Color-coded status pills — online/offline visually distinguished
**Dokumente Tab**
- Organized folder structure for all KPMN sticks and personal documents
- 3-column explorer (Folders | Files | Metadata Preview — in-app file preview planned for a future version)
- Real subfolder navigation with double-click and back button
- Per-folder live stick detection: if the physical KPMN stick for that folder is plugged in, its files are shown live
**Vault Tab**
- AES-256-GCM encryption with PBKDF2 key derivation
- Both filenames and contents are encrypted
- Master password with no recovery option (zero-knowledge by design)
- Add / remove / open files — note: opening currently relies on temporary decryption and the system's default app, which is an interim solution; the goal is to handle all file viewing fully encrypted within AEGIS via a planned built-in document previewer
- Auto-lock after 5 minutes of inactivity, on tab switch, or on app close
**Templates Tab**
- Document templates: add, create new file from template (Save As → opens with default app), remove
- Text snippets: RTF editor with Bold / Italic / Underline / List, save/load, copy to clipboard with formatting
### Roadmap
 
- [ ] Device Backups Tab
- [ ] Multilanguage support (DE / EN — more languages may be available in the future)
- [ ] Real file preview in Dokumente Tab — built-in document previewer (also planned to replace temporary decryption in the Vault)
- [ ] AVAS reminder passthrough — display package and SDI database age warnings directly in AEGIS, without needing to launch AVAS
- [ ] Suite Builder — automatic setup and update of other KPMN sticks, software downloaded directly from official vendor sources via `curl`/`wget`, own files served from a self-hosted server
- [ ] Stick 5 integration (Portable Ubuntu System)
---
 
## Stick 5 — Portable Ubuntu System
**Status:** Planned
 
A fully portable Linux environment — browser, password manager, and editor — independent of the host system. Useful when working on unfamiliar or untrusted machines.
 
---
 
## Built With
 
- WPF / C# / .NET 8.0 (AVAS, AEGIS)
- HTA / VBScript (DART)
- Ventoy + GRUB2 (MABS)
- Claude (Anthropic) — AI-assisted development
---
 
*© 2026 Felix Koopmann — KPMN Development. A Vibecoding Project.*
*Licensed under [CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/). Not for commercial use.*
