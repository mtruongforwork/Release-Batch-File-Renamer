# GIAI ĐOẠN 3 — v1.1 (BẬT PRO)

Nguồn: `docs/specs/13-lo-trinh-mvp-ru-ro.md` — Giai đoạn 3 (3–4 tuần).  
Phạm vi: EXIF/media, đệ quy không giới hạn, Undo đa bước, preset không giới hạn, xuất báo cáo, CLI, tích hợp IAP.  
Cổng nghiệm thu: **QA-09, QA-14, QA-15** và **IAP chạy đúng**.

> **Trạng thái:** toàn bộ tính năng Pro đã được hiện thực và kiểm thử tự động (127 test pass).  
> Phần còn lại là thao tác bên ngoài: tạo add-on trong Partner Center, ký gói và mua thử bằng tài khoản Microsoft thật.

---

## 1. Kiến trúc phân tách Free / Pro

| Tầng | Nội dung |
| :--- | :--- |
| `BFR.Core/Licensing/` | `LicenseTier`, `LicenseLimits`, `LicenseService`, `LicenseStore`, `LicenseCoordinator`, `IStoreLicenseProvider`. Không phụ thuộc Windows. |
| `BFR.Store/` | `MicrosoftStoreLicenseProvider` — dùng `Windows.Services.Store` (WinRT) để đọc giấy phép add-on và thực hiện giao dịch mua. Chỉ hoạt động khi ứng dụng có package identity (gói MSIX). |
| `BFR.App` | Huy hiệu cấp, nút nâng cấp, khóa tính năng Pro trong trình soạn quy tắc, thư viện preset, xuất báo cáo. |
| `BFR.Cli` | `bfr license status|trial|buy`, `bfr report export`, khóa lệnh Pro theo cấp giấy phép. |

### Bảng giới hạn (đúng PHẦN 3.6 của đặc tả)

| Tính năng | Free | Pro |
| :--- | :--- | :--- |
| Số quy tắc trong chuỗi | 3 | Không giới hạn |
| Quét thư mục con | 2 cấp | Không giới hạn |
| Preset người dùng lưu được | 3 | Không giới hạn |
| Nhập/xuất tệp preset | Không | Có |
| Hoàn tác | 1 lần gần nhất | Lịch sử 30 ngày, không giới hạn |
| Ngày chụp EXIF / ngày tạo media | Không | Có |
| Xuất báo cáo CSV/JSON | Không | Có |
| CLI (`bfr rename/undo/report`) | Không | Có |
| Đánh số kiểu La Mã | Không | Có |

**Ngoại lệ (giữ nguyên cho mọi cấp):** journal, khôi phục sau sự cố, phát hiện xung đột, preview, Rule Trace, kéo thả, menu chuột phải, tích hợp Explorer — không khoá sau thanh toán.

### Nguồn cấp giấy phép
1. **Store (đóng gói MSIX):** đọc `AppLicense.AddOnLicenses` cho product ID `BFR_PRO_LIFETIME`; mua qua `RequestPurchaseAsync`.
2. **Dùng thử 14 ngày:** ghi cục bộ ở `%LocalAppData%\BatchFileRenamer\license.json` (chỉ dùng một lần cho mỗi cài đặt).
3. **Bản portable (ZIP):** **luôn ở cấp Free** — Store IAP không hoạt động ngoài Store (đúng PHẦN 13.3 của đặc tả).

### Móc thử nghiệm cho bản dev
`BFR_DEV_TIER=pro|free|trial` chỉ có tác dụng khi ứng dụng **không** chạy từ gói MSIX. Ứng dụng trong Store luôn lấy cấp từ giấy phép Store nên người dùng không thể tự mở khoá Pro bằng biến môi trường.

---

## 2. Tính năng Pro đã triển khai

### 2.1 EXIF và ngày tạo media (QA-09)
- `ExifMetadataReader` (đã có từ Giai đoạn 1) giờ **bỏ qua tệp "chỉ trực tuyến"** để không vô tình tải tệp OneDrive về máy.
- `MediaMetadataReader` (mới): quét thẻ "creation date"/"media create date"/"creation time" bằng MetadataExtractor cho MP4/MOV/AVI.
- `DateRule.DateSource.Media` mới + token `{media}`; chuỗi dự phòng mặc định: `Taken → Media → Created → Modified`.
- Nguồn ngày được chọn sẽ hiển thị trong Rule Trace (ví dụ “dự phòng Created”).
- Bản Free **không đọc** metadata: `PreviewEngine` chỉ nhận `IMetadataProvider` khi cấp Pro.

### 2.2 Bảo vệ tệp OneDrive / ổ mạng (QA-14)
- `FileItem.IsOffline` / `IsReadOnly` mới; `FileScanner` đọc thuộc tính `OFFLINE`, `RECALL_ON_OPEN`, `RECALL_ON_DATA_ACCESS`.
- Tệp placeholder **không bị đọc nội dung** (EXIF/media) → không tải tệp về ngoài ý muốn.
- Lỗi từng tệp vẫn được ghi `failed` và xử lý tiếp như trước (không chặn cả lô).

### 2.3 Undo đa bước (FR-C5)
- `HistoryStore.PruneAndLimit(maxAge, maxEntries)`: Pro giữ 30 ngày, Free giữ 1 lần gần nhất.
- Cửa sổ **Lịch sử** cho phép chọn bất kỳ mục nào để hoàn tác (đã có sẵn khung UI, nay dùng được với Pro).
- Sửa lỗi: `HistoryEntry.JournalPath` trỏ sai tệp journal (thiếu tiền tố thời gian) → bổ sung `RenameExecutorResult.JournalPath`.

