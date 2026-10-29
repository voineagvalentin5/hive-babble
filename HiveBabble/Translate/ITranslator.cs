using System.Threading.Tasks;
namespace HiveBabble.Translate;
public interface ITranslator
{
    string Name { get; }
    Task<string> TranslateAsync(string text, string? sourceLang, string targetLang);
}