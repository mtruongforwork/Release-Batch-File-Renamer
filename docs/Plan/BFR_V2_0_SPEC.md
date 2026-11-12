# Đặc tả v2.0 — Từ "đổi tên hàng loạt" thành "bộ công cụ xử lý tệp hàng loạt"

> Lập ngày: 12/11/2026 · Nền: v1.4.0 (nhánh `feat/v1.4-ui`)
> Thay thế và bao gồm toàn bộ [BFR_V1_5_PLAN.md](BFR_V1_5_PLAN.md) (bảo mật C1–C8, frontend A8–A14, tính năng B5–B11).
> Trạng thái: **đã chốt (12/11/2026)**. Phát hành một lần duy nhất (v2.0 gồm toàn bộ), giữ tên app, Pro tăng từ 3,99 lên 7,99 USD. Chi tiết ở mục 13.

---

## 1. Tầm nhìn và định vị

### 1.1 Vấn đề
Hiện app chỉ đổi tên. Người dùng có cùng một nhu cầu ("dọn một đống tệp") nhưng phải dùng 4–5 công cụ khác nhau: PowerRename để đổi tên, Explorer để chuyển thư mục, một app tìm tệp trùng, một app nén hoặc đổi định dạng ảnh, một app sửa ngày tạo tệp. Không công cụ nào có **xem trước toàn bộ** và **hoàn tác an toàn**.

### 1.2 Định vị mới
> **BFR: xử lý hàng loạt tệp trên Windows. Xem trước mọi thay đổi, hoàn tác mọi thao tác, hoàn toàn trên máy.**

Lợi thế cạnh tranh giữ nguyên và mở rộng sang mọi thao tác:
1. **Preview trước khi làm** cho mọi công cụ, không riêng đổi tên.
2. **Journal và Undo** cho mọi thao tác có thể đảo ngược.
3. **Một Shelf, nhiều công cụ:** gom tệp một lần rồi chạy liên tiếp nhiều công cụ.
4. **Không mạng, không telemetry.**

### 1.3 Đối tượng
| Nhóm | Nhu cầu chính | Module phục vụ |
|---|---|---|
| Nhiếp ảnh, người dùng điện thoại | Sắp xếp ảnh theo ngày, xoá trùng, xoá GPS trước khi chia sẻ, đổi HEIC sang JPG | Organize, Duplicates, Image, Metadata |
| Văn phòng, kế toán | Đổi tên theo danh sách Excel, chia vào thư mục theo khách hàng, nén gửi email | Rename, Organize, Archive |
| Người dọn ổ đĩa | Tìm tệp lớn, tệp trùng, thư mục rỗng | Analyze, Duplicates, Folder Tools |
| Người dùng kỹ thuật | CLI, Watch Folder, checksum | CLI, Automation, Checksum |

### 1.4 Ngoài phạm vi v2.0
Không có đồng bộ đám mây, không chỉnh sửa nội dung văn bản, không xử lý video (chuyển mã), không sửa và tách PDF, không có "xoá vĩnh viễn an toàn" (shred, vì không đảm bảo được trên SSD).

---

## 2. Bản đồ module

| # | Module | Mô tả ngắn | Undo | Free / Pro |
|---|---|---|---|---|
| M1 | **Rename** (có sẵn) | Pipeline rule hiện tại cộng B5 (token metadata) và B11 (OCR) | Có | Như v1.4 |
| M2 | **Organize** | Di chuyển hoặc sao chép tệp vào thư mục dựng theo mẫu token (`{year}\{month}`, `{ext}`, `{artist}`…) | Có | Free: 2 cấp thư mục, chỉ token cơ bản. Pro: không giới hạn |
| M3 | **Duplicates** | Tìm tệp trùng theo nội dung (hash), chọn bản giữ lại theo quy tắc, đưa bản thừa vào Thùng rác | Có (khôi phục từ khu cách ly) | Free: quét tối đa 1.000 tệp. Pro: không giới hạn, có ảnh gần giống |
| M4 | **Metadata & Timestamps** | Sửa ngày tạo, ngày sửa, thuộc tính tệp; đặt ngày tệp theo EXIF; xoá EXIF/GPS | Có | Free: timestamp, thuộc tính. Pro: ghi và xoá EXIF |
| M5 | **Image Tools** | Đổi định dạng (HEIC/PNG/WEBP/BMP → JPG/PNG), resize, xoay theo EXIF, nén chất lượng | Có (giữ bản gốc) | Free: tối đa 20 ảnh mỗi lô. Pro: không giới hạn |
| M6 | **Archive** | Nén nhiều tệp thành ZIP (một tệp hoặc mỗi thư mục một ZIP), giải nén hàng loạt | Có (xoá tệp sinh ra) | Free: nén và giải nén ZIP. Pro: chia theo nhóm, đặt tên theo mẫu |
| M7 | **Folder Tools** | Làm phẳng thư mục, xoá thư mục rỗng, tạo cây thư mục từ danh sách hoặc từ mẫu | Có | Free |
| M8 | **Analyze** | Thống kê dung lượng theo thư mục, loại tệp, tuổi tệp; tìm tệp lớn; phát hiện đuôi sai (magic bytes) | Không cần (chỉ đọc) | Free: xem. Pro: xuất báo cáo CSV/HTML |
| M9 | **Checksum** | Tính và kiểm tra SHA-256/MD5, tạo và kiểm tra tệp `.sha256` | Không cần (chỉ đọc, hoặc tạo tệp mới) | Free |
| M10 | **Workflow** | Chuỗi nhiều bước từ nhiều module (ví dụ: xoá GPS → đổi tên → sắp vào thư mục → nén), lưu thành preset | Có (Undo cả chuỗi) | Pro |

