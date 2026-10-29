using System.IO;

using NAudio.Dsp;
using NAudio.Wave;

namespace DeepSeekPet;

// Analysis is in the playback pipeline, never the microphone or system loopback.
public sealed class DanceAudio : IDisposable
{
    public const string SourcePage = "https://www.bilibili.com/video/BV1kFar6KE4t";
    public const string TrackName = "朋友的酒 DJ · B站缓存（3分05秒循环）";
    private AudioFileReader? _reader;
    private WaveOutEvent? _output;
    private SpectrumProvider? _spectrum;
    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;
    public bool HasTrack => _reader != null;
    public double Seconds => _reader == null ? 0 : Math.Max(0, _reader.CurrentTime.TotalSeconds - (IsPlaying ? .08 : 0));
    public double Duration => _reader?.TotalTime.TotalSeconds ?? 0;
    public double Bass => IsPlaying ? _spectrum?.Bass ?? 0 : 0;
    public double Energy => IsPlaying ? _spectrum?.Energy ?? 0 : 0;
    public string? PlaybackError { get; private set; }
    public float Volume { get => _output?.Volume ?? .55f; set { if (_output != null) _output.Volume = Math.Clamp(value, 0, 1); } }

    public static Task<string> GetTrackAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Audio", "friends-wine-bilibili.m4a");
        if (!File.Exists(path)) throw new FileNotFoundException("缺少本地跳舞音频，请补齐 Assets/Audio/friends-wine-bilibili.m4a。", path);
        return Task.FromResult(path);
    }
    public void Load(string path)
    {
        Dispose();
        PlaybackError = null;
        try
        {
            _reader = new AudioFileReader(path);
            _spectrum = new SpectrumProvider(new LoopingProvider(_reader));
            _output = new WaveOutEvent { DesiredLatency = 80, NumberOfBuffers = 2, Volume = .55f };
            _output.Init(_spectrum);
            _output.PlaybackStopped += (_, e) => { if (e.Exception != null) PlaybackError = "音频设备不可用，请重新选曲。"; };
        }
        catch { Dispose(); throw; }
    }

    public void Toggle()
    {
        if (_output == null || _reader == null) return;
        if (IsPlaying) _output.Pause();
        else
        {
            if (_reader.Position >= _reader.Length) { _reader.Position = 0; _spectrum?.Reset(); }
            _output.Play();
        }
    }

    public void Dispose()
    {
        _output?.Dispose(); _output = null;
        _reader?.Dispose(); _reader = null;
        _spectrum = null;
    }

    // Wrap at decoded EOF inside the output pipeline, without stopping/reopening the device.
    public sealed class LoopingProvider(WaveStream source) : ISampleProvider
    {
        private readonly ISampleProvider _samples = source.ToSampleProvider();
        public WaveFormat WaveFormat => _samples.WaveFormat;
        public int Read(float[] buffer, int offset, int count)
        {
            var total = 0;
            while (total < count)
            {
                var read = _samples.Read(buffer, offset + total, count - total);
                if (read == 0)
                {
                    source.Position = 0;
                    read = _samples.Read(buffer, offset + total, count - total);
                    if (read == 0) break; // Empty or unreadable source must not spin forever.
                }
                total += read;
            }
            return total;
        }
    }
    public sealed class SpectrumProvider(ISampleProvider source) : ISampleProvider
    {
        // 2048-point Hann FFT (~46 ms at 44.1 kHz). Kick band: 20–150 Hz.
        private const int Size = 2048;
        private readonly Complex[] _fft = new Complex[Size];
        private readonly float[] _samples = new float[Size];
        private int _position;
        private float _bass, _energy;
        public double Bass => Volatile.Read(ref _bass);
        public double Energy => Volatile.Read(ref _energy);
        public WaveFormat WaveFormat => source.WaveFormat;
        public void Reset() { _position = 0; _bass = _energy = 0; }
        public int Read(float[] buffer, int offset, int count)
        {
            var read = source.Read(buffer, offset, count);
            var channels = WaveFormat.Channels;
            for (var i = 0; i + channels <= read; i += channels)
            {
                // Use the first channel to avoid cancellation in phase-inverted stereo.
                _samples[_position++] = buffer[offset + i];
                if (_position < Size) continue;
                _position = 0;
                double sum = 0;
                for (var n = 0; n < Size; n++)
                {
                    sum += _samples[n] * _samples[n];
                    _fft[n].X = (float)(_samples[n] * .5 * (1 - Math.Cos(2 * Math.PI * n / (Size - 1))));
                    _fft[n].Y = 0;
                }
                FastFourierTransform.FFT(true, 11, _fft);
                double bass = 0;
                for (var n = 1; n < Size / 2; n++)
                {
                    var hz = n * WaveFormat.SampleRate / (double)Size;
                    if (hz is >= 20 and <= 150) bass += _fft[n].X * _fft[n].X + _fft[n].Y * _fft[n].Y;
                }
                // Tunable gains: normalized kick energy and overall RMS (pre-volume).
                Volatile.Write(ref _bass, (float)Math.Clamp(Math.Sqrt(bass) * 8, 0, 1));
                Volatile.Write(ref _energy, (float)Math.Clamp(Math.Sqrt(sum / Size) * 2.8, 0, 1));
            }
            return read;
        }
    }
}
