using System;
using System.Threading.Tasks;

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
        /// Event raised when a new frame is received.
        /// Buffer: Full frame data (Band * Width)
        /// Width: Spatial dimension
        /// Height: Spectral dimension (Band Count)
        /// </summary>
        event Action<ushort[], int, int>? FrameReceived;
        
        /// <summary>
        /// AI가 추가함: Event raised when the camera connection is lost unexpectedly.
        /// </summary>
        event Action<string>? ConnectionLost;
    }
}
