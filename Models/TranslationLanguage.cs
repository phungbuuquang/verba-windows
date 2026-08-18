namespace verba_windows.Models;

public sealed record TranslationLanguage(string Id, string EnglishName)
{
    public static readonly IReadOnlyList<TranslationLanguage> All =
    [
        new("ar", "Arabic"), new("zh-Hans", "Chinese (Simplified)"), new("zh-Hant", "Chinese (Traditional)"),
        new("nl", "Dutch"), new("en", "English"), new("fr", "French"), new("de", "German"),
        new("hi", "Hindi"), new("id", "Indonesian"), new("it", "Italian"), new("ja", "Japanese"),
        new("ko", "Korean"), new("pl", "Polish"), new("pt-BR", "Portuguese (Brazil)"),
        new("ru", "Russian"), new("es", "Spanish"), new("th", "Thai"), new("tr", "Turkish"),
        new("uk", "Ukrainian"), new("vi", "Vietnamese")
    ];

    public static TranslationLanguage FromId(string id) =>
        All.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? All[4];

    public string Name(AppLanguage language)
    {
        if (language == AppLanguage.En) return EnglishName;
        var names = language == AppLanguage.Vi ? VietnameseNames : KoreanNames;
        return names.TryGetValue(Id, out var name) ? name : EnglishName;
    }

    private static readonly IReadOnlyDictionary<string, string> VietnameseNames = new Dictionary<string, string>
    {
        ["ar"]="Tiếng Ả Rập", ["zh-Hans"]="Tiếng Trung (Giản thể)", ["zh-Hant"]="Tiếng Trung (Phồn thể)",
        ["nl"]="Tiếng Hà Lan", ["en"]="Tiếng Anh", ["fr"]="Tiếng Pháp", ["de"]="Tiếng Đức",
        ["hi"]="Tiếng Hindi", ["id"]="Tiếng Indonesia", ["it"]="Tiếng Ý", ["ja"]="Tiếng Nhật",
        ["ko"]="Tiếng Hàn", ["pl"]="Tiếng Ba Lan", ["pt-BR"]="Tiếng Bồ Đào Nha (Brazil)",
        ["ru"]="Tiếng Nga", ["es"]="Tiếng Tây Ban Nha", ["th"]="Tiếng Thái", ["tr"]="Tiếng Thổ Nhĩ Kỳ",
        ["uk"]="Tiếng Ukraina", ["vi"]="Tiếng Việt"
    };

    private static readonly IReadOnlyDictionary<string, string> KoreanNames = new Dictionary<string, string>
    {
        ["ar"]="아랍어", ["zh-Hans"]="중국어(간체)", ["zh-Hant"]="중국어(번체)", ["nl"]="네덜란드어",
        ["en"]="영어", ["fr"]="프랑스어", ["de"]="독일어", ["hi"]="힌디어", ["id"]="인도네시아어",
        ["it"]="이탈리아어", ["ja"]="일본어", ["ko"]="한국어", ["pl"]="폴란드어",
        ["pt-BR"]="포르투갈어(브라질)", ["ru"]="러시아어", ["es"]="스페인어", ["th"]="태국어",
        ["tr"]="튀르키예어", ["uk"]="우크라이나어", ["vi"]="베트남어"
    };
}
