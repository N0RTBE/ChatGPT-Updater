using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Windows.Markup;

namespace ChatGPTUpdater;

public static class LocalizationService
{
    private static readonly ResourceManager Resources = new(
        "ChatGPTUpdater.Resources.Strings",
        Assembly.GetExecutingAssembly());

    private static readonly HashSet<string> SupportedCultures = new(StringComparer.OrdinalIgnoreCase)
    {
        "en", "ru", "uk", "de", "fr", "es", "it", "pt-BR", "pl", "tr",
        "nl", "cs", "ja", "ko", "zh-Hans", "zh-Hant", "ar", "hi", "id", "vi"
    };

    public static CultureInfo CurrentUICulture { get; private set; } = CultureInfo.GetCultureInfo("en");

    public static bool IsRightToLeft => CurrentUICulture.TextInfo.IsRightToLeft;

    public static void InitializeFromSystem(CultureInfo? systemCulture = null)
    {
        CurrentUICulture = ResolveCulture(systemCulture ?? CultureInfo.CurrentUICulture);
        CultureInfo.CurrentUICulture = CurrentUICulture;
        CultureInfo.DefaultThreadCurrentUICulture = CurrentUICulture;
    }

    public static string Get(string key)
        => Resources.GetString(key, CurrentUICulture)
           ?? Resources.GetString(key, CultureInfo.InvariantCulture)
           ?? $"[{key}]";

    public static string Format(string key, params object?[] arguments)
        => string.Format(CultureInfo.CurrentCulture, Get(key), arguments);

    internal static CultureInfo ResolveCulture(CultureInfo culture)
    {
        var name = culture.Name;
        if (SupportedCultures.Contains(name))
            return CultureInfo.GetCultureInfo(name);

        if (culture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase))
        {
            string[] traditionalRegions = ["TW", "HK", "MO"];
            var isTraditional = name.Contains("Hant", StringComparison.OrdinalIgnoreCase) ||
                                traditionalRegions.Any(region => name.EndsWith($"-{region}", StringComparison.OrdinalIgnoreCase));
            return CultureInfo.GetCultureInfo(isTraditional ? "zh-Hant" : "zh-Hans");
        }

        if (culture.TwoLetterISOLanguageName.Equals("pt", StringComparison.OrdinalIgnoreCase))
            return CultureInfo.GetCultureInfo("pt-BR");

        var neutral = culture.TwoLetterISOLanguageName;
        return SupportedCultures.Contains(neutral)
            ? CultureInfo.GetCultureInfo(neutral)
            : CultureInfo.GetCultureInfo("en");
    }
}

[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; }

    public override object ProvideValue(IServiceProvider serviceProvider)
        => LocalizationService.Get(Key);
}
