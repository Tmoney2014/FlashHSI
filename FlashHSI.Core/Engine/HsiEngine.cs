using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using FlashHSI.Core.Analysis;
using FlashHSI.Core.Control;
using FlashHSI.Core.IO;
using FlashHSI.Core.Masking;
using FlashHSI.Core.Pipelines;
using FlashHSI.Core.Settings;


namespace FlashHSI.Core.Engine
{
    public class HsiEngine
    {
        // Components
        private HsiPipeline _pipeline;
        private BlobTracker? _blobTracker;
        private EjectionService? _ejectionService;
        private ModelConfig? _currentConfig;

        // Threading
        private Thread? _simThread;
        private volatile bool _isRunning;
        private double _targetFps = 700.0;

        // State
        private string _headerPath = "";
        private double[]? _whiteRef;
        private double[]? _darkRef;
        private MaskRule? _maskRule;
        private MaskMode _maskMode = MaskMode.Mean;
        private int _maskBandIndex;
        private bool _maskOperatorLess;
        private double _backgroundThreshold = 1000.0;
        
        // Events
        public event Action<EngineStats>? StatsUpdated;
        public event Action<string>? LogMessage;
        public event Action<bool>? SimulationStateChanged;
        public event Action<int[], int>? FrameProcessed;
        public event Action<EjectionLogItem>? EjectionOccurred;

        public bool IsSimulating => _isRunning;
        
        public HsiEngine()
        {
            _pipeline = new HsiPipeline();
        }

        public void LoadModel(string jsonPath)
        {
            try
            {
                string json = File.ReadAllText(jsonPath);
                var config = Newtonsoft.Json.JsonConvert.DeserializeObject<ModelConfig>(json);
                if (config == null) throw new Exception("Failed to deserialize model config");

                _currentConfig = config;
                _pipeline.LoadModel(config, Path.GetDirectoryName(jsonPath));

                // Initialize Components
                _blobTracker = new BlobTracker(config.Weights.Count);
                _ejectionService = new EjectionService();
                _ejectionService.OnEjectionSignal += (item) => EjectionOccurred?.Invoke(item);

                LogMessage?.Invoke($"Model Loaded: {config.ModelType}");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Error Loading Model: {ex.Message}");
                throw;
            }
        }

        public void SetReferences(double[]? white, double[]? dark)
        {
            _whiteRef = white;
            _darkRef = dark;
        }

        public double[]? LoadReference(string path, bool isDark)
        {
            try 
            {
                var reader = new EnviReader();
                reader.Load(path);
                
                int bands = reader.Header.Bands; 
                int width = reader.Header.Samples; 
                long[] sum = new long[bands]; 
                
                ushort[] buf = new ushort[width * bands];
                int linesToRead = Math.Min(50, reader.Header.Lines); // Average first 50 lines
                int count = 0;

                for(int i=0; i < linesToRead; i++) 
                {
                    if(!reader.ReadNextFrame(buf)) break;
                    
                    for(int x=0; x<width; x++) 
                    {
                        int baseIdx = x * bands; 
                        for(int b=0; b<bands; b++) sum[b] += buf[baseIdx+b];
                    }
                    count += width;
                }
                reader.Close();

                if(count > 0) 
                { 
                    double[] avg = new double[bands]; 
                    for(int b=0; b<bands; b++) avg[b] = (double)sum[b]/count; 
                    
                    if (isDark) _darkRef = avg;
                    else _whiteRef = avg;

                    return avg; 
                }
            } 
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Error loading reference: {ex.Message}");
            }
            return null;
        }

        public void SetMaskSettings(MaskMode mode, MaskRule? rule, int bandIndex, bool lessThan, double threshold)
        {
            _maskMode = mode;
            _maskRule = rule;
            _maskBandIndex = bandIndex;
            _maskOperatorLess = lessThan;
            _backgroundThreshold = threshold;
        }

        public void SetTargetFps(double fps) => _targetFps = fps;

        public void SetConfidenceThreshold(double threshold)
        {
            _pipeline.SetThreshold(threshold);
        }

        public void StartSimulation(string headerPath)
        {
            if (_isRunning) return;

            _headerPath = headerPath;
            if (!File.Exists(_headerPath))
            {
                LogMessage?.Invoke("File not found: " + headerPath);
                return;
            }

            _isRunning = true;
            SimulationStateChanged?.Invoke(true);

            _simThread = new Thread(RunLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
                Name = "HsiEngineLoop"
            };
            _simThread.Start();
        }

        public void Stop()
        {
            _isRunning = false;
            _simThread?.Join(500);
            SimulationStateChanged?.Invoke(false);
        }

