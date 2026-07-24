using System;
using System.Threading.Tasks;

namespace TestPlatform.TestSequences
{
    /// <summary>
    /// 独立测试序列接口。
    /// 序列类不直接依赖 MainWindow 中的测试方法，只通过事件输出日志。
    /// </summary>
    public interface ITestSequence
    {
        /// <summary>
        /// 对应 ProjectList.xml 中的 SequenceKey。
        /// </summary>
        string SequenceKey { get; }

        event Action<string> LogInfo;
        event Action<string> LogWarning;
        event Action<string> LogError;
        event Action<string> LogSuccess;

        Task<bool> RunAsync(TestSequenceContext context);
    }
}
