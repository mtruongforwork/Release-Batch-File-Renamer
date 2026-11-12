# TÀI LIỆU NỘP DUYỆT MICROSOFT STORE / STORE LISTING & CERTIFICATION NOTES

Tài liệu chuẩn bị cho Giai đoạn 2: Phát hành Free lên Microsoft Store theo đặc tả `docs/specs/10-packaging-msix-menu-chu-phai-store-ci-cd.md` và `docs/specs/13-lo-trinh-mvp-ru-ro.md`.

---

## 1. Thông tin chung (Product Details)

- **Tên sản phẩm (Product Name):** Batch File Renamer
- **Tên phụ đề (Subtitle):** Đổi tên tệp hàng loạt an toàn, nhanh chóng & hoàn tác tức thì
- **Subtitle (EN):** Fast, safe batch file renaming with instant preview and undo
- **Thể loại (Category):** Utilities & Tools (Tiện ích & Công cụ)
- **Độ tuổi (Age Rating):** 3+ / Everyone (Không chứa nội dung nhạy cảm)
- **Phiên bản (Version):** 1.4.0.0
- **Giá (Pricing):** Miễn phí (Free)
- **Hệ điều hành hỗ trợ:** Windows 10 (Build 19041+) và Windows 11 (x64)
- **Mã thực thi dòng lệnh (AppExecutionAlias):** `bfr.exe`
- **Kiến trúc đóng gói:** MSIX x64, self-contained .NET 8 (không cần cài .NET runtime)
- **Privacy Policy URL:** **CHỜ CẬP NHẬT** — host `docs/privacy.html` rồi dán URL công khai vào Partner Center
- **Support contact:** **CHỜ CẬP NHẬT** — email/GitHub Issues công khai

---

## 2. Mô tả cửa hàng (Store Description)

### Tiếng Việt (vi-VN)
**Mô tả ngắn (Short Description):**  
Đổi tên hàng loạt tệp và thư mục dễ dàng, an toàn tuyệt đối với bảng xem trước trực quan, giải quyết xung đột thông minh, tích hợp menu chuột phải Explorer và hoàn tác tức thì.

**Mô tả chi tiết (Full Description):**  
Batch File Renamer (BFR) là tiện ích Windows hiện đại giúp bạn đổi tên hàng trăm, hàng nghìn tệp và thư mục chỉ trong vài giây mà không lo mất mát dữ liệu.

