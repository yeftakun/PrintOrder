Konteks:
Project ini adalah aplikasi desktop .NET 8 WinForms. Fokus perubahan ada pada file `PrintOrder/JobListForm.cs`.

Kondisi saat ini:
- `JobListForm` menampilkan daftar job cetak dalam `_jobRowsPanel`.
- Setiap row dibuat dengan `JobRowControl`.
- Row saat ini memiliki aksi individual:
  - Cetak/Retry
  - Tolak
  - Klik row membuka detail.
- Aksi individual sudah memakai:
  - `HandlePrintActionAsync(PrintJob job)`
  - `HandleRejectActionAsync(PrintJob job)`
  - `FetchJobAsync(string jobId)`
  - `SetJobActionsBusy(bool busy)`
- Daftar job sudah memiliki mekanisme anti-flicker:
  - `_jobRowsPanel` memakai `BufferedFlowLayoutPanel`
  - `LoadJobsAsync()` skip render jika data job tidak berubah.
Jangan merusak mekanisme anti-flicker ini.

Tujuan:
Tambahkan bulk action pada daftar job:
1. Cetak beberapa job sekaligus.
2. Tolak beberapa job sekaligus.

Batasan:
- Jangan rewrite besar.
- Jangan mengubah desain visual secara ekstrem.
- Jangan mengubah API server.
- Jangan menghapus aksi individual yang sudah ada.
- Jangan menghapus polling/realtime refresh.
- Jangan membuat bulk action berjalan paralel. Proses bulk harus sequential/berurutan agar aman untuk printer.
- Refresh daftar cukup dilakukan sekali setelah bulk action selesai, bukan setelah setiap job.

Perubahan yang diminta:

1. Tambahkan state seleksi di `JobListForm`
   - Tambahkan `HashSet<string> _selectedJobIds` dengan comparer `StringComparer.OrdinalIgnoreCase`.
   - Tambahkan helper untuk:
     - memilih job
     - membatalkan pilihan job
     - clear selection
     - prune selected id yang sudah tidak ada di `_allJobs`
     - menghitung selected jobs yang masih visible/actionable.

2. Tambahkan UI bulk action
   - Tambahkan checkbox/select-all untuk memilih semua job yang sedang terlihat di filter/search saat ini.
   - Tambahkan label ringkas seperti:
     - "0 tugas dipilih"
     - "3 tugas dipilih"
   - Tambahkan tombol:
     - "Cetak Terpilih"
     - "Tolak Terpilih"
   - Tombol bulk disabled jika tidak ada job terpilih yang eligible.
   - Jangan membuat toolbar terlalu padat. Jika toolbar sekarang penuh, buat panel bulk action kecil di bawah toolbar atau di atas table card.
   - Pertahankan warna dan style yang konsisten dengan `JobCommandButton`, `UiTheme`, dan komponen yang sudah ada.

3. Tambahkan checkbox per row
   - Ubah `JobRowControl` agar menerima status selected, misalnya lewat parameter constructor:
     `JobRowControl(PrintJob job, bool selected)`
   - Tambahkan event:
     `public event EventHandler<bool>? SelectionChanged;`
     atau event lain yang sejenis.
   - Checkbox harus menampilkan state selected berdasarkan `_selectedJobIds`.
   - Klik checkbox tidak boleh membuka detail.
   - Update `WireDetailOpen()` agar tidak memasang handler buka detail pada `CheckBox` atau control selection lain.
   - Pastikan tombol Cetak/Tolak individual tetap berfungsi seperti sebelumnya.

4. Eligibility bulk action
   - Bulk Cetak hanya berlaku untuk job dengan status:
     - ready
     - pending
   - Bulk Tolak hanya berlaku untuk job dengan status:
     - ready
   - Jika user memilih job yang tidak eligible, job tersebut harus di-skip, bukan menyebabkan semua proses gagal.
   - Status label harus memberi ringkasan hasil, misalnya:
     - "Bulk cetak selesai: 3 berhasil, 1 dilewati, 0 gagal."
     - "Bulk tolak selesai: 2 berhasil, 1 dilewati."

5. Confirmation dialog
   - Sebelum bulk Cetak, tampilkan konfirmasi:
     "Cetak 3 tugas terpilih?"
   - Sebelum bulk Tolak, tampilkan konfirmasi:
     "Tolak 3 tugas terpilih? Tindakan ini akan membatalkan tugas tersebut."
   - Jika user memilih Cancel/No, jangan lakukan aksi.

