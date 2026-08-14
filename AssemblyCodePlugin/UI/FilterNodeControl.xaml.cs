using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Runtime.InteropServices;
using AssemblyCodePlugin.Models;

namespace AssemblyCodePlugin.UI
{
    public class FilterNodeDragData
    {
        public FilterNodeControl SourceControl { get; set; }
    }

    public class DragGhostWindow : Window
    {
        public DragGhostWindow(UIElement sourceControl)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            IsHitTestVisible = false;
            Topmost = true;
            
            var visual = new VisualBrush(sourceControl);
            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = sourceControl.RenderSize.Width,
                Height = sourceControl.RenderSize.Height,
                Fill = visual,
                Opacity = 0.5
            };
            Content = rect;
            
            Width = rect.Width;
            Height = rect.Height;
        }
    }

    public partial class FilterNodeControl : UserControl
    {
        public FilterNodeControl ParentNodeControl { get; set; }
        private FilterNode _node;
        private System.Collections.ObjectModel.ObservableCollection<string> _availableParams;
        public FilterNode Node => _node;
        
        public event EventHandler NodeDeleted;
        public event EventHandler NodeChanged;

        public FilterNodeControl(FilterNode node, System.Collections.ObjectModel.ObservableCollection<string> availableParams, bool isRoot = false)
        {
            InitializeComponent();
            _node = node;
            _availableParams = availableParams;
            
            // Настройка UI
            if (_node.IsGroup)
            {
                GroupBorder.Visibility = Visibility.Visible;
                RuleBorder.Visibility = Visibility.Collapsed;
                
                LogicComboBox.SelectedIndex = (_node.LogicalOperator == "OR" || _node.LogicalOperator == "ИЛИ") ? 1 : 0;
                
                if (isRoot)
                {
                    BtnDeleteGroup.Visibility = Visibility.Collapsed; // Корневую группу нельзя удалить
                }
                
                RenderChildren();
            }
            else
            {
                GroupBorder.Visibility = Visibility.Collapsed;
                RuleBorder.Visibility = Visibility.Visible;
                
                ParamComboBox.ItemsSource = _availableParams;
                ParamComboBox.Text = _node.ParamName;
                
                SetComparatorSelection(_node.Comparator);
                ValueTextBox.Text = _node.ValueString;
            }
        }
        
        private void RenderChildren()
        {
            ChildrenContainer.Children.Clear();
            if (_node.Children == null) return;
            
            foreach (var childNode in _node.Children)
            {
                var childControl = new FilterNodeControl(childNode, _availableParams);
                childControl.ParentNodeControl = this;
                childControl.NodeDeleted += ChildControl_NodeDeleted;
                childControl.NodeChanged += ChildControl_NodeChanged;
                ChildrenContainer.Children.Add(childControl);
            }
        }

        private void ChildControl_NodeChanged(object sender, EventArgs e)
        {
            NodeChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ChildControl_NodeDeleted(object sender, EventArgs e)
        {
            var childControl = sender as FilterNodeControl;
            if (childControl != null)
            {
                _node.Children.Remove(childControl.Node);
                RenderChildren();
                NodeChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void LogicComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_node != null && LogicComboBox.SelectedItem is ComboBoxItem item)
            {
                _node.LogicalOperator = item.Content.ToString() == "ИЛИ" ? "OR" : "AND";
                NodeChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void BtnAddCondition_Click(object sender, RoutedEventArgs e)
        {
            if (_node.Children == null) _node.Children = new List<FilterNode>();
            _node.Children.Add(new FilterNode { IsGroup = false, ParamName = "Имя типа", Comparator = "Содержит", ValueString = "" });
            RenderChildren();
            NodeChanged?.Invoke(this, EventArgs.Empty);
        }

        private void BtnAddGroup_Click(object sender, RoutedEventArgs e)
        {
            if (_node.Children == null) _node.Children = new List<FilterNode>();
            _node.Children.Add(new FilterNode { IsGroup = true, LogicalOperator = "AND" });
            RenderChildren();
            NodeChanged?.Invoke(this, EventArgs.Empty);
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            NodeDeleted?.Invoke(this, EventArgs.Empty);
        }

        private void ParamComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateParamName();
        }

        private void ParamComboBox_LostFocus(object sender, RoutedEventArgs e)
        {
            UpdateParamName();
        }
        
        private void UpdateParamName()
        {
            if (_node != null && ParamComboBox.Text != _node.ParamName)
            {
                _node.ParamName = ParamComboBox.Text;
                NodeChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void ComparatorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_node != null && ComparatorComboBox.SelectedItem is ComboBoxItem item)
            {
                _node.Comparator = item.Content.ToString();
                NodeChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void ValueTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_node != null)
            {
                _node.ValueString = ValueTextBox.Text;
                NodeChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void SetComparatorSelection(string comparator)
        {
            if (string.IsNullOrEmpty(comparator)) return;
            foreach (ComboBoxItem item in ComparatorComboBox.Items)
            {
                if (item.Content.ToString() == comparator)
                {
                    ComparatorComboBox.SelectedItem = item;
                    return;
                }
            }
        }

        // --- DRAG AND DROP LOGIC ---

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out POINT pt);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private Point _dragStartPoint;
        private bool _isDragging = false;

        private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            _isDragging = true;
            ((UIElement)sender).CaptureMouse();
        }

        private void DragHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || e.LeftButton != MouseButtonState.Pressed)
            {
                _isDragging = false;
                if (((UIElement)sender).IsMouseCaptured)
                    ((UIElement)sender).ReleaseMouseCapture();
                return;
            }

            Vector diff = _dragStartPoint - e.GetPosition(null);
            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                _isDragging = false;
                if (((UIElement)sender).IsMouseCaptured)
                    ((UIElement)sender).ReleaseMouseCapture();

                // Корневой узел не перетаскиваем
                if (this.ParentNodeControl == null) return;

                var ghost = new DragGhostWindow(this);
                
                POINT pt;
                if (GetCursorPos(out pt))
                {
                    ghost.Left = pt.X + 15;
                    ghost.Top = pt.Y + 15;
                }
                ghost.Show();

                GiveFeedbackEventHandler feedbackHandler = (s, args) =>
                {
                    if (GetCursorPos(out pt))
                    {
                        ghost.Left = pt.X + 15;
                        ghost.Top = pt.Y + 15;
                    }
                };
                
                this.GiveFeedback += feedbackHandler;

                var data = new DataObject("FilterNodeFormat", new FilterNodeDragData { SourceControl = this });
                DragDrop.DoDragDrop(this, data, DragDropEffects.Move);

                this.GiveFeedback -= feedbackHandler;
                ghost.Close();
            }
        }

        private void DropTarget_DragEnter(object sender, DragEventArgs e)
        {
            if (HandleDragEnter(sender as Border, e))
                e.Handled = true;
        }

        private void DropTarget_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is Border b) b.Background = Brushes.Transparent;
            e.Handled = true;
        }

        private void DropTarget_Drop(object sender, DragEventArgs e)
        {
            if (HandleDrop(sender as Border, e, isGroup: false))
                e.Handled = true;
        }

        private void Group_DragEnter(object sender, DragEventArgs e)
        {
            if (!this.Node.IsGroup) { e.Effects = DragDropEffects.None; return; }
            if (HandleDragEnter(GroupDropHighlight, e))
                e.Handled = true;
        }

        private void Group_DragLeave(object sender, DragEventArgs e)
        {
            GroupDropHighlight.BorderBrush = Brushes.Transparent;
            e.Handled = true;
        }

        private void Group_Drop(object sender, DragEventArgs e)
        {
            if (!this.Node.IsGroup) return;
            if (HandleDrop(GroupDropHighlight, e, isGroup: true))
                e.Handled = true;
        }

        private bool HandleDragEnter(Border targetBorder, DragEventArgs e)
        {
            if (targetBorder == null || !e.Data.GetDataPresent("FilterNodeFormat"))
            {
                e.Effects = DragDropEffects.None;
                return true;
            }
            
            var data = e.Data.GetData("FilterNodeFormat") as FilterNodeDragData;
            if (data == null || data.SourceControl == this || IsDescendant(this, data.SourceControl))
            {
                e.Effects = DragDropEffects.None;
            }
            else
            {
                e.Effects = DragDropEffects.Move;
                if (targetBorder == GroupDropHighlight)
                    targetBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6"));
                else
                    targetBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6"));
            }
            return true;
        }

        private bool HandleDrop(Border targetBorder, DragEventArgs e, bool isGroup)
        {
            if (targetBorder == null) return false;
            
            if (isGroup)
                targetBorder.BorderBrush = Brushes.Transparent;
            else
                targetBorder.Background = Brushes.Transparent;

            if (!e.Data.GetDataPresent("FilterNodeFormat")) return true;

            var data = e.Data.GetData("FilterNodeFormat") as FilterNodeDragData;
            if (data != null && data.SourceControl != this && !IsDescendant(this, data.SourceControl))
            {
                if (isGroup)
                    MoveNodeIntoGroup(data.SourceControl, this);
                else
                {
                    bool isTop = targetBorder == DropTargetTop;
                    MoveNode(data.SourceControl, this, isTop);
                }
                
                var root = GetRootControl();
                root.NodeChanged?.Invoke(root, EventArgs.Empty);
            }
            return true;
        }

        private bool IsDescendant(FilterNodeControl potentialDescendant, FilterNodeControl potentialAncestor)
        {
            var curr = potentialDescendant;
            while (curr != null)
            {
                if (curr == potentialAncestor) return true;
                curr = curr.ParentNodeControl;
            }
            return false;
        }

        private FilterNodeControl GetRootControl()
        {
            var curr = this;
            while (curr.ParentNodeControl != null) curr = curr.ParentNodeControl;
            return curr;
        }

        private void MoveNode(FilterNodeControl source, FilterNodeControl target, bool insertBefore)
        {
            if (target.ParentNodeControl == null) return;
            
            var sourceParent = source.ParentNodeControl;
            var targetParent = target.ParentNodeControl;

            sourceParent.Node.Children.Remove(source.Node);
            sourceParent.ChildrenContainer.Children.Remove(source);

            int insertIndex = targetParent.ChildrenContainer.Children.IndexOf(target);
            if (insertIndex < 0) insertIndex = 0;
            if (!insertBefore) insertIndex++;

            targetParent.Node.Children.Insert(insertIndex, source.Node);
            targetParent.ChildrenContainer.Children.Insert(insertIndex, source);
            
            source.ParentNodeControl = targetParent;
        }

        private void MoveNodeIntoGroup(FilterNodeControl source, FilterNodeControl targetGroup)
        {
            var sourceParent = source.ParentNodeControl;

            sourceParent.Node.Children.Remove(source.Node);
            sourceParent.ChildrenContainer.Children.Remove(source);

            if (targetGroup.Node.Children == null) targetGroup.Node.Children = new List<FilterNode>();
            
            targetGroup.Node.Children.Add(source.Node);
            targetGroup.ChildrenContainer.Children.Add(source);
            
            source.ParentNodeControl = targetGroup;
        }
    }
}
