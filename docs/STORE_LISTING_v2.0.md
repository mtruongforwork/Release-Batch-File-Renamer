# Nội dung Store cho v2.0.0 (dán vào Partner Center)

Thay thế mục 2 (mô tả), mục 5 (ảnh) và mục 6 (add-on) của `STORE_LISTING.md` cho bản 2.0.0. Các mục còn lại (Notes for Certification, privacy) giữ nguyên, nhưng xem phần "Cập nhật Notes for Certification" ở cuối.

- Gói: `artifacts/BatchFileRenamer-2.0.0.msix` (Package Identity Name `MTDStudio.BFR-BatchFileRenamer`, Version `2.0.0.0`).
- What's new: dán từ `docs/RELEASE_NOTES_v2.0.0.md` (en-US và vi-VN).
- Ảnh chụp màn hình: `docs/store-assets/v2.0/` (7 ảnh PNG 1600×900, tệp mẫu giả lập, không có dữ liệu cá nhân). Thứ tự đề xuất: 01 → 07.

---

## Tiếng Việt (vi-VN)

**Tên:** BFR - Batch File Renamer

**Mô tả ngắn (tối đa 200 ký tự):**
Đổi tên hàng loạt có xem trước, cộng bộ công cụ xử lý tệp: sắp xếp, tìm tệp trùng, ZIP, checksum, phân tích dung lượng. Mọi thay đổi đều hoàn tác được. Xử lý 100% trên máy.

**Mô tả chi tiết:**

BFR đổi tên hàng trăm, hàng nghìn tệp trong vài giây và nay là cả bộ công cụ xử lý tệp hàng loạt. Mọi thao tác đi theo một quy trình an toàn: xem trước, xác nhận, áp dụng, hoàn tác.

ĐỔI TÊN HÀNG LOẠT
- Xem trước thời gian thực: thấy tên mới trước khi áp dụng, phân biệt tệp đổi, không đổi và xung đột.
- Chuỗi quy tắc trực quan: thêm, kéo-thả sắp xếp, chỉnh tham số ngay trên màn hình chính.
- Phát hiện xung đột: trùng tên, tên dành riêng của Windows, đường dẫn quá dài. Một cú bấm tự thêm (2), (3)…
- Nhật ký ghi trước: mất điện giữa chừng vẫn hoàn tất hoặc khôi phục được khi mở lại.
- Hoàn tác bất cứ lúc nào từ Lịch sử.
- Trình thử Regex, đổi tên theo danh sách Excel/CSV, thẻ nhạc {artist}, {album}, {title}, {track}.

BỘ CÔNG CỤ MỚI TRONG 2.0
- Sắp xếp vào thư mục: theo loại, năm/tháng, nghệ sĩ/album, đuôi tệp.
- Tìm tệp trùng: bản thừa vào khu cách ly, luôn giữ lại ít nhất một bản.
- Công cụ thư mục: làm phẳng, dọn thư mục rỗng, bọc mỗi tệp vào thư mục riêng.
- Ngày giờ, thuộc tính và GPS: đặt ngày tạo/sửa, và xoá GPS khỏi ảnh JPEG mà không mã hoá lại ảnh.
- Công cụ ảnh: đổi định dạng, thu nhỏ, xoay theo EXIF, nén JPEG.
- ZIP: nén và giải nén hàng loạt, chặn đường dẫn nguy hiểm và zip bomb.
- Checksum: MD5, SHA-1, SHA-256, xuất và kiểm tra tệp .sha256.
- Phân tích thư mục: sơ đồ dung lượng theo loại tệp, tệp lớn nhất, tệp cũ, đuôi tệp sai.
- Workflow (Pro): nối nhiều bước và hoàn tác cả chuỗi một lần bấm.

GIAO DIỆN MỚI
- Trang chủ, thanh điều hướng, thẻ và hiệu ứng mới; sáng/tối; ba bảng màu nhấn (Aurora, Mint, Sunset).
- Menu tròn 8 công cụ mở ngay tại con trỏ (phím tắt tuỳ chọn, mặc định tắt).
- Bảng lệnh Ctrl+K, tiếng Việt, Anh, Pháp, Trung giản thể.

TÍCH HỢP WINDOWS
- Menu chuột phải Explorer: đổi tên nhanh và mở thẳng công cụ với tệp đã chọn.
- Khay tệp (Shelf), theo dõi nhiều thư mục (Pro), công cụ dòng lệnh bfr (Pro).

AN TOÀN VÀ RIÊNG TƯ
- "Xoá" luôn chuyển vào khu cách ly, giữ ít nhất 30 ngày.
- Thư mục hệ thống (Windows, Program Files…) không bao giờ bị đụng tới.
- 100% xử lý cục bộ, không gửi tên tệp hay dữ liệu lên mạng.

BẢN MIỄN PHÍ VÀ PRO
Bản miễn phí dùng đầy đủ quy trình đổi tên và các công cụ cơ bản với giới hạn hợp lý. Pro (mua một lần, trọn đời) mở khoá chuỗi quy tắc không giới hạn, quét đệ quy sâu, EXIF, hoàn tác nhiều bước, báo cáo, Workflow, Watch Folder, CLI và hơn nữa. Dùng thử Pro 30 ngày, không cần thẻ.

