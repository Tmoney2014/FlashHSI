using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FlashHSI.Core.Control.Camera;
using Serilog;

namespace FlashHSI.Core.Services
{
    /// <summary>
    /// AI가 작성함: 캡처 관련 파일 쓰기 및 레퍼런스 원본 복사를 수행하는 공통 서비스
    /// </summary>
    public class CaptureService : ICaptureService
    {
        // AI가 추가함: 캡처 프레임 버퍼 (GC 최적화: LiveViewModel에서 관리하던 것을 서비스로 이동)
        private readonly List<ushort[]> _captureBuffer = new();
        private int _captureWidth;
        private int _captureHeight;

        public event Action<int>? CapturedFrameCountChanged;

        public int CurrentCapturedFrameCount
        {
            get
            {
                lock (_captureBuffer)
                {
                    return _captureBuffer.Count;
                }
            }
        }

        public void ClearBuffer()
        {
            lock (_captureBuffer)
            {
                _captureBuffer.Clear();
                _captureWidth = 0;
                _captureHeight = 0;
            }
            CapturedFrameCountChanged?.Invoke(0);
        }

        public void AddFrame(ushort[] data, int width, int height)
        {
            int count;
            lock (_captureBuffer)
            {
                var copy = new ushort[data.Length];
                Array.Copy(data, copy, data.Length);
                _captureBuffer.Add(copy);
                _captureWidth = width;
                _captureHeight = height;
                count = _captureBuffer.Count;
            }
            // AI가 수정함: 데드락 방지 — lock 밖에서 이벤트 발행 (구독자가 Dispatcher.InvokeAsync 호출 가능)
            CapturedFrameCountChanged?.Invoke(count);
        }

        public (List<ushort[]> frames, int width, int height) GetCapturedDataAndClear()
        {
            List<ushort[]> frames;
            int w, h;
            lock (_captureBuffer)
            {
                frames = new List<ushort[]>(_captureBuffer);
                w = _captureWidth;
                h = _captureHeight;
                _captureBuffer.Clear();
            }
            // AI가 수정함: 데드락 방지 — lock 밖에서 이벤트 발행
            CapturedFrameCountChanged?.Invoke(0);
            return (frames, w, h);
        }