Thứ tự ưu tiên giá trị cho người dùng: M2 > M3 > M4 > M5 > M10 > M7 > M6 > M8 > M9.

---

## 3. Kiến trúc

### 3.1 Nguyên tắc
1. **Một luồng chung cho mọi công cụ:** `Input (danh sách tệp) → Planner (thuần) → Plan (danh sách Operation) → Preview → Validate (xung đột, quyền, dung lượng) → Executor (ghi Journal) → Undo`.
2. **Planner là code thuần** trong `BFR.Core`, chạy được trên `FakeFileSystem`. Toàn bộ logic test được không cần đĩa thật, giống `RenamePlanner` hiện tại.
3. **Executor duy nhất** thay cho `RenameExecutor`, xử lý mọi loại `Operation`, ghi journal trước khi làm (write-ahead) như hiện nay.
4. **Không bao giờ xoá vĩnh viễn trong luồng thường.** Mọi thao tác "xoá" đều đưa vào khu cách ly của BFR hoặc Thùng rác Windows.

### 3.2 Mô hình Operation (mở rộng journal)

```csharp
// BFR.Core/Operations/Operation.cs
public abstract record Operation(string Id);

public sealed record MoveOp(string Id, string Source, string Target) : Operation(Id);          // gồm cả đổi tên
public sealed record CopyOp(string Id, string Source, string Target) : Operation(Id);          // Undo = xoá Target
public sealed record CreateDirOp(string Id, string Path) : Operation(Id);                       // Undo = xoá nếu rỗng
public sealed record RemoveDirOp(string Id, string Path) : Operation(Id);                       // chỉ thư mục rỗng
public sealed record QuarantineOp(string Id, string Source, string QuarantinePath) : Operation(Id); // "xoá" an toàn
public sealed record SetTimestampsOp(string Id, string Path, FileTimes Before, FileTimes After) : Operation(Id);
public sealed record SetAttributesOp(string Id, string Path, FileAttributes Before, FileAttributes After) : Operation(Id);
public sealed record WriteFileOp(string Id, string Target, string TempSource, string? BackupOfExisting) : Operation(Id);
//   ^ dùng cho Image/Metadata/Archive: kết quả ghi ra tệp tạm, rồi thay nguyên tử (File.Replace) và giữ bản sao lưu.
```

- **Journal v2:** JSON Lines, mỗi dòng là một `Operation` cùng trạng thái (`pending`/`done`/`failed`/`undone`) và trường `schema: 2`. `UndoService` đọc được cả journal v1 (chỉ đổi tên) để không mất lịch sử cũ.
- **Recovery:** `RecoveryService` hiện có mở rộng theo từng loại Operation. Khi khởi động, nếu có journal dở dang thì đề nghị hoàn tất hoặc hoàn tác.
- **Thứ tự Undo:** ngược thứ tự thực thi. `CreateDirOp` được hoàn tác sau cùng.

