# HƯỚNG DẪN KIỂM THỬ APP — Batch File Renamer v1.2 (UI v2.0)

> App đã được chạy sẵn từ gói `artifacts/msix-layout/BatchFileRenamer.exe`.
> Dữ liệu mẫu: `C:\Users\<bạn>\Desktop\BFR-Demo` (15 tệp trong 3 thư mục) — **đây là bản sao, tha hồ làm**.
> Nếu app bị đóng: mở lại bằng lệnh ở mục 8.

---

## 1. Kiểm tra nhanh 2 phút (luồng chính)

| # | Thao tác | Kỳ vọng |
| :--- | :--- | :--- |
| 1 | Nhìn bảng giữa | Cột **Tên cũ** có phần chữ **đỏ gạch ngang**, cột **Tên mới** có phần **xanh** → đây là *diff* so sánh tên cũ/mới |
| 2 | Bấm **Áp dụng 15 tệp** (góc phải dưới) | Hộp thoại **"Áp dụng đổi tên"**: số tệp, số thư mục bị tác động, gợi ý "Có thể hoàn tác", 3 ví dụ |
| 3 | Bấm **Áp dụng** trong hộp thoại | Tệp trên đĩa đổi tên (kiểm tra `Desktop\BFR-Demo`), thanh dưới hiện InfoBar xanh *"Đã đổi tên 15/15 tệp"* kèm nút Hoàn tác / Lịch sử / Xuất báo cáo |
| 4 | Bấm **Hoàn tác** | Tệp về đúng tên cũ, InfoBar báo đã hoàn tác |
| 5 | Bấm vào 1 dòng trong bảng | Khung **CHI TIẾT QUY TẮC** bên dưới hiện timeline: *Tên gốc → Số thứ tự → Kết quả* |

---

## 2. Kiểm tra từng vùng giao diện

### A. Thanh tiêu đề (trên cùng)
- **Badge `Free`** → đang ở cấp miễn phí. Bấm **Cài đặt** → xem mục *Giấy phép*.
- Nút **Nâng cấp Pro** → bấm thử: hiện mô tả Pro + nút *"Dùng thử Pro 30 ngày"* và *"Mua Pro"*. Bấm **Dùng thử** → badge đổi thành **Pro (dùng thử)**.
  - Lưu ý: mua thật cần bản cài từ Microsoft Store (bản portable chỉ có Free).

### B. Thanh lệnh (1 hàng)
| Nút | Cách kiểm tra | Kỳ vọng |
| :--- | :--- | :--- |
| **＋ Thêm tệp** | Bấm → chọn vài tệp trong `Desktop\BFR-Demo` | Tệp được thêm vào bảng, preview cập nhật ngay |
| **Thêm thư mục** | Chọn `Hop 1 - Nhan khoa` | Thêm cả thư mục con khi bật chip *Quét thư mục con* |
| **Quét thư mục con** | Bật/tắt chip | Tắt → chỉ thấy tệp ở cấp gốc; bật → thấy cả `Hop 1`, `Hop 2` |
| **Gồm cả thư mục** | Bật chip | Thư mục cũng hiện trong bảng (mặc định Free chỉ quét 2 cấp) |
| **Sắp xếp theo** | Đổi giữa *Tên (tự nhiên)* / *Ngày tạo* / *Kích thước* | Thứ tự dòng **đổi thật** → số thứ tự đánh số cũng đổi theo (kiểm tra cột Tên mới) |
| **Xóa danh sách** | Bấm | Bảng trống, hiện **empty state** (kéo thả + 3 preset gợi ý) |
| **⋯ Thêm** | Bấm | Menu chứa: Theo dõi thư mục · Menu chuột phải Explorer · Lịch sử & hoàn tác · Xuất báo cáo · Thư viện preset · Nhập/Xuất preset · Kiểm tra cập nhật |

### C. Rule Builder (cột trái)
1. Bấm **✎ (bút chì)** trên thẻ quy tắc → **form mở rộng tại chỗ** (không mở hộp thoại). Sửa "Bắt đầu" từ `1` → `10` → preview cập nhật sau ~150 ms.
2. Bấm **checkbox** của thẻ → tắt quy tắc → preview đổi ngay, chip trạng thái đổi theo.
3. Bấm **🗑** → xoá quy tắc.
4. **＋ Thêm quy tắc** → menu 9 loại quy tắc (Tìm và thay, Số thứ tự, Cắt tỉa, Chèn, Hoa/thường, Bỏ dấu, Ngày tháng, Phần mở rộng, Mẫu tên).
   - **Ở cấp Free, thêm quy tắc thứ 4 sẽ bị chặn** kèm thông báo nâng cấp (giới hạn 3 quy tắc).