        private void RunLoop()
        {
            var reader = new EnviReader();
            int bandCount = 0;
            int width = 0;

            try
            {
                reader.Load(_headerPath);
                bandCount = reader.Header.Bands;
                width = reader.Header.Samples;

                // Re-configure Pipeline
                if (_currentConfig != null)
                {
                    _pipeline.Configure(bandCount, _currentConfig.SelectedBands);
                }
                
                LogMessage?.Invoke($"Playing: {Path.GetFileName(_headerPath)}");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Error: {ex.Message}");
                _isRunning = false;
                SimulationStateChanged?.Invoke(false);
                return;
            }

            // Buffers
            ushort[] lineBuffer = new ushort[width * bandCount];
            int[] classificationRow = new int[width];
            long[] currentClassCounts = new long[_currentConfig?.Weights.Count ?? 32];

            // Timing
            var frameTimer = Stopwatch.StartNew();
            var loopTimer = Stopwatch.StartNew();
            long frames = 0;
            long lastUiUpdate = 0;
            long linesSinceLastUpdate = 0;

            // Stats
            var stats = new EngineStats { ClassCounts = new long[currentClassCounts.Length] };

            ActiveBlob.ResetCounter();

            unsafe
            {
                bool useMaskRule = (_maskMode == MaskMode.MaskRule && _maskRule != null);
                bool useBandPixel = (_maskMode == MaskMode.BandPixel && _maskBandIndex < bandCount);
                bool useMean = (_maskMode == MaskMode.Mean);
                bool useAbsorbanceForMask = (_pipeline.GetExtractorType() == typeof(FlashHSI.Core.Preprocessing.LogGapFeatureExtractor));

                int globalLineIndex = 0;

                while (_isRunning)
                {
                    long startTick = loopTimer.ElapsedTicks;
                    double fps = _targetFps < 1.0 ? 1.0 : _targetFps;
                    double targetFrameTimeMs = 1000.0 / fps;

                    // IO
                    if (!reader.ReadNextFrame(lineBuffer))
                    {
                        reader.Load(_headerPath); // Restart loop
                        // ActiveBlob.ResetCounter(); // Optional: Reset IDs on loop?
                        if (!reader.ReadNextFrame(lineBuffer)) break;
                    }

                    // Processing
                    fixed (ushort* pLine = lineBuffer)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            ushort* pPixel = pLine + (x * bandCount);
                            bool isBackground = false;

                            // 1. Masking
                            if (useMean)
                            {
                                long sum = 0;
                                for (int b = 0; b < bandCount; b++) sum += pPixel[b];
                                if (((double)sum / bandCount) < _backgroundThreshold) isBackground = true;
                            }
                            else if (useMaskRule)
                            {
                                if (!_maskRule!.Evaluate(pPixel, _whiteRef, _darkRef, useAbsorbanceForMask)) isBackground = true;
                            }
                            else if (useBandPixel)
                            {
                                double val = pPixel[_maskBandIndex];
                                if (_maskOperatorLess) { if (val < _backgroundThreshold) isBackground = true; }
                                else { if (val > _backgroundThreshold) isBackground = true; }
                            }

                            if (isBackground)
                            {
                                classificationRow[x] = -1;
                                stats.Background++;
                            }
                            else
                            {
                                int cls = _pipeline.ProcessFrame(pPixel, bandCount);
                                classificationRow[x] = cls;
                                if (cls < 0) stats.Unknown++;
                            }
                        }
                    }

                    // Notify Visualization (Synchronous to avoid allocation, subscriber must be fast)
                    FrameProcessed?.Invoke(classificationRow, width);

                    // 2. Tracking
                    if (_blobTracker != null)
                    {
                       var closedBlobs = _blobTracker.ProcessLine(globalLineIndex, classificationRow);
                       foreach (var blob in closedBlobs)
                       {
                           int bestClass = blob.GetBestClass();
                           if (bestClass >= 0)
                           {
                               _ejectionService?.Process(blob);
                               stats.Objects++;
                               if (bestClass < currentClassCounts.Length) currentClassCounts[bestClass]++;
                           }
                       }
                    }


                    frames++;
                    globalLineIndex++;
                    linesSinceLastUpdate++;

                    // FPS Calculation
                    if (frameTimer.ElapsedMilliseconds >= 1000)
                    {
                        stats.Fps = frames;
                        frames = 0;
                        frameTimer.Restart();
                        lastUiUpdate = 0;
                    }

                    // UI Notification (Throttle 30fps)
                    if (frameTimer.ElapsedMilliseconds - lastUiUpdate >= 33)
                    {
                        // Copy counts safely
                        Array.Copy(currentClassCounts, stats.ClassCounts, currentClassCounts.Length);
                        Array.Clear(currentClassCounts, 0, currentClassCounts.Length); // Reset per-update counts? Or accum?
                        // Wait, UI expects ACCUMULATED counts or DELTA?
                        // MainViewModel logic was adding delta. Let's send DELTA.
                        
                        StatsUpdated?.Invoke(stats); // Send struct copy
                        
                        // Reset delta counters
                        stats.Unknown = 0;
                        stats.Background = 0;
                        stats.Objects = 0;
                        stats.ProcessedLines = linesSinceLastUpdate;
                        linesSinceLastUpdate = 0;

                        Array.Clear(stats.ClassCounts, 0, stats.ClassCounts.Length); // Clear struct buffer for next batch
                        
                        lastUiUpdate = frameTimer.ElapsedMilliseconds;
                    }

                    // Sleep
                    while (true)
                    {
                        double elapsedMs = (loopTimer.ElapsedTicks - startTick) / (double)Stopwatch.Frequency * 1000.0;
                        if (elapsedMs >= targetFrameTimeMs) break;
                        if (targetFrameTimeMs - elapsedMs > 1.0) Thread.Sleep(1); else Thread.SpinWait(10);
                    }
                }

                reader.Close();
            }
        }
    }

    public class EngineStats
    {
        public long Unknown;
        public long Background;
        public long Objects;
        public long Fps;
        public long[] ClassCounts = Array.Empty<long>(); // Will be initialized in Engine
        public long ProcessedLines;
        
        public EngineStats Copy()
        {
            return (EngineStats)this.MemberwiseClone();
        }
    }

    public enum MaskMode { Mean, BandPixel, MaskRule }
}
