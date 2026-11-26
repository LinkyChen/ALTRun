using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace ALTRun.Models
{
    public enum ParamType
    {
        ptNone,
        ptNoEncoding,
        ptURLQuery,
        ptUTF8Query
    }

    /// <summary>
    /// 快捷项数据模型。
    /// 注意：必须是普通 class（身份相等），不能用 record！record 的编译器生成 GetHashCode
    /// 会哈希所有字段值，而本模型的核心字段（ShortCut/Name/CommandLine）在管理界面可编辑——
    /// 保存时字段一变，对象的哈希码就变，WPF DataGrid 内部以对象为键的容器/选中映射
    /// （哈希表）立刻找不到原条目，"移除旧选中"静默失败，留下僵尸选中记录：
    /// 表现为 Single 模式下 SelectedItems 出现 2 条、A/B 两行同时高亮、文本框被残留事件改回旧行。
    /// 用身份相等后哈希码终生不变，该问题从机制上消失。
    /// </summary>
    public sealed class ShortCutItem : INotifyPropertyChanged
    {
        // 管理界面 DataGrid 的四列直接绑定到这些属性。实现 INotifyPropertyChanged 后，
        // 下方文本框修改并保存时单元格会自动刷新，不需要调用 Items.Refresh() 重建所有行容器
        // （重建会让 DataGrid 的视觉选中 / SelectedItem / 文本框三者脱节，表现为选中卡住）。
        public event PropertyChangedEventHandler? PropertyChanged;

        private void Notify([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private string _shortCut = string.Empty;
        private string _name = string.Empty;
        private string _commandLine = string.Empty;
        private int _freq;

        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public string ShortCut
        {
            get => _shortCut;
            set { if (_shortCut != value) { _shortCut = value; Notify(); } }
        }

        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; Notify(); } }
        }

        public string CommandLine
        {
            get => _commandLine;
            set { if (_commandLine != value) { _commandLine = value; Notify(); } }
        }

        public string WorkingDir { get; set; } = string.Empty;

        public int Freq
        {
            get => _freq;
            set { if (_freq != value) { _freq = value; Notify(); } }
        }

        public ParamType ParamType { get; set; } = ParamType.ptNone;

        [JsonIgnore]
        public string PinyinInitials { get; set; } = string.Empty;

        [JsonIgnore]
        public string PinyinShortcut { get; set; } = string.Empty;

        [JsonIgnore]
        public string PinyinFull { get; set; } = string.Empty;

        // 预计算的小写缓存：搜索时直接复用，避免每个字符都执行 ToLowerInvariant 分配新字符串
        [JsonIgnore]
        public string LowerShortCut { get; set; } = string.Empty;

        [JsonIgnore]
        public string LowerName { get; set; } = string.Empty;

        [JsonIgnore]
        public string LowerPinyinInitials { get; set; } = string.Empty;

        [JsonIgnore]
        public string LowerPinyinShortcut { get; set; } = string.Empty;

        [JsonIgnore]
        public ImageSource? IconSource { get; set; }

        [JsonIgnore]
        public string DisplayIndex { get; set; } = string.Empty;

        [JsonIgnore]
        public int MatchScore { get; set; } = 0;

        [JsonIgnore]
        public bool IsSpecialCommand { get; set; } = false;

        /// <summary>
        /// 复制出一个用于展示/搜索的副本：显式复制所有持久化字段与拼音/小写缓存；
        /// 运行时字段（图标、序号、匹配分、特殊命令标记）显式重置——这些属于展示态，
        /// 不应从源项继承，需由搜索/渲染流程重新计算。
        /// 新增字段时务必同步在这里补一行拷贝，避免漏拷。
        /// </summary>
        public ShortCutItem CopyForDisplay()
        {
            return new ShortCutItem
            {
                Id = Id,
                ShortCut = ShortCut,
                Name = Name,
                CommandLine = CommandLine,
                WorkingDir = WorkingDir,
                Freq = Freq,
                ParamType = ParamType,
                PinyinInitials = PinyinInitials,
                PinyinShortcut = PinyinShortcut,
                PinyinFull = PinyinFull,
                LowerShortCut = LowerShortCut,
                LowerName = LowerName,
                LowerPinyinInitials = LowerPinyinInitials,
                LowerPinyinShortcut = LowerPinyinShortcut,
                IconSource = null,
                DisplayIndex = string.Empty,
                MatchScore = 0,
                IsSpecialCommand = false
            };
        }
    }
}
