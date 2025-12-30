using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlashHSI.Core;
using FlashHSI.Core.Pipelines;
using FlashHSI.Core.Classifiers;
using FlashHSI.Core.Preprocessing;
using FlashHSI.Core.Interfaces;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Text;

namespace FlashHSI.UI.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly HsiPipeline _pipeline;
        private int _modelClassCount = 0;

        [ObservableProperty]
        private double _confidenceThreshold = 0.75;

        [ObservableProperty]
        private double _backgroundThreshold = 3000.0;

        [ObservableProperty]
        private long _totalFrames; // Actually Total Lines

        [ObservableProperty]
        private long _unknownCount; // Unclassified (Low Confidence)

        [ObservableProperty]
        private long _backgroundCount; // Skipped (Below Threshold)

        [ObservableProperty]
        private double _fps;

        [ObservableProperty]
        private string _statusMessage = "Ready";

        [ObservableProperty]
        private bool _isSimulating;

        [ObservableProperty]
        private double _targetFps = 100.0;

        // Dynamic Class List
        public ObservableCollection<ClassInfo> ClassStats { get; } = new();

        public MainViewModel()
        {
            _pipeline = new HsiPipeline();
        }

        partial void OnConfidenceThresholdChanged(double value)
        {
            if (_pipeline != null)
            {
                _pipeline.SetThreshold(value);
            }
        }

        private enum MaskMode { Mean, BandPixel }
        private MaskMode _currentMaskMode = MaskMode.Mean;
        private int _maskBandIndex = 0;

        [RelayCommand]
        public void LoadModel()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Filter = "Model JSON (**.json)|*.json|All files (*.*)|*.*";
            if (dlg.ShowDialog() == true)
            {
                LoadModelFromFile(dlg.FileName);
            }
        }

        private void LoadModelFromFile(string path)
        {
            try
            {
                StatusMessage = "Loading Model...";
                string json = File.ReadAllText(path);
                var config = JsonConvert.DeserializeObject<ModelConfig>(json);

                if (config == null) throw new Exception("Failed to deserialize model config.");

                // 1. Setup UI Stats
                ClassStats.Clear();
                _modelClassCount = config.Weights.Count;

                var sortedKeys = config.Labels.Keys.OrderBy(k => int.Parse(k)).ToList();

                foreach (var key in sortedKeys)
                {
                    if (int.TryParse(key, out int index))
                    {
                        var name = config.Labels.ContainsKey(key) ? config.Labels[key] : $"Class {index}";
                        var color = config.Colors.ContainsKey(key) ? config.Colors[key] : "#888888";
                        ClassStats.Add(new ClassInfo { Index = index, Name = name, ColorHex = color });
                    }
                }

                while (ClassStats.Count < _modelClassCount)
                {
                    ClassStats.Add(new ClassInfo { Index = ClassStats.Count, Name = $"Class {ClassStats.Count}", ColorHex = "#888888" });
                }

                // 2. Parse Preprocessing & Masking
                // "MaskRules": "Mean", "Threshold": "35000.0"
                // OR "MaskRules": "b80 > 33000" (Complex)

                string rules = config.Preprocessing.MaskRules ?? "Mean";
                string threshStr = config.Preprocessing.Threshold ?? "0";

                // Default to Mean
                _currentMaskMode = MaskMode.Mean;
                double parsedThresh = 0;
                double.TryParse(threshStr, out parsedThresh);
                BackgroundThreshold = parsedThresh;

                // Check for Complex Rule "b80 > 33000" inside MaskRules
                // Accessing internal band logic
                // Check for Complex Rule "b80 > 33000" inside MaskRules
                // Accessing internal band logic
                var match = System.Text.RegularExpressions.Regex.Match(rules, @"[bB](\d+)\s*[>]\s*([0-9.]+)");
                if (match.Success)
                {
                    if (int.TryParse(match.Groups[1].Value, out int bIdx) && double.TryParse(match.Groups[2].Value, out double bThresh))
                    {
                        _currentMaskMode = MaskMode.BandPixel;
                        _maskBandIndex = bIdx;
                        BackgroundThreshold = bThresh;
                        StatusMessage = $"Mask Rule: Band {bIdx} > {bThresh}";
                    }
                }
                else if (rules.Contains(">"))
                {
                    // Fallback/Error log
                    StatusMessage = $"⚠️ Mask Parsing Failed for: '{rules}'";
                }

                // 3. Setup Core Components
                // 3. Setup Core Components
                var classifier = new LinearClassifier();
                classifier.Load(config);

                int gap = config.Preprocessing.Gap;
                IFeatureExtractor extractor;

                if (config.Preprocessing.ApplyAbsorbance)
                {
                    extractor = new LogGapFeatureExtractor(gap);
                }
                else
                {
                    extractor = new RawGapFeatureExtractor(gap);
                }

                _pipeline.SetClassifier(classifier);
                _pipeline.SetFeatureExtractor(extractor);

                // Add Preprocessors
                // Note: Order matters. Usually SNV -> MinMax -> L2 if multiple selected.
                if (config.Preprocessing.ApplySNV) _pipeline.AddProcessor(new SnvProcessor());
                if (config.Preprocessing.ApplyMinMax) _pipeline.AddProcessor(new MinMaxProcessor());
                if (config.Preprocessing.ApplyL2) _pipeline.AddProcessor(new L2NormalizeProcessor());

                _pipeline.Configure(rawBandCount: 200, selectedBands: config.SelectedBands);

                _lastConfig = config;

                string modeInfo = _currentMaskMode == MaskMode.BandPixel ? $"Band{_maskBandIndex}>{BackgroundThreshold}" : $"Mean>{BackgroundThreshold}";
                StatusMessage = $"Model Loaded: {Path.GetFileName(path)} ({_modelClassCount} Classes, Mode: {config.Preprocessing.Mode}, Mask: {modeInfo})";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading model: {ex.Message}";
                MessageBox.Show($"Error: {ex.Message}");
            }
        }

        private ModelConfig? _lastConfig;

        [RelayCommand]
        public void ResetStats()
        {
            UnknownCount = 0;
            BackgroundCount = 0;
            TotalFrames = 0;
            Fps = 0;
            foreach (var cls in ClassStats)
            {
                cls.Count = 0;
            }
            StatusMessage = "Statistics Reset.";
        }

        [RelayCommand]
        public void ToggleSimulation()
        {
            if (IsSimulating)
            {
                // Stop logic
                IsSimulating = false;
                StatusMessage = "Simulation Stopping...";
            }
            else
            {
                // Start logic
                if (_lastConfig == null)
                {
                    StatusMessage = "Please load a model first!";
                    return;
                }

                if (string.IsNullOrEmpty(_headerPath) || !File.Exists(_headerPath))
                {
                    StatusMessage = "⚠️ Please select a valid ENVI Header file first.";
                    return;
                }

                if (_modelClassCount == 0)
                {
                    StatusMessage = "⚠️ Please Load a Model first.";
                    return;
                }

                IsSimulating = true;
                StatusMessage = "Simulation Started.";

                // Use a dedicated thread with HIGH priority for Real-Time Determinism
                var simThread = new Thread(RunSimulationLoop)
                {
                    IsBackground = true,
                    Priority = ThreadPriority.AboveNormal, // Industrial Standard: Calculation > UI
                    Name = "SimulationLoop"
                };
                simThread.Start();
            }
        }

        private string _headerPath = "";

        [RelayCommand]
        public void SelectDataFile()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Filter = "ENVI Header (*.hdr)|*.hdr|All files (*.*)|*.*";
            if (dlg.ShowDialog() == true)
            {
                _headerPath = dlg.FileName;
                StatusMessage = $"Data Selected: {Path.GetFileName(_headerPath)}";
            }
        }

        private void RunSimulationLoop()
        {
            var reader = new FlashHSI.Core.IO.EnviReader();
            int bandCount = 200;
            int width = 640;

            try
            {
                reader.Load(_headerPath);
                bandCount = reader.Header.Bands;
                width = reader.Header.Samples;
                StatusMessage = $"Playing: {Path.GetFileName(_headerPath)}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error Loading Data: {ex.Message}";
                IsSimulating = false;
                return;
            }

            ushort[] lineBuffer = new ushort[width * bandCount];
            var frameTimer = Stopwatch.StartNew();
            var loopTimer = Stopwatch.StartNew();
            long frames = 0;
            long lastUiUpdate = 0;

            long pendingLines = 0;
            long pendingUnknown = 0;
            long pendingBackground = 0;
            long[] pendingClassCounts = new long[_modelClassCount];

            unsafe
            {
                while (IsSimulating)
                {
                    long startTick = loopTimer.ElapsedTicks;

                    double currentTargetFps = TargetFps;
                    if (currentTargetFps < 1.0) currentTargetFps = 1.0;
                    double targetFrameTimeMs = 1000.0 / currentTargetFps;

                    bool dataAvailable = reader.ReadNextFrame(lineBuffer);
                    if (!dataAvailable)
                    {
                        reader.Load(_headerPath);
                        dataAvailable = reader.ReadNextFrame(lineBuffer);
                    }

                    if (!dataAvailable) break;

                    fixed (ushort* pLine = lineBuffer)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            ushort* pPixel = pLine + (x * bandCount);
                            bool isBackground = false;

                            // 1. Background Masking
                            if (_currentMaskMode == MaskMode.Mean)
                            {
                                long sum = 0;
                                for (int b = 0; b < bandCount; b++) sum += pPixel[b];
                                double mean = (double)sum / bandCount;
                                if (mean < BackgroundThreshold) isBackground = true;
                            }
                            else if (_currentMaskMode == MaskMode.BandPixel)
                            {
                                // Safe check in case mask index is out of bounds (should correspond to loaded data)
                                if (_maskBandIndex < bandCount)
                                {
                                    if (pPixel[_maskBandIndex] <= BackgroundThreshold) isBackground = true;
                                }
                            }

                            if (isBackground)
                            {
                                pendingBackground++;
                                continue;
                            }

                            // 2. Classification
                            int result = _pipeline.ProcessFrame(rawData: pPixel, length: bandCount);

                            if (result >= 0 && result < _modelClassCount)
                            {
                                pendingClassCounts[result]++;
                            }
                            else
                            {
                                pendingUnknown++;
                            }
                        }
                    }

                    frames++;
                    pendingLines++;

                    // FPS Update
                    if (frameTimer.ElapsedMilliseconds >= 1000)
                    {
                        Fps = frames;
                        frames = 0;
                        frameTimer.Restart();
                        lastUiUpdate = 0;
                    }

                    // UI Update (33ms)
                    if (frameTimer.ElapsedMilliseconds - lastUiUpdate >= 33)
                    {
                        long unk = pendingUnknown;
                        long bg = pendingBackground;
                        long l = pendingLines;
                        long[] countsToUpdate = new long[_modelClassCount];
                        Array.Copy(pendingClassCounts, countsToUpdate, _modelClassCount);

                        pendingUnknown = 0;
                        pendingBackground = 0;
                        pendingLines = 0;
                        Array.Clear(pendingClassCounts, 0, _modelClassCount);

                        // Debug: Capture Center Pixel Mean
                        double debugMean = 0;
                        if (width > 0)
                        {
                            fixed (ushort* pLine = lineBuffer)
                            {
                                int cx = width / 2;
                                ushort* pCenter = pLine + (cx * bandCount);
                                long cSum = 0;
                                for (int b = 0; b < bandCount; b++) cSum += pCenter[b];
                                debugMean = (double)cSum / bandCount;
                            }
                        }

                        Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            UnknownCount += unk;
                            BackgroundCount += bg;
                            TotalFrames += l;

                            for (int i = 0; i < countsToUpdate.Length; i++)
                            {
                                if (i < ClassStats.Count)
                                {
                                    ClassStats[i].Count += countsToUpdate[i];
                                }
                            }
                            // Debug: Capture Center Pixel scores
                            var scores = new double[_modelClassCount];
                            double debugMaskValue = 0;
                            string maskLabel = "Mean";

                            if (_lastConfig != null && width > 0)
                            {
                                fixed (ushort* pLine = lineBuffer)
                                {
                                    int cx = width / 2;
                                    ushort* pCenter = pLine + (cx * bandCount);

                                    // 1. Calculate Mask Value
                                    if (_currentMaskMode == MaskMode.BandPixel && _maskBandIndex < bandCount)
                                    {
                                        debugMaskValue = pCenter[_maskBandIndex];
                                        maskLabel = $"Band {_maskBandIndex}";
                                    }
                                    else
                                    {
                                        long cSum = 0;
                                        for (int b = 0; b < bandCount; b++) cSum += pCenter[b];
                                        debugMaskValue = (double)cSum / bandCount;
                                    }

                                    // 2. Feats (Gap - Target)
                                    var feats = new double[_lastConfig.SelectedBands.Count];
                                    int gap = _lastConfig.Preprocessing.Gap;
                                    for (int f = 0; f < _lastConfig.SelectedBands.Count; f++)
                                    {
                                        int tIdx = _lastConfig.SelectedBands[f];
                                        int gIdx = Math.Max(0, tIdx - gap);
                                        feats[f] = (double)pCenter[gIdx] - (double)pCenter[tIdx];
                                    }

                                    // 3. Scores
                                    for (int c = 0; c < _modelClassCount; c++)
                                    {
                                        double dot = 0;
                                        for (int f = 0; f < _lastConfig.SelectedBands.Count; f++)
                                            dot += feats[f] * _lastConfig.Weights[c][f];
                                        scores[c] = dot + _lastConfig.Bias[c];
                                    }
                                }
                            }

                            StatusMessage = $"Running... {maskLabel}: {debugMaskValue:F0} (Threshold: {BackgroundThreshold:F0}) | PP: {scores[0]:F2} | PE: {scores[1]:F2} | ABS: {scores[2]:F2}";
                        }, System.Windows.Threading.DispatcherPriority.Background); // Priority: Background < Input

                        lastUiUpdate = frameTimer.ElapsedMilliseconds;
                    }

                    // Throttle
                    while (true)
                    {
                        double elapsedMs = (loopTimer.ElapsedTicks - startTick) / (double)Stopwatch.Frequency * 1000.0;
                        if (elapsedMs >= targetFrameTimeMs) break;

                        if (targetFrameTimeMs - elapsedMs > 1.0)
                            Thread.Sleep(1); // Yield CPU
                        else
                            Thread.SpinWait(10);
                    }
                }

                reader.Close();
            }
        }
    }
}
