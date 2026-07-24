using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace TestPlatform
{
    public class OnlineConfigRoot
    {
        public Dictionary<string, int> customConfig { get; set; }
    }

    public static class OnlineConfigHelper
    {
        private static readonly HttpClient _client = new HttpClient();
        private const string ConfigUrl = "https://updatebqc.bqc-smt.com/version/configDoc";

        public static async Task<Dictionary<string, int>> GetTestLimitsAsync()
        {
            try
            {
                string json = await _client.GetStringAsync(ConfigUrl);
                var root = JsonConvert.DeserializeObject<OnlineConfigRoot>(json);
                return root?.customConfig ?? new Dictionary<string, int>();
            }
            catch
            {
                return new Dictionary<string, int>();
            }
        }
    }
}