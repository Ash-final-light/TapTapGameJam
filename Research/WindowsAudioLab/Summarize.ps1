param([string]$LabRoot = 'D:\UnityProject\TapTap_GameJam\tmp\windows-audio-lab')
$ErrorActionPreference = 'Stop'
$audioSummaries = @()
foreach ($reportName in @('report.json', 'report-buffer-256.json')) {
    $audioReport = Get-Content -Raw -LiteralPath (Join-Path $LabRoot "results/$reportName") | ConvertFrom-Json
    foreach ($timing in $audioReport.timings) {
        $clockName = 'clock-' + $timing.name.Replace(' ', '-') + '-' + $timing.requestedFps + '-buffer-' + $audioReport.bufferFrames + '.csv'
        $clockRows = @(Import-Csv -LiteralPath (Join-Path $LabRoot "results/$clockName") | Where-Object valid -EQ '1')
        $dspValues = @($clockRows | ForEach-Object { [double]::Parse($_.dsp, [Globalization.CultureInfo]::InvariantCulture) })
        $zeroSteps = 0
        $maxDspStep = 0.0
        for ($clockIndex = 1; $clockIndex -lt $dspValues.Count; $clockIndex++) {
            $dspStep = $dspValues[$clockIndex] - $dspValues[$clockIndex - 1]
            if ([math]::Abs($dspStep) -lt 1e-9) { $zeroSteps++ }
            $maxDspStep = [math]::Max($maxDspStep, $dspStep * 1000)
        }
        $audioSummaries += [pscustomobject]@{
            Report=$reportName; Status=$audioReport.status; Checks=$audioReport.checks.Count; Failures=$audioReport.failures
            BufferFrames=$audioReport.bufferFrames; BufferCount=$audioReport.buffers; OutputRate=$audioReport.outputRate
            BlockMs=[math]::Round($audioReport.bufferFrames * 1000.0 / $audioReport.outputRate,3)
            Scenario=$timing.name; RequestedFps=$timing.requestedFps; ObservedFps=[math]::Round($timing.observedFps,2)
            Samples=$timing.observations; ClockOffsetRangeMs=[math]::Round($timing.realtimeDspOffsetRangeMs,3)
            AnchoredClockErrorP95Ms=[math]::Round($timing.realtimeDspElapsedP95Ms,3)
            DspCursorErrorMaxMs=[math]::Round($timing.maxAbsoluteMs,3)
            UnchangedDspFrames=$zeroSteps; MaxDspStepMs=[math]::Round($maxDspStep,3)
        }
    }
}
$audioSummaries | ConvertTo-Json -Depth 4
