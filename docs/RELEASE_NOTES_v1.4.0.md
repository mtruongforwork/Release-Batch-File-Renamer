# BFR - Batch File Renamer — Release Notes v1.4.0

Tài liệu ghi chú phát hành chuẩn bị nộp duyệt cập nhật trên **Microsoft Partner Center** cho bản **v1.4.0** (Package Version: `1.4.0.0`).

---

## What's New in this Version (English - en-US)

### Release highlights for v1.4.0:
- **A new layout with a navigation rail**:
  - One page per feature: Rename, Shelf, History, Presets, Watch Folder and Settings, switched from a rail on the left.
  - Dialogs (apply confirmation, upgrade, license info, rule library) now open inside the window instead of as separate windows.
  - A rule library with searchable cards and short descriptions replaces the text menu for adding rules.
  - A clearer empty screen with a drop zone, quick preset suggestions and tips on faster ways to add files.
- **Rename from a list (Excel / CSV)**:
  - **Rename by list**: paste two columns (old name, new name) from Excel, or a single column to rename by order.
  - **Replace dictionary**: several find → replace pairs applied in one pass.
  - Free handles up to 50 rows per list; **Pro has no limit** and can also import a CSV/TSV file.
- **Fix duplicate names automatically**: when two files would get the same name, or a new name matches a file that is not in the batch, one click adds " (2)", " (3)"… before the extension.
- **Quick rename from the Explorer right-click menu**: right-click files → *Batch File Renamer* → pick *Number files*, *Remove accents*, *Add date*, *Clean up names* or *lowercase*. The result appears as a small notification with **Undo**; no window is opened.
- **Global hotkey to open Shelf (optional, off by default)**: choose a combination in Settings (default Ctrl+Shift+Space). It only receives the exact combination you choose.
- **Fixes and performance**:
  - Collisions with a file already in the folder (but not in the batch) are now detected before you apply, instead of failing halfway.
  - The preview table now refreshes after you edit the parameters of a rule you just added.
  - Regex Find/Replace is dramatically faster on large batches (the pattern is now built once per batch instead of once per file).
  - Choosing **Cancel** in the apply confirmation no longer renames anything.

---

## Những điểm mới trong phiên bản này (Tiếng Việt - vi-VN)

### Điểm nổi bật trong bản v1.4.0:
- **Bố cục mới với thanh điều hướng bên trái**:
  - Mỗi chức năng một trang: Đổi tên, Khay tệp, Lịch sử, Preset, Theo dõi thư mục và Cài đặt, chuyển qua thanh bên trái.
  - Các hộp thoại (xác nhận áp dụng, nâng cấp, thông tin license, thư viện quy tắc) mở ngay trong cửa sổ thay vì cửa sổ riêng.
  - Thư viện quy tắc dạng thẻ có tìm kiếm và mô tả ngắn thay cho menu chữ khi thêm quy tắc.
  - Màn hình trống rõ ràng hơn: vùng thả tệp, gợi ý preset nhanh và mẹo thêm tệp nhanh hơn.
- **Đổi tên theo danh sách (Excel / CSV)**:
  - **Đổi tên theo danh sách**: dán hai cột (tên cũ, tên mới) từ Excel, hoặc một cột để đổi tên theo thứ tự.
  - **Từ điển thay thế**: nhiều cặp tìm → thay áp dụng cùng một lượt.
  - Bản Free xử lý tối đa 50 dòng mỗi danh sách; **Pro không giới hạn** và nhập được cả tệp CSV/TSV.
- **Tự xử lý trùng tên**: khi hai tệp sắp có cùng tên, hoặc tên mới trùng một tệp không thuộc lô, một cú bấm sẽ thêm " (2)", " (3)"… trước phần mở rộng.
- **Đổi tên nhanh từ menu chuột phải của Explorer**: bấm chuột phải tệp → *Batch File Renamer* → chọn *Đánh số*, *Bỏ dấu*, *Thêm ngày*, *Dọn dẹp tên* hoặc *chữ thường*. Kết quả hiện ở thông báo nhỏ có nút **Hoàn tác**, không mở cửa sổ.
- **Phím tắt toàn cục để mở Shelf (tuỳ chọn, mặc định TẮT)**: chọn tổ hợp phím trong Cài đặt (mặc định Ctrl+Shift+Space). Chỉ nhận đúng tổ hợp bạn chọn.
- **Sửa lỗi & hiệu năng**:
  - Va chạm với tệp đã có sẵn trong thư mục (không thuộc lô) giờ được phát hiện trước khi áp dụng, thay vì lỗi giữa chừng.
  - Bảng xem trước cập nhật lại khi bạn sửa tham số của quy tắc vừa thêm.
  - Tìm và thay bằng Regex nhanh hơn rất nhiều với lô lớn (mẫu được dựng một lần cho cả lô thay vì từng tệp).
  - Chọn **Hủy** ở hộp xác nhận áp dụng không còn đổi tên tệp.
