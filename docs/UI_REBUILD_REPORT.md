# BÁO CÁO REBUILD UI/UX (v2.0)

Kế hoạch: `docs/Plan/BFR_UIUX_REBUILD_PLAN.md` · Quyết định công nghệ: `docs/ui-rebuild-decision.md`

> **Kết quả:** đã dựng lại cửa sổ chính theo IA mới (CommandBar 1 hàng · Rule Builder · bảng xem trước có diff · thanh hành động dưới), thêm theme sáng/tối có token, MVVM cho lớp UI, trang Cài đặt, hộp thoại xác nhận áp dụng và smoke test tự động.  
> **`BFR.Core` không bị sửa** → kết quả 149 test của lõi (INV-1..INV-6) giữ nguyên; thêm 14 test cho ViewModel.

---

## 1. Đối chiếu 10 vấn đề trong audit (mục 2 của plan)

| # | Vấn đề | Đã xử lý |
| :--- | :--- | :--- |
| 1 | Thanh công cụ 2 hàng, ~17 nút đồng cấp | **CommandBar 1 hàng**: nút chính "Thêm tệp", nút "Thêm thư mục", 2 chip tuỳ chọn quét, ô "Sắp xếp theo", "Xóa danh sách", menu "⋯ Thêm" gom tính năng nâng cao |
| 2 | Badge Free/Pro, ngôn ngữ lẫn trong thanh thao tác | Badge Pro/Free + nút **Cài đặt** ở **thanh tiêu đề**; ngôn ngữ & theme & cập nhật & giấy phép chuyển sang trang Cài đặt |
| 3 | Panel quy tắc gần như trống, sửa phải mở hộp thoại | **Rule Builder**: thẻ quy tắc có công tắc bật/tắt, tóm tắt 1 dòng, nút mở tuỳ chọn **tại chỗ** (accordion) — không còn hộp thoại "Sửa quy tắc" |
| 4 | Bảng không highlight khác biệt, cột "Chi tiết" trống | **Diff màu** trong Tên cũ (đỏ + gạch ngang) và Tên mới (xanh + đậm); bỏ cột "Chi tiết", chuyển vào tooltip + Rule Trace; thêm cột Thư mục |
| 5 | Rule Trace font monospace, ẩn, lệch ngôn ngữ | **Ngăn "Chi tiết quy tắc"** dạng timeline (gốc → từng quy tắc → kết quả), bám theo dòng đang chọn, dùng chung bộ chuỗi 4 ngôn ngữ |
| 6 | Thanh trạng thái là dòng chữ liền | **Chip lọc** bấm được (Tất cả / Sẽ đổi / Không đổi / Vấn đề) + ô tìm kiếm nhanh; nút "Áp dụng N tệp" nêu rõ số tệp |
| 7 | Control kiểu cũ, không icon, không dark mode | **Theme.xaml** (token + style) cho Button/CheckBox/RadioButton chip/TextBox/ComboBox/ListBox/DataGrid; theme sáng/tối theo hệ thống, icon Segoe MDL2 Assets |
| 8 | Không empty state | **Empty state**: biểu tượng, "Kéo thả tệp hoặc thư mục vào đây", 2 nút + 3 preset gợi ý bấm một lần |
| 9 | Thứ tự đánh số không rõ | Ô **"Sắp xếp theo"** (tên tự nhiên / A→Z / ngày tạo / ngày sửa / kích thước / thứ tự nạp) — sắp xếp thật sự trước khi tính preview |
| 10 | Áp dụng không có xác nhận/tóm tắt | **Hộp thoại xác nhận**: số tệp, số thư mục bị tác động, số tệp đổi phần mở rộng, gợi ý Hoàn tác, 3 ví dụ; sau khi áp dụng có **InfoBar thành công** kèm nút Hoàn tác / Lịch sử / Xuất báo cáo |

---

## 2. Những gì đã làm theo từng chặng

