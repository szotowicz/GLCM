using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Accord.Imaging;
using Microsoft.Win32;
using System.IO;
using System.Windows.Threading;

namespace GLCM
{
    public partial class MainWindow : Window
    {
        private string SourceImagePath { get; set; }
        private double SourceImageWidth { get; set; }
        private double SourceImageHeight { get; set; }
        private int StepXImage { get; set; }
        private int StepYImage { get; set; }
        private string Degree { get; set; }
        private int Distance { get; set; }

        private readonly Brush[] colorBrushes = {
            Brushes.Blue,
            Brushes.LightSkyBlue,
            Brushes.Green,
            Brushes.GreenYellow,
            Brushes.Khaki,
            Brushes.Yellow,
            Brushes.Orange,
            Brushes.OrangeRed
        };
        private readonly BackgroundWorker GlcmBackgroundWorker;
        private readonly BackgroundWorker HeatmapsBackgroundWorker;

        private OrderedDictionary EntropyValues { get; set; }
        private OrderedDictionary EnergyValues { get; set; }
        private OrderedDictionary CorrelationValues { get; set; }
        private OrderedDictionary ContrastValues { get; set; }

        public MainWindow()
        {
            InitializeComponent();

            GlcmBackgroundWorker = new BackgroundWorker
            {
                WorkerReportsProgress = true
            };
            GlcmBackgroundWorker.DoWork += OnGlcmBackgroundDoWork;
            GlcmBackgroundWorker.ProgressChanged += BackgroundProgressChanged;
            GlcmBackgroundWorker.RunWorkerCompleted += OnGlcmBackgroundWorkerCompleted;

            HeatmapsBackgroundWorker = new BackgroundWorker
            {
                WorkerReportsProgress = true
            };
            HeatmapsBackgroundWorker.DoWork += OnHeatmapsBackgroundDoWork;
            HeatmapsBackgroundWorker.ProgressChanged += BackgroundProgressChanged;
            HeatmapsBackgroundWorker.RunWorkerCompleted += OnHeatmapsBackgroundWorkerCompleted;
        }

        private void ReadInputParameters()
        {
            this.Degree = ((ComboBoxItem)degreeComboBox.SelectedItem).Tag.ToString();
            this.Distance = int.Parse(distanceTextBox.Text);
            this.StepXImage = int.Parse(StepXTextBox.Text);
            this.StepYImage = int.Parse(StepYTextBox.Text);
        }

        private void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            var op = new OpenFileDialog
            {
                Title = "Select image",
                Filter = "Image files (*.png;*.jpeg;*.jpg;*.bmp)|*.png;*.jpeg;*.jpg;*.bmp"
            };

            if (op.ShowDialog() == true)
            {
                BitmapImage image = new BitmapImage(new Uri(op.FileName));
                ImageSource.Source = image;
                SourceImagePath = op.FileName;
                SourceImageHeight = image.PixelHeight;
                SourceImageWidth = image.PixelWidth;
                if (!string.IsNullOrWhiteSpace(SourceImagePath))
                {
                    GenerateHeatmapsButton.IsEnabled = true;
                    StatusTextBlock.Text = "Status: Ready to start";
                }
            }
        }

        private void GenerateHeatmapsButton_Click(object sender, RoutedEventArgs e)
        {
            ReadInputParameters();
            GenerateHeatmapsButton.IsEnabled = false;
            StatusTextBlock.Text = "Status: Calculating GLCM matrix...";
            GlcmBackgroundWorker.RunWorkerAsync();
        }


