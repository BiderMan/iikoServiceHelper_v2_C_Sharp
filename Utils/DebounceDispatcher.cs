using System;
using System.Windows.Threading;

namespace iikoServiceHelper.Utils
{
    /// <summary>
    /// Утилита для отложенного выполнения действий (Debouncing).
    /// Полезна для автосохранения при вводе текста, поиска и других операций.
    /// </summary>
    public class DebounceDispatcher
    {
        private readonly DispatcherTimer _timer;
        private Action? _action; // Action может быть null

        public DebounceDispatcher()
        {
            _timer = new DispatcherTimer();
            _timer.Tick += Timer_Tick;
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            _timer.Stop();
            _action?.Invoke();
        }

        /// <summary>
        /// Запланировать выполнение действия через заданный интервал.
        /// Если метод вызывается повторно до истечения интервала, таймер сбрасывается.
        /// </summary>
        /// <param name="intervalMs">Интервал в миллисекундах.</param>
        /// <param name="action">Действие для выполнения.</param>
        public void Debounce(int intervalMs, Action action)
        {
            _action = action;
            _timer.Stop();
            _timer.Interval = TimeSpan.FromMilliseconds(intervalMs);
            _timer.Start();
        }
    }
}
