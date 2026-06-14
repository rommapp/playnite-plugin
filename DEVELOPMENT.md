# Development

The RomM plugin is a [Playnite](https://playnite.link) library extension. Playnite
is a Windows application, and the project targets **.NET Framework 4.6.2 (WPF)**, so
it can only be built and run on **Windows**. On macOS you do everything inside a
Windows VM (see below); the plugin cannot run natively on macOS or Linux.

The CI build (`.github/workflows/build.yml` / `pr-build.yml`) is the source of truth
for how the extension is compiled and packaged.

## Windows

> _TODO: native Windows setup instructions._

## macOS

On macOS you build and run inside a Windows VM. These steps use [UTM](https://mac.getutm.app);
Parallels or VMware work too.

First check your chip: **Apple menu → About This Mac**. Most MacBooks from 2020+ are
**Apple Silicon (M-series)** — that's the path below. Intel notes are at the end.

### 1. Install UTM

- Free: download from <https://mac.getutm.app> and drag it to Applications.
- Or the **Mac App Store** version (identical app, supports the developers).

### 2. Get the Windows 11 ARM ISO

On Apple Silicon you run **Windows 11 on ARM**; Playnite, Visual Studio, and the x86/x64
.NET Framework build all run via Windows' built-in x64 emulation.

Easiest route is **CrystalFetch** (free, open source, on the Mac App Store):

1. Open CrystalFetch → pick **Windows 11**, **Arm64**, your language → **Download**
   (it builds an official Microsoft ISO, ~5 GB).

(Microsoft also offers Arm64 ISOs directly at
<https://www.microsoft.com/software-download/windows11arm64> — either source works.)

### 3. Create the VM

1. UTM → **+ → Virtualize** (not Emulate) → **Windows**.
2. Check **"Install Windows 10 or higher"** and leave **"Install drivers and SPICE tools"** checked.
3. **Browse** → select the ISO from step 2.
4. Resources (enough to build comfortably):
   - **RAM:** 8 GB (4 GB minimum)
   - **CPU cores:** 4
   - **Storage:** 64 GB (Windows + Visual Studio is ~40 GB)
5. **Create** → **Save**.

### 4. Install Windows

1. Select the VM → **▶︎ Play**, press a key to boot the installer.
2. Two things to know during setup:
   - **No product key needed** for dev — choose "I don't have a product key";
     unactivated Windows runs fine for building/testing.
   - If OOBE forces a Microsoft account / network, press **Shift+F10**, run
     `start ms-cxh:localonly`, and you'll get a local-account option.
3. After it boots to the desktop, open the auto-mounted **UTM/SPICE guest tools** ISO and
   run the installer → reboot. This enables proper resolution, clipboard sharing, and drivers.

### 5. Build, deploy & debug

1. Install **Visual Studio 2022 Community** (free) with the **.NET desktop development**
   workload (provides MSBuild and the .NET Framework 4.6.2 targeting pack).
2. Clone and build the way CI does:

   ```powershell
   git clone https://github.com/rommapp/playnite-plugin
   cd playnite-plugin
   dotnet restore
   msbuild RomM.csproj /p:Configuration=Release /p:Platform=AnyCPU /p:RestorePackages=false
   ```

3. Install **Playnite** (<https://playnite.link>), then copy the build output
   (`bin\Release\net462\` — `RomM.dll`, `extension.yaml`, and the dependency DLLs) into
   `%AppData%\Playnite\Extensions\<extension-id>\` and restart Playnite. (You don't need to
   pack a `.pext` for local iteration.)
4. **Debug:** in Visual Studio, **Debug → Attach to Process → `Playnite.DesktopApp.exe`**,
   then set breakpoints. XAML/binding issues surface in the logs rather than the debugger.
5. **Logs:** watch `%AppData%\Playnite\playnite.log` and `extensions.log`. The plugin logs
   heavily (`[Importer]`, `[Import Controller]`, …) via `LogManager.GetLogger()`.

### VM tips

- Enable a **shared folder** (UTM VM settings → Sharing) or just `git clone` inside the VM.
- Take a **snapshot** once Windows + Visual Studio are installed so you can roll back.

### Intel Macs

Same UTM steps, but use a **Windows 11 x64** ISO
(<https://www.microsoft.com/software-download/windows11>) instead of the Arm64 one — it
runs x64 natively, with no emulation layer.

### Troubleshooting

**Boots to a "UEFI Interactive Shell v2.2" prompt instead of the installer.** The
**"Press any key to boot from CD or DVD…"** prompt only shows for a couple of seconds; if
it times out the VM drops to the UEFI shell. To recover:

1. Type `exit` and press **Enter** to reach the UEFI firmware menu (TianoCore).
2. Choose **Boot Manager**.
3. Select the **DVD/CD** entry (your Windows ISO, e.g. "UEFI … DVD/CDROM").
4. When **"Press any key to boot from CD or DVD…"** appears, immediately spam **Spacebar/Enter**.
   If you miss it, it returns to the shell — repeat from step 1.

Tip: right after you hit **▶︎ Play**, click into the VM window and tap Spacebar repeatedly to
catch the prompt. If `exit` shows no DVD/CD option, the ISO isn't attached — stop the VM and
confirm **Settings → Drives** has a CD/DVD drive pointing at the ISO.

Once Windows is installed and boots from the virtual disk, **eject the ISO** (Settings →
Drives → clear it) so it stops offering "press any key to boot from CD" on every restart.
