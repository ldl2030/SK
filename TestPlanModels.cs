using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace TestPlatform.TestSequences
{
    public static class TestProfileIds
    {
        public const string Normal = "NORMAL";
        public const string Rework = "REWORK";
        public const string Engineering = "ENGINEERING";
    }

    public static class Bcm125GroupIds
    {
        public const string Precheck = "BCM125.PRECHECK";
        public const string Cleanup = "BCM125.CLEANUP";

        public static string Chapter(int chapter)
        {
            return $"BCM125.CH{chapter:00}";
        }
    }

    public sealed class TestPlanGroupDefinition
    {
        public string GroupId { get; set; }
        public string Name { get; set; }
        public int SequenceOrder { get; set; }
        public bool Mandatory { get; set; }
        public bool DefaultEnabled { get; set; }
        public List<string> DependsOn { get; } = new List<string>();
    }

    public sealed class TestPlanStepDefinition
    {
        public string StepId { get; set; }
        public string GroupId { get; set; }
        public string Name { get; set; }
        public int SequenceOrder { get; set; }
        public bool DefaultEnabled { get; set; }
        public bool Mandatory { get; set; }
        public bool AlwaysRun { get; set; }
        public string RunCondition { get; set; }
        public List<string> DependsOn { get; } = new List<string>();
    }

    public sealed class TestProfileDefinition
    {
        public string ProfileId { get; set; }
        public string Name { get; set; }
        public bool UseBaselineDefaults { get; set; }
        public bool AllowGroupOverride { get; set; }
        public bool AllowStepOverride { get; set; }
        public bool ResetFromBaselineEveryRun { get; set; }
        public Dictionary<string, bool> GroupStates { get; } =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class TestPlanDefinition
    {
        public string BoardType { get; set; }
        public string PlanVersion { get; set; }
        public string BaselineId { get; set; }
        public IReadOnlyList<TestPlanGroupDefinition> Groups { get; set; }
        public IReadOnlyList<TestPlanStepDefinition> Steps { get; set; }
    }

    public sealed class ResolvedTestPlan
    {
        private readonly Dictionary<string, TestPlanStepDefinition> _definitions;
        private readonly HashSet<string> _enabledStepIds;
        private readonly Dictionary<string, IReadOnlyList<string>> _groupSteps;

        internal ResolvedTestPlan(
            TestPlanDefinition definition,
            TestProfileDefinition profile,
            HashSet<string> enabledStepIds)
        {
            Definition = definition;
            Profile = profile;
            _enabledStepIds = enabledStepIds;
            _definitions = definition.Steps.ToDictionary(
                x => x.StepId,
                StringComparer.OrdinalIgnoreCase);
            _groupSteps = definition.Steps
                .GroupBy(x => x.GroupId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    x => x.Key,
                    x => (IReadOnlyList<string>)x
                        .OrderBy(y => y.SequenceOrder)
                        .Select(y => y.StepId)
                        .ToList(),
                    StringComparer.OrdinalIgnoreCase);
        }

        public TestPlanDefinition Definition { get; }
        public TestProfileDefinition Profile { get; }

        public bool ShouldRun(string stepId)
        {
            return !string.IsNullOrWhiteSpace(stepId) && _enabledStepIds.Contains(stepId);
        }

        public TestPlanStepDefinition GetStep(string stepId)
        {
            TestPlanStepDefinition definition;
            if (!_definitions.TryGetValue(stepId ?? string.Empty, out definition))
                throw new TestPlanConfigurationException($"测试计划中不存在 StepId：{stepId}");
            return definition;
        }

        public IReadOnlyList<string> GetGroupStepIds(string groupId)
        {
            IReadOnlyList<string> result;
            if (!_groupSteps.TryGetValue(groupId ?? string.Empty, out result))
                throw new TestPlanConfigurationException($"测试计划中不存在 GroupId：{groupId}");
            return result;
        }

        public IReadOnlyCollection<string> EnabledStepIds => _enabledStepIds;
    }

    public sealed class StepIdCursor
    {
        private readonly string _groupId;
        private readonly IReadOnlyList<string> _stepIds;
        private int _index;

        public StepIdCursor(string groupId, IReadOnlyList<string> stepIds)
        {
            _groupId = groupId;
            _stepIds = stepIds ?? throw new ArgumentNullException(nameof(stepIds));
        }

        public string Next()
        {
            if (_index >= _stepIds.Count)
            {
                throw new TestPlanConfigurationException(
                    $"GroupId {_groupId} 中没有足够的 StepId，当前请求序号 {_index + 1}。");
            }

            return _stepIds[_index++];
        }

        public int ConsumedCount => _index;
        public int TotalCount => _stepIds.Count;
    }

    public sealed class TestPlanConfigurationException : Exception
    {
        public TestPlanConfigurationException(string message) : base(message)
        {
        }
    }

    public static class TestPlanRuntimeState
    {
        private static readonly object SyncRoot = new object();
        private static string _activeProfileId = TestProfileIds.Normal;
        private static Dictionary<string, bool> _stepOverrides =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        public static string ActiveProfileId
        {
            get
            {
                lock (SyncRoot)
                    return _activeProfileId;
            }
            set
            {
                lock (SyncRoot)
                    _activeProfileId = string.IsNullOrWhiteSpace(value)
                        ? TestProfileIds.Normal
                        : value.Trim().ToUpperInvariant();
            }
        }

        public static void SetStepOverrides(IDictionary<string, bool> values)
        {
            lock (SyncRoot)
            {
                _stepOverrides = values == null
                    ? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, bool>(values, StringComparer.OrdinalIgnoreCase);
            }
        }

        public static Dictionary<string, bool> GetStepOverridesSnapshot()
        {
            lock (SyncRoot)
                return new Dictionary<string, bool>(_stepOverrides, StringComparer.OrdinalIgnoreCase);
        }

        public static void ResetToNormal()
        {
            lock (SyncRoot)
            {
                _activeProfileId = TestProfileIds.Normal;
                _stepOverrides.Clear();
            }
        }
    }

    public static class TestPlanService
    {
        public static ResolvedTestPlan Resolve(
            string testPlanPath,
            string profileId,
            IDictionary<string, bool> runtimeStepOverrides = null)
        {
            TestPlanDefinition definition = LoadDefinition(testPlanPath);
            Dictionary<string, TestProfileDefinition> profiles = LoadProfiles(testPlanPath);

            TestProfileDefinition profile;
            if (!profiles.TryGetValue(
                string.IsNullOrWhiteSpace(profileId) ? TestProfileIds.Normal : profileId,
                out profile))
            {
                throw new TestPlanConfigurationException($"未找到测试 Profile：{profileId}");
            }

            HashSet<string> enabled = ResolveInitialState(definition, profile, runtimeStepOverrides);
            ApplyMandatorySteps(definition, enabled);
            ApplyDependencies(definition, enabled);
            ValidateEnabledPlan(definition, enabled);

            return new ResolvedTestPlan(definition, profile, enabled);
        }

        public static TestPlanDefinition LoadDefinition(string testPlanPath)
        {
            if (string.IsNullOrWhiteSpace(testPlanPath) || !File.Exists(testPlanPath))
                throw new TestPlanConfigurationException($"测试计划文件不存在：{testPlanPath}");

            XDocument doc = XDocument.Load(testPlanPath);
            XElement root = doc.Root;
            if (root == null)
                throw new TestPlanConfigurationException("测试计划 XML 缺少根节点。");

            XElement metadata = root.Element("PlanMetadata");
            List<TestPlanGroupDefinition> groups = root
                .Element("Groups")?
                .Elements("Group")
                .Select(x => new TestPlanGroupDefinition
                {
                    GroupId = Read(x, "GroupId"),
                    Name = Read(x, "Name"),
                    SequenceOrder = ReadInt(x, "SequenceOrder"),
                    Mandatory = ReadBool(x, "Mandatory"),
                    DefaultEnabled = ReadBool(x, "DefaultEnabled", true)
                })
                .ToList() ?? new List<TestPlanGroupDefinition>();

            foreach (TestPlanGroupDefinition group in groups)
            {
                XElement source = root.Element("Groups")?
                    .Elements("Group")
                    .FirstOrDefault(x => string.Equals(
                        Read(x, "GroupId"),
                        group.GroupId,
                        StringComparison.OrdinalIgnoreCase));
                AddCsv(group.DependsOn, Read(source, "DependsOn"));
            }

            List<TestPlanStepDefinition> steps = root
                .Elements("TestItem")
                .Select((x, index) =>
                {
                    var step = new TestPlanStepDefinition
                    {
                        StepId = Read(x, "StepId"),
                        GroupId = Read(x, "GroupId"),
                        Name = Read(x, "Name"),
                        SequenceOrder = ReadInt(x, "SequenceOrder", index + 1),
                        DefaultEnabled = ReadBool(
                            x,
                            "DefaultEnabled",
                            ReadBool(x, "Enabled", true)),
                        Mandatory = ReadBool(x, "Mandatory"),
                        AlwaysRun = ReadBool(x, "AlwaysRun"),
                        RunCondition = Read(x, "RunCondition")
                    };
                    AddCsv(step.DependsOn, Read(x, "DependsOn"));
                    return step;
                })
                .ToList();

            ValidateDefinition(groups, steps);

            return new TestPlanDefinition
            {
                BoardType = Read(metadata, "BoardType"),
                PlanVersion = Read(metadata, "PlanVersion"),
                BaselineId = Read(metadata, "BaselineId"),
                Groups = groups.OrderBy(x => x.SequenceOrder).ToList(),
                Steps = steps.OrderBy(x => x.SequenceOrder).ToList()
            };
        }

        public static Dictionary<string, TestProfileDefinition> LoadProfiles(string testPlanPath)
        {
            string directory = Path.GetDirectoryName(testPlanPath) ?? string.Empty;
            string boardFileName = Path.GetFileNameWithoutExtension(testPlanPath);
            string profilePath = Path.Combine(
                directory,
                boardFileName.Replace("_autoTest", string.Empty) + "_profiles.xml");

            if (!File.Exists(profilePath))
                return CreateDefaultProfiles();

            XDocument doc = XDocument.Load(profilePath);
            var profiles = new Dictionary<string, TestProfileDefinition>(
                StringComparer.OrdinalIgnoreCase);

            foreach (XElement element in doc.Root?.Elements("Profile") ?? Enumerable.Empty<XElement>())
            {
                var profile = new TestProfileDefinition
                {
                    ProfileId = ((string)element.Attribute("Id") ?? Read(element, "ProfileId"))
                        .Trim()
                        .ToUpperInvariant(),
                    Name = Read(element, "Name"),
                    UseBaselineDefaults = ReadBool(element, "UseBaselineDefaults"),
                    AllowGroupOverride = ReadBool(element, "AllowGroupOverride"),
                    AllowStepOverride = ReadBool(element, "AllowStepOverride"),
                    ResetFromBaselineEveryRun = ReadBool(element, "ResetFromBaselineEveryRun")
                };

                foreach (XElement group in element.Element("Groups")?
                    .Elements("Group") ?? Enumerable.Empty<XElement>())
                {
                    string groupId = (string)group.Attribute("Id") ?? Read(group, "GroupId");
                    bool enabled = ReadBool(group, "Enabled");
                    if (!string.IsNullOrWhiteSpace(groupId))
                        profile.GroupStates[groupId] = enabled;
                }

                if (!string.IsNullOrWhiteSpace(profile.ProfileId))
                    profiles[profile.ProfileId] = profile;
            }

            return profiles.Count == 0 ? CreateDefaultProfiles() : profiles;
        }

        private static HashSet<string> ResolveInitialState(
            TestPlanDefinition definition,
            TestProfileDefinition profile,
            IDictionary<string, bool> runtimeStepOverrides)
        {
            var enabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (TestPlanStepDefinition step in definition.Steps)
            {
                bool state = profile.UseBaselineDefaults
                    ? step.DefaultEnabled
                    : profile.GroupStates.TryGetValue(step.GroupId, out bool groupEnabled) && groupEnabled;

                if (!profile.ResetFromBaselineEveryRun &&
                    runtimeStepOverrides != null &&
                    runtimeStepOverrides.TryGetValue(step.StepId, out bool overrideState))
                {
                    state = overrideState;
                }

                if (state)
                    enabled.Add(step.StepId);
            }

            return enabled;
        }

        private static void ApplyMandatorySteps(
            TestPlanDefinition definition,
            HashSet<string> enabled)
        {
            var mandatoryGroups = new HashSet<string>(
                definition.Groups.Where(x => x.Mandatory).Select(x => x.GroupId),
                StringComparer.OrdinalIgnoreCase);

            foreach (TestPlanStepDefinition step in definition.Steps)
            {
                if (step.Mandatory || step.AlwaysRun || mandatoryGroups.Contains(step.GroupId))
                    enabled.Add(step.StepId);
            }
        }

        private static void ApplyDependencies(
            TestPlanDefinition definition,
            HashSet<string> enabled)
        {
            var stepsById = definition.Steps.ToDictionary(
                x => x.StepId,
                StringComparer.OrdinalIgnoreCase);
            var groupsById = definition.Groups.ToDictionary(
                x => x.GroupId,
                StringComparer.OrdinalIgnoreCase);

            bool changed;
            do
            {
                changed = false;
                foreach (TestPlanStepDefinition step in definition.Steps
                    .Where(x => enabled.Contains(x.StepId))
                    .ToList())
                {
                    foreach (string dependency in step.DependsOn)
                    {
                        if (stepsById.ContainsKey(dependency))
                            changed |= enabled.Add(dependency);
                        else if (groupsById.ContainsKey(dependency))
                        {
                            foreach (TestPlanStepDefinition dependencyStep in definition.Steps
                                .Where(x => string.Equals(
                                    x.GroupId,
                                    dependency,
                                    StringComparison.OrdinalIgnoreCase)))
                            {
                                changed |= enabled.Add(dependencyStep.StepId);
                            }
                        }
                    }
                }

                foreach (TestPlanGroupDefinition group in definition.Groups)
                {
                    bool groupEnabled = definition.Steps.Any(
                        x => string.Equals(x.GroupId, group.GroupId, StringComparison.OrdinalIgnoreCase) &&
                             enabled.Contains(x.StepId));
                    if (!groupEnabled)
                        continue;

                    foreach (string dependency in group.DependsOn)
                    {
                        foreach (TestPlanStepDefinition dependencyStep in definition.Steps
                            .Where(x => string.Equals(
                                x.GroupId,
                                dependency,
                                StringComparison.OrdinalIgnoreCase)))
                        {
                            changed |= enabled.Add(dependencyStep.StepId);
                        }
                    }
                }
            } while (changed);
        }

        private static void ValidateDefinition(
            IReadOnlyList<TestPlanGroupDefinition> groups,
            IReadOnlyList<TestPlanStepDefinition> steps)
        {
            if (steps.Count == 0)
                throw new TestPlanConfigurationException("测试计划不包含任何 TestItem。");

            string[] missingIds = steps
                .Where(x => string.IsNullOrWhiteSpace(x.StepId))
                .Select(x => x.Name)
                .Take(5)
                .ToArray();
            if (missingIds.Length > 0)
                throw new TestPlanConfigurationException(
                    $"存在缺少 StepId 的测试项：{string.Join("；", missingIds)}");

            string[] duplicates = steps
                .GroupBy(x => x.StepId, StringComparer.OrdinalIgnoreCase)
                .Where(x => x.Count() > 1)
                .Select(x => x.Key)
                .Take(5)
                .ToArray();
            if (duplicates.Length > 0)
                throw new TestPlanConfigurationException(
                    $"存在重复 StepId：{string.Join("；", duplicates)}");

            string[] duplicateGroups = groups
                .GroupBy(x => x.GroupId, StringComparer.OrdinalIgnoreCase)
                .Where(x => string.IsNullOrWhiteSpace(x.Key) || x.Count() > 1)
                .Select(x => string.IsNullOrWhiteSpace(x.Key) ? "(empty)" : x.Key)
                .Take(5)
                .ToArray();
            if (duplicateGroups.Length > 0)
                throw new TestPlanConfigurationException(
                    $"存在无效或重复 GroupId：{string.Join("；", duplicateGroups)}");

            var groupIds = new HashSet<string>(
                groups.Select(x => x.GroupId),
                StringComparer.OrdinalIgnoreCase);
            string[] invalidGroups = steps
                .Where(x => string.IsNullOrWhiteSpace(x.GroupId) || !groupIds.Contains(x.GroupId))
                .Select(x => $"{x.StepId}->{x.GroupId}")
                .Take(5)
                .ToArray();
            if (invalidGroups.Length > 0)
                throw new TestPlanConfigurationException(
                    $"存在无效 GroupId：{string.Join("；", invalidGroups)}");

            var stepIds = new HashSet<string>(
                steps.Select(x => x.StepId),
                StringComparer.OrdinalIgnoreCase);
            string[] invalidDependencies = groups
                .SelectMany(x => x.DependsOn.Select(y => $"{x.GroupId}->{y}"))
                .Where(x => !groupIds.Contains(x.Substring(x.IndexOf("->", StringComparison.Ordinal) + 2)))
                .Concat(steps.SelectMany(x => x.DependsOn.Select(y => $"{x.StepId}->{y}"))
                    .Where(x =>
                    {
                        string target = x.Substring(x.IndexOf("->", StringComparison.Ordinal) + 2);
                        return !groupIds.Contains(target) && !stepIds.Contains(target);
                    }))
                .Take(5)
                .ToArray();
            if (invalidDependencies.Length > 0)
                throw new TestPlanConfigurationException(
                    $"存在无效依赖：{string.Join("；", invalidDependencies)}");
        }

        private static void ValidateEnabledPlan(
            TestPlanDefinition definition,
            HashSet<string> enabled)
        {
            foreach (TestPlanStepDefinition step in definition.Steps)
            {
                if ((step.Mandatory || step.AlwaysRun) && !enabled.Contains(step.StepId))
                    throw new TestPlanConfigurationException($"不可跳过步骤被禁用：{step.StepId}");
            }
        }

        private static Dictionary<string, TestProfileDefinition> CreateDefaultProfiles()
        {
            return new[]
            {
                new TestProfileDefinition
                {
                    ProfileId = TestProfileIds.Normal,
                    Name = "常规",
                    UseBaselineDefaults = true,
                    ResetFromBaselineEveryRun = true
                },
                new TestProfileDefinition
                {
                    ProfileId = TestProfileIds.Rework,
                    Name = "返修",
                    AllowGroupOverride = true
                },
                new TestProfileDefinition
                {
                    ProfileId = TestProfileIds.Engineering,
                    Name = "工程",
                    AllowGroupOverride = true,
                    AllowStepOverride = true
                }
            }.ToDictionary(x => x.ProfileId, StringComparer.OrdinalIgnoreCase);
        }

        private static string Read(XElement element, string name)
        {
            return ((string)element?.Element(name) ?? string.Empty).Trim();
        }

        private static bool ReadBool(XElement element, string name, bool defaultValue = false)
        {
            bool value;
            return bool.TryParse(Read(element, name), out value) ? value : defaultValue;
        }

        private static int ReadInt(XElement element, string name, int defaultValue = 0)
        {
            int value;
            return int.TryParse(Read(element, name), out value) ? value : defaultValue;
        }

        private static void AddCsv(ICollection<string> destination, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            foreach (string item in value
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                destination.Add(item);
            }
        }
    }
}
