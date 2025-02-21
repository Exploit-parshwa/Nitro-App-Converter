using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace Nitro_App_Converter
{
    public partial class MainWindow : Window
    {
        private readonly string projectDirectory;
        private string? outputApkPath;

        public MainWindow()
        {
            InitializeComponent();
            projectDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Nitro_App_Convert",
                "Projects",
                DateTime.Now.ToString("yyyyMMddHHmmss")
            );
        }

        private async void ConvertToApkButton_Click(object sender, RoutedEventArgs e)
        {
            string websiteLink = WebsiteLinkTextBox.Text.Trim();

            if (!IsValidUrl(websiteLink))
            {
                ShowStatus("Please enter a valid website URL", true);
                return;
            }

            try
            {
                ToggleUI(false);
                await ProcessApkConversion(websiteLink);
                ShowStatus("Build complete! Opening output directory...");
                OpenOutputDirectory();
            }
            catch (Exception ex)
            {
                ShowStatus($"Error: {ex.Message}", true);
            }
            finally
            {
                ToggleUI(true);
            }
        }

        private async Task ProcessApkConversion(string websiteLink)
        {
            ShowStatus("Initializing project...");
            await InitializeProject();

            ShowStatus("Creating Cordova project...");
            await CreateCordovaProject(websiteLink);

            ShowStatus("Building Android package...");
            await BuildAndroidPackage(websiteLink);
        }

        private static bool IsValidUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out Uri? uriResult)
                && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
        }

        private async Task InitializeProject()
        {
            await Task.Run(() =>
            {
                string backupDirectory = Path.Combine(projectDirectory, "backup");
                if (Directory.Exists(projectDirectory))
                {
                    if (!Directory.Exists(backupDirectory))
                    {
                        Directory.CreateDirectory(backupDirectory);
                    }

                    // Move existing APKs to backup directory
                    string[] apkFiles = Directory.GetFiles(projectDirectory, "*.apk", SearchOption.AllDirectories);
                    foreach (string apkFile in apkFiles)
                    {
                        string fileName = Path.GetFileName(apkFile);
                        string destFile = Path.Combine(backupDirectory, fileName);
                        File.Move(apkFile, destFile, true);
                    }

                    Directory.Delete(projectDirectory, true);
                }
                Directory.CreateDirectory(projectDirectory);
            });
        }

        private async Task CreateCordovaProject(string websiteLink)
        {
            await ExecuteCommand("cordova create . com.example.app MyApp", projectDirectory);
            await ExecuteCommand("cordova platform add android", projectDirectory);

            var wwwPath = Path.Combine(projectDirectory, "www");
            Directory.CreateDirectory(wwwPath);

            var configContent = @"<?xml version='1.0' encoding='utf-8'?>
                <widget id=""com.example.app"" version=""1.0.0"" xmlns=""http://www.w3.org/ns/widgets"">
                    <name>MyApp</name>
                    <content src=""index.html"" />
                    <access origin=""*"" />
                    <allow-intent href=""http://*/*"" />
                    <allow-intent href=""https://*/*"" />
                </widget>";
            File.WriteAllText(Path.Combine(projectDirectory, "config.xml"), configContent);

            var htmlContent = $@"<!DOCTYPE html>
                <html>
                <head>
                    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                    <style>
                        body, html {{
                            margin: 0;
                            padding: 0;
                            width: 100%;
                            height: 100%;
                            overflow: hidden;
                        }}
                        iframe {{
                            width: 100%;
                            height: 100%;
                            border: none;
                        }}
                    </style>
                </head>
                <body>
                    <iframe src='{websiteLink}'></iframe>
                </body>
                </html>";
            File.WriteAllText(Path.Combine(wwwPath, "index.html"), htmlContent);
        }

        private async Task BuildAndroidPackage(string websiteLink)
        {
            await ExecuteCommand("cordova build android", projectDirectory);
            outputApkPath = Path.Combine(projectDirectory, "platforms", "android", "app", "build", "outputs", "apk", "debug", "app-debug.apk");

            if (!File.Exists(outputApkPath))
            {
                throw new Exception("APK generation failed. Check build logs.");
            }

            // Extract domain name from URL
            string domainName = new Uri(websiteLink).Host.Replace("www.", "").Split('.')[0];
            string newApkPath = Path.Combine(projectDirectory, "platforms", "android", "app", "build", "outputs", "apk", "debug", $"{domainName}.apk");

            // Rename the APK file
            File.Move(outputApkPath, newApkPath);
            outputApkPath = newApkPath;
        }

        private void OpenOutputDirectory()
        {
            if (!string.IsNullOrEmpty(outputApkPath) && File.Exists(outputApkPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{outputApkPath}\"",
                    UseShellExecute = true
                });
            }
            else if (Directory.Exists(projectDirectory))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = projectDirectory,
                    UseShellExecute = true
                });
            }
        }

        private static async Task ExecuteCommand(string command, string workingDirectory)
        {
            await Task.Run(() =>
            {
                using Process process = new();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/C {command}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = workingDirectory
                };

                // Set JAVA_HOME explicitly
                process.StartInfo.EnvironmentVariables["JAVA_HOME"] = @"C:\Program Files\Java\jdk-17";

                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    throw new Exception($"Command failed: {command}\nOutput: {output}\nError: {error}");
                }
            });
        }

        private void ToggleUI(bool isEnabled)
        {
            Dispatcher.Invoke(() =>
            {
                WebsiteLinkTextBox.IsEnabled = isEnabled;
                ConvertToApkButton.IsEnabled = isEnabled;
                UpgradeToPremiumButton.IsEnabled = isEnabled;
                BuildProgress.Visibility = isEnabled ? Visibility.Collapsed : Visibility.Visible;
            });
        }

        private void ShowStatus(string message, bool isError = false)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = message;
                StatusText.Foreground = isError
                    ? System.Windows.Media.Brushes.OrangeRed
                    : System.Windows.Media.Brushes.LightGray;
            });
        }

        private void UpgradeToPremiumButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("🚀 Premium Features Coming Soon!\n\n" +
                "• Remove Watermarks\n" +
                "• Custom App Icon\n" +
                "• Advanced Settings\n" +
                "• Priority Support",
                "Premium Features",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void WebsiteLinkTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (WebsiteLinkTextBox.Text == "https://example.com")
            {
                WebsiteLinkTextBox.Text = "";
                WebsiteLinkTextBox.Foreground = System.Windows.Media.Brushes.White;
            }
        }

        private void WebsiteLinkTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(WebsiteLinkTextBox.Text))
            {
                WebsiteLinkTextBox.Text = "https://example.com";
                WebsiteLinkTextBox.Foreground = System.Windows.Media.Brushes.Gray;
            }
        }
    }
}