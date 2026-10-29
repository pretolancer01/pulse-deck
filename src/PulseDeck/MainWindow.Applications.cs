using System;
using System.Linq;
using System.Windows;
using PulseDeck.Services;

namespace PulseDeck;
public partial class MainWindow
{
    void UpdateApplications()
    {
        if(AppsChart==null) return;
        var apps=ApplicationUsage.Aggregate(processes);
        AppsChart.SetData(apps);
        var ordered=(AppsGpuMetric.IsChecked==true?apps.OrderByDescending(x=>x.GpuPercent):AppsCpuMetric.IsChecked==true?apps.OrderByDescending(x=>x.CpuPercent):apps.OrderByDescending(x=>x.RamBytes)).ToList();
        string? selected=(AppsGrid.SelectedItem as ApplicationUsage)?.Name;
        AppsGrid.ItemsSource=ordered;
        if(selected!=null) AppsGrid.SelectedItem=ordered.FirstOrDefault(x=>x.Name==selected);
        var leader=AppsChart.Displayed.FirstOrDefault();
        string kind=AppsChart.Metric==2?"GPU":AppsChart.Metric==1?"CPU":"RAM";
        AppsLeader.Text=leader==null?$"Sem leitura de {kind} por processo":$"Maior consumo de {kind}: {leader.Name}  ·  {(AppsChart.Metric==2?leader.Gpu:AppsChart.Metric==1?leader.Cpu:leader.Ram)}";
        AppsReadAt.Text=$"{apps.Count} executáveis com leitura disponível  /  {DateTime.Now:HH:mm:ss}  /  Top 6 no gráfico";
    }
    void AppsMetric_Changed(object sender,RoutedEventArgs e)
    {
        if(AppsChart==null) return;
        AppsChart.SetMetric(AppsGpuMetric.IsChecked==true?2:AppsCpuMetric.IsChecked==true?1:0); UpdateApplications();
    }
    void IntroPreference_Changed(object sender,RoutedEventArgs e)
    {
        if(!IsLoaded) return;
        AppPreferences.Current.IntroEnabled=IntroToggle.IsChecked==true; AppPreferences.Current.Save();
    }
    async void ReplayIntro_Click(object sender,RoutedEventArgs e)
    {
        if(!MotionAllowed) { StatusLabel.Text="Ative os efeitos de movimento para rever a introdução."; return; }
        var intro=new IntroWindow { Owner=this }; intro.Show(); await intro.Completion;
    }
}
