using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeepSeekPet;
using NAudio.Wave;

static class DanceChecks
{
    public static void Run()
    {
        var low = Analyze(65); var high = Analyze(1000); var silence = Analyze(0);
        Check(low.Bass > .3 && high.Bass < low.Bass * .03, "FFT isolates kick band");
        Check(silence.Bass == 0 && silence.Energy == 0, "silence has no energy");
        foreach (var fps in new[] { 30, 60, 144 })
        {
            var motion = new DanceMotion();
            for (var i = 0; i < fps * 10; i++)
            {
                motion.Step(1d / fps, true, false, DanceMotion.DefaultBpm, 0, 0);
                var p = motion.Pose();
                Check(p.X is >= .94 and <= 1.18 && p.Y is >= .78 and <= 1.08 && p.Angle == 0 && p.Skew == 0, "normal envelope without tilt");

            }
            Check(Math.Abs(motion.Phase / (2 * Math.PI) - 130d / 6) < 1e-8, "130 BPM independent of render frame rate");
            for (var i = 0; i < fps * 3; i++) motion.Step(1d / fps, false, true, DanceMotion.DefaultBpm, 0, 0);
            Check(Math.Abs(motion.Squash) < .001 && motion.Activity < .001, "pause settles");
        }
        var drop = new DanceMotion();
        drop.Step(.02, true, true, 128, .9, .9); Check(drop.Drop, "drop enters");
        for (var i = 0; i < 100; i++) drop.Step(.02, true, true, 128, 0, 0);
        Check(!drop.Drop, "drop releases");
        VerifySpecifiedMotion();
        Exception? failure = null;
        var thread = new Thread(() => { try { VerifyVisuals(); } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) throw new Exception("Dance visuals", failure);

        var args = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(args, "--dance-audio");
        if (index >= 0 && index + 1 < args.Length)
        {
            using var reader = new AudioFileReader(args[index + 1]);
            var spectrum = new DanceAudio.SpectrumProvider(reader);
            var samples = new float[8192]; double peak = 0;
            for (var n = 0; n < 200 && spectrum.Read(samples, 0, samples.Length) > 0; n++) peak = Math.Max(peak, spectrum.Bass);
            Check(reader.TotalTime.TotalSeconds > 30 && peak > .05, "downloaded preview decodes and contains bass");
            Console.WriteLine($"PASS: preview decodes ({reader.TotalTime:mm\\:ss}), bass peak={peak:F2}.");
            using var playback = new DanceAudio();
            playback.Load(args[index + 1]); playback.Volume = .08f; playback.Toggle();
            Thread.Sleep(250); Check(playback.IsPlaying, "audio device starts");
            playback.Toggle(); Check(!playback.IsPlaying, "audio pauses");
            var paused = playback.Seconds; Thread.Sleep(120);
            Check(Math.Abs(playback.Seconds - paused) < .01, "pause stops position");
            playback.Toggle(); Thread.Sleep(150); Check(playback.IsPlaying, "audio resumes");
            playback.Dispose(); Check(!playback.HasTrack && !playback.IsPlaying, "audio releases on exit");
            Console.WriteLine("PASS: real audio device play, pause, resume, dispose.");
        }
        Console.WriteLine("PASS: dance FFT, spring at 30/60/144 fps, pause, Drop, stationary vertical bounce, no extra window, stop and restart.");
    }

    private static void VerifyVisuals()
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var pet = new MainWindow();
        typeof(MainWindow).GetField("_settings", flags)!.SetValue(pet, new PetSettings { AutoWalk = false });
        pet.Show();
        try
        {
            var open = typeof(MainWindow).GetMethod("OpenDance", flags)!;
            ((Task)open.Invoke(pet, [false])!).GetAwaiter().GetResult();
            var field = typeof(MainWindow).GetField("_isDancing", flags)!;
            Check((bool)field.GetValue(pet)!, "dance starts immediately");
            Check(pet.OwnedWindows.Count == 0, "no extra window");
            Check(((FrameworkElement)pet.FindName("PetVisual")).Visibility == Visibility.Visible, "uses original pet");
            var settings = (PetSettings)typeof(MainWindow).GetField("_settings", flags)!.GetValue(pet)!;
            settings.AutoWalk = true;
            typeof(MainWindow).GetField("_isWalking", flags)!.SetValue(pet, true);
            var left = pet.Left; var top = pet.Top;
            var scale = (ScaleTransform)pet.FindName("PetScale");
            var width = scale.ScaleX; var minHeight = 2d; var maxHeight = 0d;
            for (var i = 0; i < 180; i++)
            {
                typeof(MainWindow).GetMethod("UpdateBehavior", flags)!.Invoke(pet, [100d + i, 1d / 60]);
                typeof(MainWindow).GetMethod("UpdateDance", flags)!.Invoke(pet, [1d / 60]);
                Check(pet.Left == left && pet.Top == top, "pet stays stationary");
                Check(Math.Sign(scale.ScaleX) == Math.Sign(width) && ((RotateTransform)pet.FindName("PetRotate")).Angle == 0, "preserves facing without rotation");

                Check(((TranslateTransform)pet.FindName("PetTranslate")).X == 0, "no sideways drift");
                minHeight = Math.Min(minHeight, scale.ScaleY); maxHeight = Math.Max(maxHeight, scale.ScaleY);
            }
            Check(minHeight >= .78 && maxHeight <= 1.08 && maxHeight - minHeight > .15, "specified normal bounce range");
            ((Task)open.Invoke(pet, [false])!).GetAwaiter().GetResult();
            Check(!(bool)field.GetValue(pet)!, "second click stops");
            Check(settings.AutoWalk, "preserves walking preference");
            ((Task)open.Invoke(pet, [false])!).GetAwaiter().GetResult();
            Check((bool)field.GetValue(pet)! && pet.OwnedWindows.Count == 0, "restart without window");
        }
        finally { pet.Close(); }
    }

