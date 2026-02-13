using FlashHSI.Core.Models;
using FlashHSI.UI.ViewModels;
using System.Windows.Controls;
using System.Windows.Input;

namespace FlashHSI.UI.Views.Pages
{
    public partial class HomeView : UserControl
    {
        public HomeView()
        {
            InitializeComponent();
        }
        
        // AI가 추가함: 모델 카드 클릭 시 ViewModel의 SelectModelCardCommand 호출
        private void ModelCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is System.Windows.FrameworkElement fe && fe.DataContext is ModelCard card
                && DataContext is HomeViewModel vm)
            {
                vm.SelectModelCardCommand.Execute(card);
            }
        }
        
        // AI가 추가함: 모델 카드 수평 스크롤 (마우스 휠 → 수평 이동)
        private void ModelCardScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer sv)
            {
                sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta);
                e.Handled = true;
            }
        }
    }
}
