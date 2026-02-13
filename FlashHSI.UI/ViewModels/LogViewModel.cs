using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlashHSI.Core.Control;
using FlashHSI.Core.Engine;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace FlashHSI.UI.ViewModels
{
    /// <summary>
    /// 로그 화면 ViewModel - 이젝션 로그 표시
    /// </summary>
    /// <ai>AI가 작성함: PageViewModels.cs에서 분리</ai>
    public partial class LogViewModel : ObservableObject
    {
         public ObservableCollection<EjectionLogItem> Logs { get; } = new();
         
         // AI: 시스템 로그 추가
         public ObservableCollection<string> SystemLogs { get; } = new();

         /// <ai>AI가 수정함: DI</ai>
         public LogViewModel(HsiEngine engine)
         {
             engine.EjectionOccurred += OnEjection;
             engine.LogMessage += OnLogMessage;
         }
         
         private void OnEjection(EjectionLogItem item)
         {
             if (Application.Current == null) return;

             Application.Current.Dispatcher.InvokeAsync(() => {
                 Logs.Insert(0, item);
                 if(Logs.Count > 200) Logs.RemoveAt(Logs.Count - 1);
             });
         }

         private void OnLogMessage(string msg)
         {
             if (Application.Current == null) return;

             Application.Current.Dispatcher.InvokeAsync(() => {
                 SystemLogs.Insert(0, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}");
                 if(SystemLogs.Count > 100) SystemLogs.RemoveAt(SystemLogs.Count - 1);
             });
         }
         
         [RelayCommand]
         public void ClearLogs() 
         {
             // AI가 수정함: 로그가 비어있으면 무시
             if (Logs.Count == 0 && SystemLogs.Count == 0) return;

             var result = MessageBox.Show(
                 $"전체 로그 {Logs.Count + SystemLogs.Count}건을 삭제하시겠습니까?",
                 "확인",
                 MessageBoxButton.YesNo,
                 MessageBoxImage.Question);

             if (result == MessageBoxResult.Yes)
             {
                 Logs.Clear();
                 SystemLogs.Clear();
             }
         }

         /// <ai>AI가 작성함</ai>
         /// <summary>
         /// 로그를 텍스트 파일로 내보내기 (Ejection + System 로그)
         /// </summary>
         [RelayCommand]
         private void SaveLogToFile()
         {
             if (Logs.Count == 0 && SystemLogs.Count == 0)
             {
                 MessageBox.Show("저장할 로그가 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                 return;
             }

             var saveFileDialog = new SaveFileDialog
             {
                 Filter = "Text file (*.txt)|*.txt",
                 Title = "로그 저장",
                 FileName = DateTime.Now.ToString("yyyy-MM-dd_HH-mm") + "_FlashHSI_Log.txt"
             };

             if (saveFileDialog.ShowDialog() == true)
             {
                 try
                 {
                     using var writer = new StreamWriter(saveFileDialog.FileName);

                     // Ejection 로그 섹션
                     writer.WriteLine("=== Ejection Logs ===");
                     writer.WriteLine($"Total: {Logs.Count}");
                     writer.WriteLine("Time\tBlobID\tClassID\tValveID\tDelay\tType");
                     foreach (var log in Logs)
                     {
                         writer.WriteLine($"{log.TimestampShort}\t{log.BlobId}\t{log.ClassId}\t{log.ValveId}\t{log.Delay}\t{log.HitType}");
                     }

                     writer.WriteLine();

                     // System 로그 섹션
                     writer.WriteLine("=== System Logs ===");
                     writer.WriteLine($"Total: {SystemLogs.Count}");
                     foreach (var msg in SystemLogs)
                     {
                         writer.WriteLine(msg);
                     }

                     MessageBox.Show($"로그가 저장되었습니다.\n{saveFileDialog.FileName}", "저장 완료", MessageBoxButton.OK, MessageBoxImage.Information);
                 }
                 catch (Exception ex)
                 {
                     MessageBox.Show($"파일 저장 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                 }
             }
         }
    }
}