Các tính năng nổi bật:
- **Xem trước thời gian thực (Real-time Preview):** Quan sát sự thay đổi của tên mới ngay khi cấu hình quy tắc. Bảng xem trước phân biệt rõ tệp thay đổi, không đổi và tệp bị xung đột.
- **Vết quy tắc (Rule Trace):** Theo dõi từng bước biến đổi của tên tệp qua chuỗi quy tắc để đảm bảo độ chính xác tuyệt đối.
- **Phát hiện xung đột & an toàn tối đa:** Tự động phát hiện trùng tên, va chạm với tệp ngoài lô, tên dành riêng của Windows (CON, PRN, AUX...) và giới hạn độ dài đường dẫn.
- **Nhật ký & Khôi phục sự cố:** Cơ chế ghi trước (Write-Ahead Journal) đảm bảo nếu mất điện hoặc tắt máy đột ngột, bạn có thể hoàn tất hoặc khôi phục về trạng thái ban đầu khi mở lại ứng dụng.
- **Hoàn tác (Undo):** Dễ dàng khôi phục lại tên cũ chỉ với một cú nhấp chuột.
- **Tích hợp Windows File Explorer:** Bấm chuột phải vào bất kỳ tệp hoặc thư mục nào để mở nhanh trong ứng dụng.
- **Watch Folder (Pro):** Tự động đổi tên tệp mới xuất hiện trong thư mục bạn chọn — có nhật ký và hỗ trợ hoàn tác.
- **Trình thử Regex:** Thử mẫu biểu thức ngay trên tên tệp thật, xem rõ các nhóm bắt trước khi áp dụng cho cả lô.
- **Bảng lệnh nhanh (Ctrl+K):** Gõ để tìm và chạy bất kỳ lệnh nào trong ứng dụng.
- **Builder quy tắc trực quan:** Thêm, sắp xếp lại (kéo-thả) và chỉnh tham số tại chỗ, không cần rời khỏi màn hình chính.
- **Hỗ trợ đầy đủ tiếng Việt, tiếng Anh, tiếng Pháp và tiếng Trung (Giản thể):** chuẩn hóa tiếng Việt có dấu/không dấu, bỏ dấu chuẩn xác.
- **Shelf (khay tệp):** Gom tệp và thư mục từ nhiều thư mục khác nhau vào một khay nổi rồi đổi tên cùng lúc (Free: tối đa 50 tệp, Pro: không giới hạn). Mở bằng Ctrl+Shift+S.
- **Đổi tên theo danh sách (Excel/CSV):** Dán hai cột tên cũ → tên mới từ Excel, hoặc dùng từ điển thay thế nhiều cặp một lượt (Free: 50 dòng, Pro: không giới hạn và nhập tệp CSV).
- **Tự xử lý trùng tên:** Một cú bấm thêm " (2)", " (3)"… khi tên mới trùng nhau hoặc trùng tệp có sẵn.
- **Đổi tên nhanh từ menu chuột phải Explorer:** Đánh số, bỏ dấu, thêm ngày, dọn dẹp tên hoặc chữ thường mà không mở cửa sổ; có nút Hoàn tác.
- **Phím tắt toàn cục mở Shelf (tuỳ chọn):** Chọn tổ hợp phím trong Cài đặt. Mặc định tắt.
- **Giao diện điều hướng bên trái:** Mỗi chức năng một trang, hộp thoại ngay trong cửa sổ, thư viện quy tắc dạng thẻ.
- **Lắc chuột để mở Shelf (tuỳ chọn):** Khi đang kéo tệp, lắc chuột để Shelf hiện ngay dưới con trỏ. Mặc định tắt, chỉ xử lý cục bộ.
- **Chạy nền ở khay hệ thống & khởi động cùng Windows (tuỳ chọn):** Giữ BFR sẵn sàng mà không chiếm chỗ trên thanh tác vụ. Mặc định tắt.
- **Giao diện Windows 11 Fluent:** Nền Mica, nút gạt, thẻ và hộp thoại đồng bộ, hỗ trợ giao diện sáng/tối.
- **Bộ công cụ dòng lệnh (CLI, Pro):** Lệnh `bfr` hỗ trợ người dùng nâng cao và tự động hóa qua script.
- **Quyền riêng tư tuyệt đối:** 100% xử lý cục bộ trên thiết bị của bạn, không gửi bất kỳ dữ liệu nào lên mạng.

### English (en-US)
**Short Description:**  
Fast, safe, and intuitive batch file and folder renamer with real-time preview, conflict detection, File Explorer integration, and instant undo.

**Full Description:**  
Batch File Renamer (BFR) is a modern, reliable Windows utility designed to rename hundreds or thousands of files and directories in seconds with guaranteed safety.

Key Features:
- **Real-Time Preview:** Instantly see old and new file names before applying changes.
- **Rule Trace:** Inspect step-by-step transformations of each file through the rule pipeline.
- **Intelligent Conflict Detection:** Proactively prevents file overwrites, case collisions, Windows reserved device names (CON, NUL, AUX...), and path length limits.
- **Crash Recovery & Write-Ahead Journal:** Never lose files during unexpected shutdowns or power outages. Safely complete or roll back interrupted batches.
- **One-Click Undo:** Revert previous rename operations easily and safely.
- **Windows File Explorer Integration:** Select files or folders, right-click, and open directly in Batch File Renamer.
- **Watch Folder (Pro):** Automatically rename new files as they arrive in a folder you pick — with a log and undo support.
- **Regex Tester:** Try a regular expression against real file names and inspect capture groups before applying it to the whole batch.
- **Command Palette (Ctrl+K):** Type to find and run any command in the app.
- **Visual Rule Builder:** Add, reorder (drag and drop) and fine-tune rule parameters in place.
- **Four Languages:** Vietnamese, English, French and Simplified Chinese, including accent-free Vietnamese normalization.
- **Shelf:** Collect files and folders from many different locations in a floating tray and rename them together (Free: up to 50 files, Pro: unlimited). Open it with Ctrl+Shift+S.
- **Rename from a list (Excel/CSV):** Paste two columns (old name → new name) from Excel, or use a replace dictionary of several pairs in one pass (Free: 50 rows, Pro: unlimited plus CSV import).
- **Fix duplicate names automatically:** One click adds " (2)", " (3)"… when new names collide with each other or with an existing file.
- **Quick rename from the Explorer right-click menu:** Number, remove accents, add date, clean up or lowercase without opening a window; with Undo.
- **Global hotkey to open Shelf (optional):** Pick the key combination in Settings. Off by default.
- **Left navigation layout:** One page per feature, dialogs inside the window, and a card-based rule library.
- **Shake to open Shelf (optional):** While dragging files, shake the mouse and the Shelf appears right under the pointer. Off by default, processed locally only.
- **Background mode & start with Windows (optional):** Keep BFR ready from the system tray. Off by default.
- **Windows 11 Fluent look:** Mica backdrop, toggle switches, cards and dialogs, with light and dark themes.
- **Command-Line Interface (Pro):** Seamless `bfr` command for automation and scripting.
- **Accessibility & Privacy First:** Full keyboard navigation, Narrator support, High Contrast compliance, and 100% offline local processing.

