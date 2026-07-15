using System.Timers;
using PluginCore;
using Timer = System.Timers.Timer;

namespace Kitopia.Desktop.Features.Services.HotKey;

/// <summary>
/// 定时器助手类 / Timer helper class for delayed action execution
/// </summary>
public class TimerHelper
{
    private Action<HotKeyModel> _action;
    private HotKeyModel _hotKeyModel;
    private Timer _timer;

    /// <summary>
    /// 初始化定时器助手 / Initialize timer helper
    /// </summary>
    /// <param name="interval">间隔时间（毫秒）/ Interval in milliseconds</param>
    /// <param name="action">要执行的动作 / Action to execute</param>
    /// <param name="hotKeyModel">热键模型 / Hot key model</param>
    public TimerHelper(int interval, Action<HotKeyModel> action, HotKeyModel hotKeyModel)
    {
        _timer = new Timer(interval);
        _timer.AutoReset = false;
        _timer.Elapsed += OnTimerElapsed;
        _action = action;
        _hotKeyModel = hotKeyModel;
    }

    /// <summary>当计时器触发时执行的操作 / Action executed when timer elapses</summary>
    private void OnTimerElapsed(object source, ElapsedEventArgs e)
    {
        ThreadPool.QueueUserWorkItem(e => { _action.Invoke(_hotKeyModel); });
    }

    /// <summary>在需要开始计时器的地方调用该方法 / Start the timer</summary>
    public void StartTimer()
    {
        _timer.Start();
    }

    /// <summary>在需要停止计时器的地方调用该方法 / Stop the timer</summary>
    public void StopTimer()
    {
        _timer.Stop();
    }
}