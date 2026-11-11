# Kế hoạch v1.5 — Bảo mật, giao diện và tính năng mở rộng

> Lập ngày: 12/11/2026 · Dựa trên v1.4.0 (nhánh `feat/v1.4-ui`)
> Quyết định đã chốt: gộp toàn bộ A, B, C vào v1.5; **không bán mã license ngoài Store nên bỏ C6 (ký số license)**; Radial và OCR nằm trong phạm vi.

## 1. Phạm vi

### Phần C — Bảo mật
| # | Hạng mục | Căn cứ | Việc cần làm |
|---|---|---|---|
| C1 | Khoá named pipe theo người dùng | `SingleInstanceIpc.cs` tạo pipe không có `PipeSecurity`; spec 9.2 yêu cầu ACL cùng user | `PipeOptions.CurrentUserOnly` hoặc `PipeSecurity` chỉ cho SID hiện tại |
| C2 | Kiểm tra dữ liệu vào IPC và dòng lệnh | `IpcPayload`, `StartupArgs` nhận đường dẫn, `--file-list`, `--quick` không giới hạn | Giới hạn kích thước tin nhắn và số đường dẫn; `GetFullPath`, loại ký tự điều khiển, chặn UNC lạ; `--file-list` chỉ nhận trong thư mục tạm của BFR và xoá sau khi đọc; `--quick` chỉ nhận khoá preset trong danh sách trắng |
| C3 | Chống ReDoS toàn diện | `FindReplaceRule` có timeout 250 ms; cần rà Regex Tester, `ReplaceDictRule`, preset nhập từ ngoài | Timeout thống nhất cho mọi chỗ dùng Regex; báo lỗi rõ khi hết giờ |
| C4 | Nhập preset và file list an toàn | `PresetStore`, `FileListReader` đọc dữ liệu từ ngoài | Giới hạn kích thước và độ sâu JSON, kiểm tra schema, từ chối tên có ký tự cấm; fuzz `ListParser`, `PresetStore`, `HotkeySpec` |
| C5 | Cập nhật an toàn | `GitHubReleasesUpdateFeed` có User-Agent cứng `1.2 (+https://localhost)` | Chỉ HTTPS và host trong danh sách cho phép; giới hạn kích thước phản hồi; `Process.Start` chỉ mở link `https://`; User-Agent theo `AppVersion` |
| C7 | Chuỗi cung ứng và CI | CI chỉ build và test | `dotnet list package --vulnerable`, Dependabot, CodeQL, `TreatWarningsAsErrors` + analyzer, ký exe/MSIX bằng chứng chỉ thật, SBOM |
| C8 | Journal, Undo, rò rỉ dữ liệu | Journal và lịch sử chứa đường dẫn đầy đủ | Tự xoá lịch sử theo thời hạn (chỉnh được); nút "Xoá toàn bộ dữ liệu"; không ghi đường dẫn vào log; cập nhật `PRIVACY_POLICY.md` |

*(C6 bị bỏ: mã license chỉ phát hành qua Microsoft Store.)*

### Phần B — Tính năng
| # | Tính năng | Free / Pro |
|---|---|---|
| B5 | Token metadata nhạc/ảnh/video (`{artist}`, `{title}`, `{width}`…), dùng lại `MediaMetadataReader`, `ExifMetadataReader` | Free: token cơ bản; Pro: đầy đủ |
| B6 | Watch Folder nhiều thư mục, mỗi thư mục một preset | Pro |
| B7 | Lưu Shelf thành dự án `.bfrproj` (tệp, rule, thứ tự) và mở lại | Pro |
| B8 | Xuất/nhập preset và gói preset chia sẻ (qua kiểm tra C4) | Free |
| B9 | Xem trước hoàn tác (dry-run Undo) và hoàn tác từng phần theo tệp | Free |
| B10 | Radial menu: giữ nút chuột giữa trên Shelf, bật vòng tròn thao tác nhanh (Đánh số, Bỏ dấu, Thêm ngày, chữ thường…); dựa trên `MouseHook`, `ShakeDetector` | Free, opt-in |
| B11 | OCR: đổi tên ảnh/PDF scan theo chữ nhận dạng được, xử lý hoàn toàn trên máy (ví dụ `Windows.Media.Ocr`, không thêm gói lớn); cập nhật chính sách riêng tư | Pro |

