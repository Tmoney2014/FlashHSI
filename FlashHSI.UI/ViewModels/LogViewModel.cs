using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlashHSI.Core.Control;
using FlashHSI.Core.Engine;
using System.Collections.ObjectModel;
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
             Logs.Clear();
             SystemLogs.Clear();
         }
    }
}