6. Implement method khusus bulk, jangan memanggil method individual secara langsung dalam loop
   Buat method seperti:
   - `HandleBulkPrintActionAsync()`
   - `HandleBulkRejectActionAsync()`

   Alur bulk print:
   - Ambil daftar selected jobs dari `_allJobs`.
   - Filter hanya status ready/pending.
   - Konfirmasi ke user.
   - Set busy state.
   - Untuk setiap job:
     - Fetch ulang status terbaru dengan `FetchJobAsync(job.Id)`.
     - Jika tidak ditemukan, skip.
     - Jika status terbaru bukan ready/pending, skip.
     - Update `_statusLabel` dengan progress, contoh:
       "Mencetak 2 dari 5: nama-file.pdf..."
     - Jalankan `_printJobAsync(latestJob)`.
     - Catat sukses/gagal/skip.
   - Setelah selesai:
     - clear selection untuk job yang berhasil diproses atau clear semua selection.
     - `await LoadJobsAsync()` sekali.
     - tampilkan summary hasil di `_statusLabel`.

   Alur bulk reject:
   - Ambil daftar selected jobs dari `_allJobs`.
   - Filter hanya status ready.
   - Konfirmasi ke user.
   - Set busy state.
   - Untuk setiap job:
     - Fetch ulang status terbaru dengan `FetchJobAsync(job.Id)`.
     - Jika tidak ditemukan, skip.
     - Jika status terbaru bukan ready, skip.
     - Update `_statusLabel` dengan progress.
     - Jalankan `_rejectJobAsync(latestJob)`.
     - Catat sukses/gagal/skip.
   - Setelah selesai:
     - clear selection untuk job yang berhasil diproses atau clear semua selection.
     - `await LoadJobsAsync()` sekali.
     - tampilkan summary hasil di `_statusLabel`.

7. Busy state
   - Saat bulk action berjalan:
     - disable tombol individual row.
     - disable checkbox row.
     - disable select-all.
     - disable tombol bulk.
     - disable refresh/filter/search jika perlu agar state tidak berubah di tengah proses.
   - Gunakan atau perluas `SetJobActionsBusy(bool busy)` agar mencakup bulk controls dan selection controls.
   - Jangan biarkan user menjalankan bulk print dan bulk reject bersamaan.

8. Selection harus tetap konsisten setelah render
   - Karena `RenderJobRows()` masih membuat ulang row saat data berubah, pastikan state checkbox diisi ulang dari `_selectedJobIds`.
   - Setelah `LoadJobsAsync()` menerima data baru, hapus selected id yang job-nya sudah tidak ada.
   - Jika job berubah status menjadi tidak eligible, jangan otomatis error; cukup update tombol bulk berdasarkan eligibility terbaru.

9. Search/filter interaction
   - Select-all hanya memilih job yang sedang visible berdasarkan hasil `BuildFilteredJobs()`.
   - Jika user search/filter lalu memilih semua, hanya job visible yang dipilih.
   - Job yang sudah dipilih tetapi tidak visible karena filter/search boleh tetap tersimpan, tetapi label harus jelas menghitung total selected.
   - Tombol bulk bekerja pada semua selected job yang masih ada di `_allJobs`, bukan hanya visible, kecuali implementasi lebih sederhana memilih hanya visible. Jika memilih pendekatan visible-only, jelaskan di komentar kode dan status label.

10. Detail page
   - Bulk action hanya ada di list page.
   - Detail page tidak perlu diubah kecuali ada efek samping dari perubahan `JobRowControl`.
   - Aksi individual di detail page tetap seperti sekarang.

11. Minimal quality check
   Pastikan setelah perubahan:
   - aplikasi build
   - row masih bisa dibuka detail saat area row diklik
   - checkbox tidak membuka detail
   - tombol Cetak individual tetap jalan
   - tombol Tolak individual tetap jalan
   - bulk Cetak memproses job sequential
   - bulk Tolak memproses job sequential
   - polling tidak membuat selection hilang jika data sama
   - selection dipulihkan ketika row dirender ulang
   - empty state tetap benar
   - metric cards tetap benar

Output:
Lakukan perubahan kode yang diperlukan saja. Setelah selesai, jelaskan ringkas:
- file yang diubah
- komponen/method yang ditambahkan
- alur bulk Cetak
- alur bulk Tolak
- catatan behavior selection dengan search/filter