---

## 3. Ghi chú thẩm định cho người duyệt Store (Notes for Certification)

> **Mục này cực kỳ quan trọng:** Người duyệt của Microsoft sẽ kiểm tra lý do ứng dụng khai báo `runFullTrust`. Sao chép nguyên văn đoạn sau vào ô **Notes for Certification** trên Partner Center:

```text
Application: Batch File Renamer
Target Framework: .NET 8, self-contained win-x64 (Windows Desktop Bridge)
Capabilities Declared: runFullTrust, appExecutionAlias, windows.fileExplorerContextMenus, windows.startupTask

Justification for runFullTrust:
1. Core Functionality: Batch File Renamer is a file management and renaming utility. It requires full filesystem access to inspect file attributes, read metadata (such as photo EXIF creation dates), plan atomic renaming operations, and execute renaming of files and folders explicitly chosen by the user.
2. File Explorer Context Menu Integration: The package declares a Windows File Explorer context-menu extension for selected files, folders, and the folder background. A small out-of-process COM local server (BFR.Shell.exe) receives the selected IShellItemArray, writes the paths to a temporary file-list, and launches the main app. The COM server is self-contained and does not require a separate .NET runtime installation. The portable ZIP additionally supports the classic HKCU ExplorerCommandHandler registration for Windows 10 and Windows 11 "Show more options".
3. Command-Line Alias (AppExecutionAlias): The package includes a companion command-line tool bfr.exe to allow power users to perform batch renames and scripting directly from Windows Terminal / PowerShell.
4. Security & Safety:
   - The application runs strictly with standard user privileges (requestedExecutionLevel = asInvoker) and never requires or prompts for Administrator elevation.
   - The app does NOT transmit any user data, telemetry, file names, or contents over the network; all operations are strictly 100% local and offline.
   - Temporary file-lists created by the Explorer COM server are stored under %TEMP%\BatchFileRenamer\shell and deleted by the app after it reads them.
   - A Write-Ahead Journal is maintained in LocalAppData to protect user files against data loss or corruption during power loss.
5. Optional "Shake to open Shelf" (v1.3.0): when the user turns this on in Settings (it is OFF by default), the app installs a low-level mouse hook (WH_MOUSE_LL) on a dedicated thread. It only reads the pointer position and the left-button state in memory to recognise a shake gesture while the button is held; it does not read the keyboard, window or screen contents, and nothing is recorded, stored or transmitted. The hook is removed as soon as the option is turned off or the app exits. The privacy policy section 4 describes this.
6. Optional background mode and startup task (v1.3.0): the app can stay in the system tray (OFF by default). The manifest declares a windows.startupTask (TaskId BFRStartup, Enabled=false); it is only enabled if the user turns on "Start with Windows" in Settings, and the user can disable it at any time in Windows Settings > Apps > Startup. The app starts hidden in the tray only when background mode is on.
7. Optional global hotkey (v1.4.0): when the user turns it on in Settings (OFF by default) the app registers one system-wide key combination with the Win32 RegisterHotKey API on a message-only window, only to bring up the Shelf. No keyboard hook is installed and no other keystrokes are received. The combination is unregistered when the option is turned off or the app exits.
8. Explorer submenu (v1.4.0): the Explorer command exposes a submenu ("Open in Batch File Renamer" plus five quick presets). A quick preset launches the packaged app with --quick <preset>, which renames the selected items with a built-in preset and shows a small toast with Undo; all work is local and journaled.

Notes:
- The package submitted to the Store is signed with a trusted code-signing certificate whose Publisher matches the package Identity.
- The modern context menu is declared through desktop4/desktop5 extensions; the folder-background item uses desktop10 and is supported on Windows 11 22H2+.
- The app offers one durable in-app add-on (Product ID BFR_PRO_LIFETIME, lifetime license). The purchase and license check are performed by the operating system through Windows.Services.Store; the app itself has no server and never transmits user file names, paths or contents. A free 30-day Pro trial is available once per installation. The portable ZIP build is Free only.

```

