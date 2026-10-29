using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PulseDeck;
public partial class MainWindow
{
    void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this);
    }
    void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();
    void GoFans_Click(object sender, RoutedEventArgs e) => Navigation.SelectedIndex = 1;
    void GoOverclock_Click(object sender, RoutedEventArgs e) => Navigation.SelectedIndex = 2;
    void Scene_MouseMove(object sender, MouseEventArgs e) { if (Backdrop != null) Backdrop.SetPointer(e.GetPosition(RootScene)); if (sidebarField != null && e.GetPosition(Navigation).X < 196) sidebarField.SetPointer(e.GetPosition(sidebarField)); }
    void Scene_MouseDown(object sender, MouseButtonEventArgs e) { if (Backdrop != null) Backdrop.SetPointer(e.GetPosition(RootScene), true); if (sidebarField != null && e.GetPosition(Navigation).X < 196) sidebarField.SetPointer(e.GetPosition(sidebarField),true); }
    void Scene_KeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (Backdrop == null || e.NewFocus is not FrameworkElement element || element.ActualWidth <= 0) return;
        try { Backdrop.SetPointer(element.TranslatePoint(new(element.ActualWidth / 2, element.ActualHeight / 2), RootScene), true); if(sidebarField!=null && element.TranslatePoint(new(0,0),Navigation).X<196) sidebarField.SetPointer(element.TranslatePoint(new(element.ActualWidth/2,element.ActualHeight/2),sidebarField),true); } catch (InvalidOperationException) { }
    }
    void ConfigureKeyboard()
    {
        var navigate = new RoutedUICommand("Navegar", "Navigate", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(navigate, (_, e) => { CloseCommands(); Navigation.SelectedIndex = Convert.ToInt32(e.Parameter); e.Handled = true; }));
        for (int i = 0; i < Navigation.Items.Count; i++) InputBindings.Add(new KeyBinding(navigate, new KeyGesture((Key)((int)Key.D1 + i), ModifierKeys.Control)) { CommandParameter = i });
        var commands = new RoutedUICommand("Comandos", "Commands", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(commands, (_, _) => { if (CommandOverlay.Visibility == Visibility.Visible) CloseCommands(); else OpenCommands(); }));
        InputBindings.Add(new KeyBinding(commands, new KeyGesture(Key.K, ModifierKeys.Control)));
        var escape = new RoutedUICommand("Voltar", "Escape", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(escape, (_, _) => CloseCommands(), (_, e) => { e.CanExecute = CommandOverlay.Visibility == Visibility.Visible; e.Handled = true; }));
        InputBindings.Add(new KeyBinding(escape, new KeyGesture(Key.Escape)));
        var pause = new RoutedUICommand("Pausar", "Pause", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(pause, (_, e) => Pause_Click(this, e)));
        InputBindings.Add(new KeyBinding(pause, new KeyGesture(Key.Space, ModifierKeys.Control)));
        KeyboardNavigation.SetTabNavigation(CommandOverlay, KeyboardNavigationMode.Cycle);
        CommandSearch.PreviewKeyDown += (_, e) => {
            if (e.Key == Key.Down && CommandList.Items.Count > 0) { CommandList.SelectedIndex = Math.Max(0, CommandList.SelectedIndex); CommandList.Focus(); e.Handled = true; }
            else if (e.Key == Key.Enter) { ActivateCommand(); e.Handled = true; }
        };
    }
    void OpenCommands()
    {
        focusBeforeCommands = Keyboard.FocusedElement;
        CommandOverlay.Visibility = Visibility.Visible; CommandSearch.Text = "";
        CommandList.ItemsSource = navigationEntries; CommandList.SelectedIndex = 0; CommandSearch.Focus();
    }
    void CloseCommands()
    {
        if (CommandOverlay.Visibility != Visibility.Visible) return;
        CommandOverlay.Visibility = Visibility.Collapsed;
        if (focusBeforeCommands is UIElement element && element.IsVisible) Keyboard.Focus(element);
    }
    void OpenCommands_Click(object sender, RoutedEventArgs e) => OpenCommands();
    void CloseCommands_Click(object sender, RoutedEventArgs e) => CloseCommands();
    void CommandOverlay_MouseDown(object sender, MouseButtonEventArgs e) { if (ReferenceEquals(e.OriginalSource, CommandOverlay)) CloseCommands(); }
    void CommandSearch_Changed(object sender, TextChangedEventArgs e)
    {
        if (CommandList == null) return;
        CommandList.ItemsSource = navigationEntries.Where(x => x.Title.Contains(CommandSearch.Text.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (CommandList.Items.Count > 0) CommandList.SelectedIndex = 0;
    }
    void ActivateCommand()
    {
        if (CommandList.SelectedItem is not NavigationEntry entry) return;
        CloseCommands(); Navigation.SelectedIndex = entry.Tab;
    }
    void CommandList_Activate(object sender, MouseButtonEventArgs e) => ActivateCommand();
    void CommandList_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { ActivateCommand(); e.Handled = true; } }
    void ClockDraft_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || !gpu.ClockSupported) return;
        if (!gpu.ClockIsOffset) { StatusLabel.Text = "Atalhos de offset não se aplicam a esta interface de frequência absoluta."; return; }
        if (int.TryParse(button.Tag?.ToString(), out var draft) && gpu.ClockRange.Contains(draft))
        { ClockInput.Text = draft.ToString(); StatusLabel.Text = $"Rascunho de {draft:+0;-0;0} MHz. Nenhuma alteração foi enviada ao driver."; }
    }
    void FanProfile_Checked(object sender, RoutedEventArgs e)
    {
        if (fanReference == null || !gpu.FanSupported || sender is not RadioButton button || !Enum.TryParse<Services.FanProfile>(button.Tag?.ToString(), out var profile)) return;
        try
        {
            var curve = Services.FanProfiles.Build(profile, fanReference, gpu.FanSpeedRange, gpu.FanTemperatureRange);
            selectedFanProfile = profile; FanCurveGrid.ItemsSource = curve; FanCurvePreview.SetPoints(curve);
            GpuFanVisual.PreviewFactor = profile switch { Services.FanProfile.Medio=>1.55, Services.FanProfile.Avancado=>2.25, Services.FanProfile.ExtraRapido=>3.1, _=>1 };
            GpuFanVisual.InvalidateVisual();
            ApplyFanButton.IsEnabled = true;
            FanProfileSummary.Text = $"{Services.FanProfiles.Name(profile)} selecionado. Ainda não aplicado.";
        }
        catch (ArgumentException ex) { FanProfileSummary.Text = ex.Message; ApplyFanButton.IsEnabled = false; }
    }
}