### 2.4 Xuất báo cáo (FR-C7)
- `BFR.Core/Reports/ReportExporter.cs`: xuất CSV/JSON (tên cũ, tên mới, trạng thái, lỗi, thời điểm).
- Nút **Xuất báo cáo** trên thanh công cụ (Pro) — xuất lần đổi tên gần nhất.
- CLI: `bfr report export [--last|--id <id>] [--format csv|json] [--out <tệp>]`.

### 2.5 Thư viện preset
- Cửa sổ **Thư viện preset** (mới): lưu preset hiện tại vào thư viện cục bộ, áp dụng, xoá.
- Free tối đa 3 preset (có thông báo khi vượt); Pro không giới hạn.
- `PresetStore.Delete()` mới.

### 2.6 CLI
- `bfr license status|trial|buy` — xem cấp, bắt đầu dùng thử, mua Pro (chỉ trong bản Store).
- `bfr rename/undo/report` và `preset export/import` yêu cầu Pro; `context-menu`, `history`, `preset list`, `license` dùng được ở mọi cấp.
- `--depth <n>` để giới hạn độ sâu quét (Free tối đa 2).

---

## 3. Kiểm thử

| Nhóm | Số test | Kết quả |
| :--- | ---: | :--- |
| `BFR.Core.Tests` (cũ + Giai đoạn 3) | 120 | 120/120 pass |
| `BFR.Core.PropertyTests` | 7 | 7/7 pass |
| **Tổng** | **127** | **127/127 pass** |

Test mới của Giai đoạn 3 (`tests/BFR.Core.Tests/Phase3Tests.cs`, `Phase3CliTests.cs`):
- Giấy phép: mặc định Free, sau khi mua thì Pro, dùng thử 30 ngày và không dùng lại được, giới hạn rule/depth/preset, móc dev chỉ hoạt động ngoài Store.
- IAP: Store báo đã mua → cấp Pro; mua thành công → ghi quyền; huỷ mua → giữ nguyên Free; Store không khả dụng → `NotSupported`.
- QA-09: EXIF có/không → dùng đúng chuỗi dự phòng và ghi rõ nguồn; media có/không.
- QA-14: tệp offline không bị đọc; phát hiện đúng thuộc tính placeholder.
- QA-15: CLI `--dry-run --format json` khớp **từng dòng** với preview tính bằng `BFR.Core`; Free bị chặn với thông báo rõ ràng; mã thoát đúng đặc tả; `context-menu` vẫn dùng được ở Free.

---

## 4. Lệnh kiểm tra

```powershell
dotnet build BatchFileRenamer.sln -c Release
dotnet test BatchFileRenamer.sln -c Release

# Chạy CLI ở cấp Pro (chỉ khi KHÔNG đóng gói MSIX)
$env:BFR_DEV_TIER = "pro"
dotnet run --project src/BFR.Cli -c Release -- license status

# Đóng gói MSIX v1.1 + portable ZIP
powershell -ExecutionPolicy Bypass -File packaging/msix/build-msix.ps1 -Configuration Release -SkipSign
```

---

## 5. Việc còn lại (phụ thuộc bên ngoài)

| Hạng mục | Trạng thái | Ghi chú |
| :--- | :--- | :--- |
| Tạo add-on `BFR_PRO_LIFETIME` trong Partner Center | **CHỜ** | Add-on bền vững (durable), giá theo sức mua khu vực (đề xuất 3,99–4,99 USD). |
| Ký gói MSIX với chứng chỉ tin cậy | **CHỜ** | Giấy phép Publisher phải khớp `Identity Publisher`. |
| Mua thử bằng tài khoản Microsoft thật | **CHỜ** | Kiểm tra luồng `RequestPurchaseAsync` trên thiết bị có đăng nhập. |
| Kiểm thử tệp OneDrive thật | **CHỜ** | Cần tài khoản OneDrive có tệp ở trạng thái "chỉ trực tuyến". |
| Cập nhật mô tả Store cho bản Pro | **CHỜ** | Xem `docs/STORE_LISTING.md` mục cần bổ sung. |
| Ảnh Store | **XONG** | `docs/store-assets/screenshot-main.png` (1600×900) và `screenshot-preset-library.png`. |

### Giới hạn đã biết
- Tệp media tạo bằng codec/container không chuẩn có thể không có thẻ ngày tạo → ứng dụng dùng chuỗi dự phòng (đã ghi rõ trong Rule Trace).
- Bản portable không bao giờ lên cấp Pro (không có kênh thanh toán) — đúng đặc tả PHẦN 13.3.
- Gói MSIX v1.1.0 có dung lượng ~78 MB do thư viện chiếu WinRT (`Microsoft.Windows.SDK.NET.dll`) cho Store IAP.

---

## 6. Giai đoạn tiếp theo

Giai đoạn 4 (v1.2) đã được triển khai: Watch Folder (Pro), preset chia sẻ qua clipboard, 4 ngôn ngữ (vi/en/fr/zh-Hans), tối ưu hiệu năng metadata kèm cổng đo trong CI, và kiểm tra cập nhật cho bản portable.  
Chi tiết: `PHASE4_README.md` và `docs/PHASE4_QA.md`. Gói hiện tại đã lên phiên bản **1.2.0** (`artifacts/BatchFileRenamer-1.2.0.msix`).
