namespace DeepSeekPet;

public sealed class DanceMotion
{
    public const double DefaultBpm = 130;
    // Each beat starts and ends at neutral. No hold at the peak or beat boundary.
    // Fractions keep the full movement inside one beat if the tempo is adjusted.
    public const double CompressEnd = .090 * DefaultBpm / 60;
    public const double StretchEnd = .260 * DefaultBpm / 60;

    public double Phase { get; private set; }
    public double Squash => (1 - _scaleY) / .22;
    public double Activity { get; private set; }
    public bool Drop { get; private set; }
    private double _scaleX = 1, _scaleY = 1, _strength = 1;
    private double _dropHold, _pauseElapsed, _pauseX = 1, _pauseY = 1;
    private bool _wasPlaying, _pendingDrop;

    public void Step(double dt, bool playing, bool automatic, double bpm, double bass, double energy)
    {
        dt = Math.Clamp(dt, 0, .25);
        bass = automatic ? Math.Clamp(bass, 0, 1) : 0;
        energy = automatic ? Math.Clamp(energy, 0, 1) : 0;
        Activity += ((playing ? 1 : 0) - Activity) * (1 - Math.Exp(-dt * 8));
        if (!playing)
        {
            if (_wasPlaying) { _pauseElapsed = 0; _pauseX = _scaleX; _pauseY = _scaleY; }
            _wasPlaying = false;
            _pauseElapsed += dt;
            var settle = Smooth(_pauseElapsed / .15);
            _scaleX = Mix(_pauseX, 1, settle); _scaleY = Mix(_pauseY, 1, settle);
            Drop = _pendingDrop = false; _dropHold = 0;
            return;
        }

        _dropHold = Math.Max(0, _dropHold - dt);
        if (automatic && energy > .72) { _pendingDrop = true; _dropHold = .6; }
        if (!automatic || (energy < .48 && _dropHold == 0)) _pendingDrop = false;
        var previousBeat = Math.Floor(Phase / (2 * Math.PI));
        Phase += dt * Math.Clamp(bpm, 60, 200) / 60 * 2 * Math.PI;
        var beats = Phase / (2 * Math.PI);
        if (!_wasPlaying || Math.Floor(beats) != previousBeat)
        {
            // Latch once per beat. Sustained bass changes strength, never the return path.
            // Also latch Drop here so changing modes cannot reshape a half-finished bounce.
            _strength = automatic ? .65 + .35 * bass : 1;
            Drop = _pendingDrop;
        }
        _wasPlaying = true;
        var phase = beats - Math.Floor(beats);
        var compressedX = 1 + (Drop ? .35 : .18) * _strength;
        var compressedY = 1 - (Drop ? .45 : .22) * _strength;
        var stretchedX = 1 - (Drop ? .12 : .06) * _strength;
        var stretchedY = 1 + (Drop ? .15 : .08) * _strength;
        // Matching non-zero boundary tangents make neutral a through-point, not a stop.
        // Tangents are per beat phase and independent of strength/Drop, so mode changes
        // at the boundary preserve velocity as well as position.
        _scaleX = Cycle(phase, compressedX, stretchedX, .15);
        _scaleY = Cycle(phase, compressedY, stretchedY, -.20);
    }

    private static double Cycle(double phase, double compressed, double stretched, double tangent)
    {
        if (phase < CompressEnd)
            return Hermite(1, compressed, tangent, 0, phase / CompressEnd, CompressEnd);
        if (phase < StretchEnd)
            return Hermite(compressed, stretched, 0, 0, (phase - CompressEnd) / (StretchEnd - CompressEnd), StretchEnd - CompressEnd);
        return Hermite(stretched, 1, 0, tangent, (phase - StretchEnd) / (1 - StretchEnd), 1 - StretchEnd);
    }

    // Cubic Hermite retains velocity through joins. At an extremum velocity naturally
    // crosses zero with non-zero acceleration, instead of easing acceleration to zero
    // and hanging at the top. There is no hard clamp or timer reset at that peak.
    private static double Hermite(double from, double to, double startVelocity, double endVelocity, double t, double duration)
    {
        var t2 = t * t; var t3 = t2 * t;
        return (2 * t3 - 3 * t2 + 1) * from + (t3 - 2 * t2 + t) * duration * startVelocity
            + (-2 * t3 + 3 * t2) * to + (t3 - t2) * duration * endVelocity;
    }
    // Smooth settling is only used when playback stops, never at a dance-cycle peak.
    // No hard position clamp, no collision-style velocity reset, no truncated rebound.
    private static double Smooth(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * t * (t * (t * 6 - 15) + 10);
    }
    private static double Mix(double from, double to, double weight) => from + (to - from) * weight;
    public (double X, double Y, double Angle, double Skew) Pose() => (_scaleX, _scaleY, 0, 0);
}
