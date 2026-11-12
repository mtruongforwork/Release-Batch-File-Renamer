# Kế hoạch v1.4 — Giao diện điều hướng mới + tính năng B1–B4

> Lập ngày: 05/11/2026 · Nhánh: `feat/v1.4-ui` (tạo từ `feat/shelf`, vì v1.3 chưa merge)
> Quyết định đã chốt: điều hướng bên trái, mỗi chức năng một trang; giữ cả B1–B4. Radial, OCR, token metadata, lưu Shelf thành dự án dời sang v1.5.

## 1. Phạm vi

### Phần A — Giao diện
| # | Hạng mục | Kết quả |
|---|---|---|
| A1 | `ui:NavigationView` dạng rail bên trái: Đổi tên · Shelf · Lịch sử · Preset · Watch Folder (Pro) · Cài đặt | Cửa sổ chính là khung điều hướng; mỗi chức năng là một trang |
| A2 | Settings, History, Preset Library, Watch Folder chuyển từ cửa sổ thành trang (UserControl) | Không còn popup cho các chức năng này |
| A3 | Xác nhận áp dụng và nâng cấp Pro thành `ui:ContentDialog` ngay trong cửa sổ | Chỉ còn hộp thoại nổi trong cửa sổ, không mở cửa sổ mới |
| A4 | Báo thành công bằng `ui:Snackbar` (kèm nút Hoàn tác); xung đột bằng `ui:InfoBar` | Thay hộp xanh ở footer |
| A5 | Thư viện quy tắc (lưới có icon, mô tả, ô tìm kiếm) thay menu chữ "+ Thêm quy tắc" | Thêm quy tắc bằng cách chọn thẻ |
| A6 | Màn hình trống mới: vùng thả tệp, thư mục gần đây, thẻ preset gợi ý | |
| A7 | Dọn style cũ trong `Theme.xaml`; thống nhất khoảng cách 4/8/12/16 | |

### Phần B — Tính năng
| # | Tính năng | Free / Pro |
|---|---|---|
| B1 | **Đổi tên nhanh từ menu chuột phải**: menu con "Batch Rename with BFR ▸ Mở trong BFR / Đánh số / Bỏ dấu & chuẩn hoá / Thêm ngày"; áp dụng ngay không mở app, có thông báo kèm Hoàn tác | Free: preset có sẵn |
| B2 | **Đổi tên theo danh sách**: quy tắc `ListMap` (dán từ Excel/CSV: tên cũ → tên mới, hoặc theo thứ tự) và `ReplaceDict` (từ điển nhiều cặp tìm/thay); nhập tệp CSV | Free: tối đa 50 dòng. Pro: không giới hạn + nhập tệp CSV |
| B3 | **Tự xử lý trùng tên**: thêm hậu tố " (2)", " (3)"… cho tên trùng trong lô và trùng với tệp ngoài lô | Free |
| B4 | **Phím tắt toàn cục mở Shelf** (mặc định `Ctrl+Alt+Space`, cấu hình được trong danh sách cố định) | Free, opt-in |

## 2. Thiết kế kỹ thuật tóm tắt

- **B2:** `ListParser` (nhận dạng dấu phân cách tab/`;`/`,`, hỗ trợ trích dẫn CSV) → `ListMapRule`, `ReplaceDictRule`; đăng ký trong `RuleFactory` và `RuleCardViewModel`; giới hạn 50 dòng qua `LicenseLimits.MaxListRows` + `ProFeature.UnlimitedListRows`/`ListCsvImport`.
- **B3:** `ConflictResolver` (Core, thuần) nhận danh sách `PreviewItem` + tên ngoài lô, trả tên mới không trùng; `MainViewModel.AutoResolveConflicts` bật/tắt, `InfoBar` có nút "Tự sửa trùng tên".
- **B4:** `HotkeySpec` (Core, thuần: parse/format `Ctrl+Alt+Space`) + `GlobalHotkey` (Win32 `RegisterHotKey` trên cửa sổ nhận thông điệp) + công tắc trong Settings.
- **B1:** `QuickRenamer` (Core): biên dịch preset → xem trước → chặn nếu có xung đột → ghi journal → thực thi; kết quả có `JournalPath` để Hoàn tác. `BFR.Shell` thêm `IEnumExplorerCommand` cho menu con; app nhận `--quick <preset>` và hiện toast Hoàn tác.
- **A1–A7:** `MainWindow` giữ vai trò khung; trang là `UserControl` dùng chung `MainViewModel` hoặc ViewModel riêng; `ContentDialog` qua `ContentDialogHost`. Giữ nguyên `AutomationProperties.Name` mà `packaging/smoke-ui.ps1` đang dùng.

## 3. Kế hoạch kiểm thử

