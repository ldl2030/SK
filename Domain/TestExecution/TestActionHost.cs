using System;
using System.Threading;
using System.Threading.Tasks;

namespace TestPlatform.TestSequences
{
    public class TestActionHost
    {
        public Func<int, int, string, CancellationToken, Task> SN_Input { get; set; }

        public Func<int, int, CancellationToken, Task<bool>> ConfirmFixtureDownward_FC { get; set; }

        public Func<int, int, byte[], int, CancellationToken, bool, Task<bool>> GetTPVolt { get; set; }
    }
}