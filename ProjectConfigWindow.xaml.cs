using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Xml.Linq;
using TestPlatform.TestSequences;

namespace TestPlatform
{
    public partial class ProjectConfigWindow : Window
    {
        public string SelectedProjectName { get; private set; }
        public class TestProject
        {
            /// <summary>
            /// 项目名称，例如 VC、FC、ME、LS
            /// </summary>
            public string GroupName { get; set; }

            /// <summary>
            /// 机型名称，例如 Docking Station Board测试、840300-52 REV3烧录
            /// </summary>
            public string ModelName { get; set; }

            /// <summary>
            /// 完整项目名称，保持兼容 MainWindow 现有逻辑
            /// </summary>
            public string DisplayName { get; set; }

            public string FilePath { get; set; }

            public string UpdateUrl { get; set; }

            public string Runner { get; set; }

            public string SequenceKey { get; set; }

            public string ComInitKey { get; set; }

            public List<string> RequiredComPorts { get; set; } = new List<string>();

            public string FtpBasePath { get; set; }
        }

        private ObservableCollection<TestProject> projects = new ObservableCollection<TestProject>();

        // 当前项目名称下显示的机型集合
        private ObservableCollection<TestProject> filteredProjects = new ObservableCollection<TestProject>();

        private ObservableCollection<TestItem> testItems = new ObservableCollection<TestItem>();
        private ObservableCollection<TestGroupItem> testGroups = new ObservableCollection<TestGroupItem>();
        private Dictionary<string, TestProfileDefinition> testProfiles =
            new Dictionary<string, TestProfileDefinition>(StringComparer.OrdinalIgnoreCase);
        private string currentTestPlanPath;
        private bool hasStructuredTestPlan;
        private bool applyingProfile;
        private string projectListFile;

        public ProjectConfigWindow(string currentProjectName)
        {
            InitializeComponent();
            cmbTestProfile.ItemsSource = new[]
            {
                new TestProfileOption(TestProfileIds.Normal, "常规"),
                new TestProfileOption(TestProfileIds.Rework, "返修"),
                new TestProfileOption(TestProfileIds.Engineering, "工程")
            };
            cmbTestProfile.DisplayMemberPath = "Name";
            tvTestGroups.ItemsSource = testGroups;
            // 先初始化项目列表文件路径（必须）
            projectListFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProjectList.xml");
            // 再加载项目列表（内部可能调用 SaveProjectList）
            LoadProjectList();
            // 根据当前项目名称选中对应项
            if (!string.IsNullOrEmpty(currentProjectName))
            {
                var selected = projects.FirstOrDefault(p => p.DisplayName == currentProjectName);

                if (selected != null)
                {
                    cmbProjectGroups.SelectedItem = selected.GroupName;
                    FilterProjectsByGroup(selected.GroupName);
                    cmbTestProjects.SelectedItem = filteredProjects
                        .FirstOrDefault(p => p.DisplayName == currentProjectName);
                }
            }
        }

