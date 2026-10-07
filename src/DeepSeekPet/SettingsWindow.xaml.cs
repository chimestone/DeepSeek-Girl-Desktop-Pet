using System.Windows;

namespace DeepSeekPet;

public partial class SettingsWindow : Window
{
    public PetSettings Result { get; }
    private readonly ChatConfiguration _chatConfiguration = ChatConfiguration.Load();

    public SettingsWindow(PetSettings settings)
    {
        InitializeComponent();
        Result = settings;
        ApiModel.Text = _chatConfiguration.Model;
        ApiStatus.Text = string.IsNullOrEmpty(_chatConfiguration.ProtectedKey) ? "尚未配置 Key" : "已保存 Key；可输入新 Key 替换";
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
        try
        {
            var key = RemoveApiKey.IsChecked == true ? "" : (!string.IsNullOrWhiteSpace(ApiKey.Password) ? ApiKey.Password : _chatConfiguration.ReadKey());
            _chatConfiguration.Save(key, ApiModel.Text);
        }
        catch
        {
            ApiStatus.Text = "保存失败：检查模型名称；Key 无法解密时请重新输入或勾选移除。";
            return;
        }
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
