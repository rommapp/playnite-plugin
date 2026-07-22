using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Playnite.SDK;

using QRCoder;

using RomM.Models.RomM;
using RomM.Models.RomM.Platform;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace RomM.Settings
{
    public partial class SettingsView : UserControl
    {
        private string QRVerificationPath = "";

        public SettingsView()
        {
            InitializeComponent();
        }

        private void Click_TestConnection(object sender, RoutedEventArgs e)
        {
            SettingsViewModel.Instance.TestConnection(true);
            e.Handled = true;
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                if (e.Uri.Scheme == Uri.UriSchemeHttp || e.Uri.Scheme == Uri.UriSchemeHttps)
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = e.Uri.AbsoluteUri,
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to open URL: {ex.Message}");
            }
            e.Handled = true;
        }

        private async void Click_PullPlatforms(object sender, RoutedEventArgs e)
        {
            SettingsViewModel.Instance.Notify = false;

            try
            {
                HttpResponseMessage response = await HttpClientSingleton.Instance.GetAsync($"{SettingsViewModel.Instance.RomMHost}/api/platforms");
                response.EnsureSuccessStatusCode();

                string body = await response.Content.ReadAsStringAsync();
                SettingsViewModel.Instance.RomMPlatforms = JsonConvert.DeserializeObject<List<RomMPlatform>>(body);
                SettingsViewModel.Instance.UpdateNotificationBar("Platforms successfully retrieved!");
            }
            catch (Exception ex)
            {
                LogManager.GetLogger().Error($"RomM - failed to get platforms: {ex}");
                SettingsViewModel.Instance.UpdateNotificationBar($"Failed to get platforms: {ex.Message}!", true);
            }
        }

        private void Click_AddMapping(object sender, RoutedEventArgs e)
        {
            SettingsViewModel.Instance.Mappings.Add(new EmulatorMapping(SettingsViewModel.Instance.RomMPlatforms));
        }

        private void Click_Delete(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is EmulatorMapping mapping)
            {
                var res = SettingsViewModel.Instance.PlayniteAPI.Dialogs.ShowMessage(string.Format("Delete this mapping?\r\n\r\n{0}", mapping.GetDescriptionLines().Aggregate((a, b) => $"{a}{Environment.NewLine}{b}")), "Confirm delete", MessageBoxButton.YesNo);
                if (res == MessageBoxResult.Yes)
                {
                    SettingsViewModel.Instance.Mappings.Remove(mapping);
                }
            }
        }

        private void Click_BrowseDestination(object sender, RoutedEventArgs e)
        {
            var mapping = ((FrameworkElement)sender).DataContext as EmulatorMapping;
            string path;
            if ((path = GetSelectedFolderPath()) == null) return;
            var playnite = SettingsViewModel.Instance.PlayniteAPI;
            if (playnite.Paths.IsPortable)
            {
                path = path.Replace(playnite.Paths.ApplicationPath, Playnite.SDK.ExpandableVariables.PlayniteDirectory);
            }

            mapping.DestinationPath = path;
        }

        private static string GetSelectedFolderPath()
        {
            return SettingsViewModel.Instance.PlayniteAPI.Dialogs.SelectFolder();
        }

        private void Click_Browse7zDestination(object sender, RoutedEventArgs e)
        {
            string path;
            if ((path = SettingsViewModel.Instance.PlayniteAPI.Dialogs.SelectFile("7Zip Executable|7z.exe")) == null) return;

            SettingsViewModel.Instance.PathTo7z = path;
            e.Handled = true;
        }

        private async void Click_LoginViaQR(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(SettingsViewModel.Instance.RomMHost))
            {
                SettingsViewModel.Instance.UpdateNotificationBar("RomMHost is empty!", true);
                e.Handled = true;
                return;
            }

            try
            {
                HttpResponseMessage response = await HttpClientSingleton.Instance.GetAsync($"{SettingsViewModel.Instance.RomMHost}/api/heartbeat");
                response.EnsureSuccessStatusCode();

                Stream body = await response.Content.ReadAsStreamAsync();
                using (StreamReader reader = new StreamReader(body))
                {
                    var jsonResponse = JObject.Parse(reader.ReadToEnd());
                    var heartbeat = jsonResponse.ToObject<RomMHeartbeat>();

                    string raw = (heartbeat.SystemInfo.Version ?? string.Empty).Split('-', '+')[0];
                    if (!Version.TryParse(raw, out var parsed) || parsed.CompareTo(new Version(5, 0, 0)) < 0)
                    {
                        SettingsViewModel.Instance.UpdateNotificationBar("Server needs to be v5.0.0 or later!", true);
                        e.Handled = true;
                        return;
                    }

                }

                var deviceInit = new
                {
                    client_device_identifier = $"Playnite-{Environment.MachineName}",
                    name = $"Playnite-{Environment.MachineName}",
                    client = "Playnite Plugin",
                    platform = "Windows",
                    client_version = "0.7.0", // This should be probably be changed to reading the extension.yaml at runtime or
                                              //    adding a plugin version to the main file that devs change every update
                    requested_scopes = new List<string>
                    {
                        "me.read", "me.write",
                        "assets.read", "assets.write",
                        "devices.read", "devices.write",
                        "roms.user.read","roms.user.write",
                        "roms.read",
                        "platforms.read",
                        "firmware.read",
                        "collections.read", "collections.write"
                    }
                };

                var initContent = new StringContent(JsonConvert.SerializeObject(deviceInit), Encoding.UTF8, "application/json");
                response = await HttpClientSingleton.Instance.PostAsync($"{SettingsViewModel.Instance.RomMHost}/api/auth/device/init", initContent);
                response.EnsureSuccessStatusCode();

                body = await response.Content.ReadAsStreamAsync();
                using (StreamReader reader = new StreamReader(body))
                {
                    var jsonResponse = JObject.Parse(reader.ReadToEnd());
                    var pairDevice = jsonResponse.ToObject<RomMPairDevice>();
                    QRAuth.IsEnabled = false;
                    await UpdateQR(pairDevice);
                }
     
            }
            catch (Exception ex)
            {
                SettingsViewModel.Instance.UpdateNotificationBar($"Failed to login via QR! - {ex.Message}", true);
                LogManager.GetLogger().Error($"Failed to login via QR! - {ex}");
                e.Handled = true;
                return;
            }

            e.Handled = true;
        }

        private async Task UpdateQR(RomMPairDevice pairDevice)
        {
            
            var intervalMillisecs = pairDevice.Interval * 1000;
            var deviceCode = new { device_code = pairDevice.DeviceCode };

            var startTime = DateTime.UtcNow;
            var expiresin = TimeSpan.FromSeconds(pairDevice.ExpiresIn - 1);

            QRVerificationPath = pairDevice.VerificationPathComplete;

            LoginQR.Background = new System.Windows.Media.ImageBrush(BuildQRCode($"{SettingsViewModel.Instance.RomMHost}{QRVerificationPath}"));
            QRPanel.Visibility = Visibility.Visible;
            QRDetails.Visibility = Visibility.Visible;

            try
            {
                while ((DateTime.UtcNow - startTime) < expiresin)
                {
                    if (intervalMillisecs <= 0)
                    {
                        HttpResponseMessage response = null;
                        string result = "";
                        try
                        {
                            var deviceCodeContent = new StringContent(JsonConvert.SerializeObject(deviceCode), Encoding.UTF8, "application/json");
                            response = await HttpClientSingleton.Instance.PostAsync($"{SettingsViewModel.Instance.RomMHost}/api/auth/device/token", deviceCodeContent);
                            result = await response.Content.ReadAsStringAsync();
                            response.EnsureSuccessStatusCode();

                            var pairResponse = JsonConvert.DeserializeObject<RomMPairDeviceResponse>(result);
                            SettingsViewModel.Instance.RomMDeviceID = pairResponse.DeviceID;
                            SettingsViewModel.Instance.RomMClientToken = pairResponse.AccessToken;
                            SettingsViewModel.Instance.TestConnection(true, true);
                            break;

                        }
                        catch (Exception ex)
                        {
                            SettingsViewModel.Instance.Notify = false;
                            if (response == null)
                            {
                                SettingsViewModel.Instance.UpdateNotificationBar($"Failed to login via QR! - No Response", true);
                                LogManager.GetLogger().Error($"Failed to login via QR! - No Response - {ex}");
                                break;
                            }

                            if (result.Contains("expired_token"))
                            {
                                SettingsViewModel.Instance.UpdateNotificationBar($"Failed to login via QR! - Login Expired", true);
                                LogManager.GetLogger().Error($"Failed to login via QR! - Login Expired");
                                break;
                            }
                            if (result.Contains("access_denied"))
                            {
                                SettingsViewModel.Instance.UpdateNotificationBar($"Failed to login via QR! - Access Denied", true);
                                LogManager.GetLogger().Error($"Failed to login via QR! - Access Denied");
                                break;
                            }

                            if (response.StatusCode != HttpStatusCode.BadRequest)
                            {
                                SettingsViewModel.Instance.UpdateNotificationBar($"Failed to login via QR! - {ex.Message}", true);
                                LogManager.GetLogger().Error($"Failed to login via QR! - {ex}");
                                break;
                            }
                        }

                        intervalMillisecs = pairDevice.Interval * 1000;
                    }

                    LoginQRTimer.Text = $"Expires in: {(((expiresin - (DateTime.UtcNow - startTime)).TotalMilliseconds) / 1000).ToString("F1")}s";

                    await Task.Delay(100);
                    intervalMillisecs -= 100;
                }
            }
            catch (Exception){}
            finally
            {
                QRPanel.Visibility = Visibility.Collapsed;
                QRAuth.IsEnabled = true;
                QRDetails.Visibility = Visibility.Collapsed;
                QRVerificationPath = "";
            }    
        }

        private void Click_OpenInBrowser(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = $"{SettingsViewModel.Instance.RomMHost}{QRVerificationPath}",
                    UseShellExecute = true
                });           
            }
            catch (Exception ex)
            {
                SettingsViewModel.Instance.Notify = false;
                SettingsViewModel.Instance.UpdateNotificationBar($"Failed to open URL - {ex.Message}", true);
                LogManager.GetLogger().Error($"Failed to open URL! - {ex}");
            }
            e.Handled = true;
        }

        #region QR Code
        private GraphicsPath RoundedRect(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();

            float d = radius * 2;

            if (radius <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }

            if (d > rect.Width) d = rect.Width;
            if (d > rect.Height) d = rect.Height;

            radius = d / 2;

            path.StartFigure();
            path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();

            return path;
        }

        private void DrawFinder(Graphics g, int x, int y, int pixelsPerModule, System.Drawing.Color light)
        {
            int size = pixelsPerModule * 7;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var lightBrush = new SolidBrush(light))
            {
                using (var outer = new GraphicsPath())
                using (var middleHole = new GraphicsPath())
                {
                    using (var outerShape = RoundedRect(new RectangleF(x, y, size, size), pixelsPerModule * 1.6f))
                    {
                        outer.AddPath(outerShape, false);
                    }

                    int border = (int)(pixelsPerModule * 0.6f);
                    using (var middleHoleShape = RoundedRect(new RectangleF(x + border, y + border, size - border * 2, size - border * 2), pixelsPerModule * 1.2f))
                    {
                        middleHole.AddPath(middleHoleShape, false);
                    }

                    using (var region = new System.Drawing.Region(outer))
                    {
                        region.Exclude(middleHole);
                        g.FillRegion(lightBrush, region);
                    }
                }

                int centre = pixelsPerModule * 2;
                using (var centerPath = RoundedRect(new RectangleF(x + centre, y + centre, size - centre * 2, size - centre * 2), pixelsPerModule * 0.8f))
                {
                    g.FillPath(lightBrush, centerPath);
                }
            }      
        }

        private BitmapImage BuildQRCode(string data)
        {
            using (Bitmap logo = new Bitmap(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), @"icon.png")))
            {
                using (var qrGenerator = new QRCodeGenerator())
                using (var qrCodeData = qrGenerator.CreateQrCode(data, QRCodeGenerator.ECCLevel.H))
                using (var qrCode = new ArtQRCode(qrCodeData))
                {
                    var lightcolour = System.Drawing.Color.FromArgb(255, 250, 250, 245);
                    var darkcolour = System.Drawing.Color.FromArgb(255, 20, 20, 30);

                    Bitmap qrBitmap = qrCode.GetGraphic(
                                                    pixelsPerModule: 20,
                                                    darkColor: lightcolour,
                                                    lightColor: darkcolour,
                                                    backgroundColor: System.Drawing.Color.Transparent,
                                                    pixelSizeFactor: 0.7,
                                                    drawQuietZones: false
                                                    );

                    const int padding = 20;
                    Bitmap canvas = new Bitmap(qrBitmap.Width + padding * 2, qrBitmap.Height + padding * 2, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                    using (Graphics g = Graphics.FromImage(canvas))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = PixelOffsetMode.Half;

                        g.DrawImage(qrBitmap, padding, padding);

                        int finderSize = 7 * 20;
                        using (var coverPath = new GraphicsPath())
                        {
                            coverPath.AddRectangle(new RectangleF(padding, padding, finderSize, finderSize));
                            coverPath.AddRectangle(new RectangleF(canvas.Width - padding - finderSize, padding, finderSize, finderSize));
                            coverPath.AddRectangle(new RectangleF(padding, canvas.Height - padding - finderSize, finderSize, finderSize));

                            GraphicsState clearState = g.Save();
                            g.SetClip(coverPath);
                            g.Clear(System.Drawing.Color.Transparent);
                            g.Restore(clearState);
                        }

                        DrawFinder(g, 20, 20, 20, lightcolour);
                        DrawFinder(g, canvas.Width - 20 - finderSize, 20, 20, lightcolour);
                        DrawFinder(g, 20, canvas.Height - 20 - finderSize, 20, lightcolour);

                        int iconSize = (int)(canvas.Width * 0.20);
                        int iconX = (canvas.Width - iconSize) / 2;
                        int iconY = (canvas.Height - iconSize) / 2;

                        const int badgePadding = 20;
                        const int badgeRadius = 20;

                        RectangleF badgeRect = new RectangleF(iconX - badgePadding, iconY - badgePadding, iconSize + badgePadding * 2, iconSize + badgePadding * 2);
                        using (var badgePath = RoundedRect(badgeRect, badgeRadius))
                        {
                            GraphicsState state = g.Save();
                            g.SetClip(badgePath);
                            g.Clear(System.Drawing.Color.Transparent);
                            g.Restore(state);

                            using (GraphicsPath logoPath = RoundedRect(new RectangleF(iconX, iconY, iconSize, iconSize), 16))
                            {
                                state = g.Save();
                                g.SetClip(logoPath);
                                g.DrawImage(logo, iconX, iconY, iconSize, iconSize);
                                g.Restore(state);
                            }
                        }
                    }

                    qrBitmap.Dispose();
                    qrBitmap = canvas;

                    using (MemoryStream memory = new MemoryStream())
                    {
                        qrBitmap.Save(memory, ImageFormat.Png);
                        memory.Position = 0;

                        BitmapImage bitmapImage = new BitmapImage();
                        bitmapImage.BeginInit();
                        bitmapImage.StreamSource = memory;
                        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                        bitmapImage.EndInit();
                        bitmapImage.Freeze();

                        qrBitmap.Dispose();
                        return bitmapImage;
                    }
                }
            } 
        }
        #endregion

    }
}