### 3.3 Khu cách ly (Quarantine) thay cho xoá
- Vị trí: `%LOCALAPPDATA%\BatchFileRenamer\quarantine\<journalId>\`. Nếu tệp nằm khác ổ đĩa (di chuyển sẽ tốn thời gian sao chép), dùng `<ổ đĩa>:\$BFR.Quarantine\` (ẩn) để thao tác vẫn là move.
- Tự dọn sau **30 ngày** (cấu hình được 7/30/90 ngày hoặc "Chuyển vào Thùng rác Windows ngay").
- Trang **Lịch sử** cho phép xem và khôi phục từng tệp, hoặc "Dọn khu cách ly ngay".
- Thùng rác Windows (qua `IFileOperation` với `FOFX_RECYCLEONDELETE`) là phương án thay thế. Undo từ Thùng rác không đảm bảo nên mặc định dùng khu cách ly.

### 3.4 Cấu trúc dự án

| Dự án | Đích | Thay đổi |
|---|---|---|
| `BFR.Core` | net8.0 | Thêm `Operations/`, `Organize/`, `Duplicates/`, `Folders/`, `Checksum/`, `Analyze/`, `Workflow/`. Chỉ code thuần và `IFileSystem` |
| `BFR.Media` (**mới**) | net8.0-windows10.0.19041.0 | Image Tools và ghi EXIF qua **WIC** (`System.Windows.Media.Imaging` hoặc `Windows.Graphics.Imaging`), OCR qua `Windows.Media.Ocr`. Tách ra để Core vẫn thuần |
| `BFR.Archive` (gộp vào Core) | net8.0 | `System.IO.Compression` có sẵn, không thêm thư viện |
| `BFR.Presentation` | net8.0 | ViewModel cho mỗi module, theo khuôn `ToolViewModelBase` chung |
| `BFR.App` | WPF | Trang mới trên NavigationView, `ToolHostPage` dùng chung |
| `BFR.Cli` | | Lệnh con mới cho từng module (mục 8) |
| `BFR.Shell` | | Menu chuột phải thêm nhóm công cụ (mục 7.4) |

**Thư viện:** không thêm thư viện xử lý ảnh bên thứ ba (ImageSharp có giấy phép Split License, cần xem xét khi bán). Dùng WIC có sẵn trong Windows. HEIC, WEBP, AVIF cần extension codec của Windows. Nếu codec chưa cài, hiện hướng dẫn và link Store, không báo lỗi khó hiểu. Hash dùng `System.IO.Hashing` (XxHash3) để lọc nhanh và `SHA256` để xác nhận.

### 3.5 Nền tảng dùng chung
- **`ITool`**: `Id`, `DisplayName`, `RequiredFeature`, `CreatePlannerAsync(options)`, `SupportsUndo`.
- **`ToolRunContext`**: danh sách tệp (từ Shelf, thả vào, hoặc quét thư mục), `CancellationToken`, `IProgress<ToolProgress>`, `LicenseLimits`.
- **Tiến trình và huỷ:** mọi planner và executor báo tiến trình theo lô 100 tệp, huỷ được giữa chừng. Huỷ khi đang Execute thì dừng sau Operation hiện tại, journal vẫn nhất quán.
- **`PathGuard`** (từ C2): mọi đường dẫn đích phải đi qua đây. Chặn `..`, ký tự điều khiển, tên dành riêng (`CON`, `NUL`…), vượt `MAX_PATH` khi chưa bật long path, và đích nằm ngoài thư mục gốc đã chọn.

---

## 4. Đặc tả chức năng từng module

Mỗi module mô tả theo: **Đầu vào → Tuỳ chọn → Xem trước → Xung đột/kiểm tra → Thực thi → Undo → Tiêu chí chấp nhận (AC)**.

### M2. Organize (sắp xếp vào thư mục)
- **Đầu vào:** danh sách tệp và thư mục gốc đích (mặc định là thư mục chứa tệp).
- **Tuỳ chọn:**
  - Mẫu đường dẫn bằng token, dùng lại `TokenEngine`: `{exif.year}\{exif.month:00}`, `{ext.upper}`, `{type}` (Ảnh/Video/Tài liệu/Nhạc/Nén/Khác theo `MediaFileTypes`), `{artist}\{album}`, `{name.first:1}` (chữ cái đầu), `{size.bucket}` (<1MB, 1–100MB, >100MB).
  - Bộ quy tắc có sẵn: "Ảnh theo năm/tháng", "Theo loại tệp", "Nhạc theo nghệ sĩ/album", "Tải xuống theo loại".
  - Chế độ: **Di chuyển** (mặc định) hoặc **Sao chép**.
  - Tệp không có giá trị token (ví dụ ảnh không có EXIF): dùng giá trị dự phòng (ngày sửa tệp) hoặc đưa vào thư mục `_Không xác định`.
  - Có thể kết hợp với Rename: đổi tên và chuyển thư mục trong một lượt.
- **Xem trước:** dạng cây thư mục đích, mỗi nút có số tệp. Có thể chuyển sang dạng bảng (Nguồn → Đích).
- **Xung đột:** trùng tên ở đích được xử lý bằng `ConflictResolver` (thêm " (2)") hoặc bỏ qua tệp đó. Đích là chính nó thì bỏ qua. Cảnh báo khi di chuyển khác ổ đĩa (thành sao chép rồi xoá, chậm hơn).
- **Undo:** đưa tệp về chỗ cũ, xoá thư mục BFR đã tạo nếu rỗng.
- **AC:**
  - 10.000 ảnh, mẫu `{exif.year}\{exif.month}`: xem trước dưới 5 giây (EXIF đã có trong cache), thực thi và hoàn tác trả lại đúng 100% vị trí cũ.
  - Không có thao tác nào ghi ra ngoài thư mục gốc đích (kiểm bằng property test).

### M3. Duplicates (tìm tệp trùng)
- **Đầu vào:** một hoặc nhiều thư mục, có tuỳ chọn đệ quy, lọc theo đuôi và kích thước tối thiểu.
- **Thuật toán 3 tầng** (chạy nền, huỷ được):
  1. Nhóm theo kích thước. Kích thước duy nhất thì loại.
  2. Hash 64 KB đầu và 64 KB cuối bằng XxHash3.
  3. Hash toàn bộ bằng SHA-256 cho nhóm còn lại.
  - Bỏ qua hard link và tệp chỉ có ở đám mây (OneDrive "Files On-Demand", thuộc tính `RECALL_ON_DATA_ACCESS`) để không kích hoạt tải xuống.
- **Ảnh gần giống (Pro):** perceptual hash (dHash 64-bit) qua `BFR.Media`, ngưỡng Hamming chỉnh được (mặc định ≤ 6). Hiển thị cạnh nhau để người dùng xác nhận, **không bao giờ tự chọn xoá**.
- **Quy tắc chọn bản giữ lại:** mới nhất / cũ nhất / đường dẫn ngắn nhất / nằm trong thư mục ưu tiên / tên không chứa "copy", "(1)", "- Copy". Người dùng sửa tay từng nhóm.
- **Hành động với bản thừa:** đưa vào khu cách ly (mặc định), chuyển vào thư mục khác, hoặc thay bằng hard link (Pro, cùng ổ NTFS, có cảnh báo).
- **An toàn:** mỗi nhóm luôn còn ít nhất 1 tệp (bất biến được test). Không cho chọn tệp hệ thống hoặc tệp trong `C:\Windows`, `Program Files`.
- **AC:**
  - 50.000 tệp, 20 GB trên SSD: tầng 1–2 dưới 30 giây.
  - Không có kết quả dương tính giả ở tầng SHA-256 (test có tệp cùng kích thước khác nội dung).
  - Undo khôi phục đủ tệp và đúng timestamp.

### M4. Metadata & Timestamps
- **Timestamp:** đặt ngày tạo, sửa, truy cập theo: giá trị cố định, cộng/trừ khoảng thời gian (sửa lệch múi giờ máy ảnh), lấy từ EXIF `DateTimeOriginal`, lấy từ tên tệp (mẫu `IMG_20240512_103000`), hoặc sao chép từ tệp khác.
- **Thuộc tính:** chỉ đọc, ẩn, lưu trữ (archive). Không cho đặt thuộc tính hệ thống.
- **EXIF (Pro, qua `BFR.Media`):**
  - **Xoá GPS** (Free được dùng riêng tính năng này vì là quyền riêng tư), xoá toàn bộ metadata, giữ lại hướng xoay và ngày chụp.
  - Ghi `DateTimeOriginal`, tác giả, bản quyền.
  - Ghi lại bằng WIC theo cách không mã hoá lại ảnh (lossless metadata write). Nếu định dạng không hỗ trợ, báo trước khi chạy.
- **Xem trước:** bảng Trước → Sau theo từng trường.
- **Undo:** timestamp và thuộc tính lưu giá trị cũ trong journal. Ghi EXIF dùng `WriteFileOp` có bản sao lưu.
- **AC:** đặt ngày tạo theo EXIF cho 5.000 ảnh rồi hoàn tác thì timestamp khớp tới từng tick. Ảnh sau khi xoá GPS không còn thẻ GPS nào (kiểm bằng `MetadataExtractor`).

### M5. Image Tools
- **Thao tác:** đổi định dạng; resize (cạnh dài tối đa, phần trăm, hoặc khung cố định giữ tỉ lệ); xoay theo EXIF Orientation rồi đặt lại về 1; nén JPEG (chất lượng 60–95); chuyển màu về sRGB.
- **Đầu ra:** cạnh tệp gốc với hậu tố hoặc mẫu tên (dùng lại Rename pipeline), hoặc vào thư mục khác. **Mặc định không ghi đè bản gốc.** Ghi đè phải bật rõ ràng, và khi đó bản gốc vào khu cách ly.
- **Hiệu năng:** xử lý song song tối đa `ProcessorCount - 1`, giới hạn bộ nhớ (ảnh > 100 MP xử lý tuần tự).
- **Xem trước:** ảnh mẫu đầu tiên trước/sau, kích thước dự kiến, tổng dung lượng tiết kiệm (ước lượng từ 5 ảnh mẫu).
- **AC:** 1.000 ảnh JPEG 12 MP resize về cạnh dài 2048 dưới 60 giây trên máy 8 nhân. Giữ ngày chụp và hướng xoay đúng. Undo xoá đúng các tệp sinh ra.

### M6. Archive
- **Nén:** tất cả thành một ZIP; mỗi thư mục con một ZIP; hoặc mỗi nhóm N tệp/N MB một ZIP (để gửi email). Tên ZIP theo mẫu token. Mức nén: Nhanh/Cân bằng/Nhỏ nhất.
- **Giải nén hàng loạt:** mỗi ZIP vào thư mục cùng tên, hoặc tất cả vào một thư mục.
  - **Bảo mật:** chặn Zip Slip (mục nhập có `..` hoặc đường dẫn tuyệt đối), giới hạn tỉ lệ nén để chống zip bomb (mặc định 1:100 và tổng 10 GB), bỏ qua symlink.
- Chỉ ZIP trong v2.0 (có sẵn trong .NET). 7z/RAR để sau, cần thư viện ngoài.
- **AC:** tệp ZIP độc (test có sẵn mẫu Zip Slip và zip bomb) bị từ chối với thông báo rõ. Nén rồi giải nén cho ra tệp giống hệt (so SHA-256).

### M7. Folder Tools
- **Làm phẳng:** đưa mọi tệp trong cây thư mục lên một cấp, xử lý trùng tên, tuỳ chọn ghép đường dẫn vào tên (`Album_2024_IMG001.jpg`).
- **Xoá thư mục rỗng:** đệ quy, coi thư mục chỉ chứa `Thumbs.db`/`desktop.ini` là rỗng (tuỳ chọn). Đưa vào khu cách ly.
- **Tạo cây thư mục:** từ danh sách dán vào (thụt lề hoặc đường dẫn), hoặc từ mẫu có sẵn (năm/12 tháng, dự án).
- **Bọc vào thư mục:** mỗi tệp vào một thư mục cùng tên, hoặc nhóm tệp cùng tên khác đuôi (`IMG_001.jpg` + `IMG_001.CR2`) vào chung thư mục.
- **AC:** làm phẳng rồi hoàn tác trả lại đúng cây ban đầu, kể cả thư mục rỗng.

### M8. Analyze
- Quét thư mục ở nền, hiển thị: treemap hoặc thanh theo thư mục, phân bố theo loại tệp, top 100 tệp lớn nhất, tệp cũ hơn N năm.
- **Đuôi sai:** đọc magic bytes (JPEG `FF D8 FF`, PNG, PDF `%PDF`, ZIP `PK`, MP4 `ftyp`…), liệt kê tệp có đuôi không khớp nội dung. Nút "Sửa đuôi" chuyển sang module Rename với rule đổi đuôi đã điền sẵn.
- Mọi kết quả đều có thể **"Gửi tới Shelf"** để xử lý bằng module khác.
- **AC:** quét 200.000 tệp không làm treo giao diện, bộ nhớ dưới 500 MB.

### M9. Checksum
- Tính MD5/SHA-1/SHA-256 cho danh sách tệp, sao chép kết quả, xuất tệp `.sha256` chuẩn (tương thích `sha256sum`).
- Kiểm tra: mở tệp `.sha256` hoặc `.md5`, báo Khớp/Sai/Thiếu.
- **AC:** kết quả khớp với `Get-FileHash` trên bộ tệp mẫu.

### M10. Workflow (Pro)
- Chuỗi các bước từ M1–M7. Đầu ra của bước trước là đầu vào của bước sau, tính trên **kế hoạch ảo** (planner chạy trên trạng thái mô phỏng, chưa chạm đĩa).
- Xem trước tổng hợp tất cả các bước. Thực thi ghi **một journal duy nhất**, nên Undo hoàn tác cả chuỗi.
- Lưu thành preset. Gắn được vào **Watch Folder** (B6) và **menu chuột phải**.
- Mẫu có sẵn: "Chuẩn bị ảnh để chia sẻ" (xoá GPS → resize 2048 → đổi tên `{date}_{n}` → nén ZIP), "Dọn thư mục Tải xuống" (sắp theo loại → xoá thư mục rỗng).
- **AC:** workflow 4 bước trên 500 tệp; hoàn tác một lần trả lại đúng trạng thái ban đầu (so sánh danh sách và hash).

---

## 5. Giao diện

### 5.1 Điều hướng
Rail trái nhóm lại theo chức năng:

```
[Trang chủ]
── Công cụ ──
  Đổi tên
  Sắp xếp
  Tệp trùng
  Ngày & Metadata
  Ảnh
  Nén
  Thư mục
  Phân tích
  Checksum
  Workflow (Pro)
