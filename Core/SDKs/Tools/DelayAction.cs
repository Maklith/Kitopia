#region

using log4net;
using Timer = System.Timers.Timer;

#endregion

namespace Core.SDKs.Tools;

public class DelayAction
{
    private static readonly ILog Log = LogManager.GetLogger(nameof(DelayAction));
    private readonly object _lock1 = new();
    private bool _needDelay = false;
    private Timer? _timerDbc;

    /// <summary>
    ///     延迟timesMs后执行。 在此期间如果再次调用，则重新计时
    /// </summary>
    /// <param name="timeMs"></param>
    /// <param name="invoker">同步对象，一般为Control控件。 如不需同步可传null</param>
    /// <param name="action"></param>
    ///
    public void Debounce(int timeMs, TaskScheduler invoker, Action action)
    {
        lock (_lock1)
        {
            if (_timerDbc == null)
            {
                Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.None, invoker);
                //Log.Debug("完成"+timeMs);
                if (timeMs == 0) return;

                _timerDbc = new Timer(timeMs);
                _timerDbc.AutoReset = false;
                _needDelay = false;
                _timerDbc.Elapsed += (_, _) =>
                {
                    _timerDbc.Stop();
                    _timerDbc.Close();
                    _timerDbc = null;
                    if (_needDelay)
                        Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.None, invoker);
                    //Log.Debug("完成1");
                };
                _timerDbc.Start();
            }
            else
            {
                //Log.Debug("重置");
                _needDelay = true;
                _timerDbc.Stop();
                _timerDbc.Start();
            }
        }
    }
}