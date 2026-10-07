using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeepSeekPet;

static class UiChecks
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { Verify(); } catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw new Exception("UI checks failed", failure);
    }
    private static void Verify()
    {

        var pet = new MainWindow();
        var bubble = new AssistantWindow();
        Set(pet, "_assistantWindow", bubble);
        bubble.LayoutRequested += (_, _) => Invoke(pet, "PositionAssistant");
        foreach (var size in new[] { 150d, 250d, 420d })
        {
            pet.Width = size;
            pet.Height = size * 1.04;
            pet.Left = -2000;
            pet.Top = 100;
            bubble.ApplyScale(size);
            foreach (var expanded in new[] { false, true })
            {
                bubble.SetExpanded(expanded);
                Invoke(pet, "PositionAssistant");
                Check(bubble.Left >= pet.Left - .01 && bubble.Top >= pet.Top - .01 && bubble.Left + bubble.Width <= pet.Left + pet.Width + .01 && bubble.Top + bubble.Height <= pet.Top + pet.Height + .01, $"bubble containment size={size} expanded={expanded} pet={pet.Left},{pet.Top},{pet.Width},{pet.Height} bubble={bubble.Left},{bubble.Top},{bubble.Width},{bubble.Height}");
                Check(Math.Abs(bubble.Height - pet.Height * (expanded ? 1 : .5)) < .01, "bubble height");
                var bubbleRoot = (FrameworkElement)bubble.Content;
                bubbleRoot.Measure(new Size(bubble.Width, bubble.Height));
                bubbleRoot.Arrange(new Rect(0, 0, bubble.Width, bubble.Height));
                bubbleRoot.UpdateLayout();
                var input = (TextBox)bubble.FindName("ChatInput");
                Check(input.ActualWidth > 40 && input.ActualHeight >= 25, $"input usable {size} {expanded}: {input.ActualWidth}x{input.ActualHeight}");
                if (size == 250)
                {
                    bubble.PrepareManualOpen();
                    Snapshot(pet, bubble, expanded);
                }
            }
        }
        bubble.ApplyScale(250);
        bubble.Left = -2000;
        bubble.Show();
        Set(pet, "_isWalking", true);
        Invoke(pet, "UpdateBehavior", 0d, .05d);
        Check(!(bool)Get(pet, "_isWalking")!, "walking paused");
        var deadline = DateTimeOffset.UtcNow.AddMinutes(1);
        Set(bubble, "_deadline", deadline);
        bubble.Hide();
        Check((DateTimeOffset?)Get(bubble, "_deadline") == deadline, "hide keeps deadline");
        var notified = false;
        bubble.AttentionRequested += (_, _) => { notified = true; bubble.Show(); };
        bubble.SetExpanded(true);
        Set(bubble, "_deadline", DateTimeOffset.UtcNow.AddSeconds(-1));
        Invoke(bubble, "Timer_Tick", null, EventArgs.Empty);
        Check(notified && bubble.IsVisible && !bubble.IsExpanded && Get(bubble, "_deadline") == null, "timer wakes compact bubble");
        var settings = new SettingsWindow(new PetSettings());
        Check(settings.FindName("ApiModel") != null && settings.FindName("ApiKey") != null, "settings API controls");
        settings.Close();
        bubble.CloseForAppExit();
        pet.Close();
        Console.WriteLine("PASS: compact/expanded bounds at 150/250/420, input layout, walking pause, hidden timer, compact reminder, settings.");
    }
    private static void Snapshot(MainWindow pet, AssistantWindow bubble, bool expanded)
    {
        var root = (FrameworkElement)pet.Content;
        root.Measure(new Size(pet.Width, pet.Height));
        root.Arrange(new Rect(0, 0, pet.Width, pet.Height));
        root.UpdateLayout();
        var petBitmap = new RenderTargetBitmap(250, 260, 96, 96, PixelFormats.Pbgra32);
        petBitmap.Render(root);
        var bubbleBitmap = new RenderTargetBitmap(250, (int)Math.Ceiling(bubble.Height), 96, 96, PixelFormats.Pbgra32);
        bubbleBitmap.Render((Visual)bubble.Content);
        var drawing = new DrawingVisual();
        using (var canvas = drawing.RenderOpen())
        {
            canvas.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(0, 0, 250, 260));
            canvas.DrawImage(petBitmap, new Rect(0, 0, 250, 260));
            canvas.DrawImage(bubbleBitmap, new Rect(0, 260 - bubble.Height, 250, bubble.Height));
        }
        var result = new RenderTargetBitmap(250, 260, 96, 96, PixelFormats.Pbgra32);
        result.Render(drawing);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(result));
        var output = Path.GetFullPath(Path.Combine(".work", "ui-checks"));
        Directory.CreateDirectory(output);
        using var stream = File.Create(Path.Combine(output, expanded ? "expanded.png" : "compact.png"));
        encoder.Save(stream);
    }
    private static object? Get(object target, string name) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target);
    private static void Set(object target, string name, object? value) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static void Invoke(object target, string name, params object?[] args) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
