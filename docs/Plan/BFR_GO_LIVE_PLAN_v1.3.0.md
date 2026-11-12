# Kế hoạch go-live — Batch File Renamer v1.3.0

> Lập ngày: 05/11/2026 · Nhánh: `feat/shelf` (đi trước `main` 9 commit, chưa push)
> Bản đang trên Microsoft Store: **1.2.2.0**

## Mục tiêu và phạm vi

Phát hành **v1.3.0** lên Microsoft Store với:

- **Shelf**: khay nổi gom tệp từ nhiều thư mục để đổi tên một lần (Free tối đa 50 tệp, Pro không giới hạn).
- **Lắc chuột mở Shelf**: lắc khi đang kéo tệp thì Shelf hiện tại con trỏ (opt-in, mặc định tắt).
- **Chạy nền ở khay hệ thống**: đóng cửa sổ chỉ ẩn đi, có menu khay, hỗ trợ `--background` (opt-in, mặc định tắt).
- **Giao diện WPF-UI**: MainWindow, Settings và các cửa sổ phụ dùng FluentWindow, Mica, control Fluent.

**Ngoài phạm vi (dời sang v1.4):** Radial menu (rủi ro kỹ thuật cao nhất, chưa có prototype giao diện), lưu Shelf thành dự án (Pro), add-on tải theo nhu cầu.

## Mốc thời gian

| Giai đoạn | Thời gian | Kết quả |
|---|---|---|
| 0. Ổn định nhánh | 06–09/11 | CI xanh, PR mở, test tay xong |
| 1. Sửa lỗi tồn đọng | 09–13/11 | 10 mục tồn đọng xử lý xong |
| 2. Hoàn thiện tính năng | 16–20/11 | **Đóng băng tính năng 20/11** |
| 3. Kiểm thử phát hành và chuẩn bị Store | 23–27/11 | Gói đã ký, WACK qua, privacy policy đã đăng |
| 4. Nộp Store | **30/11** | Gửi lên Partner Center |
| Go-live | ~03–04/12 | Tuỳ thời gian Microsoft duyệt (không đoán trước được); dành 1–2 ngày đệm |

Cột "Ai" bên dưới: **Dev** = việc cần người làm bằng tay (thiết bị thật, Partner Center); **Claude** = việc Claude Code làm trong repo.

## Tiến độ thực hiện (cập nhật 05/11/2026)

Nhánh `feat/shelf` đã được push lên `origin`. Trạng thái theo giai đoạn:

| Giai đoạn | Trạng thái | Ghi chú |
|---|---|---|
| 0. Ổn định nhánh | Gần xong | 0.1 ✅ (162 + 74 + 7 test, PerfGate đạt mọi ngưỡng) · 0.2 ✅ PR đã mở, **CI xanh** (run `9578106`) · 0.3 **chờ Dev** · 0.4 ✅ |
| 1. Sửa lỗi tồn đọng | ✅ Xong | 10/10 mục (mục 9: đã xoá `RuleEditorWindow` vì không còn nơi nào mở) |
| 2. Hoàn thiện tính năng | ✅ Xong | 2.1–2.5 ✅. 2.1 đã kiểm chứng ở bản portable (khoá `Run`) **và trong gói MSIX thật** (`StartupTask` State 0 ↔ 2). Còn chưa kiểm chứng: Windows tự khởi động app qua `StartupTask` lúc đăng nhập (cần đăng xuất/đăng nhập lại) |
| 3. Kiểm thử phát hành và chuẩn bị Store | Đang làm | Xong: privacy policy, release notes, Store listing, version 1.3.0 (có test canh lệch), dựng được gói MSIX chưa ký (`makeappx` chấp nhận manifest, gồm `StartupTask`), bản publish self-contained chạy được, **hồi quy GUI** (xem trước → hộp thoại xác nhận → áp dụng → hoàn tác trên đĩa) và **hồi quy CLI** (dry-run, đổi tên, hoàn tác) đều đúng. **WACK: PASS** (23/24 bài; bài duy nhất báo FAIL là "Blocked executables", loại `OPTIONAL`, liệt kê DLL runtime/WPF có API khởi chạy tiến trình). **Kiểm thử trong gói MSIX thật (ký test)**: cài được, app chạy, `StartupTask` bật/tắt đúng, đóng cửa sổ thì ẩn vào khay và icon khay được đăng ký, lắc khi kéo tệp từ Explorer mở Shelf lúc cửa sổ chính đang ẩn, alias `bfr` chạy (exit 0), đủ 4 phần mở rộng (`comServer`, `fileExplorerContextMenus`, `startupTask`, `appExecutionAlias`). Chưa (cần môi trường/thiết bị khác): nâng cấp từ 1.2.2, Windows 10, DPI 125%/150% và đa màn hình, ảnh Store mới, đăng privacy policy |
| 4. Nộp Store | Chưa | Chờ Giai đoạn 3 |

