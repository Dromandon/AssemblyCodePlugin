using System;
using System.Windows;
using System.Windows.Threading;

namespace AssemblyCodePlugin.UI
{
    public partial class ProgressWindow : Window
    {
        private DispatcherTimer _timer;
        private DateTime _startTime;
        private double _estimatedTotalSeconds = 30;
        private bool _isCommitting = false;

        public ProgressWindow()
        {
            InitializeComponent();
            _startTime = DateTime.Now;

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            var elapsed = DateTime.Now - _startTime;
            TxtTimer.Text = elapsed.ToString(@"mm\:ss");

            if (_isCommitting)
            {
                double elapsedSec = elapsed.TotalSeconds;
                double remainingSec = Math.Max(1, _estimatedTotalSeconds - elapsedSec);

                int remMin = (int)(remainingSec / 60);
                int remSec = (int)(remainingSec % 60);

                if (remMin > 0)
                    TxtEstimate.Text = $"Примерное время фиксации Revit: ~{remMin} мин {remSec:D2} сек";
                else
                    TxtEstimate.Text = $"Примерное время фиксации Revit: ~{remSec} сек";

                // Плавная анимация прогресса от 50% до 98% во время Commit
                double prog = 50.0 + Math.Min(48.0, (elapsedSec / Math.Max(10, _estimatedTotalSeconds)) * 48.0);
                PbProgress.Value = prog;
                PbProgress.IsIndeterminate = false;
            }
        }

        public void SetStatus(string status, double progress, string details)
        {
            TxtStatus.Text = status;
            TxtDetails.Text = details;
            if (progress >= 0)
            {
                PbProgress.IsIndeterminate = false;
                PbProgress.Value = progress;
            }
            else
            {
                PbProgress.IsIndeterminate = true;
            }
        }

        public void StartCommitPhase(int updatedElementCount)
        {
            _isCommitting = true;
            // По телеметрии Revit тратит ~0.047 сек на элемент при обновлении соединений
            _estimatedTotalSeconds = Math.Max(10, updatedElementCount * 0.047);
            SetStatus(
                $"Фиксация изменений в модели Revit (обновление геометрии у {updatedElementCount} экз.)...",
                50,
                $"Изменено элементов: {updatedElementCount}. Ядро Revit обновляет геометрические связи."
            );
        }

        protected override void OnClosed(EventArgs e)
        {
            _timer?.Stop();
            base.OnClosed(e);
        }
    }
}
