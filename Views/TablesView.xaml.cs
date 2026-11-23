using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ServeX.Models;
using ServeX.ViewModels;

namespace ServeX.Views
{
    public partial class TablesView : UserControl
    {
        private Point _dragStart;

        public TablesView()
        {
            var cell = new Rect(0, 0, TableFloorItemViewModel.CellWidth, TableFloorItemViewModel.CellHeight);
            FloorGridBrush = new DrawingBrush(new GeometryDrawing(
                Brushes.Transparent,
                new Pen(Brushes.SlateGray, 1) { DashStyle = DashStyles.Dash },
                new RectangleGeometry(cell)))
            {
                Viewport = cell,
                ViewportUnits = BrushMappingMode.Absolute,
                TileMode = TileMode.Tile
            };
            FloorGridBrush.Freeze();
            InitializeComponent();
        }

        public DrawingBrush FloorGridBrush { get; }

        private void TableMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not TablesViewModel { IsBusy: false } || sender is not FrameworkElement card)
                return;

            _dragStart = e.GetPosition(FloorGrid);
            card.CaptureMouse();
            e.Handled = true;
        }

        private void TableMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement card && card.IsMouseCaptured)
                card.ReleaseMouseCapture();
        }

        private void TableMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed ||
                sender is not FrameworkElement { IsMouseCaptured: true, DataContext: TableFloorItemViewModel table } card)
                return;

            var point = e.GetPosition(FloorGrid);
            if (Math.Abs(point.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(point.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            card.ReleaseMouseCapture();
            DragDrop.DoDragDrop(card, new DataObject(typeof(TableFloorItemViewModel), table), DragDropEffects.Move);
        }

        private void FloorDragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            if (DataContext is not TablesViewModel { IsArrangingFloor: true, IsBusy: false } ||
                !e.Data.GetDataPresent(typeof(TableFloorItemViewModel)))
                return;

            e.Effects = DragDropEffects.Move;
            var point = e.GetPosition(FloorScroll);
            const int edge = 24;
            const int step = 16;
            if (point.X < edge)
                FloorScroll.ScrollToHorizontalOffset(FloorScroll.HorizontalOffset - step);
            else if (point.X > FloorScroll.ViewportWidth - edge)
                FloorScroll.ScrollToHorizontalOffset(FloorScroll.HorizontalOffset + step);

            if (point.Y < edge)
                FloorScroll.ScrollToVerticalOffset(FloorScroll.VerticalOffset - step);
            else if (point.Y > FloorScroll.ViewportHeight - edge)
                FloorScroll.ScrollToVerticalOffset(FloorScroll.VerticalOffset + step);
        }

        private async void FloorDrop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            var point = e.GetPosition(FloorGrid);
            if (DataContext is not TablesViewModel viewModel ||
                e.Data.GetData(typeof(TableFloorItemViewModel)) is not TableFloorItemViewModel table ||
                point.X < 0 || point.X >= FloorGrid.ActualWidth || point.Y < 0 || point.Y >= FloorGrid.ActualHeight)
                return;

            var column = (int)(point.X / TableFloorItemViewModel.CellWidth);
            var row = (int)(point.Y / TableFloorItemViewModel.CellHeight);
            await viewModel.MoveTableAsync(table, row * TableFloorLayout.Columns + column);
        }
    }
}