5. Combobox **Preset** + nút **⋯** cạnh nó: Lưu thành preset mới / Nhập / Xuất / Quản lý (thư viện preset).

### D. Bảng xem trước (cột phải)
1. **Bỏ chọn** 1 dòng (checkbox đầu cột) → dòng đó **không được đổi tên**, số trên nút *"Áp dổi N tệp"* giảm 1.
2. Chip lọc: **Tất cả / Sẽ đổi / Không đổi / Vấn đề** → bảng lọc ngay.
3. Gõ vào ô tìm kiếm (bên phải hàng chip) → lọc theo tên/thư mục. Phím tắt `Ctrl+F` đưa con trỏ vào ô này.
4. Cột **Trạng thái**: ✓ Sẽ đổi (xanh) · … Không đổi · ⚠ Xung đột (cam) · ✖ Lỗi (đỏ).
5. Kéo–thả tệp từ Explorer **vào bất kỳ đâu trong cửa sổ** → được thêm vào lô.

### E. Thanh hành động dưới
- Chip tổng: *"15 tệp | Sẽ đổi 15 | Không đổi 0 | Lỗi 0"*.
- **Áp dụng N tệp** (nút xanh) → bị **vô hiệu hoá** khi còn xung đột hoặc không còn tệp nào sẽ đổi.
- **Hoàn tác** → hoàn tác lần gần nhất.

---

## 3. Kiểm tra các màn phụ (menu **⋯ Thêm**)

| Mục | Cách kiểm tra | Kỳ vọng |
| :--- | :--- | :--- |
| **Lịch sử & hoàn tác** | Mở menu → chọn | Bảng lần áp dụng (thời gian, số tệp, preset, trạng thái); bấm *"Hoàn tác mục đã chọn"* |
| **Thư viện preset** | Mở menu → chọn | Danh sách preset đã lưu; lưu/áp dụng/xoá; **Chia sẻ** (chép JSON vào clipboard), **Nhập từ bảng nhớ** |
| **Theo dõi thư mục** | Mở menu → chọn | Cửa sổ: chọn thư mục + preset → **Bắt đầu** → copy 1 file mới vào thư mục → file tự được đổi tên sau ~1,5 s; nhật ký ghi lại |
| **Menu chuột phải Explorer** | Mở menu → chọn | Bật/tắt tích hợp; bật xong chuột phải 1 tệp trong Explorer → có mục *"Đổi tên hàng loạt với BFR"* |
| **Xuất báo cáo** | Mở menu → chọn (Pro) | Xuất CSV/JSON danh sách đổi tên lần gần nhất |
| **Kiểm tra cập nhật** | Mở menu → chọn | Bản portable: gọi URL nguồn cập nhật; bản Store: báo *"Store tự cập nhật"* |

---

## 4. Kiểm tra Cài đặt

Bấm **Cài đặt** (góc trên phải):
- **Ngôn ngữ**: Tiếng Việt / English / Français / 简体中文 → đổi là **đổi ngay lập tức**.
- **Giao diện**: *Theo hệ thống / Sáng / Tối* → đổi **ngay lập tức** toàn bộ cửa sổ.
- **Cập nhật**: bật/tắt *"Tự động kiểm tra bản cập nhật mới"*, xem URL nguồn.
- **Giấy phép**: trạng thái Free/Pro + nút nâng cấp.
- **Quyền riêng tư**: nhắc 100% xử lý cục bộ.

---

## 5. Kiểm tra giới hạn Free (quan trọng)

| Hạn mục | Kỳ vọng ở cấp Free |
| :--- | :--- |
| Số quy tắc | Tối đa **3** — thêm quy tắc thứ 4 bị chặn kèm thông báo |
| Độ sâu quét | Tối đa **2 cấp** thư mục con |
| Preset lưu được | Tối đa **3** |
| Hoàn tác | Chỉ giữ **1 lần gần nhất** |
| Ngày chụp EXIF / ngày tạo media | **Khoá** (nguồn ngày không hiện trong tuỳ chọn) |
| Xuất báo cáo / CLI / đánh số La Mã | **Khoá** |

