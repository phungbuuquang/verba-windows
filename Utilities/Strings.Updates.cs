namespace verba_windows.Utilities;

public sealed partial class Strings
{
    public string OpenVerba => Pick("Open verba", "Mở verba", "verba 열기");
    public string OpenVerbaWithShortcut(string shortcut) => Pick(
        $"Open verba ({shortcut})", $"Mở verba ({shortcut})", $"verba 열기 ({shortcut})");
    public string CheckForUpdates => Pick("Check for updates", "Kiểm tra cập nhật", "업데이트 확인");
    public string CheckingForUpdates => Pick("Checking for updates…", "Đang kiểm tra cập nhật…", "업데이트 확인 중…");
    public string DownloadingUpdate(string version) => Pick(
        $"Downloading {version}…", $"Đang tải {version}…", $"{version} 다운로드 중…");
    public string RestartToUpdate => Pick(
        "Restart to update", "Khởi động lại để cập nhật", "다시 시작하여 업데이트");
    public string RestartToUpdateVersion(string version) => Pick(
        $"Restart and update to {version}",
        $"Khởi động lại và cập nhật lên {version}",
        $"다시 시작하여 {version}(으)로 업데이트");
    public string UpdateReadyTitle => Pick(
        "Update ready", "Bản cập nhật đã sẵn sàng", "업데이트 준비 완료");
    public string UpdateReadyMessage(string version) => Pick(
        $"Verba {version} is ready. Use the tray menu to restart and update.",
        $"Verba {version} đã tải xong. Mở menu khay hệ thống để khởi động lại và cập nhật.",
        $"Verba {version} 업데이트가 준비되었습니다. 트레이 메뉴에서 다시 시작하여 업데이트하세요.");
    public string UpToDate => Pick(
        "You are using the latest version.",
        "Bạn đang dùng phiên bản mới nhất.",
        "최신 버전을 사용 중입니다.");
    public string UpdateCheckFailed => Pick(
        "Could not check for updates. Please try again later.",
        "Không thể kiểm tra cập nhật. Vui lòng thử lại sau.",
        "업데이트를 확인할 수 없습니다. 나중에 다시 시도하세요.");
}
