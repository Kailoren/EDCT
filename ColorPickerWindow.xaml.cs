using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ColonyTracker;

/// <summary>A small themed color picker (hex box + R/G/B sliders + live preview), used instead of
/// the native System.Windows.Forms.ColorDialog, which doesn't match this app's dark theme at all.</summary>
public partial class ColorPickerWindow : Window
{
    private bool _syncing;

    public Color? Result { get; private set; }

    public ColorPickerWindow(Color initial)
    {
        InitializeComponent();
        HexBox.Text = $"#{initial.R:X2}{initial.G:X2}{initial.B:X2}";
    }

    private void ChannelSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (RedLabel is null || _syncing) return; // RedLabel null: fires during InitializeComponent before other fields exist

        var color = Color.FromRgb((byte)RedSlider.Value, (byte)GreenSlider.Value, (byte)BlueSlider.Value);
        RedLabel.Text = ((byte)RedSlider.Value).ToString();
        GreenLabel.Text = ((byte)GreenSlider.Value).ToString();
        BlueLabel.Text = ((byte)BlueSlider.Value).ToString();
        PreviewSwatch.Background = new SolidColorBrush(color);

        _syncing = true;
        HexBox.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        _syncing = false;
    }

    private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        Color color;
        try
        {
            color = (Color)ColorConverter.ConvertFromString(HexBox.Text);
        }
        catch
        {
            return; // not a valid color yet (user still typing) - leave sliders/preview as they were
        }

        _syncing = true;
        RedSlider.Value = color.R;
        GreenSlider.Value = color.G;
        BlueSlider.Value = color.B;
        RedLabel.Text = color.R.ToString();
        GreenLabel.Text = color.G.ToString();
        BlueLabel.Text = color.B.ToString();
        PreviewSwatch.Background = new SolidColorBrush(color);
        _syncing = false;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Result = Color.FromRgb((byte)RedSlider.Value, (byte)GreenSlider.Value, (byte)BlueSlider.Value);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
