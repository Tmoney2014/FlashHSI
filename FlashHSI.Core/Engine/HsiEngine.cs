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
using FlashHSI.Core.Control.Camera;


namespace FlashHSI.Core.Engine
{
    public class HsiEngine
    {
        // Components
        private HsiPipeline _pipeline;
        private ICameraService? _cameraService;
        private BlobTracker? _blobTracker;
        private EjectionService? _ejectionService;
        private ModelConfig? _currentConfig;

        // Threading
        private RunMode _runMode = RunMode.Simulation;
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

        // AI가 추가함: Blob 설정값 캐싱 (모델 로드 시 초기화 방지)
        private int _cachedMinPixels = 5;
        private int _cachedLineGap = 5;
        private int _cachedPixelGap = 10;

        // AI가 추가함: 사출 타겟 클래스 캐싱 (모델 재로드 시 유지)
        private HashSet<int>? _cachedEjectionTargets;

        // Live Statistics
        private EngineStats _liveStats;
        private long[] _liveClassCounts = Array.Empty<long>();
        private Stopwatch _liveUiTimer = new Stopwatch();
        private Stopwatch _liveFpsTimer = new Stopwatch();
        private int _liveFrameCount = 0;
        
        // Events
        public event Action<EngineStats>? StatsUpdated;
        public event Action<string>? LogMessage;
        public event Action<bool>? SimulationStateChanged;
        // AI: Changed event signature to include active blobs for advanced visualization (Top Cap)
        public event Action<int[], int, System.Collections.Generic.List<FlashHSI.Core.Analysis.ActiveBlob.BlobSnapshot>>? FrameProcessed;
        public event Action<EjectionLogItem>? EjectionOccurred;

        public bool IsSimulating => _isRunning;
        public bool IsMaskRuleActive => _maskMode == MaskMode.MaskRule;
        
        /// <summary>
        /// AI: Expose Current FPS for Ejection Timing (Live Mode)
        /// </summary>
        public double CurrentFps => _runMode == RunMode.Live ? (_liveStats?.Fps ?? 0) : 0;
        
        // AI가 추가함: 현재 로드된 모델의 OriginalType (UI에서 Confidence 슬라이더 활성화 판단용)
        public string LoadedModelType { get; private set; } = "";
        