| Chặng | Kết quả |
| :--- | :--- |
| **U0** | Audit + `docs/ui-rebuild-decision.md`: giữ **WPF + theme tự thân** (không thêm WPF-UI/ModernWpf) để không rủi ro đóng gói MSIX và không phá style DataGrid/High Contrast |
| **U1** | Theme token (`Themes/Theme.xaml` + `Theme.Light/Dark.xaml`), `ThemeManager` (đọc accent + nền tối của Windows, Mica best-effort), thanh tiêu đề, CommandBar 1 hàng, bố cục 2 cột có `GridSplitter`, thanh hành động dưới, menu "⋯ Thêm", trang Cài đặt |
| **U2** | Rule Builder dạng thẻ + accordion sửa tham số tại chỗ (Find/Replace, Numbering, Trim, Insert, Case, Sanitize, Date, Extension, Template), bật/tắt quy tắc, xoá, đổi thứ tự, "+ Thêm quy tắc", menu preset (Lưu/Nhập/Xuất/Quản lý) |
| **U3** | Bảng xem trước: cột chọn, diff màu, chip trạng thái có icon, lọc + tìm nhanh, cột thư mục, ngăn Rule Trace timeline, kéo-thả thêm tệp |
| **U4** | Thanh hành động: chip lọc, nút Áp dụng nêu số tệp + vô hiệu hoá thông minh, hộp xác nhận, progress + Hủy, InfoBar thành công, Hoàn tác |
| **U5** | Lịch sử, Thư viện preset, Watch Folder, Menu chuột phải, Xuất báo cáo, Kiểm tra cập nhật → gom vào menu "⋯ Thêm"; trang Cài đặt cho ngôn ngữ/theme/cập nhật/giấy phép |
| **U6** | Empty state + 3 preset gợi ý, phím tắt (`Ctrl+O`, `Ctrl+Shift+O`, `Ctrl+Z`, `Ctrl+S`, `Ctrl+F`, `Ctrl+Enter`, `F5`) |
| **U7** | 14 test ViewModel + smoke test UI tự động + ảnh Store sáng/tối mới |

---

## 3. Bằng chứng

| Hạng mục | Kết quả |
| :--- | :--- |
| Build solution | **0 warning / 0 error** |
| Test lõi (`BFR.Core.Tests` + `BFR.Core.PropertyTests`) | **149/149 pass** — không đổi so với trước rebuild (INV-1..INV-6 giữ nguyên) |
| Test ViewModel (`BFR.Presentation.Tests`) | **52/52 pass** |
| Smoke test UI (`packaging/smoke-ui.ps1`) | **PASS**: mở cửa sổ → bảng xem trước → mở tuỳ chọn quy tắc tại chỗ (ô nhập 1 → 5) → bộ lọc → **hộp thoại xác nhận** → đổi tên 3 tệp (001/002/003.jpg) → **Hoàn tác** về tên cũ |
| Ảnh Store | `docs/store-assets/screenshot-main.png` (sáng), `screenshot-dark.png` (tối), `screenshot-watch-folder.png`, `screenshot-preset-library.png` |
| Gói MSIX | `artifacts/BatchFileRenamer-1.2.0.msix` (78.257.148 byte, SHA-256 `9187A3A0BC5946E2E05A5169A250D50E6675E21413D5728914E838CDC7B25B64`) |

### Lỗi thật phát hiện trong lúc rebuild (đã sửa)

1. **Theme không nạp được** → `ThemeManager.Apply` đã "thay" nhầm dictionary `Theme.xaml` bằng dictionary màu; sửa bằng cách chỉ thay `Theme.Light/Dark`.
2. **`ObservableCollection` bị sửa ngoài luồng UI** → preview trắng; sửa bằng cách mọi lần tính lại đều `Dispatcher.BeginInvoke`.
3. **Preset do Explorer/IPC truyền vào bị combobox ghi đè** → mất preset "Đánh số theo thứ tự"; sửa bằng cách ưu tiên `_vm.SelectedPreset` khi khởi tạo.
4. **Tham số số bị ghi thành chuỗi** khi sửa trong thẻ quy tắc → quy tắc mất giá trị; sửa bằng cách ghi JSON number.
5. **Nút Áp dụng bị khoá sau khi tính xong** (CanExecute không được thông báo lại khi `IsBusy` trở lại false) → sửa bằng cách raise lại CanExecute khi đổi trạng thái busy.
6. **Tệp mới mặc định bị bỏ chọn** khi tính lại preview → sửa bằng cách giữ trạng thái chọn theo đường dẫn, tệp mới mặc định chọn.

---

## 4. UI nâng cấp Pro (làm lại hoàn toàn)

> Yêu cầu: bỏ MessageBox 3 nút, làm dialog tùy chỉnh đồng bộ theme, có 4 trạng thái, mở theo ngữ cảnh, tách logic license khỏi UI, mọi chuỗi trong resource, có unit test.

### 4.1 Kiến trúc

| Tầng | Nội dung |
| :--- | :--- |
| `src/BFR.Core/Licensing/` | `LicenseService` (cấp, giới hạn, dùng thử, **mã kích hoạt**), `LicenseStore`, `LicenseCoordinator` (đồng bộ Store IAP), `LicenseKey` (kiểm tra mã) |
| `src/BFR.Store/` | `MicrosoftStoreLicenseProvider` (đọc giấy phép add-on), `StorePriceProvider` (lấy **giá** từ Store) |
| `src/BFR.Presentation/` (mới) | `UpgradeViewModel` + 4 trạng thái, `RuleCardViewModel`, `MainViewModel`, `ObservableObject/RelayCommand`, `IStringProvider`, `IUiDispatcher` — **không phụ thuộc WPF** nên unit test được ở mọi môi trường |
| `src/BFR.App/` | `UpgradeWindow.xaml` (dialog bo góc, nút X, Esc), `LicenseInfoWindow`, `LocStringProvider` (đọc resource 4 ngôn ngữ), `WpfUiDispatcher` |

