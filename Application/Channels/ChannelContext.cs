using System.Threading;

namespace TestPlatform
{
    public class ChannelContext
    {
        public int Index { get; set; }

        public string CurrentSN { get; set; }

        public bool IsBusy { get; set; }

        public CancellationTokenSource CancelToken { get; set; }

        public ResultWindowModel ResultModel { get; set; }

        public string PrintedSN { get; set; }

        public bool IsMesChecked { get; set; }
    }
}