**PR:** nhánh `feat/shelf` → `main`, CI xanh. `main` đã đỏ từ trước ở bước COM smoke test (`dotnet run --no-build` tìm sai thư mục đầu ra); đã sửa trong `ci.yml` (commit `9578106`).

**Việc cần Dev làm tay (Claude không làm được):**
- 0.3: kéo tệp thật từ Explorer rồi lắc; chuột phải vào icon khay; thả tệp vào Shelf rồi bấm "Đổi tên…".
- Ký gói bằng **chứng chỉ thật** hoặc để Store ký lại khi nộp. Gói test đã ký bằng chứng chỉ tự ký (hết hạn 30 ngày, đã gỡ khỏi máy) **không được đem nộp hay phát hành**.
- Cài thử gói trên Windows 10 và Windows 11 sạch, và thử nâng cấp đè lên bản 1.2.2 từ Store (giữ settings, preset, lịch sử, giấy phép Pro).
- Đăng xuất rồi đăng nhập lại với "Khởi động cùng Windows" đang bật để xem BFR tự vào khay.
- Đăng `docs/privacy.html` lên URL công khai (GitHub Pages); chụp lại ảnh Store với giao diện mới; nộp Partner Center.

**Ghi chú đã xử lý trong lúc làm:**
- Cài thử gói MSIX thật cần chứng chỉ có subject trùng `Publisher` của Store (`CN=F6CFEAEE-…`) và đặt trong `LocalMachine\TrustedPeople` (cần admin). Đã dùng một chứng chỉ test riêng (lưu ngoài repo) rồi gỡ sạch sau khi test.
- Hook chuột chuyển sang luồng riêng có message loop (không còn phụ thuộc luồng giao diện); bỏ qua lần kéo bắt đầu trong cửa sổ BFR; Shelf mở bằng lắc tự đóng sau 6 giây nếu không có tệp.
- Số phiên bản hợp nhất: `BFR.Core.AppVersion.Current` + `Directory.Build.props`; `VersionConsistencyTests` báo lỗi nếu manifest, `build-msix.ps1` hoặc release notes lệch.
- Store listing hiện chỉ có tiếng Việt và tiếng Anh vì manifest chỉ khai báo `vi-VN` và `en-US`; thêm listing tiếng Pháp/Trung cần khai báo thêm ngôn ngữ trong gói.

---

## Giai đoạn 0 — Ổn định nhánh (06–09/11)

| # | Việc | Ai | Tiêu chí xong |
|---|---|---|---|
| 0.1 | Chạy đủ CI ở máy local: `BFR.Core.PropertyTests` và PerfGate (chưa chạy sau các thay đổi trên nhánh) | Claude | Cả hai qua, PerfGate không vượt ngưỡng NFR |
| 0.2 | Push `feat/shelf`, mở PR vào `main` | Claude (Dev duyệt) | CI GitHub xanh |
| 0.3 | Test tay: menu chuột phải ở icon khay; kéo tệp thật từ Explorer rồi lắc; thả tệp vào Shelf rồi bấm "Đổi tên…" | Dev | Mỗi luồng chạy đúng |
| 0.4 | Rà diff `MainWindow.xaml.cs` (file trộn CRLF/LF) | Claude | Diff chỉ chứa thay đổi thật |

