using System.Windows.Controls;

namespace DeepSeekPet;

public partial class MainWindow
{
    private readonly DanceAudio _danceAudio = new();
    private DanceMotion _danceMotion = new();
    private CancellationTokenSource? _danceLoading;
    private MenuItem? _danceMenuItem;
    private bool _isDancing;

    private async Task OpenDance(bool startDefault = true)
    {
        if (_isDancing) { StopDance(); return; }
        _isDancing = true;
        _isWalking = false;
        _danceMotion = new DanceMotion();
        HideAssistant(immediate: true);
        _danceMenuItem!.Header = "停止跳舞";
        _danceMenuItem.ToolTip = null;
        UpdateDance(1d / 60);
        if (!startDefault) return; // Offline integration checks exercise the same animation path.

        using var request = new CancellationTokenSource();
        _danceLoading = request;
        try
        {
            var path = await DanceAudio.GetTrackAsync(request.Token);
            if (request.IsCancellationRequested || !_isDancing) return;
            _danceAudio.Load(path);
            // Start the tempo clock with the local music.
            _danceMotion = new DanceMotion();
            _danceAudio.Toggle();
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!request.IsCancellationRequested)
            {
                StopDance();
                _danceMenuItem.Header = "跳舞加载失败 · 点击重试";
                _danceMenuItem.ToolTip = "请检查本地音频文件和音频设备后重试。";
            }
        }
        finally { if (ReferenceEquals(_danceLoading, request)) _danceLoading = null; }
    }

    private void StopDance()
    {
        _isDancing = false;
        _danceLoading?.Cancel();
        _danceLoading = null;
        _danceAudio.Dispose();
        if (_danceMenuItem != null) _danceMenuItem.Header = "跳舞 · 朋友的酒 DJ";
        _reactionStart = -10;
        ScheduleNextBehavior(_clock.Elapsed.TotalSeconds, startWalking: false);
        UpdateAnimation(_clock.Elapsed.TotalSeconds);
    }

    private void UpdateDance(double dt)
    {
        if (_danceAudio.HasTrack && !_danceAudio.IsPlaying)
        {
            var failed = _danceAudio.PlaybackError != null;
            StopDance();
            if (failed) _danceMenuItem!.Header = "音频播放失败 · 点击重试";
            return;
        }
        _danceMotion.Step(dt, true, _danceAudio.IsPlaying, DanceMotion.DefaultBpm, _danceAudio.Bass, _danceAudio.Energy);
        // Keep its facing and bottom-center pivot; width gently counters the vertical spring.
        var pose = _danceMotion.Pose();
        PetScale.ScaleX = _direction * pose.X;
        PetScale.ScaleY = pose.Y;
        PetRotate.Angle = 0;
        PetTranslate.X = PetTranslate.Y = 0;
    }
}
