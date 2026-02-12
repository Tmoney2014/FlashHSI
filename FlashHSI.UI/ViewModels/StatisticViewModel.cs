using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlashHSI.Core;
using FlashHSI.Core.Engine;
using FlashHSI.Core.Services;
using Serilog;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace FlashHSI.UI.ViewModels
{
    /// <summary>
    /// 통계 화면 ViewModel - FPS, 클래스별 카운트, 수집 제어
    /// </summary>
    /// <ai>AI가 작성함: PageViewModels.cs에서 분리</ai>
    public partial class StatisticViewModel : ObservableObject
    {
         private readonly HsiEngine _hsiEngine;
         private readonly CommonDataShareService _dataShare;
         
         public ObservableCollection<ClassInfo> ClassStats => _dataShare.CurrentClassStats;
         
         [ObservableProperty] private double _fps;
         [ObservableProperty] private long _totalObjects;
         [ObservableProperty] private bool _isCollecting = true;

         /// <ai>AI가 수정함: DI 주입</ai>
         public StatisticViewModel(HsiEngine engine, CommonDataShareService dataShare)
         {
             _hsiEngine = engine;
             _dataShare = dataShare;
             _hsiEngine.StatsUpdated += OnStatsUpdated;
         }
         
         // OnSimulationStateChanged Removed - User prefers manual control
         
         private void OnStatsUpdated(EngineStats stats)
         {
             if (Application.Current == null) return;
             // if (!IsCollecting) return; // User wants control
             
             if (!IsCollecting) return;

             // Deep Copy for UI Thread Safety (비동기 처리 중 원본이 변경될 수 있음)
             double fps = stats.Fps;
             long[] counts = stats.ClassCounts.ToArray(); 
             
             Application.Current.Dispatcher.InvokeAsync(() =>
             {
                 Fps = fps;
                 
                 // Accumulate Class Counts (Use Copied Array)
                 for (int i = 0; i < counts.Length; i++)
                 {
                     if (i < ClassStats.Count) ClassStats[i].Count += counts[i];
                 }
                 
                 // Recalculate Total & Percentage
                 long currentTotal = 0;
                 foreach(var c in ClassStats) currentTotal += c.Count;
                 TotalObjects = currentTotal;

                 if (currentTotal > 0)
                 {
                    foreach(var c in ClassStats)
                        c.Percentage = $"{(double)c.Count / currentTotal * 100:0.00}%";
                 }
             });
         }
         
         [RelayCommand]
         public void ToggleCollection() => IsCollecting = !IsCollecting;
         
         [RelayCommand]
         public void ResetStats()
         {
             _dataShare.ClearStats();
             TotalObjects = 0;
         }
         
         public void InitializeStats(ModelConfig config)
         {
             ClassStats.Clear();
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
         }
    }
}
