# APFS USB Formatter

A Windows C# console application that formats a USB drive with the **Apple APFS (Apple File System)** format, making it ready for use with macOS.

## Features

- **Detects all removable USB drives** connected to the system using WMI
- **Choose browse or format mode at startup** depending on whether you want to inspect or initialize a USB drive
- **Interactive drive selection** with a numbered menu
- **Customizable volume label** (default: `APFS`)
- **Safety confirmation** — requires typing `YES` before any data is erased
- **Full APFS preparation in two steps**:
  1. Cleans the drive and creates a GPT partition with the Apple APFS partition type GUID (`7C3457EF-0000-11AA-AA11-00306543ECAC`) using Windows `diskpart`
  2. Writes a minimal valid APFS container superblock (with Fletcher64 checksum) directly to the drive
- **Read-only browse check** — attempts to enumerate top-level folders and files when Windows exposes the formatted volume through an APFS driver
- **Read-only HFS+ browse fallback** — attempts raw root listing for HFS+/HFSX USB volumes when Windows assigns a drive letter but cannot mount the filesystem normally
- **Optional HFS+ root-file copy** — after a successful raw HFS+ root browse, you can choose a listed root-level file and copy it to `C:\Temp`
- **Colored console UI** with progress indicators

## Requirements

- Windows 10 or later (64-bit)
- **.NET 8.0 Runtime** — [Download here](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Administrator privileges** (required for raw disk access)

## Usage

1. Connect your USB drive.
2. Open a command prompt or PowerShell.
3. Run the application:
   ```
   APFSFormatter.exe
   ```
4. Follow the on-screen prompts:
  - Choose whether to browse a USB drive or format one as APFS
  - Select your USB drive by number
  - In browse mode, if the drive is parsed as raw HFS+, you can optionally select a copyable root-level file and export it to `C:\Temp`
  - If you choose format mode, run the app as Administrator, enter a volume label, and type `YES` to confirm formatting

### Running from Source

If you are running the project from the repository instead of a published executable:

```powershell
cd APFSFormatter
dotnet run
```

### Running from VS Code

Format mode requires Administrator privileges for raw disk access. If you see this message:

```text
✗ This application must be run as Administrator.
  Right-click the executable and choose 'Run as administrator'.
```

the already-running VS Code terminal cannot be elevated in place. Start a new elevated PowerShell window instead:

```powershell
Start-Process pwsh -Verb RunAs
```

Then, in that new Administrator PowerShell window, run the project from the repository root:

```powershell
cd c:\Git\APFS-USB-Formatter\APFS-USB-Formatter
dotnet run --project APFSFormatter\APFSFormatter.csproj
```

If you already built the project and want to run the executable directly:

```powershell
cd c:\Git\APFS-USB-Formatter\APFS-USB-Formatter
Start-Process .\APFSFormatter\bin\Debug\net8.0-windows\APFSFormatter.exe -Verb RunAs
```

If you prefer using the VS Code integrated terminal, close VS Code and reopen it with **Run as administrator**. Any terminal started inside VS Code will then inherit Administrator privileges.

### Troubleshooting

If formatting fails with a DiskPart message like:

```text
The disk you specified is not MBR formatted.
Please select an empty MBR disk to convert.
```

that means the USB drive was already using GPT metadata and an older build of this tool treated that as a hard failure. Rebuild or rerun with the latest version of the project so the GPT conversion step can continue correctly.

### Example Session

```
╔══════════════════════════════════════════════════════╗
║           APFS USB Formatter for Windows             ║
║     Format your USB drive with Apple APFS format     ║
╚══════════════════════════════════════════════════════╝

  [1/3] Choose an operation mode...

  Choose an action:
    [1] Browse a USB drive
    [2] Format a USB drive as APFS

  Enter your choice (1-2, or 0 to exit): 2

────────────────────────────────────────────────────────
  [2/3] Scanning for removable USB drives...

  Detected USB drives:
    [1] Disk 1: SanDisk Ultra (14.9 GB) [E:]

────────────────────────────────────────────────────────
  [3/3] Select the USB drive to format as APFS:

  Enter the number of the drive to format (or 0 to exit): 1
  Enter volume label (default: APFS):

⚠ WARNING: This will PERMANENTLY ERASE all data on the selected drive!
  Drive: Disk 1: SanDisk Ultra (14.9 GB) [E:]

  Type YES to confirm and proceed: YES

────────────────────────────────────────────────────────
  Formatting drive as APFS...

  → [1/2] Cleaning disk and creating GPT partition... Done.
  → [2/2] Writing APFS container superblock... Done.

────────────────────────────────────────────────────────
✓ USB drive successfully formatted as APFS!

  Summary:
    Drive : Disk 1: SanDisk Ultra (14.9 GB) [E:]
    Format: APFS (Apple File System)
    Layout: GPT with APFS container partition

  Notes:
    * The drive is now ready for use with macOS.
    * macOS will complete the APFS volume initialization
      the first time the drive is mounted.
    * Windows cannot natively read or write APFS drives.
      Use a third-party driver (e.g., Paragon APFS) for
      Windows access, or use the drive exclusively on Mac.

  Browse check:
⚠ Windows did not expose a readable drive letter for the formatted APFS volume.
    Stock Windows cannot browse APFS. A third-party APFS driver or macOS is required.
```

## Building from Source

Requires [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
cd APFSFormatter
dotnet build
```

To publish a self-contained executable:

```bash
dotnet publish -c Release -r win-x64 --self-contained true -o ./publish
```

## How It Works

### Step 1 — diskpart (Partition Setup)

The application uses Windows' built-in `diskpart` utility to:
1. **Clean** the selected disk (erases all existing partition data and MBR/GPT headers)
2. **Convert to GPT** — APFS requires a GUID Partition Table
3. **Create a primary partition** spanning the full disk
4. **Set the partition type GUID** to the APFS Container GUID:  
   `7C3457EF-0000-11AA-AA11-00306543ECAC`

### Step 2 — APFS Container Superblock

The application writes a minimal valid APFS container superblock (`nx_superblock_t`) to block 0 of the physical disk:
- Magic number: `NXSB` (`0x4253584E`)
- Block size: 4096 bytes
- Random container UUID
- Valid **Fletcher64 checksum** (APFS variant)
- Checkpoint descriptor and data area pointers

This project currently writes a minimal APFS container seed. It does not implement the full APFS metadata structures required for a fully mountable, browsable APFS volume on Windows.

## Notes

- **Data loss**: Formatting permanently erases all data. Make sure to back up important files first.
- **Windows APFS support**: Windows does not natively support APFS. To access an APFS drive on Windows, use a third-party driver such as [Paragon APFS for Windows](https://www.paragon-software.com/home/apfs-windows/).
- **Browsing on Windows**: The app can only list folders and files when Windows exposes the APFS volume through a compatible third-party driver. Without that, the browse check will report that the volume is not readable from Windows.
- **HFS+ file copy scope**: Raw HFS+ extraction currently supports root-level files whose data fork is fully described by inline catalog extents. Files that require extent-overflow records will be listed as copy unavailable.
- **Current APFS scope**: The formatter writes a minimal APFS container seed, not a complete APFS volume implementation with object maps, spaceman structures, volume superblocks, and file trees.
- **Administrator required for formatting**: Browse mode can run without elevation, but format mode requires elevated privileges for raw disk access.

## License

MIT
