# A window with one text box for tools/spikes/araso-coexist.ps1, writing the box's text to a file
# whenever it changes (after Araso's windows/scripts/field.ps1, MIT). `wpf`: a WPF TextBox, a TSF
# text store of its own. `winforms`: the classic Edit control, which reaches text services through
# IMM32's compatibility layer (CUAS).
#   powershell -STA -File tools/spikes/field.ps1 wpf|winforms <file>
param([string]$Kind, [string]$Out)
if ($Kind -eq "winforms") {
    Add-Type -AssemblyName System.Windows.Forms
    $form = New-Object System.Windows.Forms.Form
    $form.Text = "Orbit spike field"
    $form.Width = 500
    $form.Height = 120
    $box = New-Object System.Windows.Forms.TextBox
    $box.Dock = "Fill"
    $box.Font = New-Object System.Drawing.Font("Segoe UI", 14)
    $form.Controls.Add($box)
    $box.Add_TextChanged({ [IO.File]::WriteAllText($Out, $box.Text, [Text.Encoding]::UTF8) })
    $form.Add_Shown({ $form.TopMost = $true; $form.Activate(); $box.Focus() | Out-Null; $form.TopMost = $false })
    [System.Windows.Forms.Application]::Run($form)
    return
}
Add-Type -AssemblyName PresentationFramework
$window = New-Object System.Windows.Window
$window.Title = "Orbit spike field"
$window.Width = 500
$window.Height = 120
$box = New-Object System.Windows.Controls.TextBox
$box.FontSize = 20
$window.Content = $box
$box.Add_TextChanged({ [IO.File]::WriteAllText($Out, $box.Text, [Text.Encoding]::UTF8) })
$window.Add_ContentRendered({ $window.Topmost = $true; $window.Activate() | Out-Null; $box.Focus() | Out-Null; $window.Topmost = $false })
$window.ShowDialog() | Out-Null