## Giai đoạn 1 — Sửa lỗi tồn đọng (09–13/11)

**Lỗi đã thấy khi kiểm tra giao diện**

1. Watch Folder: nhãn hiển thị `_Theo dõi thư mục`; combobox Preset hiện `BFR.Core.Preset` thay vì tên preset.
2. History: dòng "Chưa có lần áp dụng nào" vẫn hiện khi bảng có dữ liệu.
3. Regex Tester: nút "Dùng tên đang chọn" sát mép phải.
4. `Resources/app.ico`: WPF không giải mã được (hiện đã bỏ thuộc tính `Icon` của MainWindow để tránh crash). Cần xuất lại `.ico` chuẩn (16–256px).
5. Shelf mở lúc cửa sổ chính đang minimize thì nằm ở góc trái màn hình.
6. Phiên bản `1.2.2` hard-code trong Settings; cần đọc từ assembly/package.

**Nợ do tính năng mới**

7. Chuỗi mới dùng `Loc.L(vi, en)` nên tiếng Pháp và tiếng Trung đang hiện tiếng Anh; chuyển sang khoá trong `Strings.*.xaml` cho cả 4 ngôn ngữ.
8. Kiểm tra theme tối cho mọi cửa sổ phụ (mới kiểm tra MainWindow và Settings).
9. Rule Editor không còn lối mở từ giao diện: quyết định xoá hoặc nối lại.
10. Viết test cho `TrayIcon` và logic chạy nền ở mức có thể (`ShutdownMode`, `--background`).

**Tiêu chí xong:** cả 10 mục được xử lý hoặc ghi rõ lý do hoãn; test xanh.

## Giai đoạn 2 — Hoàn thiện tính năng (16–20/11)

| # | Việc | Ghi chú |
|---|---|---|
| 2.1 | Khởi động cùng Windows cho chế độ chạy nền | Thêm `uap5:StartupTask` vào `packaging/msix/AppxManifest.xml`, chạy với `--background`; công tắc trong Settings, mặc định tắt; xin quyền qua API `StartupTask` (người dùng có thể tắt trong Task Manager) |
| 2.2 | Giảm lắc nhầm | Hiện lắc khi bôi đen chữ cũng mở Shelf. Phương án: chỉ kích hoạt khi con trỏ ở ngoài cửa sổ BFR, hoặc kiểm tra định dạng dữ liệu đang kéo. Thử nghiệm trước khi chọn |
| 2.3 | Shelf tự ẩn nếu mở bằng lắc mà không có tệp được thả vào | Tránh để lại cửa sổ thừa |
| 2.4 | Đo độ trễ hook chuột khi đang đổi tên 10.000 tệp | Windows gỡ hook nếu xử lý quá khoảng 300ms; nếu sát ngưỡng thì chuyển hook sang luồng riêng |
| 2.5 | Hoàn thiện phần Pro của Shelf | Thông báo khi chạm giới hạn 50 tệp; thêm dòng Shelf vào bảng so sánh Free/Pro trong UpgradeWindow |
| 2.6 | **Đóng băng tính năng cuối ngày 20/11** | Sau mốc này chỉ sửa lỗi |

## Giai đoạn 3 — Kiểm thử phát hành và chuẩn bị Store (23–27/11)

**Kiểm thử**