**Thử bản Pro (chỉ máy dev):** đóng app, mở PowerShell rồi chạy
```powershell
$env:BFR_DEV_TIER = "pro"
& "D:\MyProjects\MS\Batch File Renamer\artifacts\msix-layout\BatchFileRenamer.exe"
```
→ badge hiện **Pro**, số quy tắc không giới hạn, quét sâu không giới hạn, mở khoá EXIF/báo cáo.

---

## 6. Kiểm tra hộp thoại nâng cấp Pro

Bấm badge **Free** hoặc nút **Nâng cấp Pro** ở thanh tiêu đề → mở hộp thoại nâng cấp:

| Hạng mục | Cách kiểm tra | Kỳ vọng |
| :--- | :--- | :--- |
| **Bố cục** | Quan sát | Header *"Nâng cấp lên Batch File Renamer Pro"*, dòng phụ *"Mua một lần, dùng trọn đời"*, bảng Free vs Pro 7 dòng, nút **Mua Pro** (chính) + **Dùng thử Pro 30 ngày** + **Để sau**, liên kết **Tôi đã có mã kích hoạt** |
| **Theme** | Đổi Sáng/Tối rồi mở lại dialog | Dialog đổi màu theo app, bo góc, có nút **X** |
| **Đóng** | Bấm **X** hoặc phím **Esc** | Dialog đóng, app vẫn chạy |
| **Mã kích hoạt** | Bấm *"Tôi đã có mã kích hoạt"* → nhập `BFR-XXXXX-XXXXX-XXXXX-XXXXX-XXXXX` → **Kích hoạt** | Thông báo lỗi rõ ràng (sai định dạng / sai checksum), không bật Pro |
| **Ngữ cảnh** | Ở cấp Free, thêm quy tắc thứ 4 hoặc bấm **Xuất báo cáo** | Dialog mở kèm dải *"Tính năng '…' chỉ có trong gói Pro."* |
| **Dùng thử** | Bấm **Dùng thử Pro 30 ngày** | Badge đổi thành **Pro (dùng thử)**; mở lại dialog → ẩn nút dùng thử, hiện *"Còn 29 ngày dùng thử Pro."* |
| **Đã là Pro** | Bấm badge **Pro** | Mở màn hình thông tin license (trạng thái, ngày mua, nguồn kích hoạt) |

**Thử bản Pro (chỉ máy dev):** đóng app, mở PowerShell rồi chạy
```powershell
$env:BFR_DEV_TIER = "pro"
& "D:\MyProjects\MS\Batch File Renamer\artifacts\msix-layout\BatchFileRenamer.exe"
```
→ badge đổi thành **Pro**, hết giới hạn 3 quy tắc / 2 cấp quét, mở khoá EXIF + xuất báo cáo.

Ảnh từng trạng thái: `docs/ui-upgrade/upgrade-1..5-*.png`.
## 7. Kiểm tra tính năng mới (A1–A4)

| Tính năng | Cách kiểm tra | Kỳ vọng |
| :--- | :--- | :--- |
| **Trình thử Regex** | Thêm quy tắc *Tìm và thay* → bấm nút (i) trên thẻ quy tắc | Hộp thoại mở; nhập mẫu `^(\d+)_(\w+)$`, thay bằng `$2_$1`, chuỗi thử `2024_file` → kết quả `file_2024`, hiện các nhóm `[0] [1] [2]` |
| **Áp dụng Regex** | Trong Trình thử Regex bấm *Dùng cho tất cả tệp* | Quay lại thẻ quy tắc: mẫu/thay bằng/ngắt phạm vi đã cập nhật, bật Regex |
| **Thư viện mẫu** | Mở danh sách *Thư viện mẫu* | Có 5 mẫu dựng sẵn; chọn mẫu → mẫu/thay bằng/chuỗi thử được nạp |
| **Đổi thứ tự quy tắc** | Kéo biểu tượng ⠿ trên thẻ quy tắc, hoặc bấm ↑/↓ | Thứ tự quy tắc đổi ngay và kết quả đánh số đổi theo |
| **Command Palette** | Bấm **Ctrl+K** | Hộp thoại lệnh mở; gõ `lich` (không dấu) → tìm thấy lệnh *Lịch sử*; ↑↓ chọn, Enter chạy, Esc đóng |
| **Cài đặt → Kiểm tra ngay** | Cài đặt → *Kiểm tra ngay* | Hiện kết quả ngay dưới nút (bản mới / đang là bản mới nhất / lỗi) |
| **Thư viện preset** | ⋯ Thêm → *Thư viện preset* | Có ô tìm kiếm, giao diện đồng bộ theme, thông báo rỗng khi chưa có preset |
| **Đổi ngôn ngữ** | Cài đặt → Ngôn ngữ → English | Toàn bộ chuỗi (kể cả nhãn tham số quy tắc và lựa chọn) đổi sang tiếng Anh |

