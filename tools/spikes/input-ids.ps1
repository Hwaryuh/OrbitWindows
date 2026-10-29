# Input method ids shared by the spike scripts. Dot-source it: . "$PSScriptRoot\input-ids.ps1"
# Araso's from windows/tip (CLSID and its Korean profile); the Microsoft Korean IME's from Windows.
$InputIds = @{
    araso = @{ clsid = "6D6C7D5F-3B7F-4F0E-9A2C-417261736F01"; profile = "6D6C7D5F-3B7F-4F0E-9A2C-417261736F02" }
    microsoft = @{ clsid = "A028AE76-01B1-46C2-99C4-ACD9858AE02F"; profile = "B5FE1F02-D5F2-4445-9C03-C568F23C99A1" }
}
# The form language lists and InstallLayoutOrTip use: "<langid>:{clsid}{profile}".
function Get-InputTip([string]$name) { "0412:{$($InputIds[$name].clsid)}{$($InputIds[$name].profile)}" }
