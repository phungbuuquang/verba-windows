# Publish Verba for Windows

Tài liệu này mô tả quy trình publish và phát hành Verba bằng Velopack qua GitHub Releases.

## Thông tin cố định

- Project: `verba-windows.csproj`
- Runtime: `win-x64`
- Velopack App ID: `Verba.Windows`
- Release channel: `win-x64-stable`
- GitHub repository: `https://github.com/phungbuuquang/verba-windows`
- Main executable: `verba-windows.exe`

Chạy tất cả lệnh từ thư mục gốc của repository.

## 6. Publish ứng dụng

Đặt phiên bản cần phát hành. Giá trị này phải giống với `Version` trong `verba-windows.csproj` và `--packVersion` ở bước đóng gói.

```powershell
$releaseVersion = "1.0.0"
```

Khôi phục dependency và Velopack CLI:

```powershell
dotnet restore .\verba-windows.csproj
dotnet tool restore
```

Build và chạy regression tests tuần tự:

```powershell
dotnet build .\verba-windows.csproj -c Release
dotnet run --project .\Tests\verba-windows.Tests.csproj -c Release
```

Publish bản self-contained cho Windows x64:

```powershell
dotnet publish .\verba-windows.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:Version=$releaseVersion `
  -p:PublishSingleFile=false `
  -o .\artifacts\publish\win-x64
```

Không bật single-file để Velopack tạo delta update hiệu quả hơn.

## 7. Đóng gói release đầu tiên

Với release đầu tiên, chạy:

```powershell
dotnet tool run vpk -- pack `
  --packId Verba.Windows `
  --packVersion $releaseVersion `
  --packDir .\artifacts\publish\win-x64 `
  --mainExe verba-windows.exe `
  --icon .\Assets\AppIcon.ico `
  --channel=win-x64-stable `
  --outputDir .\artifacts\releases
```

Kiểm tra thư mục sau khi đóng gói:

```text
artifacts/releases/
```

Thư mục phải chứa installer, full package và metadata của channel. Upload toàn bộ artifact do Velopack tạo; không chỉ upload riêng file Setup.

## 8. Upload lên GitHub Releases

Tạo GitHub token có quyền ghi release. Chỉ lưu token trong biến môi trường của terminal hoặc secret của CI; không ghi token vào source code hay commit vào Git.

```powershell
$env:VERBA_GITHUB_TOKEN = "YOUR_GITHUB_TOKEN"
```

Upload và publish release:

```powershell
dotnet tool run vpk -- upload github `
  --repoUrl https://github.com/phungbuuquang/verba-windows `
  --token $env:VERBA_GITHUB_TOKEN `
  --channel=win-x64-stable `
  --publish `
  --tag "v$releaseVersion" `
  --releaseName "Verba $releaseVersion" `
  --outputDir .\artifacts\releases
```

Sau khi upload:

1. Mở GitHub Releases và xác nhận release không còn ở trạng thái draft.
2. Xác nhận mọi package và file `releases.win-x64-stable.json` đều đã được upload.
3. Cài ứng dụng bằng Setup do Velopack tạo.
4. Kiểm tra tray, shortcut, translation, settings và text-to-speech.

Repository và release phải public để ứng dụng hiện tại kiểm tra update mà không cần nhúng GitHub token. Không nhúng token của private repository vào ứng dụng desktop.

## 9. Phát hành phiên bản cập nhật

Ví dụ phát hành `1.0.1`:

```powershell
$releaseVersion = "1.0.1"
```

Cập nhật `Version` trong `verba-windows.csproj` thành cùng giá trị:

```xml
<Version>1.0.1</Version>
```

Tải release hiện tại về trước khi pack để Velopack có thể tạo delta package:

```powershell
dotnet tool run vpk -- download github `
  --repoUrl https://github.com/phungbuuquang/verba-windows `
  --channel=win-x64-stable `
  --outputDir .\artifacts\releases
```

Nếu repository là private, thêm `--token $env:VERBA_GITHUB_TOKEN` vào lệnh download.

Build, test và publish phiên bản mới:

```powershell
c
dotnet run --project .\Tests\verba-windows.Tests.csproj -c Release

dotnet publish .\verba-windows.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:Version=$releaseVersion `
  -p:PublishSingleFile=false `
  -o .\artifacts\publish\win-x64
```

Đóng gói phiên bản mới:

```powershell
dotnet tool run vpk -- pack `
  --packId Verba.Windows `
  --packVersion $releaseVersion `
  --packDir .\artifacts\publish\win-x64 `
  --mainExe verba-windows.exe `
  --icon .\Assets\AppIcon.ico `
  --channel=win-x64-stable `
  --outputDir .\artifacts\releases
```

Upload phiên bản mới:

```powershell
dotnet tool run vpk -- upload github `
  --repoUrl https://github.com/phungbuuquang/verba-windows `
  --token $env:VERBA_GITHUB_TOKEN `
  --channel=win-x64-stable `
  --publish `
  --tag "v$releaseVersion" `
  --releaseName "Verba $releaseVersion" `
  --outputDir .\artifacts\releases
```

Kiểm thử update bằng một máy đang cài phiên bản cũ:

1. Mở phiên bản cũ và chờ khoảng 10 giây.
2. Xác nhận Verba tải update trong nền.
3. Mở menu tray và chọn restart để cập nhật.
4. Xác nhận ứng dụng mở lại đúng một instance.
5. Xác nhận settings trong `%AppData%\verba` còn nguyên.
6. Xác nhận phiên bản mới hoạt động bình thường.

## Checklist trước khi publish

- [ ] `Version` trong project trùng với `$releaseVersion`.
- [ ] Build Release thành công.
- [ ] Console regression harness pass.
- [ ] Đúng channel `win-x64-stable`.
- [ ] Đã download release trước khi pack bản cập nhật.
- [ ] Không có token hoặc certificate được commit vào Git.
- [ ] Release đã publish, không còn là draft.
- [ ] Toàn bộ artifact Velopack đã được upload.
- [ ] Update từ phiên bản cũ đã được kiểm thử.
