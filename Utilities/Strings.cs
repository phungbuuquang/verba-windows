using verba_windows.Models;

namespace verba_windows.Utilities;

public sealed class Strings(AppLanguage language)
{
    private string Pick(string en, string vi, string ko) => language switch
    { Models.AppLanguage.Vi => vi, Models.AppLanguage.Ko => ko, _ => en };

    public string AutoDetect => Pick("Auto", "Tự động", "자동");
    public string AutoDetectOnHelp => Pick("Detecting the source language automatically — click to pick one yourself", "Đang tự nhận diện ngôn ngữ nguồn — bấm để chọn thủ công", "원본 언어를 자동으로 감지하는 중 — 직접 선택하려면 클릭하세요");
    public string AutoDetectOffHelp => Pick("Detect the source language automatically", "Tự nhận diện ngôn ngữ nguồn", "원본 언어 자동 감지");
    public string SwapLanguages => Pick("Swap languages", "Đảo chiều ngôn ngữ", "언어 바꾸기");
    public string SwapLanguagesDisabled => Pick("Pick a specific source language to swap", "Chọn ngôn ngữ nguồn cụ thể để đảo chiều", "바꾸려면 원본 언어를 직접 선택하세요");
    public string TrialDaysLeft(int n) => Pick($"{n} days left in trial", $"Còn {n} ngày dùng thử", $"체험판 {n}일 남음");
    public string AppLanguage => Pick("App language", "Ngôn ngữ ứng dụng", "앱 언어");
    public string GlobalShortcut => Pick("Global shortcut", "Phím tắt toàn cục", "전역 단축키");
    public string ShortcutHint => Pick("Click the field, then press a key combination", "Bấm vào ô rồi nhấn tổ hợp phím", "입력란을 클릭한 뒤 키 조합을 누르세요");
    public string ResetShortcut => Pick("Reset", "Đặt lại", "재설정");
    public string ShortcutNeedsModifier => Pick("Include Ctrl, Alt, Shift, or Win", "Cần có Ctrl, Alt, Shift hoặc Win", "Ctrl, Alt, Shift 또는 Win을 포함하세요");
    public string ShortcutInUse => Pick("That shortcut is already in use", "Tổ hợp phím này đang được ứng dụng khác sử dụng", "다른 앱에서 이미 사용 중인 단축키입니다");
    public string ShortcutUnavailable => Pick("The shortcut could not be registered", "Không thể đăng ký phím tắt này", "단축키를 등록할 수 없습니다");
    public string Quit => Pick("Quit", "Thoát", "종료");
    public string SourcePlaceholder => Pick("Select text in another app, or type here…", "Chọn văn bản ở app khác, hoặc gõ vào đây…", "다른 앱에서 텍스트를 선택하거나 여기에 입력하세요…");
    public string ClearAll => Pick("Clear everything", "Xoá tất cả", "모두 지우기");
    public string Translating => Pick("Translating", "Đang dịch", "번역 중");
    public string SpeakSource => Pick("Read the source aloud", "Đọc văn bản nguồn", "원문 읽어주기");
    public string SpeakResult => Pick("Read the translation aloud", "Đọc bản dịch", "번역문 읽어주기");
    public string StopSpeaking => Pick("Stop reading", "Dừng đọc", "읽기 중지");
    public string VoiceUnavailable => Pick("No matching Windows voice is installed", "Chưa cài giọng đọc Windows phù hợp", "일치하는 Windows 음성이 설치되어 있지 않습니다");
    public string AddCustomTone => Pick("Custom tone", "Giọng riêng", "커스텀 말투");
    public string AddCustomToneHelp => Pick("Write your own tone and keep it for next time", "Tự viết giọng văn và lưu lại cho lần sau", "직접 쓴 말투를 저장해 다음에도 사용하세요");
    public string CustomTonePlaceholder => Pick("Describe the tone, e.g. like a colleague on chat…", "Mô tả giọng văn, ví dụ: như đồng nghiệp nhắn tin…", "말투를 설명하세요, 예: 동료와 채팅하듯…");
    public string SaveCustomTone => Pick("Save tone", "Lưu giọng", "말투 저장");
    public string CancelCustomTone => Pick("Cancel", "Huỷ", "취소");
    public string EditCustomTone => Pick("Edit", "Sửa", "수정");
    public string DeleteCustomTone => Pick("Delete", "Xoá", "삭제");
    public string FreeformPlaceholder => Pick("What should change? Just say it…", "Cần sửa gì? Cứ nói…", "무엇을 고칠까요? 편하게 말씀하세요…");
    public string Undo => Pick("Undo", "Hoàn tác", "실행 취소");
    public string Redo => Pick("Redo", "Làm lại", "다시 실행");
    public string CopyAndClose => Pick("Copy & close", "Copy và đóng", "복사 후 닫기");
    public string Copied => Pick("Copied", "Đã chép", "복사됨");
    public string ErrorSameLanguages => Pick("The source and target languages are the same.", "Ngôn ngữ nguồn và đích đang trùng nhau.", "원본 언어와 번역 언어가 동일합니다.");
    public string ErrorInvalidResponse => Pick("The server sent an invalid response.", "Phản hồi không hợp lệ từ máy chủ.", "서버 응답이 올바르지 않습니다.");
    public string ErrorServerStatus(int code) => Pick($"The server returned an error ({code}).", $"Máy chủ trả về lỗi ({code}).", $"서버에서 오류를 반환했습니다 ({code}).");
    public string ToneCasual => Pick("Casual", "Thân mật", "친근하게");
    public string ToneNeutral => Pick("Neutral", "Trung tính", "중립적으로");
    public string ToneFormal => Pick("Formal", "Trang trọng", "격식 있게");
    public string ActionShorter => Pick("Shorter", "Ngắn hơn", "더 짧게");
    public string ActionNatural => Pick("More natural", "Tự nhiên hơn", "더 자연스럽게");
    public string ActionKeepTerms => Pick("Keep terms", "Giữ thuật ngữ", "용어 유지");
    public string ActionExplain => Pick("Explain", "Giải thích", "설명 추가");
}
