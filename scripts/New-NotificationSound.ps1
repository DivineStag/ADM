# Rebuild ADM's original two-note WAV from mathematical tones, without sampled audio.
$ErrorActionPreference = 'Stop'
$audioPath = Join-Path $PSScriptRoot '..\src\ADM.Desktop\Assets\incoming.wav'
$sampleRate = 44100
$sampleCount = [int]($sampleRate * 0.44)
$writer = [System.IO.BinaryWriter]::new([System.IO.File]::Create($audioPath))
try {
    $writer.Write([System.Text.Encoding]::ASCII.GetBytes('RIFF'))
    $writer.Write([int](36 + $sampleCount * 2))
    $writer.Write([System.Text.Encoding]::ASCII.GetBytes('WAVEfmt '))
    $writer.Write([int]16)
    $writer.Write([int16]1)
    $writer.Write([int16]1)
    $writer.Write([int]$sampleRate)
    $writer.Write([int]($sampleRate * 2))
    $writer.Write([int16]2)
    $writer.Write([int16]16)
    $writer.Write([System.Text.Encoding]::ASCII.GetBytes('data'))
    $writer.Write([int]($sampleCount * 2))
    for ($sampleIndex = 0; $sampleIndex -lt $sampleCount; $sampleIndex++) {
        $time = $sampleIndex / $sampleRate
        $value = 0.0
        foreach ($tone in @(@(0.0, 783.99), @(0.095, 1046.5))) {
            $elapsed = $time - $tone[0]
            if ($elapsed -ge 0) {
                $envelope = [Math]::Min(1.0, $elapsed / 0.004) * [Math]::Exp(-$elapsed * 16) * [Math]::Min(1.0, (0.44 - $time) / 0.02)
                $value += [Math]::Sin(2 * [Math]::PI * $tone[1] * $elapsed) * $envelope * 0.22
            }
        }
        $writer.Write([int16]($value * 32767))
    }
} finally { $writer.Dispose() }
