# Start WallpaperTurbo.UI
$process = Start-Process "src\WallpaperTurbo.UI\bin\Debug\net8.0-windows\WallpaperTurbo.UI.exe" -PassThru
Write-Host "Launched process with PID: $($process.Id)"
Start-Sleep -Seconds 6

# Focus the window
$wshell = New-Object -ComObject WScript.Shell
$success = $wshell.AppActivate($process.Id)
Write-Host "AppActivate result: $success"

# Function to send a key
function Send-Key ($key) {
    $wshell.SendKeys($key)
    Start-Sleep -Seconds 2
}

Write-Host "Sending F5 to run 50 wallpapers test..."
Send-Key "{F5}"

Write-Host "Sending F6 to run 200 wallpapers test..."
Send-Key "{F6}"

# Kill the process
Write-Host "Terminating process..."
Stop-Process -Id $process.Id -Force
Write-Host "Done!"