        public HsiEngine(ICameraService? cameraService = null)
        {
            _pipeline = new HsiPipeline();
            _cameraService = cameraService;
            
            // AI: LinearClassifier 디버그 로그 연결 (UI 출력용)
            FlashHSI.Core.Classifiers.LinearClassifier.GlobalLog += (msg) => LogMessage?.Invoke(msg);
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
                // AI: 모델 로드 후 캐싱된 설정값 재적용
                _blobTracker.MinPixels = _cachedMinPixels;
                _blobTracker.MaxLineGap = _cachedLineGap;
                _blobTracker.MaxPixelGap = _cachedPixelGap;
                LogMessage?.Invoke($"BlobTracker Settings Restored: MinPx={_cachedMinPixels}, LineGap={_cachedLineGap}, PixelGap={_cachedPixelGap}");

                _ejectionService = new EjectionService();
                _ejectionService.OnEjectionSignal += (item) => EjectionOccurred?.Invoke(item);
                // AI가 수정함: 모델 재로드 시 사출 타겟 유지
                _ejectionService.SetTargetClasses(_cachedEjectionTargets);

                // AI가 추가함: 모델의 MaskRules 자동 적용
                _maskRule = MaskRuleParser.Parse(config.Preprocessing.MaskRules);
                if (_maskRule != null)
                {
                    _maskMode = MaskMode.MaskRule;
                    LogMessage?.Invoke($"MaskRules 적용: {config.Preprocessing.MaskRules}");
                }
                else
                {
                    // MaskRules가 없거나 "Mean"인 경우 Threshold 사용
                    _maskMode = MaskMode.Mean;
                    if (double.TryParse(config.Preprocessing.Threshold, out double thresh))
                    {
                        _backgroundThreshold = thresh;
                    }
                    LogMessage?.Invoke($"Mean 마스킹 적용: Threshold = {_backgroundThreshold}");
                }

                // AI: 초기 Confidence Threshold 설정
                SetConfidenceThreshold(FlashHSI.Core.Settings.SettingsService.Instance.Settings.ConfidenceThreshold);
                
                // AI가 추가함: 모델 타입 저장 (UI 연동용)
                LoadedModelType = config.OriginalType ?? "";
                
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

        /// <summary>
        /// AI가 추가함: 배경 임계값만 동적으로 업데이트
        /// MaskRule 모드일 경우 규칙 내부의 임계값을 변경함
        /// </summary>
        public void UpdateBackgroundThreshold(double threshold)
        {
            _backgroundThreshold = threshold;
            if (_maskMode == MaskMode.MaskRule && _maskRule != null)
            {
                _maskRule.UpdateThreshold(threshold);
            }
        }

        /// <summary>
        /// AI가 추가함: 런타임 Blob Tracking 설정 업데이트
        /// </summary>
        public void UpdateBlobTrackerSettings(int minPixels, int lineGap, int pixelGap)
        {
            // AI: 설정값 캐싱
            _cachedMinPixels = minPixels;
            _cachedLineGap = lineGap;
            _cachedPixelGap = pixelGap;

            if (_blobTracker != null)
            {
                _blobTracker.MinPixels = minPixels;
                _blobTracker.MaxLineGap = lineGap;
                _blobTracker.MaxPixelGap = pixelGap;
            }
        }

        /// <summary>
        /// AI가 추가함: 분류 신뢰도 임계값 설정
        /// </summary>
        /// <summary>
        /// AI가 추가함: 분류 신뢰도 임계값 설정
        /// </summary>
        public void SetConfidenceThreshold(double threshold)
        {
            _pipeline?.SetThreshold(threshold);
        }

        /// <summary>
        /// <ai>AI가 작성함</ai>
        /// 에어건 사출 타겟 클래스를 설정해요. null이면 모든 클래스를 사출해요.
        /// </summary>
        public void SetEjectionTargets(HashSet<int>? targets)
        {
            _cachedEjectionTargets = targets;
            _ejectionService?.SetTargetClasses(targets);
        }

        /// <summary>
        /// AI가 추가함: 현재 적용된 임계값 반환
        /// </summary>
        public double GetCurrentThreshold()
        {
            if (_maskMode == MaskMode.MaskRule && _maskRule != null)
            {
                return _maskRule.GetFirstThreshold();
            }
            return _backgroundThreshold;
        }

        public void SetTargetFps(double fps) => _targetFps = fps;

        // Duplicate removed


        /// <summary>
        /// AI가 추가함: 카메라 프레임을 직접 처리하는 메서드 (이벤트 기반 라이브 분류)
        /// </summary>
        /// <param name="frameData">카메라에서 수신한 raw 프레임 데이터 (ushort)</param>
        /// <param name="width">프레임 너비 (픽셀)</param>
        /// <param name="height">프레임 높이 (밴드 수)</param>
        public unsafe void ProcessCameraFrame(ushort[] frameData, int width, int height)
        {
            if (_currentConfig == null) return;
            // The provided debug log and Application.Current check seem to belong to LiveViewModel,
            // not HsiEngine, as 'blobs' and 'Log' are undefined here.
            // Adding only the missing brace as per instruction 1.
            
            int bandCount = height; // HSI에서 height = band count
            int[] classificationRow = new int[width];
            long[] classCounts = new long[_currentConfig.Weights.Count];

            bool useMean = (_maskMode == MaskMode.Mean);

            fixed (ushort* pFrame = frameData)
            {
                for (int x = 0; x < width; x++)
                {
                    ushort* pPixel = pFrame + (x * bandCount);
                    bool isBackground = false;

                    // Masking (Mean 모드만 지원, 간소화)
                    // Masking Strategy
                    if (_maskMode == MaskMode.MaskRule && _maskRule != null)
                    {
                        // AI가 수정함: Evaluate=true는 '객체'이므로 반전 필요 (RunLoop Line 497과 통일)
                        isBackground = !_maskRule.Evaluate(pPixel, _whiteRef, _darkRef, false);
                    }
                    else if (_maskMode == MaskMode.Mean)
                    {
                        // 2. Mean-based Masking (Average Threshold)
                        long sum = 0;
                        for (int b = 0; b < bandCount; b++) sum += pPixel[b];
                        if (((double)sum / bandCount) < _backgroundThreshold) isBackground = true;
                    }
                    else if (_maskMode == MaskMode.BandPixel)
                    {
                        // 3. Single Band Threshold Masking
                        if (_maskBandIndex >= 0 && _maskBandIndex < bandCount)
                        {
                            ushort val = pPixel[_maskBandIndex];
                            if (_maskOperatorLess)
                            {
                                if (val < _backgroundThreshold) isBackground = true;
                            }
                            else
                            {
                                if (val > _backgroundThreshold) isBackground = true;
                            }
                        }
                    }

                    if (isBackground)
                    {
                        classificationRow[x] = -1;
                    }
                    else
                    {
                        int cls = _pipeline.ProcessFrame(pPixel, bandCount);
                        classificationRow[x] = cls;
                        if (cls >= 0 && cls < classCounts.Length) classCounts[cls]++;
                    }
                }
            }




            // AI가 추가함: Blob Tracking & Ejection (Live Mode)
            if (_blobTracker != null)
            {
                var closedBlobs = _blobTracker.ProcessLine(_globalLineIndex++, classificationRow);
                foreach (var blob in closedBlobs)
                {
                    int bestClass = blob.GetBestClass();
                    if (bestClass >= 0)
                    {
                        // Ejection Service
                        _ejectionService?.Process(blob);

                        // Live Stats Accumulation
                        if (_liveClassCounts != null && bestClass < _liveClassCounts.Length) 
                            _liveClassCounts[bestClass]++;
                        if (_liveStats != null) 
                            _liveStats.Objects++;

                        // Logging
                        if (LogMessage != null)
                        {
                            string key = bestClass.ToString();
                            string className = (_currentConfig?.Labels != null && _currentConfig.Labels.ContainsKey(key))
                                ? _currentConfig.Labels[key] 
                                : $"Class {bestClass}";
                            LogMessage.Invoke($"[Live] Object Detected: {className}, X={blob.CenterX:F0}, Size={blob.TotalPixels}");
                        }
                    }
                }
            }

            // AI Refactor: Create Snapshots and Invoke Event AFTER updates
            // This ensures the UI receives the latest state of blobs (Thread-Safe)
            var snapshots = new System.Collections.Generic.List<FlashHSI.Core.Analysis.ActiveBlob.BlobSnapshot>();
            if (_blobTracker != null)
            {
                foreach(var b in _blobTracker.GetActiveBlobs())
                {
                    // AI: Visualization Filter (Hide Noise in Live Mode too)
                    if (b.TotalPixels >= _cachedMinPixels)
                    {
                        snapshots.Add(b.GetSnapshot());
                    }
                }
            }
            FrameProcessed?.Invoke(classificationRow, width, snapshots);

            // Live Stats Update (Throttle 30fps)
            _liveFrameCount++;
            if (_liveFpsTimer.ElapsedMilliseconds >= 1000)
            {
                if (_liveStats != null) _liveStats.Fps = _liveFrameCount;
                _liveFrameCount = 0;
                _liveFpsTimer.Restart();
            }

            if (_liveUiTimer.ElapsedMilliseconds >= 33)
            {
                if (_liveStats != null && _liveClassCounts != null)
                {
                    Array.Copy(_liveClassCounts, _liveStats.ClassCounts, _liveClassCounts.Length);
                    Array.Clear(_liveClassCounts, 0, _liveClassCounts.Length);
                    
                    StatsUpdated?.Invoke(_liveStats);
                    
                    // Reset delta counts for next batch
                    _liveStats.Objects = 0;
                    _liveStats.Background = 0; 
                    _liveStats.Unknown = 0;
                }
                _liveUiTimer.Restart();
            }
        }


        // AI가 추가함: 라이브 모드에서 라인 인덱스 추적
        private int _globalLineIndex = 0;

        public void StartLive()
        {
            if (_isRunning) return;

            // Stats Init
            if (_currentConfig != null) {
                _liveClassCounts = new long[_currentConfig.Weights.Count];
                if (_liveStats == null) _liveStats = new EngineStats();
                _liveStats.ClassCounts = new long[_currentConfig.Weights.Count];
            }
            _liveUiTimer.Restart();
            _liveFpsTimer.Restart();
            _liveFrameCount = 0;
            if (_liveStats == null) _liveStats = new EngineStats(); // Ensure non-null
            
            _runMode = RunMode.Live;
             _isRunning = true;
            SimulationStateChanged?.Invoke(true);

            _simThread = new Thread(RunLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.Highest, // Critical for Live
                Name = "HsiEngineLiveLoop"
            };
            _simThread.Start();
        }

        public void StartSimulation(string headerPath)
        {
            if (_isRunning) return;

            _runMode = RunMode.Simulation;
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

                try
                {
                    LogMessage?.Invoke($"[RunLoop] Started. Size: {width}x{bandCount}");

                    while (_isRunning)
                    {
                        long startTick = loopTimer.ElapsedTicks;
                        double fps = _targetFps < 1.0 ? 1.0 : _targetFps;
                        double targetFrameTimeMs = 1000.0 / fps;

                        // IO
                        if (!reader.ReadNextFrame(lineBuffer))
                        {
                            LogMessage?.Invoke("[RunLoop] EOF. Rewinding...");
                            reader.Load(_headerPath); // Restart loop
                            if (!reader.ReadNextFrame(lineBuffer)) 
                            {
                                 LogMessage?.Invoke("[RunLoop] Failed to read after rewind. Stop.");
                                 break;
                            }
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

                        // Notify Visualization (Synchronous to avoid allocation? No, now we allocate for safety)
                    // Generate Snapshots for Thread Safety
                    var snapshots = new System.Collections.Generic.List<FlashHSI.Core.Analysis.ActiveBlob.BlobSnapshot>();

                        // 2. Tracking
                        if (_blobTracker != null)
                        {
                           var closedBlobs = _blobTracker.ProcessLine(globalLineIndex, classificationRow);
                           
                           // Add Active Blobs to Snapshots
                           foreach(var b in _blobTracker.GetActiveBlobs())
                           {
                               // AI: Visualization Filter (Hide Noise)
                               // Only show blobs that have enough pixels to be considered 'real'
                               if (b.TotalPixels >= _cachedMinPixels)
                               {
                                   snapshots.Add(b.GetSnapshot());
                               }
                           }

                           foreach (var blob in closedBlobs)
                           {
                               // Add Closed Blobs to Snapshots (for Bottom Cap rendering)
                               snapshots.Add(blob.GetSnapshot());

                               int bestClass = blob.GetBestClass();
                               if (bestClass >= 0)
                               {
                                   _ejectionService?.Process(blob);
                                   stats.Objects++;
                                   if (bestClass < currentClassCounts.Length) currentClassCounts[bestClass]++;
                               }
                           }
                        } // End BlobTracker
                        
                        // AI: Visualization Pixel Filtering (Noise Removal)
                        // Create a clean slate for visualization to remove 1px noise
                        int[] vizRow = new int[width];
                        Array.Fill(vizRow, -1); // Default to background
                        
                        // Only draw pixels that belong to Valid Blobs (>= MinPixels)
                        // A. Active Blobs
                        foreach (var blob in snapshots)
                        {
                             // Snapshots are already filtered by MinPixels above
                             foreach(var seg in blob.CurrentSegments)
                             {
                                 // Copy original classification for this segment
                                 // Note: We need to ensure we copy the correct class.
                                 // But classificationRow has the class info.
                                 // We can just copy from classificationRow at these positions.
                                 for(int x = seg.Start; x <= seg.End; x++)
                                 {
                                     vizRow[x] = classificationRow[x];
                                 }
                             }
                        }
                        
                        
                        if (classificationRow != null)
                        {
                            FrameProcessed?.Invoke(vizRow, width, snapshots);
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
                            Array.Copy(currentClassCounts, stats.ClassCounts, currentClassCounts.Length);
                            Array.Clear(currentClassCounts, 0, currentClassCounts.Length);
                            
                            StatsUpdated?.Invoke(stats);
                            
                            stats.Unknown = 0;
                            stats.Background = 0;
                            stats.Objects = 0;
                            stats.ProcessedLines = linesSinceLastUpdate;
                            linesSinceLastUpdate = 0;

                            Array.Clear(stats.ClassCounts, 0, stats.ClassCounts.Length);
                            
                            lastUiUpdate = frameTimer.ElapsedMilliseconds;
                        }

                        // Sleep
                        while (true)
                        {
                            double elapsedMs = (loopTimer.ElapsedTicks - startTick) / (double)Stopwatch.Frequency * 1000.0;
                            if (elapsedMs >= targetFrameTimeMs) break;
                            if (targetFrameTimeMs - elapsedMs > 1.0) Thread.Sleep(1); else Thread.SpinWait(10);
                        }
                    } // End while (_isRunning)
                }
                catch (Exception ex)
                {
                    LogMessage?.Invoke($"[RunLoop CRASH] {ex.Message}");
                    LogMessage?.Invoke(ex.StackTrace);
                }

                reader.Close();
            } // End unsafe
        } // End RunLoop
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
    public enum RunMode { Simulation, Live }
}
