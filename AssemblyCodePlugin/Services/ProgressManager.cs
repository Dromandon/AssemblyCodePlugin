using System;
using System.Threading;
using System.Windows.Threading;
using AssemblyCodePlugin.UI;

namespace AssemblyCodePlugin.Services
{
    public static class ProgressManager
    {
        private static ProgressWindow _window;
        private static Thread _uiThread;
        private static Dispatcher _dispatcher;

        public static void Show(string initialStatus = "Запуск обработки...")
        {
            Close();

            var resetEvent = new ManualResetEvent(false);

            _uiThread = new Thread(() =>
            {
                _dispatcher = Dispatcher.CurrentDispatcher;
                _window = new ProgressWindow();
                _window.SetStatus(initialStatus, 5, "Подготовка...");
                _window.Show();

                resetEvent.Set();
                Dispatcher.Run();
            });

            _uiThread.SetApartmentState(ApartmentState.STA);
            _uiThread.IsBackground = true;
            _uiThread.Start();

            resetEvent.WaitOne();
        }

        public static void UpdateStatus(string status, double progress, string details = "")
        {
            try
            {
                _dispatcher?.BeginInvoke(new Action(() =>
                {
                    _window?.SetStatus(status, progress, details);
                }));
            }
            catch { }
        }

        public static void StartCommitPhase(int updatedElementCount)
        {
            try
            {
                _dispatcher?.BeginInvoke(new Action(() =>
                {
                    _window?.StartCommitPhase(updatedElementCount);
                }));
            }
            catch { }
        }

        public static void Close()
        {
            try
            {
                if (_dispatcher != null)
                {
                    _dispatcher.Invoke(() =>
                    {
                        _window?.Close();
                        _window = null;
                    });
                    _dispatcher.InvokeShutdown();
                    _dispatcher = null;
                }
            }
            catch { }
        }
    }
}