---

## 4. Checklist chứng nhận (Store Certification Checklist)

- [x] **Không yêu cầu quyền Admin:** Application manifest cấu hình `asInvoker`.
- [x] **Khởi chạy bình thường:** Nút "Open" trong Microsoft Store hiển thị cửa sổ chính ngay lập tức (không chạy ngầm/khay hệ thống).
- [x] **Hỗ trợ đường dẫn dài (Long Path):** Khai báo `longPathAware` trong manifest.
- [x] **Nội dung chính sách quyền riêng tư:** `PRIVACY_POLICY.md` và `docs/privacy.html` nêu rõ không thu thập dữ liệu, xử lý 100% cục bộ.
- [ ] **URL privacy policy công khai:** Cần host `docs/privacy.html` (GitHub Pages) và dán URL thật vào Partner Center.
- [x] **Chất lượng hình ảnh/icon:** Đầy đủ các kích thước StoreLogo (50x50), Square44x44, Square150x150, Wide310x150, SplashScreen (620x300).
- [x] **Ảnh chụp màn hình:** `docs/store-assets/screenshot-main.png` (giao diện sáng) và `screenshot-dark.png` (giao diện tối), PNG 1600×900, dùng tệp mẫu giả lập; không có dữ liệu cá nhân thật.
- [x] **Trợ năng (Accessibility - QA-17):** Có High Contrast, phím tắt và `AutomationProperties`; **chưa kiểm thử Narrator/High Contrast thật**.
- [x] **Minh bạch về IAP:** Chính sách quyền riêng tư nêu rõ kết nối mạng duy nhất là kiểm tra/mua add-on Pro qua Microsoft Store.
- [ ] **Chứng chỉ ký:** Gói local hiện chưa ký; cần PFX/EV certificate có Publisher khớp `Identity Publisher`.
- [ ] **Cài MSIX trên máy sạch:** Cần kiểm tra trên Windows 10 19041+ và Windows 11 x64.
- [ ] **Kiểm thử Explorer thật:** Chọn 1/50/5.000 tệp và kiểm tra menu hiện đại/cổ điển.
- [ ] **Partner Center:** Chưa có tài khoản/submission để tải gói và điền listing.
- [ ] **Add-on Pro:** Cần tạo add-on bền vững `BFR_PRO_LIFETIME` trong Partner Center và gắn với ứng dụng.

---

## 5. Bộ nội dung Store đã chuẩn bị

| Hạng mục | Tệp / Giá trị | Trạng thái |
| :--- | :--- | :--- |
| Tên sản phẩm | Batch File Renamer | Sẵn sàng |
| Mô tả ngắn / dài (vi + en) | Mục 2 của tài liệu này | Sẵn sàng |
| Notes for Certification | Mục 3 của tài liệu này | Sẵn sàng |
| Ảnh chính | `docs/store-assets/screenshot-main.png` (1600×900, giao diện sáng) | Sẵn sàng |
| Ảnh giao diện tối | `docs/store-assets/screenshot-dark.png` (1600×900) | Sẵn sàng |
| Ảnh phụ | `docs/store-assets/screenshot-preset-library.png` | Sẵn sàng |
| Ảnh phụ (Pro) | `docs/store-assets/screenshot-watch-folder.png` | Sẵn sàng |
| Logo/icon | `packaging/msix/Assets/*.png` | Sẵn sàng |
| Privacy policy | `docs/privacy.html` | Cần host công khai |
| Gói cài đặt | `artifacts/BatchFileRenamer-1.4.0.msix` (v1.4) | Cần ký bằng chứng chỉ phát hành |

---

## 6. Add-on Pro (In-app purchase)