        private void LoadProjectList()
        {
            projects.Clear();

            if (File.Exists(projectListFile))
            {
                try
                {
                    XDocument doc = XDocument.Load(projectListFile);

                    foreach (var elem in doc.Root.Elements("Project"))
                    {
                        string displayName = (string)elem.Element("DisplayName") ?? "";

                        string groupName = (string)elem.Element("GroupName") ?? "";
                        string modelName = (string)elem.Element("ModelName") ?? "";

                        if (string.IsNullOrWhiteSpace(groupName))
                            groupName = GetGroupNameFromDisplayName(displayName);

                        if (string.IsNullOrWhiteSpace(modelName))
                            modelName = GetModelNameFromDisplayName(displayName);

                        var requiredPorts = elem.Element("RequiredComPorts")?
                            .Elements("ComPort")
                            .Select(x => ((string)x ?? "").Trim())
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .ToList() ?? new List<string>();

                        projects.Add(new TestProject
                        {
                            GroupName = groupName,
                            ModelName = modelName,
                            DisplayName = displayName,
                            FilePath = (string)elem.Element("FilePath") ?? "",
                            UpdateUrl = (string)elem.Element("UpdateUrl") ?? "",

                            Runner = (string)elem.Element("Runner") ?? "Legacy",
                            SequenceKey = (string)elem.Element("SequenceKey") ?? "",
                            ComInitKey = (string)elem.Element("ComInitKey") ?? "",
                            RequiredComPorts = requiredPorts,
                            FtpBasePath = (string)elem.Element("FtpBasePath") ?? ""
                        });
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"读取项目列表失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            LoadProjectGroups();
        }
        private void LoadProjectGroups()
        {
            var groups = projects
                .Select(p => string.IsNullOrWhiteSpace(p.GroupName) ? "默认" : p.GroupName)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            cmbProjectGroups.ItemsSource = groups;

            if (groups.Count > 0)
            {
                cmbProjectGroups.SelectedIndex = 0;
            }
            else
            {
                filteredProjects.Clear();
                cmbTestProjects.ItemsSource = filteredProjects;
            }
        }
        private void FilterProjectsByGroup(string groupName)
        {
            filteredProjects.Clear();

            string targetGroup = string.IsNullOrWhiteSpace(groupName) ? "默认" : groupName;

            var list = projects
                .Where(p =>
                {
                    string currentGroup = string.IsNullOrWhiteSpace(p.GroupName) ? "默认" : p.GroupName;
                    return string.Equals(currentGroup, targetGroup, StringComparison.OrdinalIgnoreCase);
                })
                .OrderBy(p => p.ModelName)
                .ToList();

            foreach (var item in list)
            {
                filteredProjects.Add(item);
            }

            cmbTestProjects.ItemsSource = filteredProjects;
        }
        private void CmbProjectGroups_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(cmbProjectGroups.SelectedItem is string selectedGroup))
                return;

            FilterProjectsByGroup(selectedGroup);

            if (filteredProjects.Count > 0)
            {
                cmbTestProjects.SelectedIndex = 0;
            }
            else
            {
                testItems.Clear();
                dgTestItems.ItemsSource = null;
            }
        }
        private void SaveProjectList()
        {
            try
            {
                XDocument doc = new XDocument(new XElement("ProjectList"));

                foreach (var proj in projects)
                {
                    string storedPath = ConvertToRelativePath(proj.FilePath);

                    XElement projectElem = new XElement("Project",
                        new XElement("GroupName", proj.GroupName ?? ""),
                        new XElement("ModelName", proj.ModelName ?? ""),
                        new XElement("DisplayName", proj.DisplayName ?? ""),
                        new XElement("FilePath", storedPath),
                        new XElement("UpdateUrl", proj.UpdateUrl ?? ""),

                        new XElement("Runner", string.IsNullOrWhiteSpace(proj.Runner) ? "Legacy" : proj.Runner),
                        new XElement("SequenceKey", proj.SequenceKey ?? ""),
                        new XElement("ComInitKey", proj.ComInitKey ?? ""),

                        new XElement("RequiredComPorts",
                            (proj.RequiredComPorts ?? new List<string>())
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Select(x => new XElement("ComPort", x))
                        ),

                        new XElement("FtpBasePath", proj.FtpBasePath ?? "")
                    );

                    doc.Root.Add(projectElem);
                }

                doc.Save(projectListFile);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存项目列表失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private string ConvertToRelativePath(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath)) return absolutePath;
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            if (absolutePath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            {
                return absolutePath.Substring(baseDir.Length);
            }
            return absolutePath;
        }
        private string GetGroupNameFromDisplayName(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
                return "默认";

            string name = displayName.Trim();

            if (name.Length <= 2)
                return name;

            return name.Substring(0, 2).Trim();
        }

        private string GetModelNameFromDisplayName(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
                return "";

            string name = displayName.Trim();

            if (name.Length <= 2)
                return name;

            return name.Substring(2).Trim();
        }

        private string ResolveFilePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return filePath;

            if (Path.IsPathRooted(filePath))
                return filePath;

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filePath);
        }
        private void CmbTestProjects_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (cmbTestProjects.SelectedItem is TestProject selected)
                {
                    LoadTestDataFromXml(ResolveFilePath(selected.FilePath));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"错误类型: {ex.GetType()}-错误信息：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadTestDataFromXml(string filePath)
        {
            currentTestPlanPath = filePath;
            if (!File.Exists(filePath))
            {
                testItems.Clear();
                testGroups.Clear();
                dgTestItems.ItemsSource = null;
                grpTestPlan.Visibility = Visibility.Collapsed;
                
                return;
            }

            try
            {
                XDocument doc = XDocument.Load(filePath);
                hasStructuredTestPlan = doc.Root?.Element("PlanMetadata") != null &&
                                        doc.Root?.Element("Groups") != null;
                var items = doc.Root.Elements("TestItem").Select(elem => new TestItem
                {
                    Enabled = (bool?)elem.Element("Enabled") ?? false,
                    StepId = (string)elem.Element("StepId") ?? "",
                    GroupId = (string)elem.Element("GroupId") ?? "",
                    SequenceOrder = (int?)elem.Element("SequenceOrder") ?? 0,
                    DefaultEnabled = (bool?)elem.Element("DefaultEnabled") ??
                                     (bool?)elem.Element("Enabled") ?? false,
                    Mandatory = (bool?)elem.Element("Mandatory") ?? false,
                    AlwaysRun = (bool?)elem.Element("AlwaysRun") ?? false,
                    RunCondition = (string)elem.Element("RunCondition") ?? "",
                    DependsOn = (string)elem.Element("DependsOn") ?? "",
                    Name = (string)elem.Element("Name") ?? "",
                    UpperLimit = (string)elem.Element("UpperLimit") ?? "",
                    LowerLimit = (string)elem.Element("LowerLimit") ?? "",
                    Unit = (string)elem.Element("Unit") ?? "",
                    ExecTime = (string)elem.Element("ExecTime") ?? ""
                }).ToList();

                testItems.Clear();
                foreach (var item in items)
                    testItems.Add(item);
                dgTestItems.ItemsSource = testItems;

                testGroups.Clear();
                if (hasStructuredTestPlan)
                {
                    TestPlanDefinition definition = TestPlanService.LoadDefinition(filePath);
                    testProfiles = TestPlanService.LoadProfiles(filePath);
                    foreach (TestPlanGroupDefinition group in definition.Groups)
                    {
                        testGroups.Add(new TestGroupItem
                        {
                            GroupId = group.GroupId,
                            DisplayName = group.Name,
                            Mandatory = group.Mandatory,
                            DependsOn = group.DependsOn.ToList(),
                            Items = new ObservableCollection<TestItem>(
                                testItems.Where(x => string.Equals(
                                    x.GroupId,
                                    group.GroupId,
                                    StringComparison.OrdinalIgnoreCase))
                                .OrderBy(x => x.SequenceOrder))
                        });
                    }

                    grpTestPlan.Visibility = Visibility.Visible;
                    btnAddStep.IsEnabled = false;
                    btnInsertStep.IsEnabled = false;
                    btnCopyStep.IsEnabled = false;
                    btnDeleteStep.IsEnabled = false;
                    btnMoveUp.IsEnabled = false;
                    btnMoveDown.IsEnabled = false;
                    SelectActiveProfile();
                }
                else
                {
                    grpTestPlan.Visibility = Visibility.Collapsed;
                    btnAddStep.IsEnabled = true;
                    btnInsertStep.IsEnabled = true;
                    btnCopyStep.IsEnabled = true;
                    btnDeleteStep.IsEnabled = true;
                    btnMoveUp.IsEnabled = true;
                    btnMoveDown.IsEnabled = true;
                    foreach (TestItem item in testItems)
                        item.CanToggle = true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载测试数据失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveTestDataToXml()
        {
            CommitGridEdit();

            if (!(cmbTestProjects.SelectedItem is TestProject selected))
            {
                MessageBox.Show("请先选择一个测试项目。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string filePath = ResolveFilePath(selected.FilePath);

            try
            {
                // 保存前校验
                for (int i = 0; i < testItems.Count; i++)
                {
                    var item = testItems[i];

                    if (string.IsNullOrWhiteSpace(item.Name))
                    {
                        MessageBox.Show(
                            $"第 {i + 1} 行测试项名称不能为空。",
                            "保存失败",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);

                        dgTestItems.SelectedItem = item;
                        dgTestItems.ScrollIntoView(item);
                        return;
                    }
                }

                XDocument doc;
                if (hasStructuredTestPlan)
                {
                    doc = XDocument.Load(filePath);
                    List<XElement> elements = doc.Root.Elements("TestItem").ToList();
                    if (elements.Count != testItems.Count)
                    {
                        throw new TestPlanConfigurationException(
                            "Structured test plan item count changed. StepId plans cannot be inserted or deleted here.");
                    }

                    for (int index = 0; index < testItems.Count; index++)
                    {
                        TestItem item = testItems[index];
                        XElement elem = elements[index];
                        SetElementValue(elem, "Enabled", item.Enabled);
                        SetElementValue(elem, "Name", item.Name?.Trim() ?? "");
                        SetElementValue(elem, "UpperLimit", item.UpperLimit ?? "");
                        SetElementValue(elem, "LowerLimit", item.LowerLimit ?? "");
                        SetElementValue(elem, "Unit", item.Unit ?? "");
                    }

                    if (cmbTestProfile.SelectedItem is TestProfileOption selectedProfile)
                    {
                        TestPlanRuntimeState.ActiveProfileId = selectedProfile.ProfileId;
                        TestPlanRuntimeState.SetStepOverrides(
                            selectedProfile.ProfileId == TestProfileIds.Normal
                                ? null
                                : testItems.ToDictionary(
                                    x => x.StepId,
                                    x => x.Enabled,
                                    StringComparer.OrdinalIgnoreCase));
                    }
                }
                else
                {
                    doc = new XDocument(new XElement("TestItems"));
                    foreach (var item in testItems)
                    {
                        XElement elem = new XElement("TestItem",
                            new XElement("Enabled", item.Enabled),
                            new XElement("Name", item.Name?.Trim() ?? ""),
                            new XElement("UpperLimit", item.UpperLimit ?? ""),
                            new XElement("LowerLimit", item.LowerLimit ?? ""),
                            new XElement("Unit", item.Unit ?? "")
                        );

                        if (!string.IsNullOrEmpty(item.ExecTime))
                            elem.Add(new XElement("ExecTime", item.ExecTime));

                        doc.Root.Add(elem);
                    }
                }

                string dir = Path.GetDirectoryName(filePath);

                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                doc.Save(filePath);

                MessageBox.Show("测试项保存成功！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);

                // 保存后重新加载，确保界面和 XML 文件一致
                LoadTestDataFromXml(filePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void SetElementValue(XElement parent, string name, object value)
        {
            XElement element = parent.Element(name);
            if (element == null)
            {
                element = new XElement(name);
                parent.Add(element);
            }
            element.Value = Convert.ToString(value) ?? "";
        }

        private void SelectActiveProfile()
        {
            string profileId = TestPlanRuntimeState.ActiveProfileId;
            TestProfileOption option = cmbTestProfile.Items
                .Cast<TestProfileOption>()
                .FirstOrDefault(x => string.Equals(
                    x.ProfileId,
                    profileId,
                    StringComparison.OrdinalIgnoreCase));
            cmbTestProfile.SelectedItem = option ?? cmbTestProfile.Items[0];
            ApplySelectedProfile();
        }

        private void CmbTestProfile_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!hasStructuredTestPlan || applyingProfile)
                return;

            ApplySelectedProfile();
        }

        private void ApplySelectedProfile()
        {
            if (!(cmbTestProfile.SelectedItem is TestProfileOption option) ||
                !testProfiles.TryGetValue(option.ProfileId, out TestProfileDefinition profile))
            {
                return;
            }

            applyingProfile = true;
            try
            {
                Dictionary<string, bool> currentOverrides =
                    string.Equals(
                        TestPlanRuntimeState.ActiveProfileId,
                        option.ProfileId,
                        StringComparison.OrdinalIgnoreCase)
                        ? TestPlanRuntimeState.GetStepOverridesSnapshot()
                        : new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

                foreach (TestGroupItem group in testGroups)
                {
                    bool enabled = profile.UseBaselineDefaults
                        ? testItems.Any(x => string.Equals(
                            x.GroupId,
                            group.GroupId,
                            StringComparison.OrdinalIgnoreCase) && x.DefaultEnabled)
                        : profile.GroupStates.TryGetValue(group.GroupId, out bool groupState) &&
                          groupState;

                    if (group.Mandatory)
                        enabled = true;

                    group.Enabled = enabled;
                    group.CanToggle = profile.AllowGroupOverride && !group.Mandatory;
                    if (!enabled)
                        group.IsExpanded = false;
                }

                foreach (TestItem item in testItems)
                {
                    TestGroupItem group = testGroups.FirstOrDefault(x => string.Equals(
                        x.GroupId,
                        item.GroupId,
                        StringComparison.OrdinalIgnoreCase));
                    bool enabled = profile.UseBaselineDefaults
                        ? item.DefaultEnabled
                        : group?.Enabled == true;

                    if (!profile.ResetFromBaselineEveryRun &&
                        currentOverrides.TryGetValue(item.StepId, out bool overrideState))
                    {
                        enabled = overrideState;
                    }

                    item.Enabled = item.Mandatory || item.AlwaysRun || enabled;
                    item.CanToggle = profile.AllowStepOverride &&
                                     !item.Mandatory &&
                                     !item.AlwaysRun;
                }

                if (profile.ResetFromBaselineEveryRun)
                {
                    txtProfileHint.Text = "常规：每次开始测试均从 XML 基线恢复；大项和子项不可临时跳过。";
                }
                else if (profile.AllowStepOverride)
                {
                    txtProfileHint.Text = "工程：可按大项或单个 StepId 启停；不可跳过项始终执行。";
                }
                else
                {
                    txtProfileHint.Text = "返修：按章节大项启停；不可跳过项始终执行。";
                }
            }
            finally
            {
                applyingProfile = false;
            }
        }

        private void GroupEnabled_Click(object sender, RoutedEventArgs e)
        {
            if (applyingProfile || !(sender is CheckBox checkBox) ||
                !(checkBox.Tag is TestGroupItem group) || !group.CanToggle)
            {
                return;
            }

            group.Enabled = checkBox.IsChecked == true;
            group.IsExpanded = group.Enabled;
            if (group.Enabled)
                EnableGroupDependencies(group);
            else
                DisableDependentGroups(group.GroupId);

            foreach (TestItem item in testItems.Where(x => string.Equals(
                x.GroupId,
                group.GroupId,
                StringComparison.OrdinalIgnoreCase)))
            {
                if (!item.Mandatory && !item.AlwaysRun)
                    item.Enabled = group.Enabled;
            }
        }

        private void TvTestGroups_SelectedItemChanged(
            object sender,
            RoutedPropertyChangedEventArgs<object> e)
        {
            TestItem target = e.NewValue as TestItem;
            if (target == null && e.NewValue is TestGroupItem group)
                target = group.Items.FirstOrDefault();

            if (target == null)
                return;

            dgTestItems.SelectedItem = target;
            dgTestItems.ScrollIntoView(target);
        }

        private void EnableGroupDependencies(TestGroupItem group)
        {
            EnableGroupDependencies(
                group,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        private void EnableGroupDependencies(
            TestGroupItem group,
            HashSet<string> visited)
        {
            if (group == null || !visited.Add(group.GroupId))
                return;

            foreach (string dependencyId in group.DependsOn)
            {
                TestGroupItem dependency = testGroups.FirstOrDefault(x => string.Equals(
                    x.GroupId,
                    dependencyId,
                    StringComparison.OrdinalIgnoreCase));
                if (dependency == null || dependency.Enabled)
                    continue;

                dependency.Enabled = true;
                foreach (TestItem item in testItems.Where(x => string.Equals(
                    x.GroupId,
                    dependency.GroupId,
                    StringComparison.OrdinalIgnoreCase)))
                {
                    item.Enabled = true;
                }
                EnableGroupDependencies(dependency, visited);
            }
        }

        private void DisableDependentGroups(string groupId)
        {
            DisableDependentGroups(
                groupId,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        private void DisableDependentGroups(
            string groupId,
            HashSet<string> visited)
        {
            if (!visited.Add(groupId))
                return;

            foreach (TestGroupItem dependent in testGroups.Where(x =>
                x.Enabled &&
                x.DependsOn.Any(y => string.Equals(
                    y,
                    groupId,
                    StringComparison.OrdinalIgnoreCase))).ToList())
            {
                if (dependent.Mandatory)
                    continue;

                dependent.Enabled = false;
                foreach (TestItem item in testItems.Where(x => string.Equals(
                    x.GroupId,
                    dependent.GroupId,
                    StringComparison.OrdinalIgnoreCase)))
                {
                    if (!item.Mandatory && !item.AlwaysRun)
                        item.Enabled = false;
                }
                DisableDependentGroups(dependent.GroupId, visited);
            }
        }

        private void StepEnabled_Click(object sender, RoutedEventArgs e)
        {
            if (applyingProfile || !(sender is CheckBox checkBox) ||
                !(checkBox.DataContext is TestItem item) || !item.CanToggle)
            {
                return;
            }

            item.Enabled = checkBox.IsChecked == true;
            if (item.Enabled)
                EnableStepDependencies(item);
            else
                DisableDependentSteps(item.StepId);

            RefreshGroupState(item.GroupId);
        }

        private void EnableStepDependencies(TestItem item)
        {
            EnableStepDependencies(
                item,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        private void EnableStepDependencies(TestItem item, HashSet<string> visited)
        {
            if (item == null || !visited.Add(item.StepId))
                return;

            foreach (string dependencyId in SplitDependencies(item.DependsOn))
            {
                TestItem dependencyStep = testItems.FirstOrDefault(x => string.Equals(
                    x.StepId,
                    dependencyId,
                    StringComparison.OrdinalIgnoreCase));
                if (dependencyStep != null)
                {
                    dependencyStep.Enabled = true;
                    EnableStepDependencies(dependencyStep, visited);
                    RefreshGroupState(dependencyStep.GroupId);
                    continue;
                }

                TestGroupItem dependencyGroup = testGroups.FirstOrDefault(x => string.Equals(
                    x.GroupId,
                    dependencyId,
                    StringComparison.OrdinalIgnoreCase));
                if (dependencyGroup != null)
                {
                    dependencyGroup.Enabled = true;
                    foreach (TestItem groupStep in testItems.Where(x => string.Equals(
                        x.GroupId,
                        dependencyGroup.GroupId,
                        StringComparison.OrdinalIgnoreCase)))
                    {
                        groupStep.Enabled = true;
                    }
                    EnableGroupDependencies(dependencyGroup);
                }
            }
        }

        private void DisableDependentSteps(string stepId)
        {
            DisableDependentSteps(
                stepId,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        private void DisableDependentSteps(
            string stepId,
            HashSet<string> visited)
        {
            if (!visited.Add(stepId))
                return;

            foreach (TestItem dependent in testItems.Where(x =>
                x.Enabled &&
                SplitDependencies(x.DependsOn).Any(y => string.Equals(
                    y,
                    stepId,
                    StringComparison.OrdinalIgnoreCase))).ToList())
            {
                if (dependent.Mandatory || dependent.AlwaysRun)
                    continue;

                dependent.Enabled = false;
                DisableDependentSteps(dependent.StepId, visited);
                RefreshGroupState(dependent.GroupId);
            }
        }

        private void RefreshGroupState(string groupId)
        {
            TestGroupItem group = testGroups.FirstOrDefault(x => string.Equals(
                x.GroupId,
                groupId,
                StringComparison.OrdinalIgnoreCase));
            if (group != null)
            {
                group.Enabled = testItems.Any(x =>
                    x.Enabled &&
                    string.Equals(x.GroupId, groupId, StringComparison.OrdinalIgnoreCase));
            }
        }

        private static IEnumerable<string> SplitDependencies(string value)
        {
            return (value ?? "")
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x));
        }

        private void ChkSelectAll_Click(object sender, RoutedEventArgs e)
        {
            var checkBox = sender as CheckBox;
            if (checkBox == null) return;
            bool isChecked = checkBox.IsChecked == true;
            foreach (var item in testItems)
            {
                if (item.CanToggle)
                    item.Enabled = isChecked;
            }
        }
        private void CommitGridEdit()
        {
            dgTestItems.CommitEdit(DataGridEditingUnit.Cell, true);
            dgTestItems.CommitEdit(DataGridEditingUnit.Row, true);
        }
        private TestItem CreateDefaultTestItem(string name = "新测试项")
        {
            return new TestItem
            {
                Enabled = true,
                Name = name,
                UpperLimit = "",
                LowerLimit = "",
                Unit = "",
                ExecTime = ""
            };
        }
        private void BtnAddProject_Click(object sender, RoutedEventArgs e)
        {
            var editWin = new ProjectEditWindow();
            editWin.Owner = this;

            if (editWin.ShowDialog() == true)
            {
                string relativePath = ConvertToRelativePath(editWin.FilePath);

                var newProject = new TestProject
                {
                    GroupName = editWin.GroupName,
                    ModelName = editWin.ModelName,
                    DisplayName = editWin.DisplayName,
                    FilePath = relativePath,
                    UpdateUrl = editWin.UpdateUrl,

                    Runner = editWin.Runner,
                    SequenceKey = editWin.SequenceKey,
                    ComInitKey = editWin.ComInitKey,
                    RequiredComPorts = editWin.RequiredComPorts,
                    FtpBasePath = editWin.FtpBasePath
                };

                projects.Add(newProject);

                SaveProjectList();

                LoadProjectGroups();

                cmbProjectGroups.SelectedItem = newProject.GroupName;
                FilterProjectsByGroup(newProject.GroupName);
                cmbTestProjects.SelectedItem = newProject;
            }
        }

        private void BtnEditProject_Click(object sender, RoutedEventArgs e)
        {
            if (!(cmbTestProjects.SelectedItem is TestProject selected))
            {
                MessageBox.Show("请先选择要编辑的项目。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var editWin = new ProjectEditWindow(
                displayName: selected.DisplayName,
                filePath: selected.FilePath,
                updateUrl: selected.UpdateUrl,
                groupName: selected.GroupName,
                modelName: selected.ModelName,
                runner: selected.Runner,
                sequenceKey: selected.SequenceKey,
                comInitKey: selected.ComInitKey,
                ftpBasePath: selected.FtpBasePath,
                requiredComPorts: selected.RequiredComPorts
            );

            editWin.Owner = this;

            if (editWin.ShowDialog() == true)
            {
                string relativePath = ConvertToRelativePath(editWin.FilePath);

                selected.GroupName = editWin.GroupName;
                selected.ModelName = editWin.ModelName;
                selected.DisplayName = editWin.DisplayName;
                selected.FilePath = relativePath;
                selected.UpdateUrl = editWin.UpdateUrl;

                selected.Runner = editWin.Runner;
                selected.SequenceKey = editWin.SequenceKey;
                selected.ComInitKey = editWin.ComInitKey;
                selected.RequiredComPorts = editWin.RequiredComPorts;
                selected.FtpBasePath = editWin.FtpBasePath;

                SaveProjectList();

                LoadProjectGroups();

                cmbProjectGroups.SelectedItem = selected.GroupName;
                FilterProjectsByGroup(selected.GroupName);
                cmbTestProjects.SelectedItem = selected;
            }
        }

        private void BtnDeleteProject_Click(object sender, RoutedEventArgs e)
        {
            if (!(cmbTestProjects.SelectedItem is TestProject selected))
            {
                MessageBox.Show("请先选择一个项目。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (MessageBox.Show($"确定要删除项目 \"{selected.DisplayName}\" 吗？\n注意：不会删除关联的XML文件。", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                projects.Remove(selected);
                SaveProjectList();
                LoadProjectGroups();

                if (projects.Count == 0)
                {
                    testItems.Clear();
                    dgTestItems.ItemsSource = null;
                }
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            SaveTestDataToXml();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (cmbTestProjects.SelectedItem is TestProject selected)
                ProjectSettings.CurrentProjectName = selected.DisplayName;
            DialogResult = true;  // 告诉主窗口数据已变更
            Close();
        }
        private void BtnAddStep_Click(object sender, RoutedEventArgs e)
        {
            CommitGridEdit();

            var newItem = CreateDefaultTestItem();
            testItems.Add(newItem);

            dgTestItems.ItemsSource = testItems;
            dgTestItems.SelectedItem = newItem;
            dgTestItems.ScrollIntoView(newItem);

            Dispatcher.BeginInvoke(new Action(() =>
            {
                dgTestItems.CurrentCell = new DataGridCellInfo(newItem, dgTestItems.Columns[1]);
                dgTestItems.BeginEdit();
            }));
        }
        private void BtnInsertStep_Click(object sender, RoutedEventArgs e)
        {
            CommitGridEdit();

            int index = dgTestItems.SelectedIndex;

            if (index < 0)
            {
                index = testItems.Count;
            }

            var newItem = CreateDefaultTestItem();
            testItems.Insert(index, newItem);

            dgTestItems.ItemsSource = testItems;
            dgTestItems.SelectedItem = newItem;
            dgTestItems.ScrollIntoView(newItem);

            Dispatcher.BeginInvoke(new Action(() =>
            {
                dgTestItems.CurrentCell = new DataGridCellInfo(newItem, dgTestItems.Columns[1]);
                dgTestItems.BeginEdit();
            }));
        }
        private void BtnCopyStep_Click(object sender, RoutedEventArgs e)
        {
            CommitGridEdit();

            if (!(dgTestItems.SelectedItem is TestItem selectedItem))
            {
                MessageBox.Show("请先选择要复制的测试步骤。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int index = testItems.IndexOf(selectedItem);

            var copyItem = new TestItem
            {
                Enabled = selectedItem.Enabled,
                Name = selectedItem.Name + "_复制",
                UpperLimit = selectedItem.UpperLimit,
                LowerLimit = selectedItem.LowerLimit,
                Unit = selectedItem.Unit,
                ExecTime = ""
            };

            testItems.Insert(index + 1, copyItem);

            dgTestItems.SelectedItem = copyItem;
            dgTestItems.ScrollIntoView(copyItem);
        }
        private void BtnDeleteStep_Click(object sender, RoutedEventArgs e)
        {
            CommitGridEdit();

            var selectedItems = dgTestItems.SelectedItems.Cast<TestItem>().ToList();

            if (selectedItems.Count == 0)
            {
                MessageBox.Show("请先选择要删除的测试步骤。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string message = selectedItems.Count == 1
                ? $"确定要删除测试步骤 \"{selectedItems[0].Name}\" 吗？"
                : $"确定要删除选中的 {selectedItems.Count} 个测试步骤吗？";

            if (MessageBox.Show(message, "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            int firstIndex = testItems.IndexOf(selectedItems[0]);

            foreach (var item in selectedItems)
            {
                testItems.Remove(item);
            }

            if (testItems.Count > 0)
            {
                int newIndex = Math.Min(firstIndex, testItems.Count - 1);
                dgTestItems.SelectedItem = testItems[newIndex];
                dgTestItems.ScrollIntoView(testItems[newIndex]);
            }
        }
        private void BtnMoveDown_Click(object sender, RoutedEventArgs e)
        {
            CommitGridEdit();

            if (!(dgTestItems.SelectedItem is TestItem selectedItem))
            {
                MessageBox.Show("请先选择要下移的测试步骤。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int index = testItems.IndexOf(selectedItem);

            if (index < 0 || index >= testItems.Count - 1)
            {
                return;
            }

            testItems.Move(index, index + 1);

            dgTestItems.SelectedItem = selectedItem;
            dgTestItems.ScrollIntoView(selectedItem);
        }
        private void BtnMoveUp_Click(object sender, RoutedEventArgs e)
        {
            CommitGridEdit();

            if (!(dgTestItems.SelectedItem is TestItem selectedItem))
            {
                MessageBox.Show("请先选择要上移的测试步骤。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int index = testItems.IndexOf(selectedItem);

            if (index <= 0)
            {
                return;
            }

            testItems.Move(index, index - 1);

            dgTestItems.SelectedItem = selectedItem;
            dgTestItems.ScrollIntoView(selectedItem);
        }
        protected override void OnClosing(CancelEventArgs e)
        {
            if (cmbTestProjects.SelectedItem is TestProject selected)
            {
                SelectedProjectName = selected.DisplayName;
                System.Diagnostics.Debug.WriteLine($"配置窗口关闭，SelectedProjectName={SelectedProjectName}");
            }
            else
            {
                SelectedProjectName = "";
            }
            base.OnClosing(e);
        }

        private void dgTestItems_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            e.Row.Header = (e.Row.GetIndex() + 1).ToString();
        }
    }

    // TestItem 类的定义（必须存在）
    public class TestItem : INotifyPropertyChanged
    {
        private bool _enabled;
        private bool _canToggle = true;
        private string _name;
        private string _upperLimit;
        private string _lowerLimit;
        private string _unit;
        private string _execTime;

        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; OnPropertyChanged(); }
        }
        public bool CanToggle
        {
            get => _canToggle;
            set { _canToggle = value; OnPropertyChanged(); }
        }
        public string StepId { get; set; }
        public string GroupId { get; set; }
        public int SequenceOrder { get; set; }
        public bool DefaultEnabled { get; set; }
        public bool Mandatory { get; set; }
        public bool AlwaysRun { get; set; }
        public string RunCondition { get; set; }
        public string DependsOn { get; set; }
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }
        public string UpperLimit
        {
            get => _upperLimit;
            set { _upperLimit = value; OnPropertyChanged(); }
        }
        public string LowerLimit
        {
            get => _lowerLimit;
            set { _lowerLimit = value; OnPropertyChanged(); }
        }
        public string Unit
        {
            get => _unit;
            set { _unit = value; OnPropertyChanged(); }
        }
        public string ExecTime
        {
            get => _execTime;
            set { _execTime = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class TestProfileOption
    {
        public TestProfileOption(string profileId, string name)
        {
            ProfileId = profileId;
            Name = name;
        }

        public string ProfileId { get; }
        public string Name { get; }
    }

    public sealed class TestGroupItem : INotifyPropertyChanged
    {
        private bool _enabled;
        private bool _canToggle;
        private bool _isExpanded;

        public string GroupId { get; set; }
        public string DisplayName { get; set; }
        public bool Mandatory { get; set; }
        public List<string> DependsOn { get; set; } = new List<string>();
        public ObservableCollection<TestItem> Items { get; set; } =
            new ObservableCollection<TestItem>();

        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value)
                    return;
                _enabled = value;
                OnPropertyChanged(nameof(Enabled));
            }
        }

        public bool CanToggle
        {
            get => _canToggle;
            set
            {
                if (_canToggle == value)
                    return;
                _canToggle = value;
                OnPropertyChanged(nameof(CanToggle));
            }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value)
                    return;
                _isExpanded = value;
                OnPropertyChanged(nameof(IsExpanded));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
