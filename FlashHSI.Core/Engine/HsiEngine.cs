using System.Buffers;
using System.Diagnostics;
using CommunityToolkit.Mvvm.Messaging;
using FlashHSI.Core.Analysis;
using FlashHSI.Core.Classifiers;
using FlashHSI.Core.Control;
using FlashHSI.Core.Control.Camera;
using FlashHSI.Core.IO;
using FlashHSI.Core.Masking;
using FlashHSI.Core.Messages;
using FlashHSI.Core.Pipelines;
using FlashHSI.Core.Preprocessing;
using FlashHSI.Core.Settings;
using Newtonsoft.Json;

namespace FlashHSI.Core.Engine
{
    public class HsiEngine : IDisposable
    {
        // Components
        private HsiPipeline _pipeline;
        private ICameraService? _cameraService;
        private BlobTracker? _blobTracker;
        private EjectionService? _ejectionService;

        /// <summary>
        /// AI가 추가함: 현재 로드된 모델 설정 (UI에서 접근 필요)
        /// </summary>
        public ModelConfig? CurrentConfig { get; private set; }

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

        // GC 최적화: ProcessCameraFrame에서 재사용할 버퍼 (field-level)
        private int[]? _classificationRowBuffer;
        private int _lastWidth;

        // GC 최적화: 스냅샷 리스트 제거 (ArrayPool 기반 Zero-Allocation으로 대체)

        // Events
        public event Action<EngineStats>? StatsUpdated;
        public event Action<string>? LogMessage;
        public event Action<bool>? SimulationStateChanged;
        // AI: Changed event signature to pass serialized blob contour array (ArrayPool) instead of objects
        public event Action<int[], int, int[], int>? FrameProcessed;
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
            LinearClassifier.GlobalLog += (msg) => LogMessage?.Invoke(msg);

            // AI가 추가함: 메시지 구독
            WeakReferenceMessenger.Default.Register<HsiEngine, SettingsChangedMessage<double>>(this, static (recipient, message) =>
            {
                switch (message.PropertyName)
                {
                    case nameof(SystemSettings.TargetFps):
                        recipient.SetTargetFps(message.Value);
                        break;
                    case nameof(SystemSettings.ConfidenceThreshold):
                        recipient.SetConfidenceThreshold(message.Value);
                        break;
                    case nameof(SystemSettings.BackgroundThreshold):
                        recipient.SetBackgroundThreshold(message.Value);
                        break;
                }
            });
        }

