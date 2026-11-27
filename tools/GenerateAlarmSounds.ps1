[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\src\JPSoftworks.TimersExtension\Assets\Sounds')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$source = @'
using System;
using System.IO;
using System.Text;

public static class TimersAlarmSoundGenerator
{
    private const int SampleRate = 44100;
    private delegate double Sampler(double time);

    public static void Generate(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        WriteWave(Path.Combine(outputDirectory, "aurora.wav"), 3.4, Aurora);
        WriteWave(Path.Combine(outputDirectory, "gentle-chime.wav"), 3.0, GentleChime);
        WriteWave(Path.Combine(outputDirectory, "bright-bell.wav"), 2.6, BrightBell);
        WriteWave(Path.Combine(outputDirectory, "digital-pulse.wav"), 2.2, DigitalPulse);
        WriteWave(Path.Combine(outputDirectory, "urgent-alert.wav"), 2.4, UrgentAlert);
    }

    private static double Aurora(double t)
    {
        return
            Chime(t, 0.00, 523.25, 2.60, 0.42, 0.85) +
            Chime(t, 0.32, 659.25, 2.55, 0.38, 0.90) +
            Chime(t, 0.64, 783.99, 2.45, 0.36, 0.95) +
            Chime(t, 1.02, 1046.50, 2.15, 0.25, 1.05);
    }

    private static double GentleChime(double t)
    {
        return
            SoftTone(t, 0.00, 392.00, 1.80, 0.42, 1.45) +
            SoftTone(t, 0.46, 493.88, 1.75, 0.38, 1.50) +
            SoftTone(t, 0.92, 587.33, 1.70, 0.34, 1.55) +
            SoftTone(t, 1.38, 783.99, 1.45, 0.24, 1.65);
    }

    private static double BrightBell(double t)
    {
        return
            Bell(t, 0.00, 659.25, 1.65, 0.52, 1.85) +
            Bell(t, 0.55, 880.00, 1.60, 0.48, 1.90) +
            Bell(t, 1.10, 1046.50, 1.50, 0.43, 2.00);
    }

    private static double DigitalPulse(double t)
    {
        return
            Pulse(t, 0.00, 523.25, 0.24, 0.52) +
            Pulse(t, 0.30, 659.25, 0.24, 0.48) +
            Pulse(t, 0.60, 783.99, 0.24, 0.50) +
            Pulse(t, 1.05, 659.25, 0.24, 0.48) +
            Pulse(t, 1.35, 783.99, 0.24, 0.50) +
            Pulse(t, 1.65, 1046.50, 0.30, 0.46);
    }

    private static double UrgentAlert(double t)
    {
        double value = 0;
        for (int i = 0; i < 8; i++)
        {
            double frequency = i % 2 == 0 ? 740.00 : 987.77;
            value += AlertTone(t, i * 0.27, frequency, 0.19, 0.48);
        }

        return value;
    }

    private static double Chime(double t, double start, double frequency, double duration, double amplitude, double decay)
    {
        double x = t - start;
        double envelope = Envelope(x, duration, 0.010, 0.18, decay);
        if (envelope == 0)
        {
            return 0;
        }

        double phase = 2 * Math.PI * frequency * x;
        double tone =
            Math.Sin(phase) +
            0.34 * Math.Sin(phase * 2.01) +
            0.18 * Math.Sin(phase * 3.98) +
            0.08 * Math.Sin(phase * 6.03);
        return amplitude * envelope * tone / 1.60;
    }

    private static double SoftTone(double t, double start, double frequency, double duration, double amplitude, double decay)
    {
        double x = t - start;
        double envelope = Envelope(x, duration, 0.025, 0.25, decay);
        if (envelope == 0)
        {
            return 0;
        }

        double phase = 2 * Math.PI * frequency * x;
        double tone = Math.Sin(phase) + 0.20 * Math.Sin(phase * 2) + 0.07 * Math.Sin(phase * 3);
        return amplitude * envelope * tone / 1.27;
    }

    private static double Bell(double t, double start, double frequency, double duration, double amplitude, double decay)
    {
        double x = t - start;
        double envelope = Envelope(x, duration, 0.004, 0.12, decay);
        if (envelope == 0)
        {
            return 0;
        }

        double phase = 2 * Math.PI * frequency * x;
        double tone =
            Math.Sin(phase) +
            0.42 * Math.Sin(phase * 2.71) +
            0.20 * Math.Sin(phase * 4.08) +
            0.10 * Math.Sin(phase * 5.43);
        return amplitude * envelope * tone / 1.72;
    }

    private static double Pulse(double t, double start, double frequency, double duration, double amplitude)
    {
        double x = t - start;
        double envelope = Envelope(x, duration, 0.008, 0.06, 3.2);
        if (envelope == 0)
        {
            return 0;
        }

        double phase = 2 * Math.PI * frequency * x;
        double tone = Math.Sin(phase) + 0.25 * Math.Sin(phase * 2) + 0.10 * Math.Sin(phase * 4);
        return amplitude * envelope * tone / 1.35;
    }

    private static double AlertTone(double t, double start, double frequency, double duration, double amplitude)
    {
        double x = t - start;
        double envelope = Envelope(x, duration, 0.006, 0.035, 1.8);
        if (envelope == 0)
        {
            return 0;
        }

        double phase = 2 * Math.PI * frequency * x;
        double tone = Math.Sin(phase) + 0.30 * Math.Sin(phase * 2) + 0.12 * Math.Sin(phase * 3);
        return amplitude * envelope * tone / 1.42;
    }

    private static double Envelope(
        double time,
        double duration,
        double attack,
        double release,
        double decay)
    {
        if (time < 0 || time >= duration)
        {
            return 0;
        }

        double attackGain = Math.Min(1, time / attack);
        double releaseGain = Math.Min(1, (duration - time) / release);
        return attackGain * releaseGain * Math.Exp(-decay * time);
    }

    private static void WriteWave(string path, double duration, Sampler sampler)
    {
        int sampleCount = (int)Math.Ceiling(duration * SampleRate);
        var samples = new double[sampleCount];
        double peak = 0;

        for (int i = 0; i < sampleCount; i++)
        {
            double sample = sampler(i / (double)SampleRate);
            samples[i] = sample;
            peak = Math.Max(peak, Math.Abs(sample));
        }

        double scale = peak > 0 ? 0.82 / peak : 0;
        int dataLength = sampleCount * sizeof(short);

        using (var stream = File.Create(path))
        using (var writer = new BinaryWriter(stream, Encoding.ASCII))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataLength);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(SampleRate);
            writer.Write(SampleRate * sizeof(short));
            writer.Write((short)sizeof(short));
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataLength);

            for (int i = 0; i < samples.Length; i++)
            {
                double normalized = Math.Max(-1, Math.Min(1, samples[i] * scale));
                writer.Write((short)Math.Round(normalized * short.MaxValue));
            }
        }
    }
}
'@

Add-Type -TypeDefinition $source -Language CSharp
[TimersAlarmSoundGenerator]::Generate((Resolve-Path -LiteralPath (New-Item -ItemType Directory -Force -Path $OutputDirectory)).Path)

Get-ChildItem -LiteralPath $OutputDirectory -Filter '*.wav' |
    Sort-Object Name |
    Select-Object Name, Length
