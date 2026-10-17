using System.Text;

namespace SecureLab.Api.Application.Search;

// ILIKE має свою мову шаблонів: % означає будь-скільки символів, _ рівно один.
// Параметр SQL не робить їх буквальними, тому перед передачею їх треба екранувати.
public static class IlikeText
{
    // Символ екранування для ILike(..., escapeCharacter). Беру "!", а не "\":
    // його не треба подвоювати в рядках C#, а в звичайних заголовках він трапляється рідко.
    public const char Mark = '!';
    public static readonly string MarkText = Mark.ToString();

    // "50%_off!" -> "%50!%!_off!!%": рядок шукається як є, у будь-якому місці тексту.
    public static string AnywhereLiteral(string raw)
    {
        var pattern = new StringBuilder(raw.Length * 2 + 2).Append('%');
        foreach (var symbol in raw)
        {
            // Сам "!" теж екраную, інакше він «з'їв би» наступний символ шаблону.
            if (symbol is Mark or '%' or '_')
            {
                pattern.Append(Mark);
            }
            pattern.Append(symbol);
        }
        return pattern.Append('%').ToString();
    }
}
