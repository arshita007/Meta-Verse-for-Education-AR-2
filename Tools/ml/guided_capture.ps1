# Spoken, paced photo session: the PC announces each pose, counts down, then grabs a few
# headset screenshots (left eye only) named after the pose.
param([string]$OutDir = 'D:\VIP-AR-Deps\ml\pen-images')

New-Item -ItemType Directory -Force $OutDir | Out-Null
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Speech
$voice = New-Object System.Speech.Synthesis.SpeechSynthesizer
$voice.Rate = -1
$voice.Volume = 100

$poses = @(
    @{ Name = '1_all_on_desk';   Say = 'Pose one. Lay all three pen parts apart on the desk, and look down at them.' },
    @{ Name = '2_refill';        Say = 'Pose two. Hold up the ink refill by itself.' },
    @{ Name = '3_tip_housing';   Say = 'Pose three. Hold up the tip housing by itself.' },
    @{ Name = '4_click_housing'; Say = 'Pose four. Hold up the click housing by itself.' },
    @{ Name = '5_half_inserted'; Say = 'Pose five. Push the refill half way into the tip housing, and hold it up.' },
    @{ Name = '6_assembled';     Say = 'Pose six. Put the whole pen together, and hold it up.' }
)

function Save-LeftEye([string]$path) {
    $raw = "$path.raw.png"
    adb shell screencap -p /sdcard/vipar_eval.png | Out-Null
    adb pull /sdcard/vipar_eval.png $raw | Out-Null
    $bitmap = [System.Drawing.Bitmap]::FromFile($raw)
    $half = [int]($bitmap.Width / 2)
    $crop = New-Object System.Drawing.Rectangle ([int]($half * 0.12)), ([int]($bitmap.Height * 0.12)), ([int]($half * 0.8)), ([int]($bitmap.Height * 0.76))
    $left = $bitmap.Clone($crop, $bitmap.PixelFormat)
    $left.Save($path, [System.Drawing.Imaging.ImageFormat]::Jpeg)
    $left.Dispose()
    $bitmap.Dispose()
    Remove-Item $raw
}

$voice.Speak('Starting the photo session. Six poses. I will count down before each photo.')
foreach ($pose in $poses) {
    $voice.Speak($pose.Say)
    Start-Sleep -Seconds 4
    $voice.Speak('Three. Two. One. Hold still.')
    for ($shot = 1; $shot -le 3; $shot++) {
        Save-LeftEye (Join-Path $OutDir ("{0}_{1}.jpg" -f $pose.Name, $shot))
        Start-Sleep -Milliseconds 700
    }
    "captured $($pose.Name)"
    $voice.Speak('Got it.')
}
adb shell rm -f /sdcard/vipar_eval.png
$voice.Speak('All done. Thank you! You can take the headset off.')
