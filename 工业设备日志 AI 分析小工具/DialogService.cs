using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace 工业设备日志_AI_分析小工具;

/// <summary>
/// 对话框严重级别，用于控制标题颜色与视觉风格。
/// </summary>
public enum DialogLevel
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>
/// 封装通用模态对话框。通过代码（非 XAML）构建窗口，避免引入额外视图依赖。
/// 主构造函数注入所属窗口，确保对话框居中于父窗口。
/// </summary>
/// <param name="owner">父窗口实例，用于设置 Owner 关系</param>
public sealed class DialogService(Window owner)
{
    private readonly Window _owner = owner;

    /// <summary>
    /// 弹出模态对话框，根据级别设置不同颜色的标题。
    /// </summary>
    /// <param name="title">对话框标题</param>
    /// <param name="message">正文内容</param>
    /// <param name="level">严重级别，控制标题颜色</param>
    public void Show(string title, string message, DialogLevel level)
    {
        // 根据级别确定标题颜色（深绿/橙/红/蓝）
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