── Chung ──
  Shelf
  Lịch sử
  Preset
  Watch Folder
  Cài đặt
```

- Rail thu gọn chỉ hiện icon khi cửa sổ hẹp hơn 1000 px.
- Command Palette hiện có (`CommandPaletteWindow`) tìm được mọi công cụ và preset.

### 5.2 Trang chủ (mới)
- Vùng thả tệp lớn. Thả vào xong, gợi ý công cụ phù hợp theo loại tệp (toàn ảnh thì ưu tiên Ảnh, Sắp xếp, Tệp trùng).
- Thư mục gần đây, workflow ghim, thao tác gần nhất kèm nút Hoàn tác.

### 5.3 Khung trang công cụ dùng chung (`ToolHostPage`)
Mọi module dùng cùng bố cục để người dùng học một lần:

```
┌──────────────────────────────────────────────────────────────┐
│ Tiêu đề công cụ                          [Preset ▾] [Lưu]    │
├───────────────┬──────────────────────────────────────────────┤
│ Tệp đầu vào   │ Tuỳ chọn của công cụ                          │
│ (danh sách,   ├──────────────────────────────────────────────┤
│  lọc, thêm)   │ Xem trước (bảng / cây / ảnh trước-sau)        │
│               │                                              │
├───────────────┴──────────────────────────────────────────────┤
│ InfoBar: 3 xung đột · 2 cảnh báo     [Tự sửa]                 │
│ 1.240 tệp · ước tính 2,3 GB         [Huỷ]  [Áp dụng]          │
└──────────────────────────────────────────────────────────────┘
```

- Xác nhận áp dụng bằng `ContentDialog` có tóm tắt (số tệp di chuyển, sao chép, cách ly, dung lượng).
- Khi chạy: thanh tiến trình trong trang, nút Huỷ, ước tính thời gian còn lại. Có thể chuyển trang khác trong lúc chạy; rail hiện huy hiệu "đang chạy".
- Xong: `Snackbar` kèm nút **Hoàn tác** và **Mở thư mục**.

### 5.4 Shelf thành trung tâm
- Từ Shelf có nút "Xử lý bằng…" mở danh sách công cụ.
- Kết quả của mọi công cụ (tệp trùng, tệp lớn, tệp đuôi sai) đều "Gửi tới Shelf" được.

### 5.5 Yêu cầu chung
Kế thừa A8–A14: bàn phím đầy đủ, Narrator, High Contrast, 4 ngôn ngữ (en/fr/vi/zh-Hans), sáng/tối, ảo hoá danh sách 100.000 dòng, không cắt chữ.

---

## 6. Free / Pro

### 6.1 Bảng giới hạn mới (`LicenseLimits`)
| Giới hạn | Free | Pro |
|---|---|---|
| Organize: số cấp thư mục trong mẫu | 2 | Không giới hạn |
| Duplicates: số tệp mỗi lần quét | 1.000 | Không giới hạn |
| Duplicates: ảnh gần giống, thay bằng hard link | Không | Có |
| Image Tools: số ảnh mỗi lô | 20 | Không giới hạn |
| Metadata: ghi và xoá EXIF | Chỉ **xoá GPS** | Đầy đủ |
| Archive: chia nhóm, tên theo mẫu | Không | Có |
| Analyze: xuất báo cáo | Không | Có |
| Workflow | Không | Có |
| Giữ khu cách ly | 30 ngày (cố định) | Tuỳ chỉnh tới 90 ngày |
| Các giới hạn cũ (rule, preset, undo, Shelf, danh sách) | Giữ như v1.4 | Giữ như v1.4 |

`ProFeature` thêm: `OrganizeDeepTemplate`, `DuplicatesUnlimited`, `SimilarImages`, `HardLinkDedupe`, `ImageUnlimited`, `ExifWrite`, `ArchiveAdvanced`, `AnalyzeExport`, `Workflow`, `QuarantineRetention`.

### 6.2 Nguyên tắc
- Free phải **dùng được thật** cho mỗi module, không chỉ là bản demo.
- Tính năng bảo vệ quyền riêng tư (xoá GPS) luôn Free.
- Khi chạm giới hạn: hiện rõ "Đã xử lý 20/150 ảnh, nâng cấp để xử lý hết", không âm thầm cắt bớt.
- **Giá:** Pro tăng từ **3,99 lên 7,99 USD** cho người mua mới, áp dụng cùng ngày phát hành v2.0 (đổi giá add-on `BFR_PRO_LIFETIME` trên Partner Center, dùng giá theo sức mua khu vực). Người đã mua Pro giữ nguyên quyền với mọi tính năng mới, không phải trả thêm. Dùng thử Pro 30 ngày giữ như v1.4.
- Giao diện không ghi giá cứng: `UpgradeView` lấy giá từ `StorePriceProvider`, nên chỉ cần cập nhật chuỗi giá dự phòng (fallback) và tài liệu.

---

## 7. Tự động hoá và tích hợp

### 7.1 CLI (`bfr.exe`)
```
bfr organize <dir> --template "{exif.year}\{exif.month:00}" [--copy] [--dry-run]
bfr dupes <dir>... [--min-size 1MB] [--keep newest|oldest|shortest] [--action quarantine|move:<dir>|report]
bfr meta <files> --set-created-from exif | --shift +2h | --strip-gps | --strip-all
bfr image <files> --to jpg --max 2048 --quality 85 [--out <dir>]
bfr zip <files> --out <name.zip> | --per-folder ; bfr unzip <files> [--each-to-folder]
bfr folder flatten|prune-empty|wrap <dir>
bfr analyze <dir> [--top 100] [--wrong-ext] [--report out.html]
bfr hash <files> [--algo sha256] [--verify file.sha256]
bfr workflow run <name> <files>
bfr undo [--journal <id>]
```
- Mọi lệnh có `--dry-run` (in kế hoạch) và `--json` (đầu ra máy đọc được).
- Mã thoát: 0 thành công, 1 có lỗi một phần, 2 tham số sai, 3 cần Pro, 4 lỗi hệ thống (giữ quy ước hiện tại).
- Sửa `appVersion = "1.2.0"` cứng trong `BFR.Cli/Program.cs` thành `AppVersion.Current`.

### 7.2 Watch Folder
Từ B6 (nhiều thư mục), mỗi thư mục gắn **một Workflow** thay vì chỉ preset đổi tên. Ví dụ: thư mục Tải xuống tự sắp xếp theo loại.

### 7.3 Workflow trong Watch Folder
Mọi lượt tự động đều ghi journal và hiện trong Lịch sử, có thể hoàn tác như thao tác tay.

### 7.4 Menu chuột phải Explorer
Nhóm "Batch File Renamer ▸":
- Mở trong BFR
- Đổi tên nhanh ▸ (5 preset hiện có)
- **Sắp xếp theo ngày**, **Tìm tệp trùng tại đây**, **Xoá GPS**, **Đổi sang JPG**, **Nén thành ZIP**, **Tính SHA-256**
- Workflow đã ghim ▸

Giữ quy tắc tối đa 16 mục con (giới hạn thực tế của Explorer). Thao tác nhanh vẫn dùng toast có nút Hoàn tác như `--quick` hiện nay.

---

## 8. Yêu cầu phi chức năng

| Hạng mục | Yêu cầu |
|---|---|
| Hiệu năng | Giao diện không bao giờ chặn quá 100 ms; mọi quét và thực thi chạy nền. Chỉ tiêu cụ thể theo từng module (mục 4), đưa vào PerfGate |
| Bộ nhớ | Dưới 500 MB với 200.000 tệp; ảnh xử lý theo luồng, không nạp cả lô |
| Độ tin cậy | Mất điện giữa chừng: khởi động lại thì khôi phục được từ journal (test bằng cách giết tiến trình ngẫu nhiên khi đang chạy) |
| Dung lượng gói | MSIX không vượt quá 2 lần v1.4. Không kèm codec hay mô hình OCR riêng |
| Tương thích | Windows 10 19041+ và Windows 11; đường dẫn dài; ổ mạng (cảnh báo chậm); OneDrive (không kích hoạt tải xuống khi quét) |
| Quyền riêng tư | Không mạng ngoài kiểm tra cập nhật (đã tắt mặc định ở bản Store); không telemetry; không đọc nội dung tệp ngoài mục đích công cụ đang chạy |
| Bản địa hoá | 4 ngôn ngữ cho toàn bộ chuỗi mới; định dạng số, ngày theo văn hoá |

---

## 9. Bảo mật (gộp từ kế hoạch v1.5 và bổ sung)

| # | Hạng mục | Nội dung |
|---|---|---|
| C1 | Named pipe theo người dùng | `PipeOptions.CurrentUserOnly` trong `SingleInstanceIpc` |
| C2 | `PathGuard` | Kiểm tra mọi đường dẫn từ IPC, dòng lệnh, `--file-list`, mẫu Organize, mục ZIP. `--quick` chỉ nhận preset trong danh sách trắng |
| C3 | Regex timeout | Thống nhất cho mọi chỗ dùng Regex |
| C4 | Nhập preset và workflow | Giới hạn kích thước và độ sâu JSON, kiểm tra schema. Workflow nhập từ ngoài **không được** chứa bước ghi ra đường dẫn tuyệt đối mà không hỏi người dùng |
| C5 | Cập nhật | Chỉ HTTPS và host cho phép, giới hạn phản hồi, User-Agent theo `AppVersion` |
| C7 | Chuỗi cung ứng | CodeQL, Dependabot, kiểm tra gói có lỗ hổng, `TreatWarningsAsErrors`, ký số exe/MSIX, SBOM |
| C8 | Dữ liệu cục bộ | Thời hạn giữ lịch sử/journal chỉnh được (7/30/90/365 ngày, mặc định 30); xoá journal của mục lịch sử bị loại và journal mồ côi (journal dở dang luôn được giữ để khôi phục); cắt nhật ký Watch Folder theo thời hạn; nút "Xoá lịch sử và dữ liệu hoàn tác ngay" (giữ preset, cài đặt, giấy phép); `LogRedactor` che đường dẫn trong nhật ký lỗi và log shell. Khu cách ly (Giai đoạn 0, phần sau) có cơ chế dọn riêng vì chứa tệp của người dùng |
| C9 | **Archive** | Chống Zip Slip, zip bomb, bỏ qua symlink và mục nhập có đường dẫn tuyệt đối |
| C10 | **Vùng cấm** | `ProtectedZones` chặn mọi thao tác ghi trong `C:\Windows`, `Program Files` (cả x86 và Common), `%LOCALAPPDATA%\Packages`, dữ liệu nội bộ của BFR, `$Recycle.Bin`, `System Volume Information`. Trạng thái xem trước `Protected` (chặn áp dụng) và lớp bảo vệ thứ hai trong executor. Quét đệ quy không đi vào symlink/junction và vùng cấm. *Ghi chú: tệp ở gốc ổ đĩa (ví dụ USB) vẫn được xử lý bình thường.* |
| C11 | **Ảnh đầu vào độc** | Giải mã ảnh qua WIC trong khối try riêng có giới hạn kích thước (≤ 500 MP) để tệp hỏng không làm sập app |
| C12 | **Hard link** | Chỉ trên cùng ổ NTFS, cảnh báo rằng sửa một bản sẽ đổi cả bản còn lại |

*(C6 ký số license không làm: license chỉ bán qua Microsoft Store.)*

---

## 10. Kiểm thử

### 10.1 Tự động
| Lớp | Bộ test mới |
|---|---|
| Core | `OperationJournalTests` (đọc journal v1 và v2), `ExecutorTests` mọi loại Operation, `OrganizePlannerTests`, `DuplicateFinderTests`, `TimestampPlannerTests`, `FolderToolsTests`, `ZipSafetyTests` (Zip Slip, bomb), `ChecksumTests` (so với vector chuẩn), `MagicBytesTests`, `WorkflowPlannerTests`, `PathGuardTests`, `QuarantineTests` |
| Property (FsCheck) | Plan → Execute → Undo luôn trả về đúng trạng thái ban đầu trên `FakeFileSystem`, cho **mọi** module. Duplicates: mỗi nhóm luôn còn ≥ 1 tệp. Organize: không ghi ra ngoài thư mục gốc |
| Crash test | Giết executor ở Operation ngẫu nhiên; `RecoveryService` phải đưa về trạng thái nhất quán |
| Media | Bộ ảnh mẫu (JPEG có GPS, PNG, HEIC nếu có codec, ảnh hỏng, ảnh 200 MP) |
| Presentation | ViewModel từng module, giới hạn Free/Pro, trạng thái tiến trình và huỷ |
| PerfGate | Chỉ tiêu ở mục 4 cho M2, M3, M5, M8 |
| Shell smoke | Số mục và tiêu đề menu mới |

### 10.2 Giao diện và gói
- UI Automation cho luồng chính của mỗi module (thêm tệp → tuỳ chọn → xem trước → áp dụng → hoàn tác), chụp 4 ngôn ngữ × 2 theme.
- MSIX: cài, WACK, menu Explorer, Watch Folder, gỡ sạch (kể cả khu cách ly theo lựa chọn người dùng).
- **Quy tắc test với dữ liệu thật:** sao lưu và khôi phục AppData của BFR (settings, history, journal, quarantine); không chạy apply khi đang ghi đè tier Free.

### 10.3 Tiêu chí phát hành
- Mọi test xanh, Release build 0 warning, CodeQL không có cảnh báo mức cao.
- Mỗi module đạt toàn bộ AC ở mục 4.
- Tài liệu: release notes, privacy policy (EXIF, OCR, khu cách ly), Store listing và ảnh chụp mới.

---

## 11. Lộ trình

**Phát hành một lần:** v2.0 lên Store khi xong cả 6 giai đoạn. Mỗi giai đoạn vẫn kết thúc bằng bản build xanh, chạy được và được merge vào nhánh `feat/v2.0`. Để giảm rủi ro của một bản lớn, mở **Store package flight (beta kín)** từ cuối giai đoạn 2 và cập nhật flight sau mỗi giai đoạn. Bản công khai vẫn là v1.4.x (chỉ nhận bản vá lỗi nghiêm trọng) cho tới khi v2.0 phát hành.

| Giai đoạn | Nội dung | Ước lượng |
|---|---|---|
| **0. Nền móng** | Bảo mật C1–C5, C8, C10; mô hình `Operation`, Executor chung, journal v2 tương thích v1; khu cách ly; `PathGuard`; `ITool` và `ToolHostPage`; điều hướng mới và Trang chủ | 3 tuần |
| **1. Tổ chức tệp** | M2 Organize, M7 Folder Tools, B5 token metadata | 3 tuần |
| **2. Dọn dẹp** | M3 Duplicates (chưa có ảnh gần giống), M8 Analyze, M9 Checksum | 3 tuần |
| **3. Media** | Dự án `BFR.Media`; M4 Metadata & Timestamps, M5 Image Tools, ảnh gần giống | 4 tuần |
| **4. Tự động hoá** | M6 Archive, M10 Workflow, Watch Folder nhiều thư mục và workflow (B6), Shelf thành dự án (B7), CLI đầy đủ, menu Explorer mới | 3 tuần |
| **5. Hoàn thiện** | Frontend A8–A14, B8–B10 (preset chia sẻ, dry-run undo, radial), OCR (B11, được phép cắt), C7, bản địa hoá, tài liệu, gói Store | 3 tuần |

Tổng ước lượng khoảng **19 tuần** cho một người làm toàn thời gian, cộng khoảng 1 tuần cho beta và duyệt Store.

**Cắt giảm khi trễ hạn** (vẫn giữ một lần phát hành): cắt theo thứ tự OCR (B11) → Radial (B10) → ảnh gần giống → Analyze xuất báo cáo → Archive chia nhóm. Không cắt nền móng, bảo mật và M2, M3, M4.

---

## 12. Rủi ro

| Rủi ro | Mức | Giảm thiểu |
|---|---|---|
| Phạm vi quá lớn, kéo dài (phát hành một lần nên rủi ro dồn về cuối) | Cao | Mỗi giai đoạn có build xanh; beta kín qua Store flight từ giai đoạn 2; danh sách cắt giảm định sẵn (mục 11) |
| Người dùng phản ứng với giá mới 7,99 USD | Trung bình | Người đã mua giữ quyền; Free vẫn dùng được thật cho mọi module; trang nâng cấp liệt kê rõ các công cụ mới |
| Xoá nhầm dữ liệu người dùng (Duplicates, Folder Tools) | Cao | Không xoá vĩnh viễn; khu cách ly; bất biến "giữ ≥ 1 tệp" được property test; vùng cấm C10 |
| Đổi journal phá hỏng Undo của bản cũ | Trung bình | Journal v2 đọc được v1; test bằng journal thật từ v1.4 |
| Codec HEIC/WEBP không có sẵn | Trung bình | Phát hiện và hướng dẫn cài từ Store; không đóng gói codec |
| App bị coi là "phình to", mất đơn giản | Trung bình | Trang chủ và đổi tên vẫn là mặc định; các công cụ có thể ẩn bớt trong Cài đặt |
| Tên "Batch File Renamer" không còn mô tả đúng | Trung bình | Xem mục 13 |
| Hiệu năng quét trên ổ mạng, OneDrive | Trung bình | Không kích hoạt tải xuống; cảnh báo ổ mạng; huỷ được |
| Store từ chối do thay đổi mô tả lớn | Thấp | Cập nhật listing, privacy policy, ảnh chụp đầy đủ trước khi nộp |

---

## 13. Quyết định

| # | Câu hỏi | Quyết định | Trạng thái |
|---|---|---|---|
| D1 | Số phiên bản | **v2.0** (thay đổi định vị, journal v2) | Đã chốt |
| D2 | Tên app trên Store | **Giữ nguyên "Batch File Renamer"**. Chỉ cập nhật mô tả, từ khoá và ảnh chụp trên Store listing để thể hiện các công cụ mới | Đã chốt |
| D3 | Giá Pro | **7,99 USD** (từ 3,99) cho người mua mới, từ ngày phát hành v2.0; người đã mua giữ nguyên quyền | Đã chốt |
| D4 | Phát hành một lần hay chia nhỏ | **Một lần:** v2.0 gồm toàn bộ 6 giai đoạn; beta kín qua Store flight | Đã chốt |
| D5 | Xoá mặc định | Khu cách ly của BFR (Undo chắc chắn), không dùng Thùng rác Windows | Đề xuất, mặc định áp dụng |
| D6 | Mức Free của từng module | Theo bảng 6.1 | Đề xuất, mặc định áp dụng |
| D7 | OCR | Giữ, dùng `Windows.Media.Ocr`, đặt cuối và được phép cắt | Đề xuất, mặc định áp dụng |

### 13.1 Việc phát sinh từ quyết định
- Cập nhật [specs/12-thuong-mai-hoa-gtm.md](../specs/12-thuong-mai-hoa-gtm.md): giá 7,99 USD và bảng dự phóng doanh thu.
- Cập nhật `docs/STORE_LISTING.md`: mô tả, từ khoá (organize, duplicate finder, HEIC to JPG, remove GPS, bulk zip), ảnh chụp các công cụ mới; tên giữ nguyên.
- Đổi giá add-on trên Partner Center đúng ngày phát hành, sau khi bản v2.0 đã qua duyệt.
- Cập nhật chuỗi giá dự phòng trong giao diện nâng cấp (4 ngôn ngữ) và ảnh chụp `docs/ui-upgrade/`.
- Release notes v2.0 nêu rõ: "Người đã mua Pro nhận toàn bộ tính năng mới miễn phí".
