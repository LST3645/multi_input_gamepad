using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using MultiInputGamepad.Core;

namespace MultiInputGamepad.App;

/// <summary>一套完整配置（映射规则 + 可调参数），落盘为一个 JSON 文件。</summary>
public sealed class ProfileConfig
{
    public int Version { get; set; } = 1;

    /// <summary>配置名（= profiles 目录下的文件名，不含扩展名）。</summary>
    public string Name { get; set; } = "";

    /// <summary>导入时的原始文件名（用于关闭窗口时写入 cache 应急备份）；非导入为 null。</summary>
    public string? SourceFileName { get; set; }

    public List<MappingRule> Mappings { get; set; } = new();

    /// <summary>「归一化摇杆」开关。</summary>
    public bool NormalizeSticks { get; set; } = true;

    /// <summary>物理摇杆死区参数。</summary>
    public StickDeadzoneConfig Deadzone { get; set; } = new();
}

/// <summary>
/// 整合配置的持久化：
/// <para>`profiles\&lt;名称&gt;.json` 保存多套配置（映射 + 归一化开关 + 死区参数）；</para>
/// <para>`cache\&lt;导入时的文件名&gt;.json` 在退出时写入导入配置的应急备份；</para>
/// <para>`settings.json` 只保存「当前激活配置名」这类 app 状态。</para>
/// </summary>
public static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // 中文按原样写出，便于手改
        Converters = { new JsonStringEnumConverter() },
    };

    public const string DefaultProfileName = "default";

    private sealed class AppState
    {
        public string ActiveProfile { get; set; } = DefaultProfileName;
    }

    private sealed class LegacySettings
    {
        public bool NormalizeSticks { get; set; } = true;
        public StickDeadzoneConfig? Deadzone { get; set; }
    }

    public static string Root { get; } = ResolveRoot();
    public static string ProfilesDir => Path.Combine(Root, "profiles");
    public static string CacheDir => Path.Combine(Root, "cache");
    public static string StatePath => Path.Combine(Root, "settings.json");
    private static string LegacyMappingsPath => Path.Combine(Root, "mappings.json");

    /// <summary>数据目录名（项目标识；改名后需与仓库名一致）。</summary>
    private const string DataDirectoryName = "multi_input_gamepad";

    /// <summary>改名前的数据目录名，仅用于一次性搬迁。</summary>
    private const string LegacyDataDirectoryName = "MultiInput";

    private static string ResolveRoot()
    {
        string? custom = Environment.GetEnvironmentVariable("MI_MAPPINGS");
        if (!string.IsNullOrWhiteSpace(custom))
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(custom));
            return string.IsNullOrEmpty(dir) ? "." : dir;
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DataDirectoryName);
    }

    /// <summary>项目改名后把旧数据目录整体搬到新目录（仅当新目录不存在且旧目录存在）。</summary>
    private static void MigrateRenamedRoot()
    {
        if (Environment.GetEnvironmentVariable("MI_MAPPINGS") is { Length: > 0 }) return;   // 测试重定向时不碰真实目录

        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string oldRoot = Path.Combine(localAppData, LegacyDataDirectoryName);

            if (!Directory.Exists(Root) && Directory.Exists(oldRoot))
                Directory.Move(oldRoot, Root);
        }
        catch
        {
            // 搬迁失败时按空配置继续运行，不阻断启动
        }
    }

    // —— 当前激活配置名（app 状态）——

    public static string LoadActiveName()
    {
        try
        {
            if (!File.Exists(StatePath)) return DefaultProfileName;
            var state = JsonSerializer.Deserialize<AppState>(File.ReadAllText(StatePath), Options);
            return string.IsNullOrWhiteSpace(state?.ActiveProfile) ? DefaultProfileName : state.ActiveProfile;
        }
        catch
        {
            return DefaultProfileName;
        }
    }

    public static void SaveActiveName(string name)
    {
        try
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(new AppState { ActiveProfile = name }, Options));
        }
        catch
        {
            // 状态写入失败不阻断运行
        }
    }

    // —— 配置列表与读写 ——

    public static List<string> ListProfiles()
    {
        try
        {
            if (!Directory.Exists(ProfilesDir)) return new List<string>();
            return Directory.GetFiles(ProfilesDir, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .OrderBy(name => name, StringComparer.CurrentCulture)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    public static ProfileConfig? Load(string name, out string? error)
    {
        error = null;
        try
        {
            string path = PathFor(name);
            if (!File.Exists(path))
            {
                error = "文件不存在";
                return null;
            }

            if (ReadAny(File.ReadAllText(path)) is not { } config)
            {
                error = "解析失败";
                return null;
            }

            config.Name = name;
            Normalize(config);
            return config;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name;
            return null;
        }
    }

    public static void Save(ProfileConfig config)
    {
        try
        {
            Directory.CreateDirectory(ProfilesDir);
            File.WriteAllText(PathFor(config.Name), JsonSerializer.Serialize(config, Options));
        }
        catch
        {
            // 持久化失败（只读目录/无权限）不阻断运行
        }
    }

    public static bool Exists(string name) => File.Exists(PathFor(name));

    /// <summary>确保默认配置存在（不存在则静默新建一份空配置，作为稳定兜底）。</summary>
    public static void EnsureDefaultProfile()
    {
        try
        {
            if (Exists(DefaultProfileName)) return;
            Save(new ProfileConfig { Name = DefaultProfileName });
        }
        catch
        {
            // 建不出来就继续用当前配置运行
        }
    }

    /// <summary>删除一个配置（默认配置由调用方拦截）。</summary>
    public static bool Delete(string name, out string? error)
    {
        error = null;
        try
        {
            string path = PathFor(name);
            if (!File.Exists(path))
            {
                error = "配置不存在";
                return false;
            }

            File.Delete(path);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name;
            return false;
        }
    }

    /// <summary>重命名配置（校验非法字符与重名）。</summary>
    public static bool Rename(string oldName, string newName, out string? error)
    {
        error = null;
        try
        {
            newName = newName.Trim();
            if (newName.Length == 0)
            {
                error = "名称不能为空";
                return false;
            }

            if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                error = "名称包含非法字符";
                return false;
            }

            if (string.Equals(oldName, newName, StringComparison.CurrentCulture)) return true;

            bool sameIgnoringCase = string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase);
            if (!sameIgnoringCase && Exists(newName))
            {
                error = "已存在同名配置";
                return false;
            }

            if (Load(oldName, out string? loadError) is not { } config)
            {
                error = loadError ?? "读取失败";
                return false;
            }

            string from = PathFor(oldName);
            config.Name = newName;
            Save(config);

            if (sameIgnoringCase)
            {
                // 仅大小写变化：路径大小写不敏感，Save 已更新内容，这里补一次改名
                try
                {
                    File.Move(from, PathFor(newName));
                }
                catch
                {
                    // 改名失败保持原名
                }
                return true;
            }

            try
            {
                File.Delete(from);
            }
            catch
            {
                // 旧文件删不掉时保留（新文件已写入，不影响使用）
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name;
            return false;
        }
    }

    /// <summary>把外部文件导入为**新配置**（重名自动加序号），返回落盘后的配置。</summary>
    public static ProfileConfig? Import(string path, out string? error)
    {
        error = null;
        try
        {
            if (ReadAny(File.ReadAllText(path)) is not { } config)
            {
                error = "无法解析该文件";
                return null;
            }

            string stem = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(stem)) stem = "导入";

            config.Name = UniqueName(stem);
            config.SourceFileName = Path.GetFileName(path);
            Normalize(config);
            Save(config);
            return config;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name;
            return null;
        }
    }

    /// <summary>写到用户指定路径；同名覆盖由保存对话框确认。</summary>
    public static bool Export(ProfileConfig config, string path, out string? error)
    {
        error = null;
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            File.WriteAllText(path, JsonSerializer.Serialize(config, Options));
            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name;
            return false;
        }
    }

    /// <summary>应急备份：把导入过的配置按**导入时的原始文件名**写入 cache 目录。</summary>
    public static void BackupImported(IEnumerable<ProfileConfig> configs)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            foreach (var config in configs)
            {
                if (string.IsNullOrWhiteSpace(config.SourceFileName)) continue;
                File.WriteAllText(Path.Combine(CacheDir, config.SourceFileName),
                    JsonSerializer.Serialize(config, Options));
            }
        }
        catch
        {
            // 备份失败不影响退出
        }
    }

    /// <summary>旧版 `mappings.json` + `settings.json` → `profiles\默认.json`（仅在没有配置时执行一次）。</summary>
    public static void MigrateLegacyIfNeeded()
    {
        try
        {
            MigrateRenamedRoot();
            Directory.CreateDirectory(ProfilesDir);
            MigrateDefaultProfileName();

            if (ListProfiles().Count > 0) return;

            var config = new ProfileConfig { Name = DefaultProfileName };

            if (File.Exists(LegacyMappingsPath))
                config.Mappings = ReadRules(File.ReadAllText(LegacyMappingsPath));

            if (TryReadLegacySettings(ReadAllTextOrNull(StatePath)) is { } legacy)
            {
                config.NormalizeSticks = legacy.NormalizeSticks;
                if (legacy.Deadzone is not null) config.Deadzone = legacy.Deadzone;
            }

            Save(config);
            SaveActiveName(DefaultProfileName);

            if (File.Exists(LegacyMappingsPath))
                File.Move(LegacyMappingsPath, LegacyMappingsPath + ".migrated", overwrite: true);
        }
        catch
        {
            // 迁移失败时按默认配置继续运行
        }
    }

    // —— 内部实现 ——

    /// <summary>旧版默认配置名「默认」→「default」（仅在目标不存在时改名）。</summary>
    private static void MigrateDefaultProfileName()
    {
        try
        {
            string legacy = PathFor("默认");
            string target = PathFor(DefaultProfileName);
            if (!File.Exists(legacy) || File.Exists(target)) return;

            bool activeIsLegacy = string.Equals(LoadActiveName(), "默认", StringComparison.CurrentCulture);
            File.Move(legacy, target, overwrite: false);
            if (activeIsLegacy) SaveActiveName(DefaultProfileName);
        }
        catch
        {
            // 改名失败则保持原名继续使用
        }
    }

    private static string PathFor(string name) => Path.Combine(ProfilesDir, name + ".json");

    private static string UniqueName(string stem)
    {
        var existing = new HashSet<string>(ListProfiles(), StringComparer.CurrentCultureIgnoreCase);
        if (!existing.Contains(stem)) return stem;

        for (int i = 2; i < 1000; i++)
        {
            string candidate = $"{stem} ({i})";
            if (!existing.Contains(candidate)) return candidate;
        }

        return $"{stem} ({DateTime.Now:HHmmss})";
    }

    /// <summary>接受两种格式：完整配置对象，或旧版「裸规则数组」。</summary>
    private static ProfileConfig? ReadAny(string json)
    {
        try
        {
            var config = JsonSerializer.Deserialize<ProfileConfig>(json, Options);
            if (config is not null) return config;
        }
        catch
        {
            // 落到下面的数组格式
        }

        try
        {
            var rules = JsonSerializer.Deserialize<List<MappingRule>>(json, Options);
            if (rules is not null) return new ProfileConfig { Mappings = rules };
        }
        catch
        {
            // 两种格式都不匹配
        }

        return null;
    }

    private static List<MappingRule> ReadRules(string json)
    {
        try
        {
            var config = new ProfileConfig
            {
                Mappings = JsonSerializer.Deserialize<List<MappingRule>>(json, Options) ?? new List<MappingRule>(),
            };
            Normalize(config);
            return config.Mappings;
        }
        catch
        {
            return new List<MappingRule>();
        }
    }

    private static LegacySettings? TryReadLegacySettings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<LegacySettings>(json, Options);
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadAllTextOrNull(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>丢弃旧格式规则与使用黑名单键的规则，并规整参数。</summary>
    private static void Normalize(ProfileConfig config)
    {
        config.Mappings ??= new List<MappingRule>();
        config.Deadzone ??= new StickDeadzoneConfig();
        config.Deadzone.Sanitize();

        config.Mappings.RemoveAll(rule => rule.Source == MappingSource.MouseAxis
            ? rule.AxisX < 0 && rule.AxisY < 0
            : rule.Outputs.Count == 0);

        config.Mappings.RemoveAll(rule =>
            rule.Source == MappingSource.Keyboard && KeyBlacklist.Contains(rule.KeyCode));
    }
}
