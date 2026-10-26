# Generates the narrator voice lines (WAV) from Assets/_Project/Resources/VO/narration.json
# using Windows speech synthesis. Run from the project root:  powershell -File Tools/generate_vo.ps1
Add-Type -AssemblyName System.Speech
$root = Split-Path -Parent $PSScriptRoot
$dir = Join-Path $root "Assets/_Project/Resources/VO"
$data = Get-Content (Join-Path $dir "narration.json") -Raw | ConvertFrom-Json
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$fmt = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(22050, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)
foreach ($l in $data.lines) {
    if ($l.speaker -like "System*") { $synth.SelectVoice("Microsoft David Desktop") } else { $synth.SelectVoice("Microsoft Zira Desktop") }
    $synth.Rate = 0
    $out = Join-Path $dir ($l.file + ".wav")
    $synth.SetOutputToWaveFile($out, $fmt)
    $pb = New-Object System.Speech.Synthesis.PromptBuilder
    $pb.StartStyle((New-Object System.Speech.Synthesis.PromptStyle([System.Speech.Synthesis.PromptRate]::Medium)))
    $pb.AppendText($l.spoken)
    $pb.EndStyle()
    $synth.Speak($pb)
}
$synth.SetOutputToNull()
Write-Output ("Generated " + $data.lines.Count + " voice lines")
