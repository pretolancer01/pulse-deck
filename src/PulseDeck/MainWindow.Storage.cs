using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using PulseDeck.Services;

namespace PulseDeck;

public partial class MainWindow
{
    void InitializeStorageDrives()
    {
        try
        {
            StorageDrivePicker.ItemsSource = StorageService.GetDrives();
            if (StorageDrivePicker.Items.Count > 0) StorageDrivePicker.SelectedIndex = 0;
            else StorageStatusText.Text = "Nenhuma unidade local pronta foi encontrada.";
        }
        catch (Exception ex) { StorageStatusText.Text = "Não foi possível listar as unidades: " + ex.Message; }
    }

    void StorageDrive_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (StorageVolumeText == null) return;
        if (StorageDrivePicker.SelectedItem is StorageDrive drive)
            StorageVolumeText.Text = $"{drive.Name}  •  {FormatStorageBytes(drive.FreeBytes)} livres de {FormatStorageBytes(drive.TotalBytes)}";
    }

    async void StorageScan_Click(object sender, RoutedEventArgs e)
    {
        if (StorageDrivePicker.SelectedItem is not StorageDrive drive) return;
        storageCancellation?.Cancel(); storageCancellation?.Dispose();
        storageCancellation = new CancellationTokenSource();
        var token = storageCancellation.Token;
        StorageScanButton.IsEnabled = false; StorageCancelButton.IsEnabled = true;
        StorageChart.ItemsSource = null; storageResult = null; StorageOpenButton.IsEnabled = false;
        StorageEmptyText.Text = "Analisando arquivos e aplicativos desta unidade…";
        StorageStatusText.Text = "Preparando a análise…";
        try
        {
            var progress = new Progress<StorageProgress>(value =>
            {
                if (!token.IsCancellationRequested)
                    StorageStatusText.Text = $"{value.FilesVisited:N0} arquivos verificados em {value.FoldersVisited:N0} pastas…";
            });
            storageResult = await StorageService.ScanAsync(drive, progress, token);
            UpdateStorageChart();
            StorageStatusText.Text = $"Análise concluída: {storageResult.Files.Count} maiores arquivos e {storageResult.Applications.Count} aplicativos com tamanho informado.";
        }
        catch (OperationCanceledException) { StorageStatusText.Text = "Análise cancelada."; StorageEmptyText.Text = "Clique em Analisar armazenamento para iniciar uma nova busca."; }
        catch (Exception ex) { StorageStatusText.Text = "Falha na análise: " + ex.Message; StorageEmptyText.Text = "Não foi possível concluir a análise."; }
        finally
        {
            StorageScanButton.IsEnabled = true; StorageCancelButton.IsEnabled = false;
            storageCancellation?.Dispose(); storageCancellation = null;
        }
    }

    void StorageCancel_Click(object sender, RoutedEventArgs e) => storageCancellation?.Cancel();
    void StorageMode_Changed(object sender, SelectionChangedEventArgs e) => UpdateStorageChart();

    void UpdateStorageChart()
    {
        if (StorageChart == null) return;
        var entries = StorageModePicker.SelectedIndex == 1 ? storageResult?.Applications : storageResult?.Files;
        if (entries == null || entries.Count == 0)
        {
            StorageChart.ItemsSource = null; StorageOpenButton.IsEnabled = false;
            if (StorageEmptyText != null) StorageEmptyText.Text = storageResult == null
                ? "Clique em Analisar armazenamento para listar os maiores itens."
                : StorageModePicker.SelectedIndex == 1
                    ? "O Windows não informou tamanhos para os aplicativos instalados nesta máquina."
                    : "Nenhum arquivo legível foi encontrado nesta unidade.";
            return;
        }
        long largest = entries.Max(item => item.Bytes);
        StorageChart.ItemsSource = entries.Select(item => new StorageChartItem(
            item, item.Name, FormatStorageBytes(item.Bytes), item.Path,
            largest > 0 ? Math.Max(1, item.Bytes * 100.0 / largest) : 0,
            File.Exists(item.Path) || Directory.Exists(item.Path))).ToArray();
        StorageEmptyText.Text = "Barras proporcionais ao maior item da lista. Selecione um item para abrir sua localização.";
    }

    void StorageItem_Changed(object sender, SelectionChangedEventArgs e) =>
        StorageOpenButton.IsEnabled = StorageChart.SelectedItem is StorageChartItem { CanOpen: true };

    void StorageOpen_Click(object sender, RoutedEventArgs e)
    {
        if (StorageChart.SelectedItem is not StorageChartItem item) return;
        try
        {
            var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            if (File.Exists(item.Entry.Path)) start.Arguments = "/select,\"" + item.Entry.Path + "\"";
            else if (Directory.Exists(item.Entry.Path)) start.Arguments = "\"" + item.Entry.Path + "\"";
            else { StorageStatusText.Text = "O local desse aplicativo não foi informado pelo Windows."; return; }
            Process.Start(start);
        }
        catch (Exception ex) { StorageStatusText.Text = "Não foi possível abrir o local: " + ex.Message; }
    }

    static string FormatStorageBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes); int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:N1} {units[unit]}";
    }
}
