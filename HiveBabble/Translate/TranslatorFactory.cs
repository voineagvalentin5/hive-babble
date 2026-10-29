using HiveBabble.Settings;
namespace HiveBabble.Translate;
public static class TranslatorFactory
{
    public static ITranslator Create(AppSettings s) => s.Provider switch
    {
        TranslatorProvider.DeepL => new DeepLTranslator(s.GetApiKey()),
        TranslatorProvider.Google => new GoogleTranslator(s.GetApiKey()),
        TranslatorProvider.Libre => new LibreTranslator(s.LibreUrl),
        _ => throw new TranslatorException("Choose a translation provider in Settings."),
    };
}
