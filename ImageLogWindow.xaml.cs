using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace TaskSchedulerApp
{
    public partial class ImageLogWindow : HandyControl.Controls.Window
    {
        private List<string> _imageFiles = new List<string>();
        private int _currentIndex = 0;
        private string _folderPath;

        public ImageLogWindow(string folderPath)
        {
            InitializeComponent();
            _folderPath = folderPath;
            LoadImages();
        }

        private void LoadImages()
        {
            if (!Directory.Exists(_folderPath))
            {
                ShowNoData();
                return;
            }

            _imageFiles = Directory.GetFiles(_folderPath, "*.png")
                                   .Select(f => new FileInfo(f))
                                   .OrderByDescending(f => f.CreationTime)
                                   .Select(f => f.FullName)
                                   .ToList();

            if (_imageFiles.Count > 0)
            {
                _currentIndex = 0;
                DisplayImage();
                TxtNoData.Visibility = Visibility.Collapsed;
                ImgDisplay.Visibility = Visibility.Visible;
            }
            else
            {
                ShowNoData();
            }
        }

        private void ShowNoData()
        {
            TxtNoData.Visibility = Visibility.Visible;
            ImgDisplay.Visibility = Visibility.Collapsed;
            TxtFileName.Text = "-";
            TxtCounter.Text = "0 / 0";
        }

        private void DisplayImage()
        {
            if (_imageFiles.Count == 0) return;

            try
            {
                string path = _imageFiles[_currentIndex];

                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path);
                bitmap.EndInit();
                bitmap.Freeze(); 

                ImgDisplay.Source = bitmap;
                TxtFileName.Text = Path.GetFileName(path);
                TxtCounter.Text = $"{_currentIndex + 1} / {_imageFiles.Count}";
            }
            catch (Exception ex)
            {
                HandyControl.Controls.MessageBox.Error($"图片加载失败: {ex.Message}");
            }
        }

        private void Prev_Click(object sender, RoutedEventArgs e)
        {
            if (_imageFiles.Count == 0) return;
            _currentIndex--;
            if (_currentIndex < 0) _currentIndex = _imageFiles.Count - 1;
            DisplayImage();
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (_imageFiles.Count == 0) return;
            _currentIndex++;
            if (_currentIndex >= _imageFiles.Count) _currentIndex = 0; 
            DisplayImage();
        }
    }
}