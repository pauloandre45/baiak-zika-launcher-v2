using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json;

namespace BaiakZikaLauncher
{
    public partial class SimpleMainWindow : Window
    {
        // CONFIGURAÇÃO - Altere estas URLs
        private const string CONFIG_URL = "https://gist.githubusercontent.com/pauloandre45/e59926d5c0c8cbc9d225e06db7e446ad/raw/SERVIDOR_launcher_config.json";
        private const string CLIENT_EXECUTABLE = "client.exe";
        
        private string clientDownloadUrl = "";
        private string remoteVersion = "";
        private string localVersion = "0.0.0";
        private WebClient webClient;

        public SimpleMainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadLocalVersion();
            await CheckForUpdates();
        }

        private void LoadLocalVersion()
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launcher_config.json");
                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    dynamic config = JsonConvert.DeserializeObject(json);
                    localVersion = config?.clientVersion ?? "0.0.0";
                }
            }
            catch { }
            
            VersionText.Text = $"Versão: {localVersion}";
        }

        private void SaveLocalVersion(string version)
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launcher_config.json");
                File.WriteAllText(configPath, JsonConvert.SerializeObject(new { clientVersion = version }));
                localVersion = version;
                VersionText.Text = $"Versão: {localVersion}";
            }
            catch { }
        }

        private async Task CheckForUpdates()
        {
            StatusText.Text = "Verificando atualizações...";
            
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    string json = await client.GetStringAsync(CONFIG_URL);
                    dynamic config = JsonConvert.DeserializeObject(json);
                    
                    remoteVersion = config?.clientVersion ?? "0.0.0";
                    clientDownloadUrl = config?.newClientUrl ?? "";
                    
                    if (CompareVersions(remoteVersion, localVersion) > 0)
                    {
                        StatusText.Text = $"Nova versão disponível: {remoteVersion}";
                        UpdateButton.Visibility = Visibility.Visible;
                        PlayButton.IsEnabled = false;
                    }
                    else
                    {
                        StatusText.Text = "Cliente atualizado!";
                        StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(76, 175, 80));
                        PlayButton.IsEnabled = true;
                        UpdateButton.Visibility = Visibility.Collapsed;
                    }
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "Erro ao verificar: " + ex.Message;
                PlayButton.IsEnabled = true; // Permite jogar mesmo offline
            }
        }

        private int CompareVersions(string v1, string v2)
        {
            try
            {
                var version1 = new Version(v1);
                var version2 = new Version(v2);
                return version1.CompareTo(version2);
            }
            catch
            {
                return string.Compare(v1, v2, StringComparison.Ordinal);
            }
        }

        private void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(clientDownloadUrl))
            {
                MessageBox.Show("URL de download não configurada!", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            
            StartDownload();
        }

        private void StartDownload()
        {
            UpdateButton.IsEnabled = false;
            ProgressBar.Visibility = Visibility.Visible;
            ProgressBar.Value = 0;
            StatusText.Text = "Baixando atualização...";

            webClient = new WebClient();
            webClient.DownloadProgressChanged += WebClient_DownloadProgressChanged;
            webClient.DownloadFileCompleted += WebClient_DownloadFileCompleted;

            string zipPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update.zip");
            
            try
            {
                webClient.DownloadFileAsync(new Uri(clientDownloadUrl), zipPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erro ao iniciar download: " + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateButton.IsEnabled = true;
                ProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private void WebClient_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressBar.Value = e.ProgressPercentage;
                double mbReceived = e.BytesReceived / 1024.0 / 1024.0;
                double mbTotal = e.TotalBytesToReceive / 1024.0 / 1024.0;
                StatusText.Text = $"Baixando... {mbReceived:F1} MB / {mbTotal:F1} MB ({e.ProgressPercentage}%)";
            });
        }

        private async void WebClient_DownloadFileCompleted(object sender, AsyncCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                Dispatcher.Invoke(() =>
                {
                    StatusText.Text = "Erro no download: " + e.Error.Message;
                    UpdateButton.IsEnabled = true;
                    ProgressBar.Visibility = Visibility.Collapsed;
                });
                return;
            }

            if (e.Cancelled)
            {
                Dispatcher.Invoke(() =>
                {
                    StatusText.Text = "Download cancelado";
                    UpdateButton.IsEnabled = true;
                    ProgressBar.Visibility = Visibility.Collapsed;
                });
                return;
            }

            await ExtractUpdate();
        }

        private async Task ExtractUpdate()
        {
            Dispatcher.Invoke(() => StatusText.Text = "Extraindo arquivos...");

            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            string zipPath = Path.Combine(basePath, "update.zip");

            try
            {
                await Task.Run(() =>
                {
                    // Extrai o ZIP
                    using (ZipArchive archive = ZipFile.OpenRead(zipPath))
                    {
                        foreach (ZipArchiveEntry entry in archive.Entries)
                        {
                            string destinationPath = Path.Combine(basePath, entry.FullName);
                            
                            // Cria diretório se necessário
                            string dirPath = Path.GetDirectoryName(destinationPath);
                            if (!string.IsNullOrEmpty(dirPath) && !Directory.Exists(dirPath))
                            {
                                Directory.CreateDirectory(dirPath);
                            }
                            
                            // Extrai arquivo (pula diretórios)
                            if (!string.IsNullOrEmpty(entry.Name))
                            {
                                entry.ExtractToFile(destinationPath, true);
                            }
                        }
                    }
                    
                    // Remove o ZIP
                    File.Delete(zipPath);
                });

                // Salva nova versão
                SaveLocalVersion(remoteVersion);

                Dispatcher.Invoke(() =>
                {
                    StatusText.Text = "Atualização concluída!";
                    StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(76, 175, 80));
                    ProgressBar.Visibility = Visibility.Collapsed;
                    PlayButton.IsEnabled = true;
                    UpdateButton.Visibility = Visibility.Collapsed;
                });

                MessageBox.Show("Cliente atualizado com sucesso!", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    StatusText.Text = "Erro na extração: " + ex.Message;
                    UpdateButton.IsEnabled = true;
                    ProgressBar.Visibility = Visibility.Collapsed;
                });
                
                MessageBox.Show("Erro ao extrair: " + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            string clientPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CLIENT_EXECUTABLE);
            
            if (File.Exists(clientPath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = clientPath,
                        WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                    });
                    Application.Current.Shutdown();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Erro ao iniciar: " + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                var result = MessageBox.Show(
                    $"Cliente não encontrado ({CLIENT_EXECUTABLE}).\n\nDeseja baixar agora?",
                    "Cliente não encontrado",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    UpdateButton.Visibility = Visibility.Visible;
                    UpdateButton_Click(sender, e);
                }
            }
        }
    }
}
