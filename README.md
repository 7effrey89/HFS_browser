# HFS+ USB Browser

A Windows C# console application focused on browsing and copying files on Apple **HFS+ / HFSX** USB drives.

## Scope

This solution mainly supports:

- Browsing the root of removable HFS+ / HFSX USB drives
- Copying root-level files from HFS+ to `C:\Temp`
- Copying compatible files from `C:\Temp` back to the HFS+ root

APFS formatting support has been removed. This project is now HFS+-only.

## Features

- Detects removable USB drives through WMI and the Windows storage provider
- Attempts normal Windows filesystem browsing first when the drive is mounted
- Falls back to raw HFS+ / HFSX parsing when Windows assigns a drive letter but does not mount the filesystem normally
- Lists root-level folders and files from HFS+ volumes
- Copies supported root-level files from HFS+ to `C:\Temp`
- Copies compatible files from `C:\Temp` back to the HFS+ root
- Uses a simple numbered console UI

## Requirements

- Windows 10 or later
- .NET 8 SDK or runtime
- Administrator privileges may be required for raw HFS+ browsing and copy operations when Windows does not mount the volume normally

## Usage

1. Connect your HFS+ or HFSX USB drive.
2. Run the application.
3. Select the removable drive to browse.
4. Review the root folders and files.
5. Optionally:
   - Copy a listed HFS+ root file to `C:\Temp`
   - Copy a compatible file from `C:\Temp` back to the HFS+ root

## Running

From the repository root:

```powershell
dotnet run --project .\HFSPlusBrowser\HFSPlusBrowser.csproj
```

If you want to run the built executable directly:

```powershell
Start-Process .\HFSPlusBrowser\bin\Debug\net8.0-windows\HFSPlusBrowser.exe
```

If raw HFS+ access fails because of permissions, start an elevated PowerShell window:

```powershell
Start-Process pwsh -Verb RunAs
```

Then rerun the project from that elevated shell.

## Build

```powershell
dotnet build .\HFSPlusBrowser.sln
```

## HFS+ Limitations

- The raw HFS+ browser is focused on the root directory only.
- Raw HFS+ extraction supports files whose data fork is fully described by inline catalog extents.
- Files that require extent-overflow records are shown as copy unavailable.
- Raw HFS+ import does not create brand new files or allocate new extents.
- Raw HFS+ import only overwrites an existing same-named root-level file when that file already has enough allocated inline extent space.
- In practice, you cannot pick just any file from `C:\Temp` and copy it to a raw HFS+ drive. The file must already exist at the HFS+ root with the exact same name, and the replacement content must fit inside that existing file's allocated inline extent space.

## Notes

- This project mainly supports browsing and copying from HFS+.
- Windows may still browse some drives directly if a compatible filesystem driver is available.
- When Windows cannot mount the drive, the application uses raw HFS+ parsing instead.

## License

MIT