| Hạng mục | Chi tiết |
|---|---|
| Nâng cấp từ 1.2.2 | Cài bản Store cũ, dùng thử, cài bản mới đè lên: settings, preset, lịch sử và giấy phép Pro phải còn nguyên |
| Windows 10 (19041) | Là `MinVersion` trong manifest; Mica và WPF-UI phải tự lùi về nền đặc |
| DPI và màn hình | 100% / 125% / 150%; đa màn hình khác DPI (vị trí Shelf khi lắc) |
| Gói MSIX | Hook chuột, icon khay, khởi động cùng Windows, context menu Explorer — chạy từ gói đã ký, không phải `dotnet run` |
| Windows App Certification Kit | Chạy WACK trên gói MSIX, xử lý mọi lỗi bắt buộc |
| Bản portable | SmartScreen và các antivirus phổ biến với hook chuột toàn cục |
| Hồi quy | Đổi tên, undo, xung đột, CLI `bfr`, Watch Folder, Explorer context menu |

**Store và pháp lý**

- **Cập nhật chính sách quyền riêng tư** (`docs/privacy.html`): nêu rõ tính năng lắc chuột theo dõi chuột toàn hệ thống, chỉ chạy khi người dùng bật, xử lý trên máy, không ghi và không gửi dữ liệu. *Bắt buộc trước khi nộp.*
- Nâng `Version` trong manifest lên `1.3.0.0`; cập nhật `MaxVersionTested` theo bản Windows đã thực sự kiểm thử.
- Cập nhật `docs/STORE_LISTING.md` cho cả 4 ngôn ngữ; chụp ảnh Store mới với giao diện WPF-UI (sáng và tối).
- Viết `docs/RELEASE_NOTES_v1.3.0.md`.

**Tiêu chí xong:** WACK qua; nâng cấp từ 1.2.2 không mất dữ liệu; privacy policy đã đăng; gói đã ký sẵn sàng.

## Giai đoạn 4 — Nộp Store và go-live (30/11 → ~04/12)

1. **30/11** (Dev): nộp lên Partner Center — gói, listing, ảnh, release notes, link privacy policy. Cân nhắc phát hành dần (gradual rollout) nếu tài khoản có tuỳ chọn này.
2. Chờ Microsoft duyệt. Nếu bị từ chối: sửa theo báo cáo, nộp lại (đã tính 1–2 ngày đệm).
3. **Go-live**: merge `feat/shelf` vào `main`, gắn tag `v1.3.0`, phát hành bản portable trên GitHub Releases.

## Sau go-live (2 tuần đầu)

- Theo dõi báo cáo crash/health trong Partner Center và đánh giá Store (app không có telemetry, đây là nguồn tín hiệu duy nhất).
- Chuẩn bị sẵn nhánh hotfix `1.3.1`.
- Kéo traffic: đăng thông báo lên các kênh như Product Hunt; quay video ngắn cảnh lắc chuột mở Shelf (điểm dễ gây chú ý nhất).
- Bắt đầu đặc tả v1.4: Radial menu, lưu Shelf thành dự án (Pro).

## Checklist go/no-go trước khi nộp

- [ ] CI xanh đủ 4 bộ test và PerfGate
- [ ] Test tay xong: icon khay, lắc chuột khi kéo tệp thật, khởi động cùng Windows
- [ ] Nâng cấp từ 1.2.2 giữ nguyên dữ liệu và giấy phép Pro
- [ ] WACK qua
- [ ] Privacy policy đã cập nhật (hook chuột, chạy nền) và đã đăng online
- [ ] Listing và ảnh chụp đủ 4 ngôn ngữ
- [ ] Phiên bản 1.3.0.0 khớp ở manifest, Settings và release notes
- [ ] Không còn lỗi mức chặn trong danh sách Giai đoạn 1

## Rủi ro chính

| Rủi ro | Giảm thiểu |
|---|---|
| Hook chuột toàn cục bị antivirus hoặc Store review soi kỹ | Tính năng opt-in, mặc định tắt; privacy policy rõ ràng; kiểm tra bản portable với antivirus |
| WPF-UI chưa được kiểm thử trên Windows 10 | Kiểm thử trên 19041 ở Giai đoạn 3 |
| Thời gian Microsoft duyệt không đoán trước được | Dành 1–2 ngày đệm; nộp ngay đầu tuần |