### 3.1 Kiểm thử tự động (bắt buộc xanh trước khi commit)
| Lớp | Bộ test | Nội dung |
|---|---|---|
| Core | `ListParserTests` | tab/`;`/`,`, dấu ngoặc kép, dòng trống, BOM, giới hạn dòng |
| Core | `ListMapRuleTests` | khớp theo tên / tên+đuôi, không phân biệt hoa thường, theo thứ tự, thiếu dòng |
| Core | `ReplaceDictRuleTests` | thứ tự áp dụng, cặp dài trước, không áp dụng chồng, hoa/thường |
| Core | `ConflictResolverTests` | trùng trong lô, trùng ngoài lô, hậu tố tăng dần, giữ đuôi, không đụng tên hợp lệ, khác thư mục không coi là trùng |
| Core | `HotkeySpecTests` | parse/format, phím không hợp lệ, tổ hợp thiếu modifier |
| Core | `QuickRenamerTests` | đổi tên thành công, có xung đột thì không đổi gì, journal để Hoàn tác, preset không tồn tại (dùng `FakeFileSystem`) |
| Core | `PropertyTests` mới | `ConflictResolver` luôn cho ra tên duy nhất; `ListParser` không ném lỗi với mọi chuỗi |
| Core | `LicensingTests` | giới hạn 50 dòng của Free, Pro không giới hạn |
| Presentation | `ListRuleViewModelTests`, `PagesViewModelTests` | tham số quy tắc mới, điều hướng trang, trạng thái InfoBar/Snackbar |
| Shell | `BFR.Shell.SmokeTest` mở rộng | gọi `EnumSubCommands`, kiểm tra số lệnh con, tiêu đề và cờ của từng lệnh |
| Hiệu năng | PerfGate thêm phép đo | `ListMap` 10.000 dòng; `ConflictResolver` 100.000 tệp trong ngưỡng |

### 3.2 Kiểm thử giao diện tự động (UI Automation, theo cách đã dùng ở v1.3)
- Điều hướng từng trang bằng rail, chụp ảnh sáng/tối, kiểm tra không có cửa sổ phụ nào mở ngoài ContentDialog.
- Luồng đầy đủ: thêm thư mục → chọn preset → xem trước → xác nhận → áp dụng → Hoàn tác, kiểm tra trên đĩa.
- B2: dán danh sách → xem trước đúng → vượt 50 dòng ở bản Free thì hiện nâng cấp.
- B3: tạo xung đột → InfoBar → "Tự sửa trùng tên" → xem trước không còn xung đột.
- B4: đăng ký phím tắt, gửi tổ hợp phím, Shelf hiện.
- B1: chạy COM smoke test; chạy `BatchFileRenamer.exe --quick <preset>` với thư mục thử, kiểm tra tệp và Hoàn tác.
- 4 ngôn ngữ × 2 theme: chụp từng trang, kiểm tra không cắt chữ.

### 3.3 Kiểm thử trong gói MSIX (làm lại quy trình của v1.3)
Dựng gói → ký test → cài → WACK → kiểm tra menu con Explorer, `StartupTask`, hook chuột, phím tắt toàn cục → gỡ sạch. Menu con Explorer chỉ kiểm tra được bằng COM smoke test và thử tay (Dev).

### 3.4 Tiêu chí hoàn thành
- Toàn bộ test (Core, Presentation, Property, PerfGate, Shell smoke) xanh; Release build 0 warning.
- UI Automation: mọi luồng ở 3.2 đạt, ảnh chụp được xem lại.
- PerfGate: cuộn/xem trước 10.000 dòng không chậm hơn bản v1.3.
- Không còn cửa sổ phụ cho Settings/History/Preset/Watch Folder.
- Tài liệu: release notes v1.4, privacy policy (phím tắt toàn cục), Store listing cập nhật.

## 4. Thứ tự thực hiện
1. B2 → B3 → B4 → B1 (logic Core + test trước, giao diện sau).
2. A1–A3 (khung điều hướng và chuyển trang), rồi A4–A7.
3. Kiểm thử UI, dựng gói, cập nhật tài liệu.

## 5. Rủi ro
| Rủi ro | Giảm thiểu |
|---|---|
| Menu con Explorer dùng COM thô, dễ lỗi marshalling | Mở rộng smoke test để gọi thật `EnumSubCommands`; thử tay trên Explorer |
| `Ctrl+Alt+Space` có thể trùng phím tắt của app khác | Cho chọn trong danh sách cố định; báo lỗi rõ nếu đăng ký thất bại |
| Đổi cửa sổ thành trang làm hỏng test UI của CI | Giữ nguyên `AutomationProperties.Name`; chạy `packaging/smoke-ui.ps1` ở local trước khi push |
| Bảng xem trước chậm khi gộp vào trang | Đo 10.000 dòng trước và sau |

## 6. Ngoài phạm vi (v1.5)
Radial menu, token metadata (nhạc/ảnh/video), OCR, lưu Shelf thành dự án, Watch Folder nhiều thư mục.
