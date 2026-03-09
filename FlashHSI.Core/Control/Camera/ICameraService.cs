namespace FlashHSI.Core.Control.Camera
{
    /// <summary>
    /// Interface for HSI camera operations.
    /// Standardizes control for different hardware (Pleora, Simulator, etc).
    /// </summary>
    public interface ICameraService : IDisposable
    {
        /// <summary>
        /// Gets a value indicating whether the camera is connected.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Gets a value indicating whether the camera is currently acquiring images.
        /// </summary>
        bool IsAcquiring { get; }

        /// <summary>
        /// Connects to the first available camera or a specific device ID.
        /// </summary>
        /// <param name="deviceId">Optional device identifier (MAC or IP).</param>
        Task<bool> ConnectAsync(string? deviceId = null);

        /// <summary>
        /// Disconnects the camera.
        /// </summary>
        Task DisconnectAsync();

        /// <summary>
        /// Starts image acquisition.
        /// </summary>
        Task StartAcquisitionAsync();

        /// <summary>
        /// Stops image acquisition.
        /// </summary>
        Task StopAcquisitionAsync();

        /// <summary>
        /// Sets a GenICam parameter value.
        /// </summary>
        /// <typeparam name="T">Type of the value (long, double, string, bool, enum string).</typeparam>
        Task SetParameterAsync<T>(string name, T value);

        /// <summary>
        /// Gets a GenICam parameter value.
        /// </summary>
        Task<T> GetParameterAsync<T>(string name);

        /// <summary>
        /// Gets the min/max range for an integer GenICam parameter.
        /// </summary>
        Task<(long Min, long Max)> GetIntParameterRangeAsync(string name);

        /// <summary>
        /// Gets available enum entries for a GenICam enum parameter.
        /// Returns list of available value strings.
        /// </summary>
        Task<List<string>> GetEnumParameterOptionsAsync(string name);

        // AI가 추가함: MROI 설정을 적용하기 위한 커맨드 (RegionApply 등) 전송 인터페이스
        Task ExecuteCommandAsync(string cmdName);

        // AI가 추가함: 카메라 파라미터 제한 범위(Minimum, Maximum)를 동적으로 조회
        Task<(double Min, double Max)> GetFloatParameterRangeAsync(string name);

        // AI가 추가함: 카메라 메타데이터 (ENVI 캡처용)
        double[]? Wavelengths { get; }
        int ParameterWidth { get; }
        int ParameterHeight { get; }
        string CameraName { get; }
        string CameraType { get; }
        double ExposureTime { get; }

        /// <summary>
        /// Event raised when a new frame is received.
        /// Buffer: Full frame data (Band * Width)
        /// Width: Spatial dimension
        /// Height: Spectral dimension (Band Count)
        /// </summary>
        event Action<ushort[], int, int>? FrameReceived;

        /// <summary>
        /// AI가 추가함: 카메라 연결 성공 시 발생하는 이벤트
        /// </summary>
        event Action? Connected;

        /// <summary>
        /// AI가 추가함: Event raised when the camera connection is lost unexpectedly.
        /// </summary>
        event Action<string>? ConnectionLost;
    }
}
