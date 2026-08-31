namespace DeepSeekPet;

public sealed record SkinOption(string Id, string DisplayName, string ResourcePath)
{
    public Uri ResourceUri { get; } = new(
        $"pack://application:,,,/DeepSeekPet;component/{ResourcePath}",
        UriKind.Absolute);
}

public static class SkinCatalog
{
    public const string DefaultId = "deepseek-girl";

    public static IReadOnlyList<SkinOption> All { get; } =
    [
        new(DefaultId, "DeepSeek 娘", "Assets/Skins/deepseek-girl.png"),
        new("gugugaga", "咕咕嘎嘎", "Assets/Skins/gugugaga.png"),
        new("phoebe", "菲比", "Assets/Skins/phoebe.png"),
        new("chen-qianyu", "啥子龙", "Assets/Skins/chen-qianyu.png"),
        new("mutsumi", "睦子米", "Assets/Skins/mutsumi.png")
    ];

    public static SkinOption Find(string? id) =>
        All.FirstOrDefault(skin => skin.Id == id) ?? All[0];
}
