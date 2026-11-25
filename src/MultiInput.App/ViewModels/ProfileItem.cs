namespace MultiInput.App.ViewModels;

/// <summary>配置下拉框的一项：名称 + 是否允许删除（默认配置不可删）。</summary>
public sealed class ProfileItem
{
    public string Name { get; }

    public bool CanDelete { get; }

    public ProfileItem(string name, bool canDelete)
    {
        Name = name;
        CanDelete = canDelete;
    }
}