| Hạng mục | Giá trị |
| :--- | :--- |
| Product ID | `BFR_PRO_LIFETIME` |
| Loại | Durable add-on (mua đứt trọn đời) |
| Giá đề xuất | 7,99 USD cho người mua mới từ v2.0 (người đã mua giữ nguyên quyền; Partner Center tự tính theo sức mua khu vực). Đổi giá add-on `BFR_PRO_LIFETIME` cùng ngày phát hành |
| Dùng thử | 30 ngày, một lần cho mỗi cài đặt (ghi cục bộ) |
| Nâng cấp trong app | Hộp thoại *"Nâng cấp lên Batch File Renamer Pro"* (so sánh Free/Pro, nút Mua Pro, nhập mã kích hoạt) |
| Bản portable (ZIP) | Có thể dùng thử và nhập **mã kích hoạt**; không mua được trong Store |
| Bản portable (ZIP) | **Không** bán Pro (Store IAP không hoạt động ngoài Store) |

**Tính năng Pro (mô tả cho listing add-on):**
- Chuỗi quy tắc không giới hạn (Free: tối đa 3).
- Quét đệ quy không giới hạn cấp (Free: tối đa 2).
- Ngày chụp EXIF và ngày tạo media (video/audio) (Free: chỉ ngày tạo/sửa/hiện tại).
- Hoàn tác nhiều bước, lưu lịch sử 30 ngày (Free: 1 lần gần nhất).
- Lưu preset không giới hạn và nhập/xuất tệp `.bfrpreset` (Free: 3 preset, không nhập/xuất).
- Xuất báo cáo CSV/JSON.
- Công cụ dòng lệnh `bfr` cho tự động hoá.
- Đánh số kiểu La Mã.
- **Shelf không giới hạn số tệp** (Free: tối đa 50 tệp mỗi lần).
- **Danh sách đổi tên không giới hạn dòng và nhập tệp CSV** (Free: tối đa 50 dòng, không nhập CSV).
- **Watch Folder:** tự động đổi tên tệp mới trong thư mục bạn chọn (chờ tệp ghi xong, có nhật ký, chống thao tác lặp, hỗ trợ hoàn tác).

**Tên hiển thị gợi ý:** `Batch File Renamer Pro` — mô tả: “Mở khoá toàn bộ quy tắc nâng cao, EXIF, undo nhiều bước, báo cáo và CLI.”

---

## 7. Hướng dẫn host privacy policy bằng GitHub Pages

1. Đưa repository lên GitHub (nhánh `main`).
2. Vào **Settings → Pages**, chọn **Deploy from a branch**: nhánh `main`, thư mục `/docs`.
3. URL dự kiến: `https://<github-user>.github.io/<repo>/privacy.html`.
4. Kiểm tra URL bằng trình duyệt ẩn danh, sau đó dán vào **Privacy policy URL** trong Partner Center.
5. Cập nhật lại mục 1 của tài liệu này với URL thật (xóa dòng “CHỜ CẬP NHẬT”).

> Không nộp Store với URL placeholder hoặc `file:///...`; Microsoft sẽ từ chối.

---

## 8. Việc cần làm ngay trước khi bấm Submit

1. Tạo/cập nhật chứng chỉ ký và đặt `BFR_PFX_PATH`, `BFR_PFX_PASSWORD` khi chạy `build-msix.ps1`.
2. Xác nhận `Identity Publisher` trong `AppxManifest.xml` khớp chủ thể chứng chỉ.
3. Host privacy policy và cập nhật URL.
4. Cài thử gói đã ký trên Windows 10 và Windows 11 sạch.
5. Chạy checklist Explorer + Narrator/High Contrast trong `docs/PHASE2_QA.md`.
6. Tạo add-on `BFR_PRO_LIFETIME` trong Partner Center, gắn với ứng dụng, rồi kiểm thử luồng mua trên tài khoản thật (`docs/PHASE3_QA.md`).
7. Tải `.msix`, ảnh, mô tả và Notes for Certification lên Partner Center.
8. Dán nội dung `docs/RELEASE_NOTES_v1.4.0.md` vào mục *What's new* (en-US và vi-VN).
9. Chụp lại ảnh Store với giao diện mới (sáng và tối) — ảnh hiện có trong `docs/store-assets` là của giao diện cũ. Bộ ảnh giao diện v1.4 (sáng/tối × 4 ngôn ngữ × 8 màn hình) sinh bằng `packaging/capture-ui-matrix.ps1` nằm ở `docs/ui-v1.4`; chọn từ đó và cắt về 1600×900.
10. Cài gói đã ký và kiểm tra `StartupTask` (bật/tắt trong Cài đặt BFR và trong Task Manager) — chỉ kiểm chứng được với gói MSIX thật.
