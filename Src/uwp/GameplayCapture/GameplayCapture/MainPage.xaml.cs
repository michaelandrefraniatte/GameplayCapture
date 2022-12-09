using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using CaptureEncoder;
using System.Diagnostics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Media.MediaProperties;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;
using Windows.UI.ViewManagement;

// Pour plus d'informations sur le modèle d'élément Page vierge, consultez la page https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace GameplayCapture
{
    /// <summary>
    /// Une page vide peut être utilisée seule ou constituer une page de destination au sein d'un frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();

            ApplicationView.GetForCurrentView().SetPreferredMinSize(
               new Size(350, 200));

            if (!GraphicsCaptureSession.IsSupported())
            {
                IsEnabled = false;

                var dialog = new MessageDialog(
                    "Screen capture is not supported on this device for this release of Windows!",
                    "Screen capture unsupported");

                var ignored = dialog.ShowAsync();
                return;
            }

            _device = Direct3D11Helpers.CreateDevice();

            var settings = GetCachedSettings();

            var names = new List<string>();
            names.Add(nameof(VideoEncodingQuality.HD1080p));
            names.Add(nameof(VideoEncodingQuality.HD720p));
            names.Add(nameof(VideoEncodingQuality.Uhd2160p));
            names.Add(nameof(VideoEncodingQuality.Uhd4320p));
            QualityComboBox.ItemsSource = names;
            QualityComboBox.SelectedIndex = names.IndexOf(settings.Quality.ToString());

            var frameRates = new List<string> { "30fps", "60fps" };
            FrameRateComboBox.ItemsSource = frameRates;
            FrameRateComboBox.SelectedIndex = frameRates.IndexOf($"{settings.FrameRate}fps");

            UseCaptureItemSizeCheckBox.IsChecked = settings.UseSourceSize;
        }
        private async void Start_Button_Click(object sender, RoutedEventArgs e)
        {
            var button = (ToggleButton)sender;

            // Get our encoder properties
            var frameRate = uint.Parse(((string)FrameRateComboBox.SelectedItem).Replace("fps", ""));
            var quality = (VideoEncodingQuality)Enum.Parse(typeof(VideoEncodingQuality), (string)QualityComboBox.SelectedItem, false);
            var useSourceSize = UseCaptureItemSizeCheckBox.IsChecked.Value;

            var temp = MediaEncodingProfile.CreateMp4(quality);
            var bitrate = temp.Video.Bitrate;
            var width = temp.Video.Width;
            var height = temp.Video.Height;

            // Get our capture item
            var picker = new GraphicsCapturePicker();
            var item = await picker.PickSingleItemAsync();
            if (item == null)
            {
                button.IsChecked = false;
                return;
            }

            // Use the capture item's size for the encoding if desired
            if (useSourceSize)
            {
                width = (uint)item.Size.Width;
                height = (uint)item.Size.Height;

                // Even if we're using the capture item's real size,
                // we still want to make sure the numbers are even.
                // Some encoders get mad if you give them odd numbers.
                width = EnsureEven(width);
                height = EnsureEven(height);
            }

            // Find a place to put our vidoe for now
            var file = await GetTempFileAsync();

            // Tell the user we've started recording
            MainTextBlock.Text = "● rec";
            var originalBrush = MainTextBlock.Foreground;
            MainTextBlock.Foreground = new SolidColorBrush(Colors.Red);

            // Kick off the encoding
            try
            {
                using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
                using (_encoder = new Encoder(_device, item))
                {
                    await _encoder.EncodeAsync(
                        stream,
                        width, height, bitrate,
                        frameRate);
                }
                MainTextBlock.Foreground = originalBrush;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                Debug.WriteLine(ex);

                var message = GetMessageForHResult(ex.HResult);
                if (message == null)
                {
                    message = $"Uh-oh! Something went wrong!\n0x{ex.HResult:X8} - {ex.Message}";
                }
                var dialog = new MessageDialog(
                    message,
                    "Recording failed");

                await dialog.ShowAsync();

                button.IsChecked = false;
                MainTextBlock.Text = "failure";
                MainTextBlock.Foreground = originalBrush;
                return;
            }

            // At this point the encoding has finished,
            // tell the user we're now saving
            MainTextBlock.Text = "saving...";

            // Ask the user where they'd like the video to live
            var newFile = await PickVideoAsync();
            if (newFile == null)
            {
                // User decided they didn't want it
                // Throw out the encoded video
                button.IsChecked = false;
                MainTextBlock.Text = "canceled";
                await file.DeleteAsync();
                return;
            }
            // Move our vidoe to its new home
            await file.MoveAndReplaceAsync(newFile);

            // Tell the user we're done
            button.IsChecked = false;
            MainTextBlock.Text = "done";

            // Open the final product
            await Launcher.LaunchFileAsync(newFile);
        }

        private void Stop_Button_Click(object sender, RoutedEventArgs e)
        {
            // If the encoder is doing stuff, tell it to stop
            _encoder?.Dispose();
        }

        private void StartMessage()
        {
            /* Single object arrays with a string object and a "UICommandInvokedHandler" handler.
             * The ShowMessage function will only use the first and second index of these arrays.
             * Replace the "return" statement with a function or whatever you desire.
             * (The "return" statement will just return and do nothing (obviously))
             * (Edit:  Changed 'e' to 'h' in UICommandInvokedHandler's)
             */
            object[] button_one = { "Yes", new UICommandInvokedHandler((h) => { return; }) };
            object[] button_two = { "No", new UICommandInvokedHandler((h) => { return; }) };

            /* Object arrays within an object array.
             * The first index in this array will become the first button in the following message.
             * The first button will also get a different color and will become the default index.
             * For instance, if you press on the "enter" key, it will press on the first button.
             * You can add as many buttons as the "Windows.UI.Popups.MessageDialog" wants you to.
             */
            object[][] buttons = new object[][]
            {
                button_one,
                button_two
            };

            // Displays a popup message with multiple buttons
            ShowMessage("Title", "Content here", buttons);

            /* Displays a popup message without multiple buttons.
             * The last argument of the ShowMessage function is optional.
             * because of the definition of the namespace "System.Runtime.InteropServices".
             */
            ShowMessage("Title", "Content here");

            // PS, I have a life, just trying to get points xD // BluDay
        }

        // I will only comment those that are not obvious to comprehend.
        private async void ShowMessage(string title, string content, [Optional] object[][] buttons)
        {
            MessageDialog dialog = new MessageDialog(content, title);

            // Sets the default cancel and default indexes to zero. (incase no buttons are passed)
            dialog.CancelCommandIndex = 0;
            dialog.DefaultCommandIndex = 0;

            // If the optional buttons array is not empty or null.
            if (buttons != null)
            {
                // If there's multiple buttons
                if (buttons.Length > 1)
                {
                    // Loops through the given buttons array
                    for (Int32 i = 0; i < buttons.Length; i++)
                    {
                        /* Assigns text and handler variables from the current index subarray.
                         * The first object at the currentindex should be a string and 
                         * the second object should be a "UICommandInvokedHandler" 
                         */
                        string text = (string)buttons[i][0];

                        UICommandInvokedHandler handler = (UICommandInvokedHandler)buttons[i][1];

                        /* Checks whether both variables types actually are relevant and correct.
                         * If not, it will return and terminate this function and not display anything.
                         */
                        if (handler.GetType().Equals(typeof(UICommandInvokedHandler)) &&
                            text.GetType().Equals(typeof(string)))
                        {
                            /* Creates a new "UICommand" instance which is required for
                             * adding multiple buttons.
                             */
                            UICommand button = new UICommand(text, handler);

                            // Simply adds the newly created button to the dialog
                            dialog.Commands.Add(button);
                        }
                        else return;
                    }
                }
                else
                {
                    // Already described
                    string text = (string)buttons[0][0];

                    UICommandInvokedHandler handler = (UICommandInvokedHandler)buttons[0][1];

                    // Already described
                    if (handler.GetType().Equals(typeof(UICommandInvokedHandler)) &&
                        text.GetType().Equals(typeof(string)))
                    {
                        // Already described
                        UICommand button = new UICommand(text, handler);

                        // Already described
                        dialog.Commands.Add(button);
                    }
                    else return;
                }

                /* Sets the default command index to the length of the button array.
                 * The first, colored button will become the default button or index.
                 */
                dialog.DefaultCommandIndex = (UInt32)buttons.Length;
            }

            await dialog.ShowAsync();
        }

        private async Task<StorageFile> PickVideoAsync()
        {
            var picker = new FileSavePicker();
            picker.SuggestedStartLocation = PickerLocationId.VideosLibrary;
            picker.SuggestedFileName = "recordedVideo";
            picker.DefaultFileExtension = ".mp4";
            picker.FileTypeChoices.Add("MP4 Video", new List<string> { ".mp4" });

            var file = await picker.PickSaveFileAsync();
            return file;
        }

        private async Task<StorageFile> GetTempFileAsync()
        {
            var folder = ApplicationData.Current.TemporaryFolder;
            var name = DateTime.Now.ToString("yyyyMMdd-HHmm-ss");
            var file = await folder.CreateFileAsync($"{name}.mp4");
            return file;
        }

        private uint EnsureEven(uint number)
        {
            if (number % 2 == 0)
            {
                return number;
            }
            else
            {
                return number + 1;
            }
        }

        private AppSettings GetCurrentSettings()
        {
            var quality = ParseEnumValue<VideoEncodingQuality>((string)QualityComboBox.SelectedItem);
            var frameRate = uint.Parse(((string)FrameRateComboBox.SelectedItem).Replace("fps", ""));
            var useSourceSize = UseCaptureItemSizeCheckBox.IsChecked.Value;

            return new AppSettings { Quality = quality, FrameRate = frameRate, UseSourceSize = useSourceSize };
        }

        private AppSettings GetCachedSettings()
        {
            var localSettings = ApplicationData.Current.LocalSettings;
            var result = new AppSettings
            {
                Quality = VideoEncodingQuality.HD1080p,
                FrameRate = 60,
                UseSourceSize = true
            };
            if (localSettings.Values.TryGetValue(nameof(AppSettings.Quality), out var quality))
            {
                result.Quality = ParseEnumValue<VideoEncodingQuality>((string)quality);
            }
            if (localSettings.Values.TryGetValue(nameof(AppSettings.FrameRate), out var frameRate))
            {
                result.FrameRate = (uint)frameRate;
            }
            if (localSettings.Values.TryGetValue(nameof(AppSettings.UseSourceSize), out var useSourceSize))
            {
                result.UseSourceSize = (bool)useSourceSize;
            }
            return result;
        }

        public void CacheCurrentSettings()
        {
            var settings = GetCurrentSettings();
            CacheSettings(settings);
        }

        private static void CacheSettings(AppSettings settings)
        {
            var localSettings = ApplicationData.Current.LocalSettings;
            localSettings.Values[nameof(AppSettings.Quality)] = settings.Quality.ToString();
            localSettings.Values[nameof(AppSettings.FrameRate)] = settings.FrameRate;
            localSettings.Values[nameof(AppSettings.UseSourceSize)] = settings.UseSourceSize;
        }

        private static T ParseEnumValue<T>(string input)
        {
            return (T)Enum.Parse(typeof(T), input, false);
        }

        private string GetMessageForHResult(int hresult)
        {
            switch ((uint)hresult)
            {
                // MF_E_TRANSFORM_TYPE_NOT_SET
                case 0xC00D6D60:
                    return "The combination of options you've chosen are not supported by your hardware.";
                default:
                    return null;
            }
        }

        struct AppSettings
        {
            public VideoEncodingQuality Quality;
            public uint FrameRate;
            public bool UseSourceSize;
        }

        private IDirect3DDevice _device;
        private Encoder _encoder;
    }
}
