# APFS USB Formatter

A Windows C# console application that formats a USB drive with the **Apple APFS (Apple File System)** format, making it ready for use with macOS.

## Features

- **Detects all removable USB drives** connected to the system using WMI
- **Interactive drive selection** with a numbered menu
- **Customizable volume label** (default: `APFS`)
- **Safety confirmation** — requires typing `YES` before any data is erased
- **Full APFS preparation in two steps**:
  1. Cleans the drive and creates a GPT partition with the Apple APFS partition type GUID (`7C3457EF-0000-11AA-AA11-00306543ECAC`) using Windows `diskpart`
  2. Writes a valid APFS container superblock (with Fletcher64 checksum) directly to the drive
- **Colored console UI** with progress indicators

## Requirements

- Windows 10 or later (64-bit)
- **.NET 8.0 Runtime** — [Download here](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Administrator privileges** (required for raw disk access)

## Usage

1. Connect your USB drive.
2. Open a command prompt **as Administrator**.
3. Run the application:
   ```
   APFSFormatter.exe
   ```
4. Follow the on-screen prompts:
   - Select your USB drive by number
   - Enter a volume label (or press Enter for the default `APFS`)
   - Type `YES` to confirm formatting (all existing data will be erased)

### Example Session

```
╔══════════════════════════════════════════════════════╗
║           APFS USB Formatter for Windows             ║
║     Format your USB drive with Apple APFS format     ║
╚══════════════════════════════════════════════════════╝

  [1/3] Scanning for removable USB drives...

  Detected USB drives:
    [1] Disk 1: SanDisk Ultra (14.9 GB) [E:]

────────────────────────────────────────────────────────
  [2/3] Select the USB drive to format as APFS:

  Enter the number of the drive to format (or 0 to exit): 1
  Enter volume label (default: APFS):

⚠ WARNING: This will PERMANENTLY ERASE all data on the selected drive!
  Drive: Disk 1: SanDisk Ultra (14.9 GB) [E:]

  Type YES to confirm and proceed: YES

────────────────────────────────────────────────────────
  [3/3] Formatting drive as APFS...

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

When the drive is plugged into a Mac, macOS will recognize the APFS container and complete the volume initialization.

## Notes

- **Data loss**: Formatting permanently erases all data. Make sure to back up important files first.
- **Windows APFS support**: Windows does not natively support APFS. To access an APFS drive on Windows, use a third-party driver such as [Paragon APFS for Windows](https://www.paragon-software.com/home/apfs-windows/).
- **Administrator required**: Raw disk access requires elevated privileges.

## License

MIT
