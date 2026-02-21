using System.Runtime.InteropServices;

namespace Core.Utils;

public class TickUtil
{
    /// <summary>
    /// 定时器事件的委托定义
    /// </summary>
    /// <param name="sender">事件的发起者，即定时器对象</param>
    /// <param name="JumpPeriod">上次调用和本次调用跳跃的周期数</param>
    /// <param name="interval">上次调用和本次调用之间的间隔时间（微秒）</param>
    public delegate void OnTickHandle(object sender, long JumpPeriod, long interval);

    /// <summary>
    /// 是否正在运行定时器
    /// </summary>
    private bool _BRunTimer;

    /// <summary>
    /// 定时器运行时独占的CPU核心索引序号
    /// </summary>
    private byte _CpuIndex;

    /// <summary>
    /// 首次启动延时（微秒）
    /// </summary>
    private uint _Delay;

    /// <summary>
    /// 是否销毁定时器
    /// </summary>
    private bool _Dispose;

    /// <summary>
    /// 系统性能计数频率（每秒）
    /// </summary>
    private long _Freq;

    /// <summary>
    /// 系统性能计数频率（每微秒）
    /// </summary>
    private long _Freqmms;

    /// <summary>
    /// 定时器周期（微秒）
    /// </summary>
    private long _Period = 10;

    private Thread _threadRumTimer;

    /// <summary>
    /// 回调函数定义
    /// </summary>
    private OnTickHandle Tick;

    /// <summary>
    /// 定时器构造函数
    /// </summary>
    /// <param name="delay">首次启动定时器延时时间（微秒）</param>
    /// <param name="period">定时器触发的周期（微秒）</param>
    /// <param name="cpuIndex">指定定时器线程独占的CPU核心索引，必须>0，不允许为定时器分配0#CPU</param>
    /// <param name="tick">定时器触发时的回调函数</param>
    public TickUtil(uint delay, uint period, byte cpuIndex, OnTickHandle tick)
    {
        Tick = tick;
        _Delay = delay;
        _Period = period;
        _CpuIndex = cpuIndex;
        long freq = 0;
        QueryPerformanceFrequency(out freq);
        if (freq > 0)
        {
            _Freq = freq;
            _Freqmms = freq / 1000000; //每微秒性能计数器跳跃次数
        }
        else
        {
            throw new Exception("初始化定时器失败");
        }

        if (_CpuIndex == 0) throw new Exception("定时器不允许被分配到0#CPU");

        if (_CpuIndex >= Environment.ProcessorCount) throw new Exception("为定时器分配了超出索引的CPU");
    }

    /// <summary>
    /// 获取当前系统性能计数
    /// </summary>
    /// <param name="lpPerformanceCount"></param>
    /// <returns></returns>
    [DllImport("Kernel32.dll")]
    private static extern bool QueryPerformanceCounter(out long lpPerformanceCount);

    /// <summary>
    /// 获取当前系统性能频率
    /// </summary>
    /// <param name="lpFrequency"></param>
    /// <returns></returns>
    [DllImport("Kernel32.dll")]
    private static extern bool QueryPerformanceFrequency(out long lpFrequency);

    /// <summary>
    /// 指定某一特定线程运行在指定的CPU核心
    /// </summary>
    /// <param name="hThread"></param>
    /// <param name="dwThreadAffinityMask"></param>
    /// <returns></returns>
    [DllImport("kernel32.dll")]
    private static extern UIntPtr SetThreadAffinityMask(IntPtr hThread, UIntPtr dwThreadAffinityMask);

    /// <summary>
    /// 获取当前线程的Handler
    /// </summary>
    /// <returns></returns>
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentThread();

    /// <summary>
    /// 根据CPU的索引序号获取CPU的标识序号
    /// </summary>
    /// <param name="idx"></param>
    /// <returns></returns>
    private ulong GetCpuID(int idx)
    {
        ulong cpuid = 0;
        if (idx < 0 || idx >= Environment.ProcessorCount) idx = 0;

        cpuid |= 1UL << idx;
        return cpuid;
    }

    /// <summary>
    /// 开启定时器
    /// </summary>
    public void Open()
    {
        if (Tick != null)
        {
            _threadRumTimer = new Thread(RunTimer);
            _threadRumTimer.Start();
        }
    }

    /// <summary>
    /// 运行定时器
    /// </summary>
    private void RunTimer()
    {
        var up = UIntPtr.Zero;
        if (_CpuIndex != 0)
            up = SetThreadAffinityMask(GetCurrentThread(), new UIntPtr(GetCpuID(_CpuIndex)));
        if (up == UIntPtr.Zero) throw new Exception("为定时器分配CPU核心时失败");

        long q1, q2;
        QueryPerformanceCounter(out q1);
        QueryPerformanceCounter(out q2);
        if (_Delay > 0)
            while (q2 < q1 + _Delay * _Freqmms)
                QueryPerformanceCounter(out q2);

        QueryPerformanceCounter(out q1);
        QueryPerformanceCounter(out q2);
        while (!_Dispose)
        {
            _BRunTimer = true;
            QueryPerformanceCounter(out q2);
            if (q2 > q1 + _Freqmms * _Period)
            {
                //***********回调***********//
                if (!_Dispose)
                    Tick(this, (q2 - q1) / (_Freqmms * _Period), (q2 - q1) / _Freqmms);
                q1 = q2;
                //System.Windows.Forms.Application.DoEvents();//会导致线程等待windows消息循环，时间损失15ms以上
            }

            _BRunTimer = false;
        }
    }

    /// <summary>
    /// 销毁当前定时器所占用的资源
    /// </summary>
    public void Dispose()
    {
        _Dispose = true;
        // while (_BRunTimer)
        //     Application.DoEvents();//在工作未完成之前，允许处理消息队列，防止调用者挂起
    }
}