**Từ khoá tìm kiếm (tối đa 7):** đổi tên hàng loạt, batch rename, file renamer, tệp trùng, sắp xếp tệp, zip, checksum

---

## English (en-US)

**Title:** BFR - Batch File Renamer

**Short description (max 200 chars):**
Batch rename with live preview, plus a file toolkit: organize, find duplicates, ZIP, checksum, folder analysis. Every change can be undone. 100% processed on your PC.

**Description:**

BFR renames hundreds or thousands of files in seconds, and with 2.0 it is a complete batch file toolkit. Every tool follows one safe workflow: preview, confirm, apply, undo.

BATCH RENAME
- Live preview: see new names before applying, with changed, unchanged and conflicting files clearly marked.
- Visual rule chain: add, drag to reorder and tune parameters right on the main screen.
- Conflict detection: duplicate names, Windows reserved names, long paths. One click adds (2), (3)…
- Write-ahead journal: if the power fails mid-batch, finish or roll back when you reopen the app.
- Undo any time from History.
- Regex tester, rename from an Excel/CSV list, music tags {artist}, {album}, {title}, {track}.

NEW TOOLKIT IN 2.0
- Organize into folders: by type, year/month, artist/album or extension.
- Find duplicates: extra copies go to quarantine, and at least one copy is always kept.
- Folder tools: flatten, clean empty folders, wrap each file in its own folder.
- Dates, attributes and GPS: set created/modified dates, and remove GPS from JPEG photos without re-encoding them.
- Image tools: convert, resize, auto-rotate from EXIF, compress JPEG.
- ZIP: compress and extract in bulk, blocking unsafe paths and zip bombs.
- Checksum: MD5, SHA-1, SHA-256, export and verify .sha256 files.
- Analyze a folder: a size map by file type, largest files, old files, wrong extensions.
- Workflow (Pro): chain several steps and undo the whole chain with one click.

NEW LOOK
- New home page, navigation, cards and motion; light and dark themes; three accent palettes (Aurora, Mint, Sunset).
- A radial menu with 8 tools opens right at the cursor (optional hotkey, off by default).
- Command palette (Ctrl+K). Vietnamese, English, French and Simplified Chinese.

WINDOWS INTEGRATION
- Explorer right-click menu: quick renames and open a tool directly with the selected files.
- Shelf, multi-folder Watch Folder (Pro), and the bfr command-line tool (Pro).

SAFE AND PRIVATE
- "Delete" always moves files to a quarantine kept for at least 30 days.
- System folders (Windows, Program Files…) are never touched.
- 100% local processing. No file names or data leave your PC.

FREE AND PRO
Free includes the full rename workflow and the core tools with sensible limits. Pro (one-time, lifetime purchase) unlocks unlimited rule chains, deep recursion, EXIF, multi-step undo, reports, Workflow, Watch Folder, the CLI and more. Try Pro free for 30 days, no card needed.

**Search terms (max 7):** batch rename, file renamer, rename files, duplicate finder, organize files, zip, checksum

---

## Add-on Pro (tạo/cập nhật `BFR_PRO_LIFETIME`)

| Ô trên Partner Center | Giá trị |
| :--- | :--- |
| Product type | Durable |
| Product ID | `BFR_PRO_LIFETIME` (không đổi được sau khi tạo) |
| Product lifetime | Forever |
| Giá | 7,99 USD cho người mua mới (người đã mua giữ nguyên quyền). Đổi cùng ngày phát hành |
| Tên hiển thị (vi) | Batch File Renamer Pro |
| Tên hiển thị (en) | Batch File Renamer Pro |

**Mô tả (vi):** Mở khoá toàn bộ tính năng nâng cao một lần, trọn đời: chuỗi quy tắc và quét thư mục không giới hạn, ngày EXIF, hoàn tác nhiều bước, báo cáo, Workflow, Watch Folder, công cụ dòng lệnh và tìm ảnh gần giống.

**Mô tả (en):** Unlock every advanced feature with a single, lifetime purchase: unlimited rule chains and folder scanning, EXIF dates, multi-step undo, reports, Workflow, Watch Folder, the command-line tool and similar-image detection.

---

## Cập nhật Notes for Certification (thêm vào cuối khối ở mục 3)

Thêm hai đoạn sau vào ô Notes for Certification:

```text
9. New in v2.0.0: the Explorer submenu now also contains five file-tool entries (Organize, Find duplicates, Dates / remove GPS, Compress to ZIP, Checksum). Each one only launches the packaged app with --tool <name> and the selected paths; the tool shows a preview and nothing is changed until the user confirms. Deleting never erases data: files go to a quarantine folder under LocalAppData and are kept for at least 30 days. System folders are protected and never modified.
10. Optional radial menu hotkey (v2.0.0): when the user turns it on in Settings (OFF by default) the app registers one system-wide key combination with RegisterHotKey, only to show a small menu at the cursor. No keyboard hook is installed.
```
