[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InputVideo,
    [Parameter(Mandatory)][string]$OutputVideo
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$mobileInput = (Resolve-Path -LiteralPath $InputVideo).Path
$mobileOutput = [IO.Path]::GetFullPath($OutputVideo)
$mobileReport = $mobileOutput + '.media.json'
if ([IO.Path]::GetExtension($mobileOutput) -ine '.mp4') { throw 'Mobile gameplay output must be an MP4.' }
if ((Test-Path -LiteralPath $mobileOutput) -or (Test-Path -LiteralPath $mobileReport)) {
    throw 'Refusing to replace an existing video or media report.'
}
$mobileFfmpeg = (Get-Command ffmpeg -CommandType Application -ErrorAction Stop).Source
$mobileFfprobe = (Get-Command ffprobe -CommandType Application -ErrorAction Stop).Source

function Read-MobileMedia([string]$Path) {
    $probeText = & $mobileFfprobe -v error -show_streams -show_format -of json $Path
    if ($LASTEXITCODE -ne 0) { throw "Could not inspect video: $Path" }
    return (($probeText | Out-String) | ConvertFrom-Json)
}

function Read-MobileDuration($Media) {
    $value = [double]::Parse($Media.format.duration, [Globalization.CultureInfo]::InvariantCulture)
    if (-not [double]::IsFinite($value) -or $value -le 0) { throw 'Video duration is invalid.' }
    return $value
}

function Read-MobileMp4Layout([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $moov = $null
    $mdat = $null
    try {
        while ($stream.Position -lt $stream.Length) {
            $start = $stream.Position
            $header = [byte[]]::new(8)
            if ($stream.Read($header, 0, 8) -ne 8) { throw 'Truncated MP4 box header.' }
            $size = ([uint64]$header[0] -shl 24) + ([uint64]$header[1] -shl 16) +
                ([uint64]$header[2] -shl 8) + [uint64]$header[3]
            $type = [Text.Encoding]::ASCII.GetString($header, 4, 4)
            $headerSize = 8
            if ($size -eq 1) {
                $extended = [byte[]]::new(8)
                if ($stream.Read($extended, 0, 8) -ne 8) { throw 'Truncated extended MP4 box.' }
                $size = [uint64]0
                foreach ($part in $extended) { $size = ($size -shl 8) + [uint64]$part }
                $headerSize = 16
            } elseif ($size -eq 0) { $size = [uint64]($stream.Length - $start) }
            if ($size -lt $headerSize -or $size -gt [uint64]($stream.Length - $start)) {
                throw 'Invalid MP4 box extent.'
            }
            if ($type -eq 'moov' -and $null -eq $moov) { $moov = $start }
            if ($type -eq 'mdat' -and $null -eq $mdat) { $mdat = $start }
            $stream.Position = $start + [long]$size
        }
    } finally { $stream.Dispose() }
    if ($null -eq $moov -or $null -eq $mdat -or $moov -ge $mdat) {
        throw 'MP4 metadata must precede media data for immediate mobile playback.'
    }
    return [ordered]@{ fastStart = $true; moovOffset = $moov; mdatOffset = $mdat }
}

$mobileSourceMedia = Read-MobileMedia $mobileInput
$mobileSourceDuration = Read-MobileDuration $mobileSourceMedia
$mobileSourceVideo = @($mobileSourceMedia.streams | Where-Object codec_type -eq 'video')
$mobileSourceAudio = @($mobileSourceMedia.streams | Where-Object codec_type -eq 'audio')
if ($mobileSourceVideo.Count -ne 1 -or $mobileSourceVideo[0].width -le 0 -or $mobileSourceVideo[0].height -le 0) {
    throw 'Input must contain one actual video stream.'
}
$mobileSourceHash = (Get-FileHash -LiteralPath $mobileInput -Algorithm SHA256).Hash.ToLowerInvariant()
$mobileDirectory = [IO.Path]::GetDirectoryName($mobileOutput)
[IO.Directory]::CreateDirectory($mobileDirectory) | Out-Null
$mobilePending = Join-Path $mobileDirectory ([IO.Path]::GetFileNameWithoutExtension($mobileOutput) +
    '.' + [Guid]::NewGuid().ToString('N') + '.pending.mp4')
$mobilePendingReport = $mobilePending + '.media.json'
$mobilePublished = $false
try {
    $mobileArguments = @('-hide_banner', '-loglevel', 'error', '-n', '-i', $mobileInput,
        '-map', '0:v:0', '-map', '0:a:0?',
        '-vf', "scale=w='min(1280,iw)':h='min(720,ih)':force_original_aspect_ratio=decrease:force_divisible_by=2",
        '-r', '30', '-c:v', 'libx264', '-preset', 'medium', '-crf', '21', '-pix_fmt', 'yuv420p',
        '-profile:v', 'main', '-level:v', '3.1', '-c:a', 'aac', '-b:a', '128k',
        '-movflags', '+faststart', $mobilePending)
    & $mobileFfmpeg @mobileArguments
    if ($LASTEXITCODE -ne 0) { throw 'Mobile video encoding failed.' }
    $mobileEncoded = Read-MobileMedia $mobilePending
    $mobileDuration = Read-MobileDuration $mobileEncoded
    $mobileVideo = @($mobileEncoded.streams | Where-Object codec_type -eq 'video')
    $mobileAudio = @($mobileEncoded.streams | Where-Object codec_type -eq 'audio')
    if ($mobileVideo.Count -ne 1 -or $mobileVideo[0].codec_name -ne 'h264' -or
        $mobileVideo[0].pix_fmt -ne 'yuv420p' -or $mobileVideo[0].r_frame_rate -ne '30/1' -or
        $mobileVideo[0].width -gt 1280 -or $mobileVideo[0].height -gt 720 -or
        $mobileVideo[0].width % 2 -ne 0 -or $mobileVideo[0].height % 2 -ne 0 -or
        $mobileAudio.Count -ne [int]($mobileSourceAudio.Count -gt 0) -or
        ($mobileAudio.Count -gt 0 -and $mobileAudio[0].codec_name -ne 'aac') -or
        [Math]::Abs($mobileDuration - $mobileSourceDuration) -gt 0.15) {
        throw 'Encoded video changed the timeline or failed the mobile media contract.'
    }
    $mobileLayout = Read-MobileMp4Layout $mobilePending
    if ((Get-FileHash -LiteralPath $mobileInput -Algorithm SHA256).Hash.ToLowerInvariant() -ne $mobileSourceHash) {
        throw 'Source take changed during encoding.'
    }
    $mobileEvidence = [ordered]@{
        schema = 'opennv-mobile-gameplay-media/v1'
        input = $mobileInput
        inputSha256 = $mobileSourceHash
        output = $mobileOutput
        outputSha256 = (Get-FileHash -LiteralPath $mobilePending -Algorithm SHA256).Hash.ToLowerInvariant()
        bytes = (Get-Item -LiteralPath $mobilePending).Length
        durationSeconds = $mobileDuration
        sourceDurationSeconds = $mobileSourceDuration
        width = $mobileVideo[0].width
        height = $mobileVideo[0].height
        framesPerSecond = 30
        videoCodec = 'h264'
        audio = $(if ($mobileAudio.Count -gt 0) { 'aac-from-source-take' } else { 'source-take-has-no-audio' })
        layout = $mobileLayout
        timeline = 'source-duration-preserved;video-resampled-to-30-fps'
        gameplayAcceptance = 'requires-independent-route-and-save-review'
        sourceTakeReplaced = $false
    }
    [IO.File]::WriteAllText($mobilePendingReport, ($mobileEvidence | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))
    [IO.File]::Move($mobilePending, $mobileOutput, $false)
    $mobilePublished = $true
    [IO.File]::Move($mobilePendingReport, $mobileReport, $false)
    Write-Output ('OPENNV_MOBILE_GAMEPLAY_MEDIA_PASS ' + ($mobileEvidence | ConvertTo-Json -Depth 4 -Compress))
} catch {
    if ($mobilePublished -and (Test-Path -LiteralPath $mobileOutput)) { Remove-Item -LiteralPath $mobileOutput }
    throw
} finally {
    foreach ($mobileScratch in @($mobilePending, $mobilePendingReport)) {
        if (Test-Path -LiteralPath $mobileScratch) { Remove-Item -LiteralPath $mobileScratch }
    }
}
