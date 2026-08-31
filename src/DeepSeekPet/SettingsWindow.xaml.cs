using System.Windows;

namespace DeepSeekPet;

public partial class SettingsWindow : Window
{
    public PetSettings Result { get; }

    public SettingsWindow(PetSettings settings)
    {
        InitializeComponent();
        Result = settings;
        SkinList.ItemsSource = SkinCatalog.All;
        SkinList.SelectedValue = SkinCatalog.Find(settings.SkinId).Id;
        AutoWalkCheck.IsChecked = settings.AutoWalk;
        SpeedSlider.Value = settings.WalkSpeed;
        SizeSlider.Value = settings.PetSize;
        TopmostCheck.IsChecked = settings.AlwaysOnTop;
        EdgeSnapCheck.IsChecked = settings.EdgeSnap;
        StartupCheck.IsChecked = settings.StartWithWindows;
        UpdateValueLabels();
    }

    private void SliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateValueLabels();

    private void UpdateValueLabels()
    {
        if (SpeedValue is null || SizeValue is null) return;
        SpeedValue.Text = $"{SpeedSlider.Value:0} 像素/秒";
        SizeValue.Text = $"{SizeSlider.Value:0} 像素";
    }

    private void SaveClicked(object sender, RoutedEventArgs e)
    {
        Result.SkinId = SkinList.SelectedValue as string ?? SkinCatalog.DefaultId;
        Result.AutoWalk = AutoWalkCheck.IsChecked == true;
        Result.WalkSpeed = SpeedSlider.Value;
        Result.PetSize = SizeSlider.Value;
        Result.AlwaysOnTop = TopmostCheck.IsChecked == true;
        Result.EdgeSnap = EdgeSnapCheck.IsChecked == true;
        Result.StartWithWindows = StartupCheck.IsChecked == true;
        AutoStartManager.SetEnabled(Result.StartWithWindows);
        DialogResult = true;
    }
}