        public async Task SaveCaptureAsync(string baseName, string captureDirectory, List<ushort[]> frames, string? whiteRefPath, string? darkRefPath, int width, int height, ICameraService cameraService)
        {
            try
            {
                // AI가 수정함: 모든 파일 IO를 Task.Run 안으로 통합 — UI 스레드 블로킹 완전 차단
                // (await 이후 SynchronizationContext 복귀 없이 백그라운드 스레드에서 전체 처리)
                await Task.Run(() =>
                {
                    // 타임스탬프 기반 전용 폴더 생성
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string folderName = $"{baseName}_{timestamp}";
                    string targetDir = Path.Combine(captureDirectory, folderName);

                    if (!Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    // 1. 원료 파일 경로 설정
                    string rawPath = Path.Combine(targetDir, $"{folderName}.raw");
                    string hdrPath = Path.Combine(targetDir, $"{folderName}.hdr");

                    // 바이너리 데이터 저장
                    using var fs = new FileStream(rawPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536);
                    using var bw = new BinaryWriter(fs);

                    foreach (var frame in frames)
                    {
                        foreach (var val in frame)
                        {
                            bw.Write(val);
                        }
                    }

                    bw.Flush();

                    // ENVI 헤더 파일 생성 (.hdr)
                    CreateEnviHeader(hdrPath, baseName, frames.Count, width, height, cameraService);

                    // 2. White Reference 복사 (원본 보존)
                    if (!string.IsNullOrEmpty(whiteRefPath) && File.Exists(whiteRefPath))
                    {
                        try
                        {
                            string sourceWhiteDir = Path.GetDirectoryName(whiteRefPath) ?? "";
                            string sourceWhiteName = Path.GetFileNameWithoutExtension(whiteRefPath);
                            string sourceWhiteRaw = Path.Combine(sourceWhiteDir, $"{sourceWhiteName}.raw");

                            string targetWhiteHdr = Path.Combine(targetDir, $"whiteref_{folderName}.hdr");
                            string targetWhiteRaw = Path.Combine(targetDir, $"whiteref_{folderName}.raw");

                            File.Copy(whiteRefPath, targetWhiteHdr, true);
                            if (File.Exists(sourceWhiteRaw))
                            {
                                File.Copy(sourceWhiteRaw, targetWhiteRaw, true);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, $"Failed to copy White Reference to {targetDir}");
                        }
                    }

                    // 3. Dark Reference 복사 (원본 보존)
                    if (!string.IsNullOrEmpty(darkRefPath) && File.Exists(darkRefPath))
                    {
                        try
                        {
                            string sourceDarkDir = Path.GetDirectoryName(darkRefPath) ?? "";
                            string sourceDarkName = Path.GetFileNameWithoutExtension(darkRefPath);
                            string sourceDarkRaw = Path.Combine(sourceDarkDir, $"{sourceDarkName}.raw");

                            string targetDarkHdr = Path.Combine(targetDir, $"darkref_{folderName}.hdr");
                            string targetDarkRaw = Path.Combine(targetDir, $"darkref_{folderName}.raw");

                            File.Copy(darkRefPath, targetDarkHdr, true);
                            if (File.Exists(sourceDarkRaw))
                            {
                                File.Copy(sourceDarkRaw, targetDarkRaw, true);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, $"Failed to copy Dark Reference to {targetDir}");
                        }
                    }

                    Log.Information($"Capture saved successfully to directory: {targetDir}");
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save capture data");
                throw;
            }
        }

        private void CreateEnviHeader(string path, string baseName, int lines, int width, int height, ICameraService cameraService)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("ENVI");
            sb.AppendLine("description = { ");
            sb.AppendLine($"  FlashHSI - Capture ({baseName})");
            sb.AppendLine($"  Date = {DateTime.Now:yyyy-MM-dd HH:mm:ss:ffff}");
            sb.AppendLine($"  Camera name = {cameraService.CameraName}");
            sb.AppendLine($"  Camera type = {cameraService.CameraType}");
            sb.AppendLine($"  Integration time = {cameraService.ExposureTime:0.##}");
            sb.AppendLine(" }");
            sb.AppendLine("file type = ENVI");
            sb.AppendLine();
            sb.AppendLine("interleave = bil");
            sb.AppendLine($"samples = {width}");
            sb.AppendLine($"lines   = {lines}");
            sb.AppendLine($"bands   = {height}");
            sb.AppendLine("header offset = 0");
            sb.AppendLine("data type = 12");
            sb.AppendLine("byte order = 0");
            sb.AppendLine();
            sb.AppendLine("errors = {none}");
            sb.AppendLine();

            // 파장(Wavelength) 배열 동적 기록 (ICameraService 캐싱 활용)
            if (cameraService.Wavelengths != null && cameraService.Wavelengths.Length == height)
            {
                sb.AppendLine("Wavelength = {");
                for (int i = 0; i < cameraService.Wavelengths.Length; i++)
                {
                    sb.Append(cameraService.Wavelengths[i]);
                    // 마지막 항목 뒤에는 콤마를 찍지 않음
                    if (i < cameraService.Wavelengths.Length - 1)
                    {
                        sb.AppendLine(",");
                    }
                    else
                    {
                        sb.AppendLine();
                    }
                }
                sb.AppendLine("}");
            }
            else
            {
                // 파장 데이터가 덜 로드된 경우 (최소한의 기본값 하드코딩 배열 폴백 방지, 없으면 생략)
                Log.Warning("카메라 Wavelength 메타데이터가 없거나 밴드 수와 불일치하여 헤더에 Wavelength 리스트를 생략합니다.");
            }

            File.WriteAllText(path, sb.ToString());
        }
    }
}
