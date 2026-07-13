using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Audio_Enhancer
{
    public sealed class EnhanceOptions
    {
        public bool NormalizeLoudness { get; set; } = true;
        public bool RemoveDcOffset { get; set; } = true;
        /// <summary>Target peak level after normalization (dBFS).</summary>
        public double TargetPeakDb { get; set; } = -1.0;
    }

    public sealed class EnhanceResult
    {
        public double AppliedGainDb { get; init; }
        public double PeakBeforeDb { get; init; }
        public TimeSpan Duration { get; init; }
    }

    public static class AudioProcessor
    {
        public const int TargetSampleRate = 48000;
        public const int TargetBits = 24;

        private static readonly Guid PcmSubFormat = new("00000001-0000-0010-8000-00AA00389B71");
        private static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00AA00389B71");

        public static (WaveFormat Format, TimeSpan Duration, long FileSizeBytes) ReadInfo(string path)
        {
            using var reader = new WaveFileReader(path);
            return (reader.WaveFormat, reader.TotalTime, new FileInfo(path).Length);
        }

        public static string DescribeFormat(WaveFormat format)
        {
            string channels = format.Channels switch
            {
                1 => "mono",
                2 => "stereo",
                var n => $"{n} channels",
            };
            string bits = format.Encoding == WaveFormatEncoding.IeeeFloat
                          || (format is WaveFormatExtensible ext && ext.SubFormat == IeeeFloatSubFormat)
                ? $"{format.BitsPerSample}-bit float"
                : $"{format.BitsPerSample}-bit";
            return $"{format.SampleRate / 1000.0:0.###} kHz • {bits} • {channels}";
        }

        public static EnhanceResult Enhance(string inputPath, string outputPath, EnhanceOptions options,
            IProgress<double>? progress, CancellationToken ct)
        {
            using var reader = new WaveFileReader(inputPath);
            var sampleProvider = NormalizeWaveFormat(reader).ToSampleProvider();
            int channels = sampleProvider.WaveFormat.Channels;
            if (sampleProvider.WaveFormat.SampleRate != TargetSampleRate)
                sampleProvider = new WdlResamplingSampleProvider(sampleProvider, TargetSampleRate);

            // 1) Read the whole (already resampled) signal into memory in chunks.
            var chunks = new List<float[]>();
            var buffer = new float[TargetSampleRate * channels];
            long totalSamples = 0;
            int read;
            while ((read = sampleProvider.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                var chunk = new float[read];
                Array.Copy(buffer, chunk, read);
                chunks.Add(chunk);
                totalSamples += read;
                if (reader.Length > 0)
                    progress?.Report(0.70 * Math.Min(1.0, (double)reader.Position / reader.Length));
            }

            if (totalSamples == 0)
                throw new InvalidDataException("The file contains no audio data.");

            // 2) DC offset – per-channel mean.
            var dc = new double[channels];
            if (options.RemoveDcOffset)
            {
                var sums = new double[channels];
                int ch = 0;
                foreach (var chunk in chunks)
                    for (int i = 0; i < chunk.Length; i++)
                    {
                        sums[ch] += chunk[i];
                        ch = (ch + 1) % channels;
                    }
                long framesPerChannel = totalSamples / channels;
                for (int c = 0; c < channels; c++)
                    dc[c] = sums[c] / framesPerChannel;
            }

            // 3) Peak after DC offset removal.
            float peak = 0f;
            {
                int ch = 0;
                foreach (var chunk in chunks)
                    for (int i = 0; i < chunk.Length; i++)
                    {
                        float a = Math.Abs((float)(chunk[i] - dc[ch]));
                        if (a > peak) peak = a;
                        ch = (ch + 1) % channels;
                    }
            }

            double gain = 1.0;
            if (options.NormalizeLoudness && peak > 0f)
                gain = Math.Pow(10.0, options.TargetPeakDb / 20.0) / peak;

            ct.ThrowIfCancellationRequested();
            progress?.Report(0.72);

            // 4) Write 24-bit PCM @ 48 kHz.
            var outFormat = new WaveFormat(TargetSampleRate, TargetBits, channels);
            using (var writer = new WaveFileWriter(outputPath, outFormat))
            {
                var byteBuffer = new byte[buffer.Length * 3];
                long written = 0;
                int ch = 0;
                foreach (var chunk in chunks)
                {
                    ct.ThrowIfCancellationRequested();
                    int bi = 0;
                    for (int i = 0; i < chunk.Length; i++)
                    {
                        double v = (chunk[i] - dc[ch]) * gain;
                        if (v > 1.0) v = 1.0;
                        else if (v < -1.0) v = -1.0;
                        int s = (int)Math.Round(v * 8388607.0);
                        byteBuffer[bi++] = (byte)s;
                        byteBuffer[bi++] = (byte)(s >> 8);
                        byteBuffer[bi++] = (byte)(s >> 16);
                        ch = (ch + 1) % channels;
                    }
                    writer.Write(byteBuffer, 0, bi);
                    written += chunk.Length;
                    progress?.Report(0.72 + 0.28 * written / totalSamples);
                }
            }

            progress?.Report(1.0);
            return new EnhanceResult
            {
                AppliedGainDb = 20.0 * Math.Log10(gain),
                PeakBeforeDb = peak > 0f ? 20.0 * Math.Log10(peak) : double.NegativeInfinity,
                Duration = TimeSpan.FromSeconds((double)(totalSamples / channels) / TargetSampleRate),
            };
        }

        // NAudio sample converters don't understand WAVE_FORMAT_EXTENSIBLE headers
        // (common in 24-bit files) – rewrap them as the equivalent plain PCM/float format.
        private static IWaveProvider NormalizeWaveFormat(WaveFileReader reader)
        {
            if (reader.WaveFormat is not WaveFormatExtensible ext)
                return reader;

            WaveFormat plain;
            if (ext.SubFormat == IeeeFloatSubFormat)
                plain = WaveFormat.CreateIeeeFloatWaveFormat(ext.SampleRate, ext.Channels);
            else if (ext.SubFormat == PcmSubFormat)
                plain = new WaveFormat(ext.SampleRate, ext.BitsPerSample, ext.Channels);
            else
                throw new InvalidDataException("Unsupported WAV format (unknown SubFormat).");

            return new ReinterpretedFormatWaveProvider(reader, plain);
        }

        private sealed class ReinterpretedFormatWaveProvider(IWaveProvider source, WaveFormat format) : IWaveProvider
        {
            public WaveFormat WaveFormat => format;
            public int Read(byte[] buffer, int offset, int count) => source.Read(buffer, offset, count);
        }
    }
}
