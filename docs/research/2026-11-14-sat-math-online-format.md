# Cách các nền tảng SAT online xử lý đề Toán (điền đáp án, bảng, hình)

Ngày: 2026-11-14
Mục đích: tham chiếu cách làm của các nền tảng SAT đang chạy để chốt định dạng đề Toán cho LyraSAT.

## 1. Quy tắc điền đáp án chính thức (Bluebook)

Nguồn: bản sao đầy đủ hướng dẫn của Bluebook trên satpanda (nguyên văn hướng dẫn thi):
<https://exam.satpanda.com/exam/explanation.php?type=math&examid=mt9&section=1&no=7>

- Nếu có nhiều đáp án đúng, chỉ được nhập **một**.
- Đáp án dương tối đa **5 ký tự**; đáp án âm tối đa **6 ký tự** (dấu trừ tính một ký tự).
- Phân số không vừa ô thì nhập số thập phân tương đương.
- Số thập phân không vừa ô thì cắt hoặc làm tròn ở chữ số thập phân thứ tư.
- Hỗn số (3 1/2) **không được chấp nhận**: nhập 7/2 hoặc 3.5.
- Không nhập ký hiệu % , $ hay dấu phẩy phân cách nghìn.

Ví dụ nguyên văn trong hướng dẫn:

| Đáp án | Nhập được | Không được |
| --- | --- | --- |
| 3.5 | 3.5, 3.50, 7/2 | 3 1/2 |
| 2/3 | .6666, .6667, 0.666, 0.667 | .66, 0.67 |
| -1/3 | -.3333, -0.333 | -.33, -0.33 |

Cấu trúc đề (nguồn College Board): 44 câu Toán chia 2 module, khoảng 25% là câu điền đáp án (student-produced response), có Desmos và bảng công thức ngay trong app.
<https://satsuite.collegeboard.org/sat/whats-on-the-test/math>

## 2. Cách các nền tảng biểu diễn công thức, bảng, hình

### 2.1 satpanda (bản mô phỏng Bluebook, xem được không cần đăng nhập)

- Công thức viết bằng **TeX/MathML**, render bằng **MathJax 3** (`tex-mml-chtml.js`) + jqMath.
- Ô điền đáp án là **input text thường**, không giới hạn ký tự ở HTML; kiểm tra hợp lệ làm ở JavaScript.
- Hình minh hoạ (đồ thị, scatterplot) là **ảnh** chèn trong đề, ví dụ `https://img.satpanda.com/digital/paper/013.jpg`.
- Bảng trong trang là bảng HTML (bảng ví dụ của hướng dẫn điền đáp án).
- Đề 22 câu mẫu: 6 câu điền đáp án, các câu còn lại trắc nghiệm A-D.

### 2.2 OpenSAT (open-source, có JSON công khai)

Nguồn: <https://github.com/Anas099X/OpenSAT>, API: <https://pinesat.duckdns.org/api/questions?section=MATH&limit=5>

Mỗi câu là một object:

```json
{
  "id": "281a4f3b",
  "domain": "Advanced Math",
  "visuals": { "type": "null", "svg_content": "null" },
  "question": {
    "paragraph": "null",
    "question": "The graph of ... $f(x)=x^2+2x+3$ ...",
    "choices": { "A": "$\\frac{1}{4}$", "B": "$\\frac{1}{2}$", "C": "1", "D": "4" },
    "correct_answer": "A",
    "explanation": "..."
  },
  "difficulty": "Hard"
}
```

- Công thức nằm ngay trong chuỗi, dạng LaTeX `$...$` hoặc `\(...\)`.
- Mỗi câu có **tối đa một** khối `visuals` (SVG) cho hình/bảng; **không có** hình riêng cho từng đáp án.
- Khảo sát 1.031 câu Toán: 117 câu dùng LaTeX trong đáp án, 0 câu dùng HTML trong đáp án, 0 câu có `visuals` khác null (bộ dữ liệu mẫu chủ yếu là chữ).

### 2.3 Aniko (ngân hàng 2.000 câu Toán miễn phí)

- Trang hướng dẫn điền đáp án: <https://www.aniko.ai/help/grid-in-answers>
- Có Desmos hướng dẫn theo từng dạng; ngân hàng câu hỏi công khai: <https://www.aniko.ai/free-resources/sat/question-bank/math>
- Câu hỏi và đáp án hiển thị dạng công thức toán (không phải ảnh chụp đề).

### 2.4 OpenSAT.vn (thị trường Việt Nam)

- Trang chủ: <https://www.opensat.vn/>
- Có khoá SAT Go Math, đề 2026, 20.000+ câu, nhắc Desmos trong bundle JS.
- Nội dung luyện đề nằm sau đăng nhập, không xem được cấu trúc câu hỏi từ ngoài.

## 3. Kết luận cho LyraSAT

1. **Đề Toán không cần dòng "Question:" riêng**: đề bài (có công thức, bảng, hình) chính là câu hỏi. Đã sửa parser theo hướng này (BE `0eff323`).
2. **Công thức**: các nền tảng khác dùng LaTeX; giáo viên của mình soạn Word nên cách thực tế là giữ nguyên chữ và xuống dòng, công thức phức tạp nên là **ảnh** hoặc phương trình Word chèn ảnh, không dựa vào Equation object.
3. **Bảng trong đề**: đã hỗ trợ (bảng Word → HTML).
4. **Bảng/hình trong đáp án**: chưa hỗ trợ trọn vẹn. Trong các nguồn đã xem, đáp án là chữ/LaTeX hoặc ảnh; không nguồn nào cho mỗi đáp án một bảng HTML riêng. Đề xuất cho LyraSAT:
   - Mỗi đáp án là bảng/hình: đặt **một ảnh chụp** cho mỗi đáp án, hoặc mỗi bảng là một đoạn riêng ngay dưới nhãn `A.` / `B.` (không xếp 2x2).
   - Nếu cần xếp 2x2 như Bluebook thì phải chuyển thành ảnh.
5. **Ô điền đáp án**: LyraSAT đang chấm theo **giá trị số** (3/2 = 1.5 = 1,5), rộng rãi hơn luật chính thức. Có thể thêm giới hạn 5/6 ký tự và chặn ký tự lạ nếu muốn giống Bluebook, nhưng không bắt buộc cho mục tiêu dạy học.