        private BitmapImage BitmapToImageSource(Bitmap bitmap)
        {
            using (MemoryStream memory = new MemoryStream())
            {
                bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Bmp);
                memory.Position = 0;
                BitmapImage bitmapimage = new BitmapImage();
                bitmapimage.BeginInit();
                bitmapimage.StreamSource = memory;
                bitmapimage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapimage.EndInit();

                return bitmapimage;
            }
        }

        public static Bitmap MakeGrayscale3(Bitmap original)
        {
            Bitmap newBitmap = new Bitmap(original.Width, original.Height);
            Graphics graphics = Graphics.FromImage(newBitmap);

            ColorMatrix colorMatrix = new ColorMatrix(
               new[]
               {
                   new float[] {.3f, .3f, .3f, 0, 0},
                   new float[] {.59f, .59f, .59f, 0, 0},
                   new float[] {.11f, .11f, .11f, 0, 0},
                   new float[] {0, 0, 0, 1, 0},
                   new float[] {0, 0, 0, 0, 1}
               });

            ImageAttributes attributes = new ImageAttributes();
            attributes.SetColorMatrix(colorMatrix);
            graphics.DrawImage(original, new Rectangle(0, 0, original.Width, original.Height), 0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);
            graphics.Dispose();

            return newBitmap;
        }

        private double[,] CalculateGLCM(Bitmap bitmap)
        {
            using (var unmanagedImage = UnmanagedImage.FromManagedImage(bitmap))
            {
                GrayLevelCooccurrenceMatrix glcm = new GrayLevelCooccurrenceMatrix
                {
                    Normalize = true,
                    Distance = this.Distance,
                    Degree = (CooccurrenceDegree)Enum.Parse(typeof(CooccurrenceDegree), Degree)
                };

                return glcm.Compute(unmanagedImage);
            }
        }


        /// BACKGROUNDS WORKERS ///
        private void InvokeAction(Action action)
        {
            if (Dispatcher.CheckAccess())
            {
                action.Invoke();
            }
            else
            {
                Dispatcher.Invoke(DispatcherPriority.Background, action);
            }
        }

        private void UpdateParametersOfSelectedImage(HaralickDescriptor haralick)
        {
            InvokeAction(() =>
            {
                entropyLabel.Content = string.Format("Entropy: {0}", haralick.Entropy.ToString("N5"));
                energyLabel.Content = string.Format("Energy: {0}", haralick.AngularSecondMomentum.ToString("N5"));
                correlationLabel.Content = string.Format("Correlation: {0}", haralick.Correlation.ToString("N5"));
                contrast.Content = string.Format("Contrast: {0}", haralick.Contrast.ToString("N5"));
            });
        }

        private void GenerateHeatmap(OrderedDictionary orderedDictionary, System.Windows.Controls.Image imageControl)
        {
            Bitmap heatMap = new Bitmap((int)SourceImageWidth, (int)SourceImageHeight);

            var heatmapsValues = orderedDictionary.Values.Cast<double>();
            double maxValue = heatmapsValues.Max();
            double pivot = maxValue / (colorBrushes.Length - 1);

            using (Graphics graphics = Graphics.FromImage(heatMap))
            {
                var enumerator = orderedDictionary.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    var enumKey = enumerator.Key as Tuple<int, int, int, int>;
                    int brushIndex = Convert.ToInt32((double)enumerator.Value / pivot);
                    if (brushIndex > colorBrushes.Length -1)
                    {
                        brushIndex = colorBrushes.Length - 1;
                    }
                    graphics.FillRectangle(colorBrushes[brushIndex], enumKey.Item1, enumKey.Item2, enumKey.Item3, enumKey.Item4);
                }
            }

            InvokeAction(() =>
            {
                imageControl.Source = BitmapToImageSource(heatMap);
            });
        }

        private void ResetBackgroundWorker()
        {
            ProgressBar.Value = 0;
            GenerateHeatmapsButton.IsEnabled = true;
        }

        private void BackgroundProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            ProgressBar.Value = e.ProgressPercentage;
        }

        private void OnGlcmBackgroundDoWork(object sender, DoWorkEventArgs e)
        {
            using (var sourceBitmap = MakeGrayscale3((Bitmap)Bitmap.FromFile(SourceImagePath)))
            {
                int imgWidth = sourceBitmap.Width;
                int imgHeight = sourceBitmap.Height;
                int stepX = StepXImage;
                int stepY = StepYImage;

                int iterationCounter = 1;
                double iterations = Math.Ceiling((float)imgWidth / stepX) * Math.Ceiling((float)imgHeight / stepY);

                UpdateParametersOfSelectedImage(new HaralickDescriptor(CalculateGLCM(sourceBitmap)));

                EntropyValues = new OrderedDictionary();
                EnergyValues = new OrderedDictionary();
                CorrelationValues = new OrderedDictionary();
                ContrastValues = new OrderedDictionary();

                for (var y = 0; y < imgHeight; y += stepY)
                {
                    for (var x = 0; x < imgWidth; x += stepX)
                    {
                        var lenX = x + stepX;
                        if (lenX > imgWidth)
                        {
                            lenX = imgWidth;
                        }

                        var lenY = y + stepY;
                        if (lenY > imgHeight)
                        {
                            lenY = imgHeight;
                        }

                        using (var currentTile = new Bitmap(stepX, stepY))
                        {
                            currentTile.SetResolution(sourceBitmap.HorizontalResolution, sourceBitmap.VerticalResolution);

                            using (var currentTileGraphics = Graphics.FromImage(currentTile))
                            {
                                currentTileGraphics.Clear(Color.Black);
                                var absentRectangleArea = new Rectangle(x, y, lenX, lenY);
                                currentTileGraphics.DrawImage(sourceBitmap, 0, 0, absentRectangleArea, GraphicsUnit.Pixel);

                                var haralick = new HaralickDescriptor(CalculateGLCM(currentTile));
                                EntropyValues.Add(new Tuple<int, int, int, int>(x, y, lenX, lenY), haralick.Entropy);
                                EnergyValues.Add(new Tuple<int, int, int, int>(x, y, lenX, lenY), haralick.AngularSecondMomentum);
                                CorrelationValues.Add(new Tuple<int, int, int, int>(x, y, lenX, lenY), haralick.Correlation);
                                ContrastValues.Add(new Tuple<int, int, int, int>(x, y, lenX, lenY), haralick.Contrast);
                            }
                        }

                        GlcmBackgroundWorker.ReportProgress((int)(100 / (iterations) * iterationCounter++));
                    }
                }
            }
        }

        private void OnGlcmBackgroundWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Cancelled)
            {
                MessageBox.Show("BackgroundWorker was cancelled.", "Operation Cancelled", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                ResetBackgroundWorker();
            }
            else if (e.Error != null)
            {
                MessageBox.Show("BackgroundWorker operation failed: \n" + e.Error, "Operation Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                ResetBackgroundWorker();
            }
            else
            {
                HeatmapsBackgroundWorker.RunWorkerAsync();
            }
        }

        private void OnHeatmapsBackgroundDoWork(object sender, DoWorkEventArgs e)
        {
            InvokeAction(() =>
            {
                StatusTextBlock.Text = "Status: Generating Entropy heatmap...";
                EntropyTextBox.Visibility = Visibility.Visible;
            });

            GenerateHeatmap(EntropyValues, EntropyImageResult);
            HeatmapsBackgroundWorker.ReportProgress(25);

            InvokeAction(() =>
            {
                StatusTextBlock.Text = "Status: Generating Energy heatmap...";
                EnergyTextBox.Visibility = Visibility.Visible;
            });
            GenerateHeatmap(EnergyValues, EnergyImageResult);
            HeatmapsBackgroundWorker.ReportProgress(50);

            InvokeAction(() =>
            {
                StatusTextBlock.Text = "Status: Generating Correlation heatmap...";
                CorrelationTextBox.Visibility = Visibility.Visible;
            });
            GenerateHeatmap(CorrelationValues, CorrelationImageResult);
            HeatmapsBackgroundWorker.ReportProgress(75);

            InvokeAction(() =>
            {
                StatusTextBlock.Text = "Status: Generating Contrast heatmap...";
                ContrastTextBox.Visibility = Visibility.Visible;
            });
            GenerateHeatmap(ContrastValues, ContrastImageResult);
            HeatmapsBackgroundWorker.ReportProgress(100);
        }

        private void OnHeatmapsBackgroundWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Cancelled)
            {
                MessageBox.Show("BackgroundWorker was cancelled.", "Operation Cancelled", MessageBoxButton.OK, MessageBoxImage.Exclamation);
            }
            else if (e.Error != null)
            {
                MessageBox.Show("BackgroundWorker operation failed: \n" + e.Error, "Operation Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else
            {
                StatusTextBlock.Text = "Status: Heatmaps are ready";
            }
            ResetBackgroundWorker();
        }
    }
}