# Can a text service be registered without administrator rights? Calls what ArasoTip's
# DllRegisterServer calls, with the COM class under HKCU, and prints each result. It never activates
# the input method, doesn't enable it by default, and takes everything back at the end.
#   powershell -File tools/spikes/per-user-register.ps1 -Dll artifacts\araso-stage\ArasoTip.dll
param([Parameter(Mandatory)][string]$Dll)
$Dll = (Resolve-Path $Dll).Path
Add-Type @"
using System;
using System.Runtime.InteropServices;
[ComImport, Guid("71C6E74C-0F28-11D8-A82A-00065B84435C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ITfInputProcessorProfileMgr {
    [PreserveSig] int ActivateProfile(uint type, ushort langid, ref Guid clsid, ref Guid profile, IntPtr hkl, uint flags);
    [PreserveSig] int DeactivateProfile(uint type, ushort langid, ref Guid clsid, ref Guid profile, IntPtr hkl, uint flags);
    [PreserveSig] int GetProfile(uint type, ushort langid, ref Guid clsid, ref Guid profile, IntPtr hkl, IntPtr found);
    [PreserveSig] int EnumProfiles(ushort langid, out IntPtr list);
    [PreserveSig] int ReleaseInputProcessor(ref Guid clsid, uint flags);
    [PreserveSig] int RegisterProfile(ref Guid clsid, ushort langid, ref Guid profile,
        [MarshalAs(UnmanagedType.LPWStr)] string description, uint descriptionLength,
        [MarshalAs(UnmanagedType.LPWStr)] string icon, uint iconLength, uint iconIndex,
        IntPtr hkl, uint preferredLayout, int enabledByDefault, uint flags);
    [PreserveSig] int UnregisterProfile(ref Guid clsid, ushort langid, ref Guid profile, uint flags);
}
[ComImport, Guid("C3ACEFB5-F69D-4905-938F-FCADCF4BE830"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ITfCategoryMgr {
    [PreserveSig] int RegisterCategory(ref Guid clsid, ref Guid category, ref Guid item);
    [PreserveSig] int UnregisterCategory(ref Guid clsid, ref Guid category, ref Guid item);
}
public static class Tsf {
    public static readonly Guid Profiles = new Guid("33C53A50-F456-4884-B049-85FD643ECFED");
    public static readonly Guid Categories = new Guid("A4B544A1-438D-4B41-9325-869523E2D6C7");
    public static Guid Tip, Profile;
    static readonly Guid Keyboard = new Guid("34745C63-B2F0-4784-8B67-5E12C8701A31"); // GUID_TFCAT_TIP_KEYBOARD
    const ushort Korean = 0x0412;
    static ITfInputProcessorProfileMgr Manager() { return (ITfInputProcessorProfileMgr)Activator.CreateInstance(Type.GetTypeFromCLSID(Profiles)); }
    static ITfCategoryMgr CategoryManager() { return (ITfCategoryMgr)Activator.CreateInstance(Type.GetTypeFromCLSID(Categories)); }
    public static int RegisterProfile(string dll) {
        Guid tip = Tip, profile = Profile;
        return Manager().RegisterProfile(ref tip, Korean, ref profile, "Araso", 5, dll, (uint)dll.Length, 0, IntPtr.Zero, 0, 0, 0);
    }
    public static int RegisterCategory() {
        Guid tip = Tip, category = Keyboard, item = Tip;
        return CategoryManager().RegisterCategory(ref tip, ref category, ref item);
    }
    public static int GetProfile() {
        Guid tip = Tip, profile = Profile;
        IntPtr found = Marshal.AllocHGlobal(256);
        try { return Manager().GetProfile(1, Korean, ref tip, ref profile, IntPtr.Zero, found); }
        finally { Marshal.FreeHGlobal(found); }
    }
    public static int UnregisterCategory() {
        Guid tip = Tip, category = Keyboard, item = Tip;
        return CategoryManager().UnregisterCategory(ref tip, ref category, ref item);
    }
    public static int UnregisterProfile() {
        Guid tip = Tip, profile = Profile;
        return Manager().UnregisterProfile(ref tip, Korean, ref profile, 0);
    }
}
"@
. "$PSScriptRoot\input-ids.ps1"
$tip = [Guid]$InputIds.araso.clsid
[Tsf]::Tip = $tip
[Tsf]::Profile = [Guid]$InputIds.araso.profile
$class = "HKCU:\Software\Classes\CLSID\{$tip}"
$tipKeys = "HKLM:\SOFTWARE\Microsoft\CTF\TIP\{$tip}", "HKCU:\SOFTWARE\Microsoft\CTF\TIP\{$tip}"

function Show([string]$What, [int]$Result) { Write-Host ("{0}: 0x{1:X8}" -f $What, $Result) }

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
Write-Host "Elevated: $($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))"
foreach ($key in $tipKeys + $class) { Write-Host "Before, $key exists: $(Test-Path $key)" }

try {
    New-Item -Force "$class\InprocServer32" | Out-Null
    Set-Item $class -Value 'Araso'
    Set-Item "$class\InprocServer32" -Value $Dll
    Set-ItemProperty "$class\InprocServer32" -Name ThreadingModel -Value Apartment
    Write-Host "COM class under HKCU: written"

    Show 'RegisterProfile' ([Tsf]::RegisterProfile($Dll))
    Show 'RegisterCategory' ([Tsf]::RegisterCategory())
    foreach ($key in $tipKeys) { Write-Host "During, $key exists: $(Test-Path $key)" }
    Show 'GetProfile' ([Tsf]::GetProfile())
} finally {
    Show 'UnregisterCategory' ([Tsf]::UnregisterCategory())
    Show 'UnregisterProfile' ([Tsf]::UnregisterProfile())
    if (Test-Path $class) { Remove-Item -Recurse -Force $class }
    foreach ($key in $tipKeys + $class) { Write-Host "After, $key exists: $(Test-Path $key)" }
}
