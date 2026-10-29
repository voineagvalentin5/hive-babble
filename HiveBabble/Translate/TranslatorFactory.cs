using HiveBabble.Settings;
namespace HiveBabble.Translate;
public static class TranslatorFactory
{
    public static ITranslator Create(AppSettings s) => s.Provider switch
    {
        TranslatorProvider.DeepL => new DeepLTranslator(s.GetApiKey()),
        TranslatorProvider.Google => new GoogleTranslator(),
        _ => new LibreTranslator(s.LibreUrl),
    };
}