### 4.2 Dialog mới (`UpgradeWindow.xaml`)

- Khung bo góc 12 px, nền/viền theo theme (sáng/tối), **nút X**, **Esc đóng**, Tab điều hướng, Enter kích hoạt hành động chính.
- Header: *"Nâng cấp lên Batch File Renamer Pro"* + *"Mua một lần, dùng trọn đời"*.
- **Bảng so sánh Free vs Pro** (7 dòng) — nội dung **sinh từ `LicenseLimits` thật** nên không lệch với logic:
  Chuỗi quy tắc · Ngày chụp EXIF & ngày tạo media · Quét đệ quy · Hoàn tác nhiều bước · Lưu preset · Xuất báo cáo · Công cụ dòng lệnh.
- Nút chính **"Mua Pro"** (kèm giá lấy từ Store, hoặc từ cấu hình `ProPriceText` — **không hard-code giá**), nút phụ **"Dùng thử Pro 30 ngày"**, nút **"Để sau"**.
- Liên kết **"Tôi đã có mã kích hoạt"** → mở ô nhập mã + nút *Kích hoạt*, có thông báo lỗi rõ ràng (rỗng / sai định dạng / sai checksum).

### 4.3 Bốn trạng thái (có unit test)

| Trạng thái | Hành vi | Ảnh |
| :--- | :--- | :--- |
| **Chưa dùng thử** | Hiện cả *Mua Pro* và *Dùng thử Pro 30 ngày* | `docs/ui-upgrade/upgrade-1-not-trialed.png` |
| **Đang dùng thử** | Ẩn nút dùng thử, hiện dải xanh *"Còn 13 ngày dùng thử Pro."* | `docs/ui-upgrade/upgrade-2-trialing.png` |
| **Hết hạn dùng thử** | Chỉ còn *Mua Pro* + *Nhập mã kích hoạt* | `docs/ui-upgrade/upgrade-3-trial-expired.png` |
| **Đã là Pro** | Nút *Nâng cấp Pro* trên thanh trên biến thành **badge "Pro"**, bấm vào mở **màn hình thông tin license** | `docs/ui-upgrade/upgrade-4-license-info.png` |

### 4.4 Mở theo ngữ cảnh

Chạm vào tính năng Pro (thêm quy tắc thứ 4, chọn nguồn ngày EXIF, xuất báo cáo, nhập/xuất preset, CLI…) → hộp thoại mở với dải báo context ở đầu:
*"Tính năng **'Chuỗi quy tắc không giới hạn'** chỉ có trong gói Pro."* → `docs/ui-upgrade/upgrade-5-context.png`

### 4.5 Mã kích hoạt

- Định dạng `BFR-XXXXX-XXXXX-XXXXX-XXXXX-XXXXX` (bảng chữ 32 ký tự loại bỏ I/O/0/1, checksum FNV-1a base32 → sai 1 ký tự là bị phát hiện).
- `LicenseService.TryActivateKey()` trả về mã lỗi cụ thể: `Empty` / `BadFormat` / `BadChecksum` → UI hiện thông báo tương ứng.
- Bản Store dùng Store IAP; mã kích hoạt là kênh dự phòng cho bản portable.

### 4.6 Kiểm thử

- `tests/BFR.Presentation.Tests/UpgradeViewModelTests.cs` — 10 test cho 4 trạng thái + bảng tính năng + mã kích hoạt (sai/đúng) + sự kiện nâng cấp.
- `tests/BFR.Core.Tests/LicenseKeyTests.cs` — 6 test cho sinh/kiểm tra mã.
- Tổng: **180 test pass** (150 Core + 7 property + 23 Presentation).

---

## 5. Giai đoạn tiếp theo (A1–A7) — đã triển khai

