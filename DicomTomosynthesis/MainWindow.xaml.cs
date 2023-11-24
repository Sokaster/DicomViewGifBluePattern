using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Win32;
using System.Drawing;
using System.IO;
using Dicom.Imaging;
using Dicom.Imaging.Render;
using Dicom;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Drawing.Imaging;
using Microsoft.WindowsAPICodePack.Dialogs;

using Microsoft.Win32.SafeHandles;

namespace DicomTomosynthesis
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    /// 
    class SafeHBitmapHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteObject(IntPtr hObject);

        private SafeHBitmapHandle() : base(true) { }

        public SafeHBitmapHandle(IntPtr preexistingHandle, bool ownsHandle)
            : base(ownsHandle)
        {
            SetHandle(preexistingHandle);
        }

        protected override bool ReleaseHandle()
        {
            return DeleteObject(handle);
        }
    }
    public class ProcessedImage
    {
        public DicomFile DicomFile { get; set; }
        public Bitmap Image { get; set; }
    }
    public partial class MainWindow : Window
    {
        private List<ProcessedImage> processedImages = new List<ProcessedImage>();
        
        private int brightnessLevel = 0;
        private int brightnessStep = 10;
        private int contrastLevel = 0;
        private int contrastStep = 10;
        private bool contrastAdjustment = false;
        private float contrastValue = 1.0f;
        private float brightnessValue = 1.0f;
        private int currentIndex = 0;
        private List<DicomFile> dicomFiles = new List<DicomFile>();
        private DispatcherTimer timer;
        private double animationSpeed = 5; // начальная скорость
        private bool invertImage = false;
        private bool brightnessAdjustment = false;
        private bool grayscale = false;
       
        public MainWindow()
        {
            InitializeComponent();
        }
        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            var openFolderDialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true
            };

            if (openFolderDialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                List<string> files = Directory.GetFiles(openFolderDialog.FileName, "*.dcm").ToList();
                dicomFiles = new List<DicomFile>();

                foreach (var file in files)
                {
                    dicomFiles.Add(await Task.Run(() => DicomFile.Open(file)));
                }

                await StartAnimation(dicomFiles);
            }
        }
        public Bitmap AdjustBrightness(Bitmap image, float brightnessValue)
        {
            float[][] brightnessMatrix = {
        new float[] {brightnessValue, 0, 0, 0, 0},
        new float[] {0, brightnessValue, 0, 0, 0},
        new float[] {0, 0, brightnessValue, 0, 0},
        new float[] {0, 0, 0, 1, 0},
        new float[] {.5f, .5f, .5f, 0, 1}
    };

            var colorMatrix = new ColorMatrix(brightnessMatrix);
            var attributes = new ImageAttributes();

            attributes.SetColorMatrix(colorMatrix);

            var newImage = new Bitmap(image.Width, image.Height);
            using (var g = Graphics.FromImage(newImage))
            {
                g.DrawImage(image, new System.Drawing.Rectangle(0, 0, image.Width, image.Height),
                    0, 0, image.Width, image.Height,
                    GraphicsUnit.Pixel, attributes);
            }

            return newImage;
        }
     

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteObject(IntPtr hObject);
        private void InvertImageButton_Click(object sender, RoutedEventArgs e)
        {
            if (dicomFiles != null && dicomFiles.Count > 0)
            {
                invertImage = !invertImage;
                UpdatePreviewImage();  // update image after inversion
            }
            else
            {
                MessageBox.Show("No loaded DICOM files. Please load the files first.");
            }
        }

        private void GrayscaleButton_Click(object sender, RoutedEventArgs e)
        {
            if (dicomFiles != null && dicomFiles.Count > 0)
            {
                grayscale = !grayscale;
                UpdatePreviewImage();  // update image after greyscale applied
            }
            else
            {
                MessageBox.Show("No loaded DICOM files. Please load the files first.");
            }
        }
        private void IncreaseBrightness_Click(object sender, RoutedEventArgs e)
        {
            brightnessAdjustment = true;
            brightnessValue += 0.1f; // 10% increase
            if (brightnessValue > 3f) brightnessValue = 3f; //cap at 300% brightness
            UpdatePreviewImage();
        }

        private void DecreaseBrightness_Click(object sender, RoutedEventArgs e)
        {
            brightnessAdjustment = true;
            brightnessValue -= 0.1f; // 10% decrease
            if (brightnessValue < 0f) brightnessValue = 0f; //cap at 0% brightness
            UpdatePreviewImage();
        }
        private void IncreaseContrast_Click(object sender, RoutedEventArgs e)
        {
            contrastAdjustment = true;
            contrastValue += 0.1f; //10% increase
            if (contrastValue > 3f) contrastValue = 3f; //cap at 300% contrast
            UpdatePreviewImage();
        }

        private void DecreaseContrast_Click(object sender, RoutedEventArgs e)
        {
            contrastAdjustment = true;
            contrastValue -= 0.1f; //10% decrease
            if (contrastValue < 0f) contrastValue = 0f; //cap at 0% contrast
            UpdatePreviewImage();
        }
        private void PreviousFrameButton_Click(object sender, RoutedEventArgs e)
        {
            currentIndex--;
            if (currentIndex < 0) currentIndex = dicomFiles.Count - 1;
            ReadAndDisplayDicomFile(dicomFiles[currentIndex]);
        }


        private void NextFrameButton_Click(object sender, RoutedEventArgs e)
        {
            currentIndex++;
            if (currentIndex >= dicomFiles.Count) currentIndex = 0;
            ReadAndDisplayDicomFile(dicomFiles[currentIndex]);
        }

        private void PauseContinueButton_Click(object sender, RoutedEventArgs e)
        {
            if (timer.IsEnabled)
            {
                timer.Stop();
                PauseContinueButton.Content = "Start";
            }
            else
            {
                timer.Start();
                PauseContinueButton.Content = "Stop";
            }
        }

        private void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            animationSpeed = SpeedSlider.Value;

            if (timer != null)
            {
                timer.Interval = TimeSpan.FromMilliseconds(1000 / animationSpeed);
            }
        }


        private async Task StartAnimation(List<DicomFile> dicomFiles)
        {
            // New implementation to process images before rendering
            List<Task<ProcessedImage>> processedImagesTasks = dicomFiles.Select(file => Task.Run(() =>
                new ProcessedImage()
                {
                    DicomFile = file,
                    Image = ApplyImageFilters(file),  // Apply filters and store processed images in memory
                })).ToList();

            processedImages = new List<ProcessedImage>();
            foreach (var task in processedImagesTasks)
            {
                processedImages.Add(await task);
            }

            currentIndex = 0;

            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(1000 / animationSpeed);
            timer.Tick += Timer_Tick;
            timer.Start();
        }

        public Bitmap ApplyImageFilters(DicomFile dicomFile)
        {
            var dicomImage = new DicomImage(dicomFile.Dataset);
            var bitmap = dicomImage.RenderImage(0).AsClonedBitmap();

            if (invertImage)
                bitmap = InvertImageColors(bitmap);
            if (grayscale)
                bitmap = ConvertToGrayScale(bitmap);

            float brightnessValue = 1.2f;

            if (brightnessAdjustment)
                bitmap = AdjustBrightness(bitmap, brightnessValue);
            if (contrastAdjustment)
                AdjustContrast(bitmap, contrastValue);

            return bitmap;
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (currentIndex >= processedImages.Count)
            {
                currentIndex = 0;
            }

            var processedImage = processedImages[currentIndex];
            Bitmap currentBitmap = processedImage.Image;  // Use the already filtered image
            SetImageSource(currentBitmap);
            currentIndex++;

            if (currentIndex >= processedImages.Count)
            {
                currentIndex = 0;
            }
        }

        // Need to be called from UI thread
        public void SetImageSource(Bitmap bmp, string targetImageName = "DicomImage")
        {
            using (var hBitmap = new SafeHBitmapHandle(bmp.GetHbitmap(), true))
            {
                var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                                    hBitmap.DangerousGetHandle(),
                                    IntPtr.Zero,
                                    Int32Rect.Empty,
                                    BitmapSizeOptions.FromEmptyOptions());

                System.Windows.Controls.Image myImage = (System.Windows.Controls.Image)FindName(targetImageName);
                myImage.Source = bitmapSource;
            }
        }
        private Bitmap InvertImageColors(Bitmap bmp)
        {
            for (var x = 0; x < bmp.Width; x++)
            {
                for (var y = 0; y < bmp.Height; y++)
                {
                    var pixel = bmp.GetPixel(x, y);
                    pixel = System.Drawing.Color.FromArgb(pixel.A, 255 - pixel.R, 255 - pixel.G, 255 - pixel.B);
                    bmp.SetPixel(x, y, pixel);
                }
            }
            return bmp;
        }

        private Bitmap ConvertToGrayScale(Bitmap bmp)
        {
            for (var x = 0; x < bmp.Width; x++)
            {
                for (var y = 0; y < bmp.Height; y++)
                {
                    var pixel = bmp.GetPixel(x, y);
                    var grayScale = (int)((pixel.R * 0.3) + (pixel.G * 0.59) + (pixel.B * 0.11));
                    pixel = System.Drawing.Color.FromArgb(pixel.A, grayScale, grayScale, grayScale);
                    bmp.SetPixel(x, y, pixel);
                }
            }
            return bmp;
        }

        private void SaveAsGifButton_Click(object sender, RoutedEventArgs e)
        {
            if (dicomFiles == null || dicomFiles.Count == 0)
            {
                MessageBox.Show("No DICOM files to save. Please open a folder first.");
                return;
            }

            var saveFileDialog = new SaveFileDialog();
            saveFileDialog.Filter = "Gif Image|*.gif";
            saveFileDialog.Title = "Save the animation as Gif";
            saveFileDialog.ShowDialog();

            if (saveFileDialog.FileName != "")
            {
                using (FileStream fs = new FileStream(saveFileDialog.FileName, FileMode.Create))
                {
                    GenerateGifFromDicom().Save(fs);
                }

                // Call the update preview method after creating the gif.
                UpdatePreviewImage();
            }
        }
        private Bitmap ApplyFiltersToDicomImage(DicomFile dicomFile)
        {
            var dicomImage = new DicomImage(dicomFile.Dataset);
            var bitmap = dicomImage.RenderImage(0).AsClonedBitmap();

            if (invertImage)
                bitmap = InvertImageColors(bitmap);
            if (grayscale)
                bitmap = ConvertToGrayScale(bitmap);

            return bitmap;
        }

        private BitmapFrame CreateGifFrameFromBitmap(Bitmap bitmap)
        {
            var hBitmap = bitmap.GetHbitmap();
            BitmapSource bitmapSource = null;

            try
            {
                bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap, IntPtr.Zero, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                return BitmapFrame.Create(bitmapSource);
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }


        private GifBitmapEncoder GenerateGifFromDicom()
        {
            var gifEncoder = new GifBitmapEncoder();

            foreach (ProcessedImage processedImage in processedImages)
            {
                var gifFrame = CreateGifFrameFromBitmap(processedImage.Image);
                gifEncoder.Frames.Add(gifFrame);
            }

            return gifEncoder;
        }

        private void SpeedSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            animationSpeed = SpeedSlider.Value;
            if (timer != null)
            {
                timer.Interval = TimeSpan.FromMilliseconds(1000 / animationSpeed);
            }
        }

        private void InvertImageTogButton_Checked(object sender, RoutedEventArgs e)
        {
            if (dicomFiles != null && dicomFiles.Count > 0)
            {
                invertImage = true;
                UpdatePreviewImage();
            }
        }

        private void InvertImageTogButton_Unchecked(object sender, RoutedEventArgs e)
        {
            if (dicomFiles != null && dicomFiles.Count > 0)
            {
                invertImage = false;
                UpdatePreviewImage();
            }
        }

        private void GrayscaleTogButton_Checked(object sender, RoutedEventArgs e)
        {
            if (dicomFiles != null && dicomFiles.Count > 0)
            {
                grayscale = true;
                UpdatePreviewImage();
            }
        }

        private void GrayscaleTogButton_Unchecked(object sender, RoutedEventArgs e)
        {
            if (dicomFiles != null && dicomFiles.Count > 0)
            {
                grayscale = false;
                UpdatePreviewImage();
            }
        }

        private void UpdatePreviewImage()
        {
            if (processedImages != null && currentIndex >= 0 && currentIndex < processedImages.Count)
            {
                Bitmap newBmp = processedImages[currentIndex].Image;
                SetImageSource(newBmp, "DicomImage");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Error: Problem with currentIndex or dicomFiles array.");
            }
        }
        public void ReadAndDisplayDicomFile(DicomFile dicomFile)
        {
            var dicomImage = new DicomImage(dicomFile.Dataset);
            System.Drawing.Bitmap bmp = dicomImage.RenderImage(0).AsClonedBitmap();
            if (invertImage)
                bmp = InvertImageColors(bmp);
            if (grayscale)
                bmp = ConvertToGrayScale(bmp);
            // Convert bitmap to bitmapsource
            var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                                    bmp.GetHbitmap(),
                                    IntPtr.Zero,
                                    Int32Rect.Empty,
                                    BitmapSizeOptions.FromEmptyOptions());

            // set the image control source to display the image
            System.Windows.Controls.Image myImage = (System.Windows.Controls.Image)FindName("DicomImage");
            myImage.Source = bitmapSource;
        }
        private void RemoveFiltersButton_Click(object sender, RoutedEventArgs e)
        {
            if (dicomFiles != null && dicomFiles.Count > 0)
            {
                invertImage = false;
                grayscale = false;
                brightnessAdjustment = false;
                brightnessValue = 1f;
                contrastAdjustment = false;
                contrastValue = 1f;
                UpdatePreviewImage();
            }
            else
            {
                MessageBox.Show("No loaded DICOM files. Please load the files first.");
            }
        }
        public Bitmap AdjustContrast(Bitmap image, float contrastValue)
        {
            float adjustedContrast = contrastValue / 127;

            float[][] contrastMatrix = {
        new float[] {adjustedContrast, 0, 0, 0, 0},
        new float[] {0, adjustedContrast, 0, 0, 0},
        new float[] {0, 0, adjustedContrast, 0, 0},
        new float[] {0, 0, 0, 1, 0},
        new float[] {.001f, .001f, .001f, 0, 1}};

            var colorMatrix = new ColorMatrix(contrastMatrix);
            var attributes = new ImageAttributes();

            attributes.SetColorMatrix(colorMatrix);

            var newImage = new Bitmap(image.Width, image.Height);
            using (var g = Graphics.FromImage(newImage))
            {
                g.DrawImage(image, new System.Drawing.Rectangle(0, 0, image.Width, image.Height),
                    0, 0, image.Width, image.Height,
                    GraphicsUnit.Pixel, attributes);
            }

            return newImage;

        }

    }
}
