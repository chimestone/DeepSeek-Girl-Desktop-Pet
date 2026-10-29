using System.IO;
using System.Net.Http;
using NAudio.Dsp;
using NAudio.Wave;

namespace DeepSeekPet;

// Analysis is in the playback pipeline, never the microphone or system loopback.
public sealed class DanceAudio : IDisposable
{
    public const string SourcePage = "https://www.dj95.com/mp3/121445.html";
    public const string TrackName = "朋友的酒 · DJ阿帆 Electro Rmx 2020（试听）";
    private const string PreviewUrl = "https://dj95.oss-cn-hangzhou.aliyuncs.com/mp4/zwhouse/2020/001/121445_www_Dj95_com.mp4";
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

    public static async Task<string> GetPreviewAsync(CancellationToken token)
    {
        // Public low-bitrate preview; no commercial music is embedded in releases.
        var directory = Path.Combine(PetSettings.SettingsDirectory, "DanceCache");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "friends-wine-dj-afan-preview.m4a");
        if (File.Exists(path) && new FileInfo(path).Length > 1024) return path;
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
            using var response = await http.GetAsync(PreviewUrl, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = File.Create(temp))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, token)) > 0)
                {
                    total += read;
                    if (total > 32 * 1024 * 1024) throw new IOException("试听文件过大，请使用本地音频。");
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                }
                if (total < 1024) throw new IOException("试听音源暂不可用。");
            }
            File.Move(temp, path, true);
            return path;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public void Load(string path)
    {
        Dispose();
        PlaybackError = null;
        try
        {
            _reader = new AudioFileReader(path);
            _spectrum = new SpectrumProvider(_reader);
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