### Phần A — Frontend / UX
| # | Hạng mục | Mô tả |
|---|---|---|
| A8 | Khả năng truy cập | Điều hướng bàn phím trọn vẹn, `AutomationProperties` đủ, High Contrast, Narrator |
| A9 | Bảng xem trước | Lọc theo trạng thái (lỗi, xung đột, không đổi), chọn nhiều dòng, diff cũ/mới theo ký tự |
| A10 | Onboarding lần đầu | Hướng dẫn 3 bước, nhắc phím tắt, tắt được |
| A11 | Rule builder | Kéo thả sắp xếp, nhân bản, bật/tắt nhanh, tóm tắt một dòng cho mỗi rule |
| A12 | Mật độ hiển thị | Compact/Comfortable, tôn trọng DPI |
| A13 | Dọn nợ giao diện | Gom hằng khoảng cách/màu vào `Theme.xaml`; kiểm tra cắt chữ 4 ngôn ngữ × 2 theme bằng `capture-ui-matrix.ps1` |
| A14 | Hiệu năng | Ảo hoá danh sách 100.000 tệp, giữ PerfGate như v1.4 |

## 2. Thứ tự thực hiện
1. **Đợt 1 — Bảo mật (C1–C5, C8):** sửa nhỏ, giá trị cao; có thể phát hành sớm thành v1.4.1.
2. **Đợt 2 — Frontend (A8–A14) + B8, B9.**
3. **Đợt 3 — Tính năng lớn (B5, B6, B7, B10):** logic Core và test trước, giao diện sau.
4. **Đợt 4 — C7 (CI, ký, SBOM), rồi B11 (OCR).** OCR là mục duy nhất được phép cắt nếu tăng dung lượng gói quá mức hoặc phát sinh vấn đề riêng tư.

## 3. Kiểm thử
- **Tự động (bắt buộc xanh):** Core, Presentation, Property, PerfGate, Shell smoke; Release build 0 warning.
- **Bảo mật mới:** client user khác bị pipe từ chối; payload quá lớn hoặc đường dẫn lạ bị chặn; Regex gây treo bị ngắt; fuzz không ném lỗi; update feed từ chối HTTP và host lạ.
- **UI Automation:** điều hướng từng trang, luồng thêm → xem trước → áp dụng → Hoàn tác, ảnh chụp 4 ngôn ngữ × 2 theme.
- **MSIX:** dựng gói → ký test → cài → WACK → kiểm tra menu Explorer, `StartupTask`, hook chuột, phím tắt → gỡ sạch.
- **Lưu ý khi test apply:** sao lưu và khôi phục AppData thật (settings, history, journal); không chạy apply dưới chế độ ghi đè tier Free.

## 4. Tiêu chí hoàn thành
- Mọi test ở mục 3 đạt; PerfGate không chậm hơn v1.3/v1.4.
- Không còn lỗ hổng mức cao trong danh sách C1–C5.
- Tài liệu cập nhật: release notes v1.5, privacy policy (OCR, xoá dữ liệu), Store listing.

## 5. Rủi ro
| Rủi ro | Giảm thiểu |
|---|---|
| Gói quá lớn hoặc chính sách riêng tư phức tạp khi thêm OCR | Dùng OCR có sẵn của Windows; cắt B11 nếu cần |
| Ảo hoá danh sách làm hỏng test UI | Giữ `AutomationProperties.Name`; chạy `packaging/smoke-ui.ps1` trước khi push |
| Chặn đường dẫn quá chặt (C2) làm hỏng luồng menu Explorer | Test COM smoke và chạy `--file-list` thật |
| Nhiều hạng mục trong một bản | Chia 4 đợt, mỗi đợt có commit và test xanh riêng |
