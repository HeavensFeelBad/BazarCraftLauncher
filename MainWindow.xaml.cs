using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BCLauncher
{
    public partial class MainWindow : Window
    {
        private LauncherConfig? _config;
        private readonly string _baseDir = AppContext.BaseDirectory;
        private readonly HttpClient _http = new();

        public MainWindow()
        {
            InitializeComponent();
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("BCLauncher/1.0");
            LoadConfig();
            NavigateTo(HomeView, HomeNavButton, showActionBar: true);
            _ = LoadMinecraftBuildsFromGitHub();
        }

        private void LoadConfig()
        {
            string configPath = Path.Combine(_baseDir, "launcher-config.json");

            if (!File.Exists(configPath))
            {
                MessageBox.Show("Не найден launcher-config.json рядом с лаунчером.");
                return;
            }

            string json = File.ReadAllText(configPath);

            _config = JsonSerializer.Deserialize<LauncherConfig>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }
            );

            BuildsComboBox.ItemsSource = _config?.Builds;
            BuildsComboBox.DisplayMemberPath = "Name";
            BuildsListControl.ItemsSource = _config?.Builds;
            LauncherFolderText.Text = $"Рабочая папка лаунчера: {_baseDir}";

            if (BuildsComboBox.Items.Count > 0)
                BuildsComboBox.SelectedIndex = 0;

            UpdateSelectedBuildDetails();
        }

        private async Task LoadMinecraftBuildsFromGitHub()
        {
            if (_config is null)
                return;

            try
            {
                SetStatus("Загрузка списка сборок", $"Получаем сборки из GitHub Releases, тег {_config.MinecraftReleaseTag}.");

                string apiUrl =
                    $"https://api.github.com/repos/{_config.GitHubOwner}/{_config.GitHubRepo}/releases/tags/{_config.MinecraftReleaseTag}";

                using HttpResponseMessage response = await _http.GetAsync(apiUrl);

                if (!response.IsSuccessStatusCode)
                {
                    SetStatus("Готов к установке", "Не удалось получить список сборок из GitHub. Используется локальный launcher-config.json.");
                    return;
                }

                string json = await response.Content.ReadAsStringAsync();
                List<BuildInfo> githubBuilds = ParseGithubBuilds(json);

                if (githubBuilds.Count == 0)
                {
                    SetStatus("Готов к установке", $"В релизе {_config.MinecraftReleaseTag} нет zip-архивов сборок.");
                    return;
                }

                _config.Builds = githubBuilds;
                RefreshBuildsUi();
                SetStatus("Готов к установке", $"Найдено сборок в GitHub Releases: {githubBuilds.Count}.");
            }
            catch (Exception ex)
            {
                SetStatus("Готов к установке", $"Не удалось получить сборки из GitHub: {ex.Message}");
            }
        }

        private List<BuildInfo> ParseGithubBuilds(string releaseJson)
        {
            List<BuildInfo> builds = new();

            using JsonDocument document = JsonDocument.Parse(releaseJson);
            JsonElement root = document.RootElement;
            string releaseTag = root.TryGetProperty("tag_name", out JsonElement tagElement)
                ? tagElement.GetString() ?? _config?.MinecraftReleaseTag ?? ""
                : _config?.MinecraftReleaseTag ?? "";

            if (!root.TryGetProperty("assets", out JsonElement assets) || assets.ValueKind != JsonValueKind.Array)
                return builds;

            foreach (JsonElement asset in assets.EnumerateArray())
            {
                string assetName = asset.TryGetProperty("name", out JsonElement nameElement)
                    ? nameElement.GetString() ?? ""
                    : "";

                string downloadUrl = asset.TryGetProperty("browser_download_url", out JsonElement urlElement)
                    ? urlElement.GetString() ?? ""
                    : "";

                if (string.IsNullOrWhiteSpace(assetName) || string.IsNullOrWhiteSpace(downloadUrl))
                    continue;

                if (!assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    continue;

                string buildName = Path.GetFileNameWithoutExtension(assetName);

                builds.Add(new BuildInfo
                {
                    Name = buildName,
                    VersionId = buildName,
                    Version = releaseTag,
                    MinecraftVersion = _config?.DefaultMinecraftVersion ?? "1.20.1",
                    DownloadUrl = downloadUrl,
                    MainClass = "net.minecraft.client.main.Main",
                    JavaPath = "C:\\Program Files\\Java\\jdk-17\\bin\\java.exe",
                    MemoryMb = 6144
                });
            }

            return builds;
        }

        private void RefreshBuildsUi()
        {
            BuildsComboBox.ItemsSource = null;
            BuildsComboBox.ItemsSource = _config?.Builds;
            BuildsComboBox.DisplayMemberPath = "Name";

            BuildsListControl.ItemsSource = null;
            BuildsListControl.ItemsSource = _config?.Builds;

            if (BuildsComboBox.Items.Count > 0)
                BuildsComboBox.SelectedIndex = 0;

            UpdateSelectedBuildDetails();
        }

        private void SetBusy(bool isBusy)
        {
            InstallUpdateButton.IsEnabled = !isBusy;
            PlayButton.IsEnabled = !isBusy;
            BuildsComboBox.IsEnabled = !isBusy;
            DownloadProgress.IsIndeterminate = isBusy;
            SidebarStatusText.Text = isBusy ? "Идет установка" : "Готов к игре";
        }

        private void SetStatus(string title, string details)
        {
            StatusTitleText.Text = title;
            StatusText.Text = details;
        }

        private void UpdateSelectedBuildDetails()
        {
            if (BuildsComboBox.SelectedItem is not BuildInfo build)
            {
                SelectedBuildNameText.Text = "Сборка не выбрана";
                SelectedBuildVersionText.Text = "Добавь сборку в launcher-config.json";
                return;
            }

            SelectedBuildNameText.Text = build.Name;

            string loader = string.IsNullOrWhiteSpace(build.InheritsFrom)
                ? "Forge / TLauncher"
                : build.InheritsFrom;

            SelectedBuildVersionText.Text = $"Версия {build.MinecraftVersion} • {loader}";
        }

        private void BuildsComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdateSelectedBuildDetails();
        }

        private void HomeNavButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateTo(HomeView, HomeNavButton, showActionBar: true);
        }

        private void BuildsNavButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateTo(BuildsView, BuildsNavButton, showActionBar: true);
        }

        private void NewsNavButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateTo(NewsView, NewsNavButton, showActionBar: false);
        }

        private void SettingsNavButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateTo(SettingsView, SettingsNavButton, showActionBar: false);
        }

        private void ProfileNavButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateTo(ProfileView, ProfileNavButton, showActionBar: false);
        }

        private void AboutNavButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateTo(AboutView, AboutNavButton, showActionBar: false);
        }

        private void NavigateTo(FrameworkElement selectedView, Button activeButton, bool showActionBar)
        {
            FrameworkElement[] views =
            {
                HomeView,
                BuildsView,
                NewsView,
                SettingsView,
                ProfileView,
                AboutView
            };

            foreach (FrameworkElement view in views)
                view.Visibility = view == selectedView ? Visibility.Visible : Visibility.Collapsed;

            Button[] buttons =
            {
                HomeNavButton,
                BuildsNavButton,
                NewsNavButton,
                SettingsNavButton,
                ProfileNavButton,
                AboutNavButton
            };

            foreach (Button button in buttons)
            {
                button.Background = Brushes.Transparent;
                button.Foreground = new SolidColorBrush(Color.FromRgb(184, 194, 210));
            }

            activeButton.Background = new SolidColorBrush(Color.FromRgb(21, 53, 47));
            activeButton.Foreground = Brushes.White;
            ActionBar.Visibility = showActionBar ? Visibility.Visible : Visibility.Collapsed;
        }

        private void DownloadProgress_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ProgressPercentText.Text = $"{Math.Round(DownloadProgress.Value):0}%";
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleWindowState();
                return;
            }

            DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleWindowState();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ToggleWindowState()
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (BuildsComboBox.SelectedItem is not BuildInfo build)
                return;

            try
            {
                string versionId = GetVersionId(build);
                SetBusy(true);
                SetStatus("Подготовка", $"Готовим папки для сборки {build.Name}.");
                DownloadProgress.Value = 0;
                SpeedText.Text = "Скорость: ожидание";
                TimeLeftText.Text = "Осталось: расчет";
                FilesText.Text = "Файлов: подготовка";

                string downloadsDir = Path.Combine(_baseDir, "downloads");
                string instancesDir = Path.Combine(_baseDir, "instances");

                Directory.CreateDirectory(downloadsDir);
                Directory.CreateDirectory(instancesDir);

                string zipPath = Path.Combine(downloadsDir, $"{versionId}.zip");
                string instanceDir = Path.Combine(instancesDir, versionId);

                SetStatus("Скачивание", "Загружаем архив сборки. Прогресс появится, если сервер передает размер файла.");
                await DownloadFile(build.DownloadUrl, zipPath);

                SetStatus("Распаковка", "Распаковываем архив во временную папку лаунчера.");
                TimeLeftText.Text = "Осталось: распаковка";
                DownloadProgress.IsIndeterminate = true;

                if (Directory.Exists(instanceDir))
                    Directory.Delete(instanceDir, true);

                Directory.CreateDirectory(instanceDir);

                await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, instanceDir));

                SetStatus("Установка", "Копируем сборку в папку versions и создаем файл версии для TLauncher.");
                InstallResult result = await Task.Run(() => InstallBuild(build, instanceDir));

                DownloadProgress.IsIndeterminate = false;
                DownloadProgress.Value = 100;
                SpeedText.Text = "Скорость: завершено";
                TimeLeftText.Text = "Осталось: 00:00";
                FilesText.Text = $"Файлов: {result.CopiedFiles}";
                SetStatus("Готово", $"Сборка {build.Name} установлена и готова к выбору в TLauncher. Родительская версия: {result.ParentVersion}.");

                MessageBox.Show($"Готово. Сборка {build.Name} установлена / обновлена.", "Установка завершена");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка");
                SetStatus("Ошибка", "Не удалось установить сборку. Подробности показаны в окне ошибки.");
                DownloadProgress.IsIndeterminate = false;
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task DownloadFile(string source, string destination)
        {
            DownloadProgress.IsIndeterminate = true;

            if (File.Exists(source))
            {
                File.Copy(source, destination, true);
                DownloadProgress.IsIndeterminate = false;
                DownloadProgress.Value = 100;
                SpeedText.Text = "Скорость: локальный файл";
                TimeLeftText.Text = "Осталось: 00:00";
                return;
            }

            if (source.StartsWith(@"\\"))
            {
                File.Copy(source, destination, true);
                DownloadProgress.IsIndeterminate = false;
                DownloadProgress.Value = 100;
                SpeedText.Text = "Скорость: сетевой файл";
                TimeLeftText.Text = "Осталось: 00:00";
                return;
            }

            if (!source.StartsWith("http://") && !source.StartsWith("https://"))
            {
                throw new Exception("Неподдерживаемый путь загрузки. Используй C:\\file.zip, \\\\server\\share\\file.zip или https://...");
            }

            using HttpResponseMessage response =
                await _http.GetAsync(source, HttpCompletionOption.ResponseHeadersRead);

            response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength;

            if (totalBytes.HasValue)
                DownloadProgress.IsIndeterminate = false;

            await using Stream input =
                await response.Content.ReadAsStreamAsync();

            await using FileStream output =
                File.Create(destination);

            byte[] buffer = new byte[81920];

            long totalRead = 0;
            int read;
            Stopwatch stopwatch = Stopwatch.StartNew();

            while ((read = await input.ReadAsync(buffer)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read));

                totalRead += read;

                if (totalBytes.HasValue)
                {
                    double progress = totalRead * 100d / totalBytes.Value;
                    DownloadProgress.Value = progress;
                }

                double elapsedSeconds = Math.Max(stopwatch.Elapsed.TotalSeconds, 0.1);
                double bytesPerSecond = totalRead / elapsedSeconds;
                SpeedText.Text = $"Скорость: {FormatBytes(bytesPerSecond)}/с";

                if (totalBytes.HasValue && bytesPerSecond > 0)
                {
                    double secondsLeft = (totalBytes.Value - totalRead) / bytesPerSecond;
                    TimeLeftText.Text = $"Осталось: {TimeSpan.FromSeconds(secondsLeft):mm\\:ss}";
                }
            }
        }

        private string FormatBytes(double bytes)
        {
            string[] units = { "Б", "КБ", "МБ", "ГБ" };
            int unitIndex = 0;

            while (bytes >= 1024 && unitIndex < units.Length - 1)
            {
                bytes /= 1024;
                unitIndex++;
            }

            return $"{bytes:0.0} {units[unitIndex]}";
        }

        private async void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            if (BuildsComboBox.SelectedItem is not BuildInfo build)
                return;

            try
            {
                string versionId = GetVersionId(build);
                string instanceDir = Path.Combine(_baseDir, "instances", versionId);

                if (!Directory.Exists(instanceDir))
                {
                    MessageBox.Show("Сначала нажми Установить / обновить.");
                    return;
                }

                SetBusy(true);
                SetStatus("Установка", "Копируем сборку в папку versions и создаем файл версии для TLauncher.");

                InstallResult result = await Task.Run(() => InstallBuild(build, instanceDir));

                DownloadProgress.IsIndeterminate = false;
                DownloadProgress.Value = 100;
                SetStatus("Готово", $"Сборка {build.Name} установлена в {result.VersionDir}.");
                MessageBox.Show($"Готово. Скопировано файлов: {result.CopiedFiles}\nПапка: {result.VersionDir}", "Установка завершена");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка установки");
                SetStatus("Ошибка", "Не удалось установить сборку. Подробности показаны в окне ошибки.");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private InstallResult InstallBuild(BuildInfo build, string instanceDir)
        {
            string versionId = GetVersionId(build);
            string contentDir = FindMinecraftContentDirectory(instanceDir);

            string versionsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                ".minecraft",
                "versions"
            );
            string versionDir = Path.Combine(versionsDir, versionId);

            if (Directory.Exists(versionDir))
                Directory.Delete(versionDir, true);

            Directory.CreateDirectory(versionDir);

            string[] foldersToCopy =
            {
                "libraries",
                "natives",
                "mods",
                "config",
                "defaultconfigs",
                "kubejs",
                "resourcepacks",
                "shaderpacks",
                "scripts"
            };

            int copiedFiles = 0;
            bool copiedVersionJson = false;
            bool copiedVersionJar = false;

            foreach (string folder in foldersToCopy)
            {
                string sourceFolder = Path.Combine(contentDir, folder);
                string targetFolder = Path.Combine(versionDir, folder);

                if (Directory.Exists(sourceFolder))
                {
                    copiedFiles += CopyDirectory(sourceFolder, targetFolder);
                }
            }

            string[] filesToCopy =
            {
                $"{versionId}.json",
                $"{versionId}.jar",
                $"{build.MinecraftVersion}.json",
                $"{build.MinecraftVersion}.jar",
                "forge.jar",
                "version.json",
                "options.txt",
                "servers.dat",
                "servers.dat_old"
            };

            foreach (string file in filesToCopy)
            {
                string sourceFile = Path.Combine(contentDir, file);

                if (File.Exists(sourceFile))
                {
                    string targetFileName = file;

                    if (file == $"{build.MinecraftVersion}.json" || file == "version.json")
                        targetFileName = $"{versionId}.json";

                    if (file == $"{build.MinecraftVersion}.jar" || file == "forge.jar")
                        targetFileName = $"{versionId}.jar";

                    File.Copy(sourceFile, Path.Combine(versionDir, targetFileName), true);
                    copiedVersionJson |= targetFileName == $"{versionId}.json";
                    copiedVersionJar |= targetFileName == $"{versionId}.jar";

                    if (targetFileName == $"{versionId}.json")
                        NormalizeVersionJsonId(Path.Combine(versionDir, targetFileName), versionId);

                    copiedFiles++;
                }
            }

            string parentVersion = ResolveParentVersion(versionsDir, build);

            if (copiedFiles == 0)
            {
                throw new Exception(
                    $"В сборке не найдены папки mods/config/resourcepacks и другие файлы для установки.\nПроверенный путь: {contentDir}"
                );
            }

            if (!copiedVersionJson)
            {
                GenerateTLauncherVersionJson(versionDir, build, versionId, parentVersion);
                copiedVersionJson = true;
                copiedFiles++;
            }

            return new InstallResult
            {
                CopiedFiles = copiedFiles,
                VersionDir = versionDir,
                VersionId = versionId,
                ParentVersion = parentVersion,
                HasVersionJson = copiedVersionJson,
                HasVersionJar = copiedVersionJar
            };
        }

        private string FindMinecraftContentDirectory(string directory)
        {
            string dotMinecraftDir = Path.Combine(directory, ".minecraft");

            if (Directory.Exists(dotMinecraftDir))
                return dotMinecraftDir;

            if (LooksLikeMinecraftContentDirectory(directory))
                return directory;

            string[] childDirectories = Directory.GetDirectories(directory);

            if (childDirectories.Length == 1)
                return FindMinecraftContentDirectory(childDirectories[0]);

            return directory;
        }

        private bool LooksLikeMinecraftContentDirectory(string directory)
        {
            string[] minecraftFolders =
            {
                "mods",
                "config",
                "defaultconfigs",
                "kubejs",
                "resourcepacks",
                "shaderpacks",
                "scripts"
            };

            foreach (string folder in minecraftFolders)
            {
                if (Directory.Exists(Path.Combine(directory, folder)))
                    return true;
            }

            return false;
        }

        private string GetVersionId(BuildInfo build)
        {
            string versionId = string.IsNullOrWhiteSpace(build.VersionId)
                ? build.Name
                : build.VersionId;

            versionId = versionId.Trim();

            foreach (char invalidChar in Path.GetInvalidFileNameChars())
                versionId = versionId.Replace(invalidChar, '-');

            if (string.IsNullOrWhiteSpace(versionId))
                throw new Exception("У сборки не задано имя версии.");

            return versionId;
        }

        private string ResolveParentVersion(string versionsDir, BuildInfo build)
        {
            if (!string.IsNullOrWhiteSpace(build.InheritsFrom))
                return build.InheritsFrom;

            string? modLoaderVersion = FindInstalledModLoaderVersion(versionsDir, build.MinecraftVersion);

            if (modLoaderVersion is not null)
                return modLoaderVersion;

            return build.MinecraftVersion;
        }

        private string? FindInstalledModLoaderVersion(string versionsDir, string minecraftVersion)
        {
            if (!Directory.Exists(versionsDir))
                return null;

            string[] loaderNames =
            {
                "forge",
                "neoforge",
                "fabric",
                "quilt"
            };

            foreach (string loaderName in loaderNames)
            {
                foreach (string directory in Directory.GetDirectories(versionsDir))
                {
                    string versionName = Path.GetFileName(directory);

                    if (!versionName.Contains(minecraftVersion, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!versionName.Contains(loaderName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (File.Exists(Path.Combine(directory, $"{versionName}.json")))
                        return versionName;
                }
            }

            return null;
        }

        private void GenerateTLauncherVersionJson(string versionDir, BuildInfo build, string versionId, string parentVersion)
        {
            string now = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

            Dictionary<string, object> versionJson = new()
            {
                ["id"] = versionId,
                ["inheritsFrom"] = parentVersion,
                ["type"] = "release",
                ["time"] = now,
                ["releaseTime"] = now,
                ["minimumLauncherVersion"] = 21
            };

            string json = JsonSerializer.Serialize(
                versionJson,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }
            );

            File.WriteAllText(Path.Combine(versionDir, $"{versionId}.json"), json);
        }

        private void NormalizeVersionJsonId(string jsonPath, string versionName)
        {
            try
            {
                JsonNode? node = JsonNode.Parse(File.ReadAllText(jsonPath));

                if (node is not JsonObject versionObject)
                    return;

                versionObject["id"] = versionName;

                string json = versionObject.ToJsonString(
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }
                );

                File.WriteAllText(jsonPath, json);
            }
            catch (JsonException)
            {
            }
            catch (IOException)
            {
            }
        }

        private int CopyDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            int copiedFiles = 0;

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string fileName = Path.GetFileName(file);
                string destFile = Path.Combine(targetDir, fileName);
                File.Copy(file, destFile, true);
                copiedFiles++;
            }

            foreach (string directory in Directory.GetDirectories(sourceDir))
            {
                string dirName = Path.GetFileName(directory);
                string destDir = Path.Combine(targetDir, dirName);
                copiedFiles += CopyDirectory(directory, destDir);
            }

            return copiedFiles;
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            string? launcherPath = FindTLauncher() ?? FindMinecraftLauncher();

            if (launcherPath is not null)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = launcherPath,
                    UseShellExecute = true
                });
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "minecraft://",
                    UseShellExecute = true
                });
            }
            catch
            {
                MessageBox.Show(
                    "TLauncher или Minecraft Launcher не найден. Открой TLauncher вручную и выбери установленную версию сборки.",
                    "Ошибка"
                );
            }
        }

        private string? FindTLauncher()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string startMenu = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);

            string[] candidatePaths =
            {
                Path.Combine(appData, ".minecraft", "TLauncher.exe"),
                Path.Combine(appData, ".tlauncher", "TLauncher.exe"),
                Path.Combine(appData, ".tlauncher", "legacy", "Minecraft", "TLauncher.exe"),
                Path.Combine(localAppData, "Programs", "TLauncher", "TLauncher.exe"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "TLauncher",
                    "TLauncher.exe"
                ),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "TLauncher",
                    "TLauncher.exe"
                )
            };

            foreach (string candidatePath in candidatePaths)
            {
                if (File.Exists(candidatePath))
                    return candidatePath;
            }

            return FindExecutableByName(desktop, "TLauncher*.exe", 2)
                ?? FindExecutableByName(startMenu, "TLauncher*.exe", 4)
                ?? FindExecutableByName(appData, "TLauncher*.exe", 3)
                ?? FindExecutableByName(localAppData, "TLauncher*.exe", 3);
        }

        private string? FindExecutableByName(string rootDirectory, string searchPattern, int maxDepth)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory))
                return null;

            return FindExecutableByName(rootDirectory, searchPattern, maxDepth, 0);
        }

        private string? FindExecutableByName(string directory, string searchPattern, int maxDepth, int currentDepth)
        {
            try
            {
                foreach (string file in Directory.EnumerateFiles(directory, searchPattern))
                    return file;

                if (currentDepth >= maxDepth)
                    return null;

                foreach (string childDirectory in Directory.EnumerateDirectories(directory))
                {
                    string? result = FindExecutableByName(childDirectory, searchPattern, maxDepth, currentDepth + 1);

                    if (result is not null)
                        return result;
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }

            return null;
        }

        private string? FindMinecraftLauncher()
        {
            string[] candidatePaths =
            {
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Minecraft Launcher",
                    "MinecraftLauncher.exe"
                ),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Minecraft Launcher",
                    "MinecraftLauncher.exe"
                ),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs",
                    "Minecraft Launcher",
                    "MinecraftLauncher.exe"
                )
            };

            foreach (string candidatePath in candidatePaths)
            {
                if (File.Exists(candidatePath))
                    return candidatePath;
            }

            return null;
        }

        private class InstallResult
        {
            public int CopiedFiles { get; set; }
            public string VersionDir { get; set; } = "";
            public string VersionId { get; set; } = "";
            public string ParentVersion { get; set; } = "";
            public bool HasVersionJson { get; set; }
            public bool HasVersionJar { get; set; }
        }
    }

    public class LauncherConfig
    {
        public string GitHubOwner { get; set; } = "HeavensFeelBad";
        public string GitHubRepo { get; set; } = "BazarCraftLauncher";
        public string MinecraftReleaseTag { get; set; } = "minecraft";
        public string DefaultMinecraftVersion { get; set; } = "1.20.1";
        public List<BuildInfo> Builds { get; set; } = new();
    }

    public class BuildInfo
    {
        public string Name { get; set; } = "";
        public string VersionId { get; set; } = "";
        public string Version { get; set; } = "";
        public string MinecraftVersion { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string MainClass { get; set; } = "";
        public string JavaPath { get; set; } = "";
        public string InheritsFrom { get; set; } = "";
        public int MemoryMb { get; set; } = 4096;
    }
}
