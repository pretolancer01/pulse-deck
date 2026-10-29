using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PulseDeck;
internal sealed class ConfirmationDialog : Window
{
    ConfirmationDialog(Window owner, string message, string title)
    {
        Owner = owner; Title = title; Width = 560; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)FindResource("Surface"); Foreground = (Brush)FindResource("Ink"); FontFamily = (FontFamily)FindResource("BodyFont"); FontSize = 14;
        var stack = new StackPanel { Margin = new Thickness(28) };
        stack.Children.Add(new TextBlock { Text = title, FontFamily = (FontFamily)FindResource("DisplayFont"), FontSize = 24, Margin = new Thickness(0, 0, 0, 20) });
        stack.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Muted"), MaxHeight = 440 });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
        var cancel = new Button { Content = "Voltar", IsDefault = true, IsCancel = true, MinWidth = 100, Margin = new Thickness(0, 0, 10, 0) };
        var apply = new Button { Content = "Confirmar", Style = (Style)FindResource("Primary"), MinWidth = 110 };
        cancel.Click += (_, _) => DialogResult = false; apply.Click += (_, _) => DialogResult = true;
        actions.Children.Add(cancel); actions.Children.Add(apply); stack.Children.Add(actions);
        Content = new Border { Child = stack, BorderBrush = (Brush)FindResource("Border"), BorderThickness = new Thickness(1) };
        Loaded += (_, _) => cancel.Focus();
    }
    public static bool Ask(Window owner, string message, string title) => new ConfirmationDialog(owner, message, title).ShowDialog() == true;
}
