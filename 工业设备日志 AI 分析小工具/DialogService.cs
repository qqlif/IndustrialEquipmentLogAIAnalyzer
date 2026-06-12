using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace 工业设备日志_AI_分析小工具;

public enum DialogLevel
{
    Info,
    Success,
    Warning,
    Error
}

public sealed class DialogService(Window owner)
{
    private readonly Window _owner = owner;

    public void Show(string title, string message, DialogLevel level)
    {
        var levelColor = level switch
        {
            DialogLevel.Success => "#1B5E20",
            DialogLevel.Warning => "#E65100",
            DialogLevel.Error => "#B71C1C",
            _ => "#0D47A1"
        };

        var dialog = new Window
        {
            Title = title,
            Owner = _owner,
            Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 420,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EEF2F7"))
        };

        var root = new Border
        {
            Margin = new Thickness(14),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D5DFEB")),
            BorderThickness = new Thickness(1)
        };

        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(levelColor))
        };
        Grid.SetRow(titleText, 0);

        var contentText = new TextBlock
        {
            Margin = new Thickness(0, 10, 0, 14),
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2F4A67"))
        };
        Grid.SetRow(contentText, 1);

        var okButton = new Button
        {
            Content = "确定",
            Width = 90,
            Height = 34,
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0052CC")),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 4, 10, 4)
        };
        okButton.Click += (_, _) => dialog.Close();
        Grid.SetRow(okButton, 2);

        panel.Children.Add(titleText);
        panel.Children.Add(contentText);
        panel.Children.Add(okButton);
        root.Child = panel;
        dialog.Content = root;
        dialog.ShowDialog();
    }
}
