using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Zypora;

public partial class SettingsWindow : Window
{
    private readonly string _dir;
    private readonly AppSettings _working;

    public AppSettings Result { get; private set; }

    public SettingsWindow(AppSettings current, string dir)
    {
        InitializeComponent();
        _dir = dir;
        _working = new AppSettings
        {
            BackgroundImagePath = current.BackgroundImagePath,
            BackgroundOpacity = current.BackgroundOpacity,
        };
        Result = current;

        OpacitySlider.Value = _working.BackgroundOpacity * 100;
        UpdatePreview();
    }

    private void OnChoose(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.gif;*.bmp|所有文件 (*.*)|*.*",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            _working.BackgroundImagePath = SettingsStore.ImportBackground(dlg.FileName, _dir);
            UpdatePreview();
        }
        catch (Exception ex)
        {
            MessageBox.Show("导入图片失败:\n" + ex.Message, "Zypora", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        _working.BackgroundImagePath = null;
        UpdatePreview();
    }

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_working == null) return;
        _working.BackgroundOpacity = OpacitySlider.Value / 100.0;
        if (OpacityLabel != null) OpacityLabel.Text = $"{(int)OpacitySlider.Value}%";
        if (PreviewImage != null) PreviewImage.Opacity = _working.BackgroundOpacity;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Result = _working;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void UpdatePreview()
    {
        ImageLabel.Text = _working.BackgroundImagePath == null
            ? "(未设置)"
            : Path.GetFileName(_working.BackgroundImagePath);

        if (!string.IsNullOrEmpty(_working.BackgroundImagePath) && File.Exists(_working.BackgroundImagePath))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(_working.BackgroundImagePath);
                bmp.EndInit();
                PreviewImage.Source = bmp;
            }
            catch
            {
                PreviewImage.Source = null;
            }
        }
        else
        {
            PreviewImage.Source = null;
        }

        PreviewImage.Opacity = _working.BackgroundOpacity;
        PreviewHint.Visibility = PreviewImage.Source == null ? Visibility.Visible : Visibility.Collapsed;
        OpacityLabel.Text = $"{(int)OpacitySlider.Value}%";
    }
}
