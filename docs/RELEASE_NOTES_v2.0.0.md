# BFR - Batch File Renamer — Release Notes v2.0.0

Tài liệu ghi chú phát hành cho **Microsoft Partner Center**, bản **v2.0.0** (Package Version: `2.0.0.0`).

> **Giá:** Pro tăng từ 3,99 lên **7,99 USD** cho người mua mới. Người đã mua Pro giữ nguyên quyền với mọi tính năng mới, không phải trả thêm.
> Đổi giá add-on `BFR_PRO_LIFETIME` trên Partner Center **cùng ngày** phát hành.

---

## What's New in this Version (English - en-US)

### Release highlights for v2.0.0:
- **From a renamer to a batch file toolkit.** A new **Tools** page brings nine tools that share one safe workflow: preview, confirm, apply, undo.
  - **Organize**: move or copy files into folders built from a pattern (by type, year/month, artist/album, extension…).
  - **Duplicates**: find identical files (and, in Pro, similar images). Extra copies go to quarantine, and at least one copy is always kept. Click a row to choose which copy to keep.
  - **Folder tools**: flatten a folder tree, clean empty folders, wrap each file in its own folder, or build a folder tree from a list.
  - **Dates, attributes and GPS**: set created/modified dates (from EXIF, the file name, a fixed value or a time shift), change attributes, and **remove GPS from JPEG photos without re-encoding them** (free).
  - **Image tools**: convert, resize, auto-rotate from EXIF and compress JPEG. Originals are never overwritten unless you choose to.
  - **ZIP**: compress (one ZIP, one per folder, or split by size or count) and extract in bulk. Extraction blocks unsafe paths and zip bombs.
  - **Checksum**: MD5 / SHA-1 / SHA-256, export `.sha256`, verify downloads.
  - **Analyze**: space by type and folder, largest files, old files and files whose extension does not match their content.
  - **Workflow (Pro)**: chain several steps and undo the whole chain with one click.
- **Send files to a tool from anywhere.** A *Tools home* with a card per tool and a drop zone, a *Send to Tools* button on the rename list, and new Explorer right-click entries (Organize, Find duplicates, Dates / remove GPS, Compress to ZIP, Checksum) that open the tool with the selected files, ready to preview.
- **Safer by design.** "Delete" never erases anything: files move to a **quarantine** and are kept for at least 30 days. Every change is journaled, so everything can be undone from History. System folders (Windows, Program Files…) are never touched.
- **Watch Folder for several folders (Pro).** Each rule watches one folder and runs a preset or a whole workflow on new files.
- **Command line.** `bfr organize`, `dupes`, `flatten`, `zip`, `unzip`, `hash`, `verify`, `analyze`, `image`, `touch`, `strip-gps`, `workflow run`… all with `--dry-run`, `--yes` and `--format json`.
- **Music tags in names.** New tokens `{artist}`, `{album}`, `{title}`, `{track}` for MP3 and FLAC.
- **Security hardening.** Safer pipe and file-list handling, regex time limits, safer preset import, HTTPS-only update feed, protected system zones, shorter-lived activity data with a "clear history now" button.
- **Fixes.** Undoing a rename that only changed letter case (for example `Photo.JPG` to `photo.jpg`) no longer reports "old name already taken".

---

## Những điểm mới trong phiên bản này (Tiếng Việt - vi-VN)

### Điểm nổi bật trong bản v2.0.0:
- **Từ đổi tên hàng loạt thành bộ công cụ xử lý tệp.** Trang **Công cụ** mới có chín công cụ dùng chung một quy trình an toàn: xem trước, xác nhận, áp dụng, hoàn tác.
  - **Sắp xếp vào thư mục**: di chuyển hoặc sao chép tệp vào thư mục dựng theo mẫu (theo loại, năm/tháng, nghệ sĩ/album, đuôi tệp…).
  - **Tệp trùng**: tìm tệp giống hệt (và ảnh gần giống ở bản Pro). Bản thừa vào khu cách ly, luôn giữ lại ít nhất một bản. Bấm vào một dòng để chọn bản giữ lại.
  - **Công cụ thư mục**: làm phẳng cây thư mục, dọn thư mục rỗng, bọc mỗi tệp vào thư mục riêng, tạo cây thư mục từ danh sách.
  - **Ngày giờ, thuộc tính và GPS**: đặt ngày tạo/sửa (theo EXIF, tên tệp, giá trị cố định hoặc lệch giờ), đổi thuộc tính, và **xoá GPS khỏi ảnh JPEG mà không mã hoá lại ảnh** (miễn phí).
  - **Công cụ ảnh**: đổi định dạng, thu nhỏ, xoay đúng chiều theo EXIF, nén JPEG. Không ghi đè bản gốc trừ khi bạn chọn.
  - **ZIP**: nén (một ZIP, mỗi thư mục một ZIP, hoặc chia theo dung lượng/số tệp) và giải nén hàng loạt. Giải nén chặn đường dẫn nguy hiểm và zip bomb.
  - **Checksum**: MD5 / SHA-1 / SHA-256, xuất `.sha256`, kiểm tra tệp tải về.
  - **Phân tích**: dung lượng theo loại và thư mục, tệp lớn nhất, tệp cũ và tệp có đuôi không khớp nội dung.
  - **Workflow (Pro)**: nối nhiều bước và hoàn tác cả chuỗi chỉ một lần bấm.
- **Gửi tệp tới công cụ từ mọi nơi.** Có *Trang chủ công cụ* với thẻ cho từng công cụ và vùng thả tệp, nút *Gửi tới Công cụ* trên danh sách đổi tên, và các mục mới trong menu chuột phải Explorer (Sắp xếp, Tìm tệp trùng, Ngày giờ / xoá GPS, Nén ZIP, Checksum) mở công cụ với các tệp đã chọn, sẵn sàng xem trước.
- **An toàn từ thiết kế.** “Xoá” không bao giờ xoá vĩnh viễn: tệp chuyển vào **khu cách ly** và được giữ ít nhất 30 ngày. Mọi thay đổi đều ghi journal nên hoàn tác được trong Lịch sử. Thư mục hệ thống (Windows, Program Files…) không bao giờ bị đụng tới.
- **Theo dõi nhiều thư mục (Pro).** Mỗi quy tắc theo dõi một thư mục và chạy preset hoặc cả workflow cho tệp mới.
- **Dòng lệnh.** `bfr organize`, `dupes`, `flatten`, `zip`, `unzip`, `hash`, `verify`, `analyze`, `image`, `touch`, `strip-gps`, `workflow run`… đều có `--dry-run`, `--yes`, `--format json`.
- **Tag nhạc vào tên tệp.** Token mới `{artist}`, `{album}`, `{title}`, `{track}` cho MP3 và FLAC.
- **Tăng cường bảo mật.** Xử lý đường ống và danh sách tệp an toàn hơn, giới hạn thời gian regex, nhập preset an toàn hơn, nguồn cập nhật chỉ qua HTTPS, vùng hệ thống được bảo vệ, dữ liệu hoạt động giữ ngắn hơn kèm nút “Xoá lịch sử ngay”.
- **Sửa lỗi.** Hoàn tác lần đổi tên chỉ khác hoa/thường (ví dụ `Photo.JPG` thành `photo.jpg`) không còn báo “Tên cũ đã bị chiếm”.
