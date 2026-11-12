# 12. THƯƠNG MẠI HÓA, ASO VÀ GO-TO-MARKET

Nguồn: `docs/batch_file_renamer_spec_v1.0.md` — Phần 13.

---

## 12.1 Định giá

- Pro mua đứt trọn đời: đề xuất **7,99 USD từ v2.0 (trước đó 3,99 USD)** (giá theo sức mua khu vực), Durable Add-on qua `Windows.Services.Store` (Product ID ví dụ `BFR_PRO_LIFETIME`). Tỷ lệ chia sẻ doanh thu cần kiểm tra theo chính sách Store hiện hành; bảng dưới dùng giả định 85%.
- Có thể dùng thử Pro 14 ngày một lần để người dùng trải nghiệm EXIF, Undo đa bước và CLI.

---

## 12.2 Dự phóng doanh thu (thận trọng)

Công thức, với `I` là lượt cài mới mỗi tháng, `r` là tỷ lệ người dùng còn hoạt động, `c` là tỷ lệ mua Pro, `P` là giá (USD), `s` là tỷ lệ thực nhận:

`R_thang = I * r * c * P * s`

| Kịch bản | I | r | c | P | s | Doanh thu ròng/tháng (xấp xỉ) |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: |
| Bi quan | 600 | 50% | 1,0% | 3,99 | 0,85 | khoảng 10 USD |
| Thận trọng | 2.000 | 50% | 1,5% | 3,99 | 0,85 | khoảng 51 USD |
| Cơ sở | 6.000 | 55% | 2,5% | 3,99 | 0,85 | khoảng 280 USD |
| Thành công lớn | 20.000 | 55% | 3,0% | 3,99 | 0,85 | khoảng 1.119 USD |

> **Nhận định thẳng thắn:** Với đối thủ miễn phí mạnh (PowerRename), kịch bản bi quan/thận trọng là khả năng thực tế đáng kể; doanh thu ban đầu chỉ là thu nhập phụ nhỏ. Giá trị bền vững hơn của dự án nằm ở: (1) **chất lượng kỹ thuật chứng minh được** (bộ kiểm thử, property test, journal) rất phù hợp để đưa vào hồ sơ kỹ sư phần mềm, (2) kinh nghiệm phát hành sản phẩm thật lên Store, (3) tái sử dụng hạ infrastructure chung với QAS/WPD. Doanh thu thực tế là đường cong tăng dần rồi giảm sau đợt ra mắt; cập nhật bảng này bằng số liệu thật sau 2–3 tháng.

---

## 12.3 Chiến lược phân phối và license

- **Store (MSIX):** kênh chính, có IAP.
- **GitHub Releases (portable):** chỉ có tính năng Free (hoặc cơ chế license khóa riêng nếu muốn bán ngoài Store, vì IAP của Store không dùng được ngoài Store).
- **Tùy chọn mã nguồn mở `BFR.Core` (giấy phép MIT):** tăng uy tín và lượng sao trên GitHub, giúp người khác kiểm chứng độ an toàn; phần giao diện và tính năng Pro vẫn đóng. Cân nhắc kỹ trước khi quyết định (ảnh hưởng khả năng thương mại hóa).

---

## 12.4 ASO (tối ưu hiển thị trên Store)

- **Tên đề xuất:** `Batch File Renamer: Regex, Numbering & Preview` (hoặc tên thương hiệu riêng + phụ đề mô tả, để tránh trùng tên chung).
- **Mô tả ngắn:** Đổi tên hàng loạt tệp với Regex, số thứ tự, ngày tháng và cắt tỉa tên. Xem trước kết quả, phát hiện trùng tên và hoàn tác bất cứ lúc nào.
- **Từ khóa (mô tả chức năng, không dùng tên thương hiệu khác):** `batch rename`, `bulk rename files`, `rename multiple files`, `regex rename`, `file renamer`, `rename photos by date`, `add numbering to files`.
- **Bộ ảnh chụp màn hình (5 ảnh):**
  1. Ảnh chính: bảng "Tên cũ → Tên mới" trước/sau, chữ "Xem trước chính xác. Đổi tên an toàn."
  2. Chuỗi quy tắc và Rule Trace.
  3. Đánh số tự nhiên và ngày chụp EXIF.
  4. Phát hiện xung đột và hoàn tác nhiều bước.
  5. Preset tiếng Việt: bỏ dấu, dọn dẹp tên tệp.
- **Video demo 15–20 giây:** kéo 100 ảnh vào, chọn preset, xem preview, áp dụng, hoàn tác.

---

## 12.5 Go-To-Market (chi phí gần bằng 0)

1. Bài viết hướng dẫn tiếng Việt: "Cách đổi tên hàng loạt tệp, bỏ dấu tiếng Việt và đánh số thứ tự" (nhu cầu tìm kiếm thực tế, ít cạnh tranh nội dung tiếng Việt).
2. Chia sẻ GIF/video ngắn trên cộng đồng sinh viên, nhiếp ảnh, lập trình, tuân thủ nội quy từng nhóm và nêu rõ là sản phẩm của chính mình.
3. Giveaway mã Pro không ràng buộc đánh giá (không yêu cầu đánh giá 5 sao).
4. Đăng bài kỹ thuật về độ tin cậy (journal, property test, thuật toán hoán đổi tên) lên blog/GitHub để thu hút người dùng nâng cao và nhà tuyển dụng.
5. Liên kết chéo trong trang Giới thiệu với QAS và WPD.

---

## 12.6 KPI

| Chỉ số | Cách đo | Tham chiếu |
| :--- | :--- | :--- |
| Lượt cài mới/tháng | Partner Center | Theo kịch bản 12.2 |
| Tỷ lệ hoàn tất một lần đổi tên (mở app → áp dụng thành công) | Telemetry opt-in | Trên 60% |
| Tỷ lệ lỗi/hoàn tác bất thường | Telemetry opt-in + báo cáo | Dưới 1% lần áp dụng |
| Tỷ lệ chuyển đổi Pro | Partner Center | 1–3% |
| Điểm đánh giá Store | Partner Center | Từ 4,3 sao trở lên |
