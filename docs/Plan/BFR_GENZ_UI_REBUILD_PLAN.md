# BFR — Plan rebuild UI/UX theo phong cách hiện đại, hướng Gen Z

> Phạm vi: **chỉ lớp giao diện** (`BFR.App`, và một phần `BFR.Presentation`). `BFR.Core` và toàn bộ test logic giữ nguyên.
> Nối tiếp bản rebuild v1.4 (`BFR_UIUX_REBUILD_PLAN.md`, Fluent mặc định) và bộ công cụ v2.0. Bản này đổi **gu thẩm mỹ và trải nghiệm**, không đổi kiến trúc.

---

## 1. Chẩn đoán: vì sao app trông như "dev năm 2015"

Soi từ ảnh chụp app thật (dark mode, trang Công cụ, Watch, Rename, hộp thoại Pro):

| # | Triệu chứng | Nguyên nhân gốc |
|---|---|---|
| 1 | Mọi thứ là **hộp xám phẳng** xếp chồng, không có điểm nhấn | Chỉ có một cấp bề mặt (`CardBackground` #2D2D2D) trên nền #202020; không có chiều sâu, không có nhóm thị giác |
| 2 | Chữ **13 px đồng đều**, tiêu đề chỉ lớn hơn một chút | Không có thang chữ; không có chữ "to và đậm" tạo nhịp |
| 3 | Chỉ **một màu nhấn xanh cyan**, dùng cho cả chọn lọc, nút chính, icon | Màu không mang cảm xúc, không phân biệt được "chính" với "đang chọn" |
| 4 | Công cụ nào cũng là **form dọc** (nhãn trái, ô phải) | Khung công cụ dựng chung từ danh sách tuỳ chọn nên mọi màn trông như phiếu khai báo |
| 5 | Kết quả là **danh sách chữ** hai cột | Không có ảnh thu nhỏ, không có trước/sau trực quan, không có "khoảnh khắc thành công" |
| 6 | Ô chọn, nút, checkbox là **control mặc định của Windows** | Chưa có bộ control riêng; bo góc và padding "vừa đủ" |
| 7 | **Không có chuyển động** (chuyển trang là bật/tắt) | Không có thang thời gian, không có phản hồi khi rê/bấm |
| 8 | Màn trống chỉ là **một dòng chữ** | Không có minh hoạ, không có "bước tiếp theo" |
| 9 | Thanh bên chứa 7 mục chức năng ngang hàng, **không có trang chủ** | Người mới không biết bắt đầu từ đâu; công cụ nằm sau một mục "Công cụ" |
| 10 | Giọng chữ **trung tính kiểu phần mềm văn phòng** | Không có cá tính; thông báo thành công là một câu cụt |

**Kết luận:** vấn đề không nằm ở một màu hay một icon, mà ở thiếu **hệ thống**: bề mặt, chữ, màu, chuyển động, giọng điệu. Sửa lẻ từng màn sẽ lại ra "2015". Cần dựng hệ thống trước, màn hình sau.

---

## 2. Định hướng thiết kế (đề xuất để bạn chốt)

### 2.1 Ba hướng thẩm mỹ để chọn

| | **A. Neo-glass, tối là chính** (đề xuất) | **B. Sticker / pastel vui** | **C. Bento mono, tối giản đậm** |
|---|---|---|---|
| Cảm giác | Kính mờ, gradient sáng trên nền tối, như app Arc/Raycast/Linear | Màu kẹo, bo rất tròn, nhãn dán, hơi "đồ chơi" | Lưới ô vuông chữ to, đen trắng + một màu chói |
| Hợp với | Công cụ tiện ích, người dùng trẻ làm việc nhiều giờ | Người dùng phổ thông, nhóm sáng tạo | Người dùng kỹ thuật, thích gọn |
| Rủi ro | Hiệu năng làm mờ; tương phản chữ trên kính | Dễ bị coi là "trẻ con", khó giữ nghiêm túc khi xoá/hoàn tác | Dễ lạnh, kém thân thiện với người mới |
| Công sức | Vừa | Cao (cần bộ minh hoạ) | Thấp |

**Đề xuất: A kết hợp bố cục Bento của C** (thẻ chữ nhật bo lớn kích cỡ khác nhau, kiểu trang chủ iOS/Apple). Lý do: hợp với app có hành động "nguy hiểm" (di chuyển, cách ly) mà vẫn trẻ trung; làm tối trước, sáng sau.

### 2.2 Nguyên tắc thiết kế (dùng để duyệt mọi màn hình)

1. **Một việc chính mỗi màn.** Mỗi màn có đúng một nút chính, to và có màu.
2. **Thấy trước khi làm.** Mọi thay đổi hiện thành hình (trước → sau) trước khi áp dụng.
3. **Chuyển động có nghĩa.** Chuyển động chỉ để chỉ hướng/phản hồi, tối đa 250 ms, tắt được.
4. **Sai thì sửa được, và app nói rõ điều đó.** Chữ "Hoàn tác" luôn ở trong tầm mắt sau mọi thao tác ghi.
5. **Có cá tính nhưng không đùa trên việc nghiêm túc.** Hộp xác nhận, lỗi, xoá dùng giọng rõ ràng; chỗ vui chỉ ở trạng thái trống và thành công.
6. **Đẹp ở cả 4 ngôn ngữ.** Tiếng Pháp dài hơn 30%, tiếng Trung không có khoảng trắng: bố cục không được vỡ.

---

## 3. Hệ thống thiết kế (design tokens)

### 3.1 Bề mặt và màu (dark-first)

| Token | Dark | Light | Dùng cho |
|---|---|---|---|
| `bg.base` | #0E0F13 | #F6F6F9 | Nền cửa sổ |
| `bg.glow` | 2 đốm gradient mờ (tím #7C5CFF, hồng #FF5C9E) ở góc, opacity 18% | tương tự, opacity 10% | Chiều sâu phía sau, tĩnh (không animate để tiết kiệm GPU) |
| `surface.1` | #16181F | #FFFFFF | Thẻ chính |
| `surface.2` | #1D2029 | #F0F0F6 | Thẻ lồng, ô nhập |
| `surface.glass` | #FFFFFF 6% + viền #FFFFFF 10% | #FFFFFF 70% + viền #000 6% | Thanh bên, menu tròn, hộp thoại |
| `accent.gradient` | #7C5CFF → #FF5C9E (góc 135°) | giữ nguyên | Nút chính, tiến trình, ô đang chọn |
| `accent.solid` | #8B6CFF | #6C4DFF | Liên kết, focus ring |
| `success` / `warn` / `danger` | #3DDC97 / #FFB74D / #FF6B6B | đậm hơn 15% | Trạng thái, luôn kèm icon + chữ (không chỉ màu) |

Quy tắc: **chữ trên gradient luôn trắng đậm, tương phản ≥ 4.5:1** (kiểm bằng script, mục 9). Người dùng chọn 3 bảng màu nhấn: *Aurora* (tím-hồng, mặc định), *Mint* (xanh ngọc-lam), *Sunset* (cam-đỏ). Chọn trong Cài đặt, đổi tức thì.

### 3.2 Chữ

| Cấp | Cỡ / đậm | Dùng cho |
|---|---|---|
| Display | 32 / 700 | Tiêu đề trang chủ, màn thành công |
| Title | 22 / 650 | Tiêu đề trang |
| Heading | 16 / 600 | Tiêu đề thẻ |
| Body | 14 / 450 | Nội dung (tăng từ 13) |
| Caption | 12 / 450 | Phụ chú, nhãn trạng thái |
| Mono | 12 / Cascadia | Đường dẫn, mẫu token |

Phông: **Inter Variable** cho thân chữ (OFL, hỗ trợ tiếng Việt, Latin mở rộng), tiêu đề dùng **Plus Jakarta Sans** (OFL) cho nét tròn trẻ trung. Bắt buộc kiểm đủ glyph cho vi/fr; chữ Trung dự phòng **Microsoft YaHei UI**. Nhúng cả hai phông vào gói (≈ 1,2 MB).

### 3.2.1 Hình khối và khoảng cách

- Bo góc: thẻ **20**, ô nhập/nút **12**, chip **999**. (Hiện 6–8.)
- Lưới 4 px; khoảng cách giữa thẻ **16**, lề trang **32**, padding thẻ **20**.
- Bóng: 2 mức (`elev.1`: blur 16 opacity 25%; `elev.2`: blur 32 opacity 35%), chỉ dùng cho thẻ nổi và menu.
- Viền: 1 px `#FFFFFF 8%` thay cho bóng ở thẻ thường (nhẹ hơn, rẻ hơn).

### 3.3 Chuyển động

| Loại | Thời lượng / đường cong | Ví dụ |
|---|---|---|
| Phản hồi | 90 ms, ease-out | Rê chuột nổi thẻ lên 2 px, nút lún khi bấm |
| Chuyển cảnh | 220 ms, cubic-bezier(.2,.8,.2,1) | Chuyển trang: nội dung trượt 12 px + mờ dần |
| Xuất hiện danh sách | 30 ms trễ tăng dần, tối đa 8 dòng đầu | Kết quả xem trước "đổ" vào |
| Thành công | 600 ms một lần | Dấu tích vẽ nét + pháo giấy nhẹ ở màn hoàn thành |
| Tôn trọng hệ thống | Tắt hết khi Windows bật "Hiệu ứng hoạt hình = Tắt" hoặc chế độ tiết kiệm pin | |

Không animate bên trong danh sách ảo hoá từ dòng thứ 9 trở đi (giữ cuộn 100.000 dòng mượt).

---

## 4. Bộ thành phần (component library)

Dựng một lần trong `Themes/` + `Controls/`, mọi màn dùng chung. Mỗi thành phần có đủ trạng thái: mặc định, rê, bấm, focus, vô hiệu, lỗi, và chế độ High Contrast.

| Thành phần | Ghi chú |
|---|---|
| `BfrButton` (Primary gradient / Secondary kính / Ghost / Danger) | Một nút Primary mỗi màn; Danger luôn kèm xác nhận |
| `BfrCard` (+ biến thể `Interactive`, `Hero`) | Thẻ Bento; `Hero` có gradient nền |
| `BfrChip`, `BfrSegmented` | Thay ComboBox cho lựa chọn ≤ 4 giá trị (hiện toàn là ComboBox) |
| `BfrToggle` | Công tắc kiểu iOS thay CheckBox |
| `BfrTextField` | Nhãn nổi, viền sáng khi focus, nút xoá nhanh |
| `BfrDropZone` | Viền nét đứt gradient chạy nhẹ khi kéo tệp tới; dùng chung cho Rename và Công cụ |
| `BfrStepper` | Thanh bước *Chọn tệp → Chọn kiểu → Xem trước → Xong* |
| `BfrPreviewRow` | Dòng "tên cũ → tên mới" có diff màu, ảnh thu nhỏ, chip trạng thái |
| `BfrToast` + `BfrUndoBar` | Thanh hoàn tác nổi đáy cửa sổ 10 giây sau mỗi thao tác ghi |
| `BfrEmptyState` | Minh hoạ vẽ bằng vector (XAML Path) + một câu + nút hành động |
| `BfrSkeleton` | Khung xám chạy sáng khi đang quét/ băm tệp |
| `BfrCommandBar` / `BfrRail` | Thanh bên kính, thanh lệnh đỉnh trang |

Công nghệ: **giữ WPF + WPF-UI 4.x**, nhưng viết lại `ControlTemplate` của các control trên bằng style riêng (không dùng nguyên bản WPF-UI vì chính nó cho ra giao diện Fluent mặc định). Backdrop: Mica/Acrylic qua `DwmSetWindowAttribute` (Windows 11), nền đặc có gradient trên Windows 10.

---

## 5. Thiết kế lại từng màn hình

### 5.1 Khung ứng dụng (shell)

- **Thanh bên kính, 72 px, chỉ icon** (tên hiện khi rê), nhóm thành: *Trang chủ · Đổi tên · Công cụ · Lịch sử* trên; *Theo dõi · Cài đặt* dưới. Gộp Shelf, Presets vào trang chủ/đổi tên (đang là mục riêng, ít dùng).
- Chuyển trang có chuyển cảnh 220 ms; tiêu đề trang to ở góc trái; nút phụ (Pro, ngôn ngữ, theme) gom vào menu avatar góc phải, **không còn lẫn trong thanh thao tác**.
- Thanh hoàn tác nổi ở đáy, hiện sau mọi thao tác ghi.

### 5.2 Trang chủ mới (hiện chưa có)

Bố cục Bento:
- **Thẻ lớn "Thả tệp vào đây"** (Hero, vùng thả chiếm 2/3): thả xong hiện ngay *"Bạn muốn làm gì với 38 tệp này?"* với 4 thẻ gợi ý theo nội dung (toàn ảnh → Đổi tên theo ngày chụp, Xoá GPS, Thu nhỏ; toàn nhạc → Đổi tên theo tag, Xếp theo nghệ sĩ…).
- **Lưới thẻ công cụ** (9 công cụ, mỗi thẻ icon lớn + 1 dòng mô tả + nhãn *Pro* nếu cần).
- **Thẻ "Gần đây"**: 3 lần làm gần nhất kèm nút Hoàn tác.
- **Thẻ "Mẹo"**: xoay vòng, có thể tắt.
- Lần đầu mở: tour 4 bước (tối đa 30 giây, bỏ qua được).

### 5.3 Không gian Đổi tên (màn quan trọng nhất)

Thay bố cục "thanh công cụ 2 hàng + bảng" bằng **hai cột**:
- **Trái (360 px): bảng điều khiển quy tắc dạng thẻ**, mỗi quy tắc là một thẻ chứa tham số ngay tại chỗ (không còn mở hộp thoại), kéo để đổi thứ tự, có xem trước nhanh "tên mẫu".
- **Phải: danh sách xem trước dạng `BfrPreviewRow`**: ảnh thu nhỏ (cho tệp ảnh/video), tên cũ mờ, tên mới có phần thay đổi **tô sáng**, chip trạng thái (Sẽ đổi / Trùng / Lỗi). Lọc bằng chip phía trên danh sách (bấm được).
- **Nút "Áp dụng 128 tệp"** to, gradient, cố định ở góc dưới phải; bấm ra hộp xác nhận kính có **tóm tắt rủi ro** (bao nhiêu tệp, bao nhiêu xung đột) rồi màn **thành công** (dấu tích + "Xong 128 tệp trong 1,4 giây" + nút Hoàn tác).

### 5.4 Trang Công cụ

Đây là chỗ "như phiếu khai báo" nặng nhất, đổi hẳn:
- **Luồng 4 bước** bằng `BfrStepper`: Chọn tệp → Chọn kiểu → Xem trước → Xong. Mỗi bước một màn gọn, không dồn mọi thứ vào một trang.
- **Bước "Chọn kiểu"** dùng **thẻ lựa chọn lớn có hình minh hoạ** thay cho ComboBox (ví dụ Sắp xếp: thẻ "Theo loại", "Theo ngày", "Theo nghệ sĩ" mỗi thẻ vẽ sơ đồ cây thư mục kết quả). Tuỳ chọn nâng cao nằm trong phần *Nâng cao* thu gọn.
- **Bước "Xem trước"** theo từng công cụ: Sắp xếp = cây thư mục kết quả; Tệp trùng = nhóm ảnh cạnh nhau, bấm để chọn bản giữ; Ảnh = trước/sau kéo thanh trượt; Phân tích = **biểu đồ khối (treemap)** thay danh sách; Checksum = lưới trạng thái xanh/đỏ.
- **Màn "Xong"** dùng chung: số liệu, nút Hoàn tác, nút "Làm tiếp với kết quả này" (đưa sang công cụ khác).
- Giữ **Menu tròn** nhưng làm lại theo token mới (kính, gradient nhấn, icon nét dày 1,5).

### 5.5 Theo dõi thư mục

Danh sách quy tắc thành các **thẻ có công tắc bật/tắt**, mỗi thẻ hiện thư mục, quy trình (dạng chip bước), và **sparkline số tệp đã xử lý 7 ngày**. Nhật ký là ngăn kéo phải thay vì khung dưới.

### 5.6 Lịch sử và Khu cách ly

Gộp một trang **"Lịch sử"** dạng dòng thời gian theo ngày: mỗi lần làm là một thẻ (biểu tượng công cụ, mô tả một dòng, số tệp, nút Hoàn tác). Khu cách ly là tab thứ hai, hiện **lưới ảnh/tệp có thể khôi phục từng cái**, đếm ngược "còn 12 ngày".

### 5.7 Cài đặt, nâng cấp Pro, hộp thoại

- Cài đặt chia thẻ theo chủ đề với công tắc; có **xem trước bảng màu** ngay khi chọn.
- Hộp thoại nâng cấp: bỏ bảng 17 dòng; thay bằng **3 thẻ lợi ích** (Không giới hạn, Tự động hoá, Ảnh & tệp trùng) + bảng so sánh thu gọn; giá lấy từ Store như hiện tại.
- Mọi hộp thoại là **lớp kính nổi trong cửa sổ** (không MessageBox hệ thống).

---

## 6. Giọng điệu và vi mô trải nghiệm

- Bộ **microcopy** có hai tầng: *Chuẩn* (mặc định, rõ ràng) và *Vui* (tuỳ chọn trong Cài đặt) cho riêng trạng thái trống/thành công. Ví dụ *Vui* tiếng Việt: "Kéo thả tệp vào đây nè", "Xong rồi! 128 tệp đã vào nếp". Hộp xác nhận, lỗi, xoá **không** có bản *Vui*.
- Phản hồi: nút lún, thẻ nổi, danh sách đổ vào, dấu tích vẽ nét, pháo giấy nhẹ (chỉ khi xong ≥ 10 tệp, tắt được).
- **Phím tắt hiển thị** trên tooltip; thanh lệnh Ctrl+K giữ nguyên nhưng đổi sang giao diện kính.
- Không có âm thanh, không có mascot ở bản này (xem mục 11).

---

## 7. Trợ năng và hiệu năng (không đánh đổi cho "đẹp")

| Yêu cầu | Cách đảm bảo |
|---|---|
| Tương phản chữ ≥ 4.5:1 trên mọi nền kể cả gradient | Script đo tự động trên ảnh chụp từng màn (đã có kỹ thuật chụp cửa sổ bằng UI Automation) |
| **High Contrast** | App hiện **chưa hỗ trợ**; plan này bổ sung: khi bật, tắt gradient/kính/bóng, dùng màu hệ thống, viền 2 px |
| Narrator, bàn phím | Mọi control mới có `AutomationProperties.Name`; thứ tự Tab theo bố cục; mọi thao tác chuột đều có phím tương đương |
| Cỡ chữ hệ thống 150% | Bố cục co giãn, không cắt chữ; kiểm ở 100/125/150/200% |
| Giảm chuyển động | Đọc `SystemParameters.ClientAreaAnimation`; tắt toàn bộ hoạt hình |
| Hiệu năng | Mở app < 1,2 giây; chuyển trang < 220 ms; cuộn 100.000 dòng ≥ 55 fps; làm mờ chỉ ở thanh bên/hộp thoại, không ở danh sách |
| Máy yếu / Windows 10 | Nền đặc + gradient tĩnh thay cho Mica/Acrylic; không bóng động |

---

## 8. Lộ trình (khoảng 8–9 tuần, một người)

| Giai đoạn | Nội dung | Tuần | Cổng chấp nhận |
|---|---|---|---|
| **0. Chốt hướng** | Bạn chọn hướng A/B/C, bảng màu, phông, mức "vui". Dựng **prototype HTML tương tác 3 màn** (Trang chủ, Đổi tên, bước Xem trước Sắp xếp) để duyệt trước khi viết XAML | 1 | Bạn duyệt prototype |
| **1. Nền tảng** | Tokens màu/chữ/hình khối, bảng màu Aurora/Mint/Sunset, nhúng phông, backdrop, bộ component 1 (Button, Card, Chip, Segmented, Toggle, TextField) + trang mẫu "Gallery" để xem mọi trạng thái | 2 | Gallery đủ trạng thái ở dark/light/High Contrast |
| **2. Khung ứng dụng** | Thanh bên mới, chuyển cảnh, thanh hoàn tác, menu avatar, hộp thoại kính, **Trang chủ** | 1,5 | Chụp tự động qua UIA, không cắt chữ ở 4 ngôn ngữ |
| **3. Không gian Đổi tên** | Hai cột, thẻ quy tắc nội tuyến, `BfrPreviewRow`, màn thành công | 2 | Giữ nguyên toàn bộ test INV và hành vi áp dụng/hoàn tác |
| **4. Công cụ** | `BfrStepper` + thẻ lựa chọn có minh hoạ + xem trước riêng cho 9 công cụ + treemap + trước/sau ảnh; làm lại Theo dõi, Lịch sử/Khu cách ly | 2,5 | Mỗi công cụ chạy đủ 4 bước trên giao diện, có ảnh chụp |
| **5. Tinh chỉnh** | Chuyển động, tour lần đầu, microcopy 2 tầng × 4 ngôn ngữ, menu tròn mới | 1 | Duyệt toàn bộ bằng checklist mục 7 |
| **6. QA và phát hành** | Kiểm tự động tương phản, hiệu năng, DPI 100–200%, Narrator thủ công, bản beta nhỏ | 1 | Không còn lỗi chặn; số liệu hiệu năng đạt |

**Cắt giảm khi trễ:** pháo giấy và microcopy *Vui* → sparkline Theo dõi → treemap Phân tích → thẻ gợi ý thông minh ở Trang chủ → bảng màu Mint/Sunset. **Không cắt:** tokens, bộ component, High Contrast, Trang chủ, màn Đổi tên mới.

---

## 9. Cách kiểm chứng (để không "đẹp trong tưởng tượng")

1. **Gallery** là một trang trong app (ẩn sau cờ dev) liệt kê mọi component ở mọi trạng thái; mỗi thay đổi theme chụp lại.
2. **Bộ ảnh chuẩn**: script UI Automation chụp ~40 màn (4 ngôn ngữ × dark/light × các trạng thái chính). So sánh với ảnh đã duyệt; lệch quá ngưỡng là fail.
3. **Đo tương phản tự động** trên ảnh chụp; **đo thời gian** mở app và chuyển trang bằng PerfGate hiện có.
4. **Duyệt nhóm người dùng nhỏ** (5–8 người, ít nhất 3 người chưa từng dùng app) làm 3 việc: đổi tên 30 ảnh, tìm và xoá tệp trùng, nén thư mục. Đo thời gian hoàn thành và số lần bấm nhầm; so với bản hiện tại.

---

## 10. Rủi ro

| Rủi ro | Mức | Giảm thiểu |
|---|---|---|
| Làm lại control bằng WPF tốn thời gian hơn dự tính | Cao | Dựng Gallery ở Giai đoạn 1 và chỉ qua cổng khi đủ trạng thái; không tự viết control phức tạp (DataGrid, ComboBox) nếu style lại là đủ |
| Hiệu ứng kính/gradient làm chậm máy yếu | Trung bình | Chế độ "Hiệu năng" tự bật khi phát hiện GPU yếu hoặc pin; nền đặc |
| Chữ dài (tiếng Pháp) làm vỡ thẻ Bento | Trung bình | Thẻ co giãn chiều cao; kiểm 4 ngôn ngữ trong bộ ảnh chuẩn |
| "Vui" bị coi là thiếu nghiêm túc khi thao tác nguy hiểm | Trung bình | Tách tầng giọng điệu; xác nhận và lỗi luôn dùng giọng chuẩn |
| Hồi quy hành vi khi sửa lớp giao diện | Trung bình | Không đụng `BFR.Core`; giữ nguyên bộ test và thêm kiểm UIA cho luồng chính |
| Phông nhúng làm nặng gói / vướng giấy phép | Thấp | Chỉ Inter + Plus Jakarta Sans (OFL), tập con glyph vi/fr/en |
| Mất tính nhất quán nếu làm dở dang | Cao | Giai đoạn 2–4 chỉ chuyển trang sang style mới khi **cả trang** xong; không trộn hai kiểu trong một màn |

---

## 11. Câu hỏi cần bạn chốt trước khi bắt đầu

| # | Câu hỏi | Đề xuất |
|---|---|---|
| D1 | Hướng thẩm mỹ A / B / C? | **A + bố cục Bento** |
| D2 | Màu nhấn mặc định? | **Aurora (tím-hồng)**, cho chọn thêm Mint, Sunset |
| D3 | Tối hay sáng là mặc định? | **Theo hệ thống**, thiết kế tối trước |
| D4 | Có microcopy giọng "Vui" không? | **Có, mặc định tắt**, chỉ ở trạng thái trống/thành công |
| D5 | Có mascot/nhân vật không? | **Chưa** (tốn công minh hoạ; cân nhắc sau v2.1) |
| D6 | Bao nhiêu người dùng thử ở Giai đoạn 6? | **5–8 người** |
| D7 | Giữ tên mục "Công cụ" hay đổi tên theo gu mới ("Studio"…)? | **Giữ "Công cụ"** cho rõ nghĩa; thương hiệu chỉ đổi ở màn chào |
| D8 | Phát hành cùng v2.0 hay thành v2.1? | **v2.1** (v2.0 đã rộng về tính năng; UI rebuild cần kiểm riêng) |

---

## 12. Sản phẩm bàn giao

1. Prototype HTML tương tác (Giai đoạn 0) và bản duyệt.
2. `Themes/` mới: tokens, 3 bảng màu, phông nhúng; `Controls/` với 12 thành phần ở mục 4.
3. Trang Gallery (cờ dev).
4. Toàn bộ màn hình ở mục 5, 4 ngôn ngữ, dark/light/High Contrast.
5. Bộ ảnh chuẩn + script chụp UIA + báo cáo tương phản/hiệu năng.
6. Cập nhật tài liệu: ghi chú phát hành, ảnh Store, hướng dẫn kiểm thử thủ công.