| # | Hạng mục | Kết quả | Ảnh |
| :--- | :--- | :--- | :--- |
| **A1** | **Trình thử Regex** (spec 7.3) | Hộp thoại riêng: mẫu, thay bằng, chuỗi thử, bỏ qua hoa/thường, kết quả + **các nhóm bắt**, thư viện 5 mẫu, "Dùng tên đang chọn", "Dùng cho tất cả tệp" (ghi thẳng vào quy tắc Tìm và thay). Logic đo khớp nằm ở `BFR.Core/RegexProbe.cs` (giới hạn 2 s chống regex lỗi) | `docs/ui-features/feature-2-regex-tester.png` |
| **A2** | **Kéo-thả đổi thứ tự quy tắc** | Kéo tay cầm ⠿ để đổi thứ tự **+** nút ↑/↓ (bàn phím) trên mỗi thẻ quy tắc; preset và chuỗi tính toán luôn khớp thứ tự | `docs/ui-features/feature-1-rule-builder.png` |
| **A3** | **Restyle cửa sổ phụ** | Lịch sử, Thư viện preset (thêm ô tìm kiếm), Watch Folder: theo theme mới, có trạng thái rỗng, bo góc, nút theo style chuẩn. Trang **Cài đặt** có nút **"Kiểm tra ngay"** gọi thật `UpdateChecker` (dùng chung `UpdateRunner` với menu chính) | `docs/ui-features/feature-4-settings.png` |
| **A4** | **Command Palette `Ctrl+K`** | 17 lệnh, lọc **không dấu** ("lich" → "Lịch sử"), khớp chữ cái đầu ("tt" → "Thêm tệp"), ↑↓ chọn, Enter chạy, Esc đóng | `docs/ui-features/feature-3-command-palette.png` |
| **A5** | **Đưa mọi chuỗi vào resource** | 401 khoá × 4 ngôn ngữ (vi/en/fr/zh-Hans), đồng bộ & không trùng. Nhãn tham số quy tắc, **nhãn lựa chọn** (trước đây hiện tiếng Anh thô như `ReplaceAll`), tên quy tắc, tóm tắt, trạng thái xem trước, tiêu đề cột — tất cả qua `IStringProvider` | `docs/ui-features/feature-1-rule-builder.png` |
| **A6** | **Bộ test UI tự động** | `packaging/smoke-ui.ps1` nay kiểm **14 mục**: xem trước, mở rộng thẻ quy tắc, lọc, áp dụng + xác nhận, hoàn tác, **Ctrl+K + lọc lệnh**, **Trình thử Regex (kết quả đúng)**, **nút đổi thứ tự quy tắc**, **cửa sổ cài đặt + nút kiểm tra cập nhật** | — |
| **A7** | **Benchmark + cổng hiệu năng trong CI** | Sửa `.github/workflows/ci.yml` (đường dẫn test cũ `BFR.App.Tests` đã hỏng), thêm bước chạy BenchmarkDotNet (`--job short`), upload báo cáo hiệu năng + log smoke + ảnh chụp | — |

### 5.1 Kiểm thử sau giai đoạn này

- `tests/BFR.Presentation.Tests/RegexTesterTests.cs` — 13 test (đo khớp, nhóm bắt, lỗi cú pháp, bỏ qua hoa/thường, áp dụng vào quy tắc).
- `tests/BFR.Presentation.Tests/CommandPaletteTests.cs` — 8 test (lọc không dấu, lọc chữ cái đầu, chuyển vùng chọn, chạy lệnh).
- `tests/BFR.Presentation.Tests/RuleOrderTests.cs` — 10 test (đổi thứ tự quy tắc, cờ lên/xuống, nhãn tham số qua resource).
- Tổng: **209 test pass** (150 Core + 7 property + 52 Presentation) — build 0 warning / 0 error.

---
## 6. Còn lại (nợ đã biết)

| Hạng mục | Trạng thái | Ghi chú |
| :--- | :--- | :--- |
| ~~Kéo-thả **đổi thứ tự quy tắc**~~ | **Đã làm (A2)** | Kéo tay cầm ⠿ + nút ↑/↓ |
| ~~Command Palette `Ctrl+K` (mục 10 của plan)~~ | **Đã làm (A4)** | 17 lệnh, lọc không dấu |
| Mica hiển thị đầy đủ | Best-effort | DWM bật được nhưng WPF vẫn vẽ nền đặc; cần `WindowChrome` nếu muốn nền trong suốt |
| Nhãn tham số quy tắc trong ViewModel dùng `Loc.L(...)` | Tạm chấp nhận | Chưa đưa vào file resource 4 ngôn ngữ (sẽ làm ở đợt i18n sau) |
| Cửa sổ Lịch sử / Thư viện preset / Watch Folder | Giữ nguyên giao diện cũ | Đã chuyển vị trí truy cập sang menu "⋯ Thêm"; chưa restyle theo theme mới |
| ~~Regex Tester (spec 7.3)~~ | **Đã làm (A1)** | `RegexTesterWindow` + `RegexProbe` trong Core |
| Kiểm thử Narrator/Accessibility Insights | Chờ | Cần máy thật + đợt U7 cuối |