    private static void VerifySpecifiedMotion()
    {
        foreach (var drop in new[] { false, true })
        {
            var motion = new DanceMotion();
            void Advance(double seconds) { while (seconds > 1e-10) { var dt = Math.Min(.01, seconds); motion.Step(dt, true, true, 130, 1, drop ? .9 : .4); seconds -= dt; } }
            Advance(.09);
            Check(Math.Abs(motion.Pose().Y - (drop ? .55 : .78)) < 1e-8 && Math.Abs(motion.Pose().X - (drop ? 1.35 : 1.18)) < 1e-8, "compression completes at 90ms");
            Advance(.17);
            Check(Math.Abs(motion.Pose().Y - (drop ? 1.15 : 1.08)) < 1e-8 && Math.Abs(motion.Pose().X - (drop ? .88 : .94)) < 1e-8, "sustained bass still allows full stretch at 260ms");
            Advance(.15);
            Check(motion.Pose().Y > 1, "return continues after 410ms without early cutoff");
            Advance(.04);
            Check(motion.Pose().Y > 1, "no rest before next beat");
            Advance(60d / 130 - .45);
            Check(Math.Abs(motion.Pose().X - 1) < 1e-8 && Math.Abs(motion.Pose().Y - 1) < 1e-8, "cycle ends at neutral");
        }
        var poses = new List<(double X, double Y)>();
        foreach (var fps in new[] { 30, 60, 144 })
        {
            var motion = new DanceMotion();
            for (var i = 0; i < fps * 2; i++) motion.Step(1d / fps, true, true, 130, .2, .4);
            poses.Add((motion.Pose().X, motion.Pose().Y));
        }
        Check(poses.All(p => Math.Abs(p.X - poses[0].X) < 1e-8 && Math.Abs(p.Y - poses[0].Y) < 1e-8), "same pose across frame rates");
        var reference = new DanceMotion(); var changing = new DanceMotion();
        for (var i = 0; i < 40; i++)
        {
            reference.Step(.01, true, true, 130, 1, .4);
            changing.Step(.01, true, true, 130, i == 0 ? 1 : 0, i == 0 ? .4 : .9);
            Check(Math.Abs(reference.Pose().Y - changing.Pose().Y) < 1e-10, "bass and Drop changes cannot interrupt current beat");
        }
        foreach (var boundary in new[] { .09, .26, 60d / 130 })
        {
            (double X, double Y) At(double t)
            {
                var motion = new DanceMotion();
                while (t > 1e-12) { var dt = Math.Min(.01, t); motion.Step(dt, true, false, 130, 0, 0); t -= dt; }
                return (motion.Pose().X, motion.Pose().Y);
            }
            const double epsilon = .0001;
            var before = At(boundary - epsilon); var center = At(boundary); var after = At(boundary + epsilon);
                        var incoming = (center.Y - before.Y) / epsilon;
            var outgoing = (after.Y - center.Y) / epsilon;
            Check(Math.Abs(incoming - outgoing) < .04, "continuous speed at peak and cycle boundary");
            if (boundary > .4)
                Check(incoming < -.3 && outgoing < -.3, "neutral is a moving through-point");
            else
                Check(Math.Abs((after.Y - 2 * center.Y + before.Y) / (epsilon * epsilon)) > 1, "non-zero acceleration at extrema avoids hanging");
        }
    }
    private static (double Bass, double Energy) Analyze(double hz)
    {
        var spectrum = new DanceAudio.SpectrumProvider(new Tone(hz));
        var buffer = new float[4096]; spectrum.Read(buffer, 0, buffer.Length);
        return (spectrum.Bass, spectrum.Energy);
    }
    private sealed class Tone(double hz) : ISampleProvider
    {
        private int _position;
        public WaveFormat WaveFormat => WaveFormat.CreateIeeeFloatWaveFormat(44100, 1);
        public int Read(float[] buffer, int offset, int count)
        { for (var i = 0; i < count; i++) buffer[offset + i] = (float)(.5 * Math.Sin(2 * Math.PI * hz * _position++ / 44100)); return count; }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