Ảnh minh hoạ: `docs/ui-features/feature-1..4-*.png`.
## 8. Kiểm tra phím tắt

| Phím | Chức năng |
| :--- | :--- |
| `Ctrl+O` | Thêm tệp |
| `Ctrl+Shift+O` | Thêm thư mục |
| `Ctrl+Z` | Hoàn tác |
| `Ctrl+S` | Lưu preset ra tệp |
| `Ctrl+F` | Ô tìm kiếm |
| `Ctrl+K` | ủ Command Palette (lçnh nhanh) |
| `Ctrl+Enter` | Áp dụng |
| `F5` | Tính lại preview |

---

## 9. Kiểm tra bằng CLI (mở PowerShell khác)

```powershell
$cli = "D:\MyProjects\MS\Batch File Renamer\artifacts\msix-layout\bfr.exe"

& $cli license status                     # xem cấp giấy phép + giới hạn
& $cli context-menu status                # trạng thái menu chuột phải
& $cli rename "C:\Users\<bạn>\Desktop\BFR-Demo" --find "Bai giang" --replace "BG" --dry-run --format json
& $cli history                            # lịch sử các lần đổi tên
& $cli report export --last --format csv --out "$env:TEMP\bfr-report.csv"
```
> Ở cấp **Free**, `rename/undo/report` bị chặn (in thông báo nâng cấp Pro) — đúng thiết kế. Thêm `$env:BFR_DEV_TIER="pro"` để thử.

---

## 10. Mở lại app khi cần

```powershell
$demo = "C:\Users\<bạn>\Desktop\BFR-Demo"
$list = Get-ChildItem $demo -Recurse -File | Select-Object -ExpandProperty FullName
$list | Set-Content "$demo\danh-sach.txt" -Encoding UTF8
& "D:\MyProjects\MS\Batch File Renamer\artifacts\msix-layout\BatchFileRenamer.exe" --file-list "$demo\danh-sach.txt" --preset "Đánh số theo thứ tự"
```

---

## 11. Bảng ghi kết quả (điền rồi gửi lại)

| Mục | Kết quả | Ghi chú |
| :--- | :--- | :--- |
| Luồng Thêm → Xem trước → Áp dụng → Hoàn tác | ☐ Đạt ☐ Lỗi | |
| Diff màu (đỏ gạch ngang / xanh) | ☐ Đạt ☐ Lỗi | |
| Bỏ chọn 1 tệp → không đổi tên tệp đó | ☐ Đạt ☐ Lỗi | |
| Lọc theo chip + tìm kiếm | ☐ Đạt ☐ Lỗi | |
| Sắp xếp theo → đổi thứ tự đánh số | ☐ Đạt ☐ Lỗi | |
| Mở tuỳ chọn quy tắc tại chỗ | ☐ Đạt ☐ Lỗi | |
| Hộp thoại xác nhận áp dụng | ☐ Đạt ☐ Lỗi | |
| Chuyển theme sáng/tối | ☐ Đạt ☐ Lỗi | |
| Đổi ngôn ngữ (fr / zh-Hans) | ☐ Đạt ☐ Lỗi | |
| Giới hạn Free (3 quy tắc / 2 cấp / 3 preset) | ☐ Đạt ☐ Lỗi | |
| Watch Folder tự đổi tên tệp mới | ☐ Đạt ☐ Lỗi | |
| Menu chuột phải Explorer | ☐ Đạt ☐ Lỗi | |
| Lịch sử & hoàn tác từng mục | ☐ Đạt ☐ Lỗi | |
| Xuất báo cáo CSV/JSON | ☐ Đạt ☐ Lỗi | |
| CLI (dry-run / history / report) | ☐ Đạt ☐ Lỗi | |
