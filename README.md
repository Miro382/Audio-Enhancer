# Audio Enhancer

**Audio Enhancer** is a simple desktop tool for improving WAV audio quality. Drop in a WAV file and it converts it to high-res 24-bit / 48 kHz, normalizes loudness to a −1 dB peak, and removes DC offset — all without altering the actual content of the recording.

Many recordings are quieter than they need to be, use a low-resolution format, or carry a slight DC offset from the recording hardware. Audio Enhancer fixes all of that in one click while keeping the recording itself completely intact — no EQ, no compression, no effects.

Built with C# and [Avalonia](https://avaloniaui.net/), audio processing powered by [NAudio](https://github.com/naudio/NAudio).

## Features

- **High-res output** — every file is saved as 24-bit / 48 kHz PCM WAV
- **High-quality resampling** — sample rate conversion uses the WDL resampler (files already at 48 kHz are left untouched)
- **Loudness normalization** *(optional)* — finds the loudest point of the recording and raises the whole file evenly so the peak sits at −1 dBFS
- **DC offset removal** *(optional)* — re-centers the waveform around zero, improving headroom and signal cleanliness
- **Drag & drop** — drop a WAV file straight onto the window, or pick it with a file dialog
- **Wide input support** — 8/16/24/32-bit PCM and 32/64-bit float WAV files, including `WAVE_FORMAT_EXTENSIBLE` headers, mono/stereo/multichannel
- **Non-destructive** — the original file is never modified; the result is always saved as a new file

## How it works

1. **Decode** — the input WAV is decoded into 32-bit floating point samples, regardless of its original bit depth.
2. **Resample** — if the sample rate differs from 48 kHz, the signal is resampled with the high-quality WDL resampler.
3. **Analyze** — the whole signal is scanned to find the per-channel DC offset (average value) and the true peak level.
4. **Correct** — the DC offset is subtracted and a single constant gain is applied to the entire recording so the peak lands at −1 dBFS. Because every sample is scaled by the same factor, dynamics and character stay exactly the same — only the overall volume changes.
5. **Encode** — the result is written as 24-bit PCM WAV at 48 kHz.

> **Note:** converting e.g. a 16-bit / 44.1 kHz file to 24-bit / 48 kHz cannot invent detail that was never recorded. What it does is a lossless-quality conversion into a high-res container with improved loudness and a cleaner signal — nothing essential in the audio is changed.

## Usage

### Requirements

- Windows, Linux or macOS
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (to build from source)

### Build and run

```
dotnet run --project "Audio Enhancer"
```

or open `Audio Enhancer.slnx` in Visual Studio / Rider and press **F5**.

### Enhancing a file

1. Launch the app.
2. Drag a `.wav` file onto the window, or click **Choose file…** — the file's format, duration and size appear below.
3. (Optional) toggle the enhancements:
   - **Normalize loudness (peak to −1 dB)**
   - **Remove DC offset (center the signal)**
4. Click **Enhance and save…**, pick where to save the result (a name like `song (24-bit 48 kHz).wav` is suggested).
5. Wait for the progress bar to finish — the status line shows the saved file and how much the loudness was adjusted (e.g. *Loudness adjusted by +11.0 dB*).
