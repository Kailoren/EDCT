using System.Windows;
using System.Windows.Controls;

namespace ColonyTracker;

/// <summary>The custom title-bar chrome (icon, title text, close button) shared by
/// NewProjectDialog and SettingsWindow - previously copy-pasted between the two identically
/// except for the title text.</summary>
public partial class DialogTitleBar : UserControl
{
    public static readonly DependencyProperty TitleTextProperty =
        DependencyProperty.Register(nameof(TitleText), typeof(string), typeof(DialogTitleBar));

    public string TitleText
    {
        get => (string)GetValue(TitleTextProperty);
        set => SetValue(TitleTextProperty, value);
    }

    public event RoutedEventHandler? CloseClicked;

    public DialogTitleBar() => InitializeComponent();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseClicked?.Invoke(this, e);
}
