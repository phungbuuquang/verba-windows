using verba_windows.Models;

namespace verba_windows.Services;

public interface ITranslationApiService
{
    Task<TranslateResponse> TranslateAsync(TranslateRequest request, CancellationToken cancellationToken);
}
