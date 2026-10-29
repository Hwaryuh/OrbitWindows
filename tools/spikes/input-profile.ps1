# Switches the session's input method between Araso and the Microsoft Korean IME, as the taskbar's
# input indicator does (ITfInputProcessorProfileMgr::ActivateProfile, for the session). Windows
# shares one input method across apps, so a default override alone doesn't change the one in use.
#   powershell -File tools/spikes/input-profile.ps1 araso|microsoft
param([ValidateSet("araso", "microsoft")][string]$To)
Add-Type @"
using System;
using System.Runtime.InteropServices;
[ComImport, Guid("71C6E74C-0F28-11D8-A82A-00065B84435C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ITfInputProcessorProfileMgr {
    [PreserveSig] int ActivateProfile(uint profileType, ushort langid, ref Guid clsid, ref Guid profile, IntPtr hkl, uint flags);
}
public static class Profiles {
    static readonly Guid Clsid = new Guid("33C53A50-F456-4884-B049-85FD643ECFED"); // CLSID_TF_InputProcessorProfiles
    public static int Activate(Guid tip, Guid profile) {
        var manager = (ITfInputProcessorProfileMgr)Activator.CreateInstance(Type.GetTypeFromCLSID(Clsid));
        // TF_PROFILETYPE_INPUTPROCESSOR, Korean, for the session and enabled if it wasn't.
        return manager.ActivateProfile(1, 0x0412, ref tip, ref profile, IntPtr.Zero, 0x20000000 | 0x1);
    }
}
"@
. "$PSScriptRoot\input-ids.ps1"
$hr = [Profiles]::Activate([Guid]$InputIds[$To].clsid, [Guid]$InputIds[$To].profile)
Write-Host ("ActivateProfile {0}: 0x{1:X8}" -f $To, $hr)
if ($hr -ne 0) { exit 1 }