        public void LoadModel(string jsonPath)
        {
            try
            {
                string json = File.ReadAllText(jsonPath);
                var config = JsonConvert.DeserializeObject<ModelConfig>(json);
                if (config == null) throw new Exception("Failed to deserialize model config");

                CurrentConfig = config;
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
                SetConfidenceThreshold(SettingsService.Instance.Settings.ConfidenceThreshold);

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

                for (int i = 0; i < linesToRead; i++)
                {
                    if (!reader.ReadNextFrame(buf)) break;

                    for (int x = 0; x < width; x++)
                    {
                        int baseIdx = x * bands;
                        for (int b = 0; b < bands; b++) sum[b] += buf[baseIdx + b];
                    }
                    count += width;
                }
                reader.Close();

                if (count > 0)
                {
                    double[] avg = new double[bands];
                    for (int b = 0; b < bands; b++) avg[b] = (double)sum[b] / count;

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
            // Debug.WriteLine($"[HsiEngine] SetMaskSettings: mode={mode}, bandIndex={bandIndex}, lessThan={lessThan}, threshold={threshold}");

            _maskMode = mode;

            // Mean 모드에서는 maskRule을 clear (BandPixel 모드에서도 기존 rule 제거)
            if (mode == MaskMode.Mean || mode == MaskMode.BandPixel)
            {
                _maskRule = null;
            }
            // rule이 null이 아니면 기존 MaskRule 교체 (MaskRule 모드에서만)
            else if (rule != null)
            {
                _maskRule = rule;
            }

            _maskBandIndex = bandIndex;
            _maskOperatorLess = lessThan;
            _backgroundThreshold = threshold;

            // MaskRule 모드이고 rule이 있으면 Threshold 동기화
            if (mode == MaskMode.MaskRule && _maskRule != null)
            {
                _maskRule.UpdateThreshold(threshold);
            }
        }

        /// <summary>
        /// AI가 추가함: MaskRuleConditionCollection에서 MaskRule을 생성하여 적용
        /// </summary>
        public void SetMaskRuleFromCollection(MaskRuleConditionCollection collection)
        {
            if (collection.ConditionGroups.Count > 0)
            {
                _maskMode = MaskMode.MaskRule;
                _maskRule = collection.ToMaskRule();
                LogMessage?.Invoke($"MaskRule 적용: {collection.ToMaskRuleString()}");
            }
            else
            {
                _maskMode = MaskMode.Mean;
                _maskRule = null;
            }
        }

        /// <summary>
        /// AI가 추가함: 현재 MaskRuleConditions 문자열을 반환 (설정 로드용)
        /// </summary>
        public string GetCurrentMaskRulesString()
        {
            if (_maskRule != null && CurrentConfig != null)
            {
                return CurrentConfig.Preprocessing.MaskRules ?? "Mean";
            }
            return "Mean";
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
        /// AI가 추가함: 메시징용 배경 임계값 설정 (UpdateBackgroundThreshold와 동일)
        /// </summary>
        public void SetBackgroundThreshold(double threshold)
        {
            // Debug.WriteLine($"[HsiEngine] SetBackgroundThreshold called: {threshold}");
            UpdateBackgroundThreshold(threshold);
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

        /// <summary>
        /// AI가 추가함: 현재 Mask 설정 반환 (UI 연동용)
        /// </summary>
        public (MaskMode mode, int bandIndex, bool lessThan, double threshold) GetMaskSettings()
        {
            return (_maskMode, _maskBandIndex, _maskOperatorLess, _backgroundThreshold);
        }

        /// <summary>
        /// AI가 추가함: MaskRule의 첫 번째 조건 정보를 반환 (UI 초기화용)
        /// </summary>
        public (int bandIndex, double threshold, bool isLess) GetMaskRuleConditionInfo()
        {
            if (_maskRule != null)
            {
                return _maskRule.GetFirstConditionInfo();
            }
            return (80, 35000.0, true);
        }

        /// <summary>
        /// AI가 추가함: MaskRule의 첫 번째 조건을 업데이트 (Slider 연동용)
        /// </summary>
        public void UpdateMaskRuleCondition(int bandIndex, double threshold, bool isLess)
        {
            if (_maskRule != null)
            {
                _maskRule.UpdateFirstCondition(bandIndex, threshold, isLess);
            }
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
            if (CurrentConfig == null) return;

            int bandCount = height; // HSI에서 height = band count

            // GC 최적화: field-level 버퍼 재사용 (width가 같으면 새 할당 없이 재사용)
            if (_classificationRowBuffer == null || _lastWidth != width)
            {
                _classificationRowBuffer = new int[width];
                _lastWidth = width;
            }
            int[] classificationRow = _classificationRowBuffer;

            // ArrayPool을 사용한 GC 최적화
            long[] classCounts = ArrayPool<long>.Shared.Rent(CurrentConfig.Weights.Count);
            Array.Clear(classCounts, 0, CurrentConfig.Weights.Count);

            // GC 최적화: UI 표시용 버퍼 (필터링된 결과값 전달)
            int[] vizRow = ArrayPool<int>.Shared.Rent(width);
            Array.Fill(vizRow, -1); // Default to background

            try
            {
                // AI가 수정함: Slider 변경 시 즉시 적용되도록 매 프레임마다 모드 확인
                bool useMean = (_maskMode == MaskMode.Mean);

                // 디버그 로그
                if (Debugger.IsAttached)
                {
                    // Debug.WriteLine($"[ProcessCameraFrame] _backgroundThreshold={_backgroundThreshold}, useMean={useMean}, _maskMode={_maskMode}");
                }

                fixed (ushort* pFrame = frameData)
                {
                    // AI가 추가함: 5초마다 HsiEngine 프레임 수신 진단 (블랙 프레임 여부 확인)
                    if (_liveFpsTimer.ElapsedMilliseconds % 5000 < 33 && _liveFrameCount % 30 == 0) // 대략 5초 주기
                    {
                        long sampleSum = 0;
                        for (int k = 0; k < bandCount; k++) sampleSum += pFrame[k];
                        LogMessage?.Invoke($"[HsiEngine] 프레임 수신 중... 첫 픽셀 평균값: {sampleSum / bandCount}, Width: {width}");
                    }

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
                            double meanValue = (double)sum / bandCount;
                            // 체크박스 (MaskLessThan)에 따라 조건 결정
                            if (_maskOperatorLess)
                            {
                                if (meanValue < _backgroundThreshold) isBackground = true;
                            }
                            else
                            {
                                if (meanValue > _backgroundThreshold) isBackground = true;
                            }
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
                    // FIX: Process blobs BEFORE releasing to pool (이전: ReleaseClosedBlobs가 foreach 전에 호출되어 리스트가 비어짐)
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
                                string className = (CurrentConfig?.Labels != null && CurrentConfig.Labels.ContainsKey(key))
                                    ? CurrentConfig.Labels[key]
                                    : $"Class {bestClass}";
                                LogMessage.Invoke($"[Live] Object Detected: {className}, X={blob.CenterX:F0}, Size={blob.TotalPixels}");
                            }
                        }
                    }
                    // 처리 완료 후 pool에 반환
                    _blobTracker?.ReleaseClosedBlobs(closedBlobs);
                }

                // AI Refactor: Create Snapshots and Invoke Event AFTER updates
                // GC 최적화: BlobSnapshot(객체) 대신 ArrayPool을 사용해 int[]로 윤곽선 정보 전체를 압축 직렬화하여 전달
                if (FrameProcessed != null)
                {
                    int[] contourData = ArrayPool<int>.Shared.Rent(1024);
                    int contourLen = 0;
                    contourData[contourLen++] = 0; // index 0: 유효 Blob 개수 기록용

                    int validBlobCount = 0;
                    if (_blobTracker != null)
                    {
                        foreach (var b in _blobTracker.GetActiveBlobs())
                        {
                            if (b.TotalPixels >= _cachedMinPixels)
                            {
                                validBlobCount++;
                                int cCount = b.CurrentSegments.Count;
                                int pCount = b.PrevSegments.Count;

                                // Buffer size check (safely expand if needed)
                                if (contourLen + 2 + (cCount + pCount) * 2 >= contourData.Length)
                                {
                                    int[] newArr = ArrayPool<int>.Shared.Rent(contourData.Length * 2);
                                    Array.Copy(contourData, newArr, contourLen);
                                    ArrayPool<int>.Shared.Return(contourData);
                                    contourData = newArr;
                                }

                                contourData[contourLen++] = cCount;
                                contourData[contourLen++] = pCount;
                                foreach (var seg in b.CurrentSegments) { contourData[contourLen++] = seg.Start; contourData[contourLen++] = seg.End; }
                                foreach (var seg in b.PrevSegments) { contourData[contourLen++] = seg.Start; contourData[contourLen++] = seg.End; }
                            }
                        }
                    }
                    contourData[0] = validBlobCount;

                    // AI가 수정함: vizRow에 전체 프레임 분류 결과 복사 (LiveView에서 보이도록 강제 렌더링)
                    Array.Copy(classificationRow, vizRow, width);

                    // UI 렌더링 측에서 사용 후 반환해야 함
                    FrameProcessed.Invoke(vizRow, width, contourData, contourLen);
                }

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
            } // try 블록 종료

            // ArrayPool 반환 (GC 최적화)
            finally
            {
                ArrayPool<long>.Shared.Return(classCounts);
                ArrayPool<int>.Shared.Return(vizRow);
            }
        }


        // AI가 추가함: 라이브 모드에서 라인 인덱스 추적
        private int _globalLineIndex = 0;

        /// <summary>
        /// AI가 수정함: 라이브 모드 시작 - ProcessCameraFrame()은 카메라 콜백에서 호출되므로 별도 스레드 불필요
        /// </summary>
        public void StartLive()
        {
            if (_isRunning) return;

            // Stats Init
            if (CurrentConfig != null)
            {
                _liveClassCounts = new long[CurrentConfig.Weights.Count];
                if (_liveStats == null) _liveStats = new EngineStats();
                _liveStats.ClassCounts = new long[CurrentConfig.Weights.Count];
            }

            // AI가 수정함: 라인 인덱스 초기화 (카메라에서 프레임이 올라올 때마다 증가)
            _globalLineIndex = 0;

            _liveUiTimer.Restart();
            _liveFpsTimer.Restart();
            _liveFrameCount = 0;
            if (_liveStats == null) _liveStats = new EngineStats(); // Ensure non-null

            _runMode = RunMode.Live;
            _isRunning = true;
            SimulationStateChanged?.Invoke(true);

            // 참고: ProcessCameraFrame()은 PleoraCameraService.AcquisitionLoop() 콜백에서 호출됨
            // 별도 스레드 필요 없음
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
                if (CurrentConfig != null)
                {
                    _pipeline.Configure(bandCount, CurrentConfig.SelectedBands);
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
            long[] currentClassCounts = new long[CurrentConfig?.Weights.Count ?? 32];
            int[] vizRow = new int[width]; // GC 최적화: while 루프 밖에서 한 번만 할당

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
                int globalLineIndex = 0;
                double lastBackgroundThreshold = 0;  // AI가 추가함: 이전 값 저장
                bool lastUseMean = false;  // AI가 추가함: 이전 모드 저장

                try
                {
                    LogMessage?.Invoke($"[RunLoop] Started. Size: {width}x{bandCount}");
                    LogMessage?.Invoke($"[RunLoop] MaskMode: {_maskMode}, BackgroundThreshold: {_backgroundThreshold}");

                    while (_isRunning)
                    {
                        // AI가 수정함: Threshold 변경 시 플래그 업데이트 (캐시)
                        if (lastBackgroundThreshold != _backgroundThreshold || lastUseMean != (_maskMode == MaskMode.Mean))
                        {
                            lastBackgroundThreshold = _backgroundThreshold;
                            lastUseMean = (_maskMode == MaskMode.Mean);
                            LogMessage?.Invoke($"[RunLoop] Mask config updated: useMean={lastUseMean}, threshold={_backgroundThreshold}");
                        }

                        // cached 플래그 사용 (값 변경 시에만 업데이트됨)
                        bool useMaskRule = (_maskMode == MaskMode.MaskRule && _maskRule != null);
                        bool useBandPixel = (_maskMode == MaskMode.BandPixel && _maskBandIndex < bandCount);
                        bool useMean = lastUseMean;
                        bool useAbsorbanceForMask = (_pipeline.GetExtractorType() == typeof(LogGapFeatureExtractor));

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
                                    double meanValue = (double)sum / bandCount;
                                    // 체크박스 (MaskLessThan)에 따라 조건 결정
                                    if (_maskOperatorLess)
                                    {
                                        if (meanValue < _backgroundThreshold) isBackground = true;
                                    }
                                    else
                                    {
                                        if (meanValue > _backgroundThreshold) isBackground = true;
                                    }
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

                        // Notify Visualization (Zero-Allocation)
                        int[] contourData = null!;
                        int contourLen = 0;
                        if (FrameProcessed != null)
                        {
                            contourData = ArrayPool<int>.Shared.Rent(1024);
                            contourData[contourLen++] = 0;
                        }

                        // 2. Tracking
                        Array.Fill(vizRow, -1); // Default to background
                        int validBlobCount = 0;

                        if (_blobTracker != null)
                        {
                            var closedBlobs = _blobTracker.ProcessLine(globalLineIndex, classificationRow);

                            // Add Active Blobs
                            foreach (var b in _blobTracker.GetActiveBlobs())
                            {
                                // AI: Visualization Filter (Hide Noise)
                                if (b.TotalPixels >= _cachedMinPixels)
                                {
                                    validBlobCount++;
                                    if (FrameProcessed != null)
                                    {
                                        int cCount = b.CurrentSegments.Count;
                                        int pCount = b.PrevSegments.Count;
                                        if (contourLen + 2 + (cCount + pCount) * 2 >= contourData.Length)
                                        {
                                            int[] newArr = ArrayPool<int>.Shared.Rent(contourData.Length * 2);
                                            Array.Copy(contourData, newArr, contourLen);
                                            ArrayPool<int>.Shared.Return(contourData);
                                            contourData = newArr;
                                        }
                                        contourData[contourLen++] = cCount;
                                        contourData[contourLen++] = pCount;
                                        foreach (var seg in b.CurrentSegments) { contourData[contourLen++] = seg.Start; contourData[contourLen++] = seg.End; }
                                        foreach (var seg in b.PrevSegments) { contourData[contourLen++] = seg.Start; contourData[contourLen++] = seg.End; }
                                    }

                                    // Set vizRow according to valid blob positions
                                    foreach (var seg in b.CurrentSegments)
                                    {
                                        for (int x = seg.Start; x <= seg.End; x++) vizRow[x] = classificationRow[x];
                                    }
                                }
                            }

                            // Add Closed Blobs to Snapshots (for Bottom Cap rendering)
                            foreach (var blob in closedBlobs)
                            {
                                validBlobCount++;
                                if (FrameProcessed != null)
                                {
                                    int cCount = blob.CurrentSegments.Count;
                                    int pCount = blob.PrevSegments.Count;
                                    if (contourLen + 2 + (cCount + pCount) * 2 >= contourData.Length)
                                    {
                                        int[] newArr = ArrayPool<int>.Shared.Rent(contourData.Length * 2);
                                        Array.Copy(contourData, newArr, contourLen);
                                        ArrayPool<int>.Shared.Return(contourData);
                                        contourData = newArr;
                                    }
                                    contourData[contourLen++] = cCount;
                                    contourData[contourLen++] = pCount;
                                    foreach (var seg in blob.CurrentSegments) { contourData[contourLen++] = seg.Start; contourData[contourLen++] = seg.End; }
                                    foreach (var seg in blob.PrevSegments) { contourData[contourLen++] = seg.Start; contourData[contourLen++] = seg.End; }
                                }

                                foreach (var seg in blob.CurrentSegments)
                                {
                                    for (int x = seg.Start; x <= seg.End; x++) vizRow[x] = classificationRow[x];
                                }

                                int bestClass = blob.GetBestClass();
                                if (bestClass >= 0)
                                {
                                    _ejectionService?.Process(blob);
                                    stats.Objects++;
                                    if (bestClass < currentClassCounts.Length) currentClassCounts[bestClass]++;
                                }
                            }

                            // Pool 반환
                            _blobTracker.ReleaseClosedBlobs(closedBlobs);
                        } // End BlobTracker

                        if (FrameProcessed != null)
                        {
                            contourData[0] = validBlobCount;
                            FrameProcessed.Invoke(vizRow, width, contourData, contourLen);
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

        // GC 최적화: 이벤트 핸들러 누수 방지
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // 정적 이벤트 구독 해제 (메모리 누수 방지)
            LinearClassifier.GlobalLog -= (msg) => LogMessage?.Invoke(msg);

            // 다른 리소스 정리
            _pipeline = null;
            _cameraService = null;
            _blobTracker = null;
            _ejectionService = null;

            GC.SuppressFinalize(this);
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
    public enum RunMode { Simulation, Live